# Gear Context Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the multiple inline icon buttons on collections, folders, and requests with a single ⚙ gear button that reveals a context menu on click, visible only on hover.

**Architecture:** Pure Blazor state — each component tracks `_hoveredId` and `_openMenuId` fields. The gear button appears via CSS opacity when the row wrapper is hovered (`@onmouseenter`/`@onmouseleave`). The dropdown is `position: absolute` inside a `position: relative` gear wrapper, closed when the mouse leaves the row.

**Tech Stack:** Blazor Server (.NET 10), scoped CSS per component, no JS required.

---

## File Map

| File | Change |
|------|--------|
| `HttpForge/Components/Layout/NavMenu.razor` | Replace 4 inline action buttons on collection-header with gear + dropdown |
| `HttpForge/Components/Layout/NavMenu.razor.css` | Add `.gear-wrap`, `.context-menu`, `.context-menu-item` styles |
| `HttpForge/Components/Layout/CollectionNode.razor` | Replace 3 inline action buttons on folder-row with gear + dropdown |
| `HttpForge/Components/Layout/CollectionNode.razor.css` | Add same context menu styles |
| `HttpForge/Components/Layout/RequestRow.razor` | Replace 2 inline action buttons with gear + dropdown; add inline rename |
| `HttpForge/Components/Layout/RequestRow.razor.css` | Add same context menu styles + `.request-rename-input` |

---

## Task 1: Collection gear menu — NavMenu.razor

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor` (lines 128–145 and `@code` block)
- Modify: `HttpForge/Components/Layout/NavMenu.razor.css`

- [ ] **Step 1: Add context menu CSS to NavMenu.razor.css**

Append at the end of `HttpForge/Components/Layout/NavMenu.razor.css`:

```css
.gear-wrap {
    position: relative;
    display: flex;
    align-items: center;
}

.gear-btn {
    opacity: 0;
    pointer-events: none;
    transition: opacity 0.1s;
}

.gear-wrap.visible .gear-btn {
    opacity: 1;
    pointer-events: auto;
}

.context-menu {
    position: absolute;
    right: 0;
    top: 100%;
    background: var(--bg-panel);
    border: 1px solid var(--border-main);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.18);
    min-width: 170px;
    z-index: 100;
    padding: 0.25rem 0;
}

.context-menu-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0.75rem;
    font-size: 0.82rem;
    cursor: pointer;
    color: var(--text-primary);
    white-space: nowrap;
    width: 100%;
    text-align: left;
    background: none;
    border: none;
    border-radius: 0;
}

.context-menu-item:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
}

.context-menu-item:hover:not(:disabled) {
    background: var(--bg-hover);
}

.context-menu-item.danger {
    color: var(--accent-red, #e05252);
}

.context-menu-item.danger:hover {
    color: var(--accent-red, #e05252);
}
```

- [ ] **Step 2: Add state fields to NavMenu @code block**

In `HttpForge/Components/Layout/NavMenu.razor`, in the `@code` block, add these two fields after the existing `private bool _disposed;` line:

```csharp
private int? _hoveredCollectionId;
private int? _openCollectionMenuId;
```

- [ ] **Step 3: Replace collection-header buttons with gear + dropdown**

In `HttpForge/Components/Layout/NavMenu.razor`, replace the `<div class="collection-header">` block (lines 128–145) with:

```razor
<div class="collection-header"
     @onmouseenter="() => _hoveredCollectionId = c.Id"
     @onmouseleave="() => { _hoveredCollectionId = null; _openCollectionMenuId = null; }">
    <button class="collection-toggle" @onclick="() => ToggleCollection(c.Id)">
        <span class="caret">@(_expanded.Contains(c.Id) ? "▾" : "▸")</span>
        <span>@c.Name</span>
        @{
            var activeBadgeSet = c.VariableSets.FirstOrDefault(s => s.Id == c.ActiveCollectionVariableSetId);
        }
        @if (activeBadgeSet is not null)
        {
            <span class="subset-badge">@activeBadgeSet.Name</span>
        }
    </button>
    <div class="gear-wrap @(_hoveredCollectionId == c.Id ? "visible" : "")">
        <button class="icon-btn gear-btn"
                title="Actions"
                @onclick:stopPropagation
                @onclick="() => _openCollectionMenuId = (_openCollectionMenuId == c.Id ? (int?)null : c.Id)">
            ⚙
        </button>
        @if (_openCollectionMenuId == c.Id)
        {
            <div class="context-menu">
                <button class="context-menu-item @(_collVarEditorOpen.Contains(c.Id) ? "active" : "")"
                        @onclick:stopPropagation
                        @onclick="() => { ToggleCollVarEditor(c.Id); _openCollectionMenuId = null; }">
                    <span>⚙</span> Variables de collection
                </button>
                <button class="context-menu-item"
                        @onclick:stopPropagation
                        @onclick="() => { StartAddFolder(c.Id); _openCollectionMenuId = null; }">
                    <span>📁</span> Nouveau dossier
                </button>
                <button class="context-menu-item"
                        @onclick:stopPropagation
                        @onclick="() => { _ = AddRequest(c); _openCollectionMenuId = null; }">
                    <span>＋</span> Nouvelle requête
                </button>
                <button class="context-menu-item danger"
                        @onclick:stopPropagation
                        @onclick="() => { _ = DeleteCollection(c); _openCollectionMenuId = null; }">
                    <span>🗑</span> Supprimer la collection
                </button>
            </div>
        }
    </div>
</div>
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add HttpForge/Components/Layout/NavMenu.razor HttpForge/Components/Layout/NavMenu.razor.css
git commit -m "feat: replace collection action buttons with gear context menu"
```

---

## Task 2: Folder gear menu — CollectionNode.razor

**Files:**
- Modify: `HttpForge/Components/Layout/CollectionNode.razor` (folder-row section, `@code` block)
- Modify: `HttpForge/Components/Layout/CollectionNode.razor.css`

- [ ] **Step 1: Add context menu CSS to CollectionNode.razor.css**

Append at the end of `HttpForge/Components/Layout/CollectionNode.razor.css`:

```css
.gear-wrap {
    position: relative;
    display: flex;
    align-items: center;
}

.gear-btn {
    opacity: 0;
    pointer-events: none;
    transition: opacity 0.1s;
}

.gear-wrap.visible .gear-btn {
    opacity: 1;
    pointer-events: auto;
}

.context-menu {
    position: absolute;
    right: 0;
    top: 100%;
    background: var(--bg-panel);
    border: 1px solid var(--border-main);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.18);
    min-width: 170px;
    z-index: 100;
    padding: 0.25rem 0;
}

.context-menu-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0.75rem;
    font-size: 0.82rem;
    cursor: pointer;
    color: var(--text-primary);
    white-space: nowrap;
    width: 100%;
    text-align: left;
    background: none;
    border: none;
    border-radius: 0;
}

.context-menu-item:hover {
    background: var(--hover-bg, rgba(0,0,0,0.06));
    color: var(--text-primary);
}

.context-menu-item.danger {
    color: var(--accent-red, #e05252);
}

.context-menu-item.danger:hover {
    color: var(--accent-red, #e05252);
}
```

- [ ] **Step 2: Add state fields to CollectionNode @code block**

In `HttpForge/Components/Layout/CollectionNode.razor`, in the `@code` block, add after the existing `private bool _shouldFocusRename;` line:

```csharp
private int? _hoveredFolderId;
private int? _openFolderMenuId;
```

- [ ] **Step 3: Replace folder-row buttons with gear + dropdown**

In `HttpForge/Components/Layout/CollectionNode.razor`, replace the `<div class="folder-row" ...>` block (lines 14–39) with:

```razor
<div class="folder-row"
     tabindex="0"
     draggable="true"
     data-drag="folder:@folder.Id"
     data-drop="folder:@folder.Id"
     @onkeydown="e => OnFolderKeyDown(folder, e)"
     @onmouseenter="() => _hoveredFolderId = folder.Id"
     @onmouseleave="() => { _hoveredFolderId = null; _openFolderMenuId = null; }">
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
        <span class="folder-name" @ondblclick="() => ToggleExpand(folder.Id)" @ondblclick:stopPropagation>
            <span class="folder-icon">@(_expanded.Contains(folder.Id) ? "📂" : "📁")</span>@folder.Name
        </span>
        <div class="gear-wrap @(_hoveredFolderId == folder.Id ? "visible" : "")">
            <button class="icon-btn gear-btn"
                    title="Actions"
                    @onclick:stopPropagation
                    @onclick="() => _openFolderMenuId = (_openFolderMenuId == folder.Id ? (int?)null : folder.Id)">
                ⚙
            </button>
            @if (_openFolderMenuId == folder.Id)
            {
                <div class="context-menu">
                    <button class="context-menu-item"
                            @onclick:stopPropagation
                            @onclick="() => { StartAddFolder(folder.Id); _openFolderMenuId = null; }">
                        <span>＋</span> Nouveau sous-dossier
                    </button>
                    <button class="context-menu-item"
                            @onclick:stopPropagation
                            @onclick="() => { _ = AddRequest(folder); _openFolderMenuId = null; }">
                        <span>↵</span> Nouvelle requête
                    </button>
                    <button class="context-menu-item"
                            @onclick:stopPropagation
                            @onclick="() => { StartRename(folder); _openFolderMenuId = null; }">
                        <span>✏</span> Renommer
                    </button>
                    <button class="context-menu-item danger"
                            @onclick:stopPropagation
                            @onclick="() => { _ = DeleteFolder(folder); _openFolderMenuId = null; }">
                        <span>🗑</span> Supprimer le dossier
                    </button>
                </div>
            }
        </div>
    }
</div>
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add HttpForge/Components/Layout/CollectionNode.razor HttpForge/Components/Layout/CollectionNode.razor.css
git commit -m "feat: replace folder action buttons with gear context menu"
```

---

## Task 3: Request gear menu + rename — RequestRow.razor

**Files:**
- Modify: `HttpForge/Components/Layout/RequestRow.razor`
- Modify: `HttpForge/Components/Layout/RequestRow.razor.css`

- [ ] **Step 1: Add context menu + rename CSS to RequestRow.razor.css**

Append at the end of `HttpForge/Components/Layout/RequestRow.razor.css`:

```css
.gear-wrap {
    position: relative;
    display: flex;
    align-items: center;
}

.gear-btn {
    opacity: 0;
    pointer-events: none;
    transition: opacity 0.1s;
}

.gear-wrap.visible .gear-btn {
    opacity: 1;
    pointer-events: auto;
}

.context-menu {
    position: absolute;
    right: 0;
    top: 100%;
    background: var(--bg-panel);
    border: 1px solid var(--border-main);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.18);
    min-width: 150px;
    z-index: 100;
    padding: 0.25rem 0;
}

.context-menu-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0.75rem;
    font-size: 0.82rem;
    cursor: pointer;
    color: var(--text-primary);
    white-space: nowrap;
    width: 100%;
    text-align: left;
    background: none;
    border: none;
    border-radius: 0;
}

.context-menu-item:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
}

.context-menu-item.danger {
    color: var(--accent-red, #e05252);
}

.context-menu-item.danger:hover {
    color: var(--accent-red, #e05252);
}

.request-rename-input {
    flex: 1;
    font-size: 0.82rem;
    background: var(--bg-input);
    border: 1px solid var(--border-input);
    color: var(--text-primary);
    border-radius: 3px;
    padding: 1px 4px;
    min-width: 0;
}
```

- [ ] **Step 2: Replace RequestRow.razor template and @code**

Replace the entire content of `HttpForge/Components/Layout/RequestRow.razor` with:

```razor
@* HttpForge/Components/Layout/RequestRow.razor *@
@rendermode InteractiveServer
@inject IDbContextFactory<AppDbContext> DbFactory
@inject AppState State
@inject IJSRuntime JS

<div class="request-row @(State.SelectedRequestId == Request.Id ? "selected" : "")"
     draggable="true"
     data-drag="request:@Request.Id"
     @onclick="Select"
     @onmouseenter="() => _isHovered = true"
     @onmouseleave="() => { _isHovered = false; _menuOpen = false; }">
    <span class="method method-@Request.Method.ToString().ToLower()">@Request.Method</span>
    @if (_renaming)
    {
        <input class="request-rename-input"
               @ref="_renameInput"
               value="@_renameValue"
               @oninput="e => _renameValue = e.Value?.ToString() ?? string.Empty"
               @onblur="CommitRename"
               @onkeydown="OnRenameKeyDown"
               @onclick:stopPropagation />
    }
    else
    {
        <span class="request-name">@(string.IsNullOrWhiteSpace(Request.Name) ? "Untitled" : Request.Name)</span>
        <div class="gear-wrap @(_isHovered ? "visible" : "")">
            <button class="icon-btn gear-btn"
                    title="Actions"
                    @onclick:stopPropagation
                    @onclick="() => _menuOpen = !_menuOpen">
                ⚙
            </button>
            @if (_menuOpen)
            {
                <div class="context-menu">
                    <button class="context-menu-item"
                            @onclick:stopPropagation
                            @onclick="StartRename">
                        <span>✏</span> Renommer
                    </button>
                    <button class="context-menu-item"
                            @onclick:stopPropagation
                            @onclick="Duplicate">
                        <span>⎘</span> Dupliquer
                    </button>
                    <button class="context-menu-item danger"
                            @onclick:stopPropagation
                            @onclick="Delete">
                        <span>✕</span> Supprimer
                    </button>
                </div>
            }
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public HttpRequestItem Request { get; set; } = null!;
    [Parameter] public EventCallback<int> OnChanged { get; set; }

    private bool _isHovered;
    private bool _menuOpen;
    private bool _renaming;
    private string _renameValue = string.Empty;
    private bool _shouldFocusRename;
    private ElementReference _renameInput;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusRename && _renaming)
        {
            _shouldFocusRename = false;
            try { await _renameInput.FocusAsync(); } catch { }
        }
    }

    private void Select()
    {
        State.SelectedRequestId = Request.Id;
        State.NotifyChanged();
    }

    private void StartRename()
    {
        _renameValue = Request.Name;
        _renaming = true;
        _menuOpen = false;
        _shouldFocusRename = true;
        StateHasChanged();
    }

    private async Task CommitRename()
    {
        if (!_renaming) return;
        _renaming = false;
        var name = _renameValue.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == Request.Name) return;
        using var db = await DbFactory.CreateDbContextAsync();
        var r = await db.Requests.FirstAsync(x => x.Id == Request.Id);
        r.Name = name;
        r.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await OnChanged.InvokeAsync(Request.CollectionId);
    }

    private async Task OnRenameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await CommitRename();
        else if (e.Key == "Escape") _renaming = false;
    }

    private async Task Duplicate()
    {
        _menuOpen = false;
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
        await OnChanged.InvokeAsync(copy.CollectionId);
    }

    private async Task Delete()
    {
        _menuOpen = false;
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
        await OnChanged.InvokeAsync(Request.CollectionId);
    }
}
```

- [ ] **Step 3: Build and verify**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add HttpForge/Components/Layout/RequestRow.razor HttpForge/Components/Layout/RequestRow.razor.css
git commit -m "feat: replace request action buttons with gear context menu, add rename"
```
