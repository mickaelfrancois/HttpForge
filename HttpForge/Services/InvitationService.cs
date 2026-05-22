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

    public async Task<InvitationToken> CreateAsync(int? teamId, string email, string role, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = new InvitationToken
        {
            TeamId = teamId,
            Email = email.ToLower().Trim(),
            Role = role,
            Token = GenerateToken(),
            ExpiresAt = DateTime.UtcNow.AddHours(72)
        };
        db.InvitationTokens.Add(invitation);
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    public async Task<InvitationToken?> ValidateAsync(string token, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = await db.InvitationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invitation is null) return null;
        if (invitation.UsedAt is not null) return null;
        if (invitation.ExpiresAt < DateTime.UtcNow) return null;
        return invitation;
    }

    public async Task MarkUsedAsync(int invitationId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = await db.InvitationTokens.FirstAsync(i => i.Id == invitationId, ct);
        invitation.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
