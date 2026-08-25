using Microsoft.Data.Sqlite;

namespace HexContracts;

/// <summary>
/// SQLite schema for the structural cache. Single source of truth for table/column names shared between
/// Generate (writer, via <see cref="SqliteCacheStore"/>) and Query (reader, direct SqliteCommand text).
/// List-valued leaf fields that are never independently filtered/joined (function params, class
/// methods/properties) are stored as JSON-encoded TEXT rather than child tables — they're always returned
/// wholesale, so normalizing them would only add joins for no query benefit. Imports/Exports are dropped
/// entirely: nothing in Generate populates them.
/// </summary>
public static class CacheSchema
{
    public const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS Meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Files (
            path TEXT PRIMARY KEY,
            language TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Functions (
            id TEXT PRIMARY KEY,
            file TEXT NOT NULL,
            name TEXT NOT NULL,
            lineStart INTEGER NOT NULL,
            lineEnd INTEGER NOT NULL,
            returnType TEXT,
            paramsJson TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Variables (
            file TEXT NOT NULL,
            name TEXT NOT NULL,
            lineStart INTEGER NOT NULL,
            lineEnd INTEGER NOT NULL,
            kind TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Classes (
            id TEXT PRIMARY KEY,
            file TEXT NOT NULL,
            name TEXT NOT NULL,
            lineStart INTEGER NOT NULL,
            lineEnd INTEGER NOT NULL,
            methodsJson TEXT NOT NULL,
            propertiesJson TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Sections (
            file TEXT NOT NULL,
            name TEXT NOT NULL,
            level INTEGER NOT NULL,
            lineStart INTEGER NOT NULL,
            lineEnd INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS CallGraph (
            file TEXT NOT NULL,
            caller TEXT NOT NULL,
            callee TEXT NOT NULL,
            lineNumber INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ReferenceEdges (
            file TEXT NOT NULL,
            source TEXT NOT NULL,
            target TEXT NOT NULL,
            referenceType TEXT NOT NULL,
            line INTEGER
        );

        CREATE INDEX IF NOT EXISTS idx_functions_name ON Functions(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_functions_file ON Functions(file);
        CREATE INDEX IF NOT EXISTS idx_variables_name ON Variables(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_variables_file ON Variables(file);
        CREATE INDEX IF NOT EXISTS idx_classes_name ON Classes(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_classes_file ON Classes(file);
        CREATE INDEX IF NOT EXISTS idx_sections_name ON Sections(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_sections_file ON Sections(file);
        CREATE INDEX IF NOT EXISTS idx_callgraph_caller ON CallGraph(caller);
        CREATE INDEX IF NOT EXISTS idx_callgraph_callee ON CallGraph(callee);
        CREATE INDEX IF NOT EXISTS idx_refs_source ON ReferenceEdges(source);
        CREATE INDEX IF NOT EXISTS idx_refs_target ON ReferenceEdges(target);
        """;
}

/// <summary>
/// Reads/writes the FULL <see cref="StructuralCache"/> object graph to/from a SQLite file. Used only by
/// Generate: once to reconstruct <c>existingCache</c> for its incremental mtime-reuse pass, and once to
/// persist the freshly-computed cache at the end of every run (a full rewrite each time, same cadence as
/// the JSON file it replaces — Generate runs rarely, so this was never the cost this migration targets).
/// Query never uses this class; it issues its own targeted, indexed SqliteCommand queries instead of
/// reconstructing the whole graph, since Query is the thing invoked repeatedly per AI session.
/// </summary>
public static class SqliteCacheStore
{
    public static StructuralCache? ReadAll(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();

        var cache = new StructuralCache();
        using (var metaCmd = connection.CreateCommand())
        {
            metaCmd.CommandText = "SELECT key, value FROM Meta";
            using var reader = metaCmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);
                if (key == "generatedAt") cache.GeneratedAt = DateTimeOffset.Parse(value);
                else if (key == "gitCommitHash") cache.GitCommitHash = value;
            }
        }

        using (var filesCmd = connection.CreateCommand())
        {
            filesCmd.CommandText = "SELECT path, language FROM Files";
            using var reader = filesCmd.ExecuteReader();
            while (reader.Read())
                cache.Files[reader.GetString(0)] = new FileStructuralResult { Language = reader.GetString(1) };
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, file, name, lineStart, lineEnd, returnType, paramsJson FROM Functions";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(1);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.Functions.Add(new FunctionInfo
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(2),
                    LineRange = new[] { reader.GetInt32(3), reader.GetInt32(4) },
                    ReturnType = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Params = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new(),
                });
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT file, name, lineStart, lineEnd, kind FROM Variables";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(0);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.Variables.Add(new VariableInfo
                {
                    Name = reader.GetString(1),
                    LineRange = new[] { reader.GetInt32(2), reader.GetInt32(3) },
                    Kind = reader.GetString(4),
                });
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, file, name, lineStart, lineEnd, methodsJson, propertiesJson FROM Classes";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(1);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.Classes.Add(new ClassInfo
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(2),
                    LineRange = new[] { reader.GetInt32(3), reader.GetInt32(4) },
                    Methods = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? new(),
                    Properties = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new(),
                });
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT file, name, level, lineStart, lineEnd FROM Sections";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(0);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.Sections.Add(new SectionInfo
                {
                    Name = reader.GetString(1),
                    Level = reader.GetInt32(2),
                    LineRange = new[] { reader.GetInt32(3), reader.GetInt32(4) },
                });
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT file, caller, callee, lineNumber FROM CallGraph";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(0);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.CallGraph.Add(new CallGraphEntry
                {
                    Caller = reader.GetString(1),
                    Callee = reader.GetString(2),
                    LineNumber = reader.GetInt32(3),
                });
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT file, source, target, referenceType, line FROM ReferenceEdges";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var file = reader.GetString(0);
                if (!cache.Files.TryGetValue(file, out var fileResult)) continue;
                fileResult.References.Add(new ReferenceResolution
                {
                    Source = reader.GetString(1),
                    Target = reader.GetString(2),
                    ReferenceType = reader.GetString(3),
                    Line = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                });
            }
        }

        return cache;
    }

    public static void WriteAll(StructuralCache cache, string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Microsoft.Data.Sqlite pools connections by default — a prior ReadAll's connection can still hold
        // the OS file handle open even after being disposed, which would make this delete fail. Clearing
        // all pools first releases those handles before the fresh full write below.
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath); // fresh full write every run, same cadence as the JSON file it replaces

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CacheSchema.CreateTablesSql;
            schemaCmd.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();

        void Exec(string sql, Action<SqliteCommand> bind)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            bind(cmd);
            cmd.ExecuteNonQuery();
        }

        Exec("INSERT INTO Meta (key, value) VALUES ('generatedAt', @v)", c => c.Parameters.AddWithValue("@v", cache.GeneratedAt.ToString("o")));
        Exec("INSERT INTO Meta (key, value) VALUES ('gitCommitHash', @v)", c => c.Parameters.AddWithValue("@v", cache.GitCommitHash));

        foreach (var (path, file) in cache.Files)
        {
            Exec("INSERT INTO Files (path, language) VALUES (@path, @language)", c =>
            {
                c.Parameters.AddWithValue("@path", path);
                c.Parameters.AddWithValue("@language", file.Language);
            });

            foreach (var fn in file.Functions)
            {
                Exec("INSERT INTO Functions (id, file, name, lineStart, lineEnd, returnType, paramsJson) VALUES (@id, @file, @name, @s, @e, @rt, @params)", c =>
                {
                    c.Parameters.AddWithValue("@id", fn.Id);
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@name", fn.Name);
                    c.Parameters.AddWithValue("@s", fn.LineRange[0]);
                    c.Parameters.AddWithValue("@e", fn.LineRange[1]);
                    c.Parameters.AddWithValue("@rt", (object?)fn.ReturnType ?? DBNull.Value);
                    c.Parameters.AddWithValue("@params", System.Text.Json.JsonSerializer.Serialize(fn.Params));
                });
            }

            foreach (var v in file.Variables)
            {
                Exec("INSERT INTO Variables (file, name, lineStart, lineEnd, kind) VALUES (@file, @name, @s, @e, @kind)", c =>
                {
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@name", v.Name);
                    c.Parameters.AddWithValue("@s", v.LineRange[0]);
                    c.Parameters.AddWithValue("@e", v.LineRange[1]);
                    c.Parameters.AddWithValue("@kind", v.Kind);
                });
            }

            foreach (var cls in file.Classes)
            {
                Exec("INSERT INTO Classes (id, file, name, lineStart, lineEnd, methodsJson, propertiesJson) VALUES (@id, @file, @name, @s, @e, @m, @p)", c =>
                {
                    c.Parameters.AddWithValue("@id", cls.Id);
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@name", cls.Name);
                    c.Parameters.AddWithValue("@s", cls.LineRange[0]);
                    c.Parameters.AddWithValue("@e", cls.LineRange[1]);
                    c.Parameters.AddWithValue("@m", System.Text.Json.JsonSerializer.Serialize(cls.Methods));
                    c.Parameters.AddWithValue("@p", System.Text.Json.JsonSerializer.Serialize(cls.Properties));
                });
            }

            foreach (var s in file.Sections)
            {
                Exec("INSERT INTO Sections (file, name, level, lineStart, lineEnd) VALUES (@file, @name, @level, @s, @e)", c =>
                {
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@name", s.Name);
                    c.Parameters.AddWithValue("@level", s.Level);
                    c.Parameters.AddWithValue("@s", s.LineRange[0]);
                    c.Parameters.AddWithValue("@e", s.LineRange[1]);
                });
            }

            foreach (var cg in file.CallGraph)
            {
                Exec("INSERT INTO CallGraph (file, caller, callee, lineNumber) VALUES (@file, @caller, @callee, @line)", c =>
                {
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@caller", cg.Caller);
                    c.Parameters.AddWithValue("@callee", cg.Callee);
                    c.Parameters.AddWithValue("@line", cg.LineNumber);
                });
            }

            foreach (var r in file.References)
            {
                Exec("INSERT INTO ReferenceEdges (file, source, target, referenceType, line) VALUES (@file, @src, @tgt, @rt, @line)", c =>
                {
                    c.Parameters.AddWithValue("@file", path);
                    c.Parameters.AddWithValue("@src", r.Source);
                    c.Parameters.AddWithValue("@tgt", r.Target);
                    c.Parameters.AddWithValue("@rt", r.ReferenceType);
                    c.Parameters.AddWithValue("@line", (object?)r.Line ?? DBNull.Value);
                });
            }
        }

        transaction.Commit();
    }
}
