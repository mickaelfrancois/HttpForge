namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Source OpenAPI/Swagger URL this collection was imported from (null when imported
    // from a local file or created manually). Lets the user refresh the collection
    // against the same spec later. Editable in the refresh dialog.
    public string? SourceOpenApiUrl { get; set; }
    public int? ActiveCollectionVariableSetId { get; set; }
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariableSet> VariableSets { get; set; } = new();
    public List<CollectionFolder> Folders { get; set; } = [];
    public List<CollectionDefaultHeader> DefaultHeaders { get; set; } = [];
}
