# Cancel an In-Flight Request — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While a request is executing, swap the disabled Send button for an enabled Cancel button that aborts the in-flight HTTP call and returns the response pane to its empty state.

**Architecture:** A per-tab `CancellationTokenSource` on `TabState` is created in `Home.SendAsync` and passed to `RequestExecutor.ExecuteAsync`. `CancelSend` cancels it. The executor re-throws on user cancellation (distinguished from the 2-minute timeout via `ct.IsCancellationRequested`) so `SendAsync` can leave the response pane cleared. The URL bar shows Cancel or Send based on `tab.IsSending`.

**Tech Stack:** .NET 10, Blazor Server, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-05-27-cancel-request-design.md`

**Branch:** `worktree-cancel-request` (already checked out in this worktree).

---

## File Structure

- **Modify** `HttpForge/Services/RequestExecutor.cs` — re-throw on user cancellation.
- **Modify** `HttpForge/Models/TabState.cs` — add `SendCts`.
- **Modify** `HttpForge/Components/Pages/Home.razor` — CTS wiring in `SendAsync`, `CancelSend`, conditional button.
- **Modify** `HttpForge/Components/Pages/Home.razor.css` — `.cancel-btn` style.
- **Modify** `HttpForge.Tests/Services/RequestExecutorTests.cs` — cancellation test.

All commands run from the worktree root:
`D:\Development\HttpForge\.claude\worktrees\cancel-request`

---

## Task 1: Executor re-throws on user cancellation

**Files:**
- Modify: `HttpForge/Services/RequestExecutor.cs` (the `catch` in `ExecuteAsync`)
- Test: `HttpForge.Tests/Services/RequestExecutorTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HttpForge.Tests/Services/RequestExecutorTests.cs` (inside the class, after the TLS tests):

```csharp
    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var (sut, _) = Create();
        var req = new HttpRequestItem { Url = "https://example.com" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ExecuteAsync(req, NoVars, cts.Token));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~ExecuteAsync_CancelledToken"`
Expected: FAIL — no exception is thrown. The current general `catch` swallows the `OperationCanceledException` and returns an error `ExecutionResult` instead.

- [ ] **Step 3: Add the filtered catch**

In `HttpForge/Services/RequestExecutor.cs`, in `ExecuteAsync`, the current catch block is:

```csharp
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecutionResult(0, string.Empty, string.Empty, new(), sw.ElapsedMilliseconds, 0, ex.Message);
        }
```

Add a filtered catch immediately **before** it:

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

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestExecutorTests"`
Expected: PASS — the new test green, all existing executor tests still green (the `InvalidUrl` test confirms the general catch still returns an error result for non-cancellation failures).

- [ ] **Step 5: Commit**

```bash
git add HttpForge/Services/RequestExecutor.cs HttpForge.Tests/Services/RequestExecutorTests.cs
git commit -m "feat: re-throw on user cancellation in RequestExecutor"
```

---

## Task 2: Per-tab cancellation wiring + Cancel button

**Files:**
- Modify: `HttpForge/Models/TabState.cs`
- Modify: `HttpForge/Components/Pages/Home.razor` (button markup ~line 67, `SendAsync` ~line 797, new `CancelSend`)
- Modify: `HttpForge/Components/Pages/Home.razor.css`

No automated test: `Home.razor` is `InteractiveServer` and is not mounted in the bUnit suite. Verified by build + manual smoke test, consistent with the project's approach to UI.

- [ ] **Step 1: Add the per-tab CancellationTokenSource**

In `HttpForge/Models/TabState.cs`, add after `public bool IsSending { get; set; }`:

```csharp
    public bool IsSending { get; set; }
    public CancellationTokenSource? SendCts { get; set; }
```

- [ ] **Step 2: Wire the CTS into `SendAsync`**

In `HttpForge/Components/Pages/Home.razor`, the current block in `SendAsync` is:

```csharp
        tab.IsSending = true;
        tab.Result = null;
        tab.ScriptResult = null;
        StateHasChanged();

        try
        {
            var result = await Executor.ExecuteAsync(request, vars);
            tab.Result = result;
```

Replace it with:

```csharp
        var cts = new CancellationTokenSource();
        tab.SendCts = cts;
        tab.IsSending = true;
        tab.Result = null;
        tab.ScriptResult = null;
        StateHasChanged();

        try
        {
            var result = await Executor.ExecuteAsync(request, vars, cts.Token);
            tab.Result = result;
```

- [ ] **Step 3: Handle cancellation and clean up the CTS**

In `HttpForge/Components/Pages/Home.razor`, the current `finally` at the end of `SendAsync` is:

```csharp
        finally
        {
            tab.IsSending = false;
            StateHasChanged();
        }
    }
```

Replace it (adding a `catch` before it) with:

```csharp
        catch (OperationCanceledException)
        {
            // User cancelled — leave the response pane cleared (tab.Result stays null).
        }
        finally
        {
            tab.IsSending = false;
            cts.Dispose();
            tab.SendCts = null;
            StateHasChanged();
        }
    }
```

- [ ] **Step 4: Add the `CancelSend` handler**

In `HttpForge/Components/Pages/Home.razor`, immediately after the `SendAsync` method's closing brace, add:

```csharp
    private void CancelSend()
    {
        Active?.SendCts?.Cancel();
    }
```

- [ ] **Step 5: Make the button conditional**

In `HttpForge/Components/Pages/Home.razor`, the current Send button (around line 67) is:

```razor
            <button class="send-btn" @onclick="SendAsync" disabled="@tab.IsSending">
                @(tab.IsSending ? "Sending…" : "Send")
            </button>
```

Replace it with:

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

- [ ] **Step 6: Add the `.cancel-btn` style**

In `HttpForge/Components/Pages/Home.razor.css`, after the `.send-btn:disabled` rule (around line 105), add:

```css
.cancel-btn {
    background: var(--error-text);
    color: #fff;
    border: none;
    border-radius: 3px;
    padding: 0.45rem 1.5rem;
    cursor: pointer;
    font-weight: 600;
}

.cancel-btn:hover {
    opacity: 0.9;
}
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build HttpForge`
Expected: 0 errors (pre-existing warnings only).

- [ ] **Step 8: Manual smoke test**

Run: `dotnet run --project HttpForge`, then in the browser:
1. Send a request to a slow endpoint (e.g., `https://httpbin.org/delay/10`).
2. While it runs, the button reads **Cancel** (red) instead of a disabled "Sending…".
3. Click **Cancel** → the request stops immediately, the button reverts to **Send**, and the response pane shows "No response yet. Click Send." (no error).
4. Send again → works normally; a fast request still shows its response.
5. Open a second tab, send in both → cancelling one does not affect the other.

- [ ] **Step 9: Commit**

```bash
git add HttpForge/Models/TabState.cs HttpForge/Components/Pages/Home.razor HttpForge/Components/Pages/Home.razor.css
git commit -m "feat: cancel in-flight request via Cancel button"
```

---

## Task 3: Final verification

- [ ] **Step 1: Run the full suite**

Run: `dotnet test HttpForge.Tests`
Expected: all tests pass (114 existing + 1 new = 115).

---

## Notes for the implementer

- **Why re-throw instead of returning a "cancelled" result:** the spec calls for the response pane to return to its empty state on cancel. Re-throwing lets `SendAsync` skip `tab.Result = result;`, leaving `tab.Result` at the `null` it was reset to. The 2-minute `HttpClient.Timeout` is unaffected: it cancels HttpClient's internal token, not `ct`, so `ct.IsCancellationRequested` is false and it falls through to the general catch (timeout error result, as today).
- **CTS lifetime:** created per send, disposed in `finally`, and `tab.SendCts` nulled in the same `finally`. Blazor Server circuits are single-threaded, so `CancelSend` can never observe a disposed-but-non-null CTS.
- **Per-tab isolation:** the CTS lives on `TabState`, so each tab cancels only its own request.
