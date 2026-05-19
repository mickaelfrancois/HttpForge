using HttpForge.Data.Entities;

namespace HttpForge.Services;

public enum VariableSource { Global, Collection, Request }

public record ResolvedVariableEntry(string Key, string Value, bool IsSecret, VariableSource Source);

public class AppState
{
    public int? SelectedEnvironmentId { get; set; }
    public int? SelectedRequestId { get; set; }

    public event Action? OnChange;

    public void NotifyChanged() => OnChange?.Invoke();

    public IReadOnlyList<ResolvedVariableEntry> BuildVariables(
        AppEnvironment? env,
        Collection? collection,
        HttpRequestItem? request)
    {
        var merged = new Dictionary<string, ResolvedVariableEntry>(StringComparer.OrdinalIgnoreCase);

        if (env is not null)
            foreach (var v in env.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (collection is not null)
            foreach (var v in collection.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (request is not null)
            foreach (var v in request.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Request);

        return merged.Values.OrderBy(v => v.Key).ToList();
    }
}
