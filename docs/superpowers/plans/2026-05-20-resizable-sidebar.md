# Resizable Sidebar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a drag-to-resize handle between the sidebar and main content, with width persisted in localStorage (min 200px, max 500px, default 320px).

**Architecture:** A 4px-wide `<div class="forge-resize-handle">` sits between `<aside>` and `<main>` in `MainLayout.razor`. All drag logic lives in `window.forge.sidebar.init()` in `forge.js` — no Blazor state updates during drag, only direct DOM manipulation. Blazor calls `init` once via `OnAfterRenderAsync`.

**Tech Stack:** Blazor (.NET 10), vanilla JS (`window.forge` namespace), CSS custom properties (`--accent-blue`), `localStorage`.

---

### Task 1: CSS — Handle styles and sidebar adjustment

**Files:**
- Modify: `HttpForge/Components/Layout/MainLayout.razor.css`

- [ ] **Step 1: Update `.forge-sidebar` — remove min-width (JS enforces the 200px minimum)**

  In `MainLayout.razor.css`, change the `.forge-sidebar` rule from:
  ```css
  .forge-sidebar {
      width: 320px;
      min-width: 240px;
      background: var(--bg-sidebar);
      border-right: 1px solid var(--border-main);
      display: flex;
      flex-direction: column;
      overflow: hidden;
  }
  ```
  To:
  ```css
  .forge-sidebar {
      width: 320px;
      background: var(--bg-sidebar);
      border-right: 1px solid var(--border-main);
      display: flex;
      flex-direction: column;
      overflow: hidden;
      flex-shrink: 0;
  }
  ```
  (`flex-shrink: 0` prevents flex from squishing the sidebar; `min-width` removed since JS clamps to 200px.)

- [ ] **Step 2: Add `.forge-resize-handle` and `.forge-resize-handle:hover` rules**

  Append to `MainLayout.razor.css`:
  ```css
  .forge-resize-handle {
      width: 4px;
      cursor: col-resize;
      background: transparent;
      transition: background 0.15s;
      flex-shrink: 0;
      user-select: none;
  }

  .forge-resize-handle:hover,
  .forge-resize-handle.dragging {
      background: rgba(14, 99, 156, 0.2);
  }
  ```
  (`rgba(14, 99, 156, 0.2)` is `--accent-blue: #0e639c` at 20% opacity.)

- [ ] **Step 3: Commit**
  ```bash
  git add HttpForge/Components/Layout/MainLayout.razor.css
  git commit -m "style: add forge-resize-handle CSS and adjust sidebar flex"
  ```

---

### Task 2: JS — `window.forge.sidebar.init`

**Files:**
- Modify: `HttpForge/wwwroot/forge.js`

- [ ] **Step 1: Add `window.forge.sidebar` at the end of `forge.js` (before the closing of the file)**

  Append this block at the end of `HttpForge/wwwroot/forge.js`:
  ```javascript
  window.forge.sidebar = {
      init(sidebarEl, handleEl) {
          const stored = localStorage.getItem('forge-sidebar-width');
          if (stored) sidebarEl.style.width = stored + 'px';

          let startX = 0, startWidth = 0;

          const onMouseMove = (e) => {
              const newWidth = Math.min(500, Math.max(200, startWidth + (e.clientX - startX)));
              sidebarEl.style.width = newWidth + 'px';
          };

          const onMouseUp = () => {
              const w = parseInt(sidebarEl.style.width, 10);
              localStorage.setItem('forge-sidebar-width', w);
              handleEl.classList.remove('dragging');
              document.removeEventListener('mousemove', onMouseMove);
              document.removeEventListener('mouseup', onMouseUp);
              document.body.style.cursor = '';
              document.body.style.userSelect = '';
          };

          handleEl.addEventListener('mousedown', (e) => {
              e.preventDefault();
              startX = e.clientX;
              startWidth = sidebarEl.offsetWidth;
              handleEl.classList.add('dragging');
              document.body.style.cursor = 'col-resize';
              document.body.style.userSelect = 'none';
              document.addEventListener('mousemove', onMouseMove);
              document.addEventListener('mouseup', onMouseUp);
          });
      }
  };
  ```

- [ ] **Step 2: Verify the `window.forge` namespace is not broken**

  Confirm the file still starts with `window.forge = window.forge || {};` and that the new block is outside (after) all existing methods.

- [ ] **Step 3: Commit**
  ```bash
  git add HttpForge/wwwroot/forge.js
  git commit -m "feat: add forge.sidebar.init for drag-to-resize"
  ```

---

### Task 3: Blazor — Wire the handle into MainLayout

**Files:**
- Modify: `HttpForge/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Replace the entire content of `MainLayout.razor` with the following**

  ```razor
  @inherits LayoutComponentBase
  @inject IJSRuntime JS

  <ThemeToggle />
  <div class="forge-shell">
      <aside class="forge-sidebar" @ref="_sidebarRef">
          <NavMenu />
      </aside>
      <div class="forge-resize-handle" @ref="_handleRef"></div>
      <main class="forge-main">
          @Body
      </main>
  </div>

  <div id="blazor-error-ui" data-nosnippet>
      An unhandled error has occurred.
      <a href="." class="reload">Reload</a>
      <span class="dismiss">🗙</span>
  </div>

  @code {
      private ElementReference _sidebarRef;
      private ElementReference _handleRef;

      protected override async Task OnAfterRenderAsync(bool firstRender)
      {
          if (firstRender)
              await JS.InvokeVoidAsync("forge.sidebar.init", _sidebarRef, _handleRef);
      }
  }
  ```

- [ ] **Step 2: Commit**
  ```bash
  git add HttpForge/Components/Layout/MainLayout.razor
  git commit -m "feat: wire resize handle in MainLayout with JSInterop"
  ```

---

### Task 4: Manual smoke test

**No automated tests exist for Blazor UI in this repo. Verify manually.**

- [ ] **Step 1: Build and run**
  ```bash
  dotnet run --project HttpForge/HttpForge.csproj
  ```
  Expected: app starts without build errors.

- [ ] **Step 2: Verify initial state**

  Open the app in a browser. The sidebar should appear at its default width (320px, or the stored width if `forge-sidebar-width` is already in localStorage).

- [ ] **Step 3: Verify hover**

  Hover over the 4px strip between the sidebar and the main panel. The cursor should change to `col-resize` and a faint blue tint should appear on the handle.

- [ ] **Step 4: Verify drag**

  Click and drag the handle left and right. The sidebar should resize fluidly without any Blazor re-render lag. Dragging past the limits (200px left, 500px right) should stop at the boundary.

- [ ] **Step 5: Verify persistence**

  Resize the sidebar, then reload the page (F5). The sidebar should reappear at the same width.

- [ ] **Step 6: Verify body cursor resets**

  Start a drag, move the mouse quickly outside the browser window, then release. Bring the mouse back — the cursor should be back to the default (not stuck on `col-resize`) and no ghost drag should be active.
