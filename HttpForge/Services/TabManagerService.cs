using System.Text.Json;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HttpForge.Services;

public record TabStorageState(int RequestId, string ActiveSubTab);
public record TabStorageData(TabStorageState[] Tabs, int? ActiveRequestId);

public class TabManagerService(IDbContextFactory<AppDbContext> dbFactory, AppState appState)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly List<TabState> _tabs = [];
    private IJSRuntime? _js;

    public IReadOnlyList<TabState> Tabs => _tabs;
    public TabState? ActiveTab { get; private set; }

    public event Action? OnChange;
    public event Func<TabState, Task>? OnCloseRequested;

    public async Task InitAsync(IJSRuntime js, string userId)
    {
        if (_js is not null) return;
        _js = js;
        var json = await js.InvokeAsync<string?>("forge.tabs.load");
        if (json is null) return;

        var data = JsonSerializer.Deserialize<TabStorageData>(json, JsonOpts);
        if (data is null) return;

        foreach (var stored in data.Tabs)
            await OpenTabInternalAsync(stored.RequestId, userId, stored.ActiveSubTab);

        if (data.ActiveRequestId.HasValue)
            ActivateTab(data.ActiveRequestId.Value, persist: false);
    }

    public async Task OpenTabAsync(int requestId, string userId)
    {
        if (_tabs.Any(t => t.RequestId == requestId))
        {
            ActivateTab(requestId);
            return;
        }
        await OpenTabInternalAsync(requestId, userId, "Params");
        ActivateTab(requestId);
    }

    public void ActivateTab(int requestId, bool persist = true)
    {
        var tab = _tabs.FirstOrDefault(t => t.RequestId == requestId);
        if (tab is null) return;
        ActiveTab = tab;
        appState.SelectedRequestId = requestId;
        appState.NotifyChanged();
        Notify(persist);
    }

    public void CloseTab(int requestId)
    {
        var tab = _tabs.FirstOrDefault(t => t.RequestId == requestId);
        if (tab is null) return;
        if (tab.Draft.IsDirty)
        {
            _ = OnCloseRequested?.Invoke(tab);
            return;
        }
        RemoveTab(tab);
    }

    public void ForceCloseTab(int requestId)
    {
        var tab = _tabs.FirstOrDefault(t => t.RequestId == requestId);
        if (tab is not null) RemoveTab(tab);
    }

    public void CloseOtherTabs(int keepRequestId)
    {
        var toRemove = _tabs.Where(t => t.RequestId != keepRequestId).ToList();
        foreach (var t in toRemove) _tabs.Remove(t);
        ActiveTab = _tabs.FirstOrDefault();
        appState.SelectedRequestId = ActiveTab?.RequestId;
        appState.NotifyChanged();
        Notify();
    }

    public void CloseAllTabs()
    {
        _tabs.Clear();
        ActiveTab = null;
        appState.SelectedRequestId = null;
        appState.NotifyChanged();
        Notify();
    }

    private async Task OpenTabInternalAsync(int requestId, string userId, string activeSubTab)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.Requests
            .Include(r => r.Headers)
            .Include(r => r.QueryParams)
            .Include(r => r.FormFields)
            .Include(r => r.Variables)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null) return;

        var userVarValues = !string.IsNullOrEmpty(userId)
            ? await db.UserVariableValues
                .Where(v => v.UserId == userId && v.ScopeType == "request" && v.ScopeId == requestId)
                .ToListAsync()
            : [];

        var pendingPersonal = userVarValues.ToDictionary(v => v.VariableKey, v => v.Value, StringComparer.OrdinalIgnoreCase);

        _tabs.Add(new TabState
        {
            RequestId = requestId,
            Name = request.Name,
            Method = request.Method.ToString(),
            Draft = RequestDraft.FromRequest(request, DateTime.UtcNow),
            ActiveSubTab = activeSubTab,
            UserVarValues = userVarValues,
            PendingPersonalValues = pendingPersonal
        });
    }

    private void RemoveTab(TabState tab)
    {
        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (ActiveTab == tab)
        {
            ActiveTab = _tabs.ElementAtOrDefault(idx - 1) ?? _tabs.FirstOrDefault();
            appState.SelectedRequestId = ActiveTab?.RequestId;
            appState.NotifyChanged();
        }
        Notify();
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
            _tabs.Select(t => new TabStorageState(t.RequestId, t.ActiveSubTab)).ToArray(),
            ActiveTab?.RequestId);
        await _js.InvokeVoidAsync("forge.tabs.save", JsonSerializer.Serialize(data));
    }
}
