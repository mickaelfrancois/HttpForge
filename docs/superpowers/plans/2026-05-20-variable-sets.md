# Variable Sets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat collection variable list and single global environment with a base+sub-sets model at both global and collection levels, persisted to DB.

**Architecture:** New `CollectionVariableSet`/`CollectionVariableEntry` entities replace `CollectionVariable`. `AppEnvironment` gains an `IsBase` flag. An `AppSettings` table (single row) persists the active global sub-set. `Collection` stores the active collection sub-set ID. `AppState.BuildVariables` takes 5 params (globalBase, globalSubset, collectionBase, collectionSubset, request). Home.razor and NavMenu.razor are updated to load and display this new model.

**Tech Stack:** Blazor Server, EF Core, SQLite, SchemaUpgrader (raw SQL, no migrations)

---

## File Map

**New files:**
- `HttpForge/Data/Entities/CollectionVariableSet.cs`
- `HttpForge/Data/Entities/CollectionVariableEntry.cs`
- `HttpForge/Data/Entities/AppSettings.cs`

**Modified files:**
- `HttpForge/Data/Entities/AppEnvironment.cs` — add `IsBase bool`
- `HttpForge/Data/Entities/Collection.cs` — add `ActiveCollectionVariableSetId?`, add `VariableSets` nav prop, keep `Variables` (removed at end of Task 4)
- `HttpForge/Data/AppDbContext.cs` — add DbSets + relationships; remove `Collection.Variables` relationship at end of Task 4
- `HttpForge/Data/SchemaUpgrader.cs` — new schema steps + data migration
- `HttpForge/Services/AppState.cs` — new `BuildVariables` signature (5 params)
- `HttpForge/Components/Pages/Home.razor` — update fields + loading methods
- `HttpForge/Components/Layout/NavMenu.razor` — full rework of env block and collection var editor
- `HttpForge/Components/Layout/NavMenu.razor.css` — add `.subset-badge`

---

## Task 1: DB Layer — Entities, AppDbContext, SchemaUpgrader

**Files:**
- Create: `HttpForge/Data/Entities/CollectionVariableSet.cs`
- Create: `HttpForge/Data/Entities/CollectionVariableEntry.cs`
- Create: `HttpForge/Data/Entities/AppSettings.cs`
- Modify: `HttpForge/Data/Entities/AppEnvironment.cs`
- Modify: `HttpForge/Data/Entities/Collection.cs`
- Modify: `HttpForge/Data/AppDbContext.cs`
- Modify: `HttpForge/Data/SchemaUpgrader.cs`

- [ ] **Step 1: Create CollectionVariableSet entity**

`HttpForge/Data/Entities/CollectionVariableSet.cs`:
```csharp
namespace HttpForge.Data.Entities;

public class CollectionVariableSet
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBase { get; set; }
    public List<CollectionVariableEntry> Entries { get; set; } = new();
}
```

- [ ] **Step 2: Create CollectionVariableEntry entity**

`HttpForge/Data/Entities/CollectionVariableEntry.cs`:
```csharp
namespace HttpForge.Data.Entities;

public class CollectionVariableEntry
{
    public int Id { get; set; }
    public int CollectionVariableSetId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
```

- [ ] **Step 3: Create AppSettings entity**

`HttpForge/Data/Entities/AppSettings.cs`:
```csharp
namespace HttpForge.Data.Entities;

public class AppSettings
{
    public int Id { get; set; }
    public int? ActiveGlobalSubsetId { get; set; }
}
```

- [ ] **Step 4: Add IsBase to AppEnvironment**

`HttpForge/Data/Entities/AppEnvironment.cs` — full replacement:
```csharp
namespace HttpForge.Data.Entities;

public class AppEnvironment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBase { get; set; }
    public List<EnvironmentVariable> Variables { get; set; } = new();
}
```

- [ ] **Step 5: Update Collection entity**

`HttpForge/Data/Entities/Collection.cs` — full replacement:
```csharp
namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ActiveCollectionVariableSetId { get; set; }
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariableSet> VariableSets { get; set; } = new();
    public List<CollectionVariable> Variables { get; set; } = new(); // kept until Task 4 cleanup
}
```

- [ ] **Step 6: Update AppDbContext**

`HttpForge/Data/AppDbContext.cs` — full replacement:
```csharp
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<HttpRequestItem> Requests => Set<HttpRequestItem>();
    public DbSet<HeaderItem> Headers => Set<HeaderItem>();
    public DbSet<QueryParamItem> QueryParams => Set<QueryParamItem>();
    public DbSet<FormFieldItem> FormFields => Set<FormFieldItem>();
    public DbSet<AppEnvironment> Environments => Set<AppEnvironment>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<CollectionVariable> CollectionVariables => Set<CollectionVariable>(); // kept for migration
    public DbSet<RequestVariable> RequestVariables => Set<RequestVariable>();
    public DbSet<CollectionVariableSet> CollectionVariableSets => Set<CollectionVariableSet>();
    public DbSet<CollectionVariableEntry> CollectionVariableEntries => Set<CollectionVariableEntry>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Collection>()
            .HasMany(c => c.Requests)
            .WithOne(r => r.Collection!)
            .HasForeignKey(r => r.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Collection>()
            .HasMany(c => c.Variables)
            .WithOne()
            .HasForeignKey(v => v.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Collection>()
            .HasMany(c => c.VariableSets)
            .WithOne()
            .HasForeignKey(s => s.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<CollectionVariableSet>()
            .HasMany(s => s.Entries)
            .WithOne()
            .HasForeignKey(e => e.CollectionVariableSetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.Headers)
            .WithOne()
            .HasForeignKey(h => h.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.QueryParams)
            .WithOne()
            .HasForeignKey(q => q.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.FormFields)
            .WithOne()
            .HasForeignKey(f => f.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.Variables)
            .WithOne()
            .HasForeignKey(v => v.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<AppEnvironment>()
            .HasMany(e => e.Variables)
            .WithOne()
            .HasForeignKey(v => v.AppEnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 7: Update SchemaUpgrader**

`HttpForge/Data/SchemaUpgrader.cs` — full replacement:
```csharp
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "EnvironmentVariables", "IsSecret", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "AppEnvironments", "IsBase", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Collections", "ActiveCollectionVariableSetId", "INTEGER NULL");

        EnsureTable(db, "CollectionVariables",
            "CREATE TABLE \"CollectionVariables\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionId\") REFERENCES \"Collections\"(\"Id\") ON DELETE CASCADE);");

        EnsureTable(db, "RequestVariables",
            "CREATE TABLE \"RequestVariables\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"HttpRequestItemId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"HttpRequestItemId\") REFERENCES \"Requests\"(\"Id\") ON DELETE CASCADE);");

        EnsureTable(db, "AppSettings",
            "CREATE TABLE \"AppSettings\" (" +
            "\"Id\" INTEGER PRIMARY KEY, " +
            "\"ActiveGlobalSubsetId\" INTEGER NULL);");

        EnsureTable(db, "CollectionVariableSets",
            "CREATE TABLE \"CollectionVariableSets\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionId\" INTEGER NOT NULL, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', " +
            "\"IsBase\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionId\") REFERENCES \"Collections\"(\"Id\") ON DELETE CASCADE);");

        EnsureTable(db, "CollectionVariableEntries",
            "CREATE TABLE \"CollectionVariableEntries\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionVariableSetId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionVariableSetId\") REFERENCES \"CollectionVariableSets\"(\"Id\") ON DELETE CASCADE);");

        EnsureGlobalBase(db);
        EnsureAppSettings(db);
        MigrateCollectionVariables(db);
    }

    private static void EnsureGlobalBase(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM \"AppEnvironments\" WHERE \"IsBase\" = 1;";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO \"AppEnvironments\" (\"Name\", \"IsBase\") VALUES ('Base', 1);";
        insert.ExecuteNonQuery();
    }

    private static void EnsureAppSettings(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM \"AppSettings\" WHERE \"Id\" = 1;";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO \"AppSettings\" (\"Id\", \"ActiveGlobalSubsetId\") VALUES (1, NULL);";
        insert.ExecuteNonQuery();
    }

    private static void MigrateCollectionVariables(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var setCheck = conn.CreateCommand();
        setCheck.CommandText = "SELECT COUNT(*) FROM \"CollectionVariableSets\";";
        if ((long)setCheck.ExecuteScalar()! > 0) return;

        using var varCheck = conn.CreateCommand();
        varCheck.CommandText = "SELECT COUNT(*) FROM \"CollectionVariables\";";
        if ((long)varCheck.ExecuteScalar()! == 0) return;

        using var insertSets = conn.CreateCommand();
        insertSets.CommandText =
            "INSERT INTO \"CollectionVariableSets\" (\"CollectionId\", \"Name\", \"IsBase\") " +
            "SELECT DISTINCT \"CollectionId\", 'Base', 1 FROM \"CollectionVariables\";";
        insertSets.ExecuteNonQuery();

        using var insertEntries = conn.CreateCommand();
        insertEntries.CommandText =
            "INSERT INTO \"CollectionVariableEntries\" (\"CollectionVariableSetId\", \"Key\", \"Value\", \"IsSecret\") " +
            "SELECT cvs.\"Id\", cv.\"Key\", cv.\"Value\", cv.\"IsSecret\" " +
            "FROM \"CollectionVariables\" cv " +
            "JOIN \"CollectionVariableSets\" cvs ON cvs.\"CollectionId\" = cv.\"CollectionId\" AND cvs.\"IsBase\" = 1;";
        insertEntries.ExecuteNonQuery();
    }

    private static void EnsureTable(AppDbContext db, string table, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        create.ExecuteNonQuery();
    }

    private static void EnsureColumn(AppDbContext db, string table, string column, string definition)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        alter.ExecuteNonQuery();
    }
}
```

- [ ] **Step 8: Build to verify Task 1 compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add HttpForge/Data/Entities/CollectionVariableSet.cs HttpForge/Data/Entities/CollectionVariableEntry.cs HttpForge/Data/Entities/AppSettings.cs HttpForge/Data/Entities/AppEnvironment.cs HttpForge/Data/Entities/Collection.cs HttpForge/Data/AppDbContext.cs HttpForge/Data/SchemaUpgrader.cs
git commit -m "feat: add variable set entities, AppSettings, schema migrations"
```

---

## Task 2: AppState + Home.razor

**Files:**
- Modify: `HttpForge/Services/AppState.cs`
- Modify: `HttpForge/Components/Pages/Home.razor`

- [ ] **Step 1: Update AppState.BuildVariables**

`HttpForge/Services/AppState.cs` — full replacement:
```csharp
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public enum VariableSource { Global, Collection, Request }

public record ResolvedVariableEntry(string Key, string Value, bool IsSecret, VariableSource Source);

public class AppState
{
    public int? SelectedEnvironmentId { get; set; }
    public int? SelectedRequestId { get; set; }

    public event Action? OnChange;

    public void NotifyChanged() => OnChange?.Invoke();

    public IReadOnlyList<ResolvedVariableEntry> BuildVariables(
        AppEnvironment? globalBase,
        AppEnvironment? globalSubset,
        CollectionVariableSet? collectionBase,
        CollectionVariableSet? collectionSubset,
        HttpRequestItem? request)
    {
        var merged = new Dictionary<string, ResolvedVariableEntry>(StringComparer.OrdinalIgnoreCase);

        if (globalBase is not null)
            foreach (var v in globalBase.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (globalSubset is not null)
            foreach (var v in globalSubset.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (collectionBase is not null)
            foreach (var v in collectionBase.Entries)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (collectionSubset is not null)
            foreach (var v in collectionSubset.Entries)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (request is not null)
            foreach (var v in request.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Request);

        return merged.Values.OrderBy(v => v.Key).ToList();
    }
}
```

- [ ] **Step 2: Update Home.razor @code fields and loading methods**

In `HttpForge/Components/Pages/Home.razor`, replace the entire `@code { ... }` block with:
```csharp
@code {
    private HttpRequestItem? _request;
    private AppEnvironment? _globalBase;
    private AppEnvironment? _globalSubset;
    private CollectionVariableSet? _collectionBase;
    private CollectionVariableSet? _collectionSubset;
    private IReadOnlyList<ResolvedVariableEntry> _resolvedVariables = Array.Empty<ResolvedVariableEntry>();
    private ExecutionResult? _result;
    private bool _sending;
    private string _activeTab = "Params";
    private string _responseTab = "Body";
    private readonly string[] _tabs = ["Params", "Headers", "Body", "Variables"];

    protected override async Task OnInitializedAsync()
    {
        State.OnChange += OnStateChanged;
        await LoadEnvAsync();
        await LoadRequestAsync();
    }

    public void Dispose() => State.OnChange -= OnStateChanged;

    private async void OnStateChanged()
    {
        try
        {
            await LoadEnvAsync();
            await LoadRequestAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HttpForge] State change error: {ex.Message}");
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadEnvAsync()
    {
        using var db = await DbFactory.CreateDbContextAsync();
        var envs = await db.Environments
            .Include(e => e.Variables)
            .ToListAsync();
        _globalBase = envs.FirstOrDefault(e => e.IsBase);
        _globalSubset = State.SelectedEnvironmentId is int id
            ? envs.FirstOrDefault(e => e.Id == id && !e.IsBase)
            : null;
        RebuildVariables();
    }

    private async Task LoadRequestAsync()
    {
        if (State.SelectedRequestId is null)
        {
            _request = null;
            _collectionBase = null;
            _collectionSubset = null;
            _result = null;
            RebuildVariables();
            return;
        }

        _result = null;

        using var db = await DbFactory.CreateDbContextAsync();
        _request = await db.Requests
            .Include(r => r.Headers)
            .Include(r => r.QueryParams)
            .Include(r => r.FormFields)
            .Include(r => r.Variables)
            .FirstOrDefaultAsync(r => r.Id == State.SelectedRequestId);

        var collection = _request is null ? null : await db.Collections
            .Include(c => c.VariableSets).ThenInclude(s => s.Entries)
            .FirstOrDefaultAsync(c => c.Id == _request.CollectionId);

        _collectionBase = collection?.VariableSets.FirstOrDefault(s => s.IsBase);
        _collectionSubset = collection?.ActiveCollectionVariableSetId is int sid
            ? collection.VariableSets.FirstOrDefault(s => s.Id == sid)
            : null;

        RebuildVariables();
    }

    private void RebuildVariables()
    {
        _resolvedVariables = State.BuildVariables(_globalBase, _globalSubset, _collectionBase, _collectionSubset, _request);
    }

    private string Tooltip(string? text) => VariablePreview.Build(text, _resolvedVariables);

    private void OnNameInput(ChangeEventArgs e)
    {
        if (_request is null) return;
        _request.Name = e.Value?.ToString() ?? string.Empty;
    }

    private async Task OnMethodChanged(ChangeEventArgs e)
    {
        if (_request is null) return;
        if (Enum.TryParse<HttpMethodKind>(e.Value?.ToString(), out var m))
        {
            _request.Method = m;
            await SaveRequestDebounced();
        }
    }

    private void OnUrlChanged(string value)
    {
        if (_request is null) return;
        _request.Url = value;
    }

    private void OnBodyChanged(string value)
    {
        if (_request is null) return;
        _request.BodyContent = value;
    }

    private async Task OnBodyKindChanged(BodyKind kind)
    {
        if (_request is null) return;
        _request.BodyKind = kind;
        await SaveRequestDebounced();
    }

    private async Task SaveRequestDebounced()
    {
        if (_request is null) return;
        _request.UpdatedAt = DateTime.UtcNow;
        using var db = await DbFactory.CreateDbContextAsync();
        db.Requests.Update(_request);
        await db.SaveChangesAsync();
        State.NotifyChanged();
    }

    private async Task SaveHeaderAsync(object item)
    {
        var h = (HeaderItem)item;
        using var db = await DbFactory.CreateDbContextAsync();
        if (h.Id == 0) db.Headers.Add(h);
        else db.Headers.Update(h);
        await db.SaveChangesAsync();
    }

    private async Task RemoveHeaderAsync(object item)
    {
        var h = (HeaderItem)item;
        if (h.Id == 0) { _request?.Headers.Remove(h); return; }
        using var db = await DbFactory.CreateDbContextAsync();
        db.Headers.Remove(await db.Headers.FirstAsync(x => x.Id == h.Id));
        await db.SaveChangesAsync();
        _request?.Headers.Remove(h);
    }

    private async Task SaveQueryParamAsync(object item)
    {
        var p = (QueryParamItem)item;
        using var db = await DbFactory.CreateDbContextAsync();
        if (p.Id == 0) db.QueryParams.Add(p);
        else db.QueryParams.Update(p);
        await db.SaveChangesAsync();
    }

    private async Task RemoveQueryParamAsync(object item)
    {
        var p = (QueryParamItem)item;
        if (p.Id == 0) { _request?.QueryParams.Remove(p); return; }
        using var db = await DbFactory.CreateDbContextAsync();
        db.QueryParams.Remove(await db.QueryParams.FirstAsync(x => x.Id == p.Id));
        await db.SaveChangesAsync();
        _request?.QueryParams.Remove(p);
    }

    private async Task SaveFormFieldAsync(object item)
    {
        var f = (FormFieldItem)item;
        using var db = await DbFactory.CreateDbContextAsync();
        if (f.Id == 0) db.FormFields.Add(f);
        else db.FormFields.Update(f);
        await db.SaveChangesAsync();
    }

    private async Task RemoveFormFieldAsync(object item)
    {
        var f = (FormFieldItem)item;
        if (f.Id == 0) { _request?.FormFields.Remove(f); return; }
        using var db = await DbFactory.CreateDbContextAsync();
        db.FormFields.Remove(await db.FormFields.FirstAsync(x => x.Id == f.Id));
        await db.SaveChangesAsync();
        _request?.FormFields.Remove(f);
    }

    private async Task SaveRequestVarAsync(object item)
    {
        var v = (RequestVariable)item;
        using var db = await DbFactory.CreateDbContextAsync();
        if (v.Id == 0) db.RequestVariables.Add(v);
        else db.RequestVariables.Update(v);
        await db.SaveChangesAsync();
        RebuildVariables();
    }

    private async Task RemoveRequestVarAsync(object item)
    {
        var v = (RequestVariable)item;
        if (v.Id == 0) { _request?.Variables.Remove(v); RebuildVariables(); return; }
        using var db = await DbFactory.CreateDbContextAsync();
        db.RequestVariables.Remove(await db.RequestVariables.FirstAsync(x => x.Id == v.Id));
        await db.SaveChangesAsync();
        _request?.Variables.Remove(v);
        RebuildVariables();
    }

    private async Task SendAsync()
    {
        if (_request is null) return;
        await SaveRequestDebounced();

        _sending = true;
        _result = null;
        StateHasChanged();

        var vars = _resolvedVariables.ToDictionary(
            v => v.Key,
            v => v.Value,
            StringComparer.OrdinalIgnoreCase);

        _result = await Executor.ExecuteAsync(_request, vars);
        _sending = false;
    }

    private static string StatusClass(int code) => code switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 => "5xx",
        _ => "0xx"
    };

    private static string FormatBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch { }
        }
        return body;
    }
}
```

- [ ] **Step 3: Build to verify Task 2 compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add HttpForge/Services/AppState.cs HttpForge/Components/Pages/Home.razor
git commit -m "feat: update BuildVariables to 5-param base+subset model, update Home.razor loading"
```

---

## Task 3: NavMenu — Global Variable Editor

Replace the global variables section (env-block) in `HttpForge/Components/Layout/NavMenu.razor`.

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Replace the env-block template section**

In `NavMenu.razor`, replace the entire `<div class="env-block">...</div>` block (lines 12–61) with:
```razor
<div class="env-block">
    <label>Global variables</label>
    <div class="env-row">
        <select value="@(State.SelectedEnvironmentId?.ToString() ?? "")" @onchange="OnSubsetChanged">
            <option value="">— base only —</option>
            @foreach (var s in _globalSubsets)
            {
                <option value="@s.Id">@s.Name</option>
            }
        </select>
        <button class="icon-btn" title="Manage global variables" @onclick="ToggleEnvEditor">@(_showEnvEditor ? "✕" : "⚙")</button>
    </div>
    @if (_showEnvEditor)
    {
        <div class="env-editor">
            @if (_globalBase is not null)
            {
                <div class="var-list">
                    <div class="var-list-header"><span>Base</span></div>
                    @foreach (var v in _globalBase.Variables)
                    {
                        var masked = v.IsSecret && !_revealed.Contains(v.Id);
                        <div class="var-row">
                            <input placeholder="key" value="@v.Key" @oninput="e => OnVarKeyChanged(v, e)" />
                            <input placeholder="value" type="@(masked ? "password" : "text")"
                                   value="@v.Value" @oninput="e => OnVarValueChanged(v, e)" />
                            @if (v.IsSecret)
                            {
                                <button class="icon-btn" title="@(masked ? "Reveal" : "Hide")"
                                        @onclick="() => ToggleReveal(v)">@(masked ? "👁" : "🚫")</button>
                            }
                            <button class="icon-btn @(v.IsSecret ? "active" : "")"
                                    title="@(v.IsSecret ? "Unmark secret" : "Mark secret")"
                                    @onclick="() => ToggleSecret(v)">@(v.IsSecret ? "🔒" : "🔓")</button>
                            <button class="icon-btn" @onclick="() => RemoveVar(v)">✕</button>
                        </div>
                    }
                    <button class="link-btn" @onclick="AddBaseVar">+ add base variable</button>
                </div>
            }
            <div class="var-list" style="margin-top:0.5rem">
                <div class="var-list-header"><span>Sub-sets</span></div>
                <div class="env-actions">
                    <input placeholder="New sub-set name" @bind="_newEnvName" @bind:event="oninput" />
                    <button @onclick="AddSubset" disabled="@string.IsNullOrWhiteSpace(_newEnvName)">Add</button>
                </div>
                @foreach (var s in _globalSubsets)
                {
                    <div class="var-list" style="margin-top:0.3rem">
                        <div class="var-list-header">
                            <span>@s.Name</span>
                            <button class="link-btn" @onclick="() => DeleteSubset(s)">delete</button>
                        </div>
                        @foreach (var v in s.Variables)
                        {
                            var masked = v.IsSecret && !_revealed.Contains(v.Id);
                            <div class="var-row">
                                <input placeholder="key" value="@v.Key" @oninput="e => OnVarKeyChanged(v, e)" />
                                <input placeholder="value" type="@(masked ? "password" : "text")"
                                       value="@v.Value" @oninput="e => OnVarValueChanged(v, e)" />
                                @if (v.IsSecret)
                                {
                                    <button class="icon-btn" title="@(masked ? "Reveal" : "Hide")"
                                            @onclick="() => ToggleReveal(v)">@(masked ? "👁" : "🚫")</button>
                                }
                                <button class="icon-btn @(v.IsSecret ? "active" : "")"
                                        title="@(v.IsSecret ? "Unmark secret" : "Mark secret")"
                                        @onclick="() => ToggleSecret(v)">@(v.IsSecret ? "🔒" : "🔓")</button>
                                <button class="icon-btn" @onclick="() => RemoveVar(v)">✕</button>
                            </div>
                        }
                        <button class="link-btn" @onclick="() => AddSubsetVar(s)">+ add variable</button>
                    </div>
                }
            </div>
        </div>
    }
</div>
```

- [ ] **Step 2: Replace @code fields and OnInitializedAsync/ReloadAsync**

In `@code`, replace the field declarations and the `OnInitializedAsync`/`ReloadAsync` methods:

**Replace fields** (the block starting at `private List<Collection> _collections = new();`):
```csharp
private List<Collection> _collections = new();
private AppEnvironment? _globalBase;
private List<AppEnvironment> _globalSubsets = new();
private HashSet<int> _expanded = new();
private bool _showAddCollection;
private string _newCollectionName = string.Empty;
private bool _showEnvEditor;
private string _newEnvName = string.Empty;
private HashSet<int> _revealed = new();
private HashSet<int> _collVarEditorOpen = new();
private HashSet<int> _collVarEntryRevealed = new();
```

**Replace `OnInitializedAsync`**:
```csharp
protected override async Task OnInitializedAsync()
{
    State.OnChange += OnStateChanged;
    await LoadSettingsAsync();
    await ReloadAsync();
}

private async Task LoadSettingsAsync()
{
    using var db = await DbFactory.CreateDbContextAsync();
    var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
    State.SelectedEnvironmentId = settings?.ActiveGlobalSubsetId;
}
```

**Replace `ReloadAsync`**:
```csharp
private async Task ReloadAsync()
{
    using var db = await DbFactory.CreateDbContextAsync();
    _collections = await db.Collections
        .Include(c => c.Requests)
        .Include(c => c.VariableSets).ThenInclude(s => s.Entries)
        .OrderBy(c => c.Name)
        .ToListAsync();
    var envs = await db.Environments
        .Include(e => e.Variables)
        .OrderBy(e => e.Name)
        .ToListAsync();
    _globalBase = envs.FirstOrDefault(e => e.IsBase);
    _globalSubsets = envs.Where(e => !e.IsBase).ToList();
}
```

- [ ] **Step 3: Replace env-related @code methods**

Remove all of these old methods: `OnEnvChanged`, `ToggleEnvEditor`, `AddEnvironment`, `DeleteSelectedEnv`, `AddVar`, `RemoveVar`, `OnVarKeyChanged`, `OnVarValueChanged`, `ToggleSecret`, `ToggleReveal`.

Add the following new methods in their place:

```csharp
private void ToggleEnvEditor() => _showEnvEditor = !_showEnvEditor;

private async Task OnSubsetChanged(ChangeEventArgs e)
{
    var id = int.TryParse(e.Value?.ToString(), out var v) ? (int?)v : null;
    State.SelectedEnvironmentId = id;
    using var db = await DbFactory.CreateDbContextAsync();
    var settings = await db.Settings.FirstAsync(s => s.Id == 1);
    settings.ActiveGlobalSubsetId = id;
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task AddSubset()
{
    if (string.IsNullOrWhiteSpace(_newEnvName)) return;
    using var db = await DbFactory.CreateDbContextAsync();
    var env = new AppEnvironment { Name = _newEnvName.Trim(), IsBase = false };
    db.Environments.Add(env);
    await db.SaveChangesAsync();
    _newEnvName = string.Empty;
    State.SelectedEnvironmentId = env.Id;
    var settings = await db.Settings.FirstAsync(s => s.Id == 1);
    settings.ActiveGlobalSubsetId = env.Id;
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task DeleteSubset(AppEnvironment s)
{
    var msg = s.Variables.Count > 0
        ? $"Delete sub-set \"{s.Name}\" and its {s.Variables.Count} variable(s)?"
        : $"Delete sub-set \"{s.Name}\"?";
    if (!await Confirm(msg)) return;
    using var db = await DbFactory.CreateDbContextAsync();
    db.Environments.Remove(await db.Environments.FirstAsync(e => e.Id == s.Id));
    if (State.SelectedEnvironmentId == s.Id)
    {
        State.SelectedEnvironmentId = null;
        var settings = await db.Settings.FirstAsync(x => x.Id == 1);
        settings.ActiveGlobalSubsetId = null;
    }
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task AddBaseVar()
{
    if (_globalBase is null) return;
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Add(new EnvironmentVariable { AppEnvironmentId = _globalBase.Id });
    await db.SaveChangesAsync();
    await ReloadAsync();
}

private async Task AddSubsetVar(AppEnvironment s)
{
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Add(new EnvironmentVariable { AppEnvironmentId = s.Id });
    await db.SaveChangesAsync();
    await ReloadAsync();
}

private async Task RemoveVar(EnvironmentVariable v)
{
    var label = string.IsNullOrWhiteSpace(v.Key) ? "(empty)" : v.Key;
    if (!await Confirm($"Delete variable \"{label}\"?")) return;
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Remove(await db.EnvironmentVariables.FirstAsync(x => x.Id == v.Id));
    await db.SaveChangesAsync();
    await ReloadAsync();
}

private async Task OnVarKeyChanged(EnvironmentVariable v, ChangeEventArgs e)
{
    v.Key = e.Value?.ToString() ?? "";
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task OnVarValueChanged(EnvironmentVariable v, ChangeEventArgs e)
{
    v.Value = e.Value?.ToString() ?? "";
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task ToggleSecret(EnvironmentVariable v)
{
    v.IsSecret = !v.IsSecret;
    if (!v.IsSecret) _revealed.Remove(v.Id);
    using var db = await DbFactory.CreateDbContextAsync();
    db.EnvironmentVariables.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private void ToggleReveal(EnvironmentVariable v)
{
    if (!_revealed.Add(v.Id)) _revealed.Remove(v.Id);
    StateHasChanged();
}
```

- [ ] **Step 4: Build to verify Task 3 compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors. (Old collection var methods still present — removed in Task 4.)

- [ ] **Step 5: Commit**

```bash
git add HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: rework global variable editor to base+sub-sets model"
```

---

## Task 4: NavMenu — Collection Variable Editor + Cleanup

Replace the collection variable editor in NavMenu, remove old CollectionVariable code, and clean up Collection.cs/AppDbContext.cs.

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`
- Modify: `HttpForge/Data/Entities/Collection.cs`
- Modify: `HttpForge/Data/AppDbContext.cs`
- Modify: `HttpForge/Components/Layout/NavMenu.razor.css`

- [ ] **Step 1: Replace collection-node template**

In `NavMenu.razor`, replace the entire `@foreach (var c in _collections)` block (the `<div class="collection-node">` and everything inside) with:

```razor
@foreach (var c in _collections)
{
    <div class="collection-node">
        <div class="collection-header">
            <button class="collection-toggle" @onclick="() => ToggleCollection(c.Id)">
                <span class="caret">@(_expanded.Contains(c.Id) ? "▾" : "▸")</span>
                <span>@c.Name</span>
                @{
                    var activeBadgeSet = c.VariableSets.FirstOrDefault(s => s.Id == c.ActiveCollectionVariableSetId);
                }
                @if (activeBadgeSet is not null)
                {
                    <span class="subset-badge">@activeBadgeSet.Name</span>
                }
            </button>
            <button class="icon-btn @(_collVarEditorOpen.Contains(c.Id) ? "active" : "")"
                    title="Variables" @onclick="() => ToggleCollVarEditor(c.Id)">⚙</button>
            <button class="icon-btn" title="New request" @onclick="() => AddRequest(c)">+</button>
            <button class="icon-btn" title="Delete collection" @onclick="() => DeleteCollection(c)">🗑</button>
        </div>
        @if (_collVarEditorOpen.Contains(c.Id))
        {
            var cBase = c.VariableSets.FirstOrDefault(s => s.IsBase);
            var cSubsets = c.VariableSets.Where(s => !s.IsBase).ToList();
            var activeSet = c.VariableSets.FirstOrDefault(s => s.Id == c.ActiveCollectionVariableSetId);

            <div class="env-editor">
                <div class="var-list">
                    <div class="var-list-header"><span>Base</span></div>
                    @foreach (var v in cBase?.Entries ?? [])
                    {
                        var masked = v.IsSecret && !_collVarEntryRevealed.Contains(v.Id);
                        <div class="var-row">
                            <input placeholder="key" value="@v.Key"
                                   @oninput="e => OnCollEntryKeyChanged(v, e)" />
                            <input placeholder="value" type="@(masked ? "password" : "text")"
                                   value="@v.Value"
                                   @oninput="e => OnCollEntryValueChanged(v, e)" />
                            @if (v.IsSecret)
                            {
                                <button class="icon-btn" title="@(masked ? "Reveal" : "Hide")"
                                        @onclick="() => ToggleCollEntryReveal(v)">@(masked ? "👁" : "🚫")</button>
                            }
                            <button class="icon-btn @(v.IsSecret ? "active" : "")"
                                    title="@(v.IsSecret ? "Unmark secret" : "Mark secret")"
                                    @onclick="() => ToggleCollEntrySecret(v)">@(v.IsSecret ? "🔒" : "🔓")</button>
                            <button class="icon-btn" @onclick="() => RemoveCollEntry(v)">✕</button>
                        </div>
                    }
                    <button class="link-btn" @onclick="() => AddCollBaseEntry(c.Id)">+ add base variable</button>
                </div>

                <div class="var-list" style="margin-top:0.5rem">
                    <div class="var-list-header"><span>Sub-set</span></div>
                    <div class="env-row">
                        <select value="@(c.ActiveCollectionVariableSetId?.ToString() ?? "")"
                                @onchange="e => OnCollSubsetChanged(c, e)">
                            <option value="">— none —</option>
                            @foreach (var s in cSubsets)
                            {
                                <option value="@s.Id">@s.Name</option>
                            }
                        </select>
                        <button class="icon-btn" title="New sub-set" @onclick="() => AddCollSubset(c.Id)">+</button>
                        @if (activeSet is not null)
                        {
                            <button class="icon-btn" title="Delete sub-set"
                                    @onclick="() => DeleteCollSubset(c, activeSet)">🗑</button>
                        }
                    </div>
                    @if (activeSet is not null)
                    {
                        @foreach (var v in activeSet.Entries)
                        {
                            var masked = v.IsSecret && !_collVarEntryRevealed.Contains(v.Id);
                            <div class="var-row">
                                <input placeholder="key" value="@v.Key"
                                       @oninput="e => OnCollEntryKeyChanged(v, e)" />
                                <input placeholder="value" type="@(masked ? "password" : "text")"
                                       value="@v.Value"
                                       @oninput="e => OnCollEntryValueChanged(v, e)" />
                                @if (v.IsSecret)
                                {
                                    <button class="icon-btn" title="@(masked ? "Reveal" : "Hide")"
                                            @onclick="() => ToggleCollEntryReveal(v)">@(masked ? "👁" : "🚫")</button>
                                }
                                <button class="icon-btn @(v.IsSecret ? "active" : "")"
                                        title="@(v.IsSecret ? "Unmark secret" : "Mark secret")"
                                        @onclick="() => ToggleCollEntrySecret(v)">@(v.IsSecret ? "🔒" : "🔓")</button>
                                <button class="icon-btn" @onclick="() => RemoveCollEntry(v)">✕</button>
                            </div>
                        }
                        <button class="link-btn" @onclick="() => AddCollSubsetEntry(activeSet.Id)">+ add variable</button>
                    }
                </div>
            </div>
        }
        @if (_expanded.Contains(c.Id))
        {
            <div class="request-list">
                @foreach (var r in c.Requests.OrderByDescending(r => r.UpdatedAt))
                {
                    <div class="request-row @(State.SelectedRequestId == r.Id ? "selected" : "")"
                         @onclick="() => SelectRequest(r.Id)">
                        <span class="method method-@r.Method.ToString().ToLower()">@r.Method</span>
                        <span class="request-name">@(string.IsNullOrWhiteSpace(r.Name) ? "Untitled" : r.Name)</span>
                        <button class="icon-btn" title="Duplicate request" @onclick:stopPropagation @onclick="() => DuplicateRequest(r)">⎘</button>
                        <button class="icon-btn" title="Delete request" @onclick:stopPropagation @onclick="() => DeleteRequest(r)">✕</button>
                    </div>
                }
                @if (c.Requests.Count == 0)
                {
                    <div class="empty-hint">No requests — click + to add.</div>
                }
            </div>
        }
    </div>
}
```

- [ ] **Step 2: Replace collection @code methods**

Remove old methods: `ToggleCollVarEditor`, `AddCollVar`, `RemoveCollVar`, `OnCollVarKeyChanged`, `OnCollVarValueChanged`, `ToggleCollVarSecret`, `ToggleCollVarReveal`.

Add new methods:

```csharp
private void ToggleCollVarEditor(int collectionId)
{
    if (!_collVarEditorOpen.Add(collectionId))
        _collVarEditorOpen.Remove(collectionId);
}

private async Task OnCollSubsetChanged(Collection c, ChangeEventArgs e)
{
    var id = int.TryParse(e.Value?.ToString(), out var v) ? (int?)v : null;
    c.ActiveCollectionVariableSetId = id;
    using var db = await DbFactory.CreateDbContextAsync();
    var col = await db.Collections.FirstAsync(x => x.Id == c.Id);
    col.ActiveCollectionVariableSetId = id;
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task AddCollSubset(int collectionId)
{
    var name = await JS.InvokeAsync<string?>("prompt", "Sub-set name:");
    if (string.IsNullOrWhiteSpace(name)) return;
    using var db = await DbFactory.CreateDbContextAsync();
    var set = new CollectionVariableSet { CollectionId = collectionId, Name = name.Trim(), IsBase = false };
    db.CollectionVariableSets.Add(set);
    await db.SaveChangesAsync();
    var col = await db.Collections.FirstAsync(x => x.Id == collectionId);
    col.ActiveCollectionVariableSetId = set.Id;
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task DeleteCollSubset(Collection c, CollectionVariableSet s)
{
    var msg = s.Entries.Count > 0
        ? $"Delete sub-set \"{s.Name}\" and its {s.Entries.Count} variable(s)?"
        : $"Delete sub-set \"{s.Name}\"?";
    if (!await Confirm(msg)) return;
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableSets.Remove(await db.CollectionVariableSets.FirstAsync(x => x.Id == s.Id));
    if (c.ActiveCollectionVariableSetId == s.Id)
    {
        var col = await db.Collections.FirstAsync(x => x.Id == c.Id);
        col.ActiveCollectionVariableSetId = null;
        c.ActiveCollectionVariableSetId = null;
    }
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task AddCollBaseEntry(int collectionId)
{
    using var db = await DbFactory.CreateDbContextAsync();
    var baseSet = await db.CollectionVariableSets
        .FirstOrDefaultAsync(s => s.CollectionId == collectionId && s.IsBase);
    if (baseSet is null)
    {
        baseSet = new CollectionVariableSet { CollectionId = collectionId, Name = "Base", IsBase = true };
        db.CollectionVariableSets.Add(baseSet);
        await db.SaveChangesAsync();
    }
    db.CollectionVariableEntries.Add(new CollectionVariableEntry { CollectionVariableSetId = baseSet.Id });
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task AddCollSubsetEntry(int setId)
{
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableEntries.Add(new CollectionVariableEntry { CollectionVariableSetId = setId });
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task RemoveCollEntry(CollectionVariableEntry v)
{
    var label = string.IsNullOrWhiteSpace(v.Key) ? "(empty)" : v.Key;
    if (!await Confirm($"Delete variable \"{label}\"?")) return;
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableEntries.Remove(await db.CollectionVariableEntries.FirstAsync(x => x.Id == v.Id));
    await db.SaveChangesAsync();
    State.NotifyChanged();
    await ReloadAsync();
}

private async Task OnCollEntryKeyChanged(CollectionVariableEntry v, ChangeEventArgs e)
{
    v.Key = e.Value?.ToString() ?? "";
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableEntries.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task OnCollEntryValueChanged(CollectionVariableEntry v, ChangeEventArgs e)
{
    v.Value = e.Value?.ToString() ?? "";
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableEntries.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private async Task ToggleCollEntrySecret(CollectionVariableEntry v)
{
    v.IsSecret = !v.IsSecret;
    if (!v.IsSecret) _collVarEntryRevealed.Remove(v.Id);
    using var db = await DbFactory.CreateDbContextAsync();
    db.CollectionVariableEntries.Update(v);
    await db.SaveChangesAsync();
    State.NotifyChanged();
}

private void ToggleCollEntryReveal(CollectionVariableEntry v)
{
    if (!_collVarEntryRevealed.Add(v.Id)) _collVarEntryRevealed.Remove(v.Id);
    StateHasChanged();
}
```

- [ ] **Step 3: Remove Collection.Variables nav prop and AppDbContext relationship**

In `HttpForge/Data/Entities/Collection.cs`, remove the `Variables` property. Final file:
```csharp
namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ActiveCollectionVariableSetId { get; set; }
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariableSet> VariableSets { get; set; } = new();
}
```

In `HttpForge/Data/AppDbContext.cs`, remove the `Collection.Variables` HasMany block from `OnModelCreating`. The block to remove is:
```csharp
b.Entity<Collection>()
    .HasMany(c => c.Variables)
    .WithOne()
    .HasForeignKey(v => v.CollectionId)
    .OnDelete(DeleteBehavior.Cascade);
```
Leave all other relationship configs intact.

- [ ] **Step 4: Add subset-badge CSS**

Append to `HttpForge/Components/Layout/NavMenu.razor.css`:
```css
.subset-badge {
    font-size: 0.6rem;
    background: var(--accent-orange);
    color: #fff;
    padding: 1px 5px;
    border-radius: 3px;
    font-weight: 600;
    margin-left: 2px;
    opacity: 0.85;
}
```

- [ ] **Step 5: Build to verify Task 4 compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add HttpForge/Components/Layout/NavMenu.razor HttpForge/Components/Layout/NavMenu.razor.css HttpForge/Data/Entities/Collection.cs HttpForge/Data/AppDbContext.cs
git commit -m "feat: rework collection variable editor to base+sub-sets, add subset badge"
```

---

## Task 5: Final Build Verification

- [ ] **Step 1: Clean build**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Verify SchemaUpgrader runs without error**

Start the app and check the console for any startup errors:
```bash
dotnet run --project HttpForge/HttpForge.csproj
```
Expected: App starts, no exceptions in console related to SchemaUpgrader or EF.

- [ ] **Step 3: Smoke test checklist**

Verify manually in the browser:
- [ ] Global "Base only" option shows in dropdown; no sub-set selected by default
- [ ] Adding a base global variable persists and appears in autocomplete
- [ ] Creating a global sub-set via "Add" button → sub-set appears in dropdown and is auto-selected
- [ ] Sub-set variable overrides base variable of same key in autocomplete
- [ ] Active global sub-set persists after page refresh
- [ ] Opening ⚙ on a collection shows Base and Sub-set sections
- [ ] Adding a base collection variable works
- [ ] Creating a collection sub-set via "+" → browser prompt for name → sub-set created and selected
- [ ] Sub-set badge appears next to collection name
- [ ] Active collection sub-set persists after page refresh
- [ ] Deleting active sub-set resets to base-only (badge disappears)

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: variable sets feature complete"
```
