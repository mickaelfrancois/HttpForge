# Resizable Sidebar — Design Spec

**Date:** 2026-05-20  
**Status:** Approved

## Overview

Allow the user to resize the sidebar by dragging a vertical handle between the sidebar and the main content area. The chosen width is persisted across sessions via `localStorage`.

## Requirements

- Drag handle between sidebar (`<aside>`) and main content (`<main>`)
- Width constraints: min 200px, max 500px, default 320px
- Width persisted in `localStorage` under key `forge-sidebar-width`
- Width restored on page load before first paint (no layout flash)
- No collapse button
- Handle has a visible hover state consistent with the app theme

## Architecture

### Handle Element

A `<div class="forge-resize-handle">` is inserted in `MainLayout.razor` between `<aside class="forge-sidebar">` and `<main class="forge-main">`. It is a purely presentational element managed entirely by JS.

### JS Logic (`wwwroot/forge.js`)

Added under `window.forge.sidebar`:

```
forge.sidebar.init(sidebarEl, handleEl)
  - Reads localStorage('forge-sidebar-width'), applies it to sidebarEl.style.width
  - Attaches mousedown on handleEl
    - On mousedown: records startX, startWidth, attaches mousemove + mouseup on document
    - On mousemove: computes newWidth = startWidth + (e.clientX - startX), clamps to [200, 500], applies to sidebarEl.style.width
    - On mouseup: saves width to localStorage, detaches mousemove + mouseup
```

No Blazor state updates during drag. All width changes are direct DOM manipulations.

### Blazor Integration (`MainLayout.razor`)

`OnAfterRenderAsync(firstRender)` calls:
```csharp
await JS.InvokeVoidAsync("forge.sidebar.init",
    sidebarRef,   // ElementReference to <aside>
    handleRef);   // ElementReference to <div class="forge-resize-handle">
```

Two `@ref` attributes added to the layout elements.

### CSS (`MainLayout.razor.css`)

```
.forge-resize-handle
  width: 4px
  cursor: col-resize
  background: transparent
  transition: background 0.15s
  flex-shrink: 0

.forge-resize-handle:hover
  background: var(--accent-blue) at 20% opacity (rgba equivalent)
```

The `.forge-sidebar` CSS `width` (320px) remains the default fallback. The CSS `min-width: 240px` is removed and replaced by the JS clamp at 200px. If JS is unavailable the sidebar renders at its CSS default width.

## Data Flow

```
User drags handle
  → JS mousemove handler
    → sidebarEl.style.width = clampedWidth + 'px'   (instant, no Blazor)
  → User releases
    → localStorage.setItem('forge-sidebar-width', width)

Page load
  → forge.sidebar.init called
    → reads localStorage
    → applies width via inline style (on first render callback, may cause a brief 1-frame flash if stored width differs from CSS default 320px — acceptable)
```

## Files Changed

| File | Change |
|------|--------|
| `HttpForge/wwwroot/forge.js` | Add `window.forge.sidebar` object with `init` function |
| `HttpForge/Components/Layout/MainLayout.razor` | Add handle `<div>`, `@ref` attributes, `OnAfterRenderAsync` JSInterop call |
| `HttpForge/Components/Layout/MainLayout.razor.css` | Add `.forge-resize-handle` styles, adjust `.forge-sidebar` width |

## Out of Scope

- Touch/mobile resize support
- Keyboard-driven resize
- Collapse/hide sidebar
