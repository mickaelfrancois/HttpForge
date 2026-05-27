using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Models;

public class TabState
{
    public int RequestId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public RequestDraft Draft { get; set; } = null!;
    public string ActiveSubTab { get; set; } = "Params";
    public string ActiveResponseTab { get; set; } = "Body";
    public List<UserVariableValue> UserVarValues { get; set; } = [];
    public Dictionary<string, string> PendingPersonalValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExecutionResult? Result { get; set; }
    public ScriptResult? ScriptResult { get; set; }
    public bool IsSending { get; set; }
    public CancellationTokenSource? SendCts { get; set; }
    public bool IsReadOnly { get; set; }
}
