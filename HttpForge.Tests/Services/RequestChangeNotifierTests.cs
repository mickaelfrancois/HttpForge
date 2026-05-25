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
}
