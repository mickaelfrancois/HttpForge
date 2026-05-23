namespace HttpForge.Services;

public class RequestChangeNotifier
{
    public event Func<int, string, string, Task>? RequestSaved;

    public async Task NotifyAsync(int requestId, string savedByUserId, string savedByUserName)
    {
        if (RequestSaved is not null)
            await RequestSaved.Invoke(requestId, savedByUserId, savedByUserName);
    }
}
