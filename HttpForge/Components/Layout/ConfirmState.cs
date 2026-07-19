namespace HttpForge.Components.Layout;

// Bridges the synchronous `if (!await Confirm(msg)) return;` call shape onto an async,
// event-driven Blazor modal. AskAsync shows the dialog and returns a Task that completes
// with the user's choice (true = confirmed) when Confirm/Cancel is invoked.
//
// Owned as a plain field by the hosting component: OnChange captures that component's
// render callback, so its lifetime matches the component — nothing to unsubscribe.
public sealed class ConfirmState
{
    public bool Visible { get; private set; }
    public string? Title { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string ConfirmLabel { get; private set; } = "Supprimer";
    public bool Danger { get; private set; } = true;

    // Set by the host component to re-render when the dialog opens/closes.
    public Action? OnChange { get; set; }

    private TaskCompletionSource<bool>? _tcs;

    public Task<bool> AskAsync(string message, string? title = null,
        string confirmLabel = "Supprimer", bool danger = true)
    {
        Message = message;
        Title = title;
        ConfirmLabel = confirmLabel;
        Danger = danger;
        Visible = true;

        // A still-open prior prompt resolves to false before a new one replaces it, so a
        // stale awaiter never hangs.
        _tcs?.TrySetResult(false);
        // Run the awaiter's continuation off the click handler's stack (avoids re-entering
        // the renderer synchronously from within the Confirm/Cancel event).
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnChange?.Invoke();
        return _tcs.Task;
    }

    public void Confirm() => Complete(true);

    public void Cancel() => Complete(false);

    private void Complete(bool result)
    {
        Visible = false;
        var tcs = _tcs;
        _tcs = null;
        OnChange?.Invoke();
        tcs?.TrySetResult(result);
    }
}
