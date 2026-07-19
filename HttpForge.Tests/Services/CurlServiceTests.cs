using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Tests.Services;

public class CurlServiceTests
{
    private readonly CurlService _sut = new();

    // ── Parse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleGet_UrlOnly()
    {
        var r = _sut.Parse("curl https://api.example.com/users");

        Assert.Equal(HttpMethodKind.GET, r.Method);
        Assert.Equal("https://api.example.com/users", r.Url);
        Assert.Equal(BodyKind.None, r.BodyKind);
        Assert.Empty(r.Headers);
    }

    [Fact]
    public void Parse_PostWithHeadersAndJsonBody()
    {
        var curl = "curl -X POST 'https://api.example.com/login' " +
                   "-H 'Content-Type: application/json' " +
                   "-H 'Accept: application/json' " +
                   "-d '{\"user\":\"bob\"}'";

        var r = _sut.Parse(curl);

        Assert.Equal(HttpMethodKind.POST, r.Method);
        Assert.Equal("https://api.example.com/login", r.Url);
        Assert.Equal(BodyKind.Json, r.BodyKind);
        Assert.Equal("{\"user\":\"bob\"}", r.Body);
        // Content-Type is folded into BodyKind and removed from the header list.
        Assert.DoesNotContain(r.Headers, h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Headers, h => h.Key == "Accept" && h.Value == "application/json");
    }

    [Fact]
    public void Parse_DataWithoutMethod_ImpliesPost()
    {
        var r = _sut.Parse("curl https://api.example.com -d 'hello=world'");

        Assert.Equal(HttpMethodKind.POST, r.Method);
    }

    [Fact]
    public void Parse_DataUrlencode_ProducesFormFields()
    {
        var curl = "curl https://api.example.com/form " +
                   "--data-urlencode 'name=John Doe' --data-urlencode 'age=30'";

        var r = _sut.Parse(curl);

        Assert.Equal(BodyKind.FormUrlEncoded, r.BodyKind);
        Assert.Equal(HttpMethodKind.POST, r.Method);
        Assert.Collection(r.FormFields,
            f => { Assert.Equal("name", f.Key); Assert.Equal("John Doe", f.Value); },
            f => { Assert.Equal("age", f.Key); Assert.Equal("30", f.Value); });
    }

    [Fact]
    public void Parse_FormContentType_SplitsDataIntoFields()
    {
        var curl = "curl -X POST https://api.example.com " +
                   "-H 'Content-Type: application/x-www-form-urlencoded' " +
                   "-d 'a=1&b=2'";

        var r = _sut.Parse(curl);

        Assert.Equal(BodyKind.FormUrlEncoded, r.BodyKind);
        Assert.Collection(r.FormFields,
            f => { Assert.Equal("a", f.Key); Assert.Equal("1", f.Value); },
            f => { Assert.Equal("b", f.Key); Assert.Equal("2", f.Value); });
    }

    [Fact]
    public void Parse_Insecure_SetsIgnoreTls()
    {
        var r = _sut.Parse("curl -k https://self-signed.local");

        Assert.True(r.IgnoreTlsErrors);
    }

    [Fact]
    public void Parse_UserFlag_BecomesBasicAuthHeader()
    {
        var r = _sut.Parse("curl -u alice:secret https://api.example.com");

        var auth = Assert.Single(r.Headers, h => h.Key == "Authorization");
        Assert.StartsWith("Basic ", auth.Value);
    }

    [Fact]
    public void Parse_UnknownFlag_WarnsButDoesNotThrow()
    {
        var r = _sut.Parse("curl --frobnicate https://api.example.com");

        Assert.Equal("https://api.example.com", r.Url);
        Assert.Contains(r.Warnings, w => w.Contains("--frobnicate"));
    }

    [Fact]
    public void Parse_MultilineWithContinuationAndMixedQuotes()
    {
        var curl = "curl -X PUT \\\n" +
                   "  \"https://api.example.com/items/1\" \\\n" +
                   "  -H \"Authorization: Bearer abc123\" \\\n" +
                   "  --data-raw '{\"name\":\"box\"}'";

        var r = _sut.Parse(curl);

        Assert.Equal(HttpMethodKind.PUT, r.Method);
        Assert.Equal("https://api.example.com/items/1", r.Url);
        Assert.Contains(r.Headers, h => h.Key == "Authorization" && h.Value == "Bearer abc123");
        Assert.Equal(BodyKind.Json, r.BodyKind);
        Assert.Equal("{\"name\":\"box\"}", r.Body);
    }

    [Fact]
    public void Parse_MissingUrl_Warns()
    {
        var r = _sut.Parse("curl -X POST -d 'x=1'");

        Assert.Equal(string.Empty, r.Url);
        Assert.Contains(r.Warnings, w => w.Contains("URL"));
    }

    // ── Build ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Get_OmitsExplicitMethod()
    {
        var cmd = _sut.Build(new CurlExportRequest(
            HttpMethodKind.GET, "https://api.example.com", [], BodyKind.None, null, []));

        Assert.StartsWith("curl 'https://api.example.com'", cmd);
        Assert.DoesNotContain("-X", cmd);
    }

    [Fact]
    public void Build_PostJson_IncludesMethodContentTypeAndData()
    {
        var cmd = _sut.Build(new CurlExportRequest(
            HttpMethodKind.POST, "https://api.example.com",
            [new CurlHeader("Accept", "application/json")],
            BodyKind.Json, "{\"a\":1}", []));

        Assert.Contains("-X POST", cmd);
        Assert.Contains("-H 'Accept: application/json'", cmd);
        Assert.Contains("-H 'Content-Type: application/json'", cmd);
        Assert.Contains("--data-raw '{\"a\":1}'", cmd);
    }

    // ── Round-trip (parse ∘ build) ───────────────────────────────────────────────

    [Fact]
    public void RoundTrip_JsonPost_IsStable()
    {
        var original = new CurlExportRequest(
            HttpMethodKind.POST, "https://api.example.com/login",
            [new CurlHeader("Accept", "application/json")],
            BodyKind.Json, "{\"user\":\"bob\"}", []);

        var r = _sut.Parse(_sut.Build(original));

        Assert.Equal(original.Method, r.Method);
        Assert.Equal(original.Url, r.Url);
        Assert.Equal(original.BodyKind, r.BodyKind);
        Assert.Equal(original.Body, r.Body);
        Assert.Equal(original.Headers, r.Headers);
    }

    [Fact]
    public void RoundTrip_Form_IsStable()
    {
        var original = new CurlExportRequest(
            HttpMethodKind.POST, "https://api.example.com/form", [],
            BodyKind.FormUrlEncoded, null,
            [new CurlFormField("name", "john"), new CurlFormField("age", "30")]);

        var r = _sut.Parse(_sut.Build(original));

        Assert.Equal(BodyKind.FormUrlEncoded, r.BodyKind);
        Assert.Equal(original.FormFields, r.FormFields);
    }

    [Fact]
    public void RoundTrip_Raw_IsStable()
    {
        var original = new CurlExportRequest(
            HttpMethodKind.POST, "https://api.example.com/echo", [],
            BodyKind.Raw, "hello world", []);

        var r = _sut.Parse(_sut.Build(original));

        Assert.Equal(BodyKind.Raw, r.BodyKind);
        Assert.Equal("hello world", r.Body);
    }

    [Fact]
    public void RoundTrip_GetWithHeader_IsStable()
    {
        var original = new CurlExportRequest(
            HttpMethodKind.GET, "https://api.example.com/me",
            [new CurlHeader("Authorization", "Bearer xyz")],
            BodyKind.None, null, []);

        var r = _sut.Parse(_sut.Build(original));

        Assert.Equal(HttpMethodKind.GET, r.Method);
        Assert.Equal(original.Headers, r.Headers);
        Assert.Equal(BodyKind.None, r.BodyKind);
    }

    [Fact]
    public void RoundTrip_ValueWithSingleQuote_SurvivesShellQuoting()
    {
        // Exercises the '\'' escaping idiom in Build and the tokenizer in Parse.
        var original = new CurlExportRequest(
            HttpMethodKind.GET, "https://api.example.com",
            [new CurlHeader("X-Msg", "it's a test")],
            BodyKind.None, null, []);

        var r = _sut.Parse(_sut.Build(original));

        var header = Assert.Single(r.Headers);
        Assert.Equal("X-Msg", header.Key);
        Assert.Equal("it's a test", header.Value);
    }
}
