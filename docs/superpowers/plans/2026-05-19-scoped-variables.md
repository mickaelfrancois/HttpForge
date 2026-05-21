# Scoped Variables Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter des variables scoppées à trois niveaux (global > collection > request) avec coloration par source dans l'autocomplete et les tooltips.

**Architecture:** Deux nouvelles entités miroir (`CollectionVariable`, `RequestVariable`) avec cascade delete. `AppState.BuildVariables` fusionne les trois couches et retourne une liste typée `ResolvedVariableEntry` portant la source. Tous les composants UI consomment ce type unifié.

**Tech Stack:** Blazor Server, EF Core (SQLite via SchemaUpgrader), C# records, scoped CSS

---

## Cartographie des fichiers

| Fichier | Action | Rôle |
|---|---|---|
| `HttpForge/Data/Entities/CollectionVariable.cs` | Créer | Entité variable de collection |
| `HttpForge/Data/Entities/RequestVariable.cs` | Créer | Entité variable de requête |
| `HttpForge/Data/Entities/Collection.cs` | Modifier | Ajouter nav property `Variables` |
| `HttpForge/Data/Entities/HttpRequestItem.cs` | Modifier | Ajouter nav property `Variables` |
| `HttpForge/Data/AppDbContext.cs` | Modifier | Ajouter DbSets + relations |
| `HttpForge/Data/SchemaUpgrader.cs` | Modifier | Créer les deux nouvelles tables SQLite |
| `HttpForge/Services/AppState.cs` | Modifier | Ajouter `VariableSource`, `ResolvedVariableEntry`, nouveau `BuildVariables` |
| `HttpForge/Services/VariablePreview.cs` | Modifier | Mise à jour signature `Build` |
| `HttpForge/Components/Pages/VariableInput.razor` | Modifier | Nouveau type `EnvVariables`, badge couleur source |
| `HttpForge/Components/Pages/VariableInput.razor.css` | Modifier | Styles `.vi-source-*` |
| `HttpForge/Components/Pages/KeyValueGrid.razor` | Modifier | Nouveau type `EnvVariables` |
| `HttpForge/Components/Pages/Home.razor` | Modifier | Chargement 3 couches, onglet Variables, CRUD RequestVariable |
| `HttpForge/Components/Layout/NavMenu.razor` | Modifier | Éditeur de variables inline par collection |

> **Note de compilation :** Les tâches 3 à 6 forment un groupe solidaire — le projet ne compilera qu'après la fin de la tâche 6. Commiter après chaque tâche sauf entre 3 et 6 (commiter tout après la tâche 6).

---

## Tâche 1 : Entités CollectionVariable et RequestVariable

**Fichiers :**
- Créer : `HttpForge/Data/Entities/CollectionVariable.cs`
- Créer : `HttpForge/Data/Entities/RequestVariable.cs`
- Modifier : `HttpForge/Data/Entities/Collection.cs`
- Modifier : `HttpForge/Data/Entities/HttpRequestItem.cs`

- [ ] **Étape 1 : Créer CollectionVariable.cs**

```csharp
namespace HttpForge.Data.Entities;

public class CollectionVariable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
```

- [ ] **Étape 2 : Créer RequestVariable.cs**

```csharp
namespace HttpForge.Data.Entities;

public class RequestVariable
{
    public int Id { get; set; }
    public int HttpRequestItemId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
```

- [ ] **Étape 3 : Ajouter la nav property Variables à Collection.cs**

Remplacer le contenu de `Collection.cs` par :

```csharp
namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariable> Variables { get; set; } = new();
}
```

- [ ] **Étape 4 : Ajouter la nav property Variables à HttpRequestItem.cs**

Ajouter après `public List<FormFieldItem> FormFields { get; set; } = new();` :

```csharp
    public List<RequestVariable> Variables { get; set; } = new();
```

- [ ] **Étape 5 : Vérifier la compilation**

```powershell
cd D:\Development\HttpForge\HttpForge
dotnet build
```

Résultat attendu : `Build succeeded.`

- [ ] **Étape 6 : Commit**

```powershell
git add HttpForge/Data/Entities/CollectionVariable.cs HttpForge/Data/Entities/RequestVariable.cs HttpForge/Data/Entities/Collection.cs HttpForge/Data/Entities/HttpRequestItem.cs
git commit -m "feat: add CollectionVariable and RequestVariable entities"
```

---

## Tâche 2 : Câblage base de données

**Fichiers :**
- Modifier : `HttpForge/Data/AppDbContext.cs`
- Modifier : `HttpForge/Data/SchemaUpgrader.cs`

- [ ] **Étape 1 : Mettre à jour AppDbContext.cs**

Remplacer le contenu par :

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
    public DbSet<CollectionVariable> CollectionVariables => Set<CollectionVariable>();
    public DbSet<RequestVariable> RequestVariables => Set<RequestVariable>();

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

- [ ] **Étape 2 : Mettre à jour SchemaUpgrader.cs**

Remplacer le contenu par :

```csharp
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "EnvironmentVariables", "IsSecret", "INTEGER NOT NULL DEFAULT 0");
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
    }

    private static void EnsureTable(AppDbContext db, string table, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        var count = (long)check.ExecuteScalar()!;
        if (count > 0) return;

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
        reader.Close();

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        alter.ExecuteNonQuery();
    }
}
```

- [ ] **Étape 3 : Vérifier la compilation**

```powershell
dotnet build
```

Résultat attendu : `Build succeeded.`

- [ ] **Étape 4 : Commit**

```powershell
git add HttpForge/Data/AppDbContext.cs HttpForge/Data/SchemaUpgrader.cs
git commit -m "feat: wire CollectionVariables and RequestVariables to DbContext and SchemaUpgrader"
```

---

## Tâche 3 : AppState — types de résolution + nouveau BuildVariables

**Fichier :**
- Modifier : `HttpForge/Services/AppState.cs`

> ⚠️ Cette tâche supprime l'ancienne méthode `BuildVariables(AppEnvironment?)`. Le projet ne compilera plus jusqu'à la fin de la tâche 6. Ne pas commiter avant la tâche 6.

- [ ] **Étape 1 : Remplacer AppState.cs**

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
        AppEnvironment? env,
        Collection? collection,
        HttpRequestItem? request)
    {
        var merged = new Dictionary<string, ResolvedVariableEntry>(StringComparer.OrdinalIgnoreCase);

        if (env is not null)
            foreach (var v in env.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (collection is not null)
            foreach (var v in collection.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (request is not null)
            foreach (var v in request.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Request);

        return merged.Values.OrderBy(v => v.Key).ToList();
    }
}
```

---

## Tâche 4 : VariablePreview — mise à jour de la signature Build

**Fichier :**
- Modifier : `HttpForge/Services/VariablePreview.cs`

> ⚠️ Toujours dans le groupe non-compilable. Ne pas commiter avant la tâche 6.

- [ ] **Étape 1 : Remplacer VariablePreview.cs**

```csharp
using System.Text.RegularExpressions;

namespace HttpForge.Services;

public static partial class VariablePreview
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_\-\.]+)\s*\}\}")]
    private static partial Regex Pattern();

    public static string Build(string? input, IReadOnlyList<ResolvedVariableEntry> variables)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var matches = Pattern().Matches(input);
        if (matches.Count == 0) return string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var key = m.Groups[1].Value;
            if (!seen.Add(key)) continue;
            var found = variables.FirstOrDefault(
                v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
            if (found is null)
                lines.Add($"{{{{{key}}}}} → (not defined)");
            else if (found.IsSecret)
                lines.Add($"{{{{{key}}}}} → (secret) [{found.Source}]");
            else
                lines.Add($"{{{{{key}}}}} → {found.Value} [{found.Source}]");
        }
        return string.Join("\n", lines);
    }
}
```

---

## Tâche 5 : VariableInput + KeyValueGrid — nouveau type EnvVariables + couleurs source

**Fichiers :**
- Modifier : `HttpForge/Components/Pages/VariableInput.razor`
- Modifier : `HttpForge/Components/Pages/VariableInput.razor.css`
- Modifier : `HttpForge/Components/Pages/KeyValueGrid.razor`

> ⚠️ Toujours dans le groupe non-compilable. Ne pas commiter avant la tâche 6.

- [ ] **Étape 1 : Remplacer VariableInput.razor**

```razor
@rendermode InteractiveServer
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="vi-wrap">
    @if (Multiline)
    {
        <textarea @ref="_ref" class="@CssClass" placeholder="@Placeholder" title="@Title" spellcheck="false"
                  value="@Value"
                  @oninput="OnInput"
                  @onblur="OnBlurInternal"></textarea>
    }
    else
    {
        <input @ref="_ref" class="@CssClass" placeholder="@Placeholder" title="@Title"
               value="@Value"
               @oninput="OnInput"
               @onblur="OnBlurInternal" />
    }
    @if (_open && _matches.Count > 0)
    {
        <div class="vi-dropdown">
            @for (var i = 0; i < _matches.Count; i++)
            {
                var idx = i;
                var v = _matches[idx];
                <div class="vi-item @(idx == _selectedIndex ? "selected" : "")"
                     @onmousedown:preventDefault @onclick="() => PickAsync(v)">
                    <span class="vi-key">@v.Key</span>
                    <span class="vi-source vi-source-@v.Source.ToString().ToLower()">@v.Source</span>
                    <span class="vi-val">@(v.IsSecret ? "(secret)" : v.Value)</span>
                </div>
            }
        </div>
    }
</div>

@code {
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string? Placeholder { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public bool Multiline { get; set; }
    [Parameter] public IReadOnlyList<ResolvedVariableEntry> EnvVariables { get; set; } = Array.Empty<ResolvedVariableEntry>();
    [Parameter] public EventCallback OnBlur { get; set; }

    private ElementReference _ref;
    private bool _open;
    private int _triggerIndex = -1;
    private int _selectedIndex;
    private List<ResolvedVariableEntry> _matches = new();
    private int? _pendingCaret;
    private DotNetObjectReference<VariableInput>? _selfRef;
    private bool _jsAttached;
    private bool _lastOpenState;

    private async Task OnInput(ChangeEventArgs e)
    {
        Value = e.Value?.ToString() ?? string.Empty;
        Detect();
        await ValueChanged.InvokeAsync(Value);
    }

    private void Detect()
    {
        _open = false;
        _matches = new();
        _triggerIndex = -1;
        _selectedIndex = 0;
        if (string.IsNullOrEmpty(Value)) return;

        var idx = Value.LastIndexOf("{{", StringComparison.Ordinal);
        if (idx < 0) return;
        var after = Value.Substring(idx + 2);
        if (after.Contains("}}")) return;

        foreach (var c in after)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
                return;
        }

        _triggerIndex = idx;
        _matches = EnvVariables
            .Where(v => v.Key.StartsWith(after, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Key)
            .Take(10)
            .ToList();
        _open = _matches.Count > 0;
    }

    private async Task PickAsync(ResolvedVariableEntry v)
    {
        if (_triggerIndex < 0 || string.IsNullOrEmpty(Value)) { _open = false; return; }
        var insertion = "{{" + v.Key + "}}";
        Value = Value.Substring(0, _triggerIndex) + insertion;
        _pendingCaret = _triggerIndex + insertion.Length;
        _open = false;
        _matches = new();
        _triggerIndex = -1;
        _selectedIndex = 0;
        await ValueChanged.InvokeAsync(Value);
    }

    private async Task OnBlurInternal()
    {
        _open = false;
        await OnBlur.InvokeAsync();
    }

    [JSInvokable]
    public async Task OnNavigate(int delta)
    {
        if (_matches.Count == 0) return;
        _selectedIndex = (_selectedIndex + delta + _matches.Count) % _matches.Count;
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnSelectCurrent()
    {
        if (_matches.Count == 0) return;
        var idx = Math.Clamp(_selectedIndex, 0, _matches.Count - 1);
        await InvokeAsync(() => PickAsync(_matches[idx]));
    }

    [JSInvokable]
    public async Task OnEscape()
    {
        _open = false;
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _selfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("forge.attach", _ref, _selfRef);
            _jsAttached = true;
        }

        if (_jsAttached && _open != _lastOpenState)
        {
            _lastOpenState = _open;
            await JS.InvokeVoidAsync("forge.setOpen", _ref, _open);
        }

        if (_pendingCaret is int pos)
        {
            _pendingCaret = null;
            await JS.InvokeVoidAsync("forge.setCaret", _ref, pos);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsAttached)
        {
            try { await JS.InvokeVoidAsync("forge.detach", _ref); }
            catch { /* circuit may already be torn down */ }
        }
        _selfRef?.Dispose();
    }
}
```

- [ ] **Étape 2 : Ajouter les styles source dans VariableInput.razor.css**

Ajouter à la fin du fichier existant :

```css
.vi-source {
    font-size: 0.65rem;
    padding: 1px 5px;
    border-radius: 3px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    flex-shrink: 0;
}

.vi-source-global {
    background: #1a3a5c;
    color: #5aacf0;
}

.vi-source-collection {
    background: #3d2600;
    color: #f0a050;
}

.vi-source-request {
    background: #0d3320;
    color: #4ecb8d;
}
```

- [ ] **Étape 3 : Mettre à jour KeyValueGrid.razor**

Remplacer le contenu par :

```razor
@rendermode InteractiveServer

<div class="kv-grid">
    <div class="kv-header">
        <span></span>
        <span>Key</span>
        <span>Value</span>
        <span></span>
    </div>
    @foreach (var item in Items)
    {
        <div class="kv-row">
            <input type="checkbox" checked="@EnabledOf(item!)" @onchange="@(e => OnEnabledChanged(item!, e))" />
            <VariableInput Value="@KeyOf(item!)"
                           ValueChanged="@(v => OnKeyChanged(item!, v))"
                           OnBlur="@(() => OnChanged.InvokeAsync(item!))"
                           Placeholder="key"
                           Title="@VariablePreview.Build(KeyOf(item!), EnvVariables)"
                           EnvVariables="EnvVariables" />
            <VariableInput Value="@ValueOf(item!)"
                           ValueChanged="@(v => OnValueChanged(item!, v))"
                           OnBlur="@(() => OnChanged.InvokeAsync(item!))"
                           Placeholder="value"
                           Title="@VariablePreview.Build(ValueOf(item!), EnvVariables)"
                           EnvVariables="EnvVariables" />
            <button class="row-del" @onclick="@(() => OnRemove.InvokeAsync(item!))">✕</button>
        </div>
    }
    <button class="add-row" @onclick="AddRow">+ add row</button>
</div>

@code {
    [Parameter, EditorRequired] public required System.Collections.IList Items { get; set; }
    [Parameter, EditorRequired] public required Func<object> ItemCreator { get; set; }
    [Parameter, EditorRequired] public required Func<object, string> KeyOf { get; set; }
    [Parameter, EditorRequired] public required Func<object, string> ValueOf { get; set; }
    [Parameter, EditorRequired] public required Func<object, bool> EnabledOf { get; set; }
    [Parameter, EditorRequired] public required Action<object, string> SetKey { get; set; }
    [Parameter, EditorRequired] public required Action<object, string> SetValue { get; set; }
    [Parameter, EditorRequired] public required Action<object, bool> SetEnabled { get; set; }
    [Parameter] public EventCallback<object> OnChanged { get; set; }
    [Parameter] public EventCallback<object> OnRemove { get; set; }
    [Parameter] public IReadOnlyList<ResolvedVariableEntry> EnvVariables { get; set; } = Array.Empty<ResolvedVariableEntry>();

    private async Task AddRow()
    {
        var item = ItemCreator();
        Items.Add(item);
        await OnChanged.InvokeAsync(item);
    }

    private void OnKeyChanged(object item, string value) => SetKey(item, value);

    private void OnValueChanged(object item, string value) => SetValue(item, value);

    private async Task OnEnabledChanged(object item, ChangeEventArgs e)
    {
        SetEnabled(item, e.Value is bool b && b);
        await OnChanged.InvokeAsync(item);
    }
}
```

---

## Tâche 6 : Home.razor — chargement trois couches + onglet Variables

**Fichier :**
- Modifier : `HttpForge/Components/Pages/Home.razor`

- [ ] **Étape 1 : Remplacer Home.razor**

```razor
@page "/"
@rendermode InteractiveServer
@inject IDbContextFactory<AppDbContext> DbFactory
@inject AppState State
@inject RequestExecutor Executor
@implements IDisposable

<PageTitle>HttpForge</PageTitle>

@if (_request is null)
{
    <div class="empty-state">
        <h2>HttpForge</h2>
        <p>Select or create a request from the sidebar to get started.</p>
    </div>
}
else
{
    <div class="editor">
        <div class="request-name-row">
            <input class="request-name-input" value="@_request.Name"
                   @oninput="OnNameInput" @onblur="SaveRequestDebounced" placeholder="Request name" />
        </div>
        <div class="url-bar">
            <select class="method-select method-@_request.Method.ToString().ToLower()"
                    @onchange="OnMethodChanged">
                @foreach (var m in Enum.GetValues<HttpMethodKind>())
                {
                    <option value="@m" selected="@(_request.Method == m)">@m</option>
                }
            </select>
            <VariableInput Value="@_request.Url"
                           ValueChanged="OnUrlChanged"
                           OnBlur="SaveRequestDebounced"
                           CssClass="url-input"
                           Placeholder="https://api.example.com/path"
                           Title="@Tooltip(_request.Url)"
                           EnvVariables="_resolvedVariables" />
            <button class="send-btn" @onclick="SendAsync" disabled="@_sending">
                @(_sending ? "Sending…" : "Send")
            </button>
        </div>

        <div class="tabs">
            @foreach (var t in _tabs)
            {
                <button class="tab @(t == _activeTab ? "active" : "")" @onclick="() => _activeTab = t">@t</button>
            }
        </div>

        <div class="tab-body">
            @switch (_activeTab)
            {
                case "Params":
                    <KeyValueGrid Items="_request.QueryParams"
                                  ItemCreator="@(() => new QueryParamItem { HttpRequestItemId = _request.Id })"
                                  KeyOf="@(i => ((QueryParamItem)i).Key)"
                                  ValueOf="@(i => ((QueryParamItem)i).Value)"
                                  EnabledOf="@(i => ((QueryParamItem)i).Enabled)"
                                  SetKey="@((i, v) => ((QueryParamItem)i).Key = v)"
                                  SetValue="@((i, v) => ((QueryParamItem)i).Value = v)"
                                  SetEnabled="@((i, v) => ((QueryParamItem)i).Enabled = v)"
                                  OnChanged="SaveQueryParamAsync"
                                  OnRemove="RemoveQueryParamAsync"
                                  EnvVariables="_resolvedVariables" />
                    break;
                case "Headers":
                    <KeyValueGrid Items="_request.Headers"
                                  ItemCreator="@(() => new HeaderItem { HttpRequestItemId = _request.Id })"
                                  KeyOf="@(i => ((HeaderItem)i).Key)"
                                  ValueOf="@(i => ((HeaderItem)i).Value)"
                                  EnabledOf="@(i => ((HeaderItem)i).Enabled)"
                                  SetKey="@((i, v) => ((HeaderItem)i).Key = v)"
                                  SetValue="@((i, v) => ((HeaderItem)i).Value = v)"
                                  SetEnabled="@((i, v) => ((HeaderItem)i).Enabled = v)"
                                  OnChanged="SaveHeaderAsync"
                                  OnRemove="RemoveHeaderAsync"
                                  EnvVariables="_resolvedVariables" />
                    break;
                case "Body":
                    <div class="body-editor">
                        <div class="body-kind-row">
                            @foreach (var bk in Enum.GetValues<BodyKind>())
                            {
                                <label class="radio">
                                    <input type="radio" name="bodykind"
                                           checked="@(_request.BodyKind == bk)"
                                           @onchange="@(_ => OnBodyKindChanged(bk))" />
                                    @bk
                                </label>
                            }
                        </div>
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
                        else if (_request.BodyKind == BodyKind.FormUrlEncoded)
                        {
                            <KeyValueGrid Items="_request.FormFields"
                                          ItemCreator="@(() => new FormFieldItem { HttpRequestItemId = _request.Id })"
                                          KeyOf="@(i => ((FormFieldItem)i).Key)"
                                          ValueOf="@(i => ((FormFieldItem)i).Value)"
                                          EnabledOf="@(i => ((FormFieldItem)i).Enabled)"
                                          SetKey="@((i, v) => ((FormFieldItem)i).Key = v)"
                                          SetValue="@((i, v) => ((FormFieldItem)i).Value = v)"
                                          SetEnabled="@((i, v) => ((FormFieldItem)i).Enabled = v)"
                                          OnChanged="SaveFormFieldAsync"
                                          OnRemove="RemoveFormFieldAsync"
                                          EnvVariables="_resolvedVariables" />
                        }
                        else
                        {
                            <div class="empty-hint">No body will be sent.</div>
                        }
                    </div>
                    break;
                case "Variables":
                    <KeyValueGrid Items="_request.Variables"
                                  ItemCreator="@(() => new RequestVariable { HttpRequestItemId = _request.Id })"
                                  KeyOf="@(i => ((RequestVariable)i).Key)"
                                  ValueOf="@(i => ((RequestVariable)i).Value)"
                                  EnabledOf="@(_ => true)"
                                  SetKey="@((i, v) => ((RequestVariable)i).Key = v)"
                                  SetValue="@((i, v) => ((RequestVariable)i).Value = v)"
                                  SetEnabled="@((i, v) => { })"
                                  OnChanged="SaveRequestVarAsync"
                                  OnRemove="RemoveRequestVarAsync"
                                  EnvVariables="_resolvedVariables" />
                    break;
            }
        </div>

        <div class="response-pane">
            @if (_result is null)
            {
                <div class="response-empty">No response yet. Click <b>Send</b>.</div>
            }
            else if (_result.Error is not null)
            {
                <div class="response-error">
                    <strong>Error:</strong> @_result.Error
                </div>
            }
            else
            {
                <div class="response-meta">
                    <span class="status status-@StatusClass(_result.StatusCode)">@_result.StatusCode @_result.ReasonPhrase</span>
                    <span>⏱ @_result.ElapsedMs ms</span>
                    <span>📦 @_result.BodyBytes B</span>
                    <button class="tab @(_responseTab == "Body" ? "active" : "")" @onclick="@(() => _responseTab = "Body")">Body</button>
                    <button class="tab @(_responseTab == "Headers" ? "active" : "")" @onclick="@(() => _responseTab = "Headers")">Headers (@_result.Headers.Count)</button>
                </div>
                <div class="response-body">
                    @if (_responseTab == "Body")
                    {
                        <pre>@FormatBody(_result.Body)</pre>
                    }
                    else
                    {
                        <table class="header-table">
                            @foreach (var h in _result.Headers.OrderBy(h => h.Key))
                            {
                                <tr><th>@h.Key</th><td>@h.Value</td></tr>
                            }
                        </table>
                    }
                </div>
            }
        </div>
    </div>
}

@code {
    private HttpRequestItem? _request;
    private AppEnvironment? _env;
    private Collection? _collection;
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
        await LoadEnvAsync();
        await LoadRequestAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadEnvAsync()
    {
        if (State.SelectedEnvironmentId is null)
        {
            _env = null;
            RebuildVariables();
            return;
        }
        using var db = await DbFactory.CreateDbContextAsync();
        _env = await db.Environments
            .Include(e => e.Variables)
            .FirstOrDefaultAsync(e => e.Id == State.SelectedEnvironmentId);
        RebuildVariables();
    }

    private async Task LoadRequestAsync()
    {
        if (State.SelectedRequestId is null)
        {
            _request = null;
            _collection = null;
            _result = null;
            RebuildVariables();
            return;
        }

        using var db = await DbFactory.CreateDbContextAsync();
        _request = await db.Requests
            .Include(r => r.Headers)
            .Include(r => r.QueryParams)
            .Include(r => r.FormFields)
            .Include(r => r.Variables)
            .FirstOrDefaultAsync(r => r.Id == State.SelectedRequestId);

        _collection = _request is null ? null : await db.Collections
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == _request.CollectionId);

        RebuildVariables();
    }

    private void RebuildVariables()
    {
        _resolvedVariables = State.BuildVariables(_env, _collection, _request);
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

- [ ] **Étape 2 : Vérifier la compilation (groupe tâches 3-6)**

```powershell
cd D:\Development\HttpForge\HttpForge
dotnet build
```

Résultat attendu : `Build succeeded.`

- [ ] **Étape 3 : Commit groupé des tâches 3 à 6**

```powershell
git add HttpForge/Services/AppState.cs `
        HttpForge/Services/VariablePreview.cs `
        HttpForge/Components/Pages/VariableInput.razor `
        HttpForge/Components/Pages/VariableInput.razor.css `
        HttpForge/Components/Pages/KeyValueGrid.razor `
        HttpForge/Components/Pages/Home.razor
git commit -m "feat: wire three-layer variable resolution with source colors and Variables tab"
```

---

## Tâche 7 : NavMenu.razor — éditeur de variables de collection

**Fichier :**
- Modifier : `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Étape 1 : Ajouter les champs d'état pour l'éditeur de variables de collection**

Dans le bloc `@code`, ajouter après `private HashSet<int> _revealed = new();` :

```csharp
    private HashSet<int> _collVarEditorOpen = new();
    private Dictionary<int, List<CollectionVariable>> _collVars = new();
    private HashSet<int> _collVarRevealed = new();
```

- [ ] **Étape 2 : Mettre à jour ReloadAsync pour charger les variables de collection**

Remplacer la méthode `ReloadAsync` existante par :

```csharp
    private async Task ReloadAsync()
    {
        using var db = await DbFactory.CreateDbContextAsync();
        _collections = await db.Collections
            .Include(c => c.Requests)
            .Include(c => c.Variables)
            .OrderBy(c => c.Name)
            .ToListAsync();
        _environments = await db.Environments
            .Include(e => e.Variables)
            .OrderBy(e => e.Name)
            .ToListAsync();
        _selectedEnv = State.SelectedEnvironmentId is int id
            ? _environments.FirstOrDefault(e => e.Id == id)
            : null;
        _collVars = _collections.ToDictionary(c => c.Id, c => c.Variables);
    }
```

- [ ] **Étape 3 : Ajouter le bouton ⚙ et l'éditeur inline dans le template collection**

Remplacer le bloc `<div class="collection-header">` existant par :

```razor
            <div class="collection-header">
                <button class="collection-toggle" @onclick="() => ToggleCollection(c.Id)">
                    <span class="caret">@(_expanded.Contains(c.Id) ? "▾" : "▸")</span>
                    <span>@c.Name</span>
                </button>
                <button class="icon-btn @(_collVarEditorOpen.Contains(c.Id) ? "active" : "")"
                        title="Variables" @onclick="() => ToggleCollVarEditor(c.Id)">⚙</button>
                <button class="icon-btn" title="New request" @onclick="() => AddRequest(c)">+</button>
                <button class="icon-btn" title="Delete collection" @onclick="() => DeleteCollection(c)">🗑</button>
            </div>
            @if (_collVarEditorOpen.Contains(c.Id) && _collVars.TryGetValue(c.Id, out var cvars))
            {
                <div class="env-editor">
                    <div class="var-list">
                        @foreach (var v in cvars)
                        {
                            var masked = v.IsSecret && !_collVarRevealed.Contains(v.Id);
                            <div class="var-row">
                                <input placeholder="key" value="@v.Key"
                                       @oninput="e => OnCollVarKeyChanged(v, e)" />
                                <input placeholder="value" type="@(masked ? "password" : "text")"
                                       value="@v.Value"
                                       @oninput="e => OnCollVarValueChanged(v, e)" />
                                @if (v.IsSecret)
                                {
                                    <button class="icon-btn" title="@(masked ? "Reveal" : "Hide")"
                                            @onclick="() => ToggleCollVarReveal(v)">@(masked ? "👁" : "🚫")</button>
                                }
                                <button class="icon-btn @(v.IsSecret ? "active" : "")"
                                        title="@(v.IsSecret ? "Unmark as secret" : "Mark as secret")"
                                        @onclick="() => ToggleCollVarSecret(v)">@(v.IsSecret ? "🔒" : "🔓")</button>
                                <button class="icon-btn" @onclick="() => RemoveCollVar(v, c.Id)">✕</button>
                            </div>
                        }
                        <button class="link-btn" @onclick="() => AddCollVar(c.Id)">+ add variable</button>
                    </div>
                </div>
            }
```

- [ ] **Étape 4 : Ajouter les méthodes de gestion des variables de collection**

Ajouter dans le bloc `@code`, avant la méthode `Confirm` :

```csharp
    private void ToggleCollVarEditor(int collectionId)
    {
        if (!_collVarEditorOpen.Add(collectionId))
            _collVarEditorOpen.Remove(collectionId);
    }

    private async Task AddCollVar(int collectionId)
    {
        using var db = await DbFactory.CreateDbContextAsync();
        var v = new CollectionVariable { CollectionId = collectionId, Key = "", Value = "" };
        db.CollectionVariables.Add(v);
        await db.SaveChangesAsync();
        await ReloadAsync();
        State.NotifyChanged();
    }

    private async Task RemoveCollVar(CollectionVariable v, int collectionId)
    {
        var label = string.IsNullOrWhiteSpace(v.Key) ? "(empty)" : v.Key;
        if (!await Confirm($"Delete variable \"{label}\"?")) return;
        using var db = await DbFactory.CreateDbContextAsync();
        db.CollectionVariables.Remove(await db.CollectionVariables.FirstAsync(x => x.Id == v.Id));
        await db.SaveChangesAsync();
        await ReloadAsync();
        State.NotifyChanged();
    }

    private async Task OnCollVarKeyChanged(CollectionVariable v, ChangeEventArgs e)
    {
        v.Key = e.Value?.ToString() ?? "";
        using var db = await DbFactory.CreateDbContextAsync();
        db.CollectionVariables.Update(v);
        await db.SaveChangesAsync();
        State.NotifyChanged();
    }

    private async Task OnCollVarValueChanged(CollectionVariable v, ChangeEventArgs e)
    {
        v.Value = e.Value?.ToString() ?? "";
        using var db = await DbFactory.CreateDbContextAsync();
        db.CollectionVariables.Update(v);
        await db.SaveChangesAsync();
        State.NotifyChanged();
    }

    private async Task ToggleCollVarSecret(CollectionVariable v)
    {
        v.IsSecret = !v.IsSecret;
        if (!v.IsSecret) _collVarRevealed.Remove(v.Id);
        using var db = await DbFactory.CreateDbContextAsync();
        db.CollectionVariables.Update(v);
        await db.SaveChangesAsync();
        State.NotifyChanged();
    }

    private void ToggleCollVarReveal(CollectionVariable v)
    {
        if (!_collVarRevealed.Add(v.Id)) _collVarRevealed.Remove(v.Id);
    }
```

- [ ] **Étape 5 : Vérifier la compilation**

```powershell
cd D:\Development\HttpForge\HttpForge
dotnet build
```

Résultat attendu : `Build succeeded.`

- [ ] **Étape 6 : Vérifier manuellement dans le navigateur**

Lancer l'application (`dotnet run`) et vérifier :
1. Cliquer sur ⚙ d'une collection → éditeur inline s'ouvre
2. Ajouter une variable de collection (ex: `base_url` = `https://api.example.com`)
3. Ouvrir une requête de cette collection → taper `{{base` dans l'URL → suggestion orange "Collection" apparaît
4. Si un set global actif a aussi `base_url`, la version collection prend la priorité (orange dans l'autocomplete)
5. Aller dans l'onglet "Variables" de la requête → ajouter `base_url` → taper `{{base` → suggestion verte "Request" apparaît et prend la priorité
6. Le tooltip de l'URL affiche `{{base_url}} → https://api.example.com [Collection]`

- [ ] **Étape 7 : Commit**

```powershell
git add HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: add per-collection variable editor in sidebar"
```
