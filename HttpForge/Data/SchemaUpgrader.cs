using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "EnvironmentVariables", "IsSecret", "INTEGER NOT NULL DEFAULT 0");
        EnsureTable(db, "CollectionVariables",
            "CREATE TABLE \"CollectionVariables\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionId\") REFERENCES \"Collections\"(\"Id\") ON DELETE CASCADE);");
        EnsureTable(db, "RequestVariables",
            "CREATE TABLE \"RequestVariables\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"HttpRequestItemId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"HttpRequestItemId\") REFERENCES \"Requests\"(\"Id\") ON DELETE CASCADE);");
    }

    private static void EnsureTable(AppDbContext db, string table, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        var count = (long)check.ExecuteScalar()!;
        if (count > 0) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        create.ExecuteNonQuery();
    }

    private static void EnsureColumn(AppDbContext db, string table, string column, string definition)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        alter.ExecuteNonQuery();
    }
}
