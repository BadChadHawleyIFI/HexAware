```
                        ⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡

                ██╗  ██╗ ███████╗ ██╗  ██╗  █████╗  ██╗    ██╗  █████╗  ██████╗  ███████╗
                ██║  ██║ ██╔════╝ ╚██╗██╔╝ ██╔══██╗ ██║    ██║ ██╔══██╗ ██╔══██╗ ██╔════╝
                ███████║ █████╗    ╚███╔╝  ███████║ ██║ █╗ ██║ ███████║ ██████╔╝ █████╗  
                ██╔══██║ ██╔══╝    ██╔██╗  ██╔══██║ ██║███╗██║ ██╔══██║ ██╔══██╗ ██╔══╝  
                ██║  ██║ ███████╗ ██╔╝ ██╗ ██║  ██║ ╚███╔███╔╝ ██║  ██║ ██║  ██║ ███████╗
                ╚═╝  ╚═╝ ╚══════╝ ╚═╝  ╚═╝ ╚═╝  ╚═╝  ╚══╝╚══╝  ╚═╝  ╚═╝ ╚═╝  ╚═╝ ╚══════╝

                        ⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡⬡
```

<div align="center">

### *It knows where the magic is!* ✨

**A compiler-accurate, zero-token structural cache for mixed C# / VB.NET / ASP.NET Web Forms codebases — built so an AI agent can query your legacy code instead of re-reading it.**

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C%23](https://img.shields.io/badge/C%23-Roslyn-239120?logo=csharp&logoColor=white)
![VB.NET](https://img.shields.io/badge/VB.NET-supported-blueviolet?logo=visualstudio&logoColor=white)
![SQLite](https://img.shields.io/badge/cache-SQLite-003B57?logo=sqlite&logoColor=white)
![TreeSitter](https://img.shields.io/badge/Parsing-TreeSitter.DotNet-blue)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-informational)

</div>

---

## Table of contents

- [Table of contents](#table-of-contents)
- [What is HexAware?](#what-is-hexaware)
- [Why it exists](#why-it-exists)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Getting started](#getting-started)
- [`hex-generate` — build the cache](#hex-generate--build-the-cache)
- [`hex-query` — read the cache](#hex-query--read-the-cache)
  - [Base lookups](#base-lookups)
  - [Onboarding lookups](#onboarding-lookups)
  - [Output fields](#output-fields)
  - [Chaining queries together](#chaining-queries-together)
- [Project layout](#project-layout)
- [What gets captured](#what-gets-captured)
- [Known limitations](#known-limitations)
- [Roadmap](#roadmap)
- [License](#license)

---

## What is HexAware?

**HexAware** is a pair of small, focused .NET command-line tools — `hex-generate` and `hex-query` — that turn a real, messy, mixed-language codebase (think: 15 years of ASP.NET Web Forms, VB.NET code-behinds, C# class libraries, inline JavaScript, and a pile of config files) into a single **queryable structural cache**.

`hex-generate` does the expensive part *once*: it loads your `.sln` through **Roslyn's** real compiler/semantic APIs (not regex, not guesswork) for `.cs`/`.vb`, and uses **tree-sitter** grammars for JavaScript and HTML/Web-Forms markup, and writes everything — functions, classes, inheritance, cross-language call graphs, event wire-ups, even documentation and config files — into one small **SQLite** database.

`hex-query` does the part that matters for AI-assisted development: it answers precise, instant, **zero-LLM-token** questions against that cache — *"who calls this method, across languages?"*, *"what does this Web Forms button actually wire up to?"*, *"is there documentation for this function?"* — via small, composable command-line primitives an AI agent (or you) can chain together.

No LLM involved in generating or reading the cache. No token cost. No re-reading the same 3,000-line legacy file five times in one session just to answer "who calls this."

## Why it exists

Legacy ASP.NET Web Forms solutions are exactly the kind of codebase that's expensive for an AI coding assistant to reason about:

- Cross-language call graphs (C# ↔ VB.NET) that plain text search can't follow.
- Server-side (`OnClick="..."`) and client-side (`onclick="..."`) event wiring that look almost identical but mean completely different things.
- `__doPostBack` and `PageMethods.*` AJAX calls hiding real control flow inside inline `<script>` blocks.
- VB's `Handles` clause, which wires up event handlers with no textual reference anywhere near the control itself.

HexAware resolves all of this **once**, compiler-accurate, and stores it so it can be looked up in milliseconds — instead of asking an LLM to re-derive it from raw source every single time.

## Architecture

```
 Mixed Codebase (C# / VB.NET / ASP.NET Web Forms / JS / configs / docs)
 .sln · .csproj/.vbproj · .aspx/.ascx · .js · .json/.xml/.ini · .md/.docx
                    │                              │
                    ▼ (Roslyn semantic pass)        ▼ (tree-sitter parse)
     ┌───────────────────────────┐      ┌────────────────────────────────┐
     │ Roslyn: C#/VB.NET         │      │ TreeSitter.DotNet: .js files,   │
     │ SemanticModel walk        │      │ inline <script>, .aspx/.ascx    │
     │                           │      │ markup itself (HTML grammar)    │
     └───────────────────────────┘      └────────────────────────────────┘
                    │                              │
                    └──────────────┬───────────────┘
                                   ▼
                    ┌───────────────────────────────┐
                    │   hex-generate                │
                    │   writes ONE SQLite cache,     │
                    │   indexed by name/file/id      │
                    └───────────────────────────────┘
                                   │
                                   ▼
                    ┌───────────────────────────────┐
                    │   hex-query                    │
                    │   instant, indexed, zero-token  │
                    │   reads — callers/callees/      │
                    │   wire-ups/docs/overview/...    │
                    └───────────────────────────────┘
                                   │
                                   ▼
                    ┌───────────────────────────────┐
                    │   AI agent / terminal / you    │
                    └───────────────────────────────┘
```

Three projects, one solution:

| Project | Role | Depends on |
|---|---|---|
| **`HexContracts`** | Shared schema (POCOs) + the SQLite read/write layer | `Microsoft.Data.Sqlite` only |
| **`HexGenerate`** | Heavy lifter — Roslyn/MSBuild + TreeSitter.DotNet. Runs rarely. | `HexContracts` |
| **`HexQuery`** | Lean reader — pure indexed SQL queries, no MSBuild/Roslyn/TreeSitter. Runs repeatedly. | `HexContracts` only |

`hex-generate` and `hex-query` are deliberately separate executables: an AI agent invokes `hex-query` many times per session and it needs to start and respond instantly, while `hex-generate` only needs to run when your code actually changes.

## Requirements

- **.NET 10 SDK** — needed to build/run both CLIs, and it's also what supplies the real MSBuild toolchain `hex-generate` needs to load your `.sln`/`.csproj`/`.vbproj` files (via `MSBuildLocator`). If you can run `dotnet build` on your solution today, you already have everything `hex-generate` needs.
- **A `.sln`** for the codebase you want to analyze — old-style non-SDK .NET Framework projects (typical for Web Forms) work exactly like modern SDK-style ones.
- **Windows, Linux, or macOS** — nothing in this tool is Windows-specific; SQLite and the .NET SDK are both cross-platform.
- No database server, no Node.js, no external services. Everything runs locally, in-process.

## Getting started

```bash
git clone <this-repo>
cd HexAware
dotnet build .\HexAware.slnx
```

That builds `HexContracts`, `HexGenerate`, and `HexQuery`. Run either tool straight out of the build with `dotnet run --project`, or `dotnet publish` them for a standalone executable:

```bash
dotnet publish HexGenerate -c Release -o ./tools
dotnet publish HexQuery -c Release -o ./tools
```

## `hex-generate` — build the cache

```bash
hex-generate --sln ./MyLegacyApp.sln
```

| Option | Required | Description |
|---|---|---|
| `--sln` | ✅ | Path to the `.sln` file to analyze. |
| `--output` | | Destination cache path. Defaults to `<solution-directory>/.ha/roslyn-structural-cache.db` — it lives with the project regardless of where you invoke the CLI from, so it can be committed alongside the code it describes. |
| `--full` | | Force a complete rebuild. Without it, `hex-generate` only rescans files whose last-write time is newer than the cache's `generatedAt` (mtime-based, so uncommitted edits are always picked up) — everything else is reused as-is. |

Every run prints exactly what happened:

```
[+] Wrote structural cache: 2 file(s) rescanned, 16 reused unchanged, 18 total, at .../.ha/roslyn-structural-cache.db
```

## `hex-query` — read the cache

```bash
hex-query --method CalculateTax
hex-query --docs   # full built-in reference, readable by a human or an AI agent
```

### Base lookups

| Flag | Answers |
|---|---|
| `--method <name>` | Everything about one function/variable/config-or-doc-section: signature, callers, callees, event wire-ups, and related documentation. |
| `--file <path>` | Everything in one file — functions, variables, classes, sections, call graph, references. Matches by exact path or substring. |
| `--class <name>` | A class/type plus its inheritance **in both directions** — `inheritsFrom` and `derivedClasses` (the latter has no other lookup path). |
| `--search <text>` | Fuzzy substring match across every function/variable/class/section name at once — for discovery when you don't know the exact name. |

### Onboarding lookups

| Flag | Answers |
|---|---|
| `--overview` | "What is this codebase made of?" — file/function/class/variable counts, broken down by language. |
| `--entrypoints` | "Where does this program actually start running?" — functions with no in-graph callers, each tagged with *why* (a UI/framework wire-up, a conventional name, or genuinely unreferenced). |
| `--hotspots [--top N]` | "What's the core logic everyone depends on?" — the N functions with the most distinct callers, most-depended-upon first. |

Every command also accepts `--cache <path>` (defaults to `./.ha/roslyn-structural-cache.db`).

### Output fields

`--method` returns one JSON object per match:

```json
{
  "id": "VbLib.DerivedClass.CalculateTax",
  "name": "CalculateTax",
  "file": "VbLib/DerivedClass.vb",
  "language": "vbnet",
  "lineRange": [12, 12],
  "signature": [],
  "returnType": "Void",
  "callers": ["CSharpLib.Caller.RunBilling", "VbLib.DerivedClass.SubmitButton_Click"],
  "callees": ["VbLib.BaseClass.LogMessage"],
  "wireUps": [],
  "relatedDocs": [
    { "file": "README.md", "nearby": true, "mentionsSymbol": true },
    { "file": "OperationsNotes.docx", "nearby": true, "mentionsSymbol": false }
  ]
}
```

`wireUps.referenceType` is one of:

| Value | Meaning |
|---|---|
| `inherits` | Base class relationship (C#/VB.NET) |
| `handles_event` | VB `Handles` clause, or server-side markup `OnClick="..."` |
| `client_handles_event` | Client-side markup `onclick="..."` — distinct from the server-side case |
| `postback_trigger` | JavaScript `__doPostBack(...)` call |
| `ajax_call` | JavaScript `PageMethods.*` call |

Run `hex-query --docs` at any time for the complete, always-up-to-date field reference straight from the tool itself.

### Chaining queries together

HexAware deliberately ships small, composable primitives instead of one "answer everything" command. A multi-hop question is just `--method` called once per hop:

> **"Who should review a change to `CalculateTax`, and is it already documented?"**
>
> 1. `hex-query --method CalculateTax` → `callers: [RunBilling, SubmitButton_Click]`, `relatedDocs` shows `README.md` already discusses it.
> 2. `hex-query --method RunBilling` → inspect *its* callers/relatedDocs too, and keep walking outward as far as you need.

## Project layout

```
HexAware.slnx
HexContracts/       Shared schema + SQLite read/write layer (no MSBuild/Roslyn dependency)
HexGenerate/         Roslyn + TreeSitter.DotNet — builds the cache
HexQuery/            Lean SQL-backed reader — queries the cache
fixtures/            Synthetic legacy Web Forms solution used for development/verification
```

## What gets captured

| Kind | Source | Examples |
|---|---|---|
| Functions & classes | Roslyn semantic model (C#/VB.NET) | Signatures, line ranges, inheritance, cross-language call graph |
| Web Forms markup | tree-sitter HTML grammar | Server (`OnClick`) vs. client (`onclick`) event wiring, `id` resolution |
| JavaScript | tree-sitter JS grammar | Functions, variables, `__doPostBack`, `PageMethods.*`, name-based call graph |
| Documents | `.md`/`.docx`/`.txt`/`.rtf` | Headings as sections, full-text search for "is X documented?" |
| Configs | `.json`/`.xml`/`.config`/`.ini`/`.yaml` | Top-level keys as queryable sections |

## Known limitations

- `.doc` (legacy binary Word format) is registered but not parsed — no practical dependency-free path to its contents.
- JavaScript call-graph resolution is name-based (JS is dynamically typed), unlike the fully compiler-verified C#/VB.NET graph.
- Analyzing old-style, non-SDK `.csproj`/`.vbproj` projects requires a real MSBuild toolchain — the .NET SDK provides this out of the box.

## Roadmap

- Package both CLIs as `dotnet tool`s for one-command global install.
- A native recursive `--callchain` primitive (multi-hop caller/callee walk in a single query, via a SQLite recursive CTE).

## License

MIT — see [LICENSE](LICENSE). Update the copyright holder name in that file if you'd like it to be your own instead of the placeholder.

---

<div align="center">

**HexAware** — *it knows where the magic is.* ⬡

</div>
