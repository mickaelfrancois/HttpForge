using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Integration;

public class RequestSaveServiceTests : IAsyncLifetime
{
    private IDbContextFactory<AppDbContext> _factory = null!;
    private RequestChangeNotifier _notifier = null!;
    private int _requestId;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        _notifier = new RequestChangeNotifier();

        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        var collection = new Collection { Name = "Test" };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        var request = new HttpRequestItem
        {
            Name = "Test Request",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            CollectionId = collection.Id,
            UpdatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync();
        _requestId = request.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RequestDraft MakeDraft(DateTime loadedAt) => new()
    {
        RequestId = _requestId,
        LoadedAt = loadedAt,
        Name = "Updated Name",
        Method = HttpMethodKind.POST,
        Url = "https://updated.com",
        BodyKind = BodyKind.None
    };

    [Fact]
    public async Task SaveAsync_NoConflict_UpdatesDb()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        // LoadedAt after UpdatedAt → no conflict
        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));
        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);

        Assert.False(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var saved = await db.Requests.FirstAsync(r => r.Id == _requestId);
        Assert.Equal("Updated Name", saved.Name);
        Assert.Equal(HttpMethodKind.POST, saved.Method);
        Assert.True(saved.UpdatedAt > new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SaveAsync_NoConflict_ReturnsSavedAt()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));

        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);

        Assert.False(result.IsConflict);
        Assert.NotNull(result.SavedAt);
    }

    [Fact]
    public async Task SaveAsync_SecondConsecutiveSave_SameOrigin_NoConflict()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));

        var first = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);
        Assert.False(first.IsConflict);

        // The UI rebases the draft onto the version it just wrote. Without SavedAt
        // being returned and applied here, the second save would falsely conflict
        // because the DB's UpdatedAt now sits after the draft's original LoadedAt.
        draft.LoadedAt = first.SavedAt!.Value;

        var second = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);
        Assert.False(second.IsConflict);
    }

    [Fact]
    public async Task SaveAsync_Conflict_DbNotModified()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        // LoadedAt before DB UpdatedAt → conflict
        var draft = MakeDraft(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));
        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);

        Assert.True(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var unchanged = await db.Requests.FirstAsync(r => r.Id == _requestId);
        Assert.Equal("Test Request", unchanged.Name);
    }

    [Fact]
    public async Task SaveAsync_ForceOverwrite_SavesDespiteConflict()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        var draft = MakeDraft(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc)); // conflict scenario

        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: true);

        Assert.False(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var saved = await db.Requests.FirstAsync(r => r.Id == _requestId);
        Assert.Equal("Updated Name", saved.Name);
    }

    [Fact]
    public async Task SaveAsync_NoConflict_FiresNotification()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        int notifiedId = 0;
        _notifier.RequestSaved += (id, origin) => { notifiedId = id; return Task.CompletedTask; };

        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));
        await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);

        Assert.Equal(_requestId, notifiedId);
    }

    [Fact]
    public async Task SaveAsync_PersistsIgnoreTlsErrors()
    {
        var svc = new RequestSaveService(_factory, _notifier);
        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));
        draft.IgnoreTlsErrors = true;

        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);
        Assert.False(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var saved = await db.Requests.FirstAsync(r => r.Id == _requestId);
        Assert.True(saved.IgnoreTlsErrors);
    }

    [Fact]
    public async Task SaveAsync_ReplacesChildCollections()
    {
        // Seed the request with 2 headers
        await using var seedDb = await _factory.CreateDbContextAsync();
        seedDb.Add(new HeaderItem { HttpRequestItemId = _requestId, Key = "X-Old-1", Value = "v1", Enabled = true });
        seedDb.Add(new HeaderItem { HttpRequestItemId = _requestId, Key = "X-Old-2", Value = "v2", Enabled = true });
        await seedDb.SaveChangesAsync();

        // Save draft with only 1 new header
        var draft = new RequestDraft
        {
            RequestId = _requestId,
            LoadedAt = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc),
            Name = "Updated Name",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None,
            Headers = [new HeaderItem { Key = "X-New", Value = "new-val", Enabled = true }],
            QueryParams = [],
            FormFields = [],
            Variables = []
        };

        var svc = new RequestSaveService(_factory, _notifier);
        var result = await svc.SaveAsync(draft, "origin-1", forceOverwrite: false);

        Assert.False(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var headers = await db.Set<HeaderItem>().Where(h => h.HttpRequestItemId == _requestId).ToListAsync();
        Assert.Single(headers);
        Assert.Equal("X-New", headers[0].Key);
    }
}
