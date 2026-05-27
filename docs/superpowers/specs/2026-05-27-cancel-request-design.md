# Cancel an in-flight request

**Date:** 2026-05-27
**Status:** Approved design

## Problem

While a request is executing, the **Send** button is disabled and shows
"Sending…". A slow or hung request cannot be stopped — the user must wait for it
to complete or time out (the executor caps requests at 2 minutes). There is no
way to interrupt.

## Goal

While a request is in flight, replace the disabled Send button with an enabled
**Cancel** button. Clicking it aborts the in-flight HTTP request immediately and
returns the response pane to its empty state.

## Non-goals

- Cancelling the post-request script (it runs only after a response is received).
- Cancelling the pre-send draft save.
- Changing the existing 2-minute timeout behavior.

## Design

### State (`TabState`)

Add `CancellationTokenSource? SendCts`. Cancellation is per-tab, so independent
tabs can send and cancel without affecting each other.

### Execution (`Home.SendAsync`)

- Before executing: create a fresh `CancellationTokenSource`, assign it to
  `tab.SendCts`, set `tab.IsSending = true`, and clear `tab.Result`.
- Pass `cts.Token` to `Executor.ExecuteAsync(request, vars, cts.Token)`.
- `catch (OperationCanceledException)`: do nothing. `tab.Result` stays `null`, so
  the response pane shows its empty state ("No response yet. Click Send.").
- `finally`: `tab.IsSending = false`, dispose the CTS, clear `tab.SendCts`,
  re-render.

Add a `CancelSend()` handler that calls `Active?.SendCts?.Cancel()`.

### Executor (`RequestExecutor.ExecuteAsync`)

Today every exception is caught and converted to an error `ExecutionResult`. To
let the caller distinguish a deliberate cancellation from a real failure, add a
filtered catch **before** the general catch:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    sw.Stop();
    return new ExecutionResult(0, string.Empty, string.Empty, new(), sw.ElapsedMilliseconds, 0, ex.Message);
}
```

`ct.IsCancellationRequested` is the discriminator: a user cancel trips our token
and re-throws; the 2-minute `HttpClient.Timeout` cancels HttpClient's *internal*
token (not ours), so it falls through to the general catch and keeps returning a
timeout error result exactly as today.

### UI (`Home.razor` + `app.css`)

Replace the single disabled button with a conditional:

```razor
@if (tab.IsSending)
{
    <button class="cancel-btn" @onclick="CancelSend">Cancel</button>
}
else
{
    <button class="send-btn" @onclick="SendAsync">Send</button>
}
```

Add a `.cancel-btn` style with a danger tint so the state change is obvious.

## Testing

1. **Executor rethrow** (unit, `RequestExecutorTests`): calling `ExecuteAsync`
   with an already-cancelled token throws `OperationCanceledException` (proves the
   filtered catch re-throws instead of swallowing). The fake handler throws on a
   cancelled token via its existing `ThrowIfCancellationRequested`.
2. **Existing error path unchanged**: a non-cancellation failure still returns an
   error `ExecutionResult` (covered by the existing `InvalidUrl` test).
3. **Button toggle + cleared pane**: manual smoke test — `Home.razor` is
   `InteractiveServer` and is not mounted in the bUnit suite, consistent with the
   project's approach to UI verification.

## Edge cases

- **Cancel during the post-request script:** the HTTP call has already completed,
  so `ExecuteAsync` returns normally; cancelling the now-useless token has no
  effect. The button reverts in `finally`. Acceptable and rare.
- **Double cancel / cancel after completion:** `SendCts?.Cancel()` on a disposed
  or null CTS is guarded by the null-conditional and the `finally` ordering.
