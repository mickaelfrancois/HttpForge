using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HttpForge.Tests.Services;

public class TabManagerServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;
    private readonly AppState _appState = new();

    public TabManagerServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var ctx = new AppDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private TabManagerService CreateSut()
    {
        var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
        factory.CreateDbContextAsync(default).Returns(_ => Task.FromResult(new AppDbContext(_opts)));
        return new TabManagerService(factory, _appState, new PermissionService(factory));
    }

    // Seeds the collection under a team with "user1" as Contributor so the
    // PermissionService checks in TabManagerService grant access.
    private async Task<int> SeedRequestAsync(string name = "GET /test")
    {
        await using var db = new AppDbContext(_opts);
        var col = await db.Collections.FirstOrDefaultAsync();
        if (col is null)
        {
            var team = new Team { Name = "Test Team" };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = "user1", Role = TeamRole.Contributor });
            col = new Collection { Name = "Test", TeamId = team.Id };
            db.Collections.Add(col);
            await db.SaveChangesAsync();
        }

        var req = new HttpRequestItem
        {
            Name = name,
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            CollectionId = col.Id
        };
        db.Requests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    [Fact]
    public async Task OpenTabAsync_NewRequest_AddsAndActivatesTab()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync("GET /users");

        await sut.OpenTabAsync(id, "user1");

        Assert.Single(sut.Tabs);
        Assert.Equal(id, sut.Tabs[0].RequestId);
        Assert.Equal("GET /users", sut.Tabs[0].Name);
        Assert.Equal(id, sut.ActiveTab!.RequestId);
        Assert.Equal(id, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task OpenTabAsync_AlreadyOpen_ActivatesExistingWithoutDuplicate()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync();

        await sut.OpenTabAsync(id, "user1");
        await sut.OpenTabAsync(id, "user1");

        Assert.Single(sut.Tabs);
        Assert.Equal(id, sut.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task CloseTab_CleanDraft_RemovesTabAndActivatesNeighbour()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1, "user1");
        await sut.OpenTabAsync(id2, "user1");

        sut.CloseTab(id2);

        Assert.Single(sut.Tabs);
        Assert.Equal(id1, sut.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task CloseTab_DirtyDraft_FiresOnCloseRequestedWithoutRemovingTab()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync();
        await sut.OpenTabAsync(id, "user1");
        sut.ActiveTab!.Draft.MarkDirty();

        TabState? captured = null;
        sut.OnCloseRequested += tab => { captured = tab; return Task.CompletedTask; };

        sut.CloseTab(id);

        Assert.NotNull(captured);
        Assert.Equal(id, captured!.RequestId);
        Assert.Single(sut.Tabs);
    }

    [Fact]
    public async Task ForceCloseTab_DirtyDraft_RemovesWithoutEvent()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync();
        await sut.OpenTabAsync(id, "user1");
        sut.ActiveTab!.Draft.MarkDirty();

        bool eventFired = false;
        sut.OnCloseRequested += _ => { eventFired = true; return Task.CompletedTask; };

        sut.ForceCloseTab(id);

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
        Assert.False(eventFired);
    }

    [Fact]
    public async Task CloseAllTabs_ClearsAllTabsAndResetsAppState()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1, "user1");
        await sut.OpenTabAsync(id2, "user1");

        sut.CloseAllTabs();

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
        Assert.Null(_appState.SelectedRequestId);
    }
}
