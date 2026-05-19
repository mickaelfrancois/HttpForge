namespace HttpForge.Data.Entities;

public class CollectionVariable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
