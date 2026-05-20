namespace HttpForge.Data.Entities;

public class CollectionVariableEntry
{
    public int Id { get; set; }
    public int CollectionVariableSetId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
