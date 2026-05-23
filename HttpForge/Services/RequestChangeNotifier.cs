using System.Linq;

namespace HttpForge.Services;

public class RequestChangeNotifier
{
    public event Func<int, string, string, Task>? RequestSaved;

    public async Task NotifyAsync(int requestId, string savedByUserId, string savedByUserName)
    {
        if (RequestSaved is { } ev)
            foreach (var handler in ev.GetInvocationList().Cast<Func<int, string, string, Task>>())
                await handler(requestId, savedByUserId, savedByUserName);
    }
}
