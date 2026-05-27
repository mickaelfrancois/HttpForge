using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class RequestChangeNotifierTests
{
    [Fact]
    public async Task NotifyAsync_FiresSubscribedHandler()
    {
        var notifier = new RequestChangeNotifier();
        int receivedRequestId = 0;
        string receivedUserId = "";
        string receivedUserName = "";

        notifier.RequestSaved += (id, uid, name) =>
        {
            receivedRequestId = id;
            receivedUserId = uid;
            receivedUserName = name;
            return Task.CompletedTask;
        };

        await notifier.NotifyAsync(42, "user-123", "Alice");

        Assert.Equal(42, receivedRequestId);
        Assert.Equal("user-123", receivedUserId);
        Assert.Equal("Alice", receivedUserName);
    }

    [Fact]
    public async Task NotifyAsync_NoHandlers_DoesNotThrow()
    {
        var notifier = new RequestChangeNotifier();
        var ex = await Record.ExceptionAsync(() => notifier.NotifyAsync(1, "u", "n"));
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
        notifier.RequestSaved += (_, _, _) => throw new ObjectDisposedException("TabManagerService");

        bool liveNotified = false;
        notifier.RequestSaved += (_, _, _) => { liveNotified = true; return Task.CompletedTask; };

        var ex = await Record.ExceptionAsync(() => notifier.NotifyAsync(42, "user-123", "Alice"));

        Assert.Null(ex);            // exception must not escape to the saver
        Assert.True(liveNotified);  // later subscribers must still be notified
    }
}
