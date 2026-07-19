namespace HttpForge.Components.Layout;

// Text-input sibling of ConfirmState: bridges the synchronous `var x = await prompt(...)`
// shape onto an async modal. AskAsync shows the dialog and returns a Task that completes
// with the entered text (Submit) or null (Cancel) — matching window.prompt semantics.
public sealed class PromptState
{
    public bool Visible { get; private set; }
    public string? Title { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string ConfirmLabel { get; private set; } = "OK";
    public string Placeholder { get; private set; } = string.Empty;

    public Action? OnChange { get; set; }

    private TaskCompletionSource<string?>? _tcs;

    public Task<string?> AskAsync(string message, string? title = null,
        string confirmLabel = "OK", string placeholder = "")
    {
        Message = message;
        Title = title;
        ConfirmLabel = confirmLabel;
        Placeholder = placeholder;
        Visible = true;

        _tcs?.TrySetResult(null);
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnChange?.Invoke();
        return _tcs.Task;
    }

    public void Submit(string? value) => Complete(value);

    public void Cancel() => Complete(null);

    private void Complete(string? result)
    {
        Visible = false;
        var tcs = _tcs;
        _tcs = null;
        OnChange?.Invoke();
        tcs?.TrySetResult(result);
    }
}
