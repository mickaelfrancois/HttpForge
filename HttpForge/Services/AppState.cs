using HttpForge.Data.Entities;

namespace HttpForge.Services;

public enum VariableSource { Global, Collection, Request }

public record ResolvedVariableEntry(string Key, string Value, bool IsSecret, VariableSource Source);

public class AppState
{
    public int? SelectedEnvironmentId { get; set; }
    public int? SelectedRequestId { get; set; }
    public int? SelectedCollectionId { get; set; }

    public event Action? OnChange;

    public void NotifyChanged() => OnChange?.Invoke();

    // Raised when a component (e.g. the Home empty state) asks the sidebar to open its
    // import menu, so the import UI stays owned by NavMenu instead of being duplicated.
    public event Action? OpenImportRequested;

    public void RequestOpenImport() => OpenImportRequested?.Invoke();

    public IReadOnlyList<ResolvedVariableEntry> BuildVariables(
        AppEnvironment? globalBase,
        AppEnvironment? globalSubset,
        CollectionVariableSet? collectionBase,
        CollectionVariableSet? collectionSubset,
        HttpRequestItem? request)
    {
        var merged = new Dictionary<string, ResolvedVariableEntry>(StringComparer.OrdinalIgnoreCase);

        if (globalBase is not null)
            foreach (var v in globalBase.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (globalSubset is not null)
            foreach (var v in globalSubset.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Global);

        if (collectionBase is not null)
            foreach (var v in collectionBase.Entries)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (collectionSubset is not null)
            foreach (var v in collectionSubset.Entries)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Collection);

        if (request is not null)
            foreach (var v in request.Variables)
                merged[v.Key] = new ResolvedVariableEntry(v.Key, v.Value, v.IsSecret, VariableSource.Request);

        return merged.Values.OrderBy(v => v.Key).ToList();
    }
}
