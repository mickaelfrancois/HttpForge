using System.Diagnostics;
using HttpForge.Components;
using HttpForge.Data;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;

// Hors Development, on epingle le ContentRoot sur le dossier du binaire : sinon, lance
// depuis un autre repertoire de travail (raccourci, double-clic...), MapStaticAssets ne
// trouve plus les assets et renvoie des corps vides -> echec d'integrite SRI cote Blazor.
// En Development on garde le defaut pour que dotnet run/watch resolvent les assets sources.
var isDevelopment = string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
    "Development", StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = isDevelopment ? null : AppContext.BaseDirectory,
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Priorite a HTTPFORGE_DATA (Docker, run.ps1). Sinon : en Development ou hors Windows on
// garde le ContentRoot (db a cote des sources / du binaire) ; sur un poste Windows en
// Production (ex. double-clic sur HttpForge.exe) on range la db dans %LOCALAPPDATA%\HttpForge
// pour ne pas la melanger aux binaires publies (un republish ecraserait le dossier).
var dataDir = Environment.GetEnvironmentVariable("HTTPFORGE_DATA");
if (string.IsNullOrEmpty(dataDir))
{
    dataDir = isDevelopment || !OperatingSystem.IsWindows()
        ? builder.Environment.ContentRootPath
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HttpForge");
}
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "httpforge.db");
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<VariableResolver>();
builder.Services.AddScoped(sp => new RequestExecutor(sp.GetRequiredService<VariableResolver>()));
builder.Services.AddScoped<InsomniaImporter>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ScriptRunner>();
builder.Services.AddSingleton<RequestChangeNotifier>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RequestSaveService>();
builder.Services.AddScoped<RequestAutoSaver>();
builder.Services.AddScoped<TabManagerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
    DbSeeder.Apply(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Double-clic sur HttpForge.exe : ouvre le navigateur une fois le serveur pret.
// Conditions : Windows, hors Development, et sortie console NON redirigee. La redirection
// (Console.IsOutputRedirected) est vraie quand run.ps1 lance l'app detachee en redirigeant
// les logs : dans ce cas c'est run.ps1 qui ouvre le navigateur, on evite un double onglet.
// Docker (Linux) est exclu par OperatingSystem.IsWindows().
if (OperatingSystem.IsWindows() && !app.Environment.IsDevelopment() && !Console.IsOutputRedirected)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Pas de navigateur disponible : on laisse tourner le serveur sans bloquer.
        }
    });
}

app.Run();
