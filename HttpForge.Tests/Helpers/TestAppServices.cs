using HttpForge.Data;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Helpers;

// Registers the full app service graph against an in-memory database so page components
// (Home, NavMenu) can be rendered in bUnit. Shared state (AppState, TabManagerService)
// is registered as a singleton so a test can pre-populate it and have the rendered
// component see the same instance.
internal static class TestAppServices
{
    public static void Register(IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<VariableResolver>();
        services.AddSingleton(sp => new RequestExecutor(sp.GetRequiredService<VariableResolver>()));
        services.AddSingleton<AppState>();
        services.AddSingleton<ScriptRunner>();
        services.AddSingleton<RequestChangeNotifier>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RequestSaveService>();
        // Registered as a pre-built instance so the (synchronously-disposed) bUnit container
        // does not try to Dispose an IAsyncDisposable-only service — the container never
        // disposes instances it did not create. No auto-save is scheduled in these tests.
        services.AddSingleton(new RequestAutoSaver(TimeProvider.System));
        services.AddSingleton<InsomniaImporter>();
        services.AddSingleton<OpenApiImporter>();
        services.AddSingleton<CurlService>();
        services.AddHttpClient();
        services.AddSingleton<TabManagerService>();
    }
}
