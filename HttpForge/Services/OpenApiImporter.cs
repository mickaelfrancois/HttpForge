using System.Text.Json;
using System.Text.Json.Nodes;
using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace HttpForge.Services;

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

    public async Task<ImportResult> ImportFileAsync(Stream content, string filename)
    {
        var warnings = new List<string>();

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
            return new ImportResult(filename, 0, 0, 0, warnings);
        }

        var doc = result.Document;
        if (doc is null)
        {
            var detail = result.Diagnostic?.Errors is { Count: > 0 } errors
                ? " " + string.Join("; ", errors.Select(e => e.Message))
                : "";
            warnings.Add($"Spec illisible ou non reconnue comme OpenAPI/Swagger.{detail}");
            return new ImportResult(filename, 0, 0, 0, warnings);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var (requests, folders, variables) = await ImportDocumentAsync(db, doc, filename, warnings);
        await db.SaveChangesAsync();
        return new ImportResult(filename, requests, folders, variables, warnings);
    }

    private static async Task<(int requests, int folders, int variables)> ImportDocumentAsync(
        AppDbContext db, OpenApiDocument doc, string filename, List<string> warnings)
    {
        var collection = new Collection { Name = FirstNonEmpty(doc.Info?.Title, filename) };
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
}
