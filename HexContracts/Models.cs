using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexContracts;

public class StructuralCache
{
    [JsonPropertyName("gitCommitHash")]
    public string GitCommitHash { get; set; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    // Keyed by project-relative file path so both Generate (writer) and Query (reader) can do an O(1) lookup per file.
    [JsonPropertyName("files")]
    public Dictionary<string, FileStructuralResult> Files { get; set; } = new();
}

public class FileStructuralResult
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty; // "csharp" | "vbnet" | "javascript" | "aspx" | "ascx" | "config" | "document"

    [JsonPropertyName("functions")]
    public List<FunctionInfo> Functions { get; set; } = new();

    // Plain (non-function-valued) JS variable declarations — var/let/const holding a value, not a function.
    // Function-valued declarations (const f = function(){} / const f = () => {}) are surfaced as Functions instead.
    [JsonPropertyName("variables")]
    public List<VariableInfo> Variables { get; set; } = new();

    [JsonPropertyName("classes")]
    public List<ClassInfo> Classes { get; set; } = new();

    // Non-code structural entries: markdown/docx headings ("document" files) or top-level keys/elements
    // ("config" files: .json, .xml/.config, .ini/.cfg/.yaml/.yml). Not populated for code files.
    [JsonPropertyName("sections")]
    public List<SectionInfo> Sections { get; set; } = new();

    [JsonPropertyName("imports")]
    public List<ImportInfo> Imports { get; set; } = new();

    [JsonPropertyName("exports")]
    public List<ExportInfo> Exports { get; set; } = new();

    [JsonPropertyName("callGraph")]
    public List<CallGraphEntry> CallGraph { get; set; } = new();

    // VB `Handles` clauses + ASPX/ASCX markup wire-ups, both surfaced as referenceType "handles_event".
    [JsonPropertyName("references")]
    public List<ReferenceResolution> References { get; set; } = new();
}

public class FunctionInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty; // fully-qualified: "Namespace.Type.MethodName"
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lineRange")] public int[] LineRange { get; set; } = new int[2];
    [JsonPropertyName("params")] public List<string> Params { get; set; } = new();
    [JsonPropertyName("returnType")] public string? ReturnType { get; set; }
}

public class VariableInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lineRange")] public int[] LineRange { get; set; } = new int[2];
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty; // "var" | "let" | "const"
}

// A named structural unit inside a non-code file: a markdown/docx heading, or a top-level JSON/XML/config
// key. `level` is heading depth (1-6) for documents, or nesting depth (0 = root/top-level) for configs.
public class SectionInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("lineRange")] public int[] LineRange { get; set; } = new int[2]; // [0,0] where the source format has no line concept (e.g. JSON keys, .docx paragraph index)
}

public class ClassInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty; // fully-qualified: "Namespace.TypeName"
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lineRange")] public int[] LineRange { get; set; } = new int[2];
    [JsonPropertyName("methods")] public List<string> Methods { get; set; } = new();
    [JsonPropertyName("properties")] public List<string> Properties { get; set; } = new();
}

public class ImportInfo
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("specifiers")] public List<string> Specifiers { get; set; } = new();
    [JsonPropertyName("lineNumber")] public int LineNumber { get; set; }
}

public class ExportInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lineNumber")] public int LineNumber { get; set; }
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
}

public class CallGraphEntry
{
    [JsonPropertyName("caller")] public string Caller { get; set; } = string.Empty;
    [JsonPropertyName("callee")] public string Callee { get; set; } = string.Empty;
    [JsonPropertyName("lineNumber")] public int LineNumber { get; set; }
}

public class ReferenceResolution
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;
    [JsonPropertyName("referenceType")] public string ReferenceType { get; set; } = string.Empty; // "handles_event" | "inherits" | "postback_trigger" | "ajax_call" | "client_handles_event"
    [JsonPropertyName("line")] public int? Line { get; set; }
}

/// <summary>Shared serialization options so Generate (writer) and Query (reader) always agree on JSON shape.</summary>
public static class CacheJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
