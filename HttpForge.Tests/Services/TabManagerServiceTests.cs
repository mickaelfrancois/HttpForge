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
        return new TabManagerService(factory, _appState);
    }

    private async Task<int> SeedRequestAsync(string name = "GET /test")
    {
        await using var db = new AppDbContext(_opts);
        var col = await db.Collections.FirstOrDefaultAsync();
        if (col is null)
        {
            col = new Collection { Name = "Test" };
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

        await sut.OpenTabAsync(id);

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

        await sut.OpenTabAsync(id);
        await sut.OpenTabAsync(id);

        Assert.Single(sut.Tabs);
        Assert.Equal(id, sut.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task CloseTab_CleanDraft_RemovesTabAndActivatesNeighbour()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);

        sut.CloseTab(id2);

        Assert.Single(sut.Tabs);
        Assert.Equal(id1, sut.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task CloseTab_DirtyDraft_FiresOnCloseRequestedWithoutRemovingTab()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync();
        await sut.OpenTabAsync(id);
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
        await sut.OpenTabAsync(id);
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
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);

        sut.CloseAllTabs();

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
        Assert.Null(_appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_TargetIsFirst_RemovesAllToTheRight()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);

        sut.CloseTabsToTheRight(id1);

        Assert.Single(sut.Tabs);
        Assert.Equal(id1, sut.Tabs[0].RequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_TargetInMiddle_KeepsLeftAndTarget()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);

        sut.CloseTabsToTheRight(id2);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(id1, sut.Tabs[0].RequestId);
        Assert.Equal(id2, sut.Tabs[1].RequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_TargetIsLast_NoOp()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);

        sut.CloseTabsToTheRight(id2);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(id2, sut.ActiveTab!.RequestId);
        Assert.Equal(id2, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_UnknownRequestId_NoOp()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);

        sut.CloseTabsToTheRight(99999);

        Assert.Equal(2, sut.Tabs.Count);
    }

    [Fact]
    public async Task CloseTabsToTheRight_ActiveWasToTheRight_ActivatesTarget()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);

        sut.CloseTabsToTheRight(id1);

        Assert.Single(sut.Tabs);
        Assert.Equal(id1, sut.ActiveTab!.RequestId);
        Assert.Equal(id1, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_ActiveToTheLeftOfTarget_KeepsActive()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);
        sut.ActivateTab(id1);

        sut.CloseTabsToTheRight(id2);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(id1, sut.ActiveTab!.RequestId);
        Assert.Equal(id1, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsToTheRight_ActiveIsTarget_KeepsActive()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);
        sut.ActivateTab(id2);

        sut.CloseTabsToTheRight(id2);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(id2, sut.ActiveTab!.RequestId);
        Assert.Equal(id2, _appState.SelectedRequestId);
    }

    private async Task<int> SeedRequestInCollectionAsync(string collectionName, string name)
    {
        await using var db = new AppDbContext(_opts);
        var col = await db.Collections.FirstOrDefaultAsync(c => c.Name == collectionName);
        if (col is null)
        {
            col = new Collection { Name = collectionName };
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
    public async Task OpenTabAsync_NewRequest_TabStateCarriesCollectionId()
    {
        var sut = CreateSut();
        var id = await SeedRequestInCollectionAsync("Solo", "GET /x");
        await sut.OpenTabAsync(id);

        await using var db = new AppDbContext(_opts);
        var expectedCollectionId = (await db.Requests.FindAsync(id))!.CollectionId;

        Assert.NotEqual(0, sut.Tabs[0].CollectionId);
        Assert.Equal(expectedCollectionId, sut.Tabs[0].CollectionId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_TargetCollection_ClosesAllOfThatCollectionIncludingClicked()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var a2 = await SeedRequestInCollectionAsync("A", "GET /a2");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(a2);
        await sut.OpenTabAsync(b1);

        sut.CloseTabsOfCollection(a1);

        Assert.Single(sut.Tabs);
        Assert.Equal(b1, sut.Tabs[0].RequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_OtherCollectionsPreserved_KeepOrder()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        var a2 = await SeedRequestInCollectionAsync("A", "GET /a2");
        var c1 = await SeedRequestInCollectionAsync("C", "GET /c1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(b1);
        await sut.OpenTabAsync(a2);
        await sut.OpenTabAsync(c1);

        sut.CloseTabsOfCollection(a1);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(b1, sut.Tabs[0].RequestId);
        Assert.Equal(c1, sut.Tabs[1].RequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_ActiveInClosedCollection_ReactivatesSurvivor()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(b1);
        sut.ActivateTab(a1);

        sut.CloseTabsOfCollection(a1);

        Assert.Single(sut.Tabs);
        Assert.Equal(b1, sut.ActiveTab!.RequestId);
        Assert.Equal(b1, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_ActiveInOtherCollection_KeepsActive()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(b1);
        sut.ActivateTab(b1);

        sut.CloseTabsOfCollection(a1);

        Assert.Single(sut.Tabs);
        Assert.Equal(b1, sut.ActiveTab!.RequestId);
        Assert.Equal(b1, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_AllTabsSameCollection_ClosesEverything()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var a2 = await SeedRequestInCollectionAsync("A", "GET /a2");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(a2);

        sut.CloseTabsOfCollection(a1);

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
        Assert.Null(_appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_SingleTabCollection_ClosesOnlyThatTab()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        var c1 = await SeedRequestInCollectionAsync("C", "GET /c1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(b1);
        await sut.OpenTabAsync(c1);

        sut.CloseTabsOfCollection(c1);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(a1, sut.Tabs[0].RequestId);
        Assert.Equal(b1, sut.Tabs[1].RequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_UnknownRequestId_NoOp()
    {
        var sut = CreateSut();
        var a1 = await SeedRequestInCollectionAsync("A", "GET /a1");
        var b1 = await SeedRequestInCollectionAsync("B", "GET /b1");
        await sut.OpenTabAsync(a1);
        await sut.OpenTabAsync(b1);

        sut.CloseTabsOfCollection(99999);

        Assert.Equal(2, sut.Tabs.Count);
    }
}
