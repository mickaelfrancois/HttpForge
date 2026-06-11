using System.Linq;
using Microsoft.Extensions.Logging;

namespace HttpForge.Services;

public class RequestChangeNotifier(ILogger<RequestChangeNotifier>? logger = null)
{
    // Second arg is the originId of the saving circuit (a per-circuit GUID), so a
    // circuit can ignore notifications for its own saves. No user identity is involved —
    // this only guards the "same request open in two windows" case.
    public event Func<int, string, Task>? RequestSaved;

    public async Task NotifyAsync(int requestId, string originId)
    {
        if (RequestSaved is not { } ev) return;

        // This singleton fans out to every subscribed circuit, including ones that
        // have disconnected but are not yet disposed. Such a "zombie" subscriber can
        // throw (e.g. ObjectDisposedException) when it touches its scoped state or a
        // tearing-down renderer. Each handler MUST be isolated: it runs on the
        // *saving* circuit's thread, so letting an exception escape would tear that
        // circuit down and show the blank reconnect overlay.
        foreach (var handler in ev.GetInvocationList().Cast<Func<int, string, Task>>())
        {
            try
            {
                await handler(requestId, originId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A RequestSaved subscriber threw while being notified of request {RequestId}; skipping it.", requestId);
            }
        }
    }
}
