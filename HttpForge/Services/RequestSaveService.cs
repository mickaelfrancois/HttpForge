using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class RequestSaveService(
    IDbContextFactory<AppDbContext> dbFactory,
    RequestChangeNotifier notifier,
    UserManager<AppUser> userManager)
{
    public record SaveResult(bool IsConflict, string? ConflictByUserName = null, DateTime? ConflictAt = null);

    public static bool HasConflict(DateTime dbUpdatedAt, DateTime draftLoadedAt) =>
        dbUpdatedAt > draftLoadedAt;

    public async Task<SaveResult> SaveAsync(
        RequestDraft draft,
        string currentUserId,
        string currentUserName,
        bool forceOverwrite)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var dbItem = await db.Requests
            .Include(r => r.Headers)
            .Include(r => r.QueryParams)
            .Include(r => r.FormFields)
            .Include(r => r.Variables)
            .FirstOrDefaultAsync(r => r.Id == draft.RequestId);

        if (dbItem is null)
            return new SaveResult(IsConflict: false);

        if (!forceOverwrite && HasConflict(dbItem.UpdatedAt, draft.LoadedAt))
        {
            string conflictName = "Unknown";
            if (dbItem.UpdatedByUserId is not null)
            {
                var user = await userManager.FindByIdAsync(dbItem.UpdatedByUserId);
                conflictName = user?.Email ?? "Unknown";
            }
            return new SaveResult(IsConflict: true, ConflictByUserName: conflictName, ConflictAt: dbItem.UpdatedAt);
        }

        dbItem.Name = draft.Name;
        dbItem.Method = draft.Method;
        dbItem.Url = draft.Url;
        dbItem.BodyKind = draft.BodyKind;
        dbItem.BodyContent = draft.BodyContent;
        dbItem.PostScript = draft.PostScript;
        dbItem.UpdatedAt = DateTime.UtcNow;
        dbItem.UpdatedByUserId = currentUserId;

        db.RemoveRange(dbItem.Headers);
        db.RemoveRange(dbItem.QueryParams);
        db.RemoveRange(dbItem.FormFields);
        db.RemoveRange(dbItem.Variables);

        foreach (var h in draft.Headers)
            db.Add(new HeaderItem { HttpRequestItemId = dbItem.Id, Key = h.Key, Value = h.Value, Enabled = h.Enabled });
        foreach (var p in draft.QueryParams)
            db.Add(new QueryParamItem { HttpRequestItemId = dbItem.Id, Key = p.Key, Value = p.Value, Enabled = p.Enabled });
        foreach (var f in draft.FormFields)
            db.Add(new FormFieldItem { HttpRequestItemId = dbItem.Id, Key = f.Key, Value = f.Value, Enabled = f.Enabled });
        foreach (var v in draft.Variables)
            db.Add(new RequestVariable { HttpRequestItemId = dbItem.Id, Key = v.Key, Value = v.Value, IsSecret = v.IsSecret });

        await db.SaveChangesAsync();
        await notifier.NotifyAsync(draft.RequestId, currentUserId, currentUserName);

        return new SaveResult(IsConflict: false);
    }
}
