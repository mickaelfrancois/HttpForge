using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HttpForge.Tests.Integration;

public class RequestAutoSaverIntegrationTests : IAsyncLifetime
{
    private IDbContextFactory<AppDbContext> _factory = null!;
    private RequestChangeNotifier _notifier = null!;
    private RequestSaveService _save = null!;
    private int _requestA;
    private int _requestB;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        _notifier = new RequestChangeNotifier();
        _save = new RequestSaveService(_factory, _notifier);

        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        var collection = new Collection { Name = "Test" };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        _requestA = await SeedRequestAsync(db, collection.Id, "Request A");
        _requestB = await SeedRequestAsync(db, collection.Id, "Request B");
    }

    private static async Task<int> SeedRequestAsync(AppDbContext db, int collectionId, string name)
    {
        var req = new HttpRequestItem
        {
            Name = name,
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            CollectionId = collectionId,
            UpdatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        db.Requests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RequestDraft MakeDraft(int requestId, string name, DateTime loadedAt)
    {
        var draft = new RequestDraft
        {
            RequestId = requestId,
            LoadedAt = loadedAt,
            Name = name,
            Method = HttpMethodKind.POST,
            Url = "https://updated.com",
            BodyKind = BodyKind.None
        };
        draft.MarkDirty();
        return draft;
    }

    // Mirrors Home.razor's auto-save callback: save through RequestSaveService, then either
    // rebase + clear dirty on success, or suspend on conflict (never overwrite).
    private Func<CancellationToken, Task> AutoSaveCallback(RequestAutoSaver saver, RequestDraft draft) =>
        async _ =>
        {
            var result = await _save.SaveAsync(draft, "origin-auto", forceOverwrite: false);
            if (result.IsConflict)
            {
                saver.Suspend(draft.RequestId);
                return;
            }
            if (result.SavedAt is DateTime savedAt) draft.LoadedAt = savedAt;
            draft.ClearDirty();
        };

    [Fact]
    public async Task DebouncedEdit_AfterDelay_PersistsDraftAndClearsDirty()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var draft = MakeDraft(_requestA, "Auto Saved", new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));

        // Act
        saver.Schedule(_requestA, AutoSaveCallback(saver, draft));
        time.Advance(RequestAutoSaver.DefaultDelay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.False(draft.IsDirty);
        await using var db = await _factory.CreateDbContextAsync();
        var saved = await db.Requests.FirstAsync(r => r.Id == _requestA);
        Assert.Equal("Auto Saved", saved.Name);
        Assert.Equal(HttpMethodKind.POST, saved.Method);
    }

    [Fact]
    public async Task DebouncedEdit_PersistsScheduledTabOnly_NotAnotherTab()
    {
        // Arrange — schedule auto-save for A's draft only; B must stay untouched
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var draftA = MakeDraft(_requestA, "A edited", new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));

        // Act
        saver.Schedule(_requestA, AutoSaveCallback(saver, draftA));
        time.Advance(RequestAutoSaver.DefaultDelay);
        await saver.WhenIdleAsync();

        // Assert
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Requests.FirstAsync(r => r.Id == _requestA);
        var b = await db.Requests.FirstAsync(r => r.Id == _requestB);
        Assert.Equal("A edited", a.Name);
        Assert.Equal("Request B", b.Name);
    }

    [Fact]
    public async Task DebouncedEdit_OnConflict_SuspendsWithoutOverwriting()
    {
        // Arrange — LoadedAt before the DB UpdatedAt → SaveAsync reports a conflict
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var draft = MakeDraft(_requestA, "Should Not Win", new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));

        // Act
        saver.Schedule(_requestA, AutoSaveCallback(saver, draft));
        time.Advance(RequestAutoSaver.DefaultDelay);
        await saver.WhenIdleAsync();

        // Assert — auto-save suspended, DB unchanged, draft still dirty
        Assert.True(saver.IsSuspended(_requestA));
        Assert.True(draft.IsDirty);
        await using var db = await _factory.CreateDbContextAsync();
        var unchanged = await db.Requests.FirstAsync(r => r.Id == _requestA);
        Assert.Equal("Request A", unchanged.Name);
    }

    [Fact]
    public async Task DebouncedEdit_AfterConflictSuspends_DoesNotRetryOnFurtherTime()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var draft = MakeDraft(_requestA, "Should Not Win", new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));

        // Act — first fire conflicts and suspends; later edits are ignored until Resume
        saver.Schedule(_requestA, AutoSaveCallback(saver, draft));
        time.Advance(RequestAutoSaver.DefaultDelay);
        await saver.WhenIdleAsync();
        saver.Schedule(_requestA, AutoSaveCallback(saver, draft)); // no-op (suspended)
        time.Advance(TimeSpan.FromSeconds(10));
        await saver.WhenIdleAsync();

        // Assert — still no overwrite
        await using var db = await _factory.CreateDbContextAsync();
        var unchanged = await db.Requests.FirstAsync(r => r.Id == _requestA);
        Assert.Equal("Request A", unchanged.Name);
    }
}
