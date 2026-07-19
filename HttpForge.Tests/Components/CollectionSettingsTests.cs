using Bunit;
using HttpForge.Components.Pages;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// Exercises the CollectionSettings tab against a real in-memory DbContextFactory, so the
// new tab's DB-write logic (variables, secrets, sub-sets, default headers) has a net —
// the slice-2 matrix from plan.md. JS interop runs in Loose mode; `prompt` is stubbed where
// a handler depends on its return value. Deletions now confirm through the ConfirmDialog
// modal (no native confirm()), so those tests click the modal's button.
public class CollectionSettingsTests : BunitContext
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly int _collectionId;

    public CollectionSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        Services.AddSingleton<AppState>();

        _factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        var c = new Collection { Name = "Test" };
        db.Collections.Add(c);
        db.SaveChanges();
        _collectionId = c.Id;
    }

    private IRenderedComponent<CollectionSettings> RenderTab()
        => Render<CollectionSettings>(ps => ps.Add(c => c.CollectionId, _collectionId));

    private AppDbContext Db() => _factory.CreateDbContext();

    [Fact]
    public void Variables_AddBaseEntry_CreatesLazyBaseSetAndEntry()
    {
        var cut = RenderTab();

        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        using var db = Db();
        var sets = db.CollectionVariableSets.Include(s => s.Entries)
            .Where(s => s.CollectionId == _collectionId).ToList();
        Assert.Single(sets);
        Assert.True(sets[0].IsBase);
        Assert.Single(sets[0].Entries);
    }

    [Fact]
    public void Variables_EditEntryKey_Persists()
    {
        var cut = RenderTab();
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        cut.Find(".var-row input").Input("token");

        using var db = Db();
        var entry = db.CollectionVariableEntries.Single();
        Assert.Equal("token", entry.Key);
    }

    [Fact]
    public void Variables_ToggleSecret_Persists()
    {
        var cut = RenderTab();
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        cut.Find("button[title='Mark as secret']").Click();

        using var db = Db();
        Assert.True(db.CollectionVariableEntries.Single().IsSecret);
    }

    [Fact]
    public void Variables_EditEntryValue_Persists()
    {
        var cut = RenderTab();
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        cut.Find(".var-row input[placeholder='value']").Input("secret123");

        using var db = Db();
        Assert.Equal("secret123", db.CollectionVariableEntries.Single().Value);
    }

    [Fact]
    public void Variables_RemoveBaseEntry_DeletesIt()
    {
        var cut = RenderTab();
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        // The ✕ delete button is the last button in the row.
        cut.Find(".var-row").QuerySelectorAll("button").Last().Click();
        // Confirm in the ConfirmDialog (replaces the old native confirm()).
        cut.WaitForElement(".confirm-actions button.btn-danger").Click();

        cut.WaitForAssertion(() =>
        {
            using var db = Db();
            Assert.Empty(db.CollectionVariableEntries);
        });
    }

    [Fact]
    public void Variables_RemoveBaseEntry_Cancelled_KeepsIt()
    {
        var cut = RenderTab();
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("add collection variable"))
            .Click();

        cut.Find(".var-row").QuerySelectorAll("button").Last().Click();
        // Dismiss the confirmation — the entry must survive.
        cut.WaitForElement(".confirm-actions button:not(.btn-danger)").Click();

        using var db = Db();
        Assert.Single(db.CollectionVariableEntries);
    }

    [Fact]
    public void Variables_AddSubset_ViaPrompt_CreatesAndActivates()
    {
        JSInterop.Setup<string?>("prompt", _ => true).SetResult("Staging");
        var cut = RenderTab();

        cut.Find("button[title='New sub-set']").Click();

        using var db = Db();
        var subset = db.CollectionVariableSets.Single(s => !s.IsBase && s.CollectionId == _collectionId);
        Assert.Equal("Staging", subset.Name);
        var col = db.Collections.Single(c => c.Id == _collectionId);
        Assert.Equal(subset.Id, col.ActiveCollectionVariableSetId);
    }

    [Fact]
    public void Variables_ChangeSubsetDropdown_ToNone_ClearsActiveSet()
    {
        JSInterop.Setup<string?>("prompt", _ => true).SetResult("Staging");
        var cut = RenderTab();
        cut.Find("button[title='New sub-set']").Click();

        cut.Find(".cs-row select").Change("");

        using var db = Db();
        Assert.Null(db.Collections.Single(c => c.Id == _collectionId).ActiveCollectionVariableSetId);
    }

    [Fact]
    public void Variables_DeleteSubset_RemovesAndClearsActive()
    {
        JSInterop.Setup<string?>("prompt", _ => true).SetResult("Staging");
        var cut = RenderTab();
        cut.Find("button[title='New sub-set']").Click();

        cut.Find("button[title='Delete sub-set']").Click();
        cut.WaitForElement(".confirm-actions button.btn-danger").Click();

        cut.WaitForAssertion(() =>
        {
            using var db = Db();
            Assert.Empty(db.CollectionVariableSets.Where(s => !s.IsBase && s.CollectionId == _collectionId));
            Assert.Null(db.Collections.Single(c => c.Id == _collectionId).ActiveCollectionVariableSetId);
        });
    }

    [Fact]
    public void Headers_AddRow_PersistsDefaultHeader()
    {
        var cut = RenderTab();
        // Switch to the "Headers par défaut" sub-section (second tab), then add a row.
        cut.FindAll(".cs-tab")[1].Click();
        cut.Find("button.add-row").Click();

        using var db = Db();
        Assert.Single(db.CollectionDefaultHeaders.Where(h => h.CollectionId == _collectionId));
    }

    [Fact]
    public void Headers_EditKeyAndValue_Persists()
    {
        var cut = RenderTab();
        cut.FindAll(".cs-tab")[1].Click();
        cut.Find("button.add-row").Click();
        cut.WaitForState(() => cut.FindAll("input[placeholder='key']").Count > 0);

        var keyInput = cut.Find("input[placeholder='key']");
        keyInput.Input("Accept");
        keyInput.Blur();
        var valueInput = cut.Find("input[placeholder='value']");
        valueInput.Input("application/json");
        valueInput.Blur();

        using var db = Db();
        var header = db.CollectionDefaultHeaders.Single(h => h.CollectionId == _collectionId);
        Assert.Equal("Accept", header.Key);
        Assert.Equal("application/json", header.Value);
    }

    [Fact]
    public void Headers_ToggleEnabled_Persists()
    {
        var cut = RenderTab();
        cut.FindAll(".cs-tab")[1].Click();
        cut.Find("button.add-row").Click();
        cut.WaitForState(() => cut.FindAll("input[type='checkbox']").Count > 0);

        // Added rows default to Enabled=true; toggling the checkbox turns it off.
        cut.Find("input[type='checkbox']").Change(false);

        using var db = Db();
        Assert.False(db.CollectionDefaultHeaders.Single(h => h.CollectionId == _collectionId).Enabled);
    }

    [Fact]
    public void Headers_RemoveRow_DeletesDefaultHeader()
    {
        var cut = RenderTab();
        cut.FindAll(".cs-tab")[1].Click();
        cut.Find("button.add-row").Click();
        cut.WaitForState(() => cut.FindAll("button.row-del").Count > 0);

        cut.Find("button.row-del").Click();

        using var db = Db();
        Assert.Empty(db.CollectionDefaultHeaders.Where(h => h.CollectionId == _collectionId));
    }
}
