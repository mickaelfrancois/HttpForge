using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Models;

// One past response kept in a tab's in-memory history. Wraps the full ExecutionResult
// (status, body, headers, timing) plus the script outcome, so viewing an entry can restore
// the response pane without re-sending. A record type also makes it trivial to persist to a
// table later if history should survive sessions.
public sealed record ResponseHistoryEntry(
    DateTime At,
    HttpMethodKind Method,
    string Url,
    ExecutionResult Result,
    ScriptResult? ScriptResult);
