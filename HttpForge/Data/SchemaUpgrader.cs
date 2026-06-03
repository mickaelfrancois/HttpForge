using Microsoft.EntityFrameworkCore;

namespace HttpForge.Data;

public static class SchemaUpgrader
{
    private static readonly HashSet<string> _allowedTables =
    [
        "Collections", "Environments", "EnvironmentVariables",
        "CollectionVariables", "RequestVariables", "AppSettings",
        "CollectionVariableSets", "CollectionVariableEntries", "Requests",
        "CollectionFolders", "Teams", "TeamMembers", "InvitationTokens",
        "UserVariableValues"
    ];
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "Requests", "PostScript", "TEXT NULL");
        EnsureColumn(db, "Requests", "PostScriptTrusted", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(db, "Requests", "IgnoreTlsErrors", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "EnvironmentVariables", "IsSecret", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Environments", "IsBase", "INTEGER NOT NULL DEFAULT 0");
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

        EnsureTable(db, "CollectionFolders",
            "CREATE TABLE \"CollectionFolders\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"CollectionId\" INTEGER NOT NULL, " +
            "\"ParentFolderId\" INTEGER NULL, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', " +
            "FOREIGN KEY (\"CollectionId\") REFERENCES \"Collections\"(\"Id\") ON DELETE CASCADE);");

        EnsureColumn(db, "Requests", "FolderId", "INTEGER NULL");
        EnsureColumn(db, "Requests", "UpdatedByUserId", "TEXT NULL");

        EnsureColumn(db, "Collections", "TeamId", "INTEGER NULL");

        EnsureTable(db, "Teams",
            "CREATE TABLE \"Teams\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', " +
            "\"CreatedAt\" TEXT NOT NULL DEFAULT (datetime('now')));");

        EnsureTable(db, "TeamMembers",
            "CREATE TABLE \"TeamMembers\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"TeamId\" INTEGER NOT NULL, " +
            "\"UserId\" TEXT NOT NULL DEFAULT '', " +
            "\"Role\" INTEGER NOT NULL DEFAULT 1, " +
            "UNIQUE (\"TeamId\", \"UserId\"), " +
            "FOREIGN KEY (\"TeamId\") REFERENCES \"Teams\"(\"Id\") ON DELETE CASCADE);");

        EnsureTable(db, "InvitationTokens",
            "CREATE TABLE \"InvitationTokens\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"TeamId\" INTEGER NULL, " +
            "\"Email\" TEXT NOT NULL DEFAULT '', " +
            "\"Role\" TEXT NOT NULL DEFAULT '', " +
            "\"Token\" TEXT NOT NULL DEFAULT '', " +
            "\"ExpiresAt\" TEXT NOT NULL DEFAULT (datetime('now')), " +
            "\"UsedAt\" TEXT NULL);");

        EnsureColumn(db, "InvitationTokens", "Email", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "InvitationTokens", "Role", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "InvitationTokens", "Token", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "InvitationTokens", "ExpiresAt", "TEXT NOT NULL DEFAULT (datetime('now'))");
        EnsureColumn(db, "InvitationTokens", "UsedAt", "TEXT NULL");

        EnsureTable(db, "UserVariableValues",
            "CREATE TABLE \"UserVariableValues\" (" +
            "\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "\"UserId\" TEXT NOT NULL DEFAULT '', " +
            "\"ScopeType\" TEXT NOT NULL DEFAULT '', " +
            "\"ScopeId\" INTEGER NOT NULL DEFAULT 0, " +
            "\"VariableKey\" TEXT NOT NULL DEFAULT '', " +
            "\"Value\" TEXT NOT NULL DEFAULT '', " +
            "\"IsSecret\" INTEGER NOT NULL DEFAULT 0, " +
            "UNIQUE (\"UserId\", \"ScopeType\", \"ScopeId\", \"VariableKey\"));");

        EnsureGlobalBase(db);
        EnsureAppSettings(db);
        MigrateCollectionVariables(db);
    }

    private static void EnsureGlobalBase(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var tableCheck = conn.CreateCommand();
        tableCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Environments';";
        if ((long)tableCheck.ExecuteScalar()! == 0) return;

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

        using var tableCheck = conn.CreateCommand();
        tableCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AppSettings';";
        if ((long)tableCheck.ExecuteScalar()! == 0) return;

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

        using var tx = conn.BeginTransaction();
        using var insertSets = conn.CreateCommand();
        insertSets.Transaction = tx;
        insertSets.CommandText =
            "INSERT INTO \"CollectionVariableSets\" (\"CollectionId\", \"Name\", \"IsBase\") " +
            "SELECT DISTINCT \"CollectionId\", 'Base', 1 FROM \"CollectionVariables\";";
        insertSets.ExecuteNonQuery();

        using var insertEntries = conn.CreateCommand();
        insertEntries.Transaction = tx;
        insertEntries.CommandText =
            "INSERT INTO \"CollectionVariableEntries\" (\"CollectionVariableSetId\", \"Key\", \"Value\", \"IsSecret\") " +
            "SELECT cvs.\"Id\", cv.\"Key\", cv.\"Value\", cv.\"IsSecret\" " +
            "FROM \"CollectionVariables\" cv " +
            "JOIN \"CollectionVariableSets\" cvs ON cvs.\"CollectionId\" = cv.\"CollectionId\" AND cvs.\"IsBase\" = 1;";
        insertEntries.ExecuteNonQuery();
        tx.Commit();
    }

    private static void EnsureTable(AppDbContext db, string table, string createSql)
    {
        if (!_allowedTables.Contains(table))
            throw new ArgumentException($"Unknown table '{table}'");

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
        if (!_allowedTables.Contains(table))
            throw new ArgumentException($"Unknown table '{table}'");

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        // If the table doesn't exist yet (fresh DB, EF Core will create it), skip —
        // the column will be present in the CREATE TABLE statement from migrations.
        using var tableCheck = conn.CreateCommand();
        tableCheck.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        if ((long)tableCheck.ExecuteScalar()! == 0) return;

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
