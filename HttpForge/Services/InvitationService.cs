using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HttpForge.Services;

public class InvitationService(IDbContextFactory<AppDbContext> dbFactory)
{
    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }

    public async Task<InvitationToken> CreateAsync(int? teamId, string email, string role)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = new InvitationToken
        {
            TeamId = teamId,
            Email = email.ToLower().Trim(),
            Role = role,
            Token = GenerateToken(),
            ExpiresAt = DateTime.UtcNow.AddHours(72)
        };
        db.InvitationTokens.Add(invitation);
        await db.SaveChangesAsync();
        return invitation;
    }

    public async Task<InvitationToken?> ValidateAsync(string token)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = await db.InvitationTokens
            .FirstOrDefaultAsync(i => i.Token == token);
        if (invitation is null) return null;
        if (invitation.UsedAt is not null) return null;
        if (invitation.ExpiresAt < DateTime.UtcNow) return null;
        return invitation;
    }

    public async Task MarkUsedAsync(int invitationId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var invitation = await db.InvitationTokens.FirstAsync(i => i.Id == invitationId);
        invitation.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
