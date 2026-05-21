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
