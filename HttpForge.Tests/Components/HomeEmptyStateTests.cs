using Bunit;
using HttpForge.Components.Pages;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// The empty state (no active tab) is actionable: it creates/opens a request, pastes a cURL
// cold, and asks the sidebar to open its import menu. JS interop runs in Loose mode.
public class HomeEmptyStateTests : BunitContext
{
    private IDbContextFactory<AppDbContext> Arrange()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        TestAppServices.Register(Services);
        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        return factory;
    }

    [Fact]
    public void EmptyState_ShowsActions_WhenNoTabActive()
    {
        Arrange();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-action")));
        Assert.Contains("Créez votre première", cut.Find(".empty-state p").TextContent);
    }

    [Fact]
    public void EmptyState_Message_ReflectsExistingCollections()
    {
        var factory = Arrange();
        using (var db = factory.CreateDbContext())
        {
            db.Collections.Add(new Collection { Name = "Api" });
            db.SaveChanges();
        }

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
            Assert.Contains("Sélectionnez", cut.Find(".empty-state p").TextContent));
    }

    [Fact]
    public void EmptyState_NewRequest_CreatesCollectionAndRequest_AndOpensTab()
    {
        var factory = Arrange();
        var tabs = Services.GetRequiredService<TabManagerService>();

        var cut = Render<Home>();
        cut.WaitForElement(".empty-action--primary").Click();

        cut.WaitForAssertion(() =>
        {
            using var db = factory.CreateDbContext();
            Assert.Single(db.Collections);   // created "Ma collection" (none existed)
            Assert.Single(db.Requests);
        });
        Assert.NotEmpty(tabs.Tabs);
    }

    [Fact]
    public void EmptyState_PasteCurl_CreatesRequestAndAppliesCommand()
    {
        var factory = Arrange();
        var tabs = Services.GetRequiredService<TabManagerService>();

        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-action")));

        // Open the paste dialog, enter a command, import.
        cut.FindAll(".empty-action").Single(b => b.TextContent.Contains("Coller")).Click();
        cut.WaitForElement(".curl-dialog-input")
            .Input("curl -X POST https://api.example.com/login -H 'Accept: application/json' -d '{\"a\":1}'");
        cut.Find(".curl-dialog-primary").Click();

        // A blank request was created and opened; the draft reflects the parsed command
        // (autosave is disabled in tests, so the DB row stays the blank default).
        cut.WaitForAssertion(() =>
        {
            var tab = tabs.ActiveTab;
            Assert.NotNull(tab);
            Assert.Equal(HttpMethodKind.POST, tab!.Draft.Method);
            Assert.Equal("https://api.example.com/login", tab.Draft.Url);
            Assert.Equal(BodyKind.Json, tab.Draft.BodyKind);
        });
    }

    [Fact]
    public void RequestOpenImport_RaisesEvent()
    {
        var state = new AppState();
        var raised = false;
        state.OpenImportRequested += () => raised = true;

        state.RequestOpenImport();

        Assert.True(raised);
    }
}
