# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Run the app
dotnet run --project HttpForge

# Build only
dotnet build HttpForge

# Watch mode (auto-reload on file change)
dotnet watch --project HttpForge
```

There are no automated tests in this project.

## Architecture

HttpForge is a Blazor Server app on .NET 10. It is a local HTTP client tool (Insomnia/Postman alternative) that stores all data in a SQLite file (`httpforge.db`) in the project root.

### Render modes

`MainLayout.razor` is **Static SSR** (no `@rendermode` directive). `OnAfterRenderAsync` never fires there — JS initialization for the sidebar resize must happen from `forge.js` directly on page load (already done at the bottom of `forge.js`). `NavMenu.razor` and `Home.razor` are both `@rendermode InteractiveServer`.

### Data layer

- **No EF Core migrations.** The schema is created via `db.Database.EnsureCreated()` at startup, then `SchemaUpgrader.Apply(db)` adds missing columns and tables using raw SQLite DDL. Any schema change requires an `EnsureColumn` or `EnsureTable` call in `SchemaUpgrader`.
- All components use `IDbContextFactory<AppDbContext>` (not `AppDbContext` directly) and create short-lived contexts per operation — required for Blazor Server async patterns.

### State management

`AppState` (scoped) holds `SelectedRequestId` and `SelectedEnvironmentId`. Components subscribe to `State.OnChange` and call `State.NotifyChanged()` after writing to the DB. The standard mutation pattern is:

1. Modify the in-memory entity
2. Write to DB via a fresh `DbFactory.CreateDbContextAsync()` context
3. Call `State.NotifyChanged()`
4. Subscribers reload from DB

### Variable resolution

Variables use `{{varname}}` syntax. `AppState.BuildVariables()` merges them in priority order (lowest to highest): Global Base → Global Subset → Collection Base → Collection Subset → Request-level. `VariableResolver.Resolve()` applies the substitution via regex. `VariablePreview` provides tooltip/URL preview strings without mutating state.

### JS interop

All JS is in `wwwroot/forge.js` under the `window.forge` namespace:

- `forge.setCaret` / `forge.attach` / `forge.detach` / `forge.setOpen` — keyboard navigation for the `VariableInput` autocomplete dropdown
- `forge.theme` — dark/light/system theme stored in `localStorage`
- `forge.sidebar` — resizable sidebar with width persisted to `localStorage`
- `forge.scripts.run(script, response, vars)` — executes post-request JS scripts in an `AsyncFunction` sandbox; exposes `fg.response`, `fg.variables`, `fg.console`
- `forge.editor` — CodeMirror 5 wrapper used by `ScriptEditor.razor`

### Services

| Service | Scope | Purpose |
|---|---|---|
| `AppState` | Scoped | Current selection + variable merge + change notification |
| `VariableResolver` | Scoped | `{{var}}` substitution |
| `RequestExecutor` | Scoped | Builds and sends HTTP requests via a per-request `SocketsHttpHandler` whose `ConnectCallback` times DNS/TCP/TLS and, when the request's `IgnoreTlsErrors` flag is set, skips server-certificate validation; returns a `RequestTiming` waterfall on `ExecutionResult` |
| `ScriptRunner` | Scoped | Runs post-request JS via `forge.scripts.run`; persists mutations to DB |
| `InsomniaImporter` | Scoped | Imports Insomnia v5 YAML (collection + environment files) |

### Insomnia import

`InsomniaImporter` handles two Insomnia v5 YAML types:
- `collection.insomnia.rest/5.0` — creates a `Collection` with requests and `CollectionVariableSet`s
- `environment.insomnia.rest/5.0` — merges into global `AppEnvironment`s

Insomnia variable syntax `{{ _.VAR }}` is rewritten to `{{ VAR }}` on import. Vault entries are skipped (encrypted, unrecoverable).

### Docs

`docs/superpowers/specs/` contains feature design docs. `docs/superpowers/plans/` contains implementation plans. Check these before implementing a feature to see if a spec already exists.
