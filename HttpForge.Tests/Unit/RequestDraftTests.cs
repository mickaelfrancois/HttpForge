using HttpForge.Data.Entities;
using HttpForge.Models;

namespace HttpForge.Tests.Unit;

public class RequestDraftTests
{
    private static RequestDraft MakeDraft() => new()
    {
        RequestId = 1,
        LoadedAt = DateTime.UtcNow,
        Name = "My Request",
        Method = HttpMethodKind.GET,
        Url = "https://example.com",
        BodyKind = BodyKind.None
    };

    [Fact]
    public void IsDirty_FalseOnCreation()
    {
        var draft = MakeDraft();
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void MarkDirty_SetsIsDirtyTrue()
    {
        var draft = MakeDraft();
        draft.MarkDirty();
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void ClearDirty_ResetsIsDirty()
    {
        var draft = MakeDraft();
        draft.MarkDirty();
        draft.ClearDirty();
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void FromRequest_CopiesAllFields()
    {
        var request = new HttpRequestItem
        {
            Id = 7,
            Name = "Test",
            Method = HttpMethodKind.POST,
            Url = "https://api.test/v1",
            BodyKind = BodyKind.Json,
            BodyContent = "{\"a\":1}",
            PostScript = "fg.variables.set('x', '1');",
            Headers = [new HeaderItem { Key = "Authorization", Value = "Bearer token" }],
            QueryParams = [],
            FormFields = [],
            Variables = [new RequestVariable { Key = "BASE_URL", Value = "https://api.test" }]
        };

        var loadedAt = DateTime.UtcNow;
        var draft = RequestDraft.FromRequest(request, loadedAt);

        Assert.Equal(7, draft.RequestId);
        Assert.Equal(loadedAt, draft.LoadedAt);
        Assert.Equal("Test", draft.Name);
        Assert.Equal(HttpMethodKind.POST, draft.Method);
        Assert.Equal("https://api.test/v1", draft.Url);
        Assert.Equal(BodyKind.Json, draft.BodyKind);
        Assert.Equal("{\"a\":1}", draft.BodyContent);
        Assert.Equal("fg.variables.set('x', '1');", draft.PostScript);
        Assert.Single(draft.Headers);
        Assert.Single(draft.Variables);
        Assert.NotSame(request.Headers, draft.Headers);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void ToTransientRequest_CopiesAllFieldsAndKeepsDirty()
    {
        var draft = new RequestDraft
        {
            RequestId = 7,
            LoadedAt = DateTime.UtcNow,
            Name = "Test",
            Method = HttpMethodKind.POST,
            Url = "https://api.test/v1",
            BodyKind = BodyKind.Json,
            BodyContent = "{\"a\":1}",
            PostScript = "fg.variables.set('x', '1');",
            PostScriptTrusted = false,
            IgnoreTlsErrors = true,
            Headers = [new HeaderItem { Key = "Authorization", Value = "Bearer token" }],
            QueryParams = [new QueryParamItem { Key = "q", Value = "1", Enabled = true }],
            FormFields = [],
            Variables = [new RequestVariable { Key = "BASE_URL", Value = "https://api.test" }]
        };
        draft.MarkDirty();

        var request = draft.ToTransientRequest();

        Assert.Equal(7, request.Id);
        Assert.Equal("Test", request.Name);
        Assert.Equal(HttpMethodKind.POST, request.Method);
        Assert.Equal("https://api.test/v1", request.Url);
        Assert.Equal(BodyKind.Json, request.BodyKind);
        Assert.Equal("{\"a\":1}", request.BodyContent);
        Assert.Equal("fg.variables.set('x', '1');", request.PostScript);
        Assert.False(request.PostScriptTrusted);
        Assert.True(request.IgnoreTlsErrors);
        Assert.Single(request.Headers);
        Assert.Single(request.QueryParams);
        Assert.Single(request.Variables);
        Assert.NotSame(draft.Headers, request.Headers);
        Assert.True(draft.IsDirty); // materializing must not clear the dirty flag
    }

    [Fact]
    public void IgnoreTlsErrors_DefaultsFalse()
    {
        var draft = MakeDraft();
        Assert.False(draft.IgnoreTlsErrors);
    }

    [Fact]
    public void FromRequest_CopiesIgnoreTlsErrors()
    {
        var request = new HttpRequestItem
        {
            Id = 1,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None,
            IgnoreTlsErrors = true
        };

        var draft = RequestDraft.FromRequest(request, DateTime.UtcNow);

        Assert.True(draft.IgnoreTlsErrors);
    }
}
