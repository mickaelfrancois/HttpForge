using HttpForge.Data;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Tests.Helpers;

public class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(new AppDbContext(options));
}
