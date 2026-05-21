# Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `HttpForge.Tests` — a single xUnit project with tests for `VariableResolver`, `AppState`, `VariablePreview`, `RequestExecutor`, and `InsomniaImporter`, organized under a `Services/` folder.

**Architecture:** One test project references the main `HttpForge` project. Pure services are tested directly; `RequestExecutor` uses a `FakeHttpMessageHandler` to capture outgoing HTTP; `InsomniaImporter` uses EF Core + SQLite in-memory (shared connection) so the full import pipeline runs against a real schema without touching disk.

**Tech Stack:** xUnit 2.x, Moq 4.x, Microsoft.EntityFrameworkCore.Sqlite 9.x, Microsoft.Data.Sqlite (transitive)

---

## File Map

| Action | Path |
|---|---|
| Create | `HttpForge.sln` |
| Create | `HttpForge.Tests/HttpForge.Tests.csproj` |
| Create | `HttpForge.Tests/Helpers/FakeHttpMessageHandler.cs` |
| Create | `HttpForge.Tests/Services/VariableResolverTests.cs` |
| Create | `HttpForge.Tests/Services/AppStateTests.cs` |
| Create | `HttpForge.Tests/Services/VariablePreviewTests.cs` |
| Create | `HttpForge.Tests/Services/RequestExecutorTests.cs` |
| Create | `HttpForge.Tests/Services/InsomniaImporterTests.cs` |

---

## Task 1: Solution file and test project scaffold

**Files:**
- Create: `HttpForge.sln`
- Create: `HttpForge.Tests/HttpForge.Tests.csproj`

- [ ] **Step 1: Create the solution file**

Run from repo root (`D:\Development\HttpForge`):
```bash
dotnet new sln -n HttpForge
dotnet sln HttpForge.sln add HttpForge/HttpForge.csproj
```

Expected: `HttpForge.sln` created, `HttpForge/HttpForge.csproj` added.

- [ ] **Step 2: Scaffold the xUnit project**

```bash
dotnet new xunit -o HttpForge.Tests --target-framework net10.0
dotnet sln HttpForge.sln add HttpForge.Tests/HttpForge.Tests.csproj
```

Expected: `HttpForge.Tests/` created with `.csproj` and a stub test file.

- [ ] **Step 3: Add packages and project reference**

```bash
dotnet add HttpForge.Tests package Moq
dotnet add HttpForge.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 9.0.5
dotnet add HttpForge.Tests/HttpForge.Tests.csproj reference HttpForge/HttpForge.csproj
```

- [ ] **Step 4: Delete the stub test file**

Delete `HttpForge.Tests/UnitTest1.cs` (created by the template, not needed).

- [ ] **Step 5: Build to verify zero errors**

```bash
dotnet build HttpForge.sln
```

Expected output ends with: `Build succeeded.  0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add HttpForge.sln HttpForge.Tests/
git commit -m "chore: add HttpForge.Tests project with xUnit, Moq, EF Core Sqlite"
```

---

## Task 2: FakeHttpMessageHandler helper

**Files:**
- Create: `HttpForge.Tests/Helpers/FakeHttpMessageHandler.cs`

- [ ] **Step 1: Create the file**

```csharp
// HttpForge.Tests/Helpers/FakeHttpMessageHandler.cs
using System.Net;

namespace HttpForge.Tests.Helpers;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = string.Empty;

    public void SetResponse(HttpStatusCode statusCode, string body = "")
    {
        _statusCode = statusCode;
        _responseBody = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody)
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build HttpForge.Tests
```

Expected: `Build succeeded.  0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Helpers/FakeHttpMessageHandler.cs
git commit -m "test: add FakeHttpMessageHandler helper"
```

---

## Task 3: VariableResolver tests

**Files:**
- Create: `HttpForge.Tests/Services/VariableResolverTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
// HttpForge.Tests/Services/VariableResolverTests.cs
using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class VariableResolverTests
{
    private readonly VariableResolver _sut = new();

    [Fact]
    public void Resolve_KnownVariable_IsSubstituted()
    {
        var vars = new Dictionary<string, string> { ["name"] = "world" };
        Assert.Equal("Hello world!", _sut.Resolve("Hello {{name}}!", vars));
    }

    [Fact]
    public void Resolve_UnknownVariable_LeftAsIs()
    {
        Assert.Equal("{{unknown}}", _sut.Resolve("{{unknown}}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Resolve_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _sut.Resolve(null, new Dictionary<string, string>()));
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _sut.Resolve("", new Dictionary<string, string>()));
    }

    [Fact]
    public void Resolve_VariableNameWithHyphen_IsSubstituted()
    {
        var vars = new Dictionary<string, string> { ["api-key"] = "abc123" };
        Assert.Equal("abc123", _sut.Resolve("{{api-key}}", vars));
    }

    [Fact]
    public void Resolve_VariableNameWithDot_IsSubstituted()
    {
        var vars = new Dictionary<string, string> { ["server.host"] = "localhost" };
        Assert.Equal("localhost", _sut.Resolve("{{server.host}}", vars));
    }

    [Fact]
    public void Resolve_MultipleVariables_AllSubstituted()
    {
        var vars = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        Assert.Equal("1-2", _sut.Resolve("{{a}}-{{b}}", vars));
    }

    [Fact]
    public void Resolve_SpacesAroundVarName_IsSubstituted()
    {
        var vars = new Dictionary<string, string> { ["x"] = "yes" };
        Assert.Equal("yes", _sut.Resolve("{{ x }}", vars));
    }

    [Fact]
    public void Resolve_CaseInsensitiveDictionary_IsSubstituted()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "world" };
        Assert.Equal("world", _sut.Resolve("{{name}}", vars));
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test HttpForge.Tests --filter "FullyQualifiedName~VariableResolverTests" --logger "console;verbosity=normal"
```

Expected: `9 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Services/VariableResolverTests.cs
git commit -m "test: add VariableResolver tests"
```

---

## Task 4: AppState tests

**Files:**
- Create: `HttpForge.Tests/Services/AppStateTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
// HttpForge.Tests/Services/AppStateTests.cs
using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class AppStateTests
{
    private readonly AppState _sut = new();

    private static AppEnvironment GlobalEnv(params (string key, string value, bool isSecret)[] vars) =>
        new()
        {
            Variables = vars.Select(v => new EnvironmentVariable
            {
                Key = v.key, Value = v.value, IsSecret = v.isSecret
            }).ToList()
        };

    private static AppEnvironment GlobalEnv(params (string key, string value)[] vars) =>
        GlobalEnv(vars.Select(v => (v.key, v.value, false)).ToArray());

    private static CollectionVariableSet CollectionSet(params (string key, string value)[] vars) =>
        new()
        {
            Entries = vars.Select(v => new CollectionVariableEntry
            {
                Key = v.key, Value = v.value
            }).ToList()
        };

    private static HttpRequestItem Request(params (string key, string value)[] vars) =>
        new()
        {
            Variables = vars.Select(v => new RequestVariable
            {
                Key = v.key, Value = v.value
            }).ToList()
        };

    [Fact]
    public void BuildVariables_RequestOverridesCollection_UsesRequestValue()
    {
        var result = _sut.BuildVariables(
            null, null,
            CollectionSet(("x", "col")),
            null,
            Request(("x", "req")));

        Assert.Equal("req", result.Single(v => v.Key == "x").Value);
    }

    [Fact]
    public void BuildVariables_CollectionOverridesGlobal_UsesCollectionValue()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("x", "global")),
            null,
            CollectionSet(("x", "col")),
            null, null);

        Assert.Equal("col", result.Single(v => v.Key == "x").Value);
    }

    [Fact]
    public void BuildVariables_AllSourcesPresent_ThreeDistinctKeys()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("g", "gv")),
            null,
            CollectionSet(("c", "cv")),
            null,
            Request(("r", "rv")));

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void BuildVariables_AllNull_ReturnsEmpty()
    {
        var result = _sut.BuildVariables(null, null, null, null, null);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildVariables_CaseInsensitiveMerge_LastWriterWins()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("KEY", "global")),
            null,
            CollectionSet(("key", "col")),
            null, null);

        Assert.Single(result);
        Assert.Equal("col", result[0].Value);
    }

    [Fact]
    public void BuildVariables_ResultOrderedByKey()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("z", "1"), ("a", "2")),
            null, null, null, null);

        Assert.Equal("a", result[0].Key);
        Assert.Equal("z", result[1].Key);
    }

    [Fact]
    public void BuildVariables_GlobalSubsetOverridesGlobalBase()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("x", "base")),
            GlobalEnv(("x", "subset")),
            null, null, null);

        Assert.Equal("subset", result.Single().Value);
    }

    [Fact]
    public void BuildVariables_CollectionSubsetOverridesCollectionBase()
    {
        var result = _sut.BuildVariables(
            null, null,
            CollectionSet(("x", "base")),
            CollectionSet(("x", "subset")),
            null);

        Assert.Equal("subset", result.Single().Value);
    }

    [Fact]
    public void BuildVariables_VariableSources_SetCorrectly()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("g", "v")),
            null,
            CollectionSet(("c", "v")),
            null,
            Request(("r", "v")));

        Assert.Equal(VariableSource.Global,     result.Single(v => v.Key == "g").Source);
        Assert.Equal(VariableSource.Collection, result.Single(v => v.Key == "c").Source);
        Assert.Equal(VariableSource.Request,    result.Single(v => v.Key == "r").Source);
    }

    [Fact]
    public void BuildVariables_SecretFlag_Preserved()
    {
        var result = _sut.BuildVariables(
            GlobalEnv(("token", "abc", true)),
            null, null, null, null);

        Assert.True(result.Single().IsSecret);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test HttpForge.Tests --filter "FullyQualifiedName~AppStateTests" --logger "console;verbosity=normal"
```

Expected: `10 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Services/AppStateTests.cs
git commit -m "test: add AppState.BuildVariables tests"
```

---

## Task 5: VariablePreview tests

**Files:**
- Create: `HttpForge.Tests/Services/VariablePreviewTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
// HttpForge.Tests/Services/VariablePreviewTests.cs
using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class VariablePreviewTests
{
    private static ResolvedVariableEntry Entry(
        string key, string value,
        bool isSecret = false,
        VariableSource source = VariableSource.Global) =>
        new(key, value, isSecret, source);

    // Build() tests
    [Fact]
    public void Build_KnownVariable_ShowsValueAndSource()
    {
        var vars = new List<ResolvedVariableEntry> { Entry("host", "localhost") };
        var result = VariablePreview.Build("{{host}}", vars);
        Assert.Contains("localhost", result);
        Assert.Contains("[Global]", result);
    }

    [Fact]
    public void Build_SecretVariable_ShowsSecretMaskNotValue()
    {
        var vars = new List<ResolvedVariableEntry> { Entry("token", "abc123", isSecret: true) };
        var result = VariablePreview.Build("{{token}}", vars);
        Assert.Contains("(secret)", result);
        Assert.DoesNotContain("abc123", result);
    }

    [Fact]
    public void Build_UnknownVariable_ShowsNotDefined()
    {
        var result = VariablePreview.Build("{{missing}}", new List<ResolvedVariableEntry>());
        Assert.Contains("(not defined)", result);
    }

    [Fact]
    public void Build_DuplicateVariable_DeduplicatedToOneLine()
    {
        var vars = new List<ResolvedVariableEntry> { Entry("x", "v") };
        var result = VariablePreview.Build("{{x}} and {{x}}", vars);
        Assert.Single(result.Split('\n'));
    }

    [Fact]
    public void Build_NoVariablesInInput_ReturnsEmpty()
    {
        var result = VariablePreview.Build("no vars here", new List<ResolvedVariableEntry>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Build_NullInput_ReturnsEmpty()
    {
        var result = VariablePreview.Build(null, new List<ResolvedVariableEntry>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Build_MultipleVariables_OneLineEach()
    {
        var vars = new List<ResolvedVariableEntry>
        {
            Entry("a", "1"),
            Entry("b", "2")
        };
        var result = VariablePreview.Build("{{a}} {{b}}", vars);
        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
    }

    // Resolve() tests
    [Fact]
    public void Resolve_KnownNonSecret_Substituted()
    {
        var vars = new List<ResolvedVariableEntry> { Entry("host", "localhost") };
        Assert.Equal("http://localhost", VariablePreview.Resolve("http://{{host}}", vars));
    }

    [Fact]
    public void Resolve_SecretVariable_LeftAsIs()
    {
        var vars = new List<ResolvedVariableEntry> { Entry("token", "abc123", isSecret: true) };
        Assert.Equal("{{token}}", VariablePreview.Resolve("{{token}}", vars));
    }

    [Fact]
    public void Resolve_UnknownVariable_LeftAsIs()
    {
        Assert.Equal("{{missing}}", VariablePreview.Resolve("{{missing}}", new List<ResolvedVariableEntry>()));
    }

    // BuildFullUrl() tests
    [Fact]
    public void BuildFullUrl_NoParams_ReturnsUrlUnchanged()
    {
        var result = VariablePreview.BuildFullUrl(
            "https://example.com", [], new List<ResolvedVariableEntry>());
        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void BuildFullUrl_EnabledParam_AppendedWithQuestionMark()
    {
        var p = new QueryParamItem { Key = "q", Value = "1", Enabled = true };
        var result = VariablePreview.BuildFullUrl(
            "https://example.com", [p], new List<ResolvedVariableEntry>());
        Assert.Equal("https://example.com?q=1", result);
    }

    [Fact]
    public void BuildFullUrl_ExistingQueryString_AppendedWithAmpersand()
    {
        var p = new QueryParamItem { Key = "b", Value = "2", Enabled = true };
        var result = VariablePreview.BuildFullUrl(
            "https://example.com?a=1", [p], new List<ResolvedVariableEntry>());
        Assert.Equal("https://example.com?a=1&b=2", result);
    }

    [Fact]
    public void BuildFullUrl_DisabledParam_Excluded()
    {
        var p = new QueryParamItem { Key = "q", Value = "1", Enabled = false };
        var result = VariablePreview.BuildFullUrl(
            "https://example.com", [p], new List<ResolvedVariableEntry>());
        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void BuildFullUrl_NullUrl_ReturnsEmpty()
    {
        var result = VariablePreview.BuildFullUrl(
            null, [], new List<ResolvedVariableEntry>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildFullUrl_VariableInParam_Resolved()
    {
        var p = new QueryParamItem { Key = "q", Value = "{{term}}", Enabled = true };
        var vars = new List<ResolvedVariableEntry> { Entry("term", "hello") };
        var result = VariablePreview.BuildFullUrl("https://example.com", [p], vars);
        Assert.Equal("https://example.com?q=hello", result);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test HttpForge.Tests --filter "FullyQualifiedName~VariablePreviewTests" --logger "console;verbosity=normal"
```

Expected: `16 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Services/VariablePreviewTests.cs
git commit -m "test: add VariablePreview tests"
```

---

## Task 6: RequestExecutor tests

**Files:**
- Create: `HttpForge.Tests/Services/RequestExecutorTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
// HttpForge.Tests/Services/RequestExecutorTests.cs
using System.Net;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Moq;

namespace HttpForge.Tests.Services;

public class RequestExecutorTests
{
    private static (RequestExecutor sut, FakeHttpMessageHandler handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("forge")).Returns(client);
        return (new RequestExecutor(factory.Object, new VariableResolver()), handler);
    }

    private static readonly Dictionary<string, string> NoVars = new();

    // ── URL building ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoQueryParams_UsesBaseUrl()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem { Url = "https://example.com/api" };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Equal("https://example.com/api", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_EnabledQueryParam_AppendedToUrl()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            QueryParams = [new QueryParamItem { Key = "q", Value = "test", Enabled = true }]
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Contains("q=test", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledQueryParam_NotAppended()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            QueryParams = [new QueryParamItem { Key = "q", Value = "test", Enabled = false }]
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Equal("https://example.com", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_UrlContainsVariable_VariableResolved()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem { Url = "https://{{host}}/api" };
        var vars = new Dictionary<string, string> { ["host"] = "example.com" };

        await sut.ExecuteAsync(req, vars);

        Assert.Equal("https://example.com/api", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_UrlWithExistingQueryString_AdditionalParamUsesAmpersand()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com?a=1",
            QueryParams = [new QueryParamItem { Key = "b", Value = "2", Enabled = true }]
        };

        await sut.ExecuteAsync(req, NoVars);

        var uri = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("a=1", uri);
        Assert.Contains("b=2", uri);
        Assert.DoesNotContain("??", uri);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleEnabledParams_AllAppended()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            QueryParams =
            [
                new QueryParamItem { Key = "a", Value = "1", Enabled = true },
                new QueryParamItem { Key = "b", Value = "2", Enabled = true }
            ]
        };

        await sut.ExecuteAsync(req, NoVars);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("a=1", query);
        Assert.Contains("b=2", query);
    }

    // ── Body building ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BodyKindNone_NoContent()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem { Url = "https://example.com", BodyKind = BodyKind.None };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Null(handler.LastRequest!.Content);
    }

    [Fact]
    public async Task ExecuteAsync_BodyKindJson_ContentTypeIsApplicationJson()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Method = HttpMethodKind.POST,
            BodyKind = BodyKind.Json,
            BodyContent = "{\"key\":\"value\"}"
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Equal("application/json",
            handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
        var body = await handler.LastRequest.Content.ReadAsStringAsync();
        Assert.Equal("{\"key\":\"value\"}", body);
    }

    [Fact]
    public async Task ExecuteAsync_BodyKindJson_VariablesResolvedInBody()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Method = HttpMethodKind.POST,
            BodyKind = BodyKind.Json,
            BodyContent = "{\"key\":\"{{val}}\"}"
        };
        var vars = new Dictionary<string, string> { ["val"] = "hello" };

        await sut.ExecuteAsync(req, vars);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Equal("{\"key\":\"hello\"}", body);
    }

    [Fact]
    public async Task ExecuteAsync_BodyKindRaw_ContentTypeIsTextPlain()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Method = HttpMethodKind.POST,
            BodyKind = BodyKind.Raw,
            BodyContent = "raw body"
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.Equal("text/plain",
            handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
        var body = await handler.LastRequest.Content.ReadAsStringAsync();
        Assert.Equal("raw body", body);
    }

    [Fact]
    public async Task ExecuteAsync_BodyKindFormUrlEncoded_EnabledFieldsEncoded()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Method = HttpMethodKind.POST,
            BodyKind = BodyKind.FormUrlEncoded,
            FormFields =
            [
                new FormFieldItem { Key = "a", Value = "1", Enabled = true },
                new FormFieldItem { Key = "b", Value = "2", Enabled = false }
            ]
        };

        await sut.ExecuteAsync(req, NoVars);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("a=1", body);
        Assert.DoesNotContain("b=2", body);
    }

    // ── Headers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EnabledHeader_AddedToRequest()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Headers = [new HeaderItem { Key = "X-Test", Value = "hello", Enabled = true }]
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Test", out var values));
        Assert.Contains("hello", values);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledHeader_NotAdded()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Headers = [new HeaderItem { Key = "X-Test", Value = "hello", Enabled = false }]
        };

        await sut.ExecuteAsync(req, NoVars);

        Assert.False(handler.LastRequest!.Headers.Contains("X-Test"));
    }

    [Fact]
    public async Task ExecuteAsync_HeaderWithVariable_VariableResolved()
    {
        var (sut, handler) = Create();
        var req = new HttpRequestItem
        {
            Url = "https://example.com",
            Headers = [new HeaderItem { Key = "X-Api-Key", Value = "{{token}}", Enabled = true }]
        };
        var vars = new Dictionary<string, string> { ["token"] = "mytoken" };

        await sut.ExecuteAsync(req, vars);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Contains("mytoken", values);
    }

    // ── Result ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuccessResponse_ReturnsStatusAndBody()
    {
        var (sut, handler) = Create();
        handler.SetResponse(HttpStatusCode.Created, "{\"id\":1}");
        var req = new HttpRequestItem { Url = "https://example.com" };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal("{\"id\":1}", result.Body);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidUrl_ReturnsErrorResult()
    {
        var (sut, _) = Create();
        var req = new HttpRequestItem { Url = "not-a-valid-url" };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(0, result.StatusCode);
        Assert.NotNull(result.Error);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestExecutorTests" --logger "console;verbosity=normal"
```

Expected: `15 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Services/RequestExecutorTests.cs
git commit -m "test: add RequestExecutor tests"
```

---

## Task 7: InsomniaImporter tests

**Files:**
- Create: `HttpForge.Tests/Services/InsomniaImporterTests.cs`

The pattern: each test class instance opens one shared `SqliteConnection` to an in-memory database, creates the schema once in the constructor, and passes a Moq factory that returns fresh `AppDbContext` instances over that same connection. The class implements `IDisposable` to close the connection after each test.

- [ ] **Step 1: Create the test file**

```csharp
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
        Assert.Empty(ctx.Requests.ToList());
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
        Assert.Empty(ctx.Collections.ToList());
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test HttpForge.Tests --filter "FullyQualifiedName~InsomniaImporterTests" --logger "console;verbosity=normal"
```

Expected: `14 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add HttpForge.Tests/Services/InsomniaImporterTests.cs
git commit -m "test: add InsomniaImporter tests"
```

---

## Task 8: Final verification

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test HttpForge.sln --logger "console;verbosity=normal"
```

Expected: all tests pass, 0 failed. Total should be ≥ 64 tests across all 5 test classes.

- [ ] **Step 2: Build the main app to confirm nothing was broken**

```bash
dotnet build HttpForge/HttpForge.csproj
```

Expected: `Build succeeded.  0 Error(s)`

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "test: complete HttpForge.Tests suite — VariableResolver, AppState, VariablePreview, RequestExecutor, InsomniaImporter"
```
