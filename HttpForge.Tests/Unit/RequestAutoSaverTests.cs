using HttpForge.Services;
using Microsoft.Extensions.Time.Testing;

namespace HttpForge.Tests.Unit;

public class RequestAutoSaverTests
{
    private static readonly TimeSpan Delay = RequestAutoSaver.DefaultDelay;

    [Fact]
    public async Task Schedule_AfterDelayElapses_InvokesCallbackOnce()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;

        // Act
        saver.Schedule(1, _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        time.Advance(Delay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Schedule_ThreeRapidCalls_DebouncesToSingleInvocation()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;
        Func<CancellationToken, Task> cb = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };

        // Act — three edits within the debounce window collapse to one save
        saver.Schedule(1, cb);
        saver.Schedule(1, cb);
        saver.Schedule(1, cb);
        time.Advance(Delay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Schedule_NeverIdleLongEnough_NeverInvokes()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;
        Func<CancellationToken, Task> cb = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };

        // Act — each re-arm happens before the window elapses, so it never fires
        var partial = TimeSpan.FromTicks(Delay.Ticks / 2);
        saver.Schedule(1, cb);
        time.Advance(partial);
        saver.Schedule(1, cb);
        time.Advance(partial);

        // Assert (no WhenIdleAsync: a timer is still mid-delay; await using disposes it)
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Cancel_BeforeDelayElapses_NeverInvokes()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;

        // Act
        saver.Schedule(1, _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        saver.Cancel(1);
        time.Advance(Delay);
        await saver.WhenIdleAsync();

        // Assert — no ghost write after cancel
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Suspend_BlocksFurtherScheduling_NeverInvokes()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;
        Func<CancellationToken, Task> cb = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };

        // Act
        saver.Suspend(1);
        saver.Schedule(1, cb);     // no-op while suspended
        time.Advance(Delay + Delay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.True(saver.IsSuspended(1));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Resume_AfterSuspend_AllowsSchedulingAgain()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var count = 0;
        Func<CancellationToken, Task> cb = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };

        // Act
        saver.Suspend(1);
        saver.Resume(1);
        saver.Schedule(1, cb);
        time.Advance(Delay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.False(saver.IsSuspended(1));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DisposeAsync_CancelsPendingTimers_NeverInvokesAfterwards()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var saver = new RequestAutoSaver(time);
        var count = 0;
        saver.Schedule(1, _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        // Act
        await saver.DisposeAsync();
        time.Advance(Delay);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Schedule_DistinctTabs_AreIndependent()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var saver = new RequestAutoSaver(time);
        var countA = 0;
        var countB = 0;

        // Act — cancelling one tab must not affect the other
        saver.Schedule(1, _ => { Interlocked.Increment(ref countA); return Task.CompletedTask; });
        saver.Schedule(2, _ => { Interlocked.Increment(ref countB); return Task.CompletedTask; });
        saver.Cancel(1);
        time.Advance(Delay);
        await saver.WhenIdleAsync();

        // Assert
        Assert.Equal(0, countA);
        Assert.Equal(1, countB);
    }
}
