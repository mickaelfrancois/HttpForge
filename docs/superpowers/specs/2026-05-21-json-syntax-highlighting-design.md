# JSON Syntax Highlighting

**Date:** 2026-05-21  
**Status:** Approved

## Overview

Add CodeMirror-based JSON syntax highlighting to two zones in Home.razor:
1. **Response body** — read-only display, replaces the current `<pre>` tag
2. **Request body (JSON mode)** — editable, replaces `VariableInput` when `BodyKind == Json`

The `{{variable}}` autocomplete in the body editor is dropped in exchange for syntax highlighting. Users can still type `{{varname}}` manually.

---

## Components

### `JsonViewer.razor` (new, read-only)

Displays a read-only CodeMirror instance for response body output.

**Parameters:**
- `string Value` — pre-formatted JSON string (output of `FormatBody()`)

**Behavior:**
- `OnAfterRenderAsync`: calls `forge.viewer.init(el, value)` on first render; calls `forge.viewer.setValue(el, value)` on subsequent renders only if `Value` differs from `_lastValue`
- `_lastValue` field tracks the last value pushed to JS to avoid redundant updates
- Implements `IAsyncDisposable`: calls `forge.viewer.dispose(el)`

**CSS (`JsonViewer.razor.css`):**
- `.json-viewer-wrap .CodeMirror`: `max-height: 400px; overflow-y: auto;`
- Match existing response body appearance (no extra border)

---

### `JsonBodyEditor.razor` (new, editable)

Editable CodeMirror instance for the JSON request body.

**Parameters:**
- `string Value` — initial content
- `EventCallback<string> ValueChanged` — fired with debounce of 300ms on content change
- `EventCallback OnBlur` — fired when CodeMirror loses focus (triggers `SaveRequestDebounced`)

**Behavior:**
- `OnAfterRenderAsync`: calls `forge.editor.init(el, dotnetRef, value, 'json')` on first render only
- `[JSInvokable] OnEditorChanged(string value)` — updates `Value` and invokes `ValueChanged`
- `[JSInvokable] OnEditorBlur()` — invokes `OnBlur`
- Implements `IAsyncDisposable`: calls `forge.editor.dispose(el)`

**CSS (`JsonBodyEditor.razor.css`):**
- `.json-body-editor-wrap .CodeMirror`: same height as `.body-textarea` (min-height ~200px, max-height ~400px)

---

## JS Changes (`forge.js`)

### `forge.viewer` (new)

```js
window.forge.viewer = {
    _instances: new Map(),

    init(el, value) { ... },      // creates read-only CodeMirror, JSON mode
    setValue(el, value) { ... },  // updates value of existing instance
    dispose(el) { ... }           // tears down instance
};
```

`init` options: `mode: 'json'`, `readOnly: true`, `lineNumbers: true`, `lineWrapping: false`, theme synced to current dark/light setting (same logic as `forge.editor.init`).

### `forge.editor.init` (extend)

Add optional `mode` and `hasBlur` parameters:

```js
init(textareaEl, dotnetRef, initialValue, mode = 'javascript', hasBlur = false) { ... }
```

When `hasBlur` is `true`, register a CodeMirror `blur` event that calls `dotnetRef.invokeMethodAsync('OnEditorBlur')`.

`ScriptEditor.razor` does not change — it calls `forge.editor.init` without `mode` or `hasBlur` and gets the defaults (`'javascript'`, `false`).

---

## `App.razor` Changes

Add JSON mode script after the existing javascript mode script:

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/json/json.min.js"></script>
```

---

## `Home.razor` Changes

**Response body** — replace:
```razor
<pre>@FormatBody(_result.Body)</pre>
```
with:
```razor
<JsonViewer Value="@FormatBody(_result.Body)" />
```

**Request body JSON mode** — replace `<VariableInput Multiline="true" ...>` (when `BodyKind == Json`) with:
```razor
<JsonBodyEditor Value="@_request.BodyContent"
                ValueChanged="OnBodyChanged"
                OnBlur="SaveRequestDebounced" />
```

The `Raw` mode keeps `VariableInput` unchanged.

---

## Out of Scope

- JSON linting / error markers
- Variable autocomplete inside `JsonBodyEditor`
- Syntax highlighting for response headers
- Highlighting in other text areas (Scripts tab uses JS mode, already handled)
