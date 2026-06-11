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

var dataDir = Environment.GetEnvironmentVariable("HTTPFORGE_DATA")
    ?? builder.Environment.ContentRootPath;
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "httpforge.db");
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<VariableResolver>();
builder.Services.AddScoped(sp => new RequestExecutor(sp.GetRequiredService<VariableResolver>()));
builder.Services.AddScoped<InsomniaImporter>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ScriptRunner>();
builder.Services.AddSingleton<RequestChangeNotifier>();
builder.Services.AddScoped<RequestSaveService>();
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

app.Run();
