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
