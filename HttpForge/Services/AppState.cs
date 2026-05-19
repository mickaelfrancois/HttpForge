using HttpForge.Data.Entities;

namespace HttpForge.Services;

public class AppState
{
    public int? SelectedEnvironmentId { get; set; }
    public int? SelectedRequestId { get; set; }

    public event Action? OnChange;

    public void NotifyChanged() => OnChange?.Invoke();

    public Dictionary<string, string> BuildVariables(AppEnvironment? env)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (env is null) return d;
        foreach (var v in env.Variables)
            d[v.Key] = v.Value;
        return d;
    }
}
