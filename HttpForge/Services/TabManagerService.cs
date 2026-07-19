using System.Text.Json;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HttpForge.Services;

// Persistence shape. RequestId/CollectionId are nullable so a stored entry carries
// only the id relevant to its Kind. Kind defaults to Request and ActiveKey to null,
// so JSON written by an earlier version ({RequestId, ActiveSubTab} + ActiveRequestId)
// still deserializes as request tabs — the format evolution is additive.
public record TabStorageState(int? RequestId, string ActiveSubTab, TabKind Kind = TabKind.Request, int? CollectionId = null, bool IsLocked = false);
public record TabStorageData(TabStorageState[] Tabs, int? ActiveRequestId, string? ActiveKey = null);

public class TabManagerService(
    IDbContextFactory<AppDbContext> dbFactory,
    AppState appState)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly List<TabState> _tabs = [];
    private IJSRuntime? _js;

    public IReadOnlyList<TabState> Tabs => _tabs;
    public TabState? ActiveTab { get; private set; }

    public event Action? OnChange;
    public event Func<TabState, Task>? OnCloseRequested;
    // Raised once per REQUEST tab as it is removed (single or bulk close). Lets listeners
    // drop per-tab resources — e.g. cancel a pending auto-save timer — without coupling this
    // service to the save pipeline. Never raised for a CollectionSettings tab (no draft,
    // no auto-save timer to reclaim).
    public event Action<int>? OnTabRemoved;

    public async Task InitAsync(IJSRuntime js)
    {
        if (_js is not null) return;
        _js = js;
        var json = await js.InvokeAsync<string?>("forge.tabs.load");
        if (json is null) return;

        var data = JsonSerializer.Deserialize<TabStorageData>(json, JsonOpts);
        if (data is null) return;

        foreach (var stored in data.Tabs)
        {
            var before = _tabs.Count;
            if (stored.Kind == TabKind.GlobalSettings)
            {
                OpenGlobalSettingsTabInternal();
            }
            else if (stored.Kind == TabKind.CollectionSettings)
            {
                if (stored.CollectionId is int cid)
                    await OpenCollectionSettingsTabInternalAsync(cid);
            }
            else if (stored.RequestId is int rid)
            {
                await OpenTabInternalAsync(rid, stored.ActiveSubTab);
            }
            if (stored.IsLocked && _tabs.Count > before) _tabs[^1].IsLocked = true;
        }

        // Prefer the canonical ActiveKey; fall back to the legacy ActiveRequestId so a tab
        // set persisted by an earlier version restores its active tab too.
        if (data.ActiveKey is { } activeKey)
            ActivateTab(activeKey, persist: false);
        else if (data.ActiveRequestId is int activeId)
            ActivateTab(activeId, persist: false);
    }

    // ── Request tabs ───────────────────────────────────────────────────────────

    public async Task OpenTabAsync(int requestId)
    {
        var key = TabState.RequestKey(requestId);
        if (_tabs.Any(t => t.Key == key))
        {
            ActivateTab(key);
            return;
        }
        await OpenTabInternalAsync(requestId, "Params");
        ActivateTab(key);
    }

    // ── Collection settings tab ──────────────────────────────────────────────────

    public async Task OpenCollectionSettingsTabAsync(int collectionId)
    {
        var key = TabState.CollectionKey(collectionId);
        if (_tabs.Any(t => t.Key == key))
        {
            ActivateTab(key);
            return;
        }
        if (!await OpenCollectionSettingsTabInternalAsync(collectionId)) return;
        ActivateTab(key);
    }

    public void CloseCollectionSettingsTab(int collectionId)
        => ForceCloseTab(TabState.CollectionKey(collectionId));

    // ── Global settings tab (singleton) ──────────────────────────────────────────

    public void OpenGlobalSettingsTab()
    {
        if (!_tabs.Any(t => t.Key == TabState.GlobalSettingsKey))
            OpenGlobalSettingsTabInternal();
        ActivateTab(TabState.GlobalSettingsKey);
    }

    // ── Locking ──────────────────────────────────────────────────────────────────

    public void ToggleLock(int requestId) => ToggleLock(TabState.RequestKey(requestId));

    public void ToggleLock(string key)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key);
        if (tab is null) return;
        tab.IsLocked = !tab.IsLocked;
        Notify();
    }

    // ── Activation / closing — canonical (by Key) with int wrappers for request tabs ──

    public void ActivateTab(int requestId, bool persist = true)
        => ActivateTab(TabState.RequestKey(requestId), persist);

    public void ActivateTab(string key, bool persist = true)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key);
        if (tab is null) return;
        ActiveTab = tab;
        SyncActiveSelection();
        appState.NotifyChanged();
        Notify(persist);
    }

    public void CloseTab(int requestId) => CloseTab(TabState.RequestKey(requestId));

    public void CloseTab(string key)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key);
        if (tab is null) return;
        // Only request tabs carry an editable (dirty-able) draft; a settings tab saves
        // instantly, so it always closes without a guard.
        if (tab.Kind == TabKind.Request && tab.Draft.IsDirty)
        {
            _ = OnCloseRequested?.Invoke(tab);
            return;
        }
        RemoveTab(tab);
    }

    public void ForceCloseTab(int requestId) => ForceCloseTab(TabState.RequestKey(requestId));

    public void ForceCloseTab(string key)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key);
        if (tab is not null) RemoveTab(tab);
    }

    public void CloseOtherTabs(int keepRequestId) => CloseOtherTabs(TabState.RequestKey(keepRequestId));

    public void CloseOtherTabs(string keepKey)
    {
        var toRemove = _tabs.Where(t => t.Key != keepKey && !t.IsLocked).ToList();
        foreach (var t in toRemove) _tabs.Remove(t);
        if (ActiveTab is null || !_tabs.Contains(ActiveTab))
            ActiveTab = _tabs.FirstOrDefault(t => t.Key == keepKey) ?? _tabs.FirstOrDefault();
        SyncActiveSelection();
        appState.NotifyChanged();
        foreach (var t in toRemove) NotifyRemoved(t);
        Notify();
    }

    public void CloseTabsToTheRight(int requestId) => CloseTabsToTheRight(TabState.RequestKey(requestId));

    public void CloseTabsToTheRight(string key)
    {
        var idx = _tabs.FindIndex(t => t.Key == key);
        if (idx < 0) return;

        var toRemove = _tabs.Skip(idx + 1).Where(t => !t.IsLocked).ToList();
        if (toRemove.Count == 0) return;

        foreach (var t in toRemove) _tabs.Remove(t);

        if (ActiveTab is null || !_tabs.Contains(ActiveTab))
        {
            ActiveTab = _tabs[idx];
            SyncActiveSelection();
        }
        appState.NotifyChanged();
        foreach (var t in toRemove) NotifyRemoved(t);
        Notify();
    }

    public void CloseTabsOfCollection(int requestId)
    {
        var target = _tabs.FirstOrDefault(t => t.Kind == TabKind.Request && t.RequestId == requestId);
        if (target is null) return;

        var collectionId = target.CollectionId;
        var toRemove = _tabs.Where(t => t.CollectionId == collectionId && !t.IsLocked).ToList();
        foreach (var t in toRemove) _tabs.Remove(t);

        if (ActiveTab is null || !_tabs.Contains(ActiveTab))
        {
            ActiveTab = _tabs.FirstOrDefault();
            SyncActiveSelection();
        }
        appState.NotifyChanged();
        Notify();
    }

    public void CloseAllTabs()
    {
        var toRemove = _tabs.Where(t => !t.IsLocked).ToList();
        foreach (var t in toRemove) _tabs.Remove(t);
        if (ActiveTab is null || !_tabs.Contains(ActiveTab)) ActiveTab = _tabs.FirstOrDefault();
        SyncActiveSelection();
        appState.NotifyChanged();
        foreach (var t in toRemove) NotifyRemoved(t);
        Notify();
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    private async Task OpenTabInternalAsync(int requestId, string activeSubTab)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.Requests
            .Include(r => r.Headers)
            .Include(r => r.QueryParams)
            .Include(r => r.FormFields)
            .Include(r => r.Variables)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null) return;

        _tabs.Add(new TabState
        {
            Kind = TabKind.Request,
            RequestId = requestId,
            CollectionId = request.CollectionId,
            Name = request.Name,
            Method = request.Method.ToString(),
            Draft = RequestDraft.FromRequest(request, DateTime.UtcNow),
            ActiveSubTab = activeSubTab
        });
    }

    // Mirrors OpenTabInternalAsync: a target that no longer exists is silently ignored
    // (returns false), so a stale persisted tab never resurrects a deleted collection.
    private async Task<bool> OpenCollectionSettingsTabInternalAsync(int collectionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection is null) return false;

        _tabs.Add(new TabState
        {
            Kind = TabKind.CollectionSettings,
            CollectionId = collectionId,
            Name = collection.Name
        });
        return true;
    }

    // No DB round-trip: the global variable set is a workspace singleton that always exists
    // (seeded at startup), so the tab is valid without loading anything.
    private void OpenGlobalSettingsTabInternal()
        => _tabs.Add(new TabState { Kind = TabKind.GlobalSettings, Name = "Variables globales" });

    private void RemoveTab(TabState tab)
    {
        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (ActiveTab == tab)
        {
            ActiveTab = _tabs.ElementAtOrDefault(idx - 1) ?? _tabs.FirstOrDefault();
            SyncActiveSelection();
            appState.NotifyChanged();
        }
        NotifyRemoved(tab);
        Notify();
    }

    // SelectedRequestId tracks the active tab only when it is a request; a settings tab
    // has no request, so it clears the selection. Centralized so every path that
    // recomputes the active tab keeps AppState consistent.
    private void SyncActiveSelection()
        => appState.SelectedRequestId = ActiveTab is { Kind: TabKind.Request } t ? t.RequestId : null;

    private void NotifyRemoved(TabState tab)
    {
        if (tab.Kind == TabKind.Request) OnTabRemoved?.Invoke(tab.RequestId);
    }

    private void Notify(bool persist = true)
    {
        if (persist) _ = PersistAsync();
        OnChange?.Invoke();
    }

    private async Task PersistAsync()
    {
        if (_js is null) return;
        var data = new TabStorageData(
            _tabs.Select(t => new TabStorageState(
                t.Kind == TabKind.Request ? t.RequestId : null,
                t.ActiveSubTab,
                t.Kind,
                t.Kind == TabKind.CollectionSettings ? t.CollectionId : null,
                t.IsLocked)).ToArray(),
            ActiveTab is { Kind: TabKind.Request } rt ? rt.RequestId : null,
            ActiveTab?.Key);
        await _js.InvokeVoidAsync("forge.tabs.save", JsonSerializer.Serialize(data));
    }
}
