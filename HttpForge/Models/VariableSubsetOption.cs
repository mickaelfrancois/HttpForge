namespace HttpForge.Models;

// A pickable sub-set in VariableSetEditor. Projected from either a global AppEnvironment
// or a CollectionVariableSet so the editor stays agnostic of the concrete scope entity.
public record VariableSubsetOption(int Id, string Name);
