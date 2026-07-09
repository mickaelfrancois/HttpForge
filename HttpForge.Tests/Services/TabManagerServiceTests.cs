using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
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
    public async Task ForceCloseTab_FiresOnTabRemovedWithRequestId()
    {
        var sut = CreateSut();
        var id = await SeedRequestAsync();
        await sut.OpenTabAsync(id);

        var removed = new List<int>();
        sut.OnTabRemoved += removed.Add;

        sut.ForceCloseTab(id);

        Assert.Equal([id], removed);
    }

    [Fact]
    public async Task CloseAllTabs_FiresOnTabRemovedForEveryTab()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);

        var removed = new List<int>();
        sut.OnTabRemoved += removed.Add;

        sut.CloseAllTabs();

        Assert.Equal(2, removed.Count);
        Assert.Contains(id1, removed);
        Assert.Contains(id2, removed);
    }

    [Fact]
    public async Task CloseTabsToTheRight_FiresOnTabRemovedForClosedTabs()
    {
        var sut = CreateSut();
        var id1 = await SeedRequestAsync("GET /a");
        var id2 = await SeedRequestAsync("GET /b");
        var id3 = await SeedRequestAsync("GET /c");
        await sut.OpenTabAsync(id1);
        await sut.OpenTabAsync(id2);
        await sut.OpenTabAsync(id3);

        var removed = new List<int>();
        sut.OnTabRemoved += removed.Add;

        sut.CloseTabsToTheRight(id1);

        Assert.Equal(2, removed.Count);
        Assert.Contains(id2, removed);
        Assert.Contains(id3, removed);
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

    // ── Collection settings tab (non-request tab) ────────────────────────────────

    private async Task<int> SeedCollectionAsync(string name = "Settings coll")
    {
        await using var db = new AppDbContext(_opts);
        var col = new Collection { Name = name };
        db.Collections.Add(col);
        await db.SaveChangesAsync();
        return col.Id;
    }

    [Fact]
    public void TabState_Key_IsKindScoped_NoCollisionBetweenRequestAndCollection()
    {
        var request = new TabState { Kind = TabKind.Request, RequestId = 7 };
        var collection = new TabState { Kind = TabKind.CollectionSettings, CollectionId = 7 };

        Assert.Equal("request:7", request.Key);
        Assert.Equal("collection:7", collection.Key);
        Assert.NotEqual(request.Key, collection.Key);
    }

    [Fact]
    public async Task OpenCollectionSettingsTabAsync_AddsTab_LeavesSelectedRequestIdNull()
    {
        var sut = CreateSut();
        var cid = await SeedCollectionAsync();

        await sut.OpenCollectionSettingsTabAsync(cid);

        Assert.Single(sut.Tabs);
        Assert.Equal(TabKind.CollectionSettings, sut.Tabs[0].Kind);
        Assert.Equal(cid, sut.Tabs[0].CollectionId);
        Assert.Equal(TabKind.CollectionSettings, sut.ActiveTab!.Kind);
        Assert.Null(_appState.SelectedRequestId);
    }

    [Fact]
    public async Task OpenCollectionSettingsTabAsync_UnknownCollection_NoTab()
    {
        var sut = CreateSut();

        await sut.OpenCollectionSettingsTabAsync(99999);

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
    }

    [Fact]
    public async Task OpenCollectionSettingsTabAsync_AlreadyOpen_ActivatesWithoutDuplicate()
    {
        var sut = CreateSut();
        var cid = await SeedCollectionAsync();

        await sut.OpenCollectionSettingsTabAsync(cid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        Assert.Single(sut.Tabs);
        Assert.Equal(TabKind.CollectionSettings, sut.ActiveTab!.Kind);
    }

    [Fact]
    public async Task CollectionTab_CoexistsWithRequestTabs_PreservesOrderAndSelection()
    {
        var sut = CreateSut();
        var rid = await SeedRequestAsync("GET /a");
        var cid = await SeedCollectionAsync();

        await sut.OpenTabAsync(rid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        Assert.Equal(2, sut.Tabs.Count);
        Assert.Equal(TabKind.Request, sut.Tabs[0].Kind);
        Assert.Equal(TabKind.CollectionSettings, sut.Tabs[1].Kind);
        // Activating the collection tab clears the request selection; re-activating the
        // request tab restores it — the two kinds don't clobber each other.
        Assert.Null(_appState.SelectedRequestId);
        sut.ActivateTab(rid);
        Assert.Equal(rid, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseTab_CollectionTabByKey_RemovesAndReactivatesNeighbour()
    {
        var sut = CreateSut();
        var rid = await SeedRequestAsync("GET /a");
        var cid = await SeedCollectionAsync();
        await sut.OpenTabAsync(rid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        sut.CloseTab(TabState.CollectionKey(cid));

        Assert.Single(sut.Tabs);
        Assert.Equal(TabKind.Request, sut.ActiveTab!.Kind);
        Assert.Equal(rid, _appState.SelectedRequestId);
    }

    [Fact]
    public async Task CloseCollectionSettingsTab_RemovesThatCollectionTab()
    {
        var sut = CreateSut();
        var cid = await SeedCollectionAsync();
        await sut.OpenCollectionSettingsTabAsync(cid);

        sut.CloseCollectionSettingsTab(cid);

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
    }

    [Fact]
    public async Task CloseAllTabs_MixedTabs_ClearsEverything_OnlyRequestsRaiseRemoved()
    {
        var sut = CreateSut();
        var rid = await SeedRequestAsync("GET /a");
        var cid = await SeedCollectionAsync();
        await sut.OpenTabAsync(rid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        var removed = new List<int>();
        sut.OnTabRemoved += removed.Add;

        sut.CloseAllTabs();

        Assert.Empty(sut.Tabs);
        Assert.Null(_appState.SelectedRequestId);
        Assert.Equal([rid], removed); // collection tab does not raise OnTabRemoved
    }

    [Fact]
    public async Task CloseOtherTabs_FromRequest_AlsoClosesCollectionTab()
    {
        var sut = CreateSut();
        var rid = await SeedRequestAsync("GET /a");
        var cid = await SeedCollectionAsync();
        await sut.OpenTabAsync(rid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        sut.CloseOtherTabs(rid);

        Assert.Single(sut.Tabs);
        Assert.Equal(TabKind.Request, sut.Tabs[0].Kind);
        Assert.Equal(rid, sut.Tabs[0].RequestId);
    }

    [Fact]
    public async Task CloseTabsOfCollection_AlsoClosesSettingsTabOfSameCollection()
    {
        var sut = CreateSut();
        var rid = await SeedRequestAsync("GET /a");
        int cid;
        await using (var db = new AppDbContext(_opts))
            cid = (await db.Requests.FindAsync(rid))!.CollectionId;
        await sut.OpenTabAsync(rid);
        await sut.OpenCollectionSettingsTabAsync(cid);

        // Closing the collection's request tabs also closes its settings tab (same
        // CollectionId) — a deliberate, tested side effect of the shared identity.
        sut.CloseTabsOfCollection(rid);

        Assert.Empty(sut.Tabs);
    }

    [Fact]
    public async Task OnTabRemoved_NotRaisedForCollectionTab()
    {
        var sut = CreateSut();
        var cid = await SeedCollectionAsync();
        await sut.OpenCollectionSettingsTabAsync(cid);

        var removed = new List<int>();
        sut.OnTabRemoved += removed.Add;

        sut.CloseCollectionSettingsTab(cid);

        Assert.Empty(removed);
    }

    // ── Persistence round-trip (IJSRuntime stubbed) ─────────────────────────────

    [Fact]
    public async Task PersistAndInit_RoundTripsRequestAndCollectionTabs()
    {
        var js = new FakeJsRuntime();
        var writer = CreateSut();
        await writer.InitAsync(js); // nothing stored yet → no tabs
        var rid = await SeedRequestAsync("GET /a");
        var cid = await SeedCollectionAsync();
        await writer.OpenTabAsync(rid);
        await writer.OpenCollectionSettingsTabAsync(cid); // collection tab is active + persisted

        var reader = CreateSut();
        await reader.InitAsync(js);

        Assert.Equal(2, reader.Tabs.Count);
        Assert.Equal(TabKind.Request, reader.Tabs[0].Kind);
        Assert.Equal(rid, reader.Tabs[0].RequestId);
        Assert.Equal(TabKind.CollectionSettings, reader.Tabs[1].Kind);
        Assert.Equal(cid, reader.Tabs[1].CollectionId);
        Assert.Equal(TabState.CollectionKey(cid), reader.ActiveTab!.Key);
        Assert.Null(_appState.SelectedRequestId); // active tab is the collection tab
    }

    [Fact]
    public async Task Init_LegacyJsonWithoutKind_RestoresAsRequestTab()
    {
        var rid = await SeedRequestAsync("GET /a");
        var js = new FakeJsRuntime
        {
            Stored = $"{{\"Tabs\":[{{\"RequestId\":{rid},\"ActiveSubTab\":\"Body\"}}],\"ActiveRequestId\":{rid}}}"
        };
        var sut = CreateSut();

        await sut.InitAsync(js);

        Assert.Single(sut.Tabs);
        Assert.Equal(TabKind.Request, sut.Tabs[0].Kind);
        Assert.Equal("Body", sut.Tabs[0].ActiveSubTab);
        Assert.Equal(rid, sut.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task Init_TargetsNoLongerExist_IgnoredWithoutException()
    {
        var js = new FakeJsRuntime
        {
            Stored = "{\"Tabs\":[" +
                     "{\"RequestId\":99999,\"ActiveSubTab\":\"Params\"}," +
                     "{\"Kind\":1,\"CollectionId\":88888,\"ActiveSubTab\":\"Params\"}]," +
                     "\"ActiveKey\":\"collection:88888\"}"
        };
        var sut = CreateSut();

        await sut.InitAsync(js);

        Assert.Empty(sut.Tabs);
        Assert.Null(sut.ActiveTab);
    }

    // Minimal IJSRuntime stub: captures the JSON written by forge.tabs.save and replays it
    // on forge.tabs.load. Its ValueTasks complete synchronously, so the service's
    // fire-and-forget PersistAsync has landed by the time an operation returns.
    private sealed class FakeJsRuntime : IJSRuntime
    {
        public string? Stored { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "forge.tabs.save":
                    Stored = args?[0] as string;
                    return new ValueTask<TValue>(default(TValue)!);
                case "forge.tabs.load":
                    return new ValueTask<TValue>((TValue)(object?)Stored!);
                default:
                    return new ValueTask<TValue>(default(TValue)!);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
