# Insomnia Import Design

**Date:** 2026-05-20
**Feature:** Import Insomnia v5 YAML export files into HttpForge collections

## Problem

Users with existing Insomnia workspaces have to manually recreate all their requests in HttpForge. An import feature lets them migrate in one click.

## Format

Insomnia v5 exports a directory of YAML files, one per workspace (`schema_version: "5.1"`). Two workspace types exist:

- `type: collection.insomnia.rest/5.0` — a named workspace containing requests, folders, and environments
- `type: environment.insomnia.rest/5.0` — a global environment workspace (variables only, no requests)

## Mapping

### Collection file → Collection + Requests

| Insomnia field | HttpForge entity |
|---|---|
| `name` | `Collection.Name` |
| Request node `url` | `HttpRequestItem.Url` (variable-transformed) |
| Request node `name` | `HttpRequestItem.Name` |
| Request node `method` | `HttpRequestItem.Method` (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS) |
| `headers[]` (non-disabled, non User-Agent) | `HeaderItem` |
| `authentication.type: bearer` + `token` | `Authorization: Bearer <token>` header (added if no enabled Authorization header exists) |
| `body.mimeType: application/json` + `text` | `BodyKind.Json` + `BodyContent` |
| `body.mimeType: multipart/form-data` + `params[]` | `BodyKind.FormUrlEncoded` + `FormFieldItem[]` |
| `body.mimeType: application/x-www-form-urlencoded` + `params[]` | `BodyKind.FormUrlEncoded` + `FormFieldItem[]` |
| Any other body with text | `BodyKind.Raw` + `BodyContent` |
| No body / empty | `BodyKind.None` |
| `environments.data` (non-vault entries) | `CollectionVariableSet` (IsBase=true) + `CollectionVariableEntry[]` |
| `environments.subEnvironments[]` | `CollectionVariableSet` per sub-env (IsBase=false) + entries |

Folders (`fld_` nodes with `children`) are flattened — child requests are imported directly into the collection as if they were top-level.

### Environment file → Global variables

| Insomnia field | HttpForge entity |
|---|---|
| `environments.data` (non-vault) | Variables added to the existing global base `AppEnvironment` (IsBase=true) |
| `environments.subEnvironments[]` | New `AppEnvironment` sub-sets (IsBase=false) |

### Ignored

- `scripts` (pre/post-request JavaScript)
- `cookieJar`
- `settings`
- `meta` fields
- Headers with `disabled: true`
- Headers named `User-Agent` with value starting with `insomnia/`
- `__insomnia_vault` entries (values are encrypted in export, unrecoverable)
- Scratch Pad workspace (`meta.id: wrk_scratchpad`)

## Variable Syntax Transformation

Insomnia uses `{{ _.VARNAME }}` or `{{ _['VAR-NAME'] }}`. HttpForge uses `{{ VARNAME }}`.

All text fields (URL, header values, body, form field values) are transformed:
- `{{ _.VARNAME }}` → `{{ VARNAME }}`
- `{{ _['VAR-NAME'] }}` → `{{ VAR-NAME }}`
- `{{ _.vault.KEY }}` → `{{ KEY }}` (vault reference, value not imported)

Regex: `\{\{\s*_(?:\[['"]([^'"]+)['"]\]|\.(?:vault\.)?([A-Za-z0-9_\-]+))\s*\}\}`
Replacement: `{{ group1 or group2 }}`

## Service: `InsomniaImporter`

Registered as a scoped DI service. Single public method:

```csharp
public async Task<ImportResult> ImportFileAsync(Stream content, string filename)
```

`ImportResult`:
```csharp
public record ImportResult(string FileName, int RequestsCreated, int VariablesCreated, List<string> Warnings);
```

Warnings include: unrecognized body types, skipped vault entries, unrecognized workspace type.

Internally parses YAML into light POCOs (`InsomniaFile`, `InsomniaNode`, `InsomniaEnvironment`, etc.) via YamlDotNet, then maps and saves to DB using `IDbContextFactory<AppDbContext>`.

## UI

A `↓` import button added in the `section-title` row of the Collections header in `NavMenu.razor`, left of the existing `+` button. Implemented as a `<label>` wrapping a hidden `<InputFile accept=".yaml" multiple>` — no JS interop required.

After import, a one-line status message appears temporarily in the nav:
`"Imported: 2 collections, 18 requests"` (or error if parsing failed).

## Files Changed

- `HttpForge/HttpForge.csproj` — add `YamlDotNet` NuGet package
- `HttpForge/Services/InsomniaImporter.cs` — new service (parse + save)
- `HttpForge/Components/Layout/NavMenu.razor` — import button + InputFile + handler
- `HttpForge/Program.cs` — register `InsomniaImporter` as scoped service

## Acceptance Criteria

- Selecting one or more `.yaml` Insomnia v5 files imports each as a separate collection
- All requests appear in the new collection with correct method, URL, headers, body
- `{{ _.VAR }}` and `{{ _['VAR'] }}` syntax is converted to `{{ VAR }}`
- `environments.data` variables appear in the collection base variable set
- `environments.subEnvironments` appear as named sub-sets
- Global environment file variables are added to the HttpForge global base
- Disabled headers and User-Agent Insomnia headers are not imported
- Scratch Pad is silently skipped
- Import result count is shown briefly in the nav
- Vault entries are skipped (not imported, listed in warnings)
