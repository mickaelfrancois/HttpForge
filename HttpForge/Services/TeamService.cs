using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class TeamService(
    IDbContextFactory<AppDbContext> dbFactory,
    InvitationService invitationService)
{
    public async Task<Team> CreateTeamAsync(string name, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var team = new Team { Name = name.Trim() };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return team;
    }

    public async Task DeleteTeamAsync(int teamId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var team = await db.Teams.FirstAsync(t => t.Id == teamId, ct);
        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Team>> GetAllTeamsAsync(CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Teams.AsNoTracking().Include(t => t.Members).OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<Team?> GetTeamAsync(int teamId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Teams.AsNoTracking().Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId, ct);
    }

    public async Task<InvitationToken> InviteMemberAsync(int teamId, string email, TeamRole role, CancellationToken ct = default)
        => await invitationService.CreateAsync(teamId, email, role.ToString(), ct);

    // Returns (true, null) if added directly, (false, token) if invitation created, throws if already a member.
    public async Task<(bool DirectlyAdded, InvitationToken? Invitation)> AddOrInviteMemberAsync(
        int teamId, string email, TeamRole role, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLower();
        using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpper(), ct);

        if (user is not null)
        {
            var alreadyMember = await db.TeamMembers
                .AnyAsync(m => m.TeamId == teamId && m.UserId == user.Id, ct);
            if (alreadyMember)
                throw new InvalidOperationException("This user is already a member of the team.");

            db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = user.Id, Role = role });
            await db.SaveChangesAsync(ct);
            return (true, null);
        }

        var invitation = await invitationService.CreateAsync(teamId, normalizedEmail, role.ToString(), ct);
        return (false, invitation);
    }

    public async Task RemoveMemberAsync(int teamId, string userId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
        if (member is not null)
        {
            db.TeamMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task AssignCollectionAsync(int teamId, int collectionId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var col = await db.Collections.FirstAsync(c => c.Id == collectionId, ct);
        col.TeamId = teamId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Collection>> GetOrphanedCollectionsAsync(CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Collections.AsNoTracking().Where(c => c.TeamId == null).OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<List<Collection>> GetTeamCollectionsAsync(int teamId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Collections.AsNoTracking().Where(c => c.TeamId == teamId).OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<List<InvitationToken>> GetPendingInvitationsAsync(int teamId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.InvitationTokens.AsNoTracking()
            .Where(i => i.TeamId == teamId && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.ExpiresAt)
            .ToListAsync(ct);
    }

    public async Task<List<(Team Team, TeamRole Role)>> GetUserTeamsAsync(string userId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);
        var members = await db.TeamMembers.AsNoTracking()
            .Include(m => m.Team)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);
        return members.Select(m => (m.Team, m.Role)).ToList();
    }
}
