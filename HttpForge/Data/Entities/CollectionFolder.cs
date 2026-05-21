namespace HttpForge.Data.Entities;

public class CollectionFolder
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public int? ParentFolderId { get; set; }
    public CollectionFolder? ParentFolder { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<CollectionFolder> Children { get; set; } = [];
    public List<HttpRequestItem> Requests { get; set; } = [];
}
