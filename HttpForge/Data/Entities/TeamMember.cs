namespace HttpForge.Data.Entities;

public enum TeamRole { TeamAdmin, Contributor, Guest }

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public TeamRole Role { get; set; }
    public Team Team { get; set; } = null!; // navigation property used by TeamService
}
