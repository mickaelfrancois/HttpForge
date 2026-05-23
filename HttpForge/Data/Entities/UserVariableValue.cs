namespace HttpForge.Data.Entities;

public class UserVariableValue
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty; // "global_env" | "collection_varset" | "request"
    public int ScopeId { get; set; }
    public string VariableKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}

public static class UserVariableScope
{
    public const string GlobalEnv = "global_env";
    public const string CollectionVarSet = "collection_varset";
    public const string Request = "request";
}
