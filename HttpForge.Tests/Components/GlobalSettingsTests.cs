using Bunit;
using HttpForge.Components.Pages;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// Exercises the global-variables tab (the slice that promoted the old cramped sidebar
// editor into a proper tab) against a real in-memory DbContextFactory, so its DB-write
// logic — base vars, secrets, sub-sets — mirrors the CollectionSettings net. The singleton
// AppSettings row and the Base environment are seeded manually because DbSeeder uses raw
// SQLite SQL that does not run on the in-memory provider.
public class GlobalSettingsTests : BunitContext
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly int _baseEnvId;

    public GlobalSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        Services.AddSingleton<AppState>();

        _factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Settings.Add(new AppSettings { Id = 1, ActiveGlobalSubsetId = null });
        var baseEnv = new AppEnvironment { Name = "Base", IsBase = true };
        db.Environments.Add(baseEnv);
        db.SaveChanges();
        _baseEnvId = baseEnv.Id;
    }

    private IRenderedComponent<GlobalSettings> RenderTab() => Render<GlobalSettings>();

    private AppDbContext Db() => _factory.CreateDbContext();

    private void AddBaseVar(IRenderedComponent<GlobalSettings> cut) =>
        cut.FindAll("button.link-btn")
            .Single(b => b.TextContent.Contains("ajouter une variable"))
            .Click();

    private void CreateSubsetViaPrompt(IRenderedComponent<GlobalSettings> cut, string name)
    {
        cut.Find("button[title='Nouveau sous-ensemble']").Click();
        cut.WaitForElement(".prompt-input").Input(name);
        cut.Find(".prompt-primary").Click();
        cut.WaitForState(() =>
        {
            using var db = Db();
            return db.Environments.Any(e => !e.IsBase);
        });
    }

    [Fact]
    public void AddBaseVar_CreatesEntryOnBaseEnv()
    {
        var cut = RenderTab();

        AddBaseVar(cut);

        using var db = Db();
        var entry = db.EnvironmentVariables.Single();
        Assert.Equal(_baseEnvId, entry.AppEnvironmentId);
    }

    [Fact]
    public void EditEntryKey_Persists()
    {
        var cut = RenderTab();
        AddBaseVar(cut);

        // Persists on blur (mirrors KeyValueGrid / VariableSetEditor), not per keystroke.
        var keyInput = cut.Find(".var-row input");
        keyInput.Input("baseUrl");
        keyInput.Blur();

        using var db = Db();
        Assert.Equal("baseUrl", db.EnvironmentVariables.Single().Key);
    }

    [Fact]
    public void ToggleSecret_Persists()
    {
        var cut = RenderTab();
        AddBaseVar(cut);

        cut.Find("button[title='Marquer comme secret']").Click();

        using var db = Db();
        Assert.True(db.EnvironmentVariables.Single().IsSecret);
    }

    [Fact]
    public void RemoveVar_Confirmed_DeletesIt()
    {
        var cut = RenderTab();
        AddBaseVar(cut);

        cut.Find(".var-row").QuerySelectorAll("button").Last().Click();
        cut.WaitForElement(".confirm-actions button.btn-danger").Click();

        cut.WaitForAssertion(() =>
        {
            using var db = Db();
            Assert.Empty(db.EnvironmentVariables);
        });
    }

    [Fact]
    public void AddSubset_ViaPrompt_CreatesAndActivates()
    {
        var cut = RenderTab();

        CreateSubsetViaPrompt(cut, "Staging");

        using var db = Db();
        var subset = db.Environments.Single(e => !e.IsBase);
        Assert.Equal("Staging", subset.Name);
        Assert.Equal(subset.Id, db.Settings.Single(s => s.Id == 1).ActiveGlobalSubsetId);
    }

    [Fact]
    public void AddSubsetVar_AddsToActiveSubset()
    {
        var cut = RenderTab();
        CreateSubsetViaPrompt(cut, "Staging");

        // The active sub-set's "+ ajouter une variable" is the second such link.
        cut.WaitForElement(".vse-subset-block .link-btn").Click();

        using var db = Db();
        var subset = db.Environments.Single(e => !e.IsBase);
        Assert.Single(db.EnvironmentVariables.Where(v => v.AppEnvironmentId == subset.Id));
    }

    [Fact]
    public void DeleteSubset_RemovesAndClearsActive()
    {
        var cut = RenderTab();
        CreateSubsetViaPrompt(cut, "Staging");

        cut.WaitForElement("button[title='Supprimer le sous-ensemble']").Click();
        cut.WaitForElement(".confirm-actions button.btn-danger").Click();

        cut.WaitForAssertion(() =>
        {
            using var db = Db();
            Assert.Empty(db.Environments.Where(e => !e.IsBase));
            Assert.Null(db.Settings.Single(s => s.Id == 1).ActiveGlobalSubsetId);
        });
    }
}
