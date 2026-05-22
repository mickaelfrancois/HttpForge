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

    [Fact]
    public async Task CreateAsync_NormalizesEmail()
    {
        var inv = await _sut.CreateAsync(teamId: null, email: "  USER@Example.COM  ", role: "Guest");

        Assert.Equal("user@example.com", inv.Email);
    }
}
