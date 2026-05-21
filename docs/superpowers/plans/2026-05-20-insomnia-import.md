# Insomnia Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import Insomnia v5 YAML export files into HttpForge collections with a single file-picker click.

**Architecture:** A scoped `InsomniaImporter` service parses YAML via YamlDotNet into lightweight POCOs, maps them to HttpForge EF entities, and persists them using `IDbContextFactory<AppDbContext>`. The NavMenu adds a `↓` label-button wrapping a hidden `<InputFile multiple accept=".yaml">` — no JS interop required.

**Tech Stack:** .NET 10 Blazor Server, EF Core / SQLite, YamlDotNet, Blazor `InputFile` component

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| Modify | `HttpForge/HttpForge.csproj` | Add `YamlDotNet` NuGet reference |
| Create | `HttpForge/Services/InsomniaImporter.cs` | YAML POCOs + import logic + `ImportResult` record |
| Modify | `HttpForge/Program.cs` | Register `InsomniaImporter` as scoped DI service |
| Modify | `HttpForge/Components/Layout/NavMenu.razor` | Inject service, import button, `InputFile`, handler, status |

---

### Task 1: `InsomniaImporter` Service

**Files:**
- Modify: `HttpForge/HttpForge.csproj`
- Create: `HttpForge/Services/InsomniaImporter.cs`
- Modify: `HttpForge/Program.cs`

No test project exists in this repository — verification is by clean build + manual end-to-end test in Task 2.

- [ ] **Step 1: Add YamlDotNet NuGet package**

Run from the repo root:

```powershell
dotnet add HttpForge/HttpForge.csproj package YamlDotNet
```

Verify `HttpForge/HttpForge.csproj` now contains a `<PackageReference Include="YamlDotNet" .../>` line.

- [ ] **Step 2: Create `HttpForge/Services/InsomniaImporter.cs` with full implementation**

```csharp
using System.Text.RegularExpressions;
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HttpForge.Services;

public record ImportResult(string FileName, int RequestsCreated, int VariablesCreated, List<string> Warnings);

public class InsomniaImporter(IDbContextFactory<AppDbContext> dbFactory)
{
    // Matches {{ _.VAR }}, {{ _['VAR-NAME'] }}, {{ _.vault.KEY }}
    private static readonly Regex VarPattern = new(
        """\{\{\s*_(?:\[['"]([^'"]+)['"]\]|\.(?:vault\.)?([A-Za-z0-9_\-]+))\s*\}\}""",
        RegexOptions.Compiled);

    private static string TransformVars(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return VarPattern.Replace(input, m =>
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return $"{{{{ {name} }}}}";
        });
    }

    public async Task<ImportResult> ImportFileAsync(Stream content, string filename)
    {
        var warnings = new List<string>();

        using var reader = new StreamReader(content);
        var yaml = await reader.ReadToEndAsync();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<InsomniaFile>(yaml);

        if (file.Meta?.Id == "wrk_scratchpad")
            return new ImportResult(filename, 0, 0, warnings);

        await using var db = await dbFactory.CreateDbContextAsync();

        if (file.Type == "collection.insomnia.rest/5.0")
        {
            var (req, vars) = await ImportCollectionAsync(db, file, warnings);
            await db.SaveChangesAsync();
            return new ImportResult(filename, req, vars, warnings);
        }

        if (file.Type == "environment.insomnia.rest/5.0")
        {
            var vars = await ImportGlobalEnvAsync(db, file, warnings);
            await db.SaveChangesAsync();
            return new ImportResult(filename, 0, vars, warnings);
        }

        warnings.Add($"Unrecognized workspace type: {file.Type}");
        return new ImportResult(filename, 0, 0, warnings);
    }

    private static async Task<(int requests, int variables)> ImportCollectionAsync(
        AppDbContext db, InsomniaFile file, List<string> warnings)
    {
        var collection = new Collection { Name = file.Name ?? "Imported" };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        int requestCount = 0;
        int varCount = 0;

        foreach (var node in FlattenNodes(file.Collection ?? []))
        {
            db.Requests.Add(MapRequest(node, collection.Id, warnings));
            requestCount++;
        }

        if (file.Environments is not null)
        {
            var baseSet = new CollectionVariableSet
            {
                CollectionId = collection.Id,
                Name = "Base",
                IsBase = true,
                Entries = MapVarEntries(file.Environments.Data, warnings)
            };
            varCount += baseSet.Entries.Count;
            db.CollectionVariableSets.Add(baseSet);

            foreach (var sub in file.Environments.SubEnvironments ?? [])
            {
                var subSet = new CollectionVariableSet
                {
                    CollectionId = collection.Id,
                    Name = sub.Name ?? "Sub",
                    IsBase = false,
                    Entries = MapVarEntries(sub.Data, warnings)
                };
                varCount += subSet.Entries.Count;
                db.CollectionVariableSets.Add(subSet);
            }
        }

        return (requestCount, varCount);
    }

    private static async Task<int> ImportGlobalEnvAsync(
        AppDbContext db, InsomniaFile file, List<string> warnings)
    {
        if (file.Environments is null) return 0;
        int count = 0;

        var globalBase = await db.Environments
            .Include(e => e.Variables)
            .FirstOrDefaultAsync(e => e.IsBase);

        if (globalBase is null)
        {
            globalBase = new AppEnvironment { Name = "Global", IsBase = true };
            db.Environments.Add(globalBase);
            await db.SaveChangesAsync();
        }

        foreach (var (key, value) in NonVaultEntries(file.Environments.Data, warnings))
        {
            globalBase.Variables.Add(new EnvironmentVariable { Key = key, Value = value });
            count++;
        }

        foreach (var sub in file.Environments.SubEnvironments ?? [])
        {
            var env = new AppEnvironment { Name = sub.Name ?? "Imported", IsBase = false };
            db.Environments.Add(env);
            foreach (var (key, value) in NonVaultEntries(sub.Data, warnings))
            {
                env.Variables.Add(new EnvironmentVariable { Key = key, Value = value });
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<InsomniaNode> FlattenNodes(List<InsomniaNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children is { Count: > 0 })
            {
                foreach (var child in FlattenNodes(node.Children))
                    yield return child;
            }
            else if (node.Url is not null)
            {
                yield return node;
            }
        }
    }

    private static HttpRequestItem MapRequest(InsomniaNode node, int collectionId, List<string> warnings)
    {
        var req = new HttpRequestItem
        {
            CollectionId = collectionId,
            Name = node.Name ?? "Untitled",
            Url = TransformVars(node.Url),
            Method = ParseMethod(node.Method),
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var h in node.Headers ?? [])
        {
            if (h.Disabled == true) continue;
            if (string.Equals(h.Name, "User-Agent", StringComparison.OrdinalIgnoreCase)
                && h.Value?.StartsWith("insomnia/", StringComparison.OrdinalIgnoreCase) == true)
                continue;
            req.Headers.Add(new HeaderItem { Key = h.Name ?? "", Value = TransformVars(h.Value) });
        }

        if (node.Authentication?.Type == "bearer" && !string.IsNullOrEmpty(node.Authentication.Token))
        {
            if (!req.Headers.Any(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase) && h.Enabled))
                req.Headers.Add(new HeaderItem { Key = "Authorization", Value = $"Bearer {TransformVars(node.Authentication.Token)}" });
        }

        var mimeType = node.Body?.MimeType;
        if (mimeType == "application/json")
        {
            req.BodyKind = BodyKind.Json;
            req.BodyContent = TransformVars(node.Body?.Text);
        }
        else if (mimeType is "multipart/form-data" or "application/x-www-form-urlencoded")
        {
            req.BodyKind = BodyKind.FormUrlEncoded;
            foreach (var p in node.Body?.Params ?? [])
            {
                if (p.Disabled == true) continue;
                req.FormFields.Add(new FormFieldItem { Key = p.Name ?? "", Value = TransformVars(p.Value) });
            }
        }
        else if (!string.IsNullOrEmpty(node.Body?.Text))
        {
            req.BodyKind = BodyKind.Raw;
            req.BodyContent = TransformVars(node.Body!.Text);
            if (mimeType is not null)
                warnings.Add($"Request '{node.Name}': unrecognized body type '{mimeType}', imported as Raw");
        }

        return req;
    }

    private static HttpMethodKind ParseMethod(string? method) => method?.ToUpperInvariant() switch
    {
        "POST" => HttpMethodKind.POST,
        "PUT" => HttpMethodKind.PUT,
        "PATCH" => HttpMethodKind.PATCH,
        "DELETE" => HttpMethodKind.DELETE,
        "HEAD" => HttpMethodKind.HEAD,
        "OPTIONS" => HttpMethodKind.OPTIONS,
        _ => HttpMethodKind.GET
    };

    private static List<CollectionVariableEntry> MapVarEntries(
        Dictionary<string, object?>? data, List<string> warnings) =>
        NonVaultEntries(data, warnings)
            .Select(kv => new CollectionVariableEntry { Key = kv.key, Value = kv.value })
            .ToList();

    private static IEnumerable<(string key, string value)> NonVaultEntries(
        Dictionary<string, object?>? data, List<string> warnings)
    {
        if (data is null) yield break;
        foreach (var (key, val) in data)
        {
            if (key == "__insomnia_vault") { warnings.Add("Vault entries skipped (encrypted, unrecoverable)"); continue; }
            yield return (key, val?.ToString() ?? "");
        }
    }
}

// ── YAML POCOs ──────────────────────────────────────────────────────────────

public class InsomniaFile
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public InsomniaFileMeta? Meta { get; set; }
    public List<InsomniaNode>? Collection { get; set; }
    public InsomniaEnvironment? Environments { get; set; }
}

public class InsomniaFileMeta
{
    public string? Id { get; set; }
}

public class InsomniaNode
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; }
    public List<InsomniaHeader>? Headers { get; set; }
    public InsomniaBody? Body { get; set; }
    public InsomniaAuth? Authentication { get; set; }
    public List<InsomniaNode>? Children { get; set; }
}

public class InsomniaHeader
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Disabled { get; set; }
}

public class InsomniaBody
{
    public string? MimeType { get; set; }
    public string? Text { get; set; }
    public List<InsomniaParam>? Params { get; set; }
}

public class InsomniaParam
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Disabled { get; set; }
}

public class InsomniaAuth
{
    public string? Type { get; set; }
    public string? Token { get; set; }
}

public class InsomniaEnvironment
{
    public string? Name { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    public List<InsomniaSubEnvironment>? SubEnvironments { get; set; }
}

public class InsomniaSubEnvironment
{
    public string? Name { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}
```

- [ ] **Step 3: Build to verify no compile errors**

Run from repo root:

```powershell
dotnet build HttpForge/HttpForge.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Register `InsomniaImporter` in `HttpForge/Program.cs`**

After the existing `builder.Services.AddScoped<VariableResolver>();` line, add:

```csharp
builder.Services.AddScoped<InsomniaImporter>();
```

The three scoped service registrations should now read:

```csharp
builder.Services.AddScoped<RequestExecutor>();
builder.Services.AddScoped<VariableResolver>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<InsomniaImporter>();
```

- [ ] **Step 5: Build again**

```powershell
dotnet build HttpForge/HttpForge.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```powershell
git add HttpForge/HttpForge.csproj HttpForge/Services/InsomniaImporter.cs HttpForge/Program.cs
git commit -m "feat: add InsomniaImporter service with YamlDotNet parsing"
```

---

### Task 2: NavMenu Import UI

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Add inject directive and import state fields at the top of `@code`**

In `NavMenu.razor`, after the `@inject IJSRuntime JS` line (around line 4), add:

```razor
@inject InsomniaImporter Importer
```

In the `@code` block, after the existing field declarations (around line 241), add:

```csharp
private string? _importStatus;
```

- [ ] **Step 2: Add `OnImportFiles` handler in `@code`**

Add this method after `StartAddCollection` (around line 298):

```csharp
private async Task OnImportFiles(InputFileChangeEventArgs e)
{
    _importStatus = null;
    int totalCollections = 0;
    int totalRequests = 0;
    var errors = new List<string>();

    foreach (var file in e.GetMultipleFiles(maxAllowedFiles: 20))
    {
        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            var result = await Importer.ImportFileAsync(stream, file.Name);
            if (result.RequestsCreated > 0 || result.VariablesCreated > 0)
            {
                totalCollections++;
                totalRequests += result.RequestsCreated;
            }
            foreach (var w in result.Warnings)
                Console.WriteLine($"[Insomnia import] {file.Name}: {w}");
        }
        catch (Exception ex)
        {
            errors.Add($"{file.Name}: {ex.Message}");
        }
    }

    _importStatus = errors.Count > 0
        ? $"Import error: {string.Join("; ", errors)}"
        : $"Imported: {totalCollections} collection(s), {totalRequests} request(s)";

    await ReloadAsync();
    State.NotifyChanged();

    _ = Task.Delay(5000).ContinueWith(_ => InvokeAsync(() =>
    {
        _importStatus = null;
        StateHasChanged();
    }));
}
```

- [ ] **Step 3: Add import button and `InputFile` to the Collections section-title**

Replace the existing `section-title` div (around line 91):

```razor
<div class="section-title">
    <span>Collections</span>
    <button class="icon-btn" title="New collection" @onclick="StartAddCollection">+</button>
</div>
```

with:

```razor
<div class="section-title">
    <span>Collections</span>
    <label class="icon-btn" title="Import Insomnia YAML file(s)">
        ↓
        <InputFile accept=".yaml" multiple OnChange="OnImportFiles" style="display:none" />
    </label>
    <button class="icon-btn" title="New collection" @onclick="StartAddCollection">+</button>
</div>
@if (_importStatus is not null)
{
    <div class="import-status">@_importStatus</div>
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build HttpForge/HttpForge.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Manual end-to-end test**

Start the app:

```powershell
dotnet run --project HttpForge/HttpForge.csproj
```

1. Open the browser at `https://localhost:5001` (or the port shown in output).
2. Click the `↓` button in the Collections header. A file picker opens.
3. Select `C:\Users\micka\Downloads\insomnia\insomnia-export.1779286724394\Task-API-wrk_1cf585e728e94c269df5a6fd2cfe2544.yaml`.
4. Verify: a collection named **Task-API** appears in the sidebar.
5. Expand it — expect 3 requests: **Healthz/live** (GET), **Status** (GET), **Password hash report** (POST).
6. Open **Password hash report** — body should be JSON with `{{ API-URL }}` in the URL.
7. Open its Variables tab — expect one base set with key `API-URL` = `https://task-api.red.divalto.com` and a sub-set `Localhost` with `API-URL` = `http://localhost:17621`.
8. Import `Global-environment-wrk_b55bfde049494d3586d7a1cebbdc93c7.yaml` — the status should say `Imported: 0 collection(s), 3 request(s)` (or similar). Verify PROJECTCODE/ERP_USERNAME/USERNAME appear in global variables.
9. Import `Scratch-Pad-wrk_scratchpad.yaml` — status should say `Imported: 0 collection(s), 0 request(s)` (silently skipped).

- [ ] **Step 6: Commit**

```powershell
git add HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: add Insomnia YAML import button and handler in NavMenu"
```
