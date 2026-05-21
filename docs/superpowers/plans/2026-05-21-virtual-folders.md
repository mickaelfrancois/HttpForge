# Virtual Folders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add nestable virtual folders to collections, with drag & drop to move requests and folders across collections/folders.

**Architecture:** New `CollectionFolder` entity (self-referencing for nesting, `CollectionId` denormalized on all nodes). NavMenu is refactored into `RequestRow.razor` + `CollectionNode.razor` (recursive). Drag & drop via HTML5 native events delegated through `window.forge.dnd` in `forge.js`; drops call `[JSInvokable] NavMenu.OnDrop`.

**Tech Stack:** Blazor Server InteractiveServer, EF Core + SQLite (manual SchemaUpgrader), HTML5 Drag & Drop API, vanilla JS in forge.js.

---

## File Map

| Action | File |
|--------|------|
| Create | `HttpForge/Data/Entities/CollectionFolder.cs` |
| Modify | `HttpForge/Data/Entities/HttpRequestItem.cs` — add `FolderId` + nav prop |
| Modify | `HttpForge/Data/Entities/Collection.cs` — add `Folders` nav prop |
| Modify | `HttpForge/Data/AppDbContext.cs` — add DbSet + EF relationships |
| Modify | `HttpForge/Data/SchemaUpgrader.cs` — EnsureTable + EnsureColumn |
| Create | `HttpForge/Components/Layout/RequestRow.razor` |
| Create | `HttpForge/Components/Layout/RequestRow.razor.css` |
| Create | `HttpForge/Components/Layout/CollectionNode.razor` |
| Create | `HttpForge/Components/Layout/CollectionNode.razor.css` |
| Modify | `HttpForge/Components/Layout/NavMenu.razor` — use new components, add OnDrop + DnD init |
| Modify | `HttpForge/wwwroot/forge.js` — add `window.forge.dnd` |

---

## Task 1: Data model — CollectionFolder entity + schema

**Files:**
- Create: `HttpForge/Data/Entities/CollectionFolder.cs`
- Modify: `HttpForge/Data/Entities/HttpRequestItem.cs`
- Modify: `HttpForge/Data/Entities/Collection.cs`
- Modify: `HttpForge/Data/AppDbContext.cs`
- Modify: `HttpForge/Data/SchemaUpgrader.cs`

- [ ] **Step 1: Create `CollectionFolder.cs`**

```csharp
// HttpForge/Data/Entities/CollectionFolder.cs
namespace HttpForge.Data.Entities;

public class CollectionFolder
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public int? ParentFolderId { get; set; }
    public CollectionFolder? ParentFolder { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<CollectionFolder> Children { get; set; } = [];
    public List<HttpRequestItem> Requests { get; set; } = [];
}
```

- [ ] **Step 2: Add `FolderId` to `HttpRequestItem`**

In `HttpForge/Data/Entities/HttpRequestItem.cs`, add after `public int CollectionId`:

```csharp
public int? FolderId { get; set; }
public CollectionFolder? Folder { get; set; }
```

- [ ] **Step 3: Add `Folders` nav prop to `Collection`**

In `HttpForge/Data/Entities/Collection.cs`, add after `public List<CollectionVariableSet> VariableSets`:

```csharp
public List<CollectionFolder> Folders { get; set; } = [];
```

- [ ] **Step 4: Update `AppDbContext`**

In `HttpForge/Data/AppDbContext.cs`, add after `public DbSet<CollectionVariableEntries>`:

```csharp
public DbSet<CollectionFolder> CollectionFolders => Set<CollectionFolder>();
```

In `OnModelCreating`, add after the existing `Collection → Requests` config:

```csharp
b.Entity<Collection>()
    .HasMany(c => c.Folders)
    .WithOne(f => f.Collection!)
    .HasForeignKey(f => f.CollectionId)
    .OnDelete(DeleteBehavior.Cascade);

b.Entity<CollectionFolder>()
    .HasMany(f => f.Children)
    .WithOne(f => f.ParentFolder)
    .HasForeignKey(f => f.ParentFolderId)
    .OnDelete(DeleteBehavior.ClientCascade);

b.Entity<CollectionFolder>()
    .HasMany(f => f.Requests)
    .WithOne(r => r.Folder)
    .HasForeignKey(r => r.FolderId)
    .OnDelete(DeleteBehavior.ClientSetNull);
```

Note: `ClientCascade` / `ClientSetNull` are used instead of DB-level cascade for the self-referencing and folder→request relationships, because recursive DB-level CASCADE in SQLite is unreliable here. Deletion is handled explicitly in code (Task 4).

- [ ] **Step 5: Update `SchemaUpgrader`**

In `SchemaUpgrader._allowedTables`, add `"CollectionFolders"`.

In `SchemaUpgrader.Apply`, add before `EnsureGlobalBase`:

```csharp
EnsureTable(db, "CollectionFolders",
    "CREATE TABLE \"CollectionFolders\" (" +
    "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
    "\"CollectionId\" INTEGER NOT NULL, " +
    "\"ParentFolderId\" INTEGER NULL, " +
    "\"Name\" TEXT NOT NULL DEFAULT '');");

EnsureColumn(db, "Requests", "FolderId", "INTEGER NULL");
```

- [ ] **Step 6: Build and verify**

```powershell
dotnet build HttpForge
```

Expected: no errors. Run the app (`dotnet run --project HttpForge`), open the browser, verify the sidebar loads normally. The DB gets the new `CollectionFolders` table and `Requests.FolderId` column on first launch.

- [ ] **Step 7: Commit**

```
git add HttpForge/Data/Entities/CollectionFolder.cs HttpForge/Data/Entities/HttpRequestItem.cs HttpForge/Data/Entities/Collection.cs HttpForge/Data/AppDbContext.cs HttpForge/Data/SchemaUpgrader.cs
git commit -m "feat: add CollectionFolder entity and schema migration"
```

---

## Task 2: Extract RequestRow.razor

**Files:**
- Create: `HttpForge/Components/Layout/RequestRow.razor`
- Create: `HttpForge/Components/Layout/RequestRow.razor.css`
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Create `RequestRow.razor`**

```razor
@* HttpForge/Components/Layout/RequestRow.razor *@
@rendermode InteractiveServer
@inject IDbContextFactory<AppDbContext> DbFactory
@inject AppState State
@inject IJSRuntime JS

<div class="request-row @(State.SelectedRequestId == Request.Id ? "selected" : "")"
     draggable="true"
     data-drag="request:@Request.Id"
     @onclick="Select">
    <span class="method method-@Request.Method.ToString().ToLower()">@Request.Method</span>
    <span class="request-name">@(string.IsNullOrWhiteSpace(Request.Name) ? "Untitled" : Request.Name)</span>
    <button class="icon-btn" title="Duplicate request" @onclick:stopPropagation @onclick="Duplicate">⎘</button>
    <button class="icon-btn" title="Delete request" @onclick:stopPropagation @onclick="Delete">✕</button>
</div>

@code {
    [Parameter, EditorRequired] public HttpRequestItem Request { get; set; } = null!;
    [Parameter] public EventCallback OnChanged { get; set; }

    private void Select()
    {
        State.SelectedRequestId = Request.Id;
        State.NotifyChanged();
    }

    private async Task Duplicate()
    {
        using var db = await DbFactory.CreateDbContextAsync();
        var source = await db.Requests
            .Include(x => x.Headers)
            .Include(x => x.QueryParams)
            .Include(x => x.FormFields)
            .Include(x => x.Variables)
            .AsNoTracking()
            .FirstAsync(x => x.Id == Request.Id);

        var copy = new HttpRequestItem
        {
            CollectionId = source.CollectionId,
            FolderId = source.FolderId,
            Name = source.Name + " (copy)",
            Method = source.Method,
            Url = source.Url,
            BodyKind = source.BodyKind,
            BodyContent = source.BodyContent,
            UpdatedAt = DateTime.UtcNow,
            Headers = source.Headers.Select(h => new HeaderItem { Key = h.Key, Value = h.Value, Enabled = h.Enabled }).ToList(),
            QueryParams = source.QueryParams.Select(p => new QueryParamItem { Key = p.Key, Value = p.Value, Enabled = p.Enabled }).ToList(),
            FormFields = source.FormFields.Select(f => new FormFieldItem { Key = f.Key, Value = f.Value, Enabled = f.Enabled }).ToList(),
            Variables = source.Variables.Select(v => new RequestVariable { Key = v.Key, Value = v.Value, IsSecret = v.IsSecret }).ToList()
        };
        db.Requests.Add(copy);
        await db.SaveChangesAsync();
        State.SelectedRequestId = copy.Id;
        State.NotifyChanged();
        await OnChanged.InvokeAsync();
    }

    private async Task Delete()
    {
        var label = string.IsNullOrWhiteSpace(Request.Name) ? "Untitled" : Request.Name;
        if (!await JS.InvokeAsync<bool>("confirm", $"Delete request \"{label}\"?")) return;
        using var db = await DbFactory.CreateDbContextAsync();
        db.Requests.Remove(await db.Requests.FirstAsync(x => x.Id == Request.Id));
        await db.SaveChangesAsync();
        if (State.SelectedRequestId == Request.Id)
        {
            State.SelectedRequestId = null;
            State.NotifyChanged();
        }
        await OnChanged.InvokeAsync();
    }
}
```

- [ ] **Step 2: Create `RequestRow.razor.css`** (empty for now — styles stay in NavMenu.razor.css)

Create an empty file at `HttpForge/Components/Layout/RequestRow.razor.css`.

- [ ] **Step 3: Replace the inline request row in `NavMenu.razor`**

Find this block in `NavMenu.razor` (inside `<div class="request-list">`):

```razor
@foreach (var r in c.Requests.OrderByDescending(r => r.UpdatedAt))
{
    <div class="request-row @(State.SelectedRequestId == r.Id ? "selected" : "")"
         @onclick="() => SelectRequest(r.Id)">
        <span class="method method-@r.Method.ToString().ToLower()">@r.Method</span>
        <span class="request-name">@(string.IsNullOrWhiteSpace(r.Name) ? "Untitled" : r.Name)</span>
        <button class="icon-btn" title="Duplicate request" @onclick:stopPropagation @onclick="() => DuplicateRequest(r)">⎘</button>
        <button class="icon-btn" title="Delete request" @onclick:stopPropagation @onclick="() => DeleteRequest(r)">✕</button>
    </div>
}
```

Replace with:

```razor
@foreach (var r in c.Requests.Where(r => r.FolderId == null).OrderByDescending(r => r.UpdatedAt))
{
    <RequestRow Request="r" OnChanged="ReloadAndNotify" />
}
```

- [ ] **Step 4: Add `ReloadAndNotify` helper and remove the methods now handled by `RequestRow`**

Add to `@code` in `NavMenu.razor`:

```csharp
private async Task ReloadAndNotify()
{
    await ReloadAsync();
    await InvokeAsync(StateHasChanged);
}
```

Remove from `NavMenu.razor @code`: `SelectRequest`, `DeleteRequest`, `DuplicateRequest` methods.

- [ ] **Step 5: Build and run**

```powershell
dotnet build HttpForge
```

Expected: no errors. Open the browser, verify request rows render and work (select, duplicate, delete).

- [ ] **Step 6: Commit**

```
git add HttpForge/Components/Layout/RequestRow.razor HttpForge/Components/Layout/RequestRow.razor.css HttpForge/Components/Layout/NavMenu.razor
git commit -m "refactor: extract RequestRow component from NavMenu"
```

---

## Task 3: CollectionNode.razor — recursive tree rendering

**Files:**
- Create: `HttpForge/Components/Layout/CollectionNode.razor`
- Create: `HttpForge/Components/Layout/CollectionNode.razor.css`
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Create `CollectionNode.razor`**

```razor
@* HttpForge/Components/Layout/CollectionNode.razor *@
@rendermode InteractiveServer
@inject IDbContextFactory<AppDbContext> DbFactory
@inject AppState State
@inject IJSRuntime JS

@* Renders the contents of a collection root (ParentFolderId=null) or a folder. *@
@* Folder header is rendered by the PARENT; this component renders CHILDREN. *@

@* Sub-folders at this level *@
@foreach (var folder in DirectFolders)
{
    <div class="folder-item">
        <div class="folder-row"
             tabindex="0"
             draggable="true"
             data-drag="folder:@folder.Id"
             data-drop="folder:@folder.Id"
             @onkeydown="e => OnFolderKeyDown(folder, e)">
            <button class="folder-toggle" @onclick="() => ToggleExpand(folder.Id)" @onclick:stopPropagation>
                @(_expanded.Contains(folder.Id) ? "▾" : "▸")
            </button>
            @if (_renaming == folder.Id)
            {
                <input class="folder-rename-input"
                       @ref="_renameInput"
                       value="@_renameValue"
                       @oninput="e => _renameValue = e.Value?.ToString() ?? string.Empty"
                       @onblur="() => CommitRename(folder)"
                       @onkeydown="e => OnRenameInputKeyDown(folder, e)"
                       @onclick:stopPropagation />
            }
            else
            {
                <span class="folder-name">@folder.Name</span>
                <button class="icon-btn" title="New sub-folder" @onclick:stopPropagation @onclick="() => StartAddFolder(folder.Id)">+</button>
                <button class="icon-btn" title="New request" @onclick:stopPropagation @onclick="() => AddRequest(folder)">↵</button>
                <button class="icon-btn" title="Delete folder" @onclick:stopPropagation @onclick="() => DeleteFolder(folder)">🗑</button>
            }
        </div>

        @if (_addingUnder == folder.Id)
        {
            <div class="folder-inline-add">
                <input placeholder="Folder name"
                       @bind="_addingName"
                       @bind:event="oninput"
                       @onkeydown="e => OnAddInputKeyDown(e, folder.Id)"
                       @onblur="() => CancelAdd()" />
            </div>
        }

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
    </div>
}

@* Requests at this level *@
@foreach (var r in DirectRequests)
{
    <RequestRow Request="r" OnChanged="OnChanged" />
}

@* Inline "add folder" at this level (triggered from parent or collection header) *@
@if (_addingUnder == -1)
{
    <div class="folder-inline-add">
        <input placeholder="Folder name"
               @bind="_addingName"
               @bind:event="oninput"
               @onkeydown="e => OnAddInputKeyDown(e, null)"
               @onblur="() => CancelAdd()" />
    </div>
}

@code {
    [Parameter, EditorRequired] public int CollectionId { get; set; }
    [Parameter] public int? ParentFolderId { get; set; }
    [Parameter, EditorRequired] public List<CollectionFolder> AllFolders { get; set; } = [];
    [Parameter, EditorRequired] public List<HttpRequestItem> AllRequests { get; set; } = [];
    [Parameter] public EventCallback OnChanged { get; set; }

    private IEnumerable<CollectionFolder> DirectFolders =>
        AllFolders.Where(f => f.CollectionId == CollectionId && f.ParentFolderId == ParentFolderId)
                  .OrderBy(f => f.Name);

    private IEnumerable<HttpRequestItem> DirectRequests =>
        AllRequests.Where(r => r.FolderId == ParentFolderId)
                   .OrderByDescending(r => r.UpdatedAt);

    private HashSet<int> _expanded = [];
    private int? _renaming;
    private string _renameValue = string.Empty;
    private bool _shouldFocusRename;
    private ElementReference _renameInput;

    // -1 = add at this level (no parent folder), >0 = add inside that folder id
    private int? _addingUnder;
    private string _addingName = string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusRename && _renaming.HasValue)
        {
            _shouldFocusRename = false;
            try { await _renameInput.FocusAsync(); } catch { }
        }
    }

    private void ToggleExpand(int id)
    {
        if (!_expanded.Add(id)) _expanded.Remove(id);
    }

    // Called from NavMenu when user clicks "+ folder" on the collection header
    public void TriggerAddFolderAtRoot()
    {
        _addingUnder = -1;
        _addingName = string.Empty;
        StateHasChanged();
    }

    private void StartAddFolder(int parentFolderId)
    {
        _addingUnder = parentFolderId;
        _addingName = string.Empty;
    }

    private void CancelAdd()
    {
        _addingUnder = null;
        _addingName = string.Empty;
    }

    private async Task OnAddInputKeyDown(KeyboardEventArgs e, int? parentFolderId)
    {
        if (e.Key == "Enter") await CommitAdd(parentFolderId);
        else if (e.Key == "Escape") CancelAdd();
    }

    private async Task CommitAdd(int? parentFolderId)
    {
        var name = _addingName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { CancelAdd(); return; }

        using var db = await DbFactory.CreateDbContextAsync();
        db.CollectionFolders.Add(new CollectionFolder
        {
            CollectionId = CollectionId,
            ParentFolderId = parentFolderId == -1 ? null : parentFolderId,
            Name = name
        });
        await db.SaveChangesAsync();
        CancelAdd();
        await OnChanged.InvokeAsync();
    }

    private void OnFolderKeyDown(CollectionFolder folder, KeyboardEventArgs e)
    {
        if (e.Key == "F2") StartRename(folder);
    }

    private void StartRename(CollectionFolder folder)
    {
        _renaming = folder.Id;
        _renameValue = folder.Name;
        _shouldFocusRename = true;
        StateHasChanged();
    }

    private async Task OnRenameInputKeyDown(CollectionFolder folder, KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await CommitRename(folder);
        else if (e.Key == "Escape") CancelRename();
    }

    private void CancelRename()
    {
        _renaming = null;
        _renameValue = string.Empty;
    }

    private async Task CommitRename(CollectionFolder folder)
    {
        if (_renaming != folder.Id) return;
        var name = _renameValue.Trim();
        _renaming = null;
        if (string.IsNullOrWhiteSpace(name) || name == folder.Name) return;

        using var db = await DbFactory.CreateDbContextAsync();
        var f = await db.CollectionFolders.FirstAsync(x => x.Id == folder.Id);
        f.Name = name;
        await db.SaveChangesAsync();
        await OnChanged.InvokeAsync();
    }

    private async Task DeleteFolder(CollectionFolder folder)
    {
        var (folderCount, requestCount) = CountDescendants(folder.Id);
        var msg = $"Delete folder \"{folder.Name}\"";
        if (requestCount > 0 || folderCount > 0)
            msg += $" and its {requestCount} request(s) and {folderCount} sub-folder(s)";
        msg += "?";
        if (!await JS.InvokeAsync<bool>("confirm", msg)) return;

        var ids = GetAllDescendantFolderIds(folder.Id);

        using var db = await DbFactory.CreateDbContextAsync();
        var requests = await db.Requests
            .Where(r => r.FolderId != null && ids.Contains(r.FolderId.Value))
            .ToListAsync();
        db.Requests.RemoveRange(requests);

        var folders = await db.CollectionFolders
            .Where(f => ids.Contains(f.Id))
            .ToListAsync();
        // Remove deepest first to avoid FK issues
        foreach (var f in folders.OrderByDescending(f => GetFolderDepth(f.Id)))
            db.CollectionFolders.Remove(f);

        await db.SaveChangesAsync();

        if (State.SelectedRequestId.HasValue &&
            requests.Any(r => r.Id == State.SelectedRequestId))
        {
            State.SelectedRequestId = null;
        }
        await OnChanged.InvokeAsync();
        State.NotifyChanged();
    }

    private async Task AddRequest(CollectionFolder folder)
    {
        using var db = await DbFactory.CreateDbContextAsync();
        var r = new HttpRequestItem
        {
            CollectionId = CollectionId,
            FolderId = folder.Id,
            Name = "New request",
            Method = HttpMethodKind.GET,
            Url = "https://"
        };
        db.Requests.Add(r);
        await db.SaveChangesAsync();
        State.SelectedRequestId = r.Id;
        State.NotifyChanged();
        await OnChanged.InvokeAsync();
    }

    private List<int> GetAllDescendantFolderIds(int folderId)
    {
        var result = new List<int> { folderId };
        var children = AllFolders.Where(f => f.ParentFolderId == folderId);
        foreach (var child in children)
            result.AddRange(GetAllDescendantFolderIds(child.Id));
        return result;
    }

    private (int folders, int requests) CountDescendants(int folderId)
    {
        int folderCount = 0, requestCount = 0;
        var children = AllFolders.Where(f => f.ParentFolderId == folderId).ToList();
        foreach (var child in children)
        {
            folderCount++;
            var (cf, cr) = CountDescendants(child.Id);
            folderCount += cf;
            requestCount += cr;
        }
        requestCount += AllRequests.Count(r => r.FolderId == folderId);
        return (folderCount, requestCount);
    }

    private int GetFolderDepth(int folderId)
    {
        var f = AllFolders.FirstOrDefault(x => x.Id == folderId);
        if (f?.ParentFolderId == null) return 0;
        return 1 + GetFolderDepth(f.ParentFolderId.Value);
    }
}
```

- [ ] **Step 2: Create `CollectionNode.razor.css`**

```css
/* HttpForge/Components/Layout/CollectionNode.razor.css */
.folder-item {
    display: flex;
    flex-direction: column;
}

.folder-row {
    display: flex;
    align-items: center;
    gap: 2px;
    padding: 2px 4px;
    border-radius: 4px;
    cursor: default;
    user-select: none;
    outline: none;
}

.folder-row:hover,
.folder-row:focus {
    background: var(--hover-bg, rgba(0,0,0,0.06));
}

.folder-row.drag-over {
    background: var(--accent-color, #4a90e2);
    color: white;
}

.folder-toggle {
    background: none;
    border: none;
    cursor: pointer;
    padding: 0 2px;
    color: inherit;
    font-size: 0.75rem;
}

.folder-name {
    flex: 1;
    font-size: 0.85rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.folder-rename-input {
    flex: 1;
    font-size: 0.85rem;
    background: var(--input-bg, white);
    border: 1px solid var(--border-color, #ccc);
    border-radius: 3px;
    padding: 1px 4px;
}

.folder-children {
    padding-left: 14px;
}

.folder-inline-add {
    padding: 2px 4px 2px 18px;
}

.folder-inline-add input {
    width: 100%;
    font-size: 0.85rem;
    background: var(--input-bg, white);
    border: 1px solid var(--border-color, #ccc);
    border-radius: 3px;
    padding: 2px 4px;
}
```

- [ ] **Step 3: Update `NavMenu.razor` to load folders and use `CollectionNode`**

Add a private field in `@code`:

```csharp
private List<CollectionFolder> _allFolders = [];
```

In `ReloadAsync()`, after loading `_collections`, add:

```csharp
_allFolders = await db.CollectionFolders.ToListAsync();
```

Find the `<div class="request-list">` block inside the collection tree and replace the entire block (from `@if (_expanded.Contains(c.Id))`) with:

```razor
@if (_expanded.Contains(c.Id))
{
    <div class="request-list" data-drop="collection:@c.Id">
        <CollectionNode CollectionId="c.Id"
                        ParentFolderId="null"
                        AllFolders="_allFolders.Where(f => f.CollectionId == c.Id).ToList()"
                        AllRequests="c.Requests"
                        OnChanged="ReloadAndNotify" />
        @if (c.Requests.Count(r => r.FolderId == null) == 0 &&
             !_allFolders.Any(f => f.CollectionId == c.Id))
        {
            <div class="empty-hint">No requests — click + to add.</div>
        }
    </div>
}
```

In the collection header, add a "+ folder" button next to the existing "+" button:

```razor
<button class="icon-btn" title="New folder" @onclick="() => AddFolderToCollection(c.Id)">📁</button>
```

Add the method in `@code`:

```csharp
private async Task AddFolderToCollection(int collectionId)
{
    // Inline: we use the same pattern as NavMenu inline-add
    _newFolderCollectionId = collectionId;
    _newFolderName = string.Empty;
    _showAddFolder = true;
    _expanded.Add(collectionId);
    await InvokeAsync(StateHasChanged);
}

private int? _newFolderCollectionId;
private string _newFolderName = string.Empty;
private bool _showAddFolder;

private async Task CommitAddFolder()
{
    if (string.IsNullOrWhiteSpace(_newFolderName) || _newFolderCollectionId is null)
    {
        _showAddFolder = false;
        return;
    }
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionFolders.Add(new CollectionFolder
    {
        CollectionId = _newFolderCollectionId.Value,
        ParentFolderId = null,
        Name = _newFolderName.Trim()
    });
    await db.SaveChangesAsync();
    _showAddFolder = false;
    _newFolderCollectionId = null;
    _newFolderName = string.Empty;
    await ReloadAsync();
    State.NotifyChanged();
}
```

Add the inline input in the collection tree markup (right after the collection header div):

```razor
@if (_showAddFolder && _newFolderCollectionId == c.Id)
{
    <div class="inline-add">
        <input placeholder="Folder name" @bind="_newFolderName" @bind:event="oninput"
               @onkeydown="OnNewFolderKey" />
        <button @onclick="CommitAddFolder" disabled="@string.IsNullOrWhiteSpace(_newFolderName)">Add</button>
        <button class="link-btn" @onclick="() => _showAddFolder = false">cancel</button>
    </div>
}
```

Add handler:

```csharp
private async Task OnNewFolderKey(KeyboardEventArgs e)
{
    if (e.Key == "Enter") await CommitAddFolder();
    else if (e.Key == "Escape") _showAddFolder = false;
}
```

- [ ] **Step 4: Build and run**

```powershell
dotnet build HttpForge
```

Expected: no errors. Open the browser: collections render, existing requests appear at root level. Create a folder via 📁 button — it should appear in the tree. Expand it (click ▸). Press F2 to rename it.

- [ ] **Step 5: Commit**

```
git add HttpForge/Components/Layout/CollectionNode.razor HttpForge/Components/Layout/CollectionNode.razor.css HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: add CollectionNode recursive folder tree"
```

---

## Task 4: Folder CRUD verification

This task is a manual verification pass — the CRUD code is already in `CollectionNode` from Task 3. Run through each operation:

- [ ] **Step 1: Create a folder at collection root** — click 📁, type a name, Enter. Verify it appears.
- [ ] **Step 2: Create a sub-folder** — click "+" on the folder row, type a name, Enter. Expand parent, verify child appears.
- [ ] **Step 3: Rename via F2** — click the folder row (focus it), press F2. Verify input appears pre-filled. Edit, press Enter. Verify name updated.
- [ ] **Step 4: Rename cancel** — press F2, edit, press Escape. Verify name unchanged.
- [ ] **Step 5: Add request inside folder** — click ↵ on folder row. Verify new request appears inside the folder.
- [ ] **Step 6: Delete a folder with requests** — confirm dialog mentions request count. Verify folder and requests removed.
- [ ] **Step 7: Delete a folder with sub-folders** — confirm dialog mentions sub-folder count. Verify all deleted.
- [ ] **Step 8: Commit**

```
git commit -m "test: verify folder CRUD operations work correctly" --allow-empty
```

(This is an empty commit just to mark the verification checkpoint — skip if you prefer.)

---

## Task 5: Drag & drop JS + attributes

**Files:**
- Modify: `HttpForge/wwwroot/forge.js`

- [ ] **Step 1: Add `window.forge.dnd` to `forge.js`**

Append to the end of `forge.js`:

```js
window.forge.dnd = {
    _ref: null,
    _dragValue: null,
    _handlers: null,

    init(dotnetRef) {
        this._ref = dotnetRef;
        this._dragValue = null;

        const onDragStart = (e) => {
            const el = e.target.closest('[data-drag]');
            if (!el) return;
            this._dragValue = el.dataset.drag;
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', this._dragValue);
        };

        const onDragOver = (e) => {
            if (!this._dragValue) return;
            const el = e.target.closest('[data-drop]');
            if (!el) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            el.classList.add('drag-over');
        };

        const onDragLeave = (e) => {
            const el = e.target.closest('[data-drop]');
            if (el && !el.contains(e.relatedTarget)) el.classList.remove('drag-over');
        };

        const onDragEnd = () => {
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            this._dragValue = null;
        };

        const onDrop = (e) => {
            e.preventDefault();
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            const el = e.target.closest('[data-drop]');
            if (!el || !this._dragValue) return;
            const dropValue = el.dataset.drop;
            const drag = this._dragValue;
            this._dragValue = null;
            this._ref.invokeMethodAsync('OnDrop', drag, dropValue);
        };

        this._handlers = { onDragStart, onDragOver, onDragLeave, onDragEnd, onDrop };
        document.addEventListener('dragstart', onDragStart);
        document.addEventListener('dragover', onDragOver);
        document.addEventListener('dragleave', onDragLeave);
        document.addEventListener('dragend', onDragEnd);
        document.addEventListener('drop', onDrop);
    },

    dispose() {
        if (!this._handlers) return;
        const h = this._handlers;
        document.removeEventListener('dragstart', h.onDragStart);
        document.removeEventListener('dragover', h.onDragOver);
        document.removeEventListener('dragleave', h.onDragLeave);
        document.removeEventListener('dragend', h.onDragEnd);
        document.removeEventListener('drop', h.onDrop);
        this._handlers = null;
        this._ref = null;
        this._dragValue = null;
    }
};
```

Note: `RequestRow.razor` already has `draggable="true"` and `data-drag="request:@Request.Id"` from Task 2. `CollectionNode.razor` already has `draggable="true"`, `data-drag="folder:@folder.Id"`, and `data-drop="folder:@folder.Id"` on folder rows, and the collection root has `data-drop="collection:@c.Id"` in `NavMenu.razor` from Task 3.

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```

Expected: no errors.

- [ ] **Step 3: Commit**

```
git add HttpForge/wwwroot/forge.js
git commit -m "feat: add forge.dnd drag & drop JS handler"
```

---

## Task 6: OnDrop handler in NavMenu

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Add DotNetObjectReference field and init/dispose in `NavMenu.razor @code`**

Add field:

```csharp
private DotNetObjectReference<NavMenu>? _dndRef;
```

In `OnAfterRenderAsync` (add if not present, or extend if already there):

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _dndRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("forge.dnd.init", _dndRef);
    }
}
```

Extend `Dispose()`:

```csharp
public void Dispose()
{
    _disposed = true;
    State.OnChange -= OnStateChanged;
    _dndRef?.Dispose();
    // Note: forge.dnd.dispose() is called via JS below but we can't await in Dispose.
    // Leaking the JS listener on unmount is acceptable for a single-page app that never unmounts NavMenu.
}
```

- [ ] **Step 2: Add `[JSInvokable] OnDrop` method**

Add to `NavMenu.razor @code`:

```csharp
[JSInvokable]
public async Task OnDrop(string drag, string drop)
{
    if (!TryParse(drag, out var dragType, out var dragId)) return;
    if (!TryParse(drop, out var dropType, out var dropId)) return;

    // Resolve target collectionId and folderId
    int targetCollectionId;
    int? targetFolderId;

    if (dropType == "collection")
    {
        if (!_collections.Any(c => c.Id == dropId)) return;
        targetCollectionId = dropId;
        targetFolderId = null;
    }
    else // "folder"
    {
        var targetFolder = _allFolders.FirstOrDefault(f => f.Id == dropId);
        if (targetFolder == null) return;
        targetCollectionId = targetFolder.CollectionId;
        targetFolderId = dropId;
    }

    if (dragType == "request")
    {
        await MoveRequestAsync(dragId, targetCollectionId, targetFolderId);
    }
    else // "folder"
    {
        // Guard: cannot drop a folder into itself or any of its descendants
        if (dropType == "folder" && IsDescendantOrSelf(dragId, dropId)) return;
        // Guard: cannot drop a folder onto itself (same parent context)
        var dragged = _allFolders.FirstOrDefault(f => f.Id == dragId);
        if (dragged == null) return;
        if (dragged.ParentFolderId == targetFolderId && dragged.CollectionId == targetCollectionId) return;

        await MoveFolderAsync(dragId, targetCollectionId, targetFolderId);
    }

    await ReloadAsync();
    State.NotifyChanged();
    await InvokeAsync(StateHasChanged);
}

private static bool TryParse(string value, out string type, out int id)
{
    type = string.Empty; id = 0;
    var idx = value.IndexOf(':');
    if (idx < 1) return false;
    type = value[..idx];
    return int.TryParse(value[(idx + 1)..], out id);
}

private bool IsDescendantOrSelf(int folderId, int candidateId)
{
    if (folderId == candidateId) return true;
    var children = _allFolders.Where(f => f.ParentFolderId == folderId);
    return children.Any(c => IsDescendantOrSelf(c.Id, candidateId));
}

private async Task MoveRequestAsync(int requestId, int targetCollectionId, int? targetFolderId)
{
    using var db = await DbFactory.CreateDbContextAsync();
    var req = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId);
    if (req == null) return;
    req.CollectionId = targetCollectionId;
    req.FolderId = targetFolderId;
    req.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}

private async Task MoveFolderAsync(int folderId, int targetCollectionId, int? targetParentFolderId)
{
    using var db = await DbFactory.CreateDbContextAsync();
    var folder = await db.CollectionFolders.FirstOrDefaultAsync(f => f.Id == folderId);
    if (folder == null) return;

    var oldCollectionId = folder.CollectionId;
    folder.CollectionId = targetCollectionId;
    folder.ParentFolderId = targetParentFolderId;

    if (oldCollectionId != targetCollectionId)
        await UpdateDescendantsCollectionAsync(db, folderId, targetCollectionId);

    await db.SaveChangesAsync();
}

private async Task UpdateDescendantsCollectionAsync(AppDbContext db, int folderId, int targetCollectionId)
{
    var childFolders = await db.CollectionFolders
        .Where(f => f.ParentFolderId == folderId)
        .ToListAsync();
    foreach (var child in childFolders)
    {
        child.CollectionId = targetCollectionId;
        await UpdateDescendantsCollectionAsync(db, child.Id, targetCollectionId);
    }
    var childRequests = await db.Requests
        .Where(r => r.FolderId == folderId)
        .ToListAsync();
    foreach (var r in childRequests)
        r.CollectionId = targetCollectionId;
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build HttpForge
```

Expected: no errors.

- [ ] **Step 4: Manual verification of drag & drop**

Start the app: `dotnet run --project HttpForge`

Test these scenarios:
1. Drag a request to a folder in the same collection → request appears inside folder.
2. Drag a request to the collection root (drop on the empty area with `data-drop="collection:{id}"`) → request moves to root.
3. Drag a request to a different collection → request disappears from source, appears in target.
4. Drag a folder into another folder → folder becomes a child.
5. Drag a folder to a different collection root → folder moves; all its requests change collection.
6. Try dragging a folder into one of its own sub-folders → nothing happens (guard).
7. The currently-selected request stays selected after a move (the selected id is unchanged; the UI reloads and shows it in the new location).

- [ ] **Step 5: Commit**

```
git add HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: add drag & drop move for requests and folders"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered by |
|---|---|
| Create folder | Task 3 — `CommitAdd` via inline input |
| Rename folder (F2) | Task 3 — `OnFolderKeyDown` → `StartRename` |
| Delete folder + cascade | Task 3 — `DeleteFolder` with recursive ID collection |
| Move request via DnD | Task 5 (JS) + Task 6 (`MoveRequestAsync`) |
| Move folder via DnD | Task 5 (JS) + Task 6 (`MoveFolderAsync`) |
| Drop into different collection changes CollectionId | Task 6 — `MoveFolderAsync` + `UpdateDescendantsCollectionAsync` |
| Nested folders (arbitrary depth) | Task 3 — recursive `CollectionNode` |
| Guard: no circular nesting | Task 6 — `IsDescendantOrSelf` |
| Delete confirmation with counts | Task 3 — `CountDescendants` |
| Requests at root (FolderId = null) | Task 2 + Task 3 — filter `r.FolderId == null` |

**Placeholder scan:** No TBDs or vague steps found.

**Type consistency:** `CollectionFolder` defined in Task 1, used consistently in Tasks 3 and 6. `OnChanged` EventCallback used throughout Tasks 2, 3, 6. `_allFolders: List<CollectionFolder>` loaded in Task 3 and used in Task 6.
