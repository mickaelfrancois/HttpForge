using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HttpForge.Tests.Integration;

public class CollectionDefaultHeaderTests : IAsyncLifetime
{
    private IDbContextFactory<AppDbContext> _factory = null!;
    private int _collectionId;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        var collection = new Collection { Name = "Test" };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        _collectionId = collection.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DefaultHeaders_PersistAndReloadWithKeyValueEnabled()
    {
        // Arrange + Act — write two headers (one disabled), then reload in a fresh context
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.CollectionDefaultHeaders.AddRange(
                new CollectionDefaultHeader { CollectionId = _collectionId, Key = "Accept", Value = "application/json", Enabled = true },
                new CollectionDefaultHeader { CollectionId = _collectionId, Key = "X-Env", Value = "test", Enabled = false });
            await db.SaveChangesAsync();
        }

        // Assert
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var headers = await db.CollectionDefaultHeaders
                .Where(h => h.CollectionId == _collectionId)
                .OrderBy(h => h.Key)
                .ToListAsync();

            Assert.Equal(2, headers.Count);
            Assert.Equal("Accept", headers[0].Key);
            Assert.Equal("application/json", headers[0].Value);
            Assert.True(headers[0].Enabled);
            Assert.Equal("X-Env", headers[1].Key);
            Assert.False(headers[1].Enabled);
        }
    }

    [Fact]
    public async Task DefaultHeaders_ReachableViaCollectionNavigation()
    {
        // Arrange
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.CollectionDefaultHeaders.Add(
                new CollectionDefaultHeader { CollectionId = _collectionId, Key = "Accept", Value = "application/json" });
            await db.SaveChangesAsync();
        }

        // Act + Assert — the same Include path Home/NavMenu use
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var collection = await db.Collections
                .Include(c => c.DefaultHeaders)
                .FirstAsync(c => c.Id == _collectionId);

            Assert.Single(collection.DefaultHeaders);
            Assert.Equal("Accept", collection.DefaultHeaders[0].Key);
        }
    }

    [Fact]
    public async Task DeletingCollection_CascadeDeletesDefaultHeaders()
    {
        // Arrange
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.CollectionDefaultHeaders.Add(
                new CollectionDefaultHeader { CollectionId = _collectionId, Key = "Accept", Value = "application/json" });
            await db.SaveChangesAsync();
        }

        // Act — remove the collection (dependents loaded so the cascade is tracked)
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var collection = await db.Collections
                .Include(c => c.DefaultHeaders)
                .FirstAsync(c => c.Id == _collectionId);
            db.Collections.Remove(collection);
            await db.SaveChangesAsync();
        }

        // Assert
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var remaining = await db.CollectionDefaultHeaders
                .CountAsync(h => h.CollectionId == _collectionId);
            Assert.Equal(0, remaining);
        }
    }
}
