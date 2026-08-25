using System.CommandLine;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HexContracts;

var cacheOption = new Option<FileInfo>("--cache")
{
    Description = "Path to the generated structural cache (a SQLite database).",
    DefaultValueFactory = _ => new FileInfo(".ha/roslyn-structural-cache.db"),
};
var methodOption = new Option<string?>("--method") { Description = "Name of the method/function/variable/section to inspect (exact name, case-insensitive)." };
var fileOption = new Option<string?>("--file") { Description = "Relative path (or substring of one) of a file to summarize." };
var classOption = new Option<string?>("--class") { Description = "Name of a class/type to inspect, including its inheritance chain in both directions." };
var searchOption = new Option<string?>("--search") { Description = "Substring (case-insensitive) to find across all function/variable/class/section names, for discovery when the exact name is unknown." };
var overviewOption = new Option<bool>("--overview") { Description = "Print a repo-wide summary (file/function/class/variable counts, broken down by language) — a starting map for someone new to the codebase." };
var entrypointsOption = new Option<bool>("--entrypoints") { Description = "List functions with no in-graph callers — candidates for where execution starts (Main, event handlers, framework-invoked lifecycle methods)." };
var hotspotsOption = new Option<bool>("--hotspots") { Description = "List the functions with the most distinct callers — the most depended-upon code, worth understanding (and being careful with) first." };
var topOption = new Option<int>("--top") { Description = "Max results to return for --hotspots.", DefaultValueFactory = _ => 10 };
var docsOption = new Option<bool>("--docs") { Description = "Print self-documentation (usage, output schema, examples) for AI/human readers and exit." };

var rootCommand = new RootCommand("Lean, dependency-free reader for the HexAware structural cache.");
rootCommand.Options.Add(cacheOption);
rootCommand.Options.Add(methodOption);
rootCommand.Options.Add(fileOption);
rootCommand.Options.Add(classOption);
rootCommand.Options.Add(searchOption);
rootCommand.Options.Add(overviewOption);
rootCommand.Options.Add(entrypointsOption);
rootCommand.Options.Add(hotspotsOption);
rootCommand.Options.Add(topOption);
rootCommand.Options.Add(docsOption);

rootCommand.SetAction(parseResult =>
{
    if (parseResult.GetValue(docsOption))
    {
        Query.PrintDocs();
        return 0;
    }

    var cache = parseResult.GetValue(cacheOption)!;
    var method = parseResult.GetValue(methodOption);
    var file = parseResult.GetValue(fileOption);
    var className = parseResult.GetValue(classOption);
    var search = parseResult.GetValue(searchOption);
    var top = parseResult.GetValue(topOption);

    if (parseResult.GetValue(overviewOption)) { Query.RunOverview(cache.FullName); return 0; }
    if (parseResult.GetValue(entrypointsOption)) { Query.RunEntrypoints(cache.FullName); return 0; }
    if (parseResult.GetValue(hotspotsOption)) { Query.RunHotspots(cache.FullName, top); return 0; }
    if (!string.IsNullOrWhiteSpace(file)) { Query.RunFile(cache.FullName, file); return 0; }
    if (!string.IsNullOrWhiteSpace(className)) { Query.RunClass(cache.FullName, className); return 0; }
    if (!string.IsNullOrWhiteSpace(search)) { Query.RunSearch(cache.FullName, search); return 0; }
    if (!string.IsNullOrWhiteSpace(method)) { Query.Run(cache.FullName, method); return 0; }

    Console.WriteLine(JsonSerializer.Serialize(new { error = "One of --method, --file, --class, --search, --overview, --entrypoints, --hotspots is required (or pass --docs for usage help)." }));
    return 1;
});

return await rootCommand.Parse(args).InvokeAsync();

static class Query
{
    public static void PrintDocs()
    {
        Console.WriteLine("""
            # hex-query

            Reads the structural cache produced by `hex-generate` and returns callers, callees, and
            wire-ups for a given method/function name. Zero LLM/token cost, instant — issues small indexed
            SQL queries against a pre-generated SQLite database, no compilation or AI involved.

            ## Usage
                hex-query --method <name> [--cache <path>]
                hex-query --docs

            --method   Name of the method/function to look up (case-insensitive, matches by simple name;
                       every overload/same-named method across files is returned as a separate result).
            --cache    Path to the structural cache (a SQLite database) produced by `hex-generate`.
                       Defaults to ./.ha/roslyn-structural-cache.db (relative to the current directory).
            --docs     Print this documentation and exit.

            ## Output: a JSON array, one entry per matching function
              id          Fully-qualified identifier: "Namespace.Type.Method" for C#/VB.NET, or the bare
                          function name for JavaScript (which has no compiler-verified qualification).
              name        Simple method/function name.
              file        Absolute path to the file containing this function.
              language    "csharp" | "vbnet" | "javascript" | "aspx" | "ascx"
              lineRange   [startLine, endLine], 1-based, in the file above.
              signature   Parameter list, e.g. ["object sender", "System.EventArgs e"].
              returnType  Return type (C#/VB.NET), or null for JavaScript.
              callers     Ids/names of functions that call this one — may span different files and
                          languages (e.g. a C# method calling into VB.NET).
              callees     Ids/names of functions this one calls.
              wireUps     Cross-cutting references, each { source, target, referenceType, line }:
                            inherits             — base class relationship (VB.NET/C#)
                            handles_event        — VB `Handles` clause, or server-side ASP.NET
                                                    markup `OnClick="..."` (PascalCase attribute)
                            client_handles_event — client-side markup `onclick="..."` (lowercase
                                                    attribute) — distinct from the server-side case above
                            postback_trigger      — JavaScript `__doPostBack(...)` call
                            ajax_call             — JavaScript `PageMethods.*` call
              relatedDocs Candidate documentation for this function/variable, each { file, nearby,
                          mentionsSymbol }:
                            nearby         — the doc lives in the same folder as this symbol's file, or
                                             an ancestor folder up to the solution root (e.g. a README.md
                                             in a parent directory)
                            mentionsSymbol — the doc's actual text contains the symbol's name (re-read
                                             from disk at query time — .md/.txt/.docx only; content search
                                             is skipped for .doc/.rtf and if --cache doesn't follow the
                                             default <solutionDir>/.ha/ convention)
                          A doc appears here if EITHER is true, so "is there documentation for X" can be
                          answered by checking whether relatedDocs is non-empty.

            ## JavaScript variables
            `--method` also matches plain (non-function) JS `var`/`let`/`const` declarations if no function
            of that name exists — e.g. `const API_URL = "/api/tax";`. These return a simpler shape:
            `{ name, file, language, lineRange, kind }` (`kind` is "var" | "let" | "const") — no callers,
            callees, or signature, since a variable isn't invoked. Function-*valued* declarations
            (`const f = function(){}` / `const f = () => {}`) are treated as ordinary functions instead,
            complete with callers/callees, and appear in the normal function results.

            ## Documents and configs
            Non-code files are captured too: `language` is "document" for `.md`/`.markdown`/`.txt`/`.doc`/
            `.docx`/`.rtf`, or "config" for `.json`/`.xml`/`.config`/`.ini`/`.cfg`/`.yaml`/`.yml`. If `--method`
            matches no function or variable, it falls back to searching each file's `sections` — markdown/docx
            headings, or top-level JSON/XML/config keys — returning `{ name, file, language, lineRange, level }`.
            `.txt`/`.rtf` have no structure to extract (still registered so their existence is queryable);
            `.doc` (legacy binary Word format) is not parsed at all, for the same reason.

            ## Other base lookups (compose these to answer richer questions)
            --file <path>    Everything the cache knows about one file: functions, variables, classes,
                             sections, callGraph, references. Matches by exact relative path or substring
                             (e.g. "BillingPage" matches "VbLib/BillingPage.aspx.vb").
            --class <name>   A class/type plus its inheritance in BOTH directions: `inheritsFrom` (base
                             types) and `derivedClasses` (other classes in the cache that inherit from
                             this one) — the latter has no other lookup path in this cache.
            --search <text>  Substring (not exact) match across every function/variable/class/section name
                             at once, for discovery when the exact spelling/casing isn't known. Returns a
                             flat, lightweight list `{ kind, name, file, language }` — no detail; follow up
                             with --method/--class/--file on a specific hit for full detail.

            ## Onboarding lookups (for a developer new to the codebase)
            --overview       Repo-wide map: total/per-language file, function, class, and variable counts,
                             plus a flat per-file breakdown. Answers "what is this codebase made of, and
                             where should I look first" before drilling into any one file.
            --entrypoints    Functions with no in-graph callers, each with a `reason`: wired via markup/
                             `Handles` (a real UI/framework entry point), a conventional name (`Main`,
                             `Page_Load`, `*_Click`, ...), or "no incoming references found" (verify
                             manually — may be reflection-invoked, an external API, or genuinely unused).
                             Answers "where does this program actually start running".
            --hotspots       The `--top` (default 10) functions with the most distinct in-graph callers,
                             most-called first. Answers "what's the core logic here that everything else
                             depends on, so I should understand it well before touching it".

            These are deliberately simple, composable primitives rather than one large "answer everything"
            command — e.g. "who should review a change to X" is `--method X` (get callers) then `--method
            <each caller>` (get its relatedDocs), not a single bespoke flag.

            ## Examples
                hex-query --method CalculateTax

            Chaining example — "who should review a change to CalculateTax, and is it documented?":
                1. hex-query --method CalculateTax
                   -> callers: ["CSharpLib.Caller.RunBilling", "VbLib.DerivedClass.SubmitButton_Click"]
                   -> relatedDocs: [{ file: "README.md", mentionsSymbol: true }, ...]  (already documented)
                2. hex-query --method RunBilling
                   -> inspect ITS relatedDocs/callers too, to see who calls the caller, and so on
                No single flag does this "blast radius" walk — it's `--method` called once per hop,
                because each hop can fan out to multiple callers/callees that all deserve the same treatment.
            """);
    }

    public static void Run(string cachePath, string methodName)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var functionRows = new List<(string Id, string Name, string File, string Language, int Start, int End, string? ReturnType, List<string> Params)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT f.id, f.name, f.file, fl.language, f.lineStart, f.lineEnd, f.returnType, f.paramsJson
                    FROM Functions f JOIN Files fl ON f.file = fl.path
                    WHERE f.name = @name COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("@name", methodName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    functionRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetInt32(4), reader.GetInt32(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? new()));
                }
            }

            if (functionRows.Count == 0)
            {
                // Fall back to plain (non-function) JS variables — const API_URL = "...", var currentUser, etc.
                var variableRows = QueryNamedByTable(connection, "Variables", "kind", methodName);
                if (variableRows.Count == 0)
                {
                    // Fall back to document/config sections — markdown/docx headings, JSON/XML/config keys.
                    var sectionRows = QueryNamedByTable(connection, "Sections", "level", methodName);
                    if (sectionRows.Count == 0)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(new { message = "No matching function, variable, or section found in the structural cache." }));
                        return;
                    }

                    var sectionResults = sectionRows.Select(s => new
                    {
                        name = s.Name,
                        file = s.File,
                        language = s.Language,
                        lineRange = new[] { s.Start, s.End },
                        level = int.Parse(s.Extra),
                    });
                    Console.WriteLine(JsonSerializer.Serialize(sectionResults, CacheJson.Options));
                    return;
                }

                var variableResults = variableRows.Select(v => new
                {
                    name = v.Name,
                    file = v.File,
                    language = v.Language,
                    lineRange = new[] { v.Start, v.End },
                    kind = v.Extra,
                    relatedDocs = FindRelatedDocs(connection, cachePath, v.File, v.Name),
                });
                Console.WriteLine(JsonSerializer.Serialize(variableResults, CacheJson.Options));
                return;
            }

            var results = functionRows.Select(fn => new
            {
                id = fn.Id,
                name = fn.Name,
                file = fn.File,
                language = fn.Language,
                lineRange = new[] { fn.Start, fn.End },
                signature = fn.Params,
                returnType = fn.ReturnType,
                callers = QueryIdColumn(connection, "SELECT caller FROM CallGraph WHERE callee = @id", fn.Id),
                callees = QueryIdColumn(connection, "SELECT callee FROM CallGraph WHERE caller = @id", fn.Id),
                // References may target either the qualified Id (VB `Handles`, "inherits") or the bare
                // Name (markup OnClick/PageMethods, since markup text has no notion of a qualified id).
                wireUps = QueryWireUps(connection, fn.Id, fn.Name),
                relatedDocs = FindRelatedDocs(connection, cachePath, fn.File, fn.Name),
            });

            Console.WriteLine(JsonSerializer.Serialize(results, CacheJson.Options));
        }
    }

    // Variables/Sections share the same (name, file, language, lineStart, lineEnd, <one extra column>) shape
    // for this lookup — Variables' extra column is "kind", Sections' is "level" (parsed back to int by callers).
    private static List<(string Name, string File, string Language, int Start, int End, string Extra)> QueryNamedByTable(
        SqliteConnection connection, string table, string extraColumn, string name)
    {
        var results = new List<(string, string, string, int, int, string)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT t.name, t.file, fl.language, t.lineStart, t.lineEnd, t.{extraColumn}
            FROM {table} t JOIN Files fl ON t.file = fl.path
            WHERE t.name = @name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetValue(5).ToString()!));
        return results;
    }

    private static List<string> QueryIdColumn(SqliteConnection connection, string sql, string id)
    {
        var results = new List<string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    private static List<object> QueryWireUps(SqliteConnection connection, string id, string name)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source, target, referenceType, line FROM ReferenceEdges WHERE target = @id OR target = @name";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new { source = reader.GetString(0), target = reader.GetString(1), referenceType = reader.GetString(2), line = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3) });
        return results;
    }

    // Documentation nearby (same folder, or an ancestor folder up to the solution root) or that actually
    // mentions the symbol by name in its text. Proximity is computed purely from cache paths (always
    // available); content search re-reads the document from disk, so it's skipped if the solution
    // directory can't be inferred or the file no longer exists there.
    private static List<object> FindRelatedDocs(SqliteConnection connection, string cachePath, string symbolFile, string symbolName)
    {
        var solutionDir = InferSolutionDir(cachePath);
        var symbolDir = Path.GetDirectoryName(symbolFile)?.Replace('\\', '/') ?? "";

        var documentPaths = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT path FROM Files WHERE language = 'document'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) documentPaths.Add(reader.GetString(0));
        }

        var related = new List<object>();
        foreach (var docPath in documentPaths)
        {
            var docDir = Path.GetDirectoryName(docPath)?.Replace('\\', '/') ?? "";
            // "Nearby": the doc's folder is the symbol's own folder, or an ancestor of it (root doc dir "" counts).
            var nearby = docDir.Length == 0 || symbolDir == docDir || symbolDir.StartsWith(docDir + "/", StringComparison.Ordinal);

            var mentionsSymbol = false;
            if (solutionDir != null)
            {
                var absoluteDocPath = Path.Combine(solutionDir, docPath.Replace('/', Path.DirectorySeparatorChar));
                var text = ReadDocumentText(absoluteDocPath);
                mentionsSymbol = text != null && text.Contains(symbolName, StringComparison.OrdinalIgnoreCase);
            }

            if (nearby || mentionsSymbol)
                related.Add(new { file = docPath, nearby, mentionsSymbol });
        }
        return related;
    }

    // Assumes the default convention: the cache lives at <solutionDir>/.ha/<file>. If a custom --output/
    // --cache path breaks that convention, content search below is silently skipped (proximity still works).
    private static string? InferSolutionDir(string cachePath)
    {
        var cacheDir = Path.GetDirectoryName(Path.GetFullPath(cachePath));
        if (cacheDir == null) return null;
        return Path.GetFileName(cacheDir).Equals(".ha", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(cacheDir)
            : cacheDir;
    }

    private static string? ReadDocumentText(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return null;
        try
        {
            return Path.GetExtension(absolutePath).ToLowerInvariant() switch
            {
                ".md" or ".markdown" or ".txt" => File.ReadAllText(absolutePath),
                ".docx" => ReadDocxText(absolutePath),
                _ => null, // .doc (legacy binary), .rtf: no dependency-free way to extract text
            };
        }
        catch { return null; }
    }

    private static string? ReadDocxText(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null) return null;
        using var stream = entry.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Concat(doc.Descendants(w + "t").Select(t => t.Value));
    }

    private static bool TryOpen(string cachePath, out SqliteConnection connection)
    {
        connection = null!;
        if (!File.Exists(cachePath))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { error = "Cache file not found. Run hex-generate first." }));
            return false;
        }
        try
        {
            connection = new SqliteConnection($"Data Source={cachePath};Mode=ReadOnly");
            connection.Open();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { error = $"Cache file could not be opened: {ex.Message}" }));
            return false;
        }
    }

    // --- --file: everything the cache knows about one file ---
    public static void RunFile(string cachePath, string filePattern)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var normalized = filePattern.Replace('\\', '/');
            var matches = new List<(string Path, string Language)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT path, language FROM Files
                    WHERE path = @exact COLLATE NOCASE OR path LIKE '%' || @pattern || '%'
                    ORDER BY LENGTH(path)
                    """;
                cmd.Parameters.AddWithValue("@exact", normalized);
                cmd.Parameters.AddWithValue("@pattern", normalized);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) matches.Add((reader.GetString(0), reader.GetString(1)));
            }

            if (matches.Count == 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { message = "No file matching that path found in the structural cache." }));
                return;
            }

            var results = matches.Select(m => new
            {
                file = m.Path,
                language = m.Language,
                functions = QueryFunctionsByFile(connection, m.Path),
                variables = QueryVariablesByFile(connection, m.Path),
                classes = QueryClassesByFile(connection, m.Path),
                sections = QuerySectionsByFile(connection, m.Path),
                callGraph = QueryCallGraphByFile(connection, m.Path),
                references = QueryReferencesByFile(connection, m.Path),
            }).ToList();
            Console.WriteLine(JsonSerializer.Serialize(results, CacheJson.Options));
        }
    }

    private static List<object> QueryFunctionsByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, lineStart, lineEnd, returnType, paramsJson FROM Functions WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new
            {
                id = reader.GetString(0),
                name = reader.GetString(1),
                lineRange = new[] { reader.GetInt32(2), reader.GetInt32(3) },
                returnType = reader.IsDBNull(4) ? null : reader.GetString(4),
                @params = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? new(),
            });
        return results;
    }

    private static List<object> QueryVariablesByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, lineStart, lineEnd, kind FROM Variables WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new { name = reader.GetString(0), lineRange = new[] { reader.GetInt32(1), reader.GetInt32(2) }, kind = reader.GetString(3) });
        return results;
    }

    private static List<object> QueryClassesByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, lineStart, lineEnd, methodsJson, propertiesJson FROM Classes WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new
            {
                id = reader.GetString(0),
                name = reader.GetString(1),
                lineRange = new[] { reader.GetInt32(2), reader.GetInt32(3) },
                methods = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? new(),
                properties = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? new(),
            });
        return results;
    }

    private static List<object> QuerySectionsByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, level, lineStart, lineEnd FROM Sections WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new { name = reader.GetString(0), level = reader.GetInt32(1), lineRange = new[] { reader.GetInt32(2), reader.GetInt32(3) } });
        return results;
    }

    private static List<object> QueryCallGraphByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT caller, callee, lineNumber FROM CallGraph WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new { caller = reader.GetString(0), callee = reader.GetString(1), lineNumber = reader.GetInt32(2) });
        return results;
    }

    private static List<object> QueryReferencesByFile(SqliteConnection connection, string file)
    {
        var results = new List<object>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source, target, referenceType, line FROM ReferenceEdges WHERE file = @file";
        cmd.Parameters.AddWithValue("@file", file);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new { source = reader.GetString(0), target = reader.GetString(1), referenceType = reader.GetString(2), line = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3) });
        return results;
    }

    // --- --class: a type plus its inheritance chain in both directions ---
    public static void RunClass(string cachePath, string className)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var matches = new List<(string Id, string Name, string File, string Language, int Start, int End, List<string> Methods, List<string> Properties)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT c.id, c.name, c.file, fl.language, c.lineStart, c.lineEnd, c.methodsJson, c.propertiesJson
                    FROM Classes c JOIN Files fl ON c.file = fl.path
                    WHERE c.name = @name COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("@name", className);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    matches.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetInt32(4), reader.GetInt32(5),
                        JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new(),
                        JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? new()));
                }
            }

            if (matches.Count == 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { message = "No matching class found in the structural cache." }));
                return;
            }

            var results = matches.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                file = m.File,
                language = m.Language,
                lineRange = new[] { m.Start, m.End },
                methods = m.Methods,
                properties = m.Properties,
                // "inherits" edges are Source = derived class id, Target = base type id/name.
                inheritsFrom = QueryReferenceEdgeValues(connection, "source", m.Id, "target"),
                derivedClasses = QueryReferenceEdgeValues(connection, "target", m.Id, "source"),
            }).ToList();
            Console.WriteLine(JsonSerializer.Serialize(results, CacheJson.Options));
        }
    }

    // whereColumn/selectColumn are always one of a small set of literal constants supplied by this file's
    // own code (never external input), so string-interpolating them into SQL text here is safe.
    private static List<string> QueryReferenceEdgeValues(SqliteConnection connection, string whereColumn, string whereValue, string selectColumn)
    {
        var results = new List<string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {selectColumn} FROM ReferenceEdges WHERE referenceType = 'inherits' AND {whereColumn} = @val";
        cmd.Parameters.AddWithValue("@val", whereValue);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    // --- --search: fuzzy discovery across every symbol kind, for when the exact name is unknown ---
    public static void RunSearch(string cachePath, string substring)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var hits = new List<object>();
            void AddHits(string table, string kind)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT t.name, t.file, fl.language FROM {table} t JOIN Files fl ON t.file = fl.path WHERE t.name LIKE '%' || @s || '%'";
                cmd.Parameters.AddWithValue("@s", substring);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    hits.Add(new { kind, name = reader.GetString(0), file = reader.GetString(1), language = reader.GetString(2) });
            }
            // Table names are literal constants, not user input — safe to interpolate.
            AddHits("Functions", "function");
            AddHits("Variables", "variable");
            AddHits("Classes", "class");
            AddHits("Sections", "section");

            if (hits.Count == 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { message = "No matches found in the structural cache." }));
                return;
            }
            Console.WriteLine(JsonSerializer.Serialize(hits, CacheJson.Options));
        }
    }

    // --- --overview: repo-wide map, a starting point for someone new to the codebase ---
    public static void RunOverview(string cachePath)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            string? generatedAt;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM Meta WHERE key = 'generatedAt'";
                generatedAt = cmd.ExecuteScalar() as string;
            }

            int Scalar(string sql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }

            Dictionary<string, int> GroupCount(string sql)
            {
                var counts = new Dictionary<string, int>();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1);
                return counts;
            }

            var fileCounts = GroupCount("SELECT language, COUNT(*) FROM Files GROUP BY language");
            var functionCounts = GroupCount("SELECT fl.language, COUNT(*) FROM Functions f JOIN Files fl ON f.file = fl.path GROUP BY fl.language");
            var classCounts = GroupCount("SELECT fl.language, COUNT(*) FROM Classes c JOIN Files fl ON c.file = fl.path GROUP BY fl.language");
            var variableCounts = GroupCount("SELECT fl.language, COUNT(*) FROM Variables v JOIN Files fl ON v.file = fl.path GROUP BY fl.language");
            var sectionCounts = GroupCount("SELECT fl.language, COUNT(*) FROM Sections s JOIN Files fl ON s.file = fl.path GROUP BY fl.language");

            var byLanguage = fileCounts.Keys
                .Select(lang => new
                {
                    language = lang,
                    fileCount = fileCounts.GetValueOrDefault(lang, 0),
                    functions = functionCounts.GetValueOrDefault(lang, 0),
                    classes = classCounts.GetValueOrDefault(lang, 0),
                    variables = variableCounts.GetValueOrDefault(lang, 0),
                    sections = sectionCounts.GetValueOrDefault(lang, 0),
                })
                .OrderByDescending(g => g.fileCount)
                .ToList();

            var files = new List<object>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT fl.path, fl.language,
                           (SELECT COUNT(*) FROM Functions WHERE file = fl.path) AS functionCount,
                           (SELECT COUNT(*) FROM Classes WHERE file = fl.path) AS classCount
                    FROM Files fl ORDER BY fl.path
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    files.Add(new { file = reader.GetString(0), language = reader.GetString(1), functionCount = reader.GetInt32(2), classCount = reader.GetInt32(3) });
            }

            var overview = new
            {
                generatedAt,
                totalFiles = Scalar("SELECT COUNT(*) FROM Files"),
                totalFunctions = Scalar("SELECT COUNT(*) FROM Functions"),
                totalClasses = Scalar("SELECT COUNT(*) FROM Classes"),
                totalVariables = Scalar("SELECT COUNT(*) FROM Variables"),
                byLanguage,
                files,
            };
            Console.WriteLine(JsonSerializer.Serialize(overview, CacheJson.Options));
        }
    }

    // --- --entrypoints: functions nothing IN THE CALL GRAPH calls -- likely where execution starts ---
    public static void RunEntrypoints(string cachePath)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var candidates = new List<(string Id, string Name, string File, string Language)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT f.id, f.name, f.file, fl.language
                    FROM Functions f JOIN Files fl ON f.file = fl.path
                    WHERE f.id NOT IN (SELECT DISTINCT callee FROM CallGraph)
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }

            var results = candidates.Select(x =>
            {
                // A reference (e.g. markup OnClick, VB Handles) means the framework/UI invokes this, not
                // ordinary code -- still a true "entry point" even though it has no call-graph callers.
                var wiredBy = new List<string>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT referenceType FROM ReferenceEdges WHERE target = @id OR target = @name";
                    cmd.Parameters.AddWithValue("@id", x.Id);
                    cmd.Parameters.AddWithValue("@name", x.Name);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) wiredBy.Add(reader.GetString(0));
                }
                var reason = wiredBy.Count > 0
                    ? $"wired via: {string.Join(", ", wiredBy)}"
                    : LooksLikeConventionalEntryPoint(x.Name)
                        ? "conventional framework entry point by name"
                        : "no incoming references found in cache -- verify manually (may be reflection-invoked, an external API, or unused)";
                return new { id = x.Id, name = x.Name, file = x.File, language = x.Language, reason };
            }).ToList();

            if (results.Count == 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { message = "Every function in the cache has at least one in-graph caller." }));
                return;
            }
            Console.WriteLine(JsonSerializer.Serialize(results, CacheJson.Options));
        }
    }

    private static bool LooksLikeConventionalEntryPoint(string name) =>
        name is "Main" or "Page_Load" or "Page_Init" or "Application_Start" or "Application_End" or "Session_Start"
        || name.EndsWith("_Click", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("_Load", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("On", StringComparison.Ordinal);

    // --- --hotspots: functions with the most distinct callers -- the "handle with care" list ---
    public static void RunHotspots(string cachePath, int top)
    {
        if (!TryOpen(cachePath, out var connection)) return;
        using (connection)
        {
            var results = new List<object>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT f.id, f.name, f.file, fl.language, cg.callerCount
                FROM (SELECT callee, COUNT(DISTINCT caller) AS callerCount FROM CallGraph GROUP BY callee) cg
                JOIN Functions f ON f.id = cg.callee
                JOIN Files fl ON f.file = fl.path
                ORDER BY cg.callerCount DESC
                LIMIT @top
                """;
            cmd.Parameters.AddWithValue("@top", top);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new { id = reader.GetString(0), name = reader.GetString(1), file = reader.GetString(2), language = reader.GetString(3), callerCount = reader.GetInt32(4) });

            if (results.Count == 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { message = "No functions with in-graph callers found." }));
                return;
            }
            Console.WriteLine(JsonSerializer.Serialize(results, CacheJson.Options));
        }
    }
}
