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
            Variables = []
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
        Assert.False(draft.IsDirty);
    }
}
