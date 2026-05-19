namespace HttpForge.Data.Entities;

public class FormFieldItem
{
    public int Id { get; set; }
    public int HttpRequestItemId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
