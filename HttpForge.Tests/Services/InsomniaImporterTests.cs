// HttpForge.Tests/Services/InsomniaImporterTests.cs
using System.Text;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HttpForge.Tests.Services;

public class InsomniaImporterTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;
    private readonly InsomniaImporter _sut;

    public InsomniaImporterTests()
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

        _sut = new InsomniaImporter(factory.Object);
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext Ctx() => new(_opts);

    private static Stream S(string yaml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(yaml));

    // ── Collection import ────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_SimpleGetRequest_CreatedInDb()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: My API
            collection:
              - name: Get Users
                url: https://api.example.com/users
                method: GET
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "test.yaml");

        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal("Get Users", req.Name);
        Assert.Equal("https://api.example.com/users", req.Url);
        Assert.Equal(HttpMethodKind.GET, req.Method);
    }

    [Fact]
    public async Task ImportFileAsync_InsomniaVarSyntax_TransformedToForgeSyntax()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Test
                url: https://{{ _.baseUrl }}/endpoint
                method: GET
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal("https://{{ baseUrl }}/endpoint", req.Url);
    }

    [Fact]
    public async Task ImportFileAsync_BracketVarSyntax_Transformed()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Test
                url: "https://{{ _['base-url'] }}/ep"
                method: GET
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal("https://{{ base-url }}/ep", req.Url);
    }

    [Fact]
    public async Task ImportFileAsync_NestedFolder_CreatedWithParentReference()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Auth
                children:
                  - name: Login
                    url: https://api.example.com/login
                    method: POST
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "test.yaml");

        Assert.Equal(1, result.FoldersCreated);
        Assert.Equal(1, result.RequestsCreated);
        await using var ctx = Ctx();
        var folder = await ctx.CollectionFolders.SingleAsync();
        Assert.Equal("Auth", folder.Name);
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(folder.Id, req.FolderId);
        Assert.NotEqual(0, folder.CollectionId);
    }

    [Fact]
    public async Task ImportFileAsync_BearerAuth_InjectedAsAuthorizationHeader()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Authenticated
                url: https://api.example.com
                method: GET
                authentication:
                  type: bearer
                  token: mytoken
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.Headers).SingleAsync();
        Assert.Contains(req.Headers,
            h => h.Key == "Authorization" && h.Value == "Bearer mytoken");
    }

    [Fact]
    public async Task ImportFileAsync_JsonBody_MappedToBodyKindJson()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Create
                url: https://api.example.com
                method: POST
                body:
                  mimeType: application/json
                  text: '{"key": "value"}'
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.SingleAsync();
        Assert.Equal(BodyKind.Json, req.BodyKind);
        Assert.Equal("{\"key\": \"value\"}", req.BodyContent);
    }

    [Fact]
    public async Task ImportFileAsync_FormBody_MappedToBodyKindFormUrlEncoded()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Submit
                url: https://api.example.com
                method: POST
                body:
                  mimeType: application/x-www-form-urlencoded
                  params:
                    - name: field1
                      value: val1
                    - name: field2
                      value: val2
                      disabled: true
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.FormFields).SingleAsync();
        Assert.Equal(BodyKind.FormUrlEncoded, req.BodyKind);
        Assert.Single(req.FormFields);
        Assert.Equal("field1", req.FormFields[0].Key);
    }

    [Fact]
    public async Task ImportFileAsync_DisabledHeader_Excluded()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: API
            collection:
              - name: Test
                url: https://api.example.com
                method: GET
                headers:
                  - name: X-Active
                    value: yes
                  - name: X-Disabled
                    value: no
                    disabled: true
            """;

        await _sut.ImportFileAsync(S(yaml), "test.yaml");

        await using var ctx = Ctx();
        var req = await ctx.Requests.Include(r => r.Headers).SingleAsync();
        Assert.Single(req.Headers);
        Assert.Equal("X-Active", req.Headers[0].Key);
    }

    // ── Environment import ───────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_GlobalEnv_CreatesBaseEnvWithVariables()
    {
        const string yaml = """
            type: environment.insomnia.rest/5.0
            name: Global
            environments:
              data:
                API_KEY: abc123
                BASE_URL: https://example.com
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "env.yaml");

        Assert.Equal(2, result.VariablesCreated);
        await using var ctx = Ctx();
        var env = await ctx.Environments.Include(e => e.Variables).SingleAsync(e => e.IsBase);
        Assert.Contains(env.Variables, v => v.Key == "API_KEY" && v.Value == "abc123");
    }

    [Fact]
    public async Task ImportFileAsync_DuplicateGlobalVar_ProducesWarningAndKeepsOriginal()
    {
        await using (var ctx = Ctx())
        {
            ctx.Environments.Add(new AppEnvironment
            {
                Name = "Global",
                IsBase = true,
                Variables = [new EnvironmentVariable { Key = "API_KEY", Value = "original" }]
            });
            await ctx.SaveChangesAsync();
        }

        const string yaml = """
            type: environment.insomnia.rest/5.0
            name: Global
            environments:
              data:
                API_KEY: new_value
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "env.yaml");

        Assert.Contains(result.Warnings, w => w.Contains("API_KEY"));
        await using var ctx2 = Ctx();
        var env = await ctx2.Environments.Include(e => e.Variables).SingleAsync(e => e.IsBase);
        Assert.Equal("original", env.Variables.Single(v => v.Key == "API_KEY").Value);
    }

    [Fact]
    public async Task ImportFileAsync_SubEnvironments_CreatedAsSeparateRows()
    {
        const string yaml = """
            type: environment.insomnia.rest/5.0
            name: Global
            environments:
              data:
                BASE_URL: https://prod.example.com
              subEnvironments:
                - name: Staging
                  data:
                    BASE_URL: https://staging.example.com
            """;

        await _sut.ImportFileAsync(S(yaml), "env.yaml");

        await using var ctx = Ctx();
        Assert.Equal(2, await ctx.Environments.CountAsync());
        var staging = await ctx.Environments
            .Include(e => e.Variables)
            .SingleAsync(e => !e.IsBase);
        Assert.Equal("Staging", staging.Name);
        Assert.Single(staging.Variables);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_VaultEntry_SkippedWithWarning()
    {
        const string yaml = """
            type: environment.insomnia.rest/5.0
            name: Global
            environments:
              data:
                API_KEY: mykey
                __insomnia_vault: encrypted
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "env.yaml");

        Assert.Contains("Vault entries skipped (encrypted, unrecoverable)", result.Warnings);
        await using var ctx = Ctx();
        var env = await ctx.Environments.Include(e => e.Variables).SingleAsync();
        Assert.Single(env.Variables);
        Assert.Equal("API_KEY", env.Variables[0].Key);
    }

    [Fact]
    public async Task ImportFileAsync_Scratchpad_ReturnsImmediatelyWithNoInserts()
    {
        const string yaml = """
            type: collection.insomnia.rest/5.0
            name: Scratch
            meta:
              id: wrk_scratchpad
            collection:
              - name: Test
                url: https://example.com
                method: GET
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "scratch.yaml");

        Assert.Equal(0, result.RequestsCreated);
        await using var ctx = Ctx();
        Assert.Empty(await ctx.Requests.ToListAsync());
    }

    [Fact]
    public async Task ImportFileAsync_UnknownType_ReturnsWarningAndNoInserts()
    {
        const string yaml = """
            type: unknown.workspace/1.0
            name: Unknown
            """;

        var result = await _sut.ImportFileAsync(S(yaml), "unknown.yaml");

        Assert.Contains(result.Warnings, w => w.Contains("Unrecognized workspace type"));
        await using var ctx = Ctx();
        Assert.Empty(await ctx.Collections.ToListAsync());
    }
}
