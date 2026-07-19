using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;

namespace HttpForge.Tests.Unit;

// TabState keeps a bounded, most-recent-first response history in memory.
public class ResponseHistoryTests
{
    private static ResponseHistoryEntry Entry(int status) =>
        new(DateTime.UnixEpoch, HttpMethodKind.GET, "https://example.com",
            new ExecutionResult(status, "", "", new(), 1, 0, null), null);

    [Fact]
    public void AddHistory_KeepsMostRecentFirst()
    {
        var tab = new TabState { Kind = TabKind.Request };

        tab.AddHistory(Entry(200));
        tab.AddHistory(Entry(404));

        Assert.Equal(404, tab.History[0].Result.StatusCode);
        Assert.Equal(200, tab.History[1].Result.StatusCode);
    }

    [Fact]
    public void AddHistory_CapsAtMaxHistory_DroppingOldest()
    {
        var tab = new TabState { Kind = TabKind.Request };

        // Add more than the cap; statuses encode insertion order.
        for (var i = 0; i < TabState.MaxHistory + 5; i++)
            tab.AddHistory(Entry(1000 + i));

        Assert.Equal(TabState.MaxHistory, tab.History.Count);
        Assert.Equal(1000 + TabState.MaxHistory + 4, tab.History[0].Result.StatusCode); // newest
        Assert.Equal(1000 + 5, tab.History[^1].Result.StatusCode);                      // oldest kept
    }
}
