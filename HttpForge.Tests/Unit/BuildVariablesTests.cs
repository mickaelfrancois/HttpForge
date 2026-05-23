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

    private static UserVariableValue MakeUserValue(string key, string value, string scopeType = "global_env", int scopeId = 1) =>
        new() { UserId = "u1", ScopeType = scopeType, ScopeId = scopeId, VariableKey = key, Value = value };

    [Fact]
    public void BuildVariables_PersonalValueOverridesShared()
    {
        var state = new AppState();
        var env = MakeEnv(("JWT", "shared-token"));
        var userValues = new List<UserVariableValue> { MakeUserValue("JWT", "my-token") };

        var result = state.BuildVariables(env, null, null, null, null, userValues);

        var jwt = result.First(r => r.Key == "JWT");
        Assert.Equal("my-token", jwt.Value);
    }

    [Fact]
    public void BuildVariables_NoPersonalValue_UsesSharedDefault()
    {
        var state = new AppState();
        var env = MakeEnv(("BASE_URL", "https://api.example.com"));
        var userValues = new List<UserVariableValue>();

        var result = state.BuildVariables(env, null, null, null, null, userValues);

        var baseUrl = result.First(r => r.Key == "BASE_URL");
        Assert.Equal("https://api.example.com", baseUrl.Value);
    }

    [Fact]
    public void BuildVariables_EmptyUserValues_BehavesLikeOriginal()
    {
        var state = new AppState();
        var env = MakeEnv(("X", "1"), ("Y", "2"));

        var result = state.BuildVariables(env, null, null, null, null, []);

        Assert.Equal(2, result.Count);
    }
}
