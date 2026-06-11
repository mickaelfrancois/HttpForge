using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Tests.Unit;

public class BuildVariablesTests
{
    private static AppEnvironment MakeEnv(params (string k, string v)[] vars) => new()
    {
        Id = 1,
        Name = "Base",
        IsBase = true,
        Variables = vars.Select(kv => new EnvironmentVariable { Key = kv.k, Value = kv.v }).ToList()
    };

    private static HttpRequestItem MakeRequest(params (string k, string v)[] vars) => new()
    {
        Variables = vars.Select(kv => new RequestVariable { Key = kv.k, Value = kv.v }).ToList()
    };

    [Fact]
    public void BuildVariables_RequestValueOverridesGlobal()
    {
        var state = new AppState();
        var env = MakeEnv(("JWT", "global-token"));
        var request = MakeRequest(("JWT", "request-token"));

        var result = state.BuildVariables(env, null, null, null, request);

        var jwt = result.First(r => r.Key == "JWT");
        Assert.Equal("request-token", jwt.Value);
        Assert.Equal(VariableSource.Request, jwt.Source);
    }

    [Fact]
    public void BuildVariables_NoOverride_UsesGlobalValue()
    {
        var state = new AppState();
        var env = MakeEnv(("BASE_URL", "https://api.example.com"));

        var result = state.BuildVariables(env, null, null, null, null);

        var baseUrl = result.First(r => r.Key == "BASE_URL");
        Assert.Equal("https://api.example.com", baseUrl.Value);
    }

    [Fact]
    public void BuildVariables_MergesAllScopes()
    {
        var state = new AppState();
        var env = MakeEnv(("X", "1"), ("Y", "2"));

        var result = state.BuildVariables(env, null, null, null, null);

        Assert.Equal(2, result.Count);
    }
}
