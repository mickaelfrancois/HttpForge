# Post-Request Scripts — Design Spec

**Date:** 2026-05-20  
**Status:** Approved

## Overview

Allow each HTTP request to carry a post-request JavaScript script that runs after the response is received. The script can read the response (status, headers, body) and write variables back to any scope (request / collection / global). Primary use case: extract a JWT from an auth response and store it in a variable for use in subsequent requests.

## Requirements

- Each `HttpRequestItem` optionally has a `PostScript` (JS code, nullable)
- Script runs **after** the request executes, in the browser via a sandboxed IIFE
- `fg` API exposed to the script: `fg.response`, `fg.variables.get/set`, `fg.console.log`
- `fg.variables.set(key, value)` defaults to collection scope
- `fg.variables.get(key)` searches request → collection → global
- `fg.variables.set(key, value, scope)` writes to `"request"` | `"collection"` | `"global"`
- Mutations are persisted to the DB in the appropriate table
- Console output and errors are captured and shown in the UI (not in the browser dev console)
- `async/await` supported inside scripts
- InsomniaImporter extended to import `afterResponseScript` fields
- No pre-request scripts in this iteration

---

## Architecture

### 1. Data Model

**Entity:** `HttpForge/Data/Entities/HttpRequestItem.cs`  
Add: `public string? PostScript { get; set; }`

**Migration:** EF Core migration adds nullable column `PostScript TEXT` to `HttpRequestItems`.

No other DB changes. Mutations written by scripts use the existing `RequestVariable`, `CollectionVariableEntry`, and `EnvironmentVariable` tables.

---

### 2. JS Execution — `forge.scripts.run`

Added to `HttpForge/wwwroot/forge.js` under `window.forge.scripts`:

```javascript
window.forge.scripts = {
    async run(script, response, vars) {
        // response: { status, statusText, headers, body }
        // vars: { request: {}, collection: {}, global: {} }
        // returns: { mutations, logs, error }

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
                    return vars.request[key]
                        ?? vars.collection[key]
                        ?? vars.global[key]
                        ?? undefined;
                },
                set(key, value, scope = 'collection') {
                    mutations[scope][key] = String(value);
                }
            },
            console: {
                log(...args) { logs.push(args.map(String).join(' ')); }
            }
        };

        try {
            // AsyncFunction constructor supports top-level await in the user script
            const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
            await new AsyncFunction('fg', script)(fg);
            return { mutations, logs, error: null };
        } catch (e) {
            return { mutations, logs, error: e.message };
        }
    }
};
```

> **Note:** `eval` is intentional and acceptable in a developer tool. The user writes and runs their own scripts.

---

### 3. C# Service — `ScriptRunner`

New file: `HttpForge/Services/ScriptRunner.cs`

```csharp
public record ScriptResult(
    Dictionary<string, string> RequestMutations,
    Dictionary<string, string> CollectionMutations,
    Dictionary<string, string> GlobalMutations,
    List<string> Logs,
    string? Error);
```

**Method:**
```csharp
Task<ScriptResult?> RunPostScriptAsync(
    HttpRequestItem request,
    ExecutionResult response,
    VarLayers vars)   // { Request, Collection, Global } — all Dictionary<string,string>
```

**Flow:**
1. If `request.PostScript` is null/empty → return `null`
2. Call `JS.InvokeAsync<ScriptResult>("forge.scripts.run", script, responseDto, varsDto)`
3. Apply mutations to DB:
   - `request` mutations → upsert `RequestVariable` rows for `request.Id`
   - `collection` mutations → upsert `CollectionVariableEntry` rows in the active `CollectionVariableSet`
   - `global` mutations → upsert `EnvironmentVariable` rows in the selected `AppEnvironment`
4. Notify `AppState.NotifyStateChanged()` if any mutation was applied
5. Return `ScriptResult`

**Dependencies:** `IJSRuntime`, `IDbContextFactory<AppDbContext>`, `AppState`  
**Registration:** `builder.Services.AddScoped<ScriptRunner>()`

---

### 4. Caller Integration — `Home.razor`

After `Executor.ExecuteAsync(...)`:

```csharp
var scriptResult = await ScriptRunner.RunPostScriptAsync(
    _request,
    _result,
    new VarLayers(requestVars, collectionVars, globalVars));

_scriptResult = scriptResult;  // drives UI display
```

`Home.razor` injects `ScriptRunner`. The 3-layer variable split comes from sources already available in `Home.razor` at execution time (request-level vars are on `_request.Variables`, collection and global are fetched from DB/AppState).

---

### 5. UI

**New tab:** `Scripts` added to the request tab strip (after Variables).

**Tab content:**
- Label: `Post-request script`
- CodeMirror 6 editor (loaded via CDN, JS language mode, respects `data-theme` for dark/light)
- Editor content bound to `_request.PostScript`; auto-saved on change (same pattern as other fields)

**Script result panel** (shown below the response, only after execution if script ran):

```
✓ Script exécuté — 12ms
  collection["jwt"] ← "eyJhbGci..."
  ⚠ fg.console: "Token set"

✗ TypeError: Cannot read properties of undefined (reading 'access_token')   [ligne 3]
```

Mutations listed as `scope["key"] ← "value"` (value truncated to 60 chars).  
Errors shown in red with the JS error message.

**CodeMirror 5 integration** (CDN, no bundler required):  
Scripts added to `App.razor`:
```html
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.css" />
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/codemirror.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.16/mode/javascript/javascript.min.js"></script>
```
A thin `forge.editor.init(elementId, dotnetRef)` wrapper in `forge.js` creates a `CodeMirror.fromTextArea` instance, sets JS mode, and calls back to Blazor on change via `DotNetObjectReference`. Theme is toggled by swapping a CSS class matching the app's `data-theme` attribute.

---

### 6. Insomnia Import

**`InsomniaNode` POCO** — add:
```csharp
public string? AfterResponseScript { get; set; }
```

**`MapRequest`** — add at end:
```csharp
req.PostScript = string.IsNullOrWhiteSpace(node.AfterResponseScript)
    ? null
    : node.AfterResponseScript;
```

The field maps YAML key `afterResponseScript` via the existing `CamelCaseNamingConvention` deserializer.

---

## Data Flow

```
User clicks Send
  → RequestExecutor.ExecuteAsync → ExecutionResult
  → ScriptRunner.RunPostScriptAsync
      → JS: forge.scripts.run(script, response, {request, collection, global})
          → fg API evaluates user script
          → mutations collected
      → C#: upsert mutations to DB
      → AppState.NotifyStateChanged()
  → UI: script result panel updated
```

## Out of Scope

- Pre-request scripts
- Script sharing / libraries
- Script timeout / kill switch
- `fetch` inside scripts (works natively but not restricted)
- Test assertions (e.g. `fg.test(...)`)
