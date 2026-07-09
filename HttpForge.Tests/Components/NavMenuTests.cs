using Bunit;
using HttpForge.Components.Layout;
using HttpForge.Data;
using HttpForge.Data.Entities;
using HttpForge.Services;
using HttpForge.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Components;

// slice-4: the sidebar quick-switch <select> writes ActiveCollectionVariableSetId in one
// change (NavMenu.OnCollSubsetChanged) — a distinct control from CollectionSettings' own
// dropdown, so it needs its own coverage.
public class NavMenuTests : BunitContext
{
    [Fact]
    public async Task QuickSwitchDropdown_ChangesActiveCollectionVariableSet()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        TestAppServices.Register(Services);

        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        int collectionId, subsetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            var c = new Collection { Name = "Api" };
            db.Collections.Add(c);
            await db.SaveChangesAsync();
            collectionId = c.Id;
            var set = new CollectionVariableSet { CollectionId = collectionId, Name = "Staging", IsBase = false };
            db.CollectionVariableSets.Add(set);
            await db.SaveChangesAsync();
            subsetId = set.Id;
        }

        var cut = Render<NavMenu>();

        // The quick-switch select only renders once the collection (with its sub-set) has
        // loaded from the DB.
        cut.WaitForState(() => cut.FindAll(".subset-quick-select").Count > 0);
        cut.Find(".subset-quick-select").Change(subsetId.ToString());

        cut.WaitForAssertion(() =>
        {
            using var db = factory.CreateDbContext();
            Assert.Equal(subsetId, db.Collections.Single(c => c.Id == collectionId).ActiveCollectionVariableSetId);
        });
    }
}
