namespace HttpForge.Data.Entities;

public class RequestVariable
{
    public int Id { get; set; }
    public int HttpRequestItemId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
