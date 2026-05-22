using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class PermissionService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<TeamRole?> GetRoleForCollectionAsync(string userId, int collectionId, CancellationToken ct = default)
    {
        using var db = await dbFactory.CreateDbContextAsync(ct);

        var isSuperAdmin = await db.UserRoles
            .AsNoTracking()
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == userId && x.Name == "SuperAdmin", ct);
        if (isSuperAdmin) return TeamRole.Contributor;

        var collection = await db.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection?.TeamId is null) return null;

        var member = await db.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == collection.TeamId && m.UserId == userId, ct);
        return member?.Role;
    }

    public async Task<bool> IsReadOnlyAsync(string userId, int collectionId, CancellationToken ct = default)
    {
        var role = await GetRoleForCollectionAsync(userId, collectionId, ct);
        return role is null or TeamRole.Guest;
    }
}
