// HttpForge.Tests/Services/OpenApiRefreshTests.cs
using System.Text;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HttpForge.Tests.Services;

public class OpenApiRefreshTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;
    private readonly OpenApiImporter _sut;

    public OpenApiRefreshTests()
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

    private async Task<int> ImportAsync(string spec)
    {
        await _sut.ImportFileAsync(S(spec), "api.json");
        await using var ctx = Ctx();
        return await ctx.Collections.Select(c => c.Id).FirstAsync();
    }

    // ── Add / remove / merge ─────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_NewSpecOperation_IsAdded()
    {
        const string v1 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        const string v2 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} }, "/orders": { "get": {} } } }
            """;
        var id = await ImportAsync(v1);

        var result = await _sut.RefreshCollectionAsync(S(v2), id);

        Assert.Equal(1, result.Added);
        await using var ctx = Ctx();
        var urls = await ctx.Requests.Where(r => r.CollectionId == id).Select(r => r.Url).ToListAsync();
        Assert.Contains("{{baseUrl}}/orders", urls);
        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public async Task Refresh_TrackedOperationGoneFromSpec_IsRemoved()
    {
        const string v1 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} }, "/orders": { "get": {} } } }
            """;
        const string v2 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        var id = await ImportAsync(v1);

        var result = await _sut.RefreshCollectionAsync(S(v2), id);

        Assert.Equal(1, result.Removed);
        await using var ctx = Ctx();
        var urls = await ctx.Requests.Where(r => r.CollectionId == id).Select(r => r.Url).ToListAsync();
        Assert.Equal(["{{baseUrl}}/users"], urls);
    }

    [Fact]
    public async Task Refresh_MaintainedOperation_GainsMissingParamsAdditively()
    {
        const string v1 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/search": { "get": { "parameters": [
                { "name": "q", "in": "query", "schema": { "type": "string" } }
              ] } } } }
            """;
        const string v2 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/search": { "get": { "parameters": [
                { "name": "q", "in": "query", "schema": { "type": "string" } },
                { "name": "X-Trace", "in": "header", "schema": { "type": "string" } }
              ] } } } }
            """;
        var id = await ImportAsync(v1);

        // User fills in the existing param value; the additive merge must not overwrite it.
        await using (var ctx = Ctx())
        {
            var seed = await ctx.Requests.Include(r => r.QueryParams).SingleAsync(r => r.CollectionId == id);
            seed.QueryParams.Single(p => p.Key == "q").Value = "user-value";
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(v2), id);

        Assert.Equal(1, result.Completed);
        await using var check = Ctx();
        var req = await check.Requests
            .Include(r => r.QueryParams)
            .Include(r => r.Headers)
            .SingleAsync(r => r.CollectionId == id);
        // Existing value preserved, no duplicate "q", new header added empty.
        Assert.Equal("user-value", req.QueryParams.Single(p => p.Key == "q").Value);
        Assert.Contains(req.Headers, h => h.Key == "X-Trace" && h.Value == "");
    }

    [Fact]
    public async Task Refresh_MaintainedOperation_LeavesBodyNameUrlUntouched()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/items": { "post": { "requestBody": { "content": {
                "application/json": { "schema": { "type": "object", "properties": { "id": { "type": "integer" } } } }
              } } } } } }
            """;
        var id = await ImportAsync(spec);

        // Simulate user edits on the maintained request.
        await using (var ctx = Ctx())
        {
            var req = await ctx.Requests.SingleAsync(r => r.CollectionId == id);
            req.Name = "My renamed request";
            req.BodyContent = "{ \"id\": 42, \"custom\": true }";
            await ctx.SaveChangesAsync();
        }

        await _sut.RefreshCollectionAsync(S(spec), id);

        await using var check = Ctx();
        var after = await check.Requests.SingleAsync(r => r.CollectionId == id);
        Assert.Equal("My renamed request", after.Name);
        Assert.Equal("{ \"id\": 42, \"custom\": true }", after.BodyContent);
        Assert.Equal("{{baseUrl}}/items", after.Url);
        // Placement is untagged here: the refresh must not reassign a folder.
        Assert.Null(after.FolderId);
    }

    // ── Protection of manual / duplicated requests ───────────────────────────

    [Fact]
    public async Task Refresh_ManualRequest_NullKey_IsNeverTouchedOrRemoved()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        var id = await ImportAsync(spec);

        // A hand-made request with no source key and a URL absent from the spec.
        await using (var ctx = Ctx())
        {
            ctx.Requests.Add(new HttpRequestItem
            {
                CollectionId = id,
                Name = "Manual",
                Method = HttpMethodKind.POST,
                Url = "https://elsewhere.example/manual",
                SourceOperationKey = null
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(spec), id);

        Assert.Equal(0, result.Removed);
        await using var check = Ctx();
        Assert.Contains(await check.Requests.Where(r => r.CollectionId == id).ToListAsync(),
            r => r.Name == "Manual" && r.SourceOperationKey == null);
    }

    [Fact]
    public async Task Refresh_DuplicatedTrackedRequest_NullKey_StaysProtected()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        var id = await ImportAsync(spec);

        // Duplicate() reconstructs field-by-field: the copy has the same baseUrl+path URL
        // but no source key. It must not be claimed by backfill (a real tracked req holds
        // the key) nor removed.
        await using (var ctx = Ctx())
        {
            ctx.Requests.Add(new HttpRequestItem
            {
                CollectionId = id,
                Name = "users copy",
                Method = HttpMethodKind.GET,
                Url = "{{baseUrl}}/users",
                SourceOperationKey = null
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(spec), id);

        Assert.Equal(0, result.Removed);
        await using var check = Ctx();
        var reqs = await check.Requests.Where(r => r.CollectionId == id).ToListAsync();
        Assert.Equal(2, reqs.Count);
        Assert.Contains(reqs, r => r.Name == "users copy" && r.SourceOperationKey == null);
    }

    // ── Backfill of pre-feature collections ──────────────────────────────────

    [Fact]
    public async Task Refresh_PreFeatureCollection_BackfilledByMethodUrl_NoDuplicates()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} }, "/orders": { "post": {} } } }
            """;
        var id = await ImportAsync(spec);

        // Simulate a collection imported before the feature: all keys null.
        await using (var ctx = Ctx())
        {
            foreach (var req in await ctx.Requests.Where(r => r.CollectionId == id).ToListAsync())
                req.SourceOperationKey = null;
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(spec), id);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        await using var check = Ctx();
        var reqs = await check.Requests.Where(r => r.CollectionId == id).ToListAsync();
        Assert.Equal(2, reqs.Count);
        Assert.All(reqs, r => Assert.False(string.IsNullOrEmpty(r.SourceOperationKey)));
    }

    [Fact]
    public async Task Refresh_PreFeatureDuplicateSameKey_BackfillsFirstOnly()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        var id = await ImportAsync(spec);

        await using (var ctx = Ctx())
        {
            // Existing tracked /users → null (pre-feature) and add a second identical null-keyed one.
            foreach (var req in await ctx.Requests.Where(r => r.CollectionId == id).ToListAsync())
                req.SourceOperationKey = null;
            ctx.Requests.Add(new HttpRequestItem
            {
                CollectionId = id,
                Name = "users dup",
                Method = HttpMethodKind.GET,
                Url = "{{baseUrl}}/users",
                SourceOperationKey = null
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(spec), id);

        Assert.Equal(0, result.Removed);
        await using var check = Ctx();
        var reqs = await check.Requests.Where(r => r.CollectionId == id).ToListAsync();
        Assert.Equal(2, reqs.Count);
        Assert.Equal(1, reqs.Count(r => r.SourceOperationKey == "GET /users"));
        Assert.Equal(1, reqs.Count(r => r.SourceOperationKey == null));
    }

    // ── Variables & folders (additive) ───────────────────────────────────────

    [Fact]
    public async Task Refresh_NeverOverwritesExistingVariableValues_AddsMissingSecurityVars()
    {
        const string v1 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "servers": [ { "url": "https://api.example.com" } ],
              "security": [ { "bearerAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": { "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } } } }
            """;
        const string v2 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "servers": [ { "url": "https://api.example.com" } ],
              "security": [ { "bearerAuth": [] }, { "basicAuth": [] } ],
              "paths": { "/secure": { "get": {} } },
              "components": { "securitySchemes": {
                "bearerAuth": { "type": "http", "scheme": "bearer" },
                "basicAuth": { "type": "http", "scheme": "basic" }
              } } }
            """;
        var id = await ImportAsync(v1);

        // User sets values on the existing variables.
        await using (var ctx = Ctx())
        {
            var baseSet = await ctx.Set<CollectionVariableSet>().Include(s => s.Entries)
                .SingleAsync(s => s.CollectionId == id && s.IsBase);
            baseSet.Entries.Single(e => e.Key == "baseUrl").Value = "https://custom.internal";
            baseSet.Entries.Single(e => e.Key == "token").Value = "my-secret-token";
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.RefreshCollectionAsync(S(v2), id);

        Assert.Equal(1, result.VariablesAdded);
        await using var check = Ctx();
        var entries = await check.Set<CollectionVariableEntry>().ToListAsync();
        Assert.Equal("https://custom.internal", entries.Single(e => e.Key == "baseUrl").Value);
        Assert.Equal("my-secret-token", entries.Single(e => e.Key == "token").Value);
        Assert.Contains(entries, e => e.Key == "basicAuth" && e.Value == "");
    }

    [Fact]
    public async Task Refresh_NewTag_CreatesFolder_KeepsExistingFolders()
    {
        const string v1 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": { "tags": ["Users"] } } } }
            """;
        const string v2 = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": {
                "/users": { "get": { "tags": ["Users"] } },
                "/orders": { "get": { "tags": ["Orders"] } }
              } }
            """;
        var id = await ImportAsync(v1);

        var result = await _sut.RefreshCollectionAsync(S(v2), id);

        Assert.Equal(1, result.FoldersCreated);
        await using var ctx = Ctx();
        var folderNames = await ctx.CollectionFolders.Where(f => f.CollectionId == id).Select(f => f.Name).ToListAsync();
        Assert.Contains("Users", folderNames);
        Assert.Contains("Orders", folderNames);
        var ordersFolder = await ctx.CollectionFolders.SingleAsync(f => f.CollectionId == id && f.Name == "Orders");
        var ordersReq = await ctx.Requests.SingleAsync(r => r.Url == "{{baseUrl}}/orders");
        Assert.Equal(ordersFolder.Id, ordersReq.FolderId);
    }

    // ── Error / no-op paths ──────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_UnreadableSpec_IsNoOpWithWarning()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;
        var id = await ImportAsync(spec);

        var result = await _sut.RefreshCollectionAsync(S("not an openapi doc {{{"), id);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Equal(0, result.Completed);
        Assert.NotEmpty(result.Warnings);
        await using var ctx = Ctx();
        Assert.Single(await ctx.Requests.Where(r => r.CollectionId == id).ToListAsync());
    }

    [Fact]
    public async Task Refresh_UnknownCollection_WarnsAndDoesNothing()
    {
        const string spec = """
            { "openapi": "3.0.0", "info": { "title": "A", "version": "1" },
              "paths": { "/users": { "get": {} } } }
            """;

        var result = await _sut.RefreshCollectionAsync(S(spec), collectionId: 9999);

        Assert.Equal(0, result.Added);
        Assert.NotEmpty(result.Warnings);
    }
}
