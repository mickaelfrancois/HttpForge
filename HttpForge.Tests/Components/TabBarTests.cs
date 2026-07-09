using Bunit;
using HttpForge.Components.Layout;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Models;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// slice-3 structural coverage: the TabBar renders request and collection-settings tabs
// with Kind-aware markup — a gear glyph (no method badge, no dirty marker) for the
// settings tab, the method badge for a request tab.
public class TabBarTests : BunitContext
{
    private async Task<(TabManagerService Tabs, int RequestId, int CollectionId)> ArrangeAsync()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        Services.AddSingleton<AppState>();
        Services.AddSingleton<TabManagerService>();

        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        int requestId, collectionId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            var c = new Collection { Name = "MyColl" };
            db.Collections.Add(c);
            await db.SaveChangesAsync();
            collectionId = c.Id;
            var r = new HttpRequestItem
            {
                Name = "GET /users",
                Method = HttpMethodKind.GET,
                Url = "https://example.com",
                CollectionId = c.Id
            };
            db.Requests.Add(r);
            await db.SaveChangesAsync();
            requestId = r.Id;
        }

        var tabs = Services.GetRequiredService<TabManagerService>();
        await tabs.OpenTabAsync(requestId);
        await tabs.OpenCollectionSettingsTabAsync(collectionId);
        return (tabs, requestId, collectionId);
    }

    [Fact]
    public async Task Renders_CollectionSettingsTab_WithGearGlyph_NoMethodBadge()
    {
        await ArrangeAsync();

        var cut = Render<TabBar>();

        var settingsTab = cut.Find(".tab-settings");
        Assert.Contains("MyColl", settingsTab.TextContent);
        Assert.Empty(settingsTab.QuerySelectorAll(".method-badge"));
        Assert.Empty(settingsTab.QuerySelectorAll(".tab-dirty"));
        Assert.Single(cut.FindAll(".tab-settings-icon"));
    }

    [Fact]
    public async Task Renders_RequestTab_WithMethodBadge()
    {
        await ArrangeAsync();

        var cut = Render<TabBar>();

        // The request tab is the one that is not a settings tab.
        var badges = cut.FindAll(".method-badge");
        Assert.Single(badges);
        Assert.Contains("GET", badges[0].TextContent);
    }

    [Fact]
    public async Task Renders_BothKinds_AsTwoDistinctTabs()
    {
        await ArrangeAsync();

        var cut = Render<TabBar>();

        Assert.Equal(2, cut.FindAll(".tab").Count);
        Assert.Single(cut.FindAll(".tab-settings"));
    }
}
