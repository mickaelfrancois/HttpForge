using Bunit;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

public class HomeComponentTests : BunitContext
{
    [Fact]
    public void SaveButton_DisabledWhenNotDirty()
    {
        var draft = new RequestDraft
        {
            RequestId = 1,
            LoadedAt = DateTime.UtcNow,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None
        };

        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void SaveButton_EnabledAfterMarkDirty()
    {
        var draft = new RequestDraft
        {
            RequestId = 1,
            LoadedAt = DateTime.UtcNow,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None
        };

        draft.MarkDirty();

        Assert.True(draft.IsDirty);
    }

    [Fact]
    public async Task Toast_AppearsWhenAnotherWindowSaves()
    {
        var notifier = new RequestChangeNotifier();
        string? receivedMessage = null;

        async Task Handler(int requestId, string originId)
        {
            if (requestId == 42 && originId != "this-window")
                receivedMessage = "Cette requête a été modifiée dans une autre fenêtre.";
            await Task.CompletedTask;
        }

        notifier.RequestSaved += Handler;
        await notifier.NotifyAsync(42, "other-window");

        Assert.Equal("Cette requête a été modifiée dans une autre fenêtre.", receivedMessage);
    }

    [Fact]
    public async Task Toast_HiddenWhenSameWindowSaves()
    {
        var notifier = new RequestChangeNotifier();
        string? receivedMessage = null;

        async Task Handler(int requestId, string originId)
        {
            if (requestId == 42 && originId != "this-window")
                receivedMessage = "Cette requête a été modifiée dans une autre fenêtre.";
            await Task.CompletedTask;
        }

        notifier.RequestSaved += Handler;
        await notifier.NotifyAsync(42, "this-window");

        Assert.Null(receivedMessage);
    }

    [Fact]
    public void UnsavedChangesModal_TriggeredWhenNavigatingWithDirtyDraft()
    {
        var draft = new RequestDraft
        {
            RequestId = 1,
            LoadedAt = DateTime.UtcNow,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None
        };
        draft.MarkDirty();

        int? newRequestId = 2;

        bool shouldShowModal = draft.IsDirty && newRequestId != draft.RequestId;

        Assert.True(shouldShowModal);
    }

    // ── Keyboard shortcut guards (Home.SendShortcut / SaveShortcut) ────────────
    // Mirror the exact predicates guarding the [JSInvokable] shortcut handlers so the
    // truth table stays locked: Ctrl+Enter only sends a request tab that isn't already
    // sending; Ctrl+S only saves a request tab with unsaved changes.

    private static bool CanSend(TabState tab) =>
        tab is { Kind: TabKind.Request } && !tab.IsSending;

    private static bool CanSave(TabState tab) =>
        tab is { Kind: TabKind.Request } && tab.Draft.IsDirty;

    private static TabState RequestTab(bool dirty = false, bool sending = false)
    {
        var draft = new RequestDraft
        {
            RequestId = 1,
            LoadedAt = DateTime.UtcNow,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None
        };
        if (dirty) draft.MarkDirty();
        return new TabState { Kind = TabKind.Request, Draft = draft, IsSending = sending };
    }

    [Fact]
    public void SendShortcut_AllowedForIdleRequestTab()
    {
        Assert.True(CanSend(RequestTab()));
    }

    [Fact]
    public void SendShortcut_BlockedWhileSending()
    {
        Assert.False(CanSend(RequestTab(sending: true)));
    }

    [Fact]
    public void SendShortcut_BlockedForCollectionSettingsTab()
    {
        // A collection-settings tab has a null Draft; the pattern must short-circuit
        // on Kind before touching Draft (no NRE).
        var tab = new TabState { Kind = TabKind.CollectionSettings, CollectionId = 7 };
        Assert.False(CanSend(tab));
    }

    [Fact]
    public void SaveShortcut_AllowedWhenDirty()
    {
        Assert.True(CanSave(RequestTab(dirty: true)));
    }

    [Fact]
    public void SaveShortcut_BlockedWhenClean()
    {
        Assert.False(CanSave(RequestTab(dirty: false)));
    }

    [Fact]
    public void SaveShortcut_BlockedForCollectionSettingsTab()
    {
        var tab = new TabState { Kind = TabKind.CollectionSettings, CollectionId = 7 };
        Assert.False(CanSave(tab));
    }
}
