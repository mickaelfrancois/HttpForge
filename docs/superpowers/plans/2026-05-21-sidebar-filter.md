# Sidebar Collection Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a real-time text filter above the collection tree that hides non-matching collections, folders, and requests, and auto-expands folders containing matches.

**Architecture:** `NavMenu` holds `_filterText` and passes it as a `FilterText` parameter to each `CollectionNode`. `CollectionNode` handles its own visibility filtering via computed properties and helpers. No DB calls — everything is computed in-memory from the already-loaded flat lists.

**Tech Stack:** Blazor Server (.NET 10), C#, scoped CSS

---

## File Map

| Action | File | Change |
|--------|------|--------|
| Modify | `HttpForge/Components/Layout/CollectionNode.razor` | Add `FilterText` parameter, filtering helpers, update `DirectFolders`/`DirectRequests`, update expansion condition |
| Modify | `HttpForge/Components/Layout/NavMenu.razor` | Add `_filterText` field, filter input markup, collection-level visibility + expansion, pass `FilterText` to `CollectionNode` |
| Modify | `HttpForge/Components/Layout/NavMenu.razor.css` | Add `.filter-box` styles |

---

### Task 1: Add FilterText filtering logic to CollectionNode

**Files:**
- Modify: `HttpForge/Components/Layout/CollectionNode.razor`

**Context:**

Current parameters block (around line 84):
```csharp
[Parameter, EditorRequired] public int CollectionId { get; set; }
[Parameter] public int? ParentFolderId { get; set; }
[Parameter, EditorRequired] public List<CollectionFolder> AllFolders { get; set; } = [];
[Parameter, EditorRequired] public List<HttpRequestItem> AllRequests { get; set; } = [];
[Parameter] public EventCallback<int> OnChanged { get; set; }
```

Current computed properties (around line 91):
```csharp
private IEnumerable<CollectionFolder> DirectFolders =>
    AllFolders.Where(f => f.CollectionId == CollectionId && f.ParentFolderId == ParentFolderId)
              .OrderBy(f => f.Name);

private IEnumerable<HttpRequestItem> DirectRequests =>
    AllRequests.Where(r => r.FolderId == ParentFolderId)
               .OrderBy(r => r.Name);
```

Current folder expansion in template (around line 53):
```razor
@if (_expanded.Contains(folder.Id))
{
    <div class="folder-children">
        <CollectionNode CollectionId="CollectionId"
                        ParentFolderId="folder.Id"
                        AllFolders="AllFolders"
                        AllRequests="AllRequests"
                        OnChanged="OnChanged" />
    </div>
}
```

- [ ] **Step 1: Add `FilterText` parameter after existing parameters**

In the `@code` block, after `[Parameter] public EventCallback<int> OnChanged { get; set; }`, add:

```csharp
[Parameter] public string FilterText { get; set; } = string.Empty;
```

- [ ] **Step 2: Add filtering helpers and update computed properties**

Replace the existing `DirectFolders` and `DirectRequests` properties with:

```csharp
private bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);

private bool FolderMatches(CollectionFolder f) =>
    f.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

private bool RequestMatchesFilter(HttpRequestItem r) =>
    r.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

private bool SubtreeHasMatch(int folderId)
{
    if (AllRequests.Any(r => r.FolderId == folderId && RequestMatchesFilter(r))) return true;
    foreach (var child in AllFolders.Where(f => f.ParentFolderId == folderId))
        if (FolderMatches(child) || SubtreeHasMatch(child.Id)) return true;
    return false;
}

private IEnumerable<CollectionFolder> DirectFolders =>
    AllFolders.Where(f => f.CollectionId == CollectionId && f.ParentFolderId == ParentFolderId
        && (!IsFiltering || FolderMatches(f) || SubtreeHasMatch(f.Id)))
    .OrderBy(f => f.Name);

private IEnumerable<HttpRequestItem> DirectRequests =>
    AllRequests.Where(r => r.FolderId == ParentFolderId
        && (!IsFiltering || RequestMatchesFilter(r)))
    .OrderBy(r => r.Name);
```

- [ ] **Step 3: Update folder expansion condition and child FilterText in template**

Find the folder expansion block:
```razor
@if (_expanded.Contains(folder.Id))
{
    <div class="folder-children">
        <CollectionNode CollectionId="CollectionId"
                        ParentFolderId="folder.Id"
                        AllFolders="AllFolders"
                        AllRequests="AllRequests"
                        OnChanged="OnChanged" />
    </div>
}
```

Replace with:
```razor
@if (IsFiltering ? (FolderMatches(folder) || SubtreeHasMatch(folder.Id)) : _expanded.Contains(folder.Id))
{
    <div class="folder-children">
        <CollectionNode CollectionId="CollectionId"
                        ParentFolderId="folder.Id"
                        AllFolders="AllFolders"
                        AllRequests="AllRequests"
                        OnChanged="OnChanged"
                        FilterText="@(FolderMatches(folder) ? string.Empty : FilterText)" />
    </div>
}
```

Key: when a folder's own name matches, its children receive `FilterText=""` (show all). When a folder is visible only because a descendant matched, the filter continues down.

- [ ] **Step 4: Build**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add HttpForge/Components/Layout/CollectionNode.razor
git commit -m "feat: add FilterText parameter and filtering logic to CollectionNode"
```

---

### Task 2: Add filter UI to NavMenu and wire up FilterText

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`
- Modify: `HttpForge/Components/Layout/NavMenu.razor.css`

**Context:**

The `@code` block fields start around line 246:
```csharp
private List<Collection> _collections = new();
private List<CollectionFolder> _allFolders = [];
// ...
private HashSet<int> _expanded = new();
```

The "Collections" section title and tree (around lines 92–243):
```razor
<div class="section-title">
    <span>Collections</span>
    ...
</div>
@if (_importStatus is not null) { ... }
@if (_showAddCollection) { ... }

<div class="collection-tree">
    @foreach (var c in _collections)
    {
        <div class="collection-node">
            ...
            @if (_expanded.Contains(c.Id))
            {
                <div class="request-list" data-drop="collection:@c.Id">
                    <CollectionNode CollectionId="c.Id"
                                    ParentFolderId="@((int?)null)"
                                    AllFolders="_allFolders.Where(f => f.CollectionId == c.Id).ToList()"
                                    AllRequests="c.Requests"
                                    OnChanged="ReloadAndNotify" />
                    ...
                </div>
            }
        </div>
    }
    @if (_collections.Count == 0) { ... }
</div>
```

- [ ] **Step 1: Add `_filterText` field to the `@code` block**

In the `@code` block, after `private HashSet<int> _expanded = new();`, add:

```csharp
private string _filterText = string.Empty;
```

- [ ] **Step 2: Add filter input markup before `<div class="collection-tree">`**

Between the `@if (_showAddCollection)` block and `<div class="collection-tree">`, add:

```razor
<div class="filter-box">
    <input placeholder="Filter..."
           value="@_filterText"
           @oninput="e => _filterText = e.Value?.ToString() ?? string.Empty" />
    @if (!string.IsNullOrEmpty(_filterText))
    {
        <button class="icon-btn" title="Clear filter" @onclick="() => _filterText = string.Empty">✕</button>
    }
</div>
```

- [ ] **Step 3: Add `CollectionHasMatch` helper to the `@code` block**

Add this method:

```csharp
private bool CollectionHasMatch(Collection c)
{
    if (string.IsNullOrWhiteSpace(_filterText)) return true;
    if (c.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) return true;
    if (c.Requests.Any(r => r.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))) return true;
    if (_allFolders.Any(f => f.CollectionId == c.Id
        && f.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))) return true;
    return false;
}
```

- [ ] **Step 4: Update collection loop — hide non-matching collections and force-expand matching ones**

Find the collection loop:
```razor
@foreach (var c in _collections)
{
    <div class="collection-node">
```

Replace with:
```razor
@foreach (var c in _collections.Where(c => CollectionHasMatch(c)))
{
    <div class="collection-node">
```

Find the expansion condition:
```razor
@if (_expanded.Contains(c.Id))
{
    <div class="request-list" data-drop="collection:@c.Id">
        <CollectionNode CollectionId="c.Id"
                        ParentFolderId="@((int?)null)"
                        AllFolders="_allFolders.Where(f => f.CollectionId == c.Id).ToList()"
                        AllRequests="c.Requests"
                        OnChanged="ReloadAndNotify" />
```

Replace with:
```razor
@if (_expanded.Contains(c.Id) || !string.IsNullOrWhiteSpace(_filterText))
{
    <div class="request-list" data-drop="collection:@c.Id">
        <CollectionNode CollectionId="c.Id"
                        ParentFolderId="@((int?)null)"
                        AllFolders="_allFolders.Where(f => f.CollectionId == c.Id).ToList()"
                        AllRequests="c.Requests"
                        OnChanged="ReloadAndNotify"
                        FilterText="@(c.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ? string.Empty : _filterText)" />
```

Key: when the collection's own name matches the filter, its `CollectionNode` receives `FilterText=""` so all contents are shown. When the collection is visible only because a descendant matched, the filter is passed down.

- [ ] **Step 5: Add `.filter-box` styles to NavMenu.razor.css**

Append to `HttpForge/Components/Layout/NavMenu.razor.css`:

```css
.filter-box {
    display: flex;
    gap: 0.3rem;
    padding: 0.4rem 1rem;
}

.filter-box input {
    flex: 1;
    background: var(--bg-input-dark);
    border: 1px solid var(--border-input);
    color: var(--text-primary);
    border-radius: 3px;
    padding: 0.25rem 0.5rem;
    font-size: 0.82rem;
}
```

- [ ] **Step 6: Build**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Manual smoke test**

Run `dotnet run --project HttpForge` and verify:
- Filter input appears below the "Collections" title
- Typing in the filter hides collections/folders/requests that don't match
- Folders containing matching requests are auto-expanded
- If a collection name matches, all its contents are shown
- If a folder name matches, all its children are shown
- Clearing the filter (✕ or backspace) restores the normal view
- `_expanded` state is restored when filter is cleared

- [ ] **Step 8: Commit**

```powershell
git add HttpForge/Components/Layout/NavMenu.razor HttpForge/Components/Layout/NavMenu.razor.css
git commit -m "feat: add filter input to sidebar with collection/folder/request filtering"
```
