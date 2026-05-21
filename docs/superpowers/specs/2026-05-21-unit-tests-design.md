# Unit Tests — Design Spec

**Date:** 2026-05-21  
**Status:** Approved

## Goal

Add a `HttpForge.Tests` project that covers the service layer with unit tests, organized by layer using folder structure.

## Project Structure

```
HttpForge/                         (existing)
HttpForge.Tests/
├── HttpForge.Tests.csproj
├── Services/
│   ├── VariableResolverTests.cs
│   ├── AppStateTests.cs
│   ├── VariablePreviewTests.cs
│   ├── RequestExecutorTests.cs
│   └── InsomniaImporterTests.cs
└── Helpers/
    └── FakeHttpMessageHandler.cs
HttpForge.sln                      (groups both projects)
```

## Dependencies

```xml
<PackageReference Include="xunit" />
<PackageReference Include="xunit.runner.visualstudio" />
<PackageReference Include="Microsoft.NET.Test.Sdk" />
<PackageReference Include="Moq" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
```

`HttpForge.Tests` references `HttpForge` as a project reference.

## Test Strategy by Layer

### Pure Services (no mocks)

**VariableResolver**
- `{{var}}` replaced with matching value
- Unknown variable left as-is (`{{unknown}}` unchanged)
- Null/empty input returns empty string
- Variable names with hyphens and dots resolved correctly
- Case-insensitive key lookup

**AppState.BuildVariables**
- Request-level variables override Collection-level
- Collection-level variables override Global-level
- Multiple sources merged correctly (all present)
- Null layers skipped without error
- Case-insensitive key merge (last writer wins)
- Result ordered by key

**VariablePreview**
- `Build()`: tooltip line per variable, secret shows `(secret)`, unknown shows `(not defined)`, duplicate keys de-duplicated
- `Resolve()`: non-secret resolved, secret left as `{{key}}`, unknown left as-is
- `BuildFullUrl()`: no params returns URL only, params appended with `?`, existing `?` uses `&`, disabled params excluded

### RequestExecutor (FakeHttpMessageHandler)

`FakeHttpMessageHandler` in `Helpers/` captures the outgoing `HttpRequestMessage` and returns a configurable response. Moq provides the `IHttpClientFactory` that returns an `HttpClient` wrapping this handler.

**URL building**
- Base URL without params → URL unchanged
- Enabled params appended as `?key=value`
- Multiple params joined with `&`
- Existing `?` in URL → additional params use `&`
- Disabled params excluded
- Variables resolved in URL and param values

**Body building**
- `BodyKind.None` → no content
- `BodyKind.Json` → `StringContent` with `application/json`, variables resolved in body
- `BodyKind.Raw` → `StringContent` with `text/plain`
- `BodyKind.FormUrlEncoded` → `FormUrlEncodedContent`, disabled fields excluded

**Headers**
- Enabled headers added to request
- Disabled headers excluded
- Content headers (e.g. `Content-Type`) fall back to `msg.Content.Headers`
- Variables resolved in header keys and values

### InsomniaImporter (EF Core SQLite in-memory)

Each test creates its own in-memory SQLite `AppDbContext` (unique name via `Guid.NewGuid()`) with `EnsureCreated()`. Moq provides `IDbContextFactory<AppDbContext>` returning that context.

**Collection import**
- Requests created with correct name, URL, method
- `{{ _.VAR }}` → `{{ VAR }}` variable syntax transformed
- `{{ _['VAR-NAME'] }}` bracket syntax also transformed
- Vault entries skipped with warning message
- Nested folders created with correct parent references
- Bearer auth injected as `Authorization: Bearer ...` header
- JSON body mapped to `BodyKind.Json`
- FormUrlEncoded body mapped to `BodyKind.FormUrlEncoded`
- Disabled params/headers excluded

**Environment import**
- Global base environment created if absent
- Variables added to existing global base
- Duplicate key produces warning, not overwrite
- Sub-environments created as separate `AppEnvironment` rows

**Edge cases**
- Scratchpad workspace (`wrk_scratchpad`) → returns immediately, nothing inserted
- Unrecognized `Type` → warning returned, zero rows inserted

## Conventions

**Test method naming:** `Method_Scenario_ExpectedResult`  
Examples: `Resolve_UnknownVariable_LeftAsIs`, `BuildVariables_RequestOverridesCollection_UsesRequestValue`

**No shared `[Collection]`** — all tests are stateless, no synchronization needed.

**One `Arrange / Act / Assert` block per test** — no shared mutable state between tests.

## Solution File

`HttpForge.sln` at the repo root groups `HttpForge` and `HttpForge.Tests`, enabling `dotnet build` and `dotnet test` from the root.
