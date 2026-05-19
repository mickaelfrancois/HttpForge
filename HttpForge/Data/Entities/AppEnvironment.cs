namespace HttpForge.Data.Entities;

public class AppEnvironment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<EnvironmentVariable> Variables { get; set; } = new();
}
