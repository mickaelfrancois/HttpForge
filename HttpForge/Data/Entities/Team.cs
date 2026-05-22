namespace HttpForge.Data.Entities;

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TeamMember> Members { get; set; } = [];
}
