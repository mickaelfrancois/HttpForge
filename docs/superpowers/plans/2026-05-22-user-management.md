# User Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-user, multi-team authentication and authorization to HttpForge using ASP.NET Core Identity with SSO + invitation-based registration.

**Architecture:** `AppDbContext` extends `IdentityDbContext<AppUser>`, sharing the same SQLite file. A scoped `PermissionService` resolves team roles per collection by querying `TeamMembers`. `AppState.IsReadOnly` is set by `Home.razor` on each request load. All Blazor mutation paths check `State.IsReadOnly` before writing. External OAuth is handled by minimal API endpoints outside the Blazor circuit.

**Tech Stack:** ASP.NET Core Identity 9.x, EF Core SQLite 9.x, `Microsoft.AspNetCore.Authentication.Google`, `Microsoft.AspNetCore.Authentication.MicrosoftAccount`, `AspNet.Security.OAuth.GitHub`, Blazor Server (InteractiveServer)

---

## File Map

**New files:**
- `HttpForge/Data/Entities/AppUser.cs`
- `HttpForge/Data/Entities/Team.cs`
- `HttpForge/Data/Entities/TeamMember.cs` — defines `TeamRole` enum
- `HttpForge/Data/Entities/InvitationToken.cs`
- `HttpForge/Services/InvitationService.cs`
- `HttpForge/Services/PermissionService.cs`
- `HttpForge/Services/TeamService.cs`
- `HttpForge/Services/SuperAdminBootstrap.cs`
- `HttpForge/Components/Pages/LoginPage.razor`
- `HttpForge/Components/Pages/InvitePage.razor`
- `HttpForge/Components/Pages/AdminPage.razor`
- `HttpForge/Components/Pages/TeamPage.razor`
- `HttpForge/Components/Layout/RedirectToLogin.razor`
- `HttpForge.Tests/Services/InvitationServiceTests.cs`
- `HttpForge.Tests/Services/PermissionServiceTests.cs`
- `HttpForge.Tests/Helpers/TestDbContextFactory.cs`

**Modified files:**
- `HttpForge/HttpForge.csproj` — add Identity + OAuth packages
- `HttpForge.Tests/HttpForge.Tests.csproj` — add Identity package
- `HttpForge/Data/AppDbContext.cs` — extend `IdentityDbContext<AppUser>`, add new DbSets
- `HttpForge/Data/Entities/Collection.cs` — add `TeamId`
- `HttpForge/Data/SchemaUpgrader.cs` — add Teams/TeamMembers/InvitationTokens tables + Collections.TeamId column
- `HttpForge/Program.cs` — Identity services, auth middleware, OAuth endpoints, bootstrap
- `HttpForge/Services/AppState.cs` — add `SelectedCollectionId`, `IsReadOnly`
- `HttpForge/Components/_Imports.razor` — add auth namespaces
- `HttpForge/Components/Routes.razor` — `AuthorizeRouteView` + `RedirectToLogin`
- `HttpForge/Components/Layout/MainLayout.razor` — user header with logout
- `HttpForge/Components/Layout/NavMenu.razor` — filter by team, permission-aware context menus
- `HttpForge/Components/Layout/CollectionNode.razor` — hide edit/delete for Guest
- `HttpForge/Components/Layout/RequestRow.razor` — hide rename/delete for Guest
- `HttpForge/Components/Pages/Home.razor` — inject auth + PermissionService, Guest banner, gate mutations

---

## Task 1: NuGet packages

**Files:**
- Modify: `HttpForge/HttpForge.csproj`
- Modify: `HttpForge.Tests/HttpForge.Tests.csproj`

- [ ] **Step 1: Add packages to main project**

```powershell
dotnet add HttpForge/HttpForge.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.5
dotnet add HttpForge/HttpForge.csproj package Microsoft.AspNetCore.Authentication.Google
dotnet add HttpForge/HttpForge.csproj package Microsoft.AspNetCore.Authentication.MicrosoftAccount
dotnet add HttpForge/HttpForge.csproj package AspNet.Security.OAuth.GitHub
```

- [ ] **Step 2: Add Identity package to test project**

```powershell
dotnet add HttpForge.Tests/HttpForge.Tests.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.5
```

- [ ] **Step 3: Verify build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```
git add HttpForge/HttpForge.csproj HttpForge.Tests/HttpForge.Tests.csproj
git commit -m "chore: add Identity and OAuth NuGet packages"
```

---

## Task 2: AppUser entity + AppDbContext base class

**Files:**
- Create: `HttpForge/Data/Entities/AppUser.cs`
- Modify: `HttpForge/Data/AppDbContext.cs`

- [ ] **Step 1: Create AppUser.cs**

```csharp
using Microsoft.AspNetCore.Identity;

namespace HttpForge.Data.Entities;

public class AppUser : IdentityUser { }
```

- [ ] **Step 2: Update AppDbContext**

Change `public class AppDbContext : DbContext` to `public class AppDbContext : IdentityDbContext<AppUser>` and add `base.OnModelCreating(b)` as the first call in `OnModelCreating`. Add the three new DbSets. Full file:

```csharp
using HttpForge.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public class AppDbContext : IdentityDbContext<AppUser>
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
    public DbSet<CollectionVariableSet> CollectionVariableSets => Set<CollectionVariableSet>();
    public DbSet<CollectionVariableEntry> CollectionVariableEntries => Set<CollectionVariableEntry>();
    public DbSet<CollectionFolder> CollectionFolders => Set<CollectionFolder>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // must be first — registers Identity entity configurations

        b.Entity<Collection>()
            .HasMany(c => c.Requests)
            .WithOne(r => r.Collection!)
            .HasForeignKey(r => r.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Collection>()
            .HasMany(c => c.Folders)
            .WithOne(f => f.Collection!)
            .HasForeignKey(f => f.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<CollectionFolder>()
            .HasMany(f => f.Children)
            .WithOne(f => f.ParentFolder)
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.ClientCascade);

        b.Entity<CollectionFolder>()
            .HasMany(f => f.Requests)
            .WithOne(r => r.Folder)
            .HasForeignKey(r => r.FolderId)
            .OnDelete(DeleteBehavior.ClientCascade);

        b.Entity<Collection>()
            .HasMany(c => c.VariableSets)
            .WithOne()
            .HasForeignKey(s => s.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<CollectionVariableSet>()
            .HasMany(s => s.Entries)
            .WithOne(e => e.VariableSet)
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

        b.Entity<AppSettings>().ToTable("AppSettings");
    }
}
```

- [ ] **Step 3: Delete existing database** (clean break per spec — existing data not migrated)

```powershell
Remove-Item -Path "HttpForge\httpforge.db" -ErrorAction SilentlyContinue
```

- [ ] **Step 4: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```
git add HttpForge/Data/Entities/AppUser.cs HttpForge/Data/AppDbContext.cs
git commit -m "feat: switch AppDbContext to IdentityDbContext<AppUser>"
```

---

## Task 3: Team/TeamMember/InvitationToken entities + Collection.TeamId + SchemaUpgrader

**Files:**
- Create: `HttpForge/Data/Entities/Team.cs`
- Create: `HttpForge/Data/Entities/TeamMember.cs`
- Create: `HttpForge/Data/Entities/InvitationToken.cs`
- Modify: `HttpForge/Data/Entities/Collection.cs`
- Modify: `HttpForge/Data/SchemaUpgrader.cs`

- [ ] **Step 1: Create Team.cs**

```csharp
namespace HttpForge.Data.Entities;

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TeamMember> Members { get; set; } = [];
}
```

- [ ] **Step 2: Create TeamMember.cs**

```csharp
namespace HttpForge.Data.Entities;

public enum TeamRole { TeamAdmin, Contributor, Guest }

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public TeamRole Role { get; set; }
    public Team Team { get; set; } = null!; // navigation property used by TeamService
}
```

- [ ] **Step 3: Create InvitationToken.cs**

```csharp
namespace HttpForge.Data.Entities;

public class InvitationToken
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // TeamRole name or "SuperAdmin"
    public string Token { get; set; } = string.Empty; // cryptographically random 32-byte hex
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
```

- [ ] **Step 4: Add TeamId to Collection.cs**

Add `public int? TeamId { get; set; }` after `ActiveCollectionVariableSetId`:

```csharp
namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ActiveCollectionVariableSetId { get; set; }
    public int? TeamId { get; set; }
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariableSet> VariableSets { get; set; } = new();
    public List<CollectionFolder> Folders { get; set; } = [];
}
```

- [ ] **Step 5: Extend SchemaUpgrader._allowedTables**

Add `"Teams"`, `"TeamMembers"`, `"InvitationTokens"` to the HashSet:

```csharp
private static readonly HashSet<string> _allowedTables =
[
    "Collections", "Environments", "EnvironmentVariables",
    "CollectionVariables", "RequestVariables", "AppSettings",
    "CollectionVariableSets", "CollectionVariableEntries", "Requests",
    "CollectionFolders", "Teams", "TeamMembers", "InvitationTokens"
];
```

- [ ] **Step 6: Add calls at end of SchemaUpgrader.Apply()**

Append these lines at the end of `Apply()`, after the existing `EnsureColumn(db, "Requests", "FolderId", ...)` call and the private method calls:

```csharp
EnsureColumn(db, "Collections", "TeamId", "INTEGER NULL");

EnsureTable(db, "Teams",
    "CREATE TABLE \"Teams\" (" +
    "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
    "\"Name\" TEXT NOT NULL DEFAULT '', " +
    "\"CreatedAt\" TEXT NOT NULL DEFAULT (datetime('now')));");

EnsureTable(db, "TeamMembers",
    "CREATE TABLE \"TeamMembers\" (" +
    "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
    "\"TeamId\" INTEGER NOT NULL, " +
    "\"UserId\" TEXT NOT NULL DEFAULT '', " +
    "\"Role\" INTEGER NOT NULL DEFAULT 1, " +
    "FOREIGN KEY (\"TeamId\") REFERENCES \"Teams\"(\"Id\") ON DELETE CASCADE);");

EnsureTable(db, "InvitationTokens",
    "CREATE TABLE \"InvitationTokens\" (" +
    "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
    "\"TeamId\" INTEGER NULL, " +
    "\"Email\" TEXT NOT NULL DEFAULT '', " +
    "\"Role\" TEXT NOT NULL DEFAULT '', " +
    "\"Token\" TEXT NOT NULL DEFAULT '', " +
    "\"ExpiresAt\" TEXT NOT NULL DEFAULT (datetime('now')), " +
    "\"UsedAt\" TEXT NULL);");
```

Note: `"Role" INTEGER NOT NULL DEFAULT 1` stores `TeamRole` as its int value (0=TeamAdmin, 1=Contributor, 2=Guest).

- [ ] **Step 7: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 8: Commit**

```
git add HttpForge/Data/Entities/Team.cs HttpForge/Data/Entities/TeamMember.cs HttpForge/Data/Entities/InvitationToken.cs HttpForge/Data/Entities/Collection.cs HttpForge/Data/SchemaUpgrader.cs
git commit -m "feat: add Team/TeamMember/InvitationToken entities and schema"
```

---

## Task 4: Identity services + auth middleware + external OAuth endpoints in Program.cs

**Files:**
- Modify: `HttpForge/Program.cs`

- [ ] **Step 1: Add usings at top of Program.cs**

```csharp
using HttpForge.Components;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
```

- [ ] **Step 2: Add Identity services after AddRazorComponents**

```csharp
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
});

builder.Services.AddCascadingAuthenticationState();
```

- [ ] **Step 3: Add optional external OAuth providers (after ConfigureApplicationCookie)**

```csharp
var googleId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
var googleSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
    builder.Services.AddAuthentication()
        .AddGoogle(o => { o.ClientId = googleId; o.ClientSecret = googleSecret; });

var msId = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID");
var msSecret = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET");
if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
    builder.Services.AddAuthentication()
        .AddMicrosoftAccount(o => { o.ClientId = msId; o.ClientSecret = msSecret; });

var ghId = Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID");
var ghSecret = Environment.GetEnvironmentVariable("GITHUB_CLIENT_SECRET");
if (!string.IsNullOrEmpty(ghId) && !string.IsNullOrEmpty(ghSecret))
    builder.Services.AddAuthentication()
        .AddGitHub(o => { o.ClientId = ghId; o.ClientSecret = ghSecret; });
```

- [ ] **Step 4: Register new scoped services (alongside existing ones)**

```csharp
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<TeamService>();
```

- [ ] **Step 5: Replace synchronous startup block with async version including bootstrap**

```csharp
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    SchemaUpgrader.Apply(db);
    await SuperAdminBootstrap.EnsureAsync(scope.ServiceProvider);
}
```

- [ ] **Step 6: Add auth middleware BEFORE UseAntiforgery**

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery(); // was already here — just ensure order
```

- [ ] **Step 7: Add external OAuth minimal API endpoints AFTER UseAntiforgery and BEFORE MapStaticAssets**

```csharp
// Challenge endpoint — redirects browser to OAuth provider
app.MapGet("/auth/external-login", (
    string provider,
    string? returnUrl,
    SignInManager<AppUser> signInManager) =>
{
    var redirectUrl = $"/auth/external-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
    var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    return Results.Challenge(properties, [provider]);
}).AllowAnonymous();

// OAuth callback — creates/links account, signs in, redirects
app.MapGet("/auth/external-callback", async (
    string? returnUrl,
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    IDbContextFactory<AppDbContext> dbFactory) =>
{
    var info = await signInManager.GetExternalLoginInfoAsync();
    if (info is null) return Results.Redirect("/login?error=external-failed");

    var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    if (string.IsNullOrEmpty(email)) return Results.Redirect("/login?error=no-email");

    // Existing login
    var signInResult = await signInManager.ExternalLoginSignInAsync(
        info.LoginProvider, info.ProviderKey, isPersistent: false);
    if (signInResult.Succeeded)
        return Results.Redirect(returnUrl ?? "/");

    // New user — requires pending invitation
    using var db = await dbFactory.CreateDbContextAsync();
    var invitation = await db.InvitationTokens
        .Where(i => i.Email == email.ToLower()
                 && i.UsedAt == null
                 && i.ExpiresAt > DateTime.UtcNow)
        .OrderByDescending(i => i.ExpiresAt)
        .FirstOrDefaultAsync();

    if (invitation is null) return Results.Redirect("/login?error=no-invitation");

    var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
    var createResult = await userManager.CreateAsync(user);
    if (!createResult.Succeeded) return Results.Redirect("/login?error=create-failed");

    await userManager.AddLoginAsync(user, info);

    if (invitation.Role == "SuperAdmin")
        await userManager.AddToRoleAsync(user, "SuperAdmin");
    else if (invitation.TeamId.HasValue)
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = invitation.TeamId.Value,
            UserId = user.Id,
            Role = Enum.Parse<TeamRole>(invitation.Role)
        });

    invitation.UsedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Redirect(returnUrl ?? "/");
}).AllowAnonymous();
```

- [ ] **Step 8: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 9: Commit**

```
git add HttpForge/Program.cs
git commit -m "feat: add Identity services, auth middleware, and external OAuth endpoints"
```

---

## Task 5: InvitationService + tests

**Files:**
- Create: `HttpForge/Services/InvitationService.cs`
- Create: `HttpForge.Tests/Helpers/TestDbContextFactory.cs`
- Create: `HttpForge.Tests/Services/InvitationServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `HttpForge.Tests/Helpers/TestDbContextFactory.cs`:

```csharp
using HttpForge.Data;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Tests.Helpers;

public class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(new AppDbContext(options));
}
```

Create `HttpForge.Tests/Services/InvitationServiceTests.cs`:

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Tests.Services;

public class InvitationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestDbContextFactory _factory;
    private readonly InvitationService _sut;

    public InvitationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        _factory = new TestDbContextFactory(_options);
        _sut = new InvitationService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task CreateAsync_StoresInvitationWithCorrectFields()
    {
        var inv = await _sut.CreateAsync(teamId: 1, email: "user@example.com", role: "Contributor");

        Assert.NotEqual(0, inv.Id);
        Assert.Equal("user@example.com", inv.Email);
        Assert.Equal("Contributor", inv.Role);
        Assert.Equal(1, inv.TeamId);
        Assert.NotEmpty(inv.Token);
        Assert.Null(inv.UsedAt);
        Assert.True(inv.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsInvitation_ForValidToken()
    {
        var inv = await _sut.CreateAsync(teamId: null, email: "admin@example.com", role: "SuperAdmin");

        var result = await _sut.ValidateAsync(inv.Token);

        Assert.NotNull(result);
        Assert.Equal(inv.Id, result.Id);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_ForUnknownToken()
    {
        var result = await _sut.ValidateAsync("nonexistenttoken12345");
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_ForUsedToken()
    {
        var inv = await _sut.CreateAsync(teamId: 1, email: "x@x.com", role: "Guest");
        await _sut.MarkUsedAsync(inv.Id);

        var result = await _sut.ValidateAsync(inv.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_ForExpiredToken()
    {
        var inv = await _sut.CreateAsync(teamId: 1, email: "x@x.com", role: "Contributor");
        using var db = await _factory.CreateDbContextAsync();
        var stored = await db.InvitationTokens.FirstAsync(i => i.Id == inv.Id);
        stored.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var result = await _sut.ValidateAsync(inv.Token);

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (InvitationService does not exist yet)**

```powershell
dotnet test HttpForge.Tests --filter "FullyQualifiedName~InvitationServiceTests"
```
Expected: Build error — `InvitationService` not found.

- [ ] **Step 3: Create InvitationService.cs**

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HttpForge.Services;

public class InvitationService(IDbContextFactory<AppDbContext> dbFactory)
{
    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }

    public async Task<InvitationToken> CreateAsync(int? teamId, string email, string role)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = new InvitationToken
        {
            TeamId = teamId,
            Email = email.ToLower().Trim(),
            Role = role,
            Token = GenerateToken(),
            ExpiresAt = DateTime.UtcNow.AddHours(72)
        };
        db.InvitationTokens.Add(invitation);
        await db.SaveChangesAsync();
        return invitation;
    }

    public async Task<InvitationToken?> ValidateAsync(string token)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = await db.InvitationTokens
            .FirstOrDefaultAsync(i => i.Token == token);
        if (invitation is null) return null;
        if (invitation.UsedAt is not null) return null;
        if (invitation.ExpiresAt < DateTime.UtcNow) return null;
        return invitation;
    }

    public async Task MarkUsedAsync(int invitationId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = await db.InvitationTokens.FirstAsync(i => i.Id == invitationId);
        invitation.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test HttpForge.Tests --filter "FullyQualifiedName~InvitationServiceTests"
```
Expected: `Passed: 5, Failed: 0`.

- [ ] **Step 5: Commit**

```
git add HttpForge/Services/InvitationService.cs HttpForge.Tests/Services/InvitationServiceTests.cs HttpForge.Tests/Helpers/TestDbContextFactory.cs
git commit -m "feat: add InvitationService with tests"
```

---

## Task 6: SuperAdminBootstrap

**Files:**
- Create: `HttpForge/Services/SuperAdminBootstrap.cs`

- [ ] **Step 1: Create SuperAdminBootstrap.cs**

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HttpForge.Services;

public static class SuperAdminBootstrap
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        var email = Environment.GetEnvironmentVariable("HTTPFORGE_SUPERADMIN_EMAIL");
        if (string.IsNullOrEmpty(email)) return;

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("SuperAdmin"))
            await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var existing = await userManager.GetUsersInRoleAsync("SuperAdmin");
        if (existing.Count > 0) return;

        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync();

        var normalizedEmail = email.ToLower().Trim();
        var alreadyInvited = await db.InvitationTokens
            .AnyAsync(i => i.Email == normalizedEmail && i.UsedAt == null);
        if (alreadyInvited) return;

        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        db.InvitationTokens.Add(new InvitationToken
        {
            Email = normalizedEmail,
            Role = "SuperAdmin",
            Token = Convert.ToHexString(bytes).ToLower(),
            ExpiresAt = DateTime.UtcNow.AddDays(365)
        });
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Commit**

```
git add HttpForge/Services/SuperAdminBootstrap.cs
git commit -m "feat: add SuperAdmin bootstrap — auto-creates invitation on first run"
```

---

## Task 7: PermissionService + tests

**Files:**
- Create: `HttpForge/Services/PermissionService.cs`
- Create: `HttpForge.Tests/Services/PermissionServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `HttpForge.Tests/Services/PermissionServiceTests.cs`:

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Tests.Services;

public class PermissionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TestDbContextFactory _factory;
    private readonly PermissionService _sut;

    public PermissionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        _factory = new TestDbContextFactory(_options);
        _sut = new PermissionService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<(string userId, int teamId, int collectionId)> SeedAsync(TeamRole role)
    {
        using var db = await _factory.CreateDbContextAsync();
        var userId = Guid.NewGuid().ToString();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@test.com", Email = "u@test.com" });
        var team = new Team { Name = "T" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = userId, Role = role });
        var col = new Collection { Name = "C", TeamId = team.Id };
        db.Collections.Add(col);
        await db.SaveChangesAsync();
        return (userId, team.Id, col.Id);
    }

    [Fact]
    public async Task GetRoleForCollectionAsync_ReturnsContributor_ForContributorMember()
    {
        var (userId, _, colId) = await SeedAsync(TeamRole.Contributor);
        var result = await _sut.GetRoleForCollectionAsync(userId, colId);
        Assert.Equal(TeamRole.Contributor, result);
    }

    [Fact]
    public async Task GetRoleForCollectionAsync_ReturnsGuest_ForGuestMember()
    {
        var (userId, _, colId) = await SeedAsync(TeamRole.Guest);
        var result = await _sut.GetRoleForCollectionAsync(userId, colId);
        Assert.Equal(TeamRole.Guest, result);
    }

    [Fact]
    public async Task GetRoleForCollectionAsync_ReturnsNull_ForNonMember()
    {
        var (_, _, colId) = await SeedAsync(TeamRole.Contributor);
        var result = await _sut.GetRoleForCollectionAsync("unknown-user-id", colId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRoleForCollectionAsync_ReturnsNull_ForOrphanedCollection()
    {
        using var db = await _factory.CreateDbContextAsync();
        var col = new Collection { Name = "Orphan", TeamId = null };
        db.Collections.Add(col);
        await db.SaveChangesAsync();

        var result = await _sut.GetRoleForCollectionAsync("any-user", col.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task IsReadOnlyAsync_ReturnsTrue_ForGuest()
    {
        var (userId, _, colId) = await SeedAsync(TeamRole.Guest);
        Assert.True(await _sut.IsReadOnlyAsync(userId, colId));
    }

    [Fact]
    public async Task IsReadOnlyAsync_ReturnsFalse_ForContributor()
    {
        var (userId, _, colId) = await SeedAsync(TeamRole.Contributor);
        Assert.False(await _sut.IsReadOnlyAsync(userId, colId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test HttpForge.Tests --filter "FullyQualifiedName~PermissionServiceTests"
```
Expected: Build error — `PermissionService` not found.

- [ ] **Step 3: Create PermissionService.cs**

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class PermissionService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<TeamRole?> GetRoleForCollectionAsync(string userId, int collectionId)
    {
        using var db = await dbFactory.CreateDbContextAsync();

        // SuperAdmin role check via EF (avoids UserManager dependency)
        var isSuperAdmin = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == userId && x.Name == "SuperAdmin");
        if (isSuperAdmin) return TeamRole.Contributor;

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection?.TeamId is null) return null;

        var member = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == collection.TeamId && m.UserId == userId);
        return member?.Role;
    }

    public async Task<bool> IsReadOnlyAsync(string userId, int collectionId)
    {
        var role = await GetRoleForCollectionAsync(userId, collectionId);
        return role is null or TeamRole.Guest;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test HttpForge.Tests --filter "FullyQualifiedName~PermissionServiceTests"
```
Expected: `Passed: 6, Failed: 0`.

- [ ] **Step 5: Commit**

```
git add HttpForge/Services/PermissionService.cs HttpForge.Tests/Services/PermissionServiceTests.cs
git commit -m "feat: add PermissionService with tests"
```

---

## Task 8: TeamService

**Files:**
- Create: `HttpForge/Services/TeamService.cs`

- [ ] **Step 1: Create TeamService.cs**

```csharp
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class TeamService(
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<AppUser> userManager,
    InvitationService invitationService)
{
    public async Task<Team> CreateTeamAsync(string name)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var team = new Team { Name = name.Trim() };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return team;
    }

    public async Task DeleteTeamAsync(int teamId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var team = await db.Teams.FirstAsync(t => t.Id == teamId);
        db.Teams.Remove(team);
        await db.SaveChangesAsync();
    }

    public async Task<List<Team>> GetAllTeamsAsync()
    {
        using var db = await dbFactory.CreateDbContextAsync();
        return await db.Teams.Include(t => t.Members).OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Team?> GetTeamAsync(int teamId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        return await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId);
    }

    public async Task<InvitationToken> InviteMemberAsync(int teamId, string email, TeamRole role)
        => await invitationService.CreateAsync(teamId, email, role.ToString());

    public async Task RemoveMemberAsync(int teamId, string userId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var member = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (member is not null)
        {
            db.TeamMembers.Remove(member);
            await db.SaveChangesAsync();
        }
    }

    public async Task AssignCollectionAsync(int teamId, int collectionId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var col = await db.Collections.FirstAsync(c => c.Id == collectionId);
        col.TeamId = teamId;
        await db.SaveChangesAsync();
    }

    public async Task<List<Collection>> GetOrphanedCollectionsAsync()
    {
        using var db = await dbFactory.CreateDbContextAsync();
        return await db.Collections.Where(c => c.TeamId == null).OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<List<Collection>> GetTeamCollectionsAsync(int teamId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        return await db.Collections.Where(c => c.TeamId == teamId).OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<List<InvitationToken>> GetPendingInvitationsAsync(int teamId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        return await db.InvitationTokens
            .Where(i => i.TeamId == teamId && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.ExpiresAt)
            .ToListAsync();
    }

    // Returns the teams a user belongs to and their role in each
    public async Task<List<(Team Team, TeamRole Role)>> GetUserTeamsAsync(string userId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var members = await db.TeamMembers
            .Include(m => m.Team)
            .Where(m => m.UserId == userId)
            .ToListAsync();
        return members.Select(m => (m.Team, m.Role)).ToList();
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```
git add HttpForge/Services/TeamService.cs HttpForge/Data/Entities/TeamMember.cs
git commit -m "feat: add TeamService for team/member/collection management"
```

---

## Task 9: AppState changes + _Imports auth namespaces

**Files:**
- Modify: `HttpForge/Services/AppState.cs`
- Modify: `HttpForge/Components/_Imports.razor`

- [ ] **Step 1: Add auth namespaces to _Imports.razor**

Append to `HttpForge/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using System.Security.Claims
```

- [ ] **Step 2: Add SelectedCollectionId and IsReadOnly to AppState.cs**

```csharp
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public enum VariableSource { Global, Collection, Request }

public record ResolvedVariableEntry(string Key, string Value, bool IsSecret, VariableSource Source);

public class AppState
{
    public int? SelectedEnvironmentId { get; set; }
    public int? SelectedRequestId { get; set; }
    public int? SelectedCollectionId { get; set; }
    public bool IsReadOnly { get; set; }

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

- [ ] **Step 3: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```
git add HttpForge/Services/AppState.cs HttpForge/Components/_Imports.razor
git commit -m "feat: add SelectedCollectionId and IsReadOnly to AppState"
```

---

## Task 10: Routes.razor protection + RedirectToLogin component

**Files:**
- Create: `HttpForge/Components/Layout/RedirectToLogin.razor`
- Modify: `HttpForge/Components/Routes.razor`

- [ ] **Step 1: Create RedirectToLogin.razor**

```razor
@inject NavigationManager Navigation

@code {
    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(Navigation.Uri);
        Navigation.NavigateTo($"/login?returnUrl={returnUrl}", forceLoad: true);
    }
}
```

- [ ] **Step 2: Update Routes.razor to use AuthorizeRouteView**

```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 3: Mark login and invite pages as AllowAnonymous** (will be done in Tasks 11-12; for now just verify Routes builds)

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```
git add HttpForge/Components/Layout/RedirectToLogin.razor HttpForge/Components/Routes.razor
git commit -m "feat: protect all routes — unauthenticated users redirected to /login"
```

---

## Task 11: Login page (/login)

**Files:**
- Create: `HttpForge/Components/Pages/LoginPage.razor`

- [ ] **Step 1: Create LoginPage.razor**

```razor
@page "/login"
@attribute [AllowAnonymous]
@rendermode InteractiveServer
@inject SignInManager<AppUser> SignInManager
@inject UserManager<AppUser> UserManager
@inject NavigationManager Navigation
@inject IDbContextFactory<AppDbContext> DbFactory

<PageTitle>Login — HttpForge</PageTitle>

<div class="auth-page">
    <div class="auth-card">
        <h2>HttpForge</h2>

        @if (_errorMessage is not null)
        {
            <div class="auth-error">@_errorMessage</div>
        }

        @* SSO buttons — only shown when providers are configured *@
        @if (_providers.Count > 0)
        {
            <div class="auth-sso">
                @foreach (var p in _providers)
                {
                    <a class="btn-sso" href="/auth/external-login?provider=@p&returnUrl=@_returnUrl">
                        Login with @p
                    </a>
                }
            </div>
            <div class="auth-divider">or</div>
        }

        @* Password login *@
        <div class="auth-form">
            <input type="email" placeholder="Email" @bind="_email" @bind:event="oninput" />
            <input type="password" placeholder="Password" @bind="_password" @bind:event="oninput"
                   @onkeydown="OnKeyDown" />
            <button @onclick="LoginAsync" disabled="@_busy">
                @(_busy ? "Signing in…" : "Sign in")
            </button>
        </div>
    </div>
</div>

@code {
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }
    [SupplyParameterFromQuery] public string? Error { get; set; }

    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private bool _busy;
    private string _returnUrl = "/";
    private List<string> _providers = [];

    protected override async Task OnInitializedAsync()
    {
        _returnUrl = ReturnUrl ?? "/";
        _errorMessage = Error switch
        {
            "external-failed" => "External login failed. Please try again.",
            "no-email" => "The external provider did not return an email address.",
            "no-invitation" => "No pending invitation found for this email address.",
            "create-failed" => "Account creation failed. Please contact an administrator.",
            _ => null
        };

        var schemes = await SignInManager.GetExternalAuthenticationSchemesAsync();
        _providers = schemes.Select(s => s.Name).ToList();
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password)) return;
        _busy = true;
        _errorMessage = null;

        var result = await SignInManager.PasswordSignInAsync(_email.Trim(), _password, false, false);
        if (result.Succeeded)
        {
            Navigation.NavigateTo(_returnUrl, forceLoad: true);
            return;
        }

        _errorMessage = "Invalid email or password.";
        _busy = false;
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await LoginAsync();
    }
}
```

Add minimal CSS to `app.css` (or a new `auth.css` included in App.razor):
```css
.auth-page {
    display: flex; align-items: center; justify-content: center;
    min-height: 100vh; background: var(--bg);
}
.auth-card {
    background: var(--surface); border: 1px solid var(--border);
    border-radius: 8px; padding: 2rem; width: 360px;
    display: flex; flex-direction: column; gap: 1rem;
}
.auth-card h2 { text-align: center; margin: 0; }
.auth-error { color: var(--danger); font-size: 0.85rem; }
.auth-sso { display: flex; flex-direction: column; gap: 0.5rem; }
.btn-sso {
    display: block; text-align: center; padding: 0.5rem;
    border: 1px solid var(--border); border-radius: 4px; text-decoration: none;
    color: var(--text);
}
.auth-divider { text-align: center; color: var(--text-muted); font-size: 0.8rem; }
.auth-form { display: flex; flex-direction: column; gap: 0.5rem; }
.auth-form input { padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; background: var(--input-bg); color: var(--text); }
.auth-form button { padding: 0.5rem; background: var(--accent); color: white; border: none; border-radius: 4px; cursor: pointer; }
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Commit**

```
git add HttpForge/Components/Pages/LoginPage.razor
git commit -m "feat: add login page with password and SSO options"
```

---

## Task 12: Invite page (/invite/{token})

**Files:**
- Create: `HttpForge/Components/Pages/InvitePage.razor`

- [ ] **Step 1: Create InvitePage.razor**

```razor
@page "/invite/{Token}"
@attribute [AllowAnonymous]
@rendermode InteractiveServer
@inject InvitationService InvitationService
@inject SignInManager<AppUser> SignInManager
@inject UserManager<AppUser> UserManager
@inject IDbContextFactory<AppDbContext> DbFactory
@inject NavigationManager Navigation

<PageTitle>Join — HttpForge</PageTitle>

<div class="auth-page">
    <div class="auth-card">
        @if (_loading)
        {
            <p>Validating invitation…</p>
        }
        else if (_invitation is null)
        {
            <h2>Invalid invitation</h2>
            <p>This invitation link is expired, already used, or does not exist.</p>
            <a href="/login">Back to login</a>
        }
        else if (_done)
        {
            <h2>Account created</h2>
            <p>You are now signed in. <a href="/">Go to HttpForge</a></p>
        }
        else
        {
            <h2>You've been invited</h2>
            <p>Join as <strong>@_invitation.Role</strong> — <em>@_invitation.Email</em></p>

            @if (_errorMessage is not null)
            {
                <div class="auth-error">@_errorMessage</div>
            }

            @if (_providers.Count > 0)
            {
                <div class="auth-sso">
                    @foreach (var p in _providers)
                    {
                        <a class="btn-sso"
                           href="/auth/external-login?provider=@p&returnUrl=/">
                            Continue with @p
                        </a>
                    }
                </div>
                <div class="auth-divider">or set a password</div>
            }

            <div class="auth-form">
                <input type="password" placeholder="Password (min 8 characters)"
                       @bind="_password" @bind:event="oninput" />
                <input type="password" placeholder="Confirm password"
                       @bind="_confirm" @bind:event="oninput"
                       @onkeydown="OnKeyDown" />
                <button @onclick="RegisterAsync" disabled="@_busy">
                    @(_busy ? "Creating account…" : "Create account")
                </button>
            </div>
        }
    </div>
</div>

@code {
    [Parameter] public string Token { get; set; } = string.Empty;

    private InvitationToken? _invitation;
    private bool _loading = true;
    private bool _done;
    private bool _busy;
    private string _password = string.Empty;
    private string _confirm = string.Empty;
    private string? _errorMessage;
    private List<string> _providers = [];

    protected override async Task OnInitializedAsync()
    {
        _invitation = await InvitationService.ValidateAsync(Token);
        _loading = false;

        if (_invitation is not null)
        {
            var schemes = await SignInManager.GetExternalAuthenticationSchemesAsync();
            _providers = schemes.Select(s => s.Name).ToList();
        }
    }

    private async Task RegisterAsync()
    {
        if (_invitation is null) return;
        if (_password.Length < 8) { _errorMessage = "Password must be at least 8 characters."; return; }
        if (_password != _confirm) { _errorMessage = "Passwords do not match."; return; }

        _busy = true;
        _errorMessage = null;

        var user = new AppUser
        {
            UserName = _invitation.Email,
            Email = _invitation.Email,
            EmailConfirmed = true
        };

        var result = await UserManager.CreateAsync(user, _password);
        if (!result.Succeeded)
        {
            _errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            _busy = false;
            return;
        }

        using var db = await DbFactory.CreateDbContextAsync();

        if (_invitation.Role == "SuperAdmin")
            await UserManager.AddToRoleAsync(user, "SuperAdmin");
        else if (_invitation.TeamId.HasValue)
            db.TeamMembers.Add(new TeamMember
            {
                TeamId = _invitation.TeamId.Value,
                UserId = user.Id,
                Role = Enum.Parse<TeamRole>(_invitation.Role)
            });

        await InvitationService.MarkUsedAsync(_invitation.Id);
        await db.SaveChangesAsync();

        await SignInManager.SignInAsync(user, isPersistent: false);
        _done = true;
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await RegisterAsync();
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Commit**

```
git add HttpForge/Components/Pages/InvitePage.razor
git commit -m "feat: add invite page for password and SSO account creation"
```

---

## Task 13: Home.razor — auth + permission check + Guest banner + mutation gating

**Files:**
- Modify: `HttpForge/Components/Pages/Home.razor`

- [ ] **Step 1: Add injections to Home.razor**

Add these inject directives after the existing ones at the top of `Home.razor`:

```razor
@inject PermissionService PermissionService
@inject AuthenticationStateProvider AuthProvider
```

- [ ] **Step 2: Add Guest banner at the top of the request editor**

Immediately after `@if (_request is not null)` and before `<div class="editor">`, insert:

```razor
@if (State.IsReadOnly)
{
    <div class="guest-banner">
        Vous êtes en mode lecture — vos modifications ne seront pas enregistrées.
    </div>
}
```

Add CSS for the banner in `app.css`:
```css
.guest-banner {
    background: var(--warning-bg, #fff3cd);
    color: var(--warning-text, #856404);
    border: 1px solid var(--warning-border, #ffc107);
    border-radius: 4px;
    padding: 0.5rem 1rem;
    font-size: 0.85rem;
    margin-bottom: 0.5rem;
}
```

- [ ] **Step 3: Gate all save methods with IsReadOnly check**

In Home.razor's `@code` block, find the method that loads the request (typically `LoadRequestAsync` or triggered via `OnStateChanged`). After loading `_request`, add the permission check:

```csharp
private async Task LoadRequestAsync(int requestId)
{
    // ... existing load logic ...

    // Update SelectedCollectionId and IsReadOnly
    if (_request is not null)
    {
        State.SelectedCollectionId = _request.CollectionId;
        var authState = await AuthProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        State.IsReadOnly = userId is not null
            ? await PermissionService.IsReadOnlyAsync(userId, _request.CollectionId)
            : true;
    }
    else
    {
        State.SelectedCollectionId = null;
        State.IsReadOnly = false;
    }
}
```

Find the exact method name by searching for where `State.SelectedRequestId` is used to load request data. It is likely in `OnStateChanged` or `OnInitializedAsync`.

- [ ] **Step 4: Gate save methods**

At the top of every method that writes to the database (e.g., `SaveRequestDebounced`, `OnNameInput`, `OnMethodChanged`, `OnUrlChanged`, `SaveHeaderAsync`, `SaveQueryParamAsync`, `SaveFormFieldAsync`, `OnBodyChanged`), add:

```csharp
if (State.IsReadOnly) return;
// ... existing save logic
```

- [ ] **Step 5: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 6: Commit**

```
git add HttpForge/Components/Pages/Home.razor
git commit -m "feat: Home.razor — Guest banner and mutation gating via IsReadOnly"
```

---

## Task 14: NavMenu.razor — filter collections + permission-aware context menus

**Files:**
- Modify: `HttpForge/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Add injections and fields**

Add to the `@code` block:

```csharp
@inject PermissionService PermissionService
@inject AuthenticationStateProvider AuthProvider
```

Add fields:
```csharp
private string? _currentUserId;
private HashSet<int> _writableCollectionIds = []; // collection IDs where user can write
private bool _canCreateCollections;
```

- [ ] **Step 2: Resolve user identity and permissions in OnInitializedAsync**

In `OnInitializedAsync`, after existing initialization:

```csharp
var authState = await AuthProvider.GetAuthenticationStateAsync();
_currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
await RefreshPermissionsAsync();
```

Add a private method:
```csharp
private async Task RefreshPermissionsAsync()
{
    if (_currentUserId is null) return;

    var authState = await AuthProvider.GetAuthenticationStateAsync();
    var isSuperAdmin = authState.User.IsInRole("SuperAdmin");

    _writableCollectionIds.Clear();
    foreach (var c in _collections)
    {
        var role = isSuperAdmin
            ? TeamRole.Contributor
            : (await PermissionService.GetRoleForCollectionAsync(_currentUserId, c.Id));
        if (role is TeamRole.Contributor or TeamRole.TeamAdmin)
            _writableCollectionIds.Add(c.Id);
    }

    _canCreateCollections = isSuperAdmin || _writableCollectionIds.Count > 0
        || _collections.Count == 0; // allow creating first collection
}
```

Call `await RefreshPermissionsAsync()` at the end of `ReloadAsync`.

- [ ] **Step 3: Filter collections to only show team's collections**

Replace the `ReloadAsync` collections query:

```csharp
// Instead of loading all collections, load only those visible to the current user
if (_currentUserId is null)
{
    _collections = [];
}
else
{
    var authState = await AuthProvider.GetAuthenticationStateAsync();
    var isSuperAdmin = authState.User.IsInRole("SuperAdmin");

    if (isSuperAdmin)
    {
        _collections = await db.Collections
            .Include(c => c.Requests)
            .Include(c => c.VariableSets).ThenInclude(s => s.Entries)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }
    else
    {
        var teamIds = await db.TeamMembers
            .Where(m => m.UserId == _currentUserId)
            .Select(m => m.TeamId)
            .ToListAsync();

        _collections = await db.Collections
            .Include(c => c.Requests)
            .Include(c => c.VariableSets).ThenInclude(s => s.Entries)
            .Where(c => c.TeamId.HasValue && teamIds.Contains(c.TeamId.Value))
            .OrderBy(c => c.Name)
            .ToListAsync();
    }
}
```

- [ ] **Step 4: Hide the "+" add collection button for users who can't create**

In the template, find the `+ button` for new collection and wrap with condition:

```razor
@if (_canCreateCollections)
{
    <button class="icon-btn" title="New collection" @onclick="StartAddCollection">+</button>
}
```

- [ ] **Step 5: Assign TeamId when creating a collection**

In `AddCollection()`, after creating the collection, assign the user's team:

```csharp
private async Task AddCollection()
{
    if (string.IsNullOrWhiteSpace(_newCollectionName)) return;
    using var db = await DbFactory.CreateDbContextAsync();

    int? teamId = null;
    if (_currentUserId is not null)
    {
        // Assign to the user's first team where they have write access
        var member = await db.TeamMembers
            .Where(m => m.UserId == _currentUserId && m.Role != TeamRole.Guest)
            .FirstOrDefaultAsync();
        teamId = member?.TeamId;
    }

    var c = new Collection { Name = _newCollectionName.Trim(), TeamId = teamId };
    db.Collections.Add(c);
    await db.SaveChangesAsync();
    _expanded.Add(c.Id);
    _newCollectionName = string.Empty;
    _showAddCollection = false;
    await ReloadAsync();
}
```

- [ ] **Step 6: Hide mutation context menu items for collections the user can't edit**

In the collection context menu template, wrap the destructive/mutating items:

```razor
@if (_writableCollectionIds.Contains(c.Id))
{
    <button class="context-menu-item" ...>📁 Nouveau dossier</button>
    <button class="context-menu-item" ...>＋ Nouvelle requête</button>
    <button class="context-menu-item danger" ...>🗑 Supprimer la collection</button>
}
```

Keep "Variables de collection" visible to all (Guests can view variables).

- [ ] **Step 7: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 8: Commit**

```
git add HttpForge/Components/Layout/NavMenu.razor
git commit -m "feat: NavMenu — filter collections by team, hide mutations for Guest"
```

---

## Task 15: CollectionNode.razor + RequestRow.razor — hide edit/delete for Guests

**Files:**
- Modify: `HttpForge/Components/Layout/CollectionNode.razor`
- Modify: `HttpForge/Components/Layout/RequestRow.razor`

- [ ] **Step 1: Add CanWrite parameter to CollectionNode.razor**

Add a `[Parameter]` at the top of `@code`:
```csharp
[Parameter] public bool CanWrite { get; set; } = true;
```

In the template, wrap folder/request mutation buttons with `@if (CanWrite)`:
- The "Nouveau sous-dossier" and "Nouvelle requête" context menu items
- The "Rename" and "Delete" folder context menu items
- The `<RequestRow>` component should receive `CanWrite` as a parameter

Pass `CanWrite` to `<CollectionNode>` calls in NavMenu:
```razor
<CollectionNode CollectionId="c.Id"
                CanWrite="_writableCollectionIds.Contains(c.Id)"
                ... />
```

- [ ] **Step 2: Add CanWrite parameter to RequestRow.razor**

Add:
```csharp
[Parameter] public bool CanWrite { get; set; } = true;
```

Wrap the rename + delete context menu items with `@if (CanWrite)`. If `CanWrite` is false and there are no visible items, hide the gear button entirely:

```razor
@if (CanWrite)
{
    <div class="gear-wrap @(_isHovered ? "visible" : "")">
        <button class="icon-btn gear-btn" ...>⚙</button>
        @if (_menuOpen)
        {
            <div class="context-menu">
                <button ... @onclick="StartRename"><span>✏</span> Renommer</button>
                <button ... @onclick="DeleteAsync"><span>🗑</span> Supprimer</button>
            </div>
        }
    </div>
}
```

- [ ] **Step 3: Pass CanWrite to RequestRow inside CollectionNode**

In `CollectionNode.razor`, where `<RequestRow>` is rendered:
```razor
<RequestRow Request="r" CanWrite="CanWrite" OnChanged="OnChanged" />
```

- [ ] **Step 4: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```
git add HttpForge/Components/Layout/CollectionNode.razor HttpForge/Components/Layout/RequestRow.razor
git commit -m "feat: hide edit/delete for Guest role via CanWrite parameter"
```

---

## Task 16: MainLayout.razor — user header with logout

**Files:**
- Modify: `HttpForge/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Update MainLayout.razor**

```razor
@inherits LayoutComponentBase
@inject NavigationManager Navigation

<ThemeToggle />
<div class="forge-shell">
    <aside class="forge-sidebar">
        <NavMenu />
    </aside>
    <div class="forge-resize-handle"></div>
    <main class="forge-main">
        <AuthorizeView>
            <Authorized>
                <div class="user-header">
                    <span class="user-email">@context.User.Identity?.Name</span>
                    <a class="btn-logout" href="/auth/logout">Logout</a>
                </div>
            </Authorized>
        </AuthorizeView>
        @Body
    </main>
</div>

<div id="blazor-error-ui" data-nosnippet>
    An unhandled error has occurred.
    <a href="." class="reload">Reload</a>
    <span class="dismiss">🗙</span>
</div>
```

- [ ] **Step 2: Add logout endpoint in Program.cs**

After the external OAuth endpoints, before MapStaticAssets:

```csharp
app.MapPost("/auth/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

// GET version for <a href> link convenience
app.MapGet("/auth/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});
```

- [ ] **Step 3: Add CSS for user header in app.css**

```css
.user-header {
    display: flex; align-items: center; justify-content: flex-end;
    gap: 0.75rem; padding: 0.25rem 1rem; font-size: 0.8rem;
    border-bottom: 1px solid var(--border);
}
.user-email { color: var(--text-muted); }
.btn-logout { color: var(--accent); text-decoration: none; }
.btn-logout:hover { text-decoration: underline; }
```

- [ ] **Step 4: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```
git add HttpForge/Components/Layout/MainLayout.razor HttpForge/Program.cs
git commit -m "feat: add user header with email and logout to MainLayout"
```

---

## Task 17: Admin page (/admin) — SuperAdmin team management

**Files:**
- Create: `HttpForge/Components/Pages/AdminPage.razor`

- [ ] **Step 1: Create AdminPage.razor**

```razor
@page "/admin"
@attribute [Authorize(Roles = "SuperAdmin")]
@rendermode InteractiveServer
@inject TeamService TeamService
@inject NavigationManager Navigation

<PageTitle>Admin — HttpForge</PageTitle>

<div class="admin-page">
    <h2>Teams</h2>

    @if (_createError is not null)
    {
        <div class="auth-error">@_createError</div>
    }

    <div class="admin-create-team">
        <input placeholder="New team name" @bind="_newTeamName" @bind:event="oninput"
               @onkeydown="OnTeamNameKey" />
        <button @onclick="CreateTeamAsync" disabled="@string.IsNullOrWhiteSpace(_newTeamName)">
            Create team
        </button>
    </div>

    @foreach (var team in _teams)
    {
        <div class="admin-team-row">
            <span class="team-name">@team.Name</span>
            <span class="team-members">@team.Members.Count member(s)</span>
            <button @onclick="() => Navigation.NavigateTo($\"/teams/{team.Id}\")">Manage</button>
            <button class="btn-danger" @onclick="() => DeleteTeamAsync(team)">Delete</button>
        </div>
    }

    @if (_teams.Count == 0)
    {
        <p class="empty-hint">No teams yet.</p>
    }
</div>

@code {
    private List<Team> _teams = [];
    private string _newTeamName = string.Empty;
    private string? _createError;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
        => _teams = await TeamService.GetAllTeamsAsync();

    private async Task CreateTeamAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTeamName)) return;
        await TeamService.CreateTeamAsync(_newTeamName.Trim());
        _newTeamName = string.Empty;
        await ReloadAsync();
    }

    private async Task OnTeamNameKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await CreateTeamAsync();
    }

    private async Task DeleteTeamAsync(Team team)
    {
        await TeamService.DeleteTeamAsync(team.Id);
        await ReloadAsync();
    }
}
```

Add minimal CSS:
```css
.admin-page { padding: 1.5rem; max-width: 800px; }
.admin-create-team { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
.admin-team-row {
    display: flex; align-items: center; gap: 1rem;
    padding: 0.5rem; border: 1px solid var(--border); border-radius: 4px; margin-bottom: 0.5rem;
}
.team-name { font-weight: 600; flex: 1; }
.team-members { color: var(--text-muted); font-size: 0.85rem; }
.btn-danger { color: var(--danger); background: none; border: 1px solid var(--danger); border-radius: 4px; padding: 0.2rem 0.5rem; cursor: pointer; }
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Commit**

```
git add HttpForge/Components/Pages/AdminPage.razor
git commit -m "feat: add admin page for SuperAdmin team management"
```

---

## Task 18: Team page (/teams/{teamId}) — TeamAdmin member + collection management

**Files:**
- Create: `HttpForge/Components/Pages/TeamPage.razor`

- [ ] **Step 1: Create TeamPage.razor**

```razor
@page "/teams/{TeamId:int}"
@attribute [Authorize]
@rendermode InteractiveServer
@inject TeamService TeamService
@inject AuthenticationStateProvider AuthProvider
@inject NavigationManager Navigation

<PageTitle>Team — HttpForge</PageTitle>

@if (_team is null)
{
    <div class="admin-page"><p>Team not found or access denied.</p></div>
}
else
{
    <div class="admin-page">
        <h2>@_team.Name</h2>

        @* Members section *@
        <h3>Members</h3>
        @foreach (var m in _team.Members)
        {
            <div class="admin-team-row">
                <span class="team-name">@m.UserId</span>
                <span class="team-members">@m.Role</span>
                @if (_isAdmin)
                {
                    <button class="btn-danger" @onclick="() => RemoveMemberAsync(m)">Remove</button>
                }
            </div>
        }

        @* Invite section *@
        @if (_isAdmin)
        {
            <h3>Invite member</h3>
            @if (_inviteMessage is not null)
            {
                <div class="invite-message">@_inviteMessage</div>
            }
            <div class="admin-create-team">
                <input type="email" placeholder="Email" @bind="_inviteEmail" @bind:event="oninput" />
                <select @bind="_inviteRole">
                    <option value="TeamAdmin">Team Admin</option>
                    <option value="Contributor" selected>Contributor</option>
                    <option value="Guest">Guest</option>
                </select>
                <button @onclick="InviteAsync"
                        disabled="@string.IsNullOrWhiteSpace(_inviteEmail)">
                    Send invitation
                </button>
            </div>

            @* Pending invitations *@
            @if (_pendingInvitations.Count > 0)
            {
                <h4>Pending invitations</h4>
                @foreach (var inv in _pendingInvitations)
                {
                    <div class="admin-team-row">
                        <span class="team-name">@inv.Email</span>
                        <span class="team-members">@inv.Role — expires @inv.ExpiresAt.ToLocalTime().ToString("g")</span>
                        <code class="invite-link">/invite/@inv.Token</code>
                    </div>
                }
            }

            @* Orphaned collections *@
            @if (_orphanedCollections.Count > 0)
            {
                <h3>Assign orphaned collections</h3>
                @foreach (var col in _orphanedCollections)
                {
                    <div class="admin-team-row">
                        <span class="team-name">@col.Name</span>
                        <button @onclick="() => AssignCollectionAsync(col.Id)">
                            Assign to @_team.Name
                        </button>
                    </div>
                }
            }
        }

        @* Collections assigned to this team *@
        <h3>Collections</h3>
        @foreach (var col in _teamCollections)
        {
            <div class="admin-team-row">
                <span class="team-name">@col.Name</span>
            </div>
        }
        @if (_teamCollections.Count == 0)
        {
            <p class="empty-hint">No collections assigned.</p>
        }
    </div>
}

@code {
    [Parameter] public int TeamId { get; set; }

    private Team? _team;
    private List<InvitationToken> _pendingInvitations = [];
    private List<Collection> _orphanedCollections = [];
    private List<Collection> _teamCollections = [];
    private bool _isAdmin;
    private string _inviteEmail = string.Empty;
    private string _inviteRole = "Contributor";
    private string? _inviteMessage;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isSuperAdmin = authState.User.IsInRole("SuperAdmin");

        _team = await TeamService.GetTeamAsync(TeamId);
        if (_team is null) return;

        _isAdmin = isSuperAdmin
            || _team.Members.Any(m => m.UserId == userId && m.Role == TeamRole.TeamAdmin);

        if (!_isAdmin && !_team.Members.Any(m => m.UserId == userId))
        {
            Navigation.NavigateTo("/", forceLoad: true);
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _team = await TeamService.GetTeamAsync(TeamId);
        _teamCollections = await TeamService.GetTeamCollectionsAsync(TeamId);

        if (_isAdmin)
        {
            _pendingInvitations = await TeamService.GetPendingInvitationsAsync(TeamId);
            _orphanedCollections = await TeamService.GetOrphanedCollectionsAsync();
        }
    }

    private async Task InviteAsync()
    {
        if (string.IsNullOrWhiteSpace(_inviteEmail)) return;
        var role = Enum.Parse<TeamRole>(_inviteRole);
        var inv = await TeamService.InviteMemberAsync(TeamId, _inviteEmail, role);
        _inviteMessage = $"Invitation link: /invite/{inv.Token}";
        _inviteEmail = string.Empty;
        await ReloadAsync();
    }

    private async Task RemoveMemberAsync(TeamMember member)
    {
        await TeamService.RemoveMemberAsync(TeamId, member.UserId);
        await ReloadAsync();
    }

    private async Task AssignCollectionAsync(int collectionId)
    {
        await TeamService.AssignCollectionAsync(TeamId, collectionId);
        await ReloadAsync();
    }
}
```

Add CSS:
```css
.invite-message { color: var(--accent); font-size: 0.85rem; word-break: break-all; }
.invite-link { font-size: 0.75rem; color: var(--text-muted); }
```

- [ ] **Step 2: Build**

```powershell
dotnet build HttpForge
```
Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Run full test suite**

```powershell
dotnet test HttpForge.Tests
```
Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add HttpForge/Components/Pages/TeamPage.razor
git commit -m "feat: add team page for member management and collection assignment"
```

---

## Self-Review Checklist

After all tasks are complete, verify:

- [ ] Login page accessible at `/login` without authentication
- [ ] Invite page accessible at `/invite/{token}` without authentication  
- [ ] All other pages redirect to `/login` when not authenticated
- [ ] SuperAdmin can access `/admin`
- [ ] TeamAdmin can access `/teams/{teamId}` for their team
- [ ] Contributor sees full edit UI, saves persist to DB
- [ ] Guest sees read-only banner, edits don't persist after reload
- [ ] NavMenu only shows collections belonging to the user's teams
- [ ] `HTTPFORGE_SUPERADMIN_EMAIL` env var triggers invitation auto-generation at startup
- [ ] External OAuth callback rejects login when no invitation exists for the email
