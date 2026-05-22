namespace HttpForge.Data.Entities;

public class InvitationToken
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // TeamRole name or "SuperAdmin"
    public string Token { get; set; } = string.Empty; // cryptographically random 32-byte hex
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
