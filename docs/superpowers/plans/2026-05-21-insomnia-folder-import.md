# Insomnia Folder Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve the Insomnia collection folder hierarchy on import instead of flattening all requests to root.

**Architecture:** Replace the flat `FlattenNodes` enumeration in `InsomniaImporter` with a recursive `ImportNodesAsync` method that creates `CollectionFolder` entities for folder nodes and assigns `FolderId` on request nodes. `ImportResult` gains a `FoldersCreated` field; the NavMenu import status message is updated to display it.

**Tech Stack:** C#, EF Core, YamlDotNet, Blazor Server

---

## File Map

| Action | File | Change |
|--------|------|--------|
| Modify | `HttpForge/Services/InsomniaImporter.cs` | Replace `FlattenNodes`, add `ImportNodesAsync`, update `ImportResult` + call sites |
| Modify | `HttpForge/Components/Layout/NavMenu.razor` | Update import status message to include folder count |

---

### Task 1: Replace FlattenNodes with recursive ImportNodesAsync

**Files:**
- Modify: `HttpForge/Services/InsomniaImporter.cs` (entire file, see below)
- Modify: `HttpForge/Components/Layout/NavMenu.razor:493-508`

**Context:**

`InsomniaImporter.cs` currently uses `FlattenNodes` which discards the folder hierarchy:

```csharp
private static IEnumerable<InsomniaNode> FlattenNodes(List<InsomniaNode> nodes)
{
    foreach (var node in nodes)
    {
        if (node.Url is not null) yield return node;
        if (node.Children is { Count: > 0 })
            foreach (var child in FlattenNodes(node.Children))
                yield return child;
    }
}
```

And in `ImportCollectionAsync`:
```csharp
foreach (var node in FlattenNodes(file.Collection ?? []))
{
    db.Requests.Add(MapRequest(node, collection.Id, warnings));
    requestCount++;
}
```

`ImportResult` is:
```csharp
public record ImportResult(string FileName, int RequestsCreated, int VariablesCreated, List<string> Warnings);
```

In `NavMenu.razor` (around line 493):
```csharp
if (result.RequestsCreated > 0)
{
    totalCollections++;
    totalRequests += result.RequestsCreated;
}
// ...
_importStatus = errors.Count > 0
    ? $"Import error: {string.Join("; ", errors)}"
    : $"Imported: {totalCollections} collection(s), {totalRequests} request(s)";
```

- [ ] **Step 1: Update `ImportResult` record — add `FoldersCreated`**

In `HttpForge/Services/InsomniaImporter.cs`, replace line 10:

```csharp
public record ImportResult(string FileName, int RequestsCreated, int FoldersCreated, int VariablesCreated, List<string> Warnings);
```

- [ ] **Step 2: Update all `ImportResult` constructor call sites in `InsomniaImporter.cs`**

There are 4 call sites in `ImportFileAsync`. Replace them all:

```csharp
// Scratchpad early return (was: new ImportResult(filename, 0, 0, warnings))
return new ImportResult(filename, 0, 0, 0, warnings);

// Collection import (was: new ImportResult(filename, req, vars, warnings))
// becomes (after ImportCollectionAsync returns (req, folders, vars)):
return new ImportResult(filename, req, folders, vars, warnings);

// Global env import (was: new ImportResult(filename, 0, vars, warnings))
return new ImportResult(filename, 0, 0, vars, warnings);

// Unrecognized type (was: new ImportResult(filename, 0, 0, warnings))
return new ImportResult(filename, 0, 0, 0, warnings);
```

The full updated `ImportFileAsync` method:

```csharp
public async Task<ImportResult> ImportFileAsync(Stream content, string filename)
{
    var warnings = new List<string>();

    using var reader = new StreamReader(content);
    var yaml = await reader.ReadToEndAsync();

    var file = Deserializer.Deserialize<InsomniaFile>(yaml);

    if (file.Meta?.Id == "wrk_scratchpad")
        return new ImportResult(filename, 0, 0, 0, warnings);

    await using var db = await dbFactory.CreateDbContextAsync();

    if (file.Type == "collection.insomnia.rest/5.0")
    {
        var (req, folders, vars) = await ImportCollectionAsync(db, file, warnings);
        await db.SaveChangesAsync();
        return new ImportResult(filename, req, folders, vars, warnings);
    }

    if (file.Type == "environment.insomnia.rest/5.0")
    {
        var vars = await ImportGlobalEnvAsync(db, file, warnings);
        await db.SaveChangesAsync();
        return new ImportResult(filename, 0, 0, vars, warnings);
    }

    warnings.Add($"Unrecognized workspace type: {file.Type}");
    return new ImportResult(filename, 0, 0, 0, warnings);
}
```

- [ ] **Step 3: Update `ImportCollectionAsync` return type and body**

Replace the existing `ImportCollectionAsync` method:

```csharp
private static async Task<(int requests, int folders, int variables)> ImportCollectionAsync(
    AppDbContext db, InsomniaFile file, List<string> warnings)
{
    var collection = new Collection { Name = file.Name ?? "Imported" };
    db.Collections.Add(collection);
    await db.SaveChangesAsync();

    try
    {
        var (requestCount, folderCount) = await ImportNodesAsync(
            db, file.Collection ?? [], collection.Id, null, warnings);

        int varCount = 0;

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

        return (requestCount, folderCount, varCount);
    }
    catch (Exception)
    {
        db.Collections.Remove(collection);
        await db.SaveChangesAsync();
        throw;
    }
}
```

- [ ] **Step 4: Add `ImportNodesAsync` and remove `FlattenNodes`**

Delete the `FlattenNodes` method entirely. Add `ImportNodesAsync` in its place:

```csharp
private static async Task<(int requests, int folders)> ImportNodesAsync(
    AppDbContext db, List<InsomniaNode> nodes, int collectionId, int? parentFolderId, List<string> warnings)
{
    int requestCount = 0, folderCount = 0;
    foreach (var node in nodes)
    {
        if (node.Url is not null)
        {
            var req = MapRequest(node, collectionId, warnings);
            req.FolderId = parentFolderId;
            db.Requests.Add(req);
            requestCount++;
        }
        else
        {
            var folder = new CollectionFolder
            {
                CollectionId = collectionId,
                ParentFolderId = parentFolderId,
                Name = node.Name ?? "Folder"
            };
            db.CollectionFolders.Add(folder);
            await db.SaveChangesAsync();
            folderCount++;
            var (childReqs, childFolders) = await ImportNodesAsync(
                db, node.Children ?? [], collectionId, folder.Id, warnings);
            requestCount += childReqs;
            folderCount += childFolders;
        }
    }
    return (requestCount, folderCount);
}
```

Note: `await db.SaveChangesAsync()` after adding each folder is required to get the DB-generated `Id` before recursing. Requests are only added to the context here — the final `SaveChangesAsync` at the call site in `ImportFileAsync` flushes them.

- [ ] **Step 5: Update NavMenu import status to include folder count**

In `HttpForge/Components/Layout/NavMenu.razor`, find the import handler (around line 485). Replace the counting + status logic:

```csharp
int totalCollections = 0, totalRequests = 0, totalFolders = 0;
var errors = new List<string>();

foreach (var file in e.GetMultipleFiles(maximumFileCount: 20))
{
    try
    {
        using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        var result = await Importer.ImportFileAsync(stream, file.Name);
        if (result.RequestsCreated > 0 || result.FoldersCreated > 0)
        {
            totalCollections++;
            totalRequests += result.RequestsCreated;
            totalFolders += result.FoldersCreated;
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
    : $"Imported: {totalCollections} collection(s), {totalFolders} folder(s), {totalRequests} request(s)";
```

- [ ] **Step 6: Build and verify**

```powershell
dotnet build HttpForge
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Manual smoke test**

Run `dotnet run --project HttpForge`, import an Insomnia collection file that has folders. Verify:
- Folders appear in the sidebar under the collection
- Requests are inside their correct folders
- Requests without a parent folder appear at the collection root
- Import status message shows `X folder(s)`

- [ ] **Step 8: Commit**

```powershell
git add HttpForge/Services/InsomniaImporter.cs HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: preserve folder hierarchy on Insomnia collection import"
```
