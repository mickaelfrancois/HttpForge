using HttpForge.Services;

namespace HttpForge.Tests.Unit;

public class ConflictDetectionTests
{
    [Fact]
    public void HasConflict_DbUpdatedAfterLoad_ReturnsTrue()
    {
        var loadedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dbUpdatedAt = loadedAt.AddSeconds(1);
        Assert.True(RequestSaveService.HasConflict(dbUpdatedAt, loadedAt));
    }

    [Fact]
    public void HasConflict_DbUpdatedBeforeLoad_ReturnsFalse()
    {
        var loadedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dbUpdatedAt = loadedAt.AddSeconds(-1);
        Assert.False(RequestSaveService.HasConflict(dbUpdatedAt, loadedAt));
    }

    [Fact]
    public void HasConflict_SameTimestamp_ReturnsFalse()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(RequestSaveService.HasConflict(ts, ts));
    }
}
