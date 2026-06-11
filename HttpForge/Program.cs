using HttpForge.Components;
using HttpForge.Data;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
