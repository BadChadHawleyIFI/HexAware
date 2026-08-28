# HexView Future Plan

## Mission
Build a local-first, graph-based code explorer for HexAware that feels like a polished, usable understanding surface for a codebase, while staying grounded in HexGenerate + HexQuery + SQLite and working inside VS Code as a lightweight extension experience.

The goal is not to copy Understand Anything exactly. The goal is to cover the same graph exploration behavior baseline and then improve it with cleaner local-first code, better UX, and stronger grounding for AI questions.

---

## Core Direction

### Keep the architecture local-first
- HexGenerate creates the structural cache.
- HexQuery reads the SQLite cache and emits JSON overview data.
- HexView consumes that graph data and renders a visual exploration surface.
- AI is grounded in graph state and selected context, not in generic internet-scale guidance.

### Use UA as a behavior benchmark, not a visual template
The target is to cover the meaningful graph behaviors UA has:
- stable graph layout
- rich node grouping
- filter-scope navigation
- search and focus
- neighborhood selection and highlight
- clean edge routing
- clear selected-node context
- useful AI grounding around the graph

But the implementation should remain HexAware-native:
- local data flow
- better code structure
- easier maintenance
- VS Code plugin integration
- direct grounding in the project cache

---

## What Exists Today

### HexAware project foundation
- HexGenerate generates the structural SQLite cache from a solution.
- HexQuery reads the cache and emits overview JSON used by the graph and AI.
- HexContracts holds the shared models and SQLite store logic.

### HexView extension shell
- HexView is a VS Code extension project.
- It opens a webview panel with:
  - graph area
  - filter buttons
  - search box
  - focus and clear controls
  - chat panel at the bottom
- It loads HexQuery data and builds a graph model from it.
- It passes the selected node context into the assistant for grounded answers.

### Current graph/data logic
- The graph model is built in `src/graphData.ts`.
- Filtering is implemented in `filterGraphModel`.
- Node/edge conversion is driven by the HexQuery overview structure.
- The graph is assembled from project, file, and symbol nodes.

### Current AI grounding
- The assistant logic in `src/assistant.ts` answers direct count queries like:
  - how many variables
  - how many functions
  - how many symbols
- It also grounds replies using the currently selected node when present.

---

## What We Have Already Learned

### Correct root-path handling matters
A major bug was a wrong repo/workspace resolution causing HexView to search the wrong folder and fail to find the cache.

This was fixed by resolving the root upward from both the active workspace and the extension folder and locating the real HexAware project structure.

### Data quality matters more than UI polish
The graph is only as good as the data and the node mapping. If the project cache is stale, missing, or incomplete, the graph will look wrong even if the UI is polished.

### The graph has to be readable before it is pretty
The main problem has not been the color palette. It has been the layout logic.

The graph looked poor because:
- nodes were placed too close together
- graph rows/columns were not collision-safe
- edge positions were tied to crude assumptions
- resize and filter updates were not properly reflowing geometry
- focus was acting like a transform hack instead of actual graph navigation

### AI answers must be direct and factual when asked for counts
The assistant should answer direct questions without fluff when the answer is already in the dataset.

Examples:
- “How many variables are in the project?”
- “How many symbols are in BillingPage.aspx?”

These should return direct answers, not generic project guidance.

---

## The Actual Requirement

The requirement was never to copy UA exactly.

It was to cover the key graph exploration behaviors that make a graph viewer usable and valuable, then build on that with better design and cleaner local-first code.

The necessary behaviors include:
- stable graph render
- proper node grouping
- visible selection state
- neighbor highlighting
- filtering by scope
- search by node label
- focus to the active node
- reset/clear state
- edge routing that stays aligned with nodes
- resize-aware updates
- direct AI grounding in project metrics and selection context
- graph readability under real data

---

## What Is Still Missing or Weak

### 1. Proper graph layout engine
Current node placement is heuristic and still too simplistic.

Need:
- layered project/file/symbol layout
- collision-safe spacing
- deterministic placement by group and count
- better reading order and visual hierarchy

### 2. Real graph navigation behavior
Current focus behavior is a mild pan, but it is not yet a robust graph exploration affordance.

Need:
- center-on-selection navigation
- maintain readable view framing
- allow reset to whole-graph overview
- keep selection alignment stable

### 3. Complete edge reflow
Edges are not yet fully driven by a robust layout system.

Need:
- recompute path geometry after resize
- recompute after filter updates
- keep edge endpoints anchored to node centers
- avoid stale path positioning

### 4. Better node density handling
When many files/symbols exist, the graph can still become crowded.

Need:
- spacing by group and dataset size
- vertical stacking/routing controls
- optional graph clustering or simplification

### 5. Stronger graph interaction baseline
The starting point should feel more like a real graph explorer than a static illustration.

Need:
- selected-node panel details
- neighborhood context
- more readable filter behavior
- better visual separation between groups

### 6. Deeper AI grounding around selected context
AI should respond to the graph context, not just totals.

Need:
- answer based on selected node
- use neighboring nodes as context
- answer project-related questions with graph-grounded reasoning
- avoid generic “I would inspect this” answers where a direct fact is available

---

## Target State

### UX target
The graph should feel like a local code understanding surface that lets someone:
- quickly browse project structure
- see language groupings
- inspect files and their symbols
- click into a file or element to understand context
- search for a known artifact
- ask AI focused questions about the selected portion of the graph

### Graph target
- project nodes at one layer
- file nodes clustered below them
- symbol nodes as a compact child layer for each file
- readable edges with minimal overlap
- selection/focus/resets that keep the graph coherent
- filter buttons that reflect actual views

### AI target
- answer direct numeric questions accurately
- answer selected-file questions from graph data
- answer structural and relationship questions using graph context
- stay grounded in HexQuery output and the active selection

---

## Design Principles

### 1. Better code over copied code
We should favor cleaner architecture and more maintainable logic, not imitation.

### 2. Better behavior over better screenshot
The graph must first be useful. Once it is useful, polish can be layered on.

### 3. Keep the data source honest
HexQuery is the source of truth. The UI should not invent structure beyond the data it is given.

### 4. Make the product feel local and intentional
This should not look like a generic demo graph. It should feel like a dev tool that belongs in HexAware and VS Code.

---

## Recommended Next Milestones

### Milestone 1: layout fix
- compute deterministic project/file/symbol positions
- ensure no node overlap
- separate group lanes more clearly
- improve file-to-symbol placement

### Milestone 2: graph behavior polish
- selection state remains readable
- focus centers the selected node without abusing zoom
- clear reset returns to overview state
- filter changes preserve stable layout and edge alignment

### Milestone 3: graph rendering quality
- edge routing is recomputed on resize and filter change
- path endpoints align to node centers
- graph remains readable across window sizes

### Milestone 4: stronger AI grounding
- selected file and selected node answer flows
- ask direct questions with exact answers where possible
- use relation context from graph edges

### Milestone 5: final polish and completion criteria
- extension builds cleanly
- tests pass
- graph is stable and readable
- AI answers grounded and direct
- UX feels like a real code explorer, not a prototype

---

## Completion Standard

The project is finished only when:
- the graph is easy to understand by eye
- filters and selection work without stale or broken state
- edges stay aligned as the window changes
- focus behaves naturally and predictably
- the graph feels like an explorer, not a diagram mockup
- AI can answer direct questions grounded in the graph
- the extension remains local-first and reliable

---

## Bottom Line

The right path is not to copy UA.
The right path is to absorb the graph behavior baseline UA has proven valuable, then implement it better in our own code with a better local-first architecture and cleaner UX.

HexAware should not be a clone. It should be a sharper, more maintainable, more grounded code-exploration experience.
