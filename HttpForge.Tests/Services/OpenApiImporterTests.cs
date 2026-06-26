// HttpForge.Tests/Services/OpenApiImporterTests.cs
using System.Text;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HttpForge.Tests.Services;

public class OpenApiImporterTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;
    private readonly OpenApiImporter _sut;

    public OpenApiImporterTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        _opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        using var ctx = new AppDbContext(_opts);
        ctx.Database.EnsureCreated();

        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(_opts));

        _sut = new OpenApiImporter(factory.Object);
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext Ctx() => new(_opts);

    private static Stream S(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    // ── Formats: 3.0 JSON / YAML / Swagger 2.0 ───────────────────────────────

    [Fact]
    public async Task ImportFileAsync_OpenApi30Json_CreatesCollectionWithRequest()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "My API", "version": "1.0" },
              "servers": [ { "url": "https://api.example.com" } ],
              "paths": { "/users": { "get": { "operationId": "getUsers" } } }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "api.json");

        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var collection = await ctx.Collections.SingleAsync();
        Assert.Equal("My API", collection.Name);
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(HttpMethodKind.GET, req.Method);
        Assert.Equal("{{baseUrl}}/users", req.Url);
    }

    [Fact]
    public async Task ImportFileAsync_OpenApi30Yaml_ProducesSameResult()
    {
        const string yaml = """
            openapi: 3.0.0
            info:
              title: My API
              version: '1.0'
            servers:
              - url: https://api.example.com
            paths:
              /users:
                get:
                  operationId: getUsers
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "api.yaml");

        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(HttpMethodKind.GET, req.Method);
        Assert.Equal("{{baseUrl}}/users", req.Url);
    }

    [Fact]
    public async Task ImportFileAsync_Swagger20_MapsHostBasePathToBaseUrl()
    {
        const string json = """
            {
              "swagger": "2.0",
              "info": { "title": "Legacy API", "version": "1.0" },
              "host": "api.legacy.com",
              "basePath": "/v1",
              "schemes": ["https"],
              "paths": { "/ping": { "get": {} } }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "legacy.json");

        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var baseUrl = await ctx.Set<CollectionVariableEntry>().SingleAsync(e => e.Key == "baseUrl");
        Assert.Contains("api.legacy.com", baseUrl.Value);
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal("{{baseUrl}}/ping", req.Url);
    }

    // ── baseUrl & request naming ─────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_Servers_StoredAsBaseUrlVariable()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "servers": [ { "url": "https://api.example.com/base" } ],
              "paths": { "/x": { "get": {} } }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var baseSet = await ctx.Set<CollectionVariableSet>().Include(s => s.Entries).SingleAsync(s => s.IsBase);
        Assert.Contains(baseSet.Entries, e => e.Key == "baseUrl" && e.Value == "https://api.example.com/base");
    }

    [Fact]
    public async Task ImportFileAsync_NoOperationId_FallsBackToSummaryThenMethodPath()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/a": { "get": { "summary": "Fetch A" } },
                "/b": { "post": {} }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var names = await ctx.Requests.Select(r => r.Name).ToListAsync();
        Assert.Contains("Fetch A", names);
        Assert.Contains("POST /b", names);
    }

    // ── Parameters & tags ────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_QueryAndHeaderParams_MappedToItems()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/search": {
                  "get": {
                    "parameters": [
                      { "name": "q", "in": "query", "schema": { "type": "string" } },
                      { "name": "X-Trace", "in": "header", "schema": { "type": "string" } },
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests
            .Include(r => r.QueryParams)
            .Include(r => r.Headers)
            .SingleAsync();
        Assert.Contains(req.QueryParams, p => p.Key == "q");
        Assert.Contains(req.Headers, h => h.Key == "X-Trace");
    }

    [Fact]
    public async Task ImportFileAsync_Tags_CreatedAsFolders()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/users": { "get": { "tags": ["Users"] } },
                "/orders": { "get": { "tags": ["Orders"] } },
                "/health": { "get": {} }
              }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "api.json");

        Assert.Equal(2, result.FoldersCreated);
        await using var ctx = Ctx();
        var usersFolder = await ctx.CollectionFolders.SingleAsync(f => f.Name == "Users");
        var taggedReq = await ctx.Requests.SingleAsync(r => r.Url == "{{baseUrl}}/users");
        Assert.Equal(usersFolder.Id, taggedReq.FolderId);
        var untaggedReq = await ctx.Requests.SingleAsync(r => r.Url == "{{baseUrl}}/health");
        Assert.Null(untaggedReq.FolderId);
    }

    // ── Body generation from schema ──────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_SimpleSchema_GeneratesJsonBody()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "properties": {
                              "id": { "type": "integer" },
                              "name": { "type": "string" },
                              "active": { "type": "boolean" }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(BodyKind.Json, req.BodyKind);
        Assert.Contains("\"id\"", req.BodyContent);
        Assert.Contains("\"name\"", req.BodyContent);
        Assert.Contains("\"active\"", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_SchemaRef_DereferencedInBody()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": { "$ref": "#/components/schemas/Item" }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Item": {
                    "type": "object",
                    "properties": { "sku": { "type": "string" } }
                  }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(BodyKind.Json, req.BodyKind);
        Assert.Contains("\"sku\"", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_AllOf_MergesProperties()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "allOf": [
                              { "type": "object", "properties": { "a": { "type": "string" } } },
                              { "type": "object", "properties": { "b": { "type": "integer" } } }
                            ]
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Contains("\"a\"", req.BodyContent);
        Assert.Contains("\"b\"", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_CyclicSchema_DoesNotLoopOrThrow()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/nodes": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": { "$ref": "#/components/schemas/Node" }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Node": {
                    "type": "object",
                    "properties": {
                      "value": { "type": "string" },
                      "next": { "$ref": "#/components/schemas/Node" }
                    }
                  }
                }
              }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "api.json");

        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(BodyKind.Json, req.BodyKind);
        Assert.Contains("\"value\"", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_ExplicitExample_WinsOverSchema()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": { "type": "object", "properties": { "x": { "type": "string" } } },
                          "example": { "x": "hello-world" }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Contains("hello-world", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_NonJsonBody_WarnsAndSkipsContent()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "paths": {
                "/upload": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/xml": { "schema": { "type": "string" } }
                      }
                    }
                  }
                }
              }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "api.json");

        Assert.Contains(result.Warnings, w => w.Contains("non-JSON"));
        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(BodyKind.None, req.BodyKind);
    }

    // ── Security schemes ─────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_BearerScheme_AddsAuthorizationHeaderAndVariable()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "security": [ { "bearerAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "bearerAuth": { "type": "http", "scheme": "bearer" }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.Headers).SingleAsync();
        Assert.Contains(req.Headers, h => h.Key == "Authorization" && h.Value == "Bearer {{token}}");
        var baseSet = await ctx.Set<CollectionVariableSet>().Include(s => s.Entries).SingleAsync(s => s.IsBase);
        Assert.Contains(baseSet.Entries, e => e.Key == "token");
    }

    [Fact]
    public async Task ImportFileAsync_BasicScheme_AddsAuthorizationHeader()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "security": [ { "basicAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "basicAuth": { "type": "http", "scheme": "basic" }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.Headers).SingleAsync();
        Assert.Contains(req.Headers, h => h.Key == "Authorization" && h.Value == "Basic {{basicAuth}}");
    }

    [Fact]
    public async Task ImportFileAsync_ApiKeyHeaderScheme_AddsNamedHeader()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "security": [ { "apiKeyAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "apiKeyAuth": { "type": "apiKey", "in": "header", "name": "X-API-Key" }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.Headers).SingleAsync();
        Assert.Contains(req.Headers, h => h.Key == "X-API-Key" && h.Value == "{{apiKey}}");
    }

    [Fact]
    public async Task ImportFileAsync_ApiKeyQueryScheme_AddsNamedQueryParam()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "security": [ { "apiKeyAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "apiKeyAuth": { "type": "apiKey", "in": "query", "name": "api_key" }
                }
              }
            }
            """;

        await _sut.ImportFileAsync(S(json), "api.json");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.QueryParams).SingleAsync();
        Assert.Contains(req.QueryParams, p => p.Key == "api_key" && p.Value == "{{apiKey}}");
    }

    [Fact]
    public async Task ImportFileAsync_OAuth2Scheme_ProducesWarning()
    {
        const string json = """
            {
              "openapi": "3.0.0",
              "info": { "title": "A", "version": "1" },
              "security": [ { "oauth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "oauth": {
                    "type": "oauth2",
                    "flows": { "implicit": { "authorizationUrl": "https://example.com/auth", "scopes": {} } }
                  }
                }
              }
            }
            """;

        var result = await _sut.ImportFileAsync(S(json), "api.json");

        Assert.Contains(result.Warnings, w => w.Contains("sécurité") && w.Contains("non géré"));
    }

    // ── Error paths ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_UnreadableSpec_WarnsAndCreatesNothing()
    {
        const string garbage = "this is definitely not an OpenAPI document {{{ ";

        var result = await _sut.ImportFileAsync(S(garbage), "garbage.txt");

        Assert.Equal(0, result.RequestsCreated);
        Assert.NotEmpty(result.Warnings);
        await using var ctx = Ctx();
        Assert.Empty(await ctx.Collections.ToListAsync());
    }
}
