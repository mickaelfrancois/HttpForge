namespace HttpForge.Data.Entities;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<HttpRequestItem> Requests { get; set; } = new();
    public List<CollectionVariable> Variables { get; set; } = new();
}
