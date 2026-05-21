# Sidebar Collection Filter

**Date:** 2026-05-21  
**Status:** Approved

## Overview

A text filter input above the collection tree lets users quickly find collections, folders, and requests by name. Non-matching items are hidden; folders containing matches are auto-expanded.

---

## UI

A full-width `<input>` is placed between the "Collections" section title and the collection tree. It is always visible (no toggle). Attributes: `placeholder="Filter..."`, bound via `@oninput` for real-time updates. A ✕ button to the right clears the field.

When the field is empty the sidebar behaves exactly as today. When non-empty, filter mode is active.

---

## Data Flow

### NavMenu.razor

- Adds `private string _filterText = string.Empty;` field.
- Renders the filter input (see UI section).
- Adds helper `CollectionHasMatch(Collection c)`:
  - Returns true if `_filterText` is empty.
  - Returns true if `c.Name` contains the filter (case-insensitive).
  - Returns true if any request in `c.Requests` has a name that contains the filter.
  - Returns true if any folder in `_allFolders` with `CollectionId == c.Id` has a name that contains the filter.
- In the collection tree loop:
  - Skip (hide) collections where `!CollectionHasMatch(c)`.
  - Force-expand (always render children) when `IsFiltering && CollectionHasMatch(c)`, regardless of `_expanded`.
- Passes `FilterText="@_filterText"` to each `<CollectionNode>`.

### CollectionNode.razor

- Adds `[Parameter] public string FilterText { get; set; } = string.Empty;`
- Adds `private bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);`
- Adds helpers:
  ```csharp
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
  ```
- `DirectFolders` when filtering: only folders where `FolderMatches(f) || SubtreeHasMatch(f.Id)`.
- `DirectRequests` when filtering: only requests where `RequestMatchesFilter(r)`.
- Folder expansion when filtering: render children unconditionally if `FolderMatches(folder) || SubtreeHasMatch(folder.Id)`. Normal `_expanded` check applies when not filtering.
- Child `<CollectionNode>` receives `FilterText="@(FolderMatches(folder) ? string.Empty : FilterText)"`:  when the folder itself matched, its children are shown in full (filter cleared).

### RequestRow.razor

No changes. Visibility is controlled by `DirectRequests` in its parent `CollectionNode`.

---

## CSS

Add to `NavMenu.razor.css`:

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

---

## Out of Scope

- Highlight of matching text within names.
- Debouncing (real-time `@oninput` is acceptable given in-memory filtering).
- Persisting the filter across navigation or page reload.
