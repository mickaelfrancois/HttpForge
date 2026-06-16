namespace HttpForge.Data.Entities;

// A header applied by default to every request of the owning collection at execution
// time. Modeled in parallel to CollectionVariableEntry (FK -> Collection); deliberately
// NOT a reuse of HeaderItem, whose FK points at HttpRequestItem.
public class CollectionDefaultHeader
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
