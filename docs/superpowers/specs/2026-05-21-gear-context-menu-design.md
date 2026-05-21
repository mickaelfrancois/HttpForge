# Gear Context Menu — Design Spec

**Date:** 2026-05-21

## Problem

Each sidebar item (collection, folder, request) exposes multiple icon buttons inline, cluttering the layout. The goal is to replace them with a single ⚙ gear button that reveals a clean context menu.

## Behavior

- The ⚙ button is **hidden by default** and appears on **hover** of the row.
- Clicking ⚙ opens a dropdown context menu anchored to the right of the gear button.
- The menu **closes when the mouse leaves** the wrapper that contains both the gear button and the dropdown.
- Each menu item has a small leading icon and a label.

## Menu items per type

### Collection
| Icon | Label |
|------|-------|
| ⚙ | Variables de collection |
| 📁 | Nouveau dossier |
| ＋ | Nouvelle requête |
| 🗑 | Supprimer la collection |

### Dossier
| Icon | Label |
|------|-------|
| ＋ | Nouveau sous-dossier |
| ↵ | Nouvelle requête |
| ✏ | Renommer |
| 🗑 | Supprimer le dossier |

### Requête
| Icon | Label |
|------|-------|
| ⎘ | Dupliquer |
| ✏ | Renommer |
| ✕ | Supprimer |

## Architecture

### Approach: Pure Blazor state

Each component manages its own hover and open state:

- `_hoveredId` (or `_isHovered` for single-item components like `RequestRow`) — controls gear button visibility
- `_openMenuId` — controls which item's menu is open

The row wrapper uses:
```html
<div class="item-wrapper"
     @onmouseenter="() => _hoveredId = item.Id"
     @onmouseleave="() => { _hoveredId = null; _openMenuId = null; }">
```

The dropdown is `position: absolute; right: 0` inside a `position: relative` wrapper, with `z-index` above other elements.

### Rename for requests

`RequestRow.razor` gains inline rename, matching the existing folder rename pattern in `CollectionNode.razor`:
- `_renaming` bool + `_renameValue` string
- ✏ menu item triggers rename mode
- Input replaces the name span; Enter/blur commits, Escape cancels
- Persisted to DB via `DbFactory` and `OnChanged` callback

## Files affected

| File | Change |
|------|--------|
| `NavMenu.razor` | Replace 4 inline icon buttons on collection-header with gear + dropdown |
| `NavMenu.razor.css` | Add `.gear-wrap`, `.context-menu`, `.context-menu-item` styles |
| `CollectionNode.razor` | Replace 3 inline icon buttons on folder-row with gear + dropdown |
| `CollectionNode.razor.css` | Same context menu styles |
| `RequestRow.razor` | Replace 2 inline icon buttons with gear + dropdown; add rename |
| `RequestRow.razor.css` | Same context menu styles |

## CSS pattern

```css
.gear-wrap {
    position: relative;
}

.gear-btn {
    opacity: 0;
    transition: opacity 0.1s;
}

.item-wrapper:hover .gear-btn {
    opacity: 1;
}

.context-menu {
    position: absolute;
    right: 0;
    top: 100%;
    background: var(--bg-panel);
    border: 1px solid var(--border-main);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.15);
    min-width: 160px;
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
}

.context-menu-item:hover {
    background: var(--bg-hover);
}

.context-menu-item.danger {
    color: var(--accent-red, #e05252);
}
```

## Out of scope

- Keyboard navigation inside the menu (Tab/arrow keys)
- Nested submenus
- Touch/mobile support
