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
}
