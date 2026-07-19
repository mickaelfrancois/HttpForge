namespace HttpForge.Data.Entities;

// Shared editable shape of a variable row so VariableSetEditor can edit a global
// EnvironmentVariable or a CollectionVariableEntry without knowing the concrete type.
// Id is read-only here (identity, assigned by EF); Key/Value/IsSecret are edited in place.
public interface IVariableEntry
{
    int Id { get; }
    string Key { get; set; }
    string Value { get; set; }
    bool IsSecret { get; set; }
}
