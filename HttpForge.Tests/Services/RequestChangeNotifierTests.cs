using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class RequestChangeNotifierTests
{
    [Fact]
    public async Task NotifyAsync_FiresSubscribedHandler()
    {
        var notifier = new RequestChangeNotifier();
        int receivedRequestId = 0;
        string receivedOrigin = "";

        notifier.RequestSaved += (id, origin) =>
        {
            receivedRequestId = id;
            receivedOrigin = origin;
            return Task.CompletedTask;
        };

        await notifier.NotifyAsync(42, "origin-123");

        Assert.Equal(42, receivedRequestId);
        Assert.Equal("origin-123", receivedOrigin);
    }

    [Fact]
    public async Task NotifyAsync_NoHandlers_DoesNotThrow()
    {
        var notifier = new RequestChangeNotifier();
        var ex = await Record.ExceptionAsync(() => notifier.NotifyAsync(1, "origin"));
        Assert.Null(ex);
    }

    // Regression: a "zombie" subscriber from a disconnected-but-not-yet-disposed
    // circuit can throw (e.g. ObjectDisposedException). NotifyAsync runs on the
    // *saving* circuit's thread, so a leaked exception would tear that circuit down
    // and show the blank reconnect overlay ("save -> black screen"). The faulty
    // subscriber must be isolated and the remaining subscribers still notified.
    [Fact]
    public async Task NotifyAsync_SubscriberThrows_IsolatesFailureAndNotifiesOthers()
    {
        var notifier = new RequestChangeNotifier();
        notifier.RequestSaved += (_, _) => throw new ObjectDisposedException("TabManagerService");

        bool liveNotified = false;
        notifier.RequestSaved += (_, _) => { liveNotified = true; return Task.CompletedTask; };

        var ex = await Record.ExceptionAsync(() => notifier.NotifyAsync(42, "origin-123"));

        Assert.Null(ex);            // exception must not escape to the saver
        Assert.True(liveNotified);  // later subscribers must still be notified
    }
}
