using HttpForge.Components.Layout;

namespace HttpForge.Tests.Unit;

// ConfirmState bridges the synchronous confirm() call shape onto an async modal via a
// TaskCompletionSource. These lock its contract: the awaited task resolves to the user's
// choice, Visible tracks the open/closed state, and a superseded prompt never hangs.
public class ConfirmStateTests
{
    [Fact]
    public async Task Confirm_ResolvesTaskTrue_AndHides()
    {
        var state = new ConfirmState();

        var task = state.AskAsync("Supprimer ?");
        Assert.True(state.Visible);

        state.Confirm();

        Assert.True(await task);
        Assert.False(state.Visible);
    }

    [Fact]
    public async Task Cancel_ResolvesTaskFalse_AndHides()
    {
        var state = new ConfirmState();

        var task = state.AskAsync("Supprimer ?");
        state.Cancel();

        Assert.False(await task);
        Assert.False(state.Visible);
    }

    [Fact]
    public async Task AskAsync_WhileOpen_ResolvesPreviousToFalse()
    {
        var state = new ConfirmState();

        var first = state.AskAsync("Premier ?");
        var second = state.AskAsync("Second ?");

        Assert.False(await first);   // superseded, does not hang
        state.Confirm();
        Assert.True(await second);
    }

    [Fact]
    public async Task AskAsync_AppliesMessageTitleLabelAndDanger()
    {
        var state = new ConfirmState();

        var task = state.AskAsync("msg", title: "Titre", confirmLabel: "Continuer", danger: false);

        Assert.Equal("msg", state.Message);
        Assert.Equal("Titre", state.Title);
        Assert.Equal("Continuer", state.ConfirmLabel);
        Assert.False(state.Danger);

        state.Cancel();
        await task;
    }

    [Fact]
    public void AskAsync_RaisesOnChange()
    {
        var state = new ConfirmState();
        var raised = 0;
        state.OnChange = () => raised++;

        _ = state.AskAsync("Supprimer ?");
        state.Confirm();

        Assert.Equal(2, raised); // once to open, once to close
    }
}
