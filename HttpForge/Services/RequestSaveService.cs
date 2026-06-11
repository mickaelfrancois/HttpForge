using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class RequestSaveService(
    IDbContextFactory<AppDbContext> dbFactory,
    RequestChangeNotifier notifier)
{
    public record SaveResult(bool IsConflict, DateTime? ConflictAt = null, DateTime? SavedAt = null);

    public static bool HasConflict(DateTime dbUpdatedAt, DateTime draftLoadedAt) =>
        dbUpdatedAt > draftLoadedAt;

    public async Task<SaveResult> SaveAsync(
        RequestDraft draft,
        string originId,
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

        // Conflict guard for the "same request open in two windows" case: if the DB
        // copy was updated after this draft was loaded, surface a conflict instead of
        // silently overwriting the other window's save.
        if (!forceOverwrite && HasConflict(dbItem.UpdatedAt, draft.LoadedAt))
            return new SaveResult(IsConflict: true, ConflictAt: dbItem.UpdatedAt);

        dbItem.Name = draft.Name;
        dbItem.Method = draft.Method;
        dbItem.Url = draft.Url;
        dbItem.BodyKind = draft.BodyKind;
        dbItem.BodyContent = draft.BodyContent;
        dbItem.PostScript = draft.PostScript;
        dbItem.PostScriptTrusted = draft.PostScriptTrusted;
        dbItem.IgnoreTlsErrors = draft.IgnoreTlsErrors;
        dbItem.UpdatedAt = DateTime.UtcNow;

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
        await notifier.NotifyAsync(draft.RequestId, originId);

        // Return the timestamp we just wrote so the caller can rebase its draft's
        // LoadedAt onto this version. Without that, a second consecutive save from the
        // same window would see dbItem.UpdatedAt > draft.LoadedAt and falsely conflict.
        return new SaveResult(IsConflict: false, SavedAt: dbItem.UpdatedAt);
    }
}
