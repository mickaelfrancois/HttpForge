namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ActiveCollectionVariableSetId { get; set; }
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariableSet> VariableSets { get; set; } = new();
    public List<CollectionFolder> Folders { get; set; } = [];
}
