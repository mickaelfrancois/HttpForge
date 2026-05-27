// HttpForge.Tests/Services/RequestExecutorTests.cs
using System.Net;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;

namespace HttpForge.Tests.Services;

public class RequestExecutorTests
{
    private static (RequestExecutor sut, FakeHttpMessageHandler handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        return (new RequestExecutor(new VariableResolver(), () => handler), handler);
    }

    private static readonly IReadOnlyDictionary<string, string> NoVars = new Dictionary<string, string>();

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

        Assert.Empty(handler.LastRequest!.RequestUri!.Query);
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

    // ── TLS certificate validation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SelfSignedHttps_WithoutBypass_ReturnsError()
    {
        using var server = new SelfSignedHttpsServer();
        var sut = new RequestExecutor(new VariableResolver()); // real handler, no fake
        var req = new HttpRequestItem { Url = server.Url, IgnoreTlsErrors = false };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(0, result.StatusCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_SelfSignedHttps_WithBypass_Succeeds()
    {
        using var server = new SelfSignedHttpsServer();
        var sut = new RequestExecutor(new VariableResolver()); // real handler, no fake
        var req = new HttpRequestItem { Url = server.Url, IgnoreTlsErrors = true };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("OK", result.Body);
        Assert.Null(result.Error);
    }
}
