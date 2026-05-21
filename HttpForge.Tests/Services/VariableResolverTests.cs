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
