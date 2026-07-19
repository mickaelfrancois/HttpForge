using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Tests.Services;

// CurlService only builds an export command (import was removed).
public class CurlServiceTests
{
    private readonly CurlService _sut = new();

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

    [Fact]
    public void Build_Form_EmitsDataUrlencodePerField()
    {
        var cmd = _sut.Build(new CurlExportRequest(
            HttpMethodKind.POST, "https://api.example.com/form", [],
            BodyKind.FormUrlEncoded, null,
            [new CurlFormField("name", "john"), new CurlFormField("age", "30")]));

        Assert.Contains("--data-urlencode 'name=john'", cmd);
        Assert.Contains("--data-urlencode 'age=30'", cmd);
    }

    [Fact]
    public void Build_EscapesSingleQuotes()
    {
        var cmd = _sut.Build(new CurlExportRequest(
            HttpMethodKind.GET, "https://api.example.com",
            [new CurlHeader("X-Msg", "it's a test")],
            BodyKind.None, null, []));

        // POSIX '\'' idiom keeps the value shell-safe.
        Assert.Contains("-H 'X-Msg: it'\\''s a test'", cmd);
    }
}
