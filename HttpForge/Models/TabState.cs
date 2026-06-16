using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Models;

public class TabState
{
    public int RequestId { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public RequestDraft Draft { get; set; } = null!;
    public string ActiveSubTab { get; set; } = "Params";
    public string ActiveResponseTab { get; set; } = "Body";
    public ExecutionResult? Result { get; set; }
    public ScriptResult? ScriptResult { get; set; }
    public bool IsSending { get; set; }
    public CancellationTokenSource? SendCts { get; set; }

    // Auto-save status indicator (debounced background save).
    public DateTime? LastSavedAt { get; set; }
    public bool IsAutoSaving { get; set; }
}
