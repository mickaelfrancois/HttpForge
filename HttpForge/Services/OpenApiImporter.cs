using System.Text.Json;
using System.Text.Json.Nodes;
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace HttpForge.Services;

/// <summary>
/// Outcome of an OpenAPI refresh: endpoints added / removed / completed (params filled in),
/// folders created for new tags, and security base variables added — plus any warnings.
/// </summary>
public record RefreshResult(int Added, int Removed, int Completed, int FoldersCreated, int VariablesAdded, List<string> Warnings);

/// <summary>
/// Imports an OpenAPI/Swagger document (2.0, 3.0 or 3.1; JSON or YAML) into a HttpForge
/// <see cref="Collection"/>, mirroring <see cref="InsomniaImporter"/>. The service only
/// consumes a <see cref="Stream"/> — fetching a remote URL stays in the UI layer, so the
/// import logic stays testable without any network access.
/// </summary>
public class OpenApiImporter(IDbContextFactory<AppDbContext> dbFactory)
{
    // Bounds the schema-example recursion. A cyclic schema (a $ref pointing back to an
    // ancestor) cannot loop forever: past this depth the generator emits null and stops.
    private const int MaxExampleDepth = 8;

    public async Task<ImportResult> ImportFileAsync(Stream content, string filename, string? sourceOpenApiUrl = null)
    {
        var warnings = new List<string>();

        var doc = await LoadDocumentAsync(content, warnings);
        if (doc is null)
            return new ImportResult(filename, 0, 0, 0, warnings);

        await using var db = await dbFactory.CreateDbContextAsync();
        var (requests, folders, variables) = await ImportDocumentAsync(db, doc, filename, sourceOpenApiUrl, warnings);
        await db.SaveChangesAsync();
        return new ImportResult(filename, requests, folders, variables, warnings);
    }

    /// <summary>
    /// Reads an OpenAPI/Swagger document from a stream. Returns null and appends a
    /// readable warning when the content is unreadable or not recognized as a spec.
    /// Shared by <see cref="ImportFileAsync"/> and <see cref="RefreshCollectionAsync"/>.
    /// </summary>
    private static async Task<OpenApiDocument?> LoadDocumentAsync(Stream content, List<string> warnings)
    {
        // JSON is built into the core reader; YAML needs the separate YamlReader package.
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        ReadResult result;
        try
        {
            result = await OpenApiModelFactory.LoadAsync(content, format: null, settings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Impossible de lire la spec OpenAPI : {ex.Message}");
            return null;
        }

        if (result.Document is null)
        {
            var detail = result.Diagnostic?.Errors is { Count: > 0 } errors
                ? " " + string.Join("; ", errors.Select(e => e.Message))
                : "";
            warnings.Add($"Spec illisible ou non reconnue comme OpenAPI/Swagger.{detail}");
        }
        return result.Document;
    }

    private static async Task<(int requests, int folders, int variables)> ImportDocumentAsync(
        AppDbContext db, OpenApiDocument doc, string filename, string? sourceOpenApiUrl, List<string> warnings)
    {
        var collection = new Collection
        {
            Name = FirstNonEmpty(doc.Info?.Title, filename),
            SourceOpenApiUrl = sourceOpenApiUrl
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        try
        {
            // Base variable set: baseUrl now, the auth variables added lazily as schemes
            // are mapped onto requests below.
            var baseUrl = doc.Servers?.FirstOrDefault()?.Url ?? "";
            var baseSet = new CollectionVariableSet
            {
                CollectionId = collection.Id,
                Name = "Base",
                IsBase = true,
                Entries = [new CollectionVariableEntry { Key = "baseUrl", Value = baseUrl }]
            };
            db.CollectionVariableSets.Add(baseSet);
            var varKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "baseUrl" };

            // Tags -> folders: one folder per used tag, persisted to obtain the Id before
            // requests reference it (same reason as InsomniaImporter.cs:192).
            var folderIdByTag = await CreateTagFoldersAsync(db, doc, collection.Id);

            int requestCount = 0;
            if (doc.Paths is not null)
            {
                foreach (var (path, pathItem) in doc.Paths)
                {
                    if (pathItem.Operations is null) continue;
                    foreach (var (method, operation) in pathItem.Operations)
                    {
                        var req = MapOperation(method, operation, path, collection.Id, baseSet, varKeys, doc, warnings);

                        var tag = operation.Tags?.FirstOrDefault()?.Name;
                        if (tag is not null && folderIdByTag.TryGetValue(tag, out var folderId))
                            req.FolderId = folderId;

                        db.Requests.Add(req);
                        requestCount++;
                    }
                }
            }

            return (requestCount, folderIdByTag.Count, baseSet.Entries.Count);
        }
        catch (Exception)
        {
            await db.CollectionFolders.Where(f => f.CollectionId == collection.Id).LoadAsync();
            db.Collections.Remove(collection);
            await db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Resynchronizes an existing collection against an OpenAPI spec, additively and without
    /// destroying user work. Endpoints tracked by <see cref="HttpRequestItem.SourceOperationKey"/>:
    /// present in the spec keep their body/name/values and only gain missing params; absent from
    /// the spec are removed. New spec operations are added under their first tag's folder. Manual
    /// or duplicated requests (null key) are never touched. Pre-feature collections (all keys null)
    /// are backfilled by Method + URL before reconciliation, so a first refresh never duplicates.
    /// </summary>
    public async Task<RefreshResult> RefreshCollectionAsync(Stream content, int collectionId)
    {
        var warnings = new List<string>();

        var doc = await LoadDocumentAsync(content, warnings);
        if (doc is null)
            return new RefreshResult(0, 0, 0, 0, 0, warnings);

        await using var db = await dbFactory.CreateDbContextAsync();

        var collection = await db.Collections
            .Include(c => c.Requests).ThenInclude(r => r.Headers)
            .Include(c => c.Requests).ThenInclude(r => r.QueryParams)
            .Include(c => c.Folders)
            .Include(c => c.VariableSets).ThenInclude(s => s.Entries)
            .FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection is null)
        {
            warnings.Add($"Collection introuvable (id {collectionId}).");
            return new RefreshResult(0, 0, 0, 0, 0, warnings);
        }

        // 1. Index the spec operations by their unique "{METHOD} {path}" key.
        var specByKey = new Dictionary<string, SpecOp>(StringComparer.Ordinal);
        if (doc.Paths is not null)
        {
            foreach (var (path, pathItem) in doc.Paths)
            {
                if (pathItem.Operations is null) continue;
                foreach (var (method, operation) in pathItem.Operations)
                    specByKey[OperationKey(method, path)] = new SpecOp(method, operation, path);
            }
        }

        // 2. Backfill pre-feature requests (null key) that match a spec op by Method + URL.
        //    trackedKeys doubles as the "first-match-only" guard: Add returns false when the key
        //    is already held by a real tracked request or an earlier backfill this pass, so a
        //    hand-made duplicate of the same operation stays null-keyed and therefore protected.
        var trackedKeys = new HashSet<string>(
            collection.Requests.Where(r => r.SourceOperationKey is not null).Select(r => r.SourceOperationKey!),
            StringComparer.Ordinal);
        foreach (var req in collection.Requests.Where(r => r.SourceOperationKey is null))
        {
            if (!req.Url.StartsWith("{{baseUrl}}", StringComparison.Ordinal)) continue;
            var candidate = $"{req.Method} {req.Url["{{baseUrl}}".Length..]}";
            if (specByKey.ContainsKey(candidate) && trackedKeys.Add(candidate))
                req.SourceOperationKey = candidate;
        }

        // Snapshot of the tracked requests after backfill: drives both the additive merge
        // (lookup by key) and the deletion pass. New endpoints added below are not in it.
        var trackedRequests = collection.Requests.Where(r => r.SourceOperationKey is not null).ToList();
        var reqByKey = trackedRequests
            .GroupBy(r => r.SourceOperationKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var (folderIdByTag, foldersCreated) = await EnsureTagFoldersAsync(db, doc, collection);

        // Base variable set: add missing security variables, never overwrite existing values.
        var baseSet = collection.VariableSets.FirstOrDefault(s => s.IsBase);
        if (baseSet is null)
        {
            baseSet = new CollectionVariableSet { CollectionId = collection.Id, Name = "Base", IsBase = true };
            db.CollectionVariableSets.Add(baseSet);
        }
        var baseEntriesBefore = baseSet.Entries.Count;
        var varKeys = new HashSet<string>(baseSet.Entries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);

        int added = 0, removed = 0, completed = 0;

        // 3. Reconcile each spec operation.
        foreach (var (key, specOp) in specByKey)
        {
            if (reqByKey.TryGetValue(key, out var existing))
            {
                // Additive merge: add only the params/headers missing from the request. Never
                // touch name, URL, body, scripts, folder, or existing values.
                bool changed = false;
                foreach (var parameter in specOp.Operation.Parameters ?? [])
                {
                    if (string.IsNullOrEmpty(parameter.Name)) continue;
                    switch (parameter.In)
                    {
                        case ParameterLocation.Query:
                            if (!existing.QueryParams.Any(q => string.Equals(q.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                existing.QueryParams.Add(new QueryParamItem { Key = parameter.Name, Value = "" });
                                changed = true;
                            }
                            break;
                        case ParameterLocation.Header:
                            if (!existing.Headers.Any(h => string.Equals(h.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                existing.Headers.Add(new HeaderItem { Key = parameter.Name, Value = "" });
                                changed = true;
                            }
                            break;
                    }
                }
                if (changed) completed++;
            }
            else
            {
                // New endpoint: full map (params + body + security), placed under its tag's folder.
                var req = MapOperation(specOp.Method, specOp.Operation, specOp.Path, collection.Id, baseSet, varKeys, doc, warnings);
                var tag = specOp.Operation.Tags?.FirstOrDefault()?.Name;
                if (tag is not null && folderIdByTag.TryGetValue(tag, out var folderId))
                    req.FolderId = folderId;
                db.Requests.Add(req);
                added++;
            }
        }

        // 4. Delete tracked endpoints that disappeared from the spec (never null-keyed ones).
        foreach (var req in trackedRequests.Where(r => !specByKey.ContainsKey(r.SourceOperationKey!)))
        {
            db.Requests.Remove(req);
            removed++;
        }

        // 5. Ensure the collection's base security variables for every scheme the spec references.
        //    Additive only: an existing value (baseUrl included) is never overwritten.
        EnsureSecurityVariables(doc, specByKey.Values.Select(o => o.Operation), baseSet, varKeys);
        var varsAdded = baseSet.Entries.Count - baseEntriesBefore;

        await db.SaveChangesAsync();
        return new RefreshResult(added, removed, completed, foldersCreated, varsAdded, warnings);
    }

    private readonly record struct SpecOp(HttpMethod Method, OpenApiOperation Operation, string Path);

    /// <summary>
    /// Ensures a top-level folder exists for each first-tag in the spec, creating only the
    /// missing ones (matched by Name, Ordinal — like <see cref="CreateTagFoldersAsync"/>).
    /// Never deletes a folder. Returns the tag → folder id map and the count created.
    /// </summary>
    private static async Task<(Dictionary<string, int> map, int created)> EnsureTagFoldersAsync(
        AppDbContext db, OpenApiDocument doc, Collection collection)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var folder in collection.Folders.Where(f => f.ParentFolderId is null))
            map[folder.Name] = folder.Id;

        var tagNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (doc.Paths is not null)
        {
            foreach (var (_, pathItem) in doc.Paths)
            {
                if (pathItem.Operations is null) continue;
                foreach (var (_, operation) in pathItem.Operations)
                {
                    var tag = operation.Tags?.FirstOrDefault()?.Name;
                    if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                        tagNames.Add(tag);
                }
            }
        }

        int created = 0;
        foreach (var name in tagNames)
        {
            if (map.ContainsKey(name)) continue;
            var folder = new CollectionFolder { CollectionId = collection.Id, Name = name };
            db.CollectionFolders.Add(folder);
            await db.SaveChangesAsync(); // needed to get folder.Id before requests reference it
            map[name] = folder.Id;
            created++;
        }
        return (map, created);
    }

    /// <summary>
    /// Adds the base variable (token / basicAuth / apiKey) for each security scheme the given
    /// operations reference. Mirrors the variable side of <see cref="ApplyScheme"/> without
    /// injecting auth headers into existing requests (C5): existing values are never touched.
    /// </summary>
    private static void EnsureSecurityVariables(
        OpenApiDocument doc, IEnumerable<OpenApiOperation> operations,
        CollectionVariableSet baseSet, HashSet<string> varKeys)
    {
        foreach (var operation in operations)
        {
            var requirements = operation.Security is { Count: > 0 } ? operation.Security : doc.Security;
            if (requirements is null) continue;
            foreach (var requirement in requirements)
                foreach (var scheme in requirement.Keys)
                {
                    if (SecurityVariableKey(scheme) is { } key)
                        EnsureVariable(baseSet, varKeys, key);
                }
        }
    }

    private static string? SecurityVariableKey(IOpenApiSecurityScheme scheme) => scheme.Type switch
    {
        SecuritySchemeType.Http when IsScheme(scheme, "bearer") => "token",
        SecuritySchemeType.Http when IsScheme(scheme, "basic") => "basicAuth",
        SecuritySchemeType.ApiKey when scheme.In is ParameterLocation.Header or ParameterLocation.Query => "apiKey",
        _ => null
    };

    private static async Task<Dictionary<string, int>> CreateTagFoldersAsync(
        AppDbContext db, OpenApiDocument doc, int collectionId)
    {
        // Collect each operation's first tag, in encounter order, de-duplicated.
        var tagNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (doc.Paths is not null)
        {
            foreach (var (_, pathItem) in doc.Paths)
            {
                if (pathItem.Operations is null) continue;
                foreach (var (_, operation) in pathItem.Operations)
                {
                    var tag = operation.Tags?.FirstOrDefault()?.Name;
                    if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                        tagNames.Add(tag);
                }
            }
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in tagNames)
        {
            var folder = new CollectionFolder { CollectionId = collectionId, Name = name };
            db.CollectionFolders.Add(folder);
            await db.SaveChangesAsync(); // needed to get folder.Id before requests reference it
            map[name] = folder.Id;
        }
        return map;
    }

    private static HttpRequestItem MapOperation(
        HttpMethod method, OpenApiOperation operation, string path, int collectionId,
        CollectionVariableSet baseSet, HashSet<string> varKeys, OpenApiDocument doc, List<string> warnings)
    {
        var req = new HttpRequestItem
        {
            CollectionId = collectionId,
            Name = FirstNonEmpty(operation.OperationId, operation.Summary, $"{method.Method} {path}"),
            Method = MapMethod(method),
            // The path keeps its {placeholders}; the user substitutes them at execution time.
            Url = "{{baseUrl}}" + path,
            // Identity used to reconcile this request on a later refresh from the same spec.
            SourceOperationKey = OperationKey(method, path),
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var parameter in operation.Parameters ?? [])
        {
            if (string.IsNullOrEmpty(parameter.Name)) continue;
            switch (parameter.In)
            {
                case ParameterLocation.Query:
                    req.QueryParams.Add(new QueryParamItem { Key = parameter.Name, Value = "" });
                    break;
                case ParameterLocation.Header:
                    req.Headers.Add(new HeaderItem { Key = parameter.Name, Value = "" });
                    break;
                    // Path parameters stay inline in the URL; Cookie parameters are ignored.
            }
        }

        ApplyRequestBody(req, operation, warnings);
        ApplySecurity(req, operation, doc, baseSet, varKeys, warnings);
        return req;
    }

    private static void ApplyRequestBody(HttpRequestItem req, OpenApiOperation operation, List<string> warnings)
    {
        var content = operation.RequestBody?.Content;
        if (content is null || content.Count == 0) return;

        // Pick a JSON media type (application/json, application/vnd.api+json, ...).
        var jsonEntry = content.FirstOrDefault(kv => kv.Key.Contains("json", StringComparison.OrdinalIgnoreCase));
        if (jsonEntry.Value is null)
        {
            var mime = content.Keys.FirstOrDefault() ?? "?";
            warnings.Add($"Requête '{req.Name}' : corps '{mime}' non-JSON, contenu non généré.");
            return;
        }

        var media = jsonEntry.Value;
        // Explicit example wins over schema synthesis.
        var example = media.Example?.DeepClone()
                      ?? media.Schema?.Example?.DeepClone()
                      ?? BuildJsonExample(media.Schema, 0);

        req.BodyKind = BodyKind.Json;
        req.BodyContent = example?.ToJsonString(JsonOptions) ?? "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Synthesizes a representative JSON example from an OpenAPI schema. References are
    /// dereferenced transparently by the model (2.x), <c>allOf</c> subschemas are merged,
    /// and recursion is bounded by <see cref="MaxExampleDepth"/> so cyclic schemas stop.
    /// </summary>
    private static JsonNode? BuildJsonExample(IOpenApiSchema? schema, int depth)
    {
        if (schema is null || depth > MaxExampleDepth) return null;

        // An explicit example on the schema always wins.
        if (schema.Example is not null) return schema.Example.DeepClone();

        // allOf: merge the object subschemas into a single object.
        if (schema.AllOf is { Count: > 0 } allOf)
        {
            var merged = new JsonObject();
            foreach (var sub in allOf)
            {
                if (BuildJsonExample(sub, depth + 1) is JsonObject obj)
                {
                    foreach (var property in obj.ToList())
                    {
                        obj.Remove(property.Key);
                        merged[property.Key] = property.Value;
                    }
                }
            }
            if (merged.Count > 0) return merged;
        }

        var type = schema.Type;

        // Object (declared, or implied by the presence of properties).
        if ((type?.HasFlag(JsonSchemaType.Object) ?? false) || schema.Properties is { Count: > 0 })
        {
            var obj = new JsonObject();
            if (schema.Properties is not null)
            {
                foreach (var (name, propertySchema) in schema.Properties)
                    obj[name] = BuildJsonExample(propertySchema, depth + 1);
            }
            return obj;
        }

        if (type?.HasFlag(JsonSchemaType.Array) ?? false)
        {
            var array = new JsonArray();
            if (BuildJsonExample(schema.Items, depth + 1) is { } item)
                array.Add(item);
            return array;
        }

        // Scalars. JsonSchemaType is a [Flags] enum (a 3.1 type can be a union such as
        // ["string","null"]); test with HasFlag and ignore the Null flag.
        if (type?.HasFlag(JsonSchemaType.Boolean) ?? false) return JsonValue.Create(false);
        if (type?.HasFlag(JsonSchemaType.Integer) ?? false) return JsonValue.Create(0);
        if (type?.HasFlag(JsonSchemaType.Number) ?? false) return JsonValue.Create(0.0);
        if (type?.HasFlag(JsonSchemaType.String) ?? false) return JsonValue.Create("string");

        return null;
    }

    private static void ApplySecurity(
        HttpRequestItem req, OpenApiOperation operation, OpenApiDocument doc,
        CollectionVariableSet baseSet, HashSet<string> varKeys, List<string> warnings)
    {
        // An operation's own security overrides the document-level default.
        var requirements = operation.Security is { Count: > 0 } ? operation.Security : doc.Security;
        if (requirements is null) return;

        foreach (var requirement in requirements)
            foreach (var scheme in requirement.Keys)
                ApplyScheme(req, scheme, baseSet, varKeys, warnings);
    }

    private static void ApplyScheme(
        HttpRequestItem req, IOpenApiSecurityScheme scheme,
        CollectionVariableSet baseSet, HashSet<string> varKeys, List<string> warnings)
    {
        switch (scheme.Type)
        {
            case SecuritySchemeType.Http when IsScheme(scheme, "bearer"):
                AddAuthHeader(req, "Authorization", "Bearer {{token}}");
                EnsureVariable(baseSet, varKeys, "token");
                break;
            case SecuritySchemeType.Http when IsScheme(scheme, "basic"):
                AddAuthHeader(req, "Authorization", "Basic {{basicAuth}}");
                EnsureVariable(baseSet, varKeys, "basicAuth");
                break;
            case SecuritySchemeType.ApiKey when scheme.In == ParameterLocation.Header && !string.IsNullOrEmpty(scheme.Name):
                AddAuthHeader(req, scheme.Name, "{{apiKey}}");
                EnsureVariable(baseSet, varKeys, "apiKey");
                break;
            case SecuritySchemeType.ApiKey when scheme.In == ParameterLocation.Query && !string.IsNullOrEmpty(scheme.Name):
                if (!req.QueryParams.Any(q => string.Equals(q.Key, scheme.Name, StringComparison.OrdinalIgnoreCase)))
                    req.QueryParams.Add(new QueryParamItem { Key = scheme.Name, Value = "{{apiKey}}" });
                EnsureVariable(baseSet, varKeys, "apiKey");
                break;
            default:
                var label = scheme.Type?.ToString() ?? "inconnu";
                var message = $"Schéma de sécurité '{label}' non géré, à configurer manuellement.";
                if (!warnings.Contains(message)) warnings.Add(message);
                break;
        }
    }

    private static bool IsScheme(IOpenApiSecurityScheme scheme, string name) =>
        string.Equals(scheme.Scheme, name, StringComparison.OrdinalIgnoreCase);

    private static void AddAuthHeader(HttpRequestItem req, string key, string value)
    {
        if (!req.Headers.Any(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase)))
            req.Headers.Add(new HeaderItem { Key = key, Value = value });
    }

    private static void EnsureVariable(CollectionVariableSet baseSet, HashSet<string> varKeys, string key)
    {
        if (varKeys.Add(key))
            baseSet.Entries.Add(new CollectionVariableEntry { Key = key, Value = "" });
    }

    private static HttpMethodKind MapMethod(HttpMethod method) => method.Method.ToUpperInvariant() switch
    {
        "POST" => HttpMethodKind.POST,
        "PUT" => HttpMethodKind.PUT,
        "PATCH" => HttpMethodKind.PATCH,
        "DELETE" => HttpMethodKind.DELETE,
        "HEAD" => HttpMethodKind.HEAD,
        "OPTIONS" => HttpMethodKind.OPTIONS,
        _ => HttpMethodKind.GET
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "Imported";

    // Single source of the operation identity. The method is upper-cased so the key posted
    // at import (from an HttpMethod) matches the backfill candidate built at refresh from a
    // HttpMethodKind enum (whose names are already upper-case).
    private static string OperationKey(HttpMethod method, string path) =>
        $"{method.Method.ToUpperInvariant()} {path}";
}
