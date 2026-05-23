using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Integration;

public class UserVariableValuesTests : IAsyncLifetime
{
    private IDbContextFactory<AppDbContext> _factory = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private UserVariableValue MakeValue(string userId, string key, string value) => new()
    {
        UserId = userId,
        ScopeType = UserVariableScope.Request,
        ScopeId = 1,
        VariableKey = key,
        Value = value,
        IsSecret = false
    };

    [Fact]
    public async Task Upsert_InsertsOnFirstWrite()
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.UserVariableValues.Add(MakeValue("user-a", "JWT", "token-abc"));
        await db.SaveChangesAsync();

        var saved = await db.UserVariableValues.FirstAsync();
        Assert.Equal("token-abc", saved.Value);
    }

    [Fact]
    public async Task Upsert_UpdatesOnSecondWrite()
    {
        await using var db1 = await _factory.CreateDbContextAsync();
        db1.UserVariableValues.Add(MakeValue("user-a", "JWT", "old-token"));
        await db1.SaveChangesAsync();

        await using var db2 = await _factory.CreateDbContextAsync();
        var existing = await db2.UserVariableValues.FirstAsync(v => v.UserId == "user-a" && v.VariableKey == "JWT");
        existing.Value = "new-token";
        await db2.SaveChangesAsync();

        await using var db3 = await _factory.CreateDbContextAsync();
        var updated = await db3.UserVariableValues.FirstAsync(v => v.UserId == "user-a" && v.VariableKey == "JWT");
        Assert.Equal("new-token", updated.Value);
        Assert.Single(await db3.UserVariableValues.ToListAsync());
    }

    [Fact]
    public async Task UserValues_IsolatedPerUser()
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.UserVariableValues.AddRange(
            MakeValue("user-a", "JWT", "token-for-alice"),
            MakeValue("user-b", "JWT", "token-for-bob")
        );
        await db.SaveChangesAsync();

        await using var db2 = await _factory.CreateDbContextAsync();
        var aliceValues = await db2.UserVariableValues.Where(v => v.UserId == "user-a").ToListAsync();
        var bobValues = await db2.UserVariableValues.Where(v => v.UserId == "user-b").ToListAsync();

        Assert.Single(aliceValues);
        Assert.Equal("token-for-alice", aliceValues[0].Value);
        Assert.Single(bobValues);
        Assert.Equal("token-for-bob", bobValues[0].Value);
    }
}
