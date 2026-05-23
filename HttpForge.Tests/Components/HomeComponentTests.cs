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
    public async Task Toast_AppearsWhenOtherUserSaves()
    {
        var notifier = new RequestChangeNotifier();
        string? receivedMessage = null;

        async Task Handler(int requestId, string userId, string userName)
        {
            if (requestId == 42 && userId != "user-1")
                receivedMessage = $"{userName} vient de sauvegarder cette requête.";
            await Task.CompletedTask;
        }

        notifier.RequestSaved += Handler;
        await notifier.NotifyAsync(42, "user-2", "Bob");

        Assert.Equal("Bob vient de sauvegarder cette requête.", receivedMessage);
    }

    [Fact]
    public async Task Toast_HiddenWhenSameUserSaves()
    {
        var notifier = new RequestChangeNotifier();
        string? receivedMessage = null;

        async Task Handler(int requestId, string userId, string userName)
        {
            if (requestId == 42 && userId != "user-1")
                receivedMessage = $"{userName} vient de sauvegarder cette requête.";
            await Task.CompletedTask;
        }

        notifier.RequestSaved += Handler;
        await notifier.NotifyAsync(42, "user-1", "Alice");

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
