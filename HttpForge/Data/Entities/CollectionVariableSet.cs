namespace HttpForge.Data.Entities;

public class CollectionVariableSet
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBase { get; set; }
    public List<CollectionVariableEntry> Entries { get; set; } = new();
}
