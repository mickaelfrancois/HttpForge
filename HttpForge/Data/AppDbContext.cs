using HttpForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<HttpRequestItem> Requests => Set<HttpRequestItem>();
    public DbSet<HeaderItem> Headers => Set<HeaderItem>();
    public DbSet<QueryParamItem> QueryParams => Set<QueryParamItem>();
    public DbSet<FormFieldItem> FormFields => Set<FormFieldItem>();
    public DbSet<AppEnvironment> Environments => Set<AppEnvironment>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<CollectionVariable> CollectionVariables => Set<CollectionVariable>();
    public DbSet<RequestVariable> RequestVariables => Set<RequestVariable>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Collection>()
            .HasMany(c => c.Requests)
            .WithOne(r => r.Collection!)
            .HasForeignKey(r => r.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Collection>()
            .HasMany(c => c.Variables)
            .WithOne()
            .HasForeignKey(v => v.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.Headers)
            .WithOne()
            .HasForeignKey(h => h.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.QueryParams)
            .WithOne()
            .HasForeignKey(q => q.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.FormFields)
            .WithOne()
            .HasForeignKey(f => f.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<HttpRequestItem>()
            .HasMany(r => r.Variables)
            .WithOne()
            .HasForeignKey(v => v.HttpRequestItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<AppEnvironment>()
            .HasMany(e => e.Variables)
            .WithOne()
            .HasForeignKey(v => v.AppEnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
