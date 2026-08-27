using HexContracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HexAware.Tests;

public class EnhancedCacheTests
{
    private static string NewTempDbPath() => Path.Combine(Path.GetTempPath(), $"hexaware-enhanced-{Guid.NewGuid():N}.db");

    [Fact]
    public void RoundTrips_ProjectsProjectReferencesAndPackageReferences()
    {
        var cache = new StructuralCache
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            GitCommitHash = "abc123",
            Projects =
            {
                new HexProjectInfo { Name = "CSharpLib", AssemblyName = "CSharpLib", Language = "csharp", Path = "CSharpLib/CSharpLib.csproj" },
                new HexProjectInfo { Name = "VbLib", AssemblyName = "VbLib", Language = "vbnet", Path = "VbLib/VbLib.vbproj" },
            },
            ProjectReferences =
            {
                new ProjectReferenceInfo { SourceProject = "CSharpLib", TargetProject = "VbLib" },
            },
            PackageReferences =
            {
                new PackageReferenceInfo { Project = "CSharpLib", PackageName = "Newtonsoft.Json", Version = "13.0.3", Source = "PackageReference" },
            },
        };

        var dbPath = NewTempDbPath();
        try
        {
            SqliteCacheStore.WriteAll(cache, dbPath);
            var roundTripped = SqliteCacheStore.ReadAll(dbPath);

            Assert.NotNull(roundTripped);
            Assert.Equal(2, roundTripped!.Projects.Count);
            Assert.Contains(roundTripped.Projects, p => p.Name == "CSharpLib" && p.AssemblyName == "CSharpLib" && p.Language == "csharp");
            Assert.Single(roundTripped.ProjectReferences);
            Assert.Equal("VbLib", roundTripped.ProjectReferences[0].TargetProject);
            Assert.Single(roundTripped.PackageReferences);
            Assert.Equal("Newtonsoft.Json", roundTripped.PackageReferences[0].PackageName);
            Assert.Equal("13.0.3", roundTripped.PackageReferences[0].Version);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void RoundTrips_FileProjectAttributionAndCallGraphAssemblyAttribution()
    {
        var cache = new StructuralCache
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            GitCommitHash = "abc123",
        };

        cache.Files["CSharpLib/Caller.cs"] = new FileStructuralResult
        {
            Language = "csharp",
            Project = "CSharpLib",
            Functions =
            {
                new FunctionInfo { Id = FunctionId.Create("CSharpLib.Caller", "RunBilling"), Name = "RunBilling", LineRange = new[] { 1, 5 }, Params = new() },
            },
            CallGraph =
            {
                new CallGraphEntry
                {
                    Caller = FunctionId.Create("CSharpLib.Caller", "RunBilling"),
                    Callee = FunctionId.Create("VbLib.BaseClass", "CalculateTax"),
                    LineNumber = 3,
                    CallerAssembly = "CSharpLib",
                    CalleeAssembly = "VbLib",
                },
            },
        };

        var dbPath = NewTempDbPath();
        try
        {
            SqliteCacheStore.WriteAll(cache, dbPath);
            var roundTripped = SqliteCacheStore.ReadAll(dbPath);

            Assert.NotNull(roundTripped);
            var file = Assert.Single(roundTripped!.Files.Values);
            Assert.Equal("CSharpLib", file.Project);

            var callEntry = Assert.Single(file.CallGraph);
            Assert.Equal("CSharpLib", callEntry.CallerAssembly);
            Assert.Equal("VbLib", callEntry.CalleeAssembly);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void CallGraph_CalleeAssembly_IsQueryableForInboundCallCounts()
    {
        var cache = new StructuralCache
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            GitCommitHash = "abc123",
        };

        cache.Files["A.cs"] = new FileStructuralResult
        {
            Language = "csharp",
            Project = "CSharpLib",
            CallGraph =
            {
                new CallGraphEntry { Caller = "A.One", Callee = "QuickQuote.Svc.Load", LineNumber = 1, CallerAssembly = "CSharpLib", CalleeAssembly = "QuickQuote" },
                new CallGraphEntry { Caller = "A.Two", Callee = "QuickQuote.Svc.Save", LineNumber = 2, CallerAssembly = "CSharpLib", CalleeAssembly = "QuickQuote" },
                new CallGraphEntry { Caller = "A.Three", Callee = "Other.Thing.Do", LineNumber = 3, CallerAssembly = "CSharpLib", CalleeAssembly = "Other" },
            },
        };

        var dbPath = NewTempDbPath();
        try
        {
            SqliteCacheStore.WriteAll(cache, dbPath);

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM CallGraph WHERE calleeAssembly = 'QuickQuote'";
            var quickQuoteCallCount = Convert.ToInt32(cmd.ExecuteScalar());

            Assert.Equal(2, quickQuoteCallCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
