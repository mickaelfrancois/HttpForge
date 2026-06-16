namespace HttpForge.Services;

/// <summary>
/// Debounced auto-save trigger — one timer per open tab, keyed by RequestId.
///
/// Pure POCO: it has no dependency on IJSRuntime or the Blazor renderer, so it is
/// unit-testable with a <see cref="FakeTimeProvider"/> (advance time → the timer
/// fires deterministically). The actual write is delegated to a caller-supplied
/// callback — Home.razor wires it to its tab-save flow, which reuses
/// <see cref="RequestSaveService"/> (conflict detection, notifier, dirty tracking).
///
/// Scoped (one instance per circuit). The DI container disposes it at circuit end,
/// cancelling any in-flight timer.
/// </summary>
public sealed class RequestAutoSaver : IAsyncDisposable
{
    /// <summary>Inactivity window before a dirty draft is auto-saved.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(1500);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    // CancellationTokenSource doubles as the identity of a scheduled timer: a later
    // RunAsync continuation only mutates its own slot by comparing references.
    private readonly Dictionary<int, (CancellationTokenSource Cts, Task Task)> _entries = [];
    private readonly HashSet<int> _suspended = [];
    private bool _disposed;

    public RequestAutoSaver(TimeProvider timeProvider) : this(timeProvider, DefaultDelay) { }

    public RequestAutoSaver(TimeProvider timeProvider, TimeSpan delay)
    {
        _timeProvider = timeProvider;
        _delay = delay;
    }

    /// <summary>
    /// (Re)arms the debounce timer for a tab. Each call cancels the pending delay and
    /// starts a fresh one; when the delay elapses without another <see cref="Schedule"/>,
    /// the callback fires once. No-op if the tab is suspended (a conflict is pending) or
    /// the saver is disposed.
    /// </summary>
    public void Schedule(int tabKey, Func<CancellationToken, Task> saveCallback)
    {
        ArgumentNullException.ThrowIfNull(saveCallback);
        lock (_gate)
        {
            if (_disposed || _suspended.Contains(tabKey)) return;
            CancelLocked(tabKey);
            var cts = new CancellationTokenSource();
            // RunAsync runs synchronously up to its first await (Task.Delay) and yields
            // the in-flight Task, which we record alongside its CTS.
            var task = RunAsync(tabKey, saveCallback, cts);
            _entries[tabKey] = (cts, task);
        }
    }

    /// <summary>Cancels the pending timer for a tab without saving (tab closed / switched away).</summary>
    public void Cancel(int tabKey)
    {
        lock (_gate) CancelLocked(tabKey);
    }

    /// <summary>
    /// Suspends auto-save for a tab (a save conflict is pending): cancels the current
    /// timer and makes future <see cref="Schedule"/> calls a no-op until <see cref="Resume"/>.
    /// Prevents an auto-save loop that would repeatedly re-open the conflict modal.
    /// </summary>
    public void Suspend(int tabKey)
    {
        lock (_gate)
        {
            _suspended.Add(tabKey);
            CancelLocked(tabKey);
        }
    }

    /// <summary>Re-enables auto-save for a tab after a conflict has been resolved.</summary>
    public void Resume(int tabKey)
    {
        lock (_gate) _suspended.Remove(tabKey);
    }

    /// <summary>True when auto-save is currently suspended for a tab (conflict pending).</summary>
    public bool IsSuspended(int tabKey)
    {
        lock (_gate) return _suspended.Contains(tabKey);
    }

    /// <summary>
    /// Awaits any in-flight save tasks. Test helper: call only after advancing time past
    /// the delay (or after Cancel/Dispose). Calling it while a timer is mid-delay would
    /// block until that delay elapses.
    /// </summary>
    public Task WhenIdleAsync()
    {
        Task[] snapshot;
        lock (_gate) snapshot = _entries.Values.Select(e => e.Task).ToArray();
        return Task.WhenAll(snapshot);
    }

    private async Task RunAsync(int tabKey, Func<CancellationToken, Task> saveCallback, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            await Task.Delay(_delay, _timeProvider, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Re-armed or cancelled before firing — the replacing/cancelling call owns cleanup.
            return;
        }

        if (token.IsCancellationRequested) return;

        try
        {
            await saveCallback(token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                // Only clear our own slot: a re-arm during the save would have replaced it.
                if (_entries.TryGetValue(tabKey, out var current) && ReferenceEquals(current.Cts, cts))
                    _entries.Remove(tabKey);
            }
            cts.Dispose();
        }
    }

    private void CancelLocked(int tabKey)
    {
        if (!_entries.Remove(tabKey, out var entry)) return;
        entry.Cts.Cancel();
        entry.Cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var (cts, _) in _entries.Values)
                cts.Cancel();
            pending = _entries.Values.Select(e => e.Task).ToArray();
        }

        // Drain in-flight tasks so no fire-and-forget save outlives the saver. Delayed
        // timers complete as cancelled (RunAsync swallows the OCE); a save already past
        // the delay runs to completion. Dispose must never throw, so any fault surfaced by
        // a save that fired just before disposal is swallowed here by design.
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch { /* dispose path: cancellations expected, other faults must not escape */ }

        lock (_gate)
        {
            // Timers cancelled mid-delay return via RunAsync's OperationCanceledException
            // path, which does not self-clean — their CTS is still here and must be disposed.
            // (Timers cancelled mid-save dispose themselves in RunAsync's finally.)
            foreach (var (cts, _) in _entries.Values)
                cts.Dispose();
            _entries.Clear();
        }
    }
}
