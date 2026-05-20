# Post-Request Scripts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add post-request JS scripting to each HTTP request, exposing a `fg` API to read the response and write variables back to any scope (request / collection / global).

**Architecture:** User script runs in the browser via `new AsyncFunction('fg', script)(fg)` inside `window.forge.scripts.run`. A new `ScriptRunner` C# service orchestrates the JSInterop call and persists variable mutations to SQLite. A `ScriptEditor` Blazor component wraps CodeMirror 5. Home.razor calls `ScriptRunner` after each request execution and renders a result panel.

**Tech Stack:** Blazor Server (.NET 10), SQLite (SchemaUpgrader pattern), CodeMirror 5 (CDN), vanilla JS (`window.forge` namespace), System.Text.Json (JSInterop deserialization).

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `HttpForge/Data/Entities/HttpRequestItem.cs` | Modify | Add `PostScript` property |
| `HttpForge/Data/SchemaUpgrader.cs` | Modify | Add "Requests" to allowlist + EnsureColumn PostScript |
| `HttpForge/wwwroot/forge.js` | Modify | Add `window.forge.scripts.run` + `window.forge.editor` |
| `HttpForge/Components/App.razor` | Modify | Add CodeMirror 5 CDN links |
| `HttpForge/Services/ScriptRunner.cs` | Create | JS call orchestration + DB write-back |
| `HttpForge/Program.cs` | Modify | Register ScriptRunner as Scoped |
| `HttpForge/Components/Pages/ScriptEditor.razor` | Create | CodeMirror 5 wrapper Blazor component |
| `HttpForge/Components/Pages/ScriptEditor.razor.css` | Create | Editor component styles |
| `HttpForge/Components/Pages/Home.razor` | Modify | Scripts tab, inject ScriptRunner, call after Send, result panel |
| `HttpForge/Components/Pages/Home.razor.css` | Modify | Script result panel styles |
| `HttpForge/Services/InsomniaImporter.cs` | Modify | Map `afterResponseScript` → `PostScript` |

---

### Task 1: Data model — add PostScript field

**Files:**
- Modify: `HttpForge/Data/Entities/HttpRequestItem.cs`
- Modify: `HttpForge/Data/SchemaUpgrader.cs`

- [ ] **Step 1: Add `PostScript` property to `HttpRequestItem`**

  In `HttpForge/Data/Entities/HttpRequestItem.cs`, add after `BodyContent`:
  ```csharp
  public string? PostScript { get; set; }
  ```
  The file should now end with:
  ```csharp
  public string? BodyContent { get; set; }
  public string? PostScript { get; set; }

  public List<HeaderItem> Headers { get; set; } = new();
  ```

- [ ] **Step 2: Allow "Requests" table in SchemaUpgrader and add EnsureColumn call**

  In `HttpForge/Data/SchemaUpgrader.cs`, add `"Requests"` to `_allowedTables`:
  ```csharp
  private static readonly HashSet<string> _allowedTables =
  [
      "Collections", "Environments", "EnvironmentVariables",
      "CollectionVariables", "RequestVariables", "AppSettings",
      "CollectionVariableSets", "CollectionVariableEntries", "Requests"
  ];
  ```

  Then add at the top of `Apply()`, before the first `EnsureColumn` call:
  ```csharp
  EnsureColumn(db, "Requests", "PostScript", "TEXT NULL");
  ```

- [ ] **Step 3: Build and verify no errors**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 4: Commit**
  ```bash
  git add HttpForge/Data/Entities/HttpRequestItem.cs HttpForge/Data/SchemaUpgrader.cs
  git commit -m "feat: add PostScript field to HttpRequestItem"
  ```

---

### Task 2: JS execution engine — `window.forge.scripts.run`

**Files:**
- Modify: `HttpForge/wwwroot/forge.js`

- [ ] **Step 1: Append `window.forge.scripts` to the end of `forge.js`**

  Add after the IIFE that initializes the sidebar (at the very end of the file):
  ```javascript
  window.forge.scripts = {
      async run(script, response, vars) {
          const mutations = { request: {}, collection: {}, global: {} };
          const logs = [];

          const fg = {
              response: {
                  status: response.status,
                  statusText: response.statusText,
                  headers: response.headers,
                  body: response.body,
                  json() { return JSON.parse(response.body); }
              },
              variables: {
                  get(key) {
                      return vars.request[key] ?? vars.collection[key] ?? vars.global[key] ?? undefined;
                  },
                  set(key, value, scope = 'collection') {
                      if (mutations[scope] === undefined) return;
                      mutations[scope][key] = String(value);
                  }
              },
              console: {
                  log(...args) { logs.push(args.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a)).join(' ')); }
              }
          };

          try {
              const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
              await new AsyncFunction('fg', script)(fg);
              return { mutations, logs, error: null };
          } catch (e) {
              return { mutations, logs, error: e.message };
          }
      }
  };
  ```

- [ ] **Step 2: Build to verify no JS syntax errors break the Blazor build**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 3: Commit**
  ```bash
  git add HttpForge/wwwroot/forge.js
  git commit -m "feat: add forge.scripts.run JS execution engine"
  ```

---

### Task 3: ScriptRunner C# service

**Files:**
- Create: `HttpForge/Services/ScriptRunner.cs`
- Modify: `HttpForge/Program.cs`

- [ ] **Step 1: Create `HttpForge/Services/ScriptRunner.cs`**

  ```csharp
  using System.Text.Json.Serialization;
  using HttpForge.Data;
  using HttpForge.Data.Entities;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.JSInterop;

  namespace HttpForge.Services;

  public record ScriptMutations(
      Dictionary<string, string> Request,
      Dictionary<string, string> Collection,
      Dictionary<string, string> Global);

  public record ScriptResult(
      ScriptMutations Mutations,
      List<string> Logs,
      string? Error);

  public class ScriptRunner(IJSRuntime js, IDbContextFactory<AppDbContext> dbFactory, AppState state)
  {
      public async Task<ScriptResult?> RunPostScriptAsync(
          HttpRequestItem request,
          ExecutionResult response,
          int? activeCollectionSetId,
          int? activeGlobalEnvId,
          IReadOnlyList<ResolvedVariableEntry> resolvedVars)
      {
          if (string.IsNullOrWhiteSpace(request.PostScript))
              return null;

          var responseDto = new
          {
              status = response.StatusCode,
              statusText = response.ReasonPhrase ?? string.Empty,
              headers = response.Headers,
              body = response.Body ?? string.Empty
          };

          var varsDto = new
          {
              request = resolvedVars
                  .Where(v => v.Source == VariableSource.Request)
                  .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
              collection = resolvedVars
                  .Where(v => v.Source == VariableSource.Collection)
                  .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
              global = resolvedVars
                  .Where(v => v.Source == VariableSource.Global)
                  .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
          };

          var result = await js.InvokeAsync<ScriptResult>(
              "forge.scripts.run", request.PostScript, responseDto, varsDto);

          await ApplyMutationsAsync(request.Id, activeCollectionSetId, activeGlobalEnvId, result.Mutations);

          return result;
      }

      private async Task ApplyMutationsAsync(
          int requestId,
          int? collectionSetId,
          int? globalEnvId,
          ScriptMutations mutations)
      {
          if (mutations.Request.Count == 0 && mutations.Collection.Count == 0 && mutations.Global.Count == 0)
              return;

          await using var db = await dbFactory.CreateDbContextAsync();

          foreach (var (key, value) in mutations.Request)
          {
              var existing = await db.RequestVariables
                  .FirstOrDefaultAsync(v => v.HttpRequestItemId == requestId && v.Key == key);
              if (existing is not null)
                  existing.Value = value;
              else
                  db.RequestVariables.Add(new RequestVariable
                      { HttpRequestItemId = requestId, Key = key, Value = value });
          }

          if (collectionSetId.HasValue)
          {
              foreach (var (key, value) in mutations.Collection)
              {
                  var existing = await db.CollectionVariableEntries
                      .FirstOrDefaultAsync(e => e.CollectionVariableSetId == collectionSetId.Value && e.Key == key);
                  if (existing is not null)
                      existing.Value = value;
                  else
                      db.CollectionVariableEntries.Add(new CollectionVariableEntry
                          { CollectionVariableSetId = collectionSetId.Value, Key = key, Value = value });
              }
          }

          if (globalEnvId.HasValue)
          {
              foreach (var (key, value) in mutations.Global)
              {
                  var existing = await db.EnvironmentVariables
                      .FirstOrDefaultAsync(v => v.AppEnvironmentId == globalEnvId.Value && v.Key == key);
                  if (existing is not null)
                      existing.Value = value;
                  else
                      db.EnvironmentVariables.Add(new EnvironmentVariable
                          { AppEnvironmentId = globalEnvId.Value, Key = key, Value = value });
              }
          }

          await db.SaveChangesAsync();
          state.NotifyChanged();
      }
  }
  ```

- [ ] **Step 2: Register ScriptRunner in `Program.cs`**

  After `builder.Services.AddScoped<AppState>();`, add:
  ```csharp
  builder.Services.AddScoped<ScriptRunner>();
  ```

- [ ] **Step 3: Build and verify**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 4: Commit**
  ```bash
  git add HttpForge/Services/ScriptRunner.cs HttpForge/Program.cs
  git commit -m "feat: add ScriptRunner service for post-request JS execution"
  ```

---

### Task 4: CodeMirror 5 + ScriptEditor component

**Files:**
- Modify: `HttpForge/Components/App.razor`
- Modify: `HttpForge/wwwroot/forge.js`
- Create: `HttpForge/Components/Pages/ScriptEditor.razor`
- Create: `HttpForge/Components/Pages/ScriptEditor.razor.css`

- [ ] **Step 1: Add CodeMirror 5 CDN resources to `App.razor`**

  In `HttpForge/Components/App.razor`, add inside `<head>` after the last `<link>` tag:
  ```html
  <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.css" />
  <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/theme/monokai.min.css" />
  ```

  And in `<body>` after `<script src="@Assets["_framework/blazor.web.js"]">` and before `<script src="@Assets["forge.js"]">`:
  ```html
  <script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.js"></script>
  <script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/javascript/javascript.min.js"></script>
  ```

- [ ] **Step 2: Add `window.forge.editor` to `forge.js`**

  Append after `window.forge.scripts` at the end of `forge.js`:
  ```javascript
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
          cm.on('change', () => {
              dotnetRef.invokeMethodAsync('OnScriptChanged', cm.getValue());
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

- [ ] **Step 3: Create `HttpForge/Components/Pages/ScriptEditor.razor`**

  ```razor
  @inject IJSRuntime JS
  @implements IAsyncDisposable

  <div class="script-editor-wrap">
      <textarea @ref="_textareaRef"></textarea>
  </div>

  @code {
      [Parameter] public string? Value { get; set; }
      [Parameter] public EventCallback<string> ValueChanged { get; set; }

      private ElementReference _textareaRef;
      private DotNetObjectReference<ScriptEditor>? _dotnetRef;

      protected override async Task OnAfterRenderAsync(bool firstRender)
      {
          if (firstRender)
          {
              _dotnetRef = DotNetObjectReference.Create(this);
              await JS.InvokeVoidAsync("forge.editor.init", _textareaRef, _dotnetRef, Value ?? string.Empty);
          }
      }

      [JSInvokable]
      public async Task OnScriptChanged(string value)
      {
          Value = value;
          await ValueChanged.InvokeAsync(value);
      }

      public async ValueTask DisposeAsync()
      {
          try { await JS.InvokeVoidAsync("forge.editor.dispose", _textareaRef); } catch { }
          _dotnetRef?.Dispose();
      }
  }
  ```

- [ ] **Step 4: Create `HttpForge/Components/Pages/ScriptEditor.razor.css`**

  ```css
  .script-editor-wrap {
      flex: 1;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      border: 1px solid var(--border-input);
      border-radius: 3px;
  }

  .script-editor-wrap :deep(.CodeMirror) {
      flex: 1;
      height: 100%;
      font-size: 13px;
      font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
      background: var(--bg-input);
      color: var(--text-primary);
  }

  .script-editor-wrap :deep(.CodeMirror-scroll) {
      min-height: 120px;
  }
  ```

- [ ] **Step 5: Build and verify**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 6: Commit**
  ```bash
  git add HttpForge/Components/App.razor HttpForge/wwwroot/forge.js HttpForge/Components/Pages/ScriptEditor.razor HttpForge/Components/Pages/ScriptEditor.razor.css
  git commit -m "feat: add CodeMirror 5 editor and ScriptEditor component"
  ```

---

### Task 5: Scripts tab in Home.razor

**Files:**
- Modify: `HttpForge/Components/Pages/Home.razor`

- [ ] **Step 1: Add "Scripts" to the tabs array**

  In `Home.razor` `@code` block, change:
  ```csharp
  private readonly string[] _tabs = ["Params", "Headers", "Body", "Variables"];
  ```
  To:
  ```csharp
  private readonly string[] _tabs = ["Params", "Headers", "Body", "Variables", "Scripts"];
  ```

- [ ] **Step 2: Add the Scripts tab case to the `@switch` block**

  In `Home.razor`, inside the `@switch (_activeTab)` block, add after the `case "Variables":` case (before the closing `}`):
  ```razor
  case "Scripts":
      <div class="scripts-tab">
          <div class="script-section-label">Post-request script</div>
          <ScriptEditor Value="@_request.PostScript"
                        ValueChanged="OnPostScriptChanged" />
          <div class="script-hint">
              Use <code>fg.response.json()</code>, <code>fg.variables.set("key", value)</code>.
              <code>async/await</code> supported.
          </div>
      </div>
      break;
  ```

- [ ] **Step 3: Add the `OnPostScriptChanged` handler in the `@code` block**

  Add after `OnBodyChanged`:
  ```csharp
  private async Task OnPostScriptChanged(string value)
  {
      if (_request is null) return;
      _request.PostScript = value;
      await SaveRequestDebounced();
  }
  ```

- [ ] **Step 4: Build and verify**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 5: Commit**
  ```bash
  git add HttpForge/Components/Pages/Home.razor
  git commit -m "feat: add Scripts tab with CodeMirror editor to Home.razor"
  ```

---

### Task 6: Execution integration + result panel

**Files:**
- Modify: `HttpForge/Components/Pages/Home.razor`
- Modify: `HttpForge/Components/Pages/Home.razor.css`

- [ ] **Step 1: Inject ScriptRunner and add `_scriptResult` state**

  In `Home.razor`, add after `@inject RequestExecutor Executor`:
  ```razor
  @inject ScriptRunner ScriptRunner
  ```

  In the `@code` block, add after `private bool _sending;`:
  ```csharp
  private ScriptResult? _scriptResult;
  ```

- [ ] **Step 2: Call ScriptRunner in `SendAsync` and apply mutations to in-memory state**

  Replace the `SendAsync` method:
  ```csharp
  private async Task SendAsync()
  {
      if (_request is null) return;
      await SaveRequestDebounced();

      _sending = true;
      _result = null;
      _scriptResult = null;
      StateHasChanged();

      var vars = _resolvedVariables.ToDictionary(
          v => v.Key,
          v => v.Value,
          StringComparer.OrdinalIgnoreCase);

      _result = await Executor.ExecuteAsync(_request, vars);
      _sending = false;

      if (_result is not null && _result.Error is null && !string.IsNullOrWhiteSpace(_request.PostScript))
      {
          var activeCollectionSetId = _collectionSubset?.Id ?? _collectionBase?.Id;
          var activeGlobalEnvId = State.SelectedEnvironmentId ?? _globalBase?.Id;
          _scriptResult = await ScriptRunner.RunPostScriptAsync(
              _request, _result, activeCollectionSetId, activeGlobalEnvId, _resolvedVariables);

          if (_scriptResult is not null)
              ApplyScriptMutationsToMemory(_scriptResult);
      }
  }
  ```

- [ ] **Step 3: Add `ApplyScriptMutationsToMemory` helper**

  Add after `SendAsync`:
  ```csharp
  private void ApplyScriptMutationsToMemory(ScriptResult result)
  {
      foreach (var (key, value) in result.Mutations.Request)
      {
          var existing = _request?.Variables.FirstOrDefault(v =>
              string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
          if (existing is not null)
              existing.Value = value;
          else if (_request is not null)
              _request.Variables.Add(new RequestVariable
                  { HttpRequestItemId = _request.Id, Key = key, Value = value });
      }

      foreach (var (key, value) in result.Mutations.Collection)
      {
          var target = _collectionSubset ?? _collectionBase;
          var existing = target?.Entries.FirstOrDefault(e =>
              string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
          if (existing is not null)
              existing.Value = value;
          else
              target?.Entries.Add(new CollectionVariableEntry
                  { CollectionVariableSetId = target.Id, Key = key, Value = value });
      }

      foreach (var (key, value) in result.Mutations.Global)
      {
          var target = _globalSubset ?? _globalBase;
          var existing = target?.Variables.FirstOrDefault(v =>
              string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
          if (existing is not null)
              existing.Value = value;
          else
              target?.Variables.Add(new EnvironmentVariable
                  { AppEnvironmentId = target?.Id ?? 0, Key = key, Value = value });
      }

      RebuildVariables();
  }
  ```

- [ ] **Step 4: Add script result panel to the response pane**

  In `Home.razor`, inside the response pane (after the closing `</div>` of `<div class="response-body">`), add the script result panel:
  ```razor
  @if (_scriptResult is not null)
  {
      <div class="script-result @(_scriptResult.Error is null ? "script-result--ok" : "script-result--error")">
          <div class="script-result-header">
              @if (_scriptResult.Error is null)
              {
                  <span>&#10003; Script post-requête</span>
              }
              else
              {
                  <span>&#10007; Script error: @_scriptResult.Error</span>
              }
          </div>
          @if (_scriptResult.Mutations.Request.Count > 0 ||
               _scriptResult.Mutations.Collection.Count > 0 ||
               _scriptResult.Mutations.Global.Count > 0)
          {
              <ul class="script-mutations">
                  @foreach (var (k, v) in _scriptResult.Mutations.Request)
                  {
                      <li>request[<b>@k</b>] &#8592; <span class="mutation-val">@Truncate(v, 60)</span></li>
                  }
                  @foreach (var (k, v) in _scriptResult.Mutations.Collection)
                  {
                      <li>collection[<b>@k</b>] &#8592; <span class="mutation-val">@Truncate(v, 60)</span></li>
                  }
                  @foreach (var (k, v) in _scriptResult.Mutations.Global)
                  {
                      <li>global[<b>@k</b>] &#8592; <span class="mutation-val">@Truncate(v, 60)</span></li>
                  }
              </ul>
          }
          @foreach (var log in _scriptResult.Logs)
          {
              <div class="script-log">&#9656; @log</div>
          }
      </div>
  }
  ```

- [ ] **Step 5: Add the `Truncate` helper**

  Add after `FormatBody`:
  ```csharp
  private static string Truncate(string s, int max) =>
      s.Length <= max ? s : s[..max] + "…";
  ```

- [ ] **Step 6: Add script result panel CSS to `Home.razor.css`**

  Read `HttpForge/Components/Pages/Home.razor.css` first, then append:
  ```css
  .scripts-tab {
      display: flex;
      flex-direction: column;
      height: 100%;
      gap: 8px;
      padding: 8px 0;
  }

  .script-section-label {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      color: var(--text-muted);
      letter-spacing: 0.05em;
  }

  .script-hint {
      font-size: 11px;
      color: var(--text-dim);
  }

  .script-result {
      border-top: 1px solid var(--border-main);
      padding: 8px 12px;
      font-size: 12px;
  }

  .script-result--ok .script-result-header { color: var(--status-2xx-text); }
  .script-result--error .script-result-header { color: var(--error-text); font-weight: 600; }

  .script-mutations {
      list-style: none;
      margin: 4px 0 0;
      padding: 0;
  }

  .script-mutations li { padding: 1px 0; color: var(--text-primary); }
  .mutation-val { font-family: monospace; color: var(--text-code); }

  .script-log {
      color: var(--text-muted);
      font-family: monospace;
      margin-top: 2px;
  }
  ```

- [ ] **Step 7: Build and verify**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 8: Commit**
  ```bash
  git add HttpForge/Components/Pages/Home.razor HttpForge/Components/Pages/Home.razor.css
  git commit -m "feat: integrate ScriptRunner into SendAsync with result panel"
  ```

---

### Task 7: Insomnia importer — map AfterResponseScript

**Files:**
- Modify: `HttpForge/Services/InsomniaImporter.cs`

- [ ] **Step 1: Add `AfterResponseScript` property to `InsomniaNode`**

  In `HttpForge/Services/InsomniaImporter.cs`, in the `InsomniaNode` class, add after `Children`:
  ```csharp
  public string? AfterResponseScript { get; set; }
  ```

- [ ] **Step 2: Map `AfterResponseScript` in `MapRequest`**

  In `MapRequest`, add after the `return req;` line is reached — insert before `return req;`:
  ```csharp
  if (!string.IsNullOrWhiteSpace(node.AfterResponseScript))
      req.PostScript = node.AfterResponseScript;
  ```

- [ ] **Step 3: Build and verify**

  ```bash
  dotnet build HttpForge/HttpForge.csproj --nologo -v quiet
  ```
  Expected: `0 errors, 0 warnings`

- [ ] **Step 4: Manual smoke test**

  Start the app:
  ```bash
  dotnet run --project HttpForge/HttpForge.csproj
  ```

  Verify:
  1. Open any request → Scripts tab is present and CodeMirror editor loads
  2. Enter this script and send a request to `https://httpbin.org/get`:
     ```javascript
     const data = fg.response.json();
     fg.variables.set("last_url", data.url);
     fg.console.log("URL was:", data.url);
     ```
  3. Result panel shows: `collection["last_url"] ← "https://httpbin.org/get"` and the console log
  4. Check Variables tab of any other request in the same collection — `last_url` is now available

- [ ] **Step 5: Commit**
  ```bash
  git add HttpForge/Services/InsomniaImporter.cs
  git commit -m "feat: map Insomnia afterResponseScript to PostScript on import"
  ```
