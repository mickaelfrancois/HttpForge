# JSON Syntax Highlighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CodeMirror JSON syntax highlighting to the response body display (read-only) and the JSON request body editor (editable).

**Architecture:** Two new Blazor components — `JsonViewer` (read-only) and `JsonBodyEditor` (editable) — both backed by CodeMirror 5. A new `forge.viewer` JS namespace handles the read-only viewer lifecycle. `forge.editor.init` gains optional `mode` and `hasBlur` parameters. `Home.razor` swaps the `<pre>` tag and the `VariableInput` (JSON mode) for the new components.

**Tech Stack:** Blazor Server .NET 10, CodeMirror 5.65.16, C#, scoped CSS

---

## File Map

| Action | File | Change |
|--------|------|--------|
| Modify | `HttpForge/Components/App.razor` | Add JSON mode CDN script tag |
| Modify | `HttpForge/wwwroot/forge.js` | Add `forge.viewer`; extend `forge.editor.init` with `mode`/`hasBlur` params |
| Create | `HttpForge/Components/Pages/JsonViewer.razor` | Read-only CodeMirror component |
| Create | `HttpForge/Components/Pages/JsonViewer.razor.css` | Scoped styles for viewer |
| Create | `HttpForge/Components/Pages/JsonBodyEditor.razor` | Editable CodeMirror component for JSON body |
| Create | `HttpForge/Components/Pages/JsonBodyEditor.razor.css` | Scoped styles for editor |
| Modify | `HttpForge/Components/Pages/Home.razor` | Replace `<pre>` and JSON `VariableInput` with new components |

---

### Task 1: JS layer — add JSON mode CDN + forge.viewer + extend forge.editor.init

**Files:**
- Modify: `HttpForge/Components/App.razor:27`
- Modify: `HttpForge/wwwroot/forge.js:155-189`

**Context:**

`App.razor` currently loads CodeMirror and the javascript mode (lines 26-27):
```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/javascript/javascript.min.js"></script>
<script src="@Assets["forge.js"]"></script>
```

`forge.editor` currently (lines 155-189 of `forge.js`):
```js
window.forge.editor = {
    _instances: new Map(),

    init(textareaEl, dotnetRef, initialValue) {
        if (this._instances.has(textareaEl)) return;
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark'
            || (!document.documentElement.getAttribute('data-theme')
                && window.matchMedia('(prefers-color-scheme: dark)').matches);
        const cm = CodeMirror.fromTextArea(textareaEl, {
            mode: 'javascript',
            theme: isDark ? 'monokai' : 'default',
            lineNumbers: true,
            tabSize: 2,
            indentWithTabs: false,
            lineWrapping: false,
            autofocus: false
        });
        cm.setValue(initialValue || '');
        let _changeTimer = null;
        cm.on('change', () => {
            clearTimeout(_changeTimer);
            _changeTimer = setTimeout(() => {
                dotnetRef.invokeMethodAsync('OnScriptChanged', cm.getValue());
            }, 300);
        });
        this._instances.set(textareaEl, cm);
    },

    dispose(textareaEl) {
        const cm = this._instances.get(textareaEl);
        if (!cm) return;
        cm.toTextArea();
        this._instances.delete(textareaEl);
    }
};
```

- [ ] **Step 1: Add JSON mode CDN script in `App.razor`**

In `HttpForge/Components/App.razor`, find the two existing script tags and replace them with three (add the json mode line between the javascript mode and forge.js):

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/javascript/javascript.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/json/json.min.js"></script>
<script src="@Assets["forge.js"]"></script>
```

- [ ] **Step 2: Extend `forge.editor.init` with `mode` and `hasBlur` parameters**

In `HttpForge/wwwroot/forge.js`, replace the entire `window.forge.editor` block (lines 155-189) with:

```js
window.forge.editor = {
    _instances: new Map(),

    init(textareaEl, dotnetRef, initialValue, mode = 'javascript', hasBlur = false) {
        if (this._instances.has(textareaEl)) return;
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark'
            || (!document.documentElement.getAttribute('data-theme')
                && window.matchMedia('(prefers-color-scheme: dark)').matches);
        const cm = CodeMirror.fromTextArea(textareaEl, {
            mode: mode,
            theme: isDark ? 'monokai' : 'default',
            lineNumbers: true,
            tabSize: 2,
            indentWithTabs: false,
            lineWrapping: false,
            autofocus: false
        });
        cm.setValue(initialValue || '');
        let _changeTimer = null;
        cm.on('change', () => {
            clearTimeout(_changeTimer);
            _changeTimer = setTimeout(() => {
                dotnetRef.invokeMethodAsync('OnScriptChanged', cm.getValue());
            }, 300);
        });
        if (hasBlur) {
            cm.on('blur', () => {
                dotnetRef.invokeMethodAsync('OnEditorBlur');
            });
        }
        this._instances.set(textareaEl, cm);
    },

    dispose(textareaEl) {
        const cm = this._instances.get(textareaEl);
        if (!cm) return;
        cm.toTextArea();
        this._instances.delete(textareaEl);
    }
};
```

- [ ] **Step 3: Add `forge.viewer` after the `forge.editor` block**

In `HttpForge/wwwroot/forge.js`, after the closing `};` of `window.forge.editor` (before `window.forge.dnd`), insert:

```js
window.forge.viewer = {
    _instances: new Map(),

    init(el, value) {
        if (this._instances.has(el)) return;
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark'
            || (!document.documentElement.getAttribute('data-theme')
                && window.matchMedia('(prefers-color-scheme: dark)').matches);
        const cm = CodeMirror(el, {
            value: value || '',
            mode: 'application/json',
            theme: isDark ? 'monokai' : 'default',
            lineNumbers: true,
            readOnly: true,
            lineWrapping: false
        });
        this._instances.set(el, cm);
    },

    setValue(el, value) {
        const cm = this._instances.get(el);
        if (cm) cm.setValue(value || '');
    },

    dispose(el) {
        const cm = this._instances.get(el);
        if (!cm) return;
        cm.getWrapperElement().remove();
        this._instances.delete(el);
    }
};
```

- [ ] **Step 4: Build to verify no JS/Blazor compile errors**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add HttpForge/Components/App.razor HttpForge/wwwroot/forge.js
git commit -m "feat: add JSON mode CDN, forge.viewer, extend forge.editor with mode/hasBlur"
```

---

### Task 2: Create JsonViewer component

**Files:**
- Create: `HttpForge/Components/Pages/JsonViewer.razor`
- Create: `HttpForge/Components/Pages/JsonViewer.razor.css`

**Context:**

This component wraps a read-only CodeMirror instance. It uses a `<div>` container (not a textarea) because `CodeMirror(el, options)` creates the editor inside the div. The `_lastValue` field prevents redundant JS calls when Blazor re-renders without a value change.

`ScriptEditor.razor` (at `HttpForge/Components/Pages/ScriptEditor.razor`) shows the established pattern for Blazor ↔ CodeMirror lifecycle:
- `@ref` to get the element reference
- `OnAfterRenderAsync(bool firstRender)` to initialize
- `IAsyncDisposable` to clean up

- [ ] **Step 1: Create `JsonViewer.razor`**

Create `HttpForge/Components/Pages/JsonViewer.razor` with this content:

```razor
@rendermode InteractiveServer
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="json-viewer-wrap" @ref="_containerRef"></div>

@code {
    [Parameter] public string Value { get; set; } = string.Empty;

    private ElementReference _containerRef;
    private string _lastValue = string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _lastValue = Value;
            await JS.InvokeVoidAsync("forge.viewer.init", _containerRef, Value);
        }
        else if (Value != _lastValue)
        {
            _lastValue = Value;
            await JS.InvokeVoidAsync("forge.viewer.setValue", _containerRef, Value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("forge.viewer.dispose", _containerRef); } catch { }
    }
}
```

- [ ] **Step 2: Create `JsonViewer.razor.css`**

Create `HttpForge/Components/Pages/JsonViewer.razor.css` with:

```css
.json-viewer-wrap {
    width: 100%;
}

.json-viewer-wrap ::deep .CodeMirror {
    height: auto;
    max-height: 400px;
    font-size: 0.85rem;
    font-family: 'Consolas', 'Menlo', monospace;
}
```

`::deep` is the Blazor scoped CSS combinator that applies styles to dynamically-injected children (the CodeMirror DOM isn't created by Blazor and doesn't carry the scoped attribute).

- [ ] **Step 3: Build**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add HttpForge/Components/Pages/JsonViewer.razor HttpForge/Components/Pages/JsonViewer.razor.css
git commit -m "feat: add JsonViewer read-only CodeMirror component"
```

---

### Task 3: Create JsonBodyEditor component

**Files:**
- Create: `HttpForge/Components/Pages/JsonBodyEditor.razor`
- Create: `HttpForge/Components/Pages/JsonBodyEditor.razor.css`

**Context:**

Pattern is identical to `ScriptEditor.razor` but with two differences:
1. `forge.editor.init` is called with `mode = "application/json"` and `hasBlur = true`
2. An additional `[JSInvokable] OnEditorBlur()` method fires the `OnBlur` callback

The callback name `OnScriptChanged` is reused (it's just an internal JS→C# bridge name; the `dotnetRef` is instance-specific so there's no conflict with `ScriptEditor`).

- [ ] **Step 1: Create `JsonBodyEditor.razor`**

Create `HttpForge/Components/Pages/JsonBodyEditor.razor` with:

```razor
@rendermode InteractiveServer
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="json-body-editor-wrap">
    <textarea @ref="_textareaRef"></textarea>
</div>

@code {
    [Parameter] public string Value { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public EventCallback OnBlur { get; set; }

    private ElementReference _textareaRef;
    private DotNetObjectReference<JsonBodyEditor>? _dotnetRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotnetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("forge.editor.init", _textareaRef, _dotnetRef, Value, "application/json", true);
        }
    }

    [JSInvokable]
    public async Task OnScriptChanged(string value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }

    [JSInvokable]
    public async Task OnEditorBlur()
    {
        await OnBlur.InvokeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dotnetRef is not null)
        {
            try { await JS.InvokeVoidAsync("forge.editor.dispose", _textareaRef); } catch { }
        }
        _dotnetRef?.Dispose();
    }
}
```

- [ ] **Step 2: Create `JsonBodyEditor.razor.css`**

Create `HttpForge/Components/Pages/JsonBodyEditor.razor.css` with:

```css
.json-body-editor-wrap {
    flex: 1;
    display: flex;
    flex-direction: column;
    min-height: 0;
}

.json-body-editor-wrap ::deep .CodeMirror {
    min-height: 180px;
    max-height: 400px;
    height: auto;
    font-size: 0.85rem;
    font-family: 'Consolas', 'Menlo', monospace;
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add HttpForge/Components/Pages/JsonBodyEditor.razor HttpForge/Components/Pages/JsonBodyEditor.razor.css
git commit -m "feat: add JsonBodyEditor editable CodeMirror component for JSON body"
```

---

### Task 4: Integrate components in Home.razor

**Files:**
- Modify: `HttpForge/Components/Pages/Home.razor:116-126` (body editor)
- Modify: `HttpForge/Components/Pages/Home.razor:197` (response display)

**Context:**

Current response display (line 197):
```razor
<pre>@FormatBody(_result.Body)</pre>
```

Current body editor (lines 116-126):
```razor
@if (_request.BodyKind == BodyKind.Json || _request.BodyKind == BodyKind.Raw)
{
    <VariableInput Value="@_request.BodyContent"
                   ValueChanged="OnBodyChanged"
                   OnBlur="SaveRequestDebounced"
                   Multiline="true"
                   CssClass="body-textarea"
                   Placeholder="@(_request.BodyKind == BodyKind.Json ? "{ \"key\": \"value\" }" : "raw body")"
                   Title="@Tooltip(_request.BodyContent)"
                   EnvVariables="_resolvedVariables" />
}
```

- [ ] **Step 1: Replace `<pre>` with `<JsonViewer>` in the response body**

In `HttpForge/Components/Pages/Home.razor`, find:

```razor
<pre>@FormatBody(_result.Body)</pre>
```

Replace with:

```razor
<JsonViewer Value="@FormatBody(_result.Body)" />
```

- [ ] **Step 2: Replace `VariableInput` (JSON mode) with `JsonBodyEditor`**

In `HttpForge/Components/Pages/Home.razor`, find:

```razor
@if (_request.BodyKind == BodyKind.Json || _request.BodyKind == BodyKind.Raw)
{
    <VariableInput Value="@_request.BodyContent"
                   ValueChanged="OnBodyChanged"
                   OnBlur="SaveRequestDebounced"
                   Multiline="true"
                   CssClass="body-textarea"
                   Placeholder="@(_request.BodyKind == BodyKind.Json ? "{ \"key\": \"value\" }" : "raw body")"
                   Title="@Tooltip(_request.BodyContent)"
                   EnvVariables="_resolvedVariables" />
}
```

Replace with:

```razor
@if (_request.BodyKind == BodyKind.Json)
{
    <JsonBodyEditor Value="@_request.BodyContent"
                    ValueChanged="OnBodyChanged"
                    OnBlur="SaveRequestDebounced" />
}
else if (_request.BodyKind == BodyKind.Raw)
{
    <VariableInput Value="@_request.BodyContent"
                   ValueChanged="OnBodyChanged"
                   OnBlur="SaveRequestDebounced"
                   Multiline="true"
                   CssClass="body-textarea"
                   Placeholder="raw body"
                   Title="@Tooltip(_request.BodyContent)"
                   EnvVariables="_resolvedVariables" />
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Manual smoke test**

Run `dotnet run --project HttpForge` and verify:

- Select a request with JSON body — the body editor shows JSON with syntax highlighting (strings in one color, keys in another, braces visible)
- Edit the JSON body — changes are saved (switch requests and come back to confirm)
- Switch body mode to Raw — plain textarea appears (no CodeMirror)
- Send a request that returns JSON — the response body panel shows syntax-highlighted JSON
- Send a request that returns non-JSON (e.g. plain text) — the response displays without errors
- Switch between requests — both JSON body editor and response viewer update correctly
- The script editor (Scripts tab) still works normally with JS highlighting

- [ ] **Step 5: Commit**

```powershell
git add HttpForge/Components/Pages/Home.razor
git commit -m "feat: integrate JsonViewer and JsonBodyEditor into Home.razor"
```
