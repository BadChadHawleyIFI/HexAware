using HexContracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HexAware.Tests;

public class DuplicateFunctionIdTests
{
    [Fact]
    public void FunctionIds_AreUniqueAcrossOverloadsWithinTheSameType()
    {
        var first = FunctionId.Create("Namespace.Type", "DoWork", new[] { "int", "string" });
        var second = FunctionId.Create("Namespace.Type", "DoWork", new[] { "string", "int" });
        var same = FunctionId.Create("Namespace.Type", "DoWork", new[] { "int", "string" });

        Assert.NotEqual(first, second);
        Assert.Equal(first, same);
    }

    [Fact]
    public void SqliteWriter_IgnoresDuplicateLogicalPartialDefinitions()
    {
        var cache = new StructuralCache
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            GitCommitHash = "abc123",
        };

        cache.Files["A.cs"] = new FileStructuralResult
        {
            Language = "csharp",
            Functions =
            {
                new FunctionInfo { Id = FunctionId.Create("Namespace.Type", "DoWork", new[] { "int" }), Name = "DoWork", LineRange = new[] { 10, 20 }, Params = new() { "int x" } },
                new FunctionInfo { Id = FunctionId.Create("Namespace.Type", "DoWork", new[] { "int" }), Name = "DoWork", LineRange = new[] { 30, 40 }, Params = new() { "int x" } },
            },
            Classes =
            {
                new ClassInfo { Id = "Namespace.Type", Name = "Type", LineRange = new[] { 1, 50 }, Methods = new() { "DoWork" }, Properties = new() },
                new ClassInfo { Id = "Namespace.Type", Name = "Type", LineRange = new[] { 1, 50 }, Methods = new() { "DoWork" }, Properties = new() },
            },
        };

        cache.Files["B.cs"] = new FileStructuralResult
        {
            Language = "csharp",
            Functions =
            {
                new FunctionInfo { Id = FunctionId.Create("Namespace.Type", "DoWork", new[] { "int" }), Name = "DoWork", LineRange = new[] { 60, 70 }, Params = new() { "int x" } },
            },
            Classes =
            {
                new ClassInfo { Id = "Namespace.Type", Name = "Type", LineRange = new[] { 1, 50 }, Methods = new() { "DoWork" }, Properties = new() },
            },
        };

        var dbPath = Path.Combine(Path.GetTempPath(), $"hexaware-{Guid.NewGuid():N}.db");
        try
        {
            SqliteCacheStore.WriteAll(cache, dbPath);
            var count = 0;
            using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Functions UNION ALL SELECT COUNT(*) FROM Classes";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) count += reader.GetInt32(0);
            }

            Assert.Equal(1 + 1, count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
