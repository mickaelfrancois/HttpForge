using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

// Seeds the rows the app always needs on a fresh database: the singleton AppSettings
// row and the global "Base" environment. The schema itself is owned by EF Core
// migrations (db.Database.Migrate()); this only inserts seed data, and is idempotent.
public static class DbSeeder
{
    public static void Apply(AppDbContext db)
    {
        EnsureGlobalBase(db);
        EnsureAppSettings(db);
    }

    private static void EnsureGlobalBase(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM \"Environments\" WHERE \"IsBase\" = 1;";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO \"Environments\" (\"Name\", \"IsBase\") VALUES ('Base', 1);";
        insert.ExecuteNonQuery();
    }

    private static void EnsureAppSettings(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM \"AppSettings\" WHERE \"Id\" = 1;";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO \"AppSettings\" (\"Id\", \"ActiveGlobalSubsetId\") VALUES (1, NULL);";
        insert.ExecuteNonQuery();
    }
}
