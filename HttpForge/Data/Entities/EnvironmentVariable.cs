namespace HttpForge.Data.Entities;

public class EnvironmentVariable
{
    public int Id { get; set; }
    public int AppEnvironmentId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
