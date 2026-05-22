using HttpForge.Components;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dataDir = Environment.GetEnvironmentVariable("HTTPFORGE_DATA")
    ?? builder.Environment.ContentRootPath;
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "httpforge.db");
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

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

// External OAuth providers — only registered when env vars are present
var googleId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
var googleSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
    builder.Services.AddAuthentication().AddGoogle(o => { o.ClientId = googleId; o.ClientSecret = googleSecret; });

var msId = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID");
var msSecret = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET");
if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
    builder.Services.AddAuthentication().AddMicrosoftAccount(o => { o.ClientId = msId; o.ClientSecret = msSecret; });

var ghId = Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID");
var ghSecret = Environment.GetEnvironmentVariable("GITHUB_CLIENT_SECRET");
if (!string.IsNullOrEmpty(ghId) && !string.IsNullOrEmpty(ghSecret))
    builder.Services.AddAuthentication().AddGitHub(o => { o.ClientId = ghId; o.ClientSecret = ghSecret; });

builder.Services.AddHttpClient("forge")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = false
    });

builder.Services.AddScoped<RequestExecutor>();
builder.Services.AddScoped<VariableResolver>();
builder.Services.AddScoped<InsomniaImporter>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ScriptRunner>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<TeamService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    SchemaUpgrader.Apply(db);
    await SuperAdminBootstrap.EnsureAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

// Logout
app.MapGet("/auth/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
