# HexView

# HexView

HexView is the visualizer companion for HexAware. Graph extraction and query logic stay in the HexAware toolchain (HexGenerate/HexQuery); HexView renders that data as an interactive graph with a grounded AI chat panel, inside VS Code.

## Goals

- show a central graph window with a chat panel at the bottom
- render real project structure from HexQuery's SQLite-backed cache
- keep the graph readable via a real layout engine, not ad hoc coordinates
- ground chat answers in the actual graph data and current selection

## Current state

- `hexview.open` command opens a webview panel with a live graph and chat
- graph data comes from `HexQuery --overview` against a HexAware cache (auto-generates the sample fixture cache if none exists)
- node positions are computed with a layered (dagre) layout for collision-free, readable placement
- selection, neighbor highlighting, search, filter (all/project/file/symbol), focus, drag-to-pan, and wheel-zoom are all implemented client-side without full-page reloads
- chat answers direct count questions (files/functions/classes/variables/symbols, including per-selected-file symbol counts) from real graph totals

## Project shape

- `src/extension.ts` - thin activation/wiring layer
- `src/hexQueryClient.ts` - dotnet/HexQuery process invocation and cache resolution
- `src/graphData.ts` - converts HexQuery JSON into abstract nodes/edges
- `src/graphLayout.ts` - computes node positions (dagre layered layout)
- `src/webviewContent.ts` - webview HTML/CSS/client script
- `src/assistant.ts` - grounded chat reply logic
- `src/repoRoot.ts` - repo root resolution for locating the HexAware cache
- `PLAN.md` / `GraphUpdate.md` / `FUTURE.md` - project planning history

## Next phases

1. Expand AI grounding to structural/relationship questions beyond direct counts.
2. Add a details panel for connected files/symbols under a selected node.
3. Package/bundle for distribution (currently run via `F5` extension host debugging).

