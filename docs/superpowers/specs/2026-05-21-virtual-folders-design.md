# Virtual Folders in Collections

**Date:** 2026-05-21  
**Status:** Approved

## Overview

Allow users to organize HTTP requests inside a collection using nested virtual folders. Folders can be created, renamed (F2), deleted, and reordered via drag & drop. Requests can be moved between folders and collections by drag & drop. Deleting a folder deletes all its contents recursively.

---

## Data Model

### New entity: `CollectionFolder`

| Column           | Type    | Notes                                              |
|------------------|---------|----------------------------------------------------|
| Id               | int PK  |                                                    |
| CollectionId     | int FK  | → Collections, cascade delete                      |
| ParentFolderId   | int? FK | → CollectionFolders (self-ref), cascade delete     |
| Name             | string  |                                                    |

`CollectionId` is denormalized on every folder regardless of nesting depth, to simplify queries (avoids recursive joins to find the root collection).

### Modified entity: `HttpRequestItem`

| Column   | Type   | Notes                                           |
|----------|--------|-------------------------------------------------|
| FolderId | int?   | → CollectionFolders, SET NULL on folder delete  |

`FolderId = null` means the request sits at the collection root.

### Schema migration

`SchemaUpgrader.Apply` gains:
- `EnsureTable("CollectionFolders", ...)` — creates the table with the FK constraints above
- `EnsureColumn("Requests", "FolderId", "INTEGER NULL")` — adds the column to existing DBs

### Cascade behavior

- Deleting a collection → cascade deletes all its folders (via `CollectionId` FK) and all its requests (existing behavior).
- Deleting a folder → cascade deletes all child folders (via `ParentFolderId` FK). Requests directly in the deleted folder are also deleted (cascade via `FolderId` FK — configured as `DeleteBehavior.Cascade` in EF, not SET NULL, since a request inside a deleted folder is orphaned).
- Moving a folder to another collection → update `CollectionId` on the folder and all its descendants (folders + requests) in a single DB transaction.

---

## Components

`NavMenu.razor` is refactored into three files:

### `CollectionNode.razor`

Recursive Blazor component. Accepts a `nodeId` / `nodeType` (collection root or folder) and renders:
- Expand/collapse toggle
- Folder name (read-only by default; becomes an `<input>` when renaming)
- Buttons: `[+folder]` `[+request]` `[🗑]`
- Its child folders (via `CollectionNode` recursion)
- Its direct requests (via `RequestRow`)

Rename flow:
1. User focuses the folder row (keyboard or click) and presses **F2**
2. Name text swaps to an `<input>` pre-filled with the current name
3. **Enter** or **blur** → save to DB
4. **Escape** → cancel, restore original name

### `RequestRow.razor`

Extracted from the current inline markup in `NavMenu`. Renders one request row with method badge, name, duplicate button, and delete button. Carries `draggable="true"` and `data-drag="request:{id}"`.

### `NavMenu.razor`

Retains: data loading, global environment management, Insomnia import, `[JSInvokable] OnDrop`. Renders the list of collections and delegates tree rendering to `CollectionNode`.

---

## Drag & Drop

### JS side — `window.forge.dnd`

Two functions added to `forge.js`:

```js
forge.dnd.init(dotnetRef)   // attaches delegated listeners on document
forge.dnd.dispose()         // removes listeners
```

Event delegation on `document`:
- `dragstart`: reads `data-drag` from the dragged element, stores it.
- `dragover`: finds the nearest ancestor with `data-drop`, adds `.drag-over` CSS class, calls `preventDefault()` to allow drop.
- `dragleave` / `drop`: removes `.drag-over`.
- `drop`: calls `dotnetRef.invokeMethodAsync('OnDrop', dragValue, dropValue)`.

Draggable elements carry `draggable="true"` and `data-drag="request:{id}"` or `data-drag="folder:{id}"`.  
Drop targets carry `data-drop="collection:{id}"` or `data-drop="folder:{id}"`.

### Blazor side — `[JSInvokable] OnDrop`

```
OnDrop(string drag, string drop)
```

Logic:
1. Parse drag/drop values to extract type + id.
2. Resolve target `CollectionId` and `FolderId` (null when dropped on collection root).
3. **Guard**: if dragging a folder, verify the drop target is not a descendant of that folder (prevent circular nesting). If violated, do nothing.
4. If moving a folder cross-collection: update `CollectionId` on the folder and all its descendant folders and requests in a single transaction.
5. Update the dragged item's `CollectionId` + `FolderId` (or `ParentFolderId` for folders).
6. Save to DB, call `State.NotifyChanged()`.

---

## Delete confirmation

`confirm()` message includes the recursive count:  
> *Delete folder "Auth" and its 3 request(s) and 1 sub-folder(s)?*

Count is computed before showing the dialog by traversing the in-memory tree already loaded in `NavMenu`.

---

## Out of scope

- Reordering items within the same folder (items remain sorted by `UpdatedAt` descending).
- Drag & drop on mobile / touch events.
- Folder import from Insomnia (Insomnia folders already flatten to requests on import; this is unchanged).
