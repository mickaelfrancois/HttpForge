using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public static class SchemaUpgrader
{
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "EnvironmentVariables", "IsSecret", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "AppEnvironments", "IsBase", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Collections", "ActiveCollectionVariableSetId", "INTEGER NULL");

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

        EnsureTable(db, "AppSettings",
            "CREATE TABLE \"AppSettings\" (" +
            "\"Id\" INTEGER PRIMARY KEY, " +
            "\"ActiveGlobalSubsetId\" INTEGER NULL);");

        EnsureTable(db, "CollectionVariableSets",
            "CREATE TABLE \"CollectionVariableSets\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionId\" INTEGER NOT NULL, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', " +
            "\"IsBase\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionId\") REFERENCES \"Collections\"(\"Id\") ON DELETE CASCADE);");

        EnsureTable(db, "CollectionVariableEntries",
            "CREATE TABLE \"CollectionVariableEntries\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionVariableSetId\" INTEGER NOT NULL, " +
            "\"Key\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "FOREIGN KEY (\"CollectionVariableSetId\") REFERENCES \"CollectionVariableSets\"(\"Id\") ON DELETE CASCADE);");

        EnsureGlobalBase(db);
        EnsureAppSettings(db);
        MigrateCollectionVariables(db);
    }

    private static void EnsureGlobalBase(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM \"AppEnvironments\" WHERE \"IsBase\" = 1;";
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO \"AppEnvironments\" (\"Name\", \"IsBase\") VALUES ('Base', 1);";
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

    private static void MigrateCollectionVariables(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var setCheck = conn.CreateCommand();
        setCheck.CommandText = "SELECT COUNT(*) FROM \"CollectionVariableSets\";";
        if ((long)setCheck.ExecuteScalar()! > 0) return;

        using var varCheck = conn.CreateCommand();
        varCheck.CommandText = "SELECT COUNT(*) FROM \"CollectionVariables\";";
        if ((long)varCheck.ExecuteScalar()! == 0) return;

        using var insertSets = conn.CreateCommand();
        insertSets.CommandText =
            "INSERT INTO \"CollectionVariableSets\" (\"CollectionId\", \"Name\", \"IsBase\") " +
            "SELECT DISTINCT \"CollectionId\", 'Base', 1 FROM \"CollectionVariables\";";
        insertSets.ExecuteNonQuery();

        using var insertEntries = conn.CreateCommand();
        insertEntries.CommandText =
            "INSERT INTO \"CollectionVariableEntries\" (\"CollectionVariableSetId\", \"Key\", \"Value\", \"IsSecret\") " +
            "SELECT cvs.\"Id\", cv.\"Key\", cv.\"Value\", cv.\"IsSecret\" " +
            "FROM \"CollectionVariables\" cv " +
            "JOIN \"CollectionVariableSets\" cvs ON cvs.\"CollectionId\" = cv.\"CollectionId\" AND cvs.\"IsBase\" = 1;";
        insertEntries.ExecuteNonQuery();
    }

    private static void EnsureTable(AppDbContext db, string table, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        if ((long)check.ExecuteScalar()! > 0) return;

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

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        alter.ExecuteNonQuery();
    }
}
