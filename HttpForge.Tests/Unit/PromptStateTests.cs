using HttpForge.Components.Layout;

namespace HttpForge.Tests.Unit;

// PromptState bridges the synchronous prompt() shape onto an async modal via a
// TaskCompletionSource<string?>: Submit resolves with the text, Cancel with null.
public class PromptStateTests
{
    [Fact]
    public async Task Submit_ResolvesWithValue_AndHides()
    {
        var state = new PromptState();

        var task = state.AskAsync("Nom :");
        Assert.True(state.Visible);

        state.Submit("Staging");

        Assert.Equal("Staging", await task);
        Assert.False(state.Visible);
    }

    [Fact]
    public async Task Cancel_ResolvesNull_AndHides()
    {
        var state = new PromptState();

        var task = state.AskAsync("Nom :");
        state.Cancel();

        Assert.Null(await task);
        Assert.False(state.Visible);
    }

    [Fact]
    public async Task AskAsync_WhileOpen_ResolvesPreviousToNull()
    {
        var state = new PromptState();

        var first = state.AskAsync("Premier :");
        var second = state.AskAsync("Second :");

        Assert.Null(await first);   // superseded, does not hang
        state.Submit("ok");
        Assert.Equal("ok", await second);
    }

    [Fact]
    public async Task AskAsync_AppliesMessageTitleLabelAndPlaceholder()
    {
        var state = new PromptState();

        var task = state.AskAsync("msg", title: "Titre", confirmLabel: "Créer", placeholder: "ex.");

        Assert.Equal("msg", state.Message);
        Assert.Equal("Titre", state.Title);
        Assert.Equal("Créer", state.ConfirmLabel);
        Assert.Equal("ex.", state.Placeholder);

        state.Cancel();
        await task;
    }
}
