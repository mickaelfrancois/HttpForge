using System.Text.RegularExpressions;
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HttpForge.Services;

public record ImportResult(string FileName, int RequestsCreated, int FoldersCreated, int VariablesCreated, List<string> Warnings);

public class InsomniaImporter(IDbContextFactory<AppDbContext> dbFactory)
{
    // Matches {{ _.VAR }}, {{ _['VAR-NAME'] }}, {{ _.vault.KEY }}
    private static readonly Regex VarPattern = new(
        """\{\{\s*_(?:\[['"]([^'"]+)['"]\]|\.(?:vault\.)?([A-Za-z0-9_\-]+))\s*\}\}""",
        RegexOptions.Compiled);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static string TransformVars(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return VarPattern.Replace(input, m =>
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return $"{{{{ {name} }}}}";
        });
    }

    public async Task<ImportResult> ImportFileAsync(Stream content, string filename)
    {
        var warnings = new List<string>();

        using var reader = new StreamReader(content);
        var yaml = await reader.ReadToEndAsync();

        var file = Deserializer.Deserialize<InsomniaFile>(yaml);

        if (file.Meta?.Id == "wrk_scratchpad")
            return new ImportResult(filename, 0, 0, 0, warnings);

        await using var db = await dbFactory.CreateDbContextAsync();

        if (file.Type == "collection.insomnia.rest/5.0")
        {
            var (req, folders, vars) = await ImportCollectionAsync(db, file, warnings);
            await db.SaveChangesAsync();
            return new ImportResult(filename, req, folders, vars, warnings);
        }

        if (file.Type == "environment.insomnia.rest/5.0")
        {
            var vars = await ImportGlobalEnvAsync(db, file, warnings);
            await db.SaveChangesAsync();
            return new ImportResult(filename, 0, 0, vars, warnings);
        }

        warnings.Add($"Unrecognized workspace type: {file.Type}");
        return new ImportResult(filename, 0, 0, 0, warnings);
    }

    private static async Task<(int requests, int folders, int variables)> ImportCollectionAsync(
        AppDbContext db, InsomniaFile file, List<string> warnings)
    {
        var collection = new Collection { Name = file.Name ?? "Imported" };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        try
        {
            var (requestCount, folderCount) = await ImportNodesAsync(
                db, file.Collection ?? [], collection.Id, null, warnings);

            int varCount = 0;
            if (file.Environments is not null)
            {
                var baseSet = new CollectionVariableSet
                {
                    CollectionId = collection.Id,
                    Name = "Base",
                    IsBase = true,
                    Entries = MapVarEntries(file.Environments.Data, warnings)
                };
                varCount += baseSet.Entries.Count;
                db.CollectionVariableSets.Add(baseSet);

                foreach (var sub in file.Environments.SubEnvironments ?? [])
                {
                    var subSet = new CollectionVariableSet
                    {
                        CollectionId = collection.Id,
                        Name = sub.Name ?? "Sub",
                        IsBase = false,
                        Entries = MapVarEntries(sub.Data, warnings)
                    };
                    varCount += subSet.Entries.Count;
                    db.CollectionVariableSets.Add(subSet);
                }
            }

            return (requestCount, folderCount, varCount);
        }
        catch (Exception)
        {
            await db.CollectionFolders
                .Where(f => f.CollectionId == collection.Id)
                .LoadAsync();
            db.Collections.Remove(collection);
            await db.SaveChangesAsync();
            throw;
        }
    }

    private static async Task<int> ImportGlobalEnvAsync(
        AppDbContext db, InsomniaFile file, List<string> warnings)
    {
        if (file.Environments is null) return 0;
        int count = 0;

        var globalBase = await db.Environments
            .Include(e => e.Variables)
            .FirstOrDefaultAsync(e => e.IsBase);

        if (globalBase is null)
        {
            globalBase = new AppEnvironment { Name = "Global", IsBase = true };
            db.Environments.Add(globalBase);
            await db.SaveChangesAsync();
        }

        foreach (var (key, value) in NonVaultEntries(file.Environments.Data, warnings))
        {
            if (globalBase.Variables.Any(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Global variable '{key}' already exists, skipped");
                continue;
            }
            globalBase.Variables.Add(new EnvironmentVariable { Key = key, Value = value });
            count++;
        }

        foreach (var sub in file.Environments.SubEnvironments ?? [])
        {
            var env = new AppEnvironment { Name = sub.Name ?? "Imported", IsBase = false };
            db.Environments.Add(env);
            foreach (var (key, value) in NonVaultEntries(sub.Data, warnings))
            {
                env.Variables.Add(new EnvironmentVariable { Key = key, Value = value });
                count++;
            }
        }

        return count;
    }

    private static async Task<(int requests, int folders)> ImportNodesAsync(
        AppDbContext db, List<InsomniaNode> nodes, int collectionId, int? parentFolderId, List<string> warnings)
    {
        int requestCount = 0, folderCount = 0;
        foreach (var node in nodes)
        {
            if (node.Url is not null)
            {
                var req = MapRequest(node, collectionId, warnings);
                req.FolderId = parentFolderId;
                db.Requests.Add(req);
                requestCount++;
            }
            else
            {
                var folder = new CollectionFolder
                {
                    CollectionId = collectionId,
                    ParentFolderId = parentFolderId,
                    Name = node.Name ?? "Folder"
                };
                db.CollectionFolders.Add(folder);
                await db.SaveChangesAsync();  // needed to get folder.Id before recursing
                folderCount++;
                var (childReqs, childFolders) = await ImportNodesAsync(
                    db, node.Children ?? [], collectionId, folder.Id, warnings);
                requestCount += childReqs;
                folderCount += childFolders;
            }
        }
        return (requestCount, folderCount);
    }

    private static HttpRequestItem MapRequest(InsomniaNode node, int collectionId, List<string> warnings)
    {
        var req = new HttpRequestItem
        {
            CollectionId = collectionId,
            Name = node.Name ?? "Untitled",
            Url = TransformVars(node.Url),
            Method = ParseMethod(node.Method),
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var h in node.Headers ?? [])
        {
            if (h.Disabled == true) continue;
            if (string.Equals(h.Name, "User-Agent", StringComparison.OrdinalIgnoreCase)
                && h.Value?.StartsWith("insomnia/", StringComparison.OrdinalIgnoreCase) == true)
                continue;
            req.Headers.Add(new HeaderItem { Key = h.Name ?? "", Value = TransformVars(h.Value) });
        }

        if (node.Authentication?.Type == "bearer" && !string.IsNullOrEmpty(node.Authentication.Token))
        {
            if (!req.Headers.Any(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase) && h.Enabled))
                req.Headers.Add(new HeaderItem { Key = "Authorization", Value = $"Bearer {TransformVars(node.Authentication.Token)}" });
        }

        var mimeType = node.Body?.MimeType;
        if (mimeType == "application/json")
        {
            req.BodyKind = BodyKind.Json;
            req.BodyContent = TransformVars(node.Body?.Text);
        }
        else if (mimeType is "multipart/form-data" or "application/x-www-form-urlencoded")
        {
            req.BodyKind = BodyKind.FormUrlEncoded;
            foreach (var p in node.Body?.Params ?? [])
            {
                if (p.Disabled == true) continue;
                req.FormFields.Add(new FormFieldItem { Key = p.Name ?? "", Value = TransformVars(p.Value) });
            }
        }
        else if (!string.IsNullOrEmpty(node.Body?.Text))
        {
            req.BodyKind = BodyKind.Raw;
            req.BodyContent = TransformVars(node.Body!.Text);
            if (mimeType is not null)
                warnings.Add($"Request '{node.Name}': unrecognized body type '{mimeType}', imported as Raw");
        }

        if (!string.IsNullOrWhiteSpace(node.AfterResponseScript))
            req.PostScript = node.AfterResponseScript;

        return req;
    }

    private static HttpMethodKind ParseMethod(string? method) => method?.ToUpperInvariant() switch
    {
        "POST" => HttpMethodKind.POST,
        "PUT" => HttpMethodKind.PUT,
        "PATCH" => HttpMethodKind.PATCH,
        "DELETE" => HttpMethodKind.DELETE,
        "HEAD" => HttpMethodKind.HEAD,
        "OPTIONS" => HttpMethodKind.OPTIONS,
        _ => HttpMethodKind.GET
    };

    private static List<CollectionVariableEntry> MapVarEntries(
        Dictionary<string, object?>? data, List<string> warnings) =>
        NonVaultEntries(data, warnings)
            .Select(kv => new CollectionVariableEntry { Key = kv.key, Value = kv.value })
            .ToList();

    private static IEnumerable<(string key, string value)> NonVaultEntries(
        Dictionary<string, object?>? data, List<string> warnings)
    {
        if (data is null) yield break;
        foreach (var (key, val) in data)
        {
            if (key == "__insomnia_vault")
            {
                if (!warnings.Contains("Vault entries skipped (encrypted, unrecoverable)"))
                    warnings.Add("Vault entries skipped (encrypted, unrecoverable)");
                continue;
            }
            yield return (key, val?.ToString() ?? "");
        }
    }
}

// ── YAML POCOs ──────────────────────────────────────────────────────────────

public class InsomniaFile
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public InsomniaFileMeta? Meta { get; set; }
    public List<InsomniaNode>? Collection { get; set; }
    public InsomniaEnvironment? Environments { get; set; }
}

public class InsomniaFileMeta
{
    public string? Id { get; set; }
}

public class InsomniaNode
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; }
    public List<InsomniaHeader>? Headers { get; set; }
    public InsomniaBody? Body { get; set; }
    public InsomniaAuth? Authentication { get; set; }
    public List<InsomniaNode>? Children { get; set; }
    public string? AfterResponseScript { get; set; }
}

public class InsomniaHeader
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Disabled { get; set; }
}

public class InsomniaBody
{
    public string? MimeType { get; set; }
    public string? Text { get; set; }
    public List<InsomniaParam>? Params { get; set; }
}

public class InsomniaParam
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Disabled { get; set; }
}

public class InsomniaAuth
{
    public string? Type { get; set; }
    public string? Token { get; set; }
}

public class InsomniaEnvironment
{
    public string? Name { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    public List<InsomniaSubEnvironment>? SubEnvironments { get; set; }
}

public class InsomniaSubEnvironment
{
    public string? Name { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}
