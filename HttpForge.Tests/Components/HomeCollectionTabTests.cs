using Bunit;
using HttpForge.Components.Pages;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// slice-3: Home branches on the active tab's Kind. With a CollectionSettings tab active,
// it renders the CollectionSettings component (not the request editor) and
// LoadActiveTabContextAsync points AppState at the collection, not a request.
public class HomeCollectionTabTests : BunitContext
{
    private async Task<(TabManagerService Tabs, AppState State, int CollectionId)> ArrangeActiveCollectionTabAsync()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        TestAppServices.Register(Services);

        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        int collectionId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            var c = new Collection { Name = "Api" };
            db.Collections.Add(c);
            await db.SaveChangesAsync();
            collectionId = c.Id;
        }

        var tabs = Services.GetRequiredService<TabManagerService>();
        await tabs.OpenCollectionSettingsTabAsync(collectionId);
        return (tabs, Services.GetRequiredService<AppState>(), collectionId);
    }

    [Fact]
    public async Task Home_RendersCollectionSettings_WhenActiveTabIsCollectionKind()
    {
        await ArrangeActiveCollectionTabAsync();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<CollectionSettings>()));
        // The request editor must NOT be rendered for a collection tab.
        Assert.Empty(cut.FindAll(".request-name-input"));
    }

    [Fact]
    public async Task Home_CollectionTab_SetsSelectedCollectionId_NotSelectedRequestId()
    {
        var (_, state, collectionId) = await ArrangeActiveCollectionTabAsync();

        var cut = Render<Home>();

        // LoadActiveTabContextAsync (OnAfterRenderAsync) points AppState at the collection,
        // not a request.
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(collectionId, state.SelectedCollectionId);
            Assert.Null(state.SelectedRequestId);
        });
    }
}
