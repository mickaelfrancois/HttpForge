namespace HttpForge.Services;

public static class SuperAdminBootstrap
{
    public static Task EnsureAsync(IServiceProvider services) => Task.CompletedTask;
}
