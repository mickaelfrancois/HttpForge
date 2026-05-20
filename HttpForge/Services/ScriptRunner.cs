using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HttpForge.Services;

public record ScriptMutations(
    Dictionary<string, string> Request,
    Dictionary<string, string> Collection,
    Dictionary<string, string> Global);

public record ScriptResult(
    ScriptMutations Mutations,
    List<string> Logs,
    string? Error);

public class ScriptRunner(IJSRuntime js, IDbContextFactory<AppDbContext> dbFactory, AppState state)
{
    public async Task<ScriptResult?> RunPostScriptAsync(
        HttpRequestItem request,
        ExecutionResult response,
        int? activeCollectionSetId,
        int? activeGlobalEnvId,
        IReadOnlyList<ResolvedVariableEntry> resolvedVars)
    {
        if (string.IsNullOrWhiteSpace(request.PostScript))
            return null;

        var responseDto = new
        {
            status = response.StatusCode,
            statusText = response.ReasonPhrase ?? string.Empty,
            headers = response.Headers,
            body = response.Body ?? string.Empty
        };

        var varsDto = new
        {
            request = resolvedVars
                .Where(v => v.Source == VariableSource.Request)
                .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            collection = resolvedVars
                .Where(v => v.Source == VariableSource.Collection)
                .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            global = resolvedVars
                .Where(v => v.Source == VariableSource.Global)
                .ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
        };

        try
        {
            var result = await js.InvokeAsync<ScriptResult>(
                "forge.scripts.run", request.PostScript, responseDto, varsDto);

            if (result.Mutations is not null)
                await ApplyMutationsAsync(request.Id, activeCollectionSetId, activeGlobalEnvId, result.Mutations);

            return result;
        }
        catch (Exception ex)
        {
            return new ScriptResult(
                new ScriptMutations([], [], []),
                [],
                ex.Message);
        }
    }

    private async Task ApplyMutationsAsync(
        int requestId,
        int? collectionSetId,
        int? globalEnvId,
        ScriptMutations mutations)
    {
        if (mutations.Request.Count == 0 && mutations.Collection.Count == 0 && mutations.Global.Count == 0)
            return;

        await using var db = await dbFactory.CreateDbContextAsync();

        foreach (var (key, value) in mutations.Request)
        {
            var existing = await db.RequestVariables
                .FirstOrDefaultAsync(v => v.HttpRequestItemId == requestId && v.Key == key);
            if (existing is not null)
                existing.Value = value;
            else
                db.RequestVariables.Add(new RequestVariable
                    { HttpRequestItemId = requestId, Key = key, Value = value });
        }

        if (collectionSetId.HasValue)
        {
            foreach (var (key, value) in mutations.Collection)
            {
                var existing = await db.CollectionVariableEntries
                    .FirstOrDefaultAsync(e => e.CollectionVariableSetId == collectionSetId.Value && e.Key == key);
                if (existing is not null)
                    existing.Value = value;
                else
                    db.CollectionVariableEntries.Add(new CollectionVariableEntry
                        { CollectionVariableSetId = collectionSetId.Value, Key = key, Value = value });
            }
        }

        if (globalEnvId.HasValue)
        {
            foreach (var (key, value) in mutations.Global)
            {
                var existing = await db.EnvironmentVariables
                    .FirstOrDefaultAsync(v => v.AppEnvironmentId == globalEnvId.Value && v.Key == key);
                if (existing is not null)
                    existing.Value = value;
                else
                    db.EnvironmentVariables.Add(new EnvironmentVariable
                        { AppEnvironmentId = globalEnvId.Value, Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync();
        state.NotifyChanged();
    }
}
