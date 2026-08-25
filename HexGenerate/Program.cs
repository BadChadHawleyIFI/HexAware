using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using HexContracts;
using TreeSitter;

// Must run before any MSBuild-dependent type in this assembly is JIT-touched.
MSBuildLocator.RegisterDefaults();

var slnOption = new Option<FileInfo>("--sln") { Description = "Path to the .sln solution file.", Required = true };
var outputOption = new Option<FileInfo?>("--output")
{
    Description = "Destination cache file path (a SQLite database). Defaults to <solution-directory>/.ha/roslyn-structural-cache.db, " +
                  "so the cache lives with the project (and can be committed to source control) regardless of " +
                  "the working directory the CLI is invoked from.",
};
var fullOption = new Option<bool>("--full")
{
    Description = "Force a full rebuild, ignoring any existing cache. By default, files whose last-write " +
                  "time is not newer than the existing cache's generatedAt are reused as-is instead of " +
                  "being rescanned (mtime-based, not git-based, so uncommitted edits are still picked up).",
};

var rootCommand = new RootCommand("Generates the HexAware structural cache (C#/VB.NET via Roslyn).");
rootCommand.Options.Add(slnOption);
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(fullOption);

rootCommand.SetAction(async parseResult =>
{
    var sln = parseResult.GetValue(slnOption)!;
    var output = parseResult.GetValue(outputOption)
        ?? new FileInfo(Path.Combine(Path.GetDirectoryName(sln.FullName)!, ".ha", "roslyn-structural-cache.db"));
    var full = parseResult.GetValue(fullOption);
    await Generate.HandleGenerateAsync(sln.FullName, output.FullName, full);
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static class Generate
{
    // --- CORE GENERATION PASS (ROSLYN MSBUILD WORKSPACE) — Increment 2 scope: C#/VB.NET only, no markup/JS yet ---
    public static async Task HandleGenerateAsync(string solutionPath, string outputPath, bool full = false)
    {
        Console.WriteLine($"[+] Initializing MSBuildWorkspace tracking solution: {solutionPath}");
        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(diagnostic => Console.Error.WriteLine($"[!] {diagnostic.Diagnostic.Message}"));

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        var solutionDir = Path.GetDirectoryName(solutionPath)!;

        // Load the previous cache (if any) so unchanged files can be reused instead of rescanned.
        // Deliberately mtime-based, not git-based: comparing against the last commit hash alone would miss
        // uncommitted edits, which is exactly the case that matters most while actively developing.
        StructuralCache? existingCache = null;
        if (!full && File.Exists(outputPath))
        {
            try { existingCache = SqliteCacheStore.ReadAll(outputPath); }
            catch { Console.Error.WriteLine("[!] Existing cache could not be read; doing a full rebuild."); }
        }

        var cache = new StructuralCache
        {
            GitCommitHash = GetGitCommitHash(solutionDir),
            GeneratedAt = DateTimeOffset.UtcNow,
        };
        // Relative paths of files actually rescanned this run (as opposed to reused from the old cache) —
        // markup parsing needs this to know when a code-behind's entry was freshly rebuilt and therefore
        // needs its markup-sourced references (OnClick, etc.) re-attached even if the markup itself is unchanged.
        var reprocessed = new HashSet<string>();

        foreach (var project in solution.Projects)
        {
            Console.WriteLine($"[+] Processing Project: {project.Name} ({project.Language})");
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var document in project.Documents)
            {
                // Case-insensitive: Windows file systems don't guarantee ".designer.vb" casing.
                if (document.Name.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase) ||
                    document.Name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip MSBuild-generated build artifacts (e.g. obj/Debug/*.AssemblyAttributes.cs) — not real source.
                var docPath = document.FilePath ?? document.Name;
                if (docPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    docPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var relativePath = ToRelative(solutionDir, docPath);
                if (existingCache != null
                    && existingCache.Files.TryGetValue(relativePath, out var oldResult)
                    && File.Exists(docPath)
                    && File.GetLastWriteTimeUtc(docPath) <= existingCache.GeneratedAt.UtcDateTime)
                {
                    // Unchanged since the cache was last generated — reuse the old entry, skip the semantic walk.
                    cache.Files[relativePath] = oldResult;
                    continue;
                }
                reprocessed.Add(relativePath);

                var syntaxTree = await document.GetSyntaxTreeAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (syntaxTree == null || semanticModel == null) continue;

                var root = await syntaxTree.GetRootAsync();
                var result = new FileStructuralResult { Language = project.Language == "Visual Basic" ? "vbnet" : "csharp" };
                var namedTypes = root.DescendantNodes()
                                     .Select(node => semanticModel.GetDeclaredSymbol(node))
                                     .OfType<INamedTypeSymbol>()
                                     .DistinctBy(s => s.ToDisplayString());

                foreach (var typeSymbol in namedTypes)
                {
                    var classNodeId = typeSymbol.ToDisplayString();
                    var typeSpan = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan();

                    result.Classes.Add(new ClassInfo
                    {
                        Id = classNodeId,
                        Name = typeSymbol.Name,
                        LineRange = typeSpan is { } ts ? new[] { ts.StartLinePosition.Line + 1, ts.EndLinePosition.Line + 1 } : new[] { 0, 0 },
                        Methods = typeSymbol.GetMembers().OfType<IMethodSymbol>().Select(m => m.Name).ToList(),
                        Properties = typeSymbol.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name).ToList(),
                    });

                    if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
                    {
                        result.References.Add(new ReferenceResolution
                        {
                            Source = classNodeId,
                            Target = typeSymbol.BaseType.ToDisplayString(),
                            ReferenceType = "inherits",
                        });
                    }

                    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (member.IsImplicitlyDeclared || member.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                            continue;

                        // Consistent "Namespace.Type.Method" id for every language, independent of ToDisplayString()'s
                        // language-specific default format (VB's default includes "Public Sub ...()", C#'s doesn't).
                        var methodNodeId = $"{classNodeId}.{member.Name}";
                        var methodSpan = member.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan();

                        result.Functions.Add(new FunctionInfo
                        {
                            Id = methodNodeId,
                            Name = member.Name,
                            LineRange = methodSpan is { } ms ? new[] { ms.StartLinePosition.Line + 1, ms.EndLinePosition.Line + 1 } : new[] { 0, 0 },
                            Params = member.Parameters.Select(p => p.Type.ToDisplayString() + " " + p.Name).ToList(),
                            ReturnType = member.ReturnType.ToDisplayString(),
                        });

                        // VB.NET `Handles` clause -> referenceType "handles_event" (Increment 3).
                        var syntaxNode = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                        if (syntaxNode is MethodStatementSyntax { HandlesClause: { } handlesClause })
                        {
                            foreach (var eventObj in handlesClause.Events)
                            {
                                result.References.Add(new ReferenceResolution
                                {
                                    Source = $"{classNodeId}.{eventObj.EventContainer}",
                                    Target = methodNodeId,
                                    ReferenceType = "handles_event",
                                    Line = methodSpan?.StartLinePosition.Line + 1,
                                });
                            }
                        }
                    }
                }

                // Single semantic pass per document instead of SymbolFinder.FindCallersAsync-per-method
                // (which is O(methods x solution size) and does not scale on large legacy solutions).
                foreach (var invocation in root.DescendantNodes().Where(n => n.RawKind == root.Language switch
                {
                    "C#" => (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression,
                    _ => (int)Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.InvocationExpression,
                }))
                {
                    var calleeSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    var callerSymbol = semanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
                    if (calleeSymbol == null || callerSymbol == null || callerSymbol.ContainingType == null || calleeSymbol.ContainingType == null) continue;

                    var lineNumber = syntaxTree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
                    result.CallGraph.Add(new CallGraphEntry
                    {
                        // Same "Namespace.Type.Method" scheme as FunctionInfo.Id above, so Query can join the two.
                        Caller = $"{callerSymbol.ContainingType.ToDisplayString()}.{callerSymbol.Name}",
                        Callee = $"{calleeSymbol.ContainingType.ToDisplayString()}.{calleeSymbol.Name}",
                        LineNumber = lineNumber,
                    });
                }

                cache.Files[relativePath] = result;
            }
        }

        // Web Forms markup (.aspx/.ascx) and JavaScript (.js + inline <script>) — Increments 5/6.
        ParseWebFormsMarkup(solutionDir, cache, existingCache, reprocessed);
        ParseStandaloneJavaScriptFiles(solutionDir, cache, existingCache, reprocessed);
        // Non-code files: documents (.md/.txt/.doc/.docx/.rtf) and configs (.json/.xml/.config/.ini/.cfg/.yaml/.yml).
        ParseDocumentsAndConfigs(solutionDir, cache, existingCache, reprocessed);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) is { Length: > 0 } dir ? dir : ".");
        SqliteCacheStore.WriteAll(cache, outputPath);
        Console.WriteLine($"[+] Wrote structural cache: {reprocessed.Count} file(s) rescanned, " +
                           $"{cache.Files.Count - reprocessed.Count} reused unchanged, {cache.Files.Count} total, at {outputPath}");
    }

    private static string GetGitCommitHash(string repoDir)
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = repoDir, RedirectStandardOutput = true };
        using var proc = Process.Start(psi);
        return proc?.StandardOutput.ReadToEnd().Trim() ?? string.Empty;
    }

    // Forward slashes regardless of OS, so cache files are identical whether generated on Windows or not.
    private static string ToRelative(string baseDir, string absolutePath) =>
        Path.GetRelativePath(baseDir, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    // --- WEB FORMS MARKUP PARSING (tree-sitter HTML grammar) — Increment 5 ---
    // "OnClick" (server postback, PascalCase) vs "onclick" (client JS, lowercase) are distinct relationships;
    // StringComparison.Ordinal keeps the case-sensitive distinction exact.
    private static void ParseWebFormsMarkup(string solutionDir, StructuralCache cache, StructuralCache? existingCache, HashSet<string> reprocessed)
    {
        using var html = new Language("HTML");
        using var htmlParser = new Parser(html);
        // Real ASP.NET server controls are frequently self-closing (<asp:TextBox ... />), which the HTML
        // grammar parses as `self_closing_tag`, not `start_tag` — both must be queried or self-closing
        // controls are silently missed.
        using var tagQuery = new Query(html, "(start_tag) @tag (self_closing_tag) @tag");
        using var scriptQuery = new Query(html, "(script_element) @script");

        var markupFiles = Directory.EnumerateFiles(solutionDir, "*.aspx", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(solutionDir, "*.ascx", SearchOption.AllDirectories));

        foreach (var markupPath in markupFiles)
        {
            var codeBehindPath = markupPath + ".vb";
            if (!File.Exists(codeBehindPath)) codeBehindPath = markupPath + ".cs";
            var relativeCodeBehindPath = ToRelative(solutionDir, codeBehindPath);
            cache.Files.TryGetValue(relativeCodeBehindPath, out var codeBehindResult);

            var relativeMarkupPath = ToRelative(solutionDir, markupPath);

            // Skip re-parsing only if BOTH the markup itself is unchanged AND its code-behind wasn't freshly
            // rebuilt this run — a rebuilt code-behind entry starts with an empty References list, so a
            // markup-sourced "handles_event"/"client_handles_event" reference must be re-attached even when
            // the markup file's own content didn't change.
            var codeBehindWasReprocessed = reprocessed.Contains(relativeCodeBehindPath);
            if (!codeBehindWasReprocessed && existingCache != null
                && File.GetLastWriteTimeUtc(markupPath) <= existingCache.GeneratedAt.UtcDateTime)
            {
                if (existingCache.Files.TryGetValue(relativeMarkupPath, out var oldMarkupResult))
                    cache.Files[relativeMarkupPath] = oldMarkupResult;
                continue;
            }
            reprocessed.Add(relativeMarkupPath);

            var content = File.ReadAllText(markupPath);
            // ASP.NET server-side syntax (<%@ Page %>, <%# %>, <%= %>, <%-- --%>) isn't valid HTML5 and
            // makes tree-sitter-html swallow the entire document as one ERROR node if left in place.
            // Blank it out (preserving length/newlines so line numbers of everything after stay accurate)
            // before parsing — only the real <asp:...>/<script> markup needs to be understood.
            content = System.Text.RegularExpressions.Regex.Replace(content, "<%.*?%>",
                m => new string(m.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()),
                System.Text.RegularExpressions.RegexOptions.Singleline);
            using var tree = htmlParser.Parse(content)!;

            foreach (var capture in tagQuery.Execute(tree.RootNode).Captures)
            {
                var attributes = GetHtmlAttributes(capture.Node);
                attributes.TryGetValue("id", out var controlId);
                var line = capture.Node.StartPosition.Row + 1;

                foreach (var (attrName, attrValue) in attributes)
                {
                    if (attrName.StartsWith("On", StringComparison.Ordinal))
                    {
                        // Server postback wire-up: OnClick="btnSubmit_Click" -> code-behind method.
                        codeBehindResult?.References.Add(new ReferenceResolution
                        {
                            Source = $"{Path.GetFileName(markupPath)}#{controlId ?? "?"}.{attrName}",
                            Target = attrValue,
                            ReferenceType = "handles_event",
                            Line = line,
                        });
                    }
                    else if (attrName.StartsWith("on", StringComparison.Ordinal))
                    {
                        // Client-side handler: onclick="jsFunction()" -> JS function, recorded on this page's own entry.
                        cache.Files.TryAdd(relativeMarkupPath, new FileStructuralResult { Language = "aspx" });
                        cache.Files[relativeMarkupPath].References.Add(new ReferenceResolution
                        {
                            Source = $"#{controlId ?? "?"}.{attrName}",
                            Target = StripCallParens(attrValue),
                            ReferenceType = "client_handles_event",
                            Line = line,
                        });
                    }
                }
            }

            // Inline <script> blocks are parsed with the same JavaScript grammar used for standalone .js files.
            foreach (var capture in scriptQuery.Execute(tree.RootNode).Captures)
            {
                var rawText = capture.Node.NamedChildren.FirstOrDefault(c => c.Type == "raw_text");
                if (rawText == null) continue;
                cache.Files.TryAdd(relativeMarkupPath, new FileStructuralResult { Language = "aspx" });
                // Offset by the raw_text node's real position in the .aspx file — otherwise reported line
                // numbers are relative to the extracted snippet, not the original markup file.
                ParseJavaScriptSource(rawText.Text, cache.Files[relativeMarkupPath], rawText.StartPosition.Row);
            }
        }
    }

    private static Dictionary<string, string> GetHtmlAttributes(Node tag)
    {
        var result = new Dictionary<string, string>();
        foreach (var attr in tag.NamedChildren.Where(c => c.Type == "attribute"))
        {
            var nameNode = attr.NamedChildren.FirstOrDefault(c => c.Type == "attribute_name");
            if (nameNode == null) continue;
            var valueContainer = attr.NamedChildren.FirstOrDefault(c => c.Type == "quoted_attribute_value");
            var valueNode = valueContainer?.NamedChildren.FirstOrDefault(c => c.Type == "attribute_value");
            result[nameNode.Text] = valueNode?.Text ?? string.Empty;
        }
        return result;
    }

    private static string StripCallParens(string js)
    {
        var idx = js.IndexOf('(');
        return (idx >= 0 ? js[..idx] : js).Trim();
    }

    // --- STANDALONE .js FILES (tree-sitter JavaScript grammar) — Increment 6 ---
    private static void ParseStandaloneJavaScriptFiles(string solutionDir, StructuralCache cache, StructuralCache? existingCache, HashSet<string> reprocessed)
    {
        var jsFiles = Directory.EnumerateFiles(solutionDir, "*.js", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var jsPath in jsFiles)
        {
            var relativePath = ToRelative(solutionDir, jsPath);
            if (existingCache != null
                && existingCache.Files.TryGetValue(relativePath, out var oldResult)
                && File.GetLastWriteTimeUtc(jsPath) <= existingCache.GeneratedAt.UtcDateTime)
            {
                cache.Files[relativePath] = oldResult;
                continue;
            }

            reprocessed.Add(relativePath);
            var result = new FileStructuralResult { Language = "javascript" };
            ParseJavaScriptSource(File.ReadAllText(jsPath), result);
            cache.Files[relativePath] = result;
        }
    }

    // --- NON-CODE FILES: documents and configs ---
    private static readonly string[] DocumentExtensions = { ".md", ".markdown", ".txt", ".doc", ".docx", ".rtf" };
    private static readonly string[] ConfigExtensions = { ".config", ".cfg", ".xml", ".json", ".ini", ".yaml", ".yml" };

    private static void ParseDocumentsAndConfigs(string solutionDir, StructuralCache cache, StructuralCache? existingCache, HashSet<string> reprocessed)
    {
        var extensions = new HashSet<string>(DocumentExtensions.Concat(ConfigExtensions), StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(solutionDir, "*.*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p)))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     // Don't index the bridge's own cache output directory.
                     && !p.Contains($"{Path.DirectorySeparatorChar}.ha{Path.DirectorySeparatorChar}"));

        foreach (var path in files)
        {
            var relativePath = ToRelative(solutionDir, path);
            if (existingCache != null
                && existingCache.Files.TryGetValue(relativePath, out var oldResult)
                && File.GetLastWriteTimeUtc(path) <= existingCache.GeneratedAt.UtcDateTime)
            {
                cache.Files[relativePath] = oldResult;
                continue;
            }
            reprocessed.Add(relativePath);

            var ext = Path.GetExtension(path);
            var isConfig = ConfigExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
            var result = new FileStructuralResult { Language = isConfig ? "config" : "document" };

            try
            {
                switch (ext.ToLowerInvariant())
                {
                    case ".md":
                    case ".markdown":
                        ParseMarkdownSections(path, result);
                        break;
                    case ".json":
                        ParseJsonSections(path, result);
                        break;
                    case ".xml":
                    case ".config":
                        ParseXmlSections(path, result);
                        break;
                    case ".ini":
                    case ".cfg":
                    case ".yaml":
                    case ".yml":
                        ParseKeyValueSections(path, result);
                        break;
                    case ".docx":
                        ParseDocxSections(path, result);
                        break;
                    // .txt, .rtf: plain text, no structure to extract.
                    // .doc: legacy binary Word format — parsing it needs a dedicated library, not attempted
                    // here. The file is still registered below so its existence/path is queryable.
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[!] Could not parse {relativePath}: {ex.Message}");
            }

            cache.Files[relativePath] = result;
        }
    }

    private static void ParseMarkdownSections(string path, FileStructuralResult result)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"^(#{1,6})\s+(.*)$");
            if (!match.Success) continue;
            result.Sections.Add(new SectionInfo
            {
                Name = match.Groups[2].Value.Trim(),
                Level = match.Groups[1].Value.Length,
                LineRange = new[] { i + 1, i + 1 },
            });
        }
    }

    // Simple line-based key extraction for .ini/.cfg/.yaml/.yml — good enough for flat/lightly-nested
    // key: value or key=value files; does not attempt full YAML/INI parsing (no external dependency).
    private static void ParseKeyValueSections(string path, FileStructuralResult result)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';') || trimmed.StartsWith('[')) continue;

            var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"^(\s*)([\w][\w\.\-]*)\s*[:=]");
            if (!match.Success) continue;
            result.Sections.Add(new SectionInfo
            {
                Name = match.Groups[2].Value,
                Level = match.Groups[1].Value.Length / 2, // rough indentation-based nesting for YAML
                LineRange = new[] { i + 1, i + 1 },
            });
        }
    }

    // JsonDocument doesn't track source line numbers, so JSON sections use lineRange [0, 0].
    private static void ParseJsonSections(string path, FileStructuralResult result)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result.Sections.Add(new SectionInfo { Name = prop.Name, Level = 0, LineRange = new[] { 0, 0 } });
        }
    }

    // Root element (level 0) + its direct children (level 1); real line numbers via XDocument's
    // built-in IXmlLineInfo support (LoadOptions.SetLineInfo), no extra dependency needed.
    private static void ParseXmlSections(string path, FileStructuralResult result)
    {
        var doc = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.SetLineInfo);
        if (doc.Root == null) return;

        result.Sections.Add(new SectionInfo { Name = doc.Root.Name.LocalName, Level = 0, LineRange = XmlLineRange(doc.Root) });
        foreach (var child in doc.Root.Elements())
        {
            result.Sections.Add(new SectionInfo { Name = child.Name.LocalName, Level = 1, LineRange = XmlLineRange(child) });
        }
    }

    private static int[] XmlLineRange(System.Xml.Linq.XElement element)
    {
        var lineInfo = (System.Xml.IXmlLineInfo)element;
        var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
        return new[] { line, line };
    }

    // .docx is a zip of OOXML parts — read word/document.xml directly via System.IO.Compression, no
    // external dependency needed. Only heading-styled paragraphs (pStyle val="Heading1", etc.) become
    // sections. .docx has no native "line number" concept, so lineRange holds the paragraph index instead.
    private static void ParseDocxSections(string path, FileStructuralResult result)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var docEntry = archive.GetEntry("word/document.xml");
        if (docEntry == null) return;

        using var stream = docEntry.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = doc.Descendants(w + "p").ToList();

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var styleVal = paragraphs[i].Descendants(w + "pStyle").FirstOrDefault()?.Attribute(w + "val")?.Value;
            if (styleVal == null || !styleVal.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)) continue;

            var text = string.Concat(paragraphs[i].Descendants(w + "t").Select(t => t.Value)).Trim();
            if (text.Length == 0) continue;

            result.Sections.Add(new SectionInfo
            {
                Name = text,
                Level = int.TryParse(styleVal.AsSpan("Heading".Length), out var lvl) ? lvl : 1,
                LineRange = new[] { i, i },
            });
        }
    }


    // --- CORE JS EXTRACTION (shared by standalone .js files and inline <script> blocks) ---
    // `lineOffset` (0-based) shifts reported line numbers when `source` is a snippet extracted from a larger
    // file (e.g. an inline <script> block) rather than a whole standalone file.
    private static void ParseJavaScriptSource(string source, FileStructuralResult result, int lineOffset = 0)
    {
        using var js = new Language("JavaScript");
        using var parser = new Parser(js);
        using var tree = parser.Parse(source)!;

        using var fnQuery = new Query(js, "(function_declaration name: (identifier) @fn)");
        foreach (var capture in fnQuery.Execute(tree.RootNode).Captures)
        {
            var fnDecl = capture.Node.Parent; // function_declaration itself, for the full line range
            var start = (fnDecl?.StartPosition.Row ?? capture.Node.StartPosition.Row) + lineOffset + 1;
            var end = (fnDecl?.EndPosition.Row ?? capture.Node.EndPosition.Row) + lineOffset + 1;
            result.Functions.Add(new FunctionInfo
            {
                Id = capture.Node.Text,
                Name = capture.Node.Text,
                LineRange = new[] { start, end },
            });
        }

        // var/let/const declarations: `const f = function(){}` / `const f = () => {}` are functions in every
        // way that matters here (callable, can contain calls) even though they aren't `function_declaration`
        // nodes; everything else (a string, number, DOM reference, etc.) is a plain variable.
        using var varQuery = new Query(js, "(variable_declarator) @decl");
        foreach (var capture in varQuery.Execute(tree.RootNode).Captures)
        {
            var decl = capture.Node;
            var nameNode = decl.GetChildForField("name");
            var valueNode = decl.GetChildForField("value");
            if (nameNode == null) continue;

            var start = decl.StartPosition.Row + lineOffset + 1;
            var end = decl.EndPosition.Row + lineOffset + 1;

            if (valueNode is { Type: "function_expression" or "arrow_function" })
            {
                result.Functions.Add(new FunctionInfo
                {
                    Id = nameNode.Text,
                    Name = nameNode.Text,
                    LineRange = new[] { start, end },
                });
            }
            else
            {
                // Parent is "variable_declaration" (var) or "lexical_declaration" (let/const); the actual
                // keyword is that parent's first unnamed (token) child, e.g. "const"/"let"/"var".
                var keyword = decl.Parent?.Children.FirstOrDefault(c => !c.IsNamed)?.Text ?? "var";
                result.Variables.Add(new VariableInfo
                {
                    Name = nameNode.Text,
                    LineRange = new[] { start, end },
                    Kind = keyword,
                });
            }
        }

        using var callQuery = new Query(js, "(call_expression) @call");
        foreach (var capture in callQuery.Execute(tree.RootNode).Captures)
        {
            var callNode = capture.Node;
            var calleeText = callNode.GetChildForField("function")?.Text;
            if (string.IsNullOrEmpty(calleeText)) continue;

            var lineNumber = callNode.StartPosition.Row + lineOffset + 1;
            if (calleeText == "__doPostBack")
            {
                // __doPostBack(eventTarget, eventArgument) -> the server control this client call posts back to.
                result.References.Add(new ReferenceResolution
                {
                    Source = GetEnclosingFunctionName(callNode) ?? "(inline script)",
                    Target = GetFirstStringArgument(callNode) ?? "(dynamic)",
                    ReferenceType = "postback_trigger",
                    Line = lineNumber,
                });
            }
            else if (calleeText.StartsWith("PageMethods.", StringComparison.Ordinal))
            {
                // ASP.NET AJAX PageMethods call -> a [WebMethod]-attributed method in the code-behind.
                result.References.Add(new ReferenceResolution
                {
                    Source = GetEnclosingFunctionName(callNode) ?? "(inline script)",
                    Target = calleeText["PageMethods.".Length..],
                    ReferenceType = "ajax_call",
                    Line = lineNumber,
                });
            }
            else
            {
                // Name-based only: JS is dynamically typed, so unlike the Roslyn call graph this is a
                // heuristic (no cross-file symbol resolution).
                result.CallGraph.Add(new CallGraphEntry
                {
                    Caller = GetEnclosingFunctionName(callNode) ?? "(module scope)",
                    Callee = calleeText,
                    LineNumber = lineNumber,
                });
            }
        }
    }

    private static string? GetEnclosingFunctionName(Node node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current.Type == "function_declaration")
                return current.GetChildForField("name")?.Text;

            // const f = function(){} / const f = () => {} — the enclosing scope is the variable's own name.
            if (current.Type is "function_expression" or "arrow_function"
                && current.Parent?.Type == "variable_declarator")
                return current.Parent.GetChildForField("name")?.Text;
        }
        return null;
    }

    private static string? GetFirstStringArgument(Node callNode)
    {
        var argsNode = callNode.GetChildForField("arguments");
        var firstString = argsNode?.NamedChildren.FirstOrDefault(c => c.Type == "string");
        return firstString?.NamedChildren.FirstOrDefault(c => c.Type == "string_fragment")?.Text;
    }
}
