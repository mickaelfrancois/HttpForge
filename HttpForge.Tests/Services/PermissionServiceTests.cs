using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
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
