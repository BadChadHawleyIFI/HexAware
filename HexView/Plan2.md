# HexView Plan 2

This document captures the remaining work needed to move HexView from a working prototype to a more complete, production-quality graph explorer and AI companion.

## Status

Current prototype milestone is complete and validated:
- HexView loads and resolves the real HexAware repo/cache
- HexQuery-backed graph data is displayed in the webview
- Graph filters and node selection are available
- The extension compiles and passes tests

The remaining items below are the next-phase work still required to finish the broader plan.

## Goal

Turn HexView into a polished, interactive graph explorer that is useful for understanding real-world solution structure and can support AI-guided navigation over that graph without depending on external services or a fragile manual workflow.

## Remaining work

### 1. Improve graph layout and readability
- Replace the current manual positioned nodes with a more intentional graph layout strategy
- Add better spacing rules for project/file/symbol clusters
- Make long labels truncate cleanly and avoid overlap
- Add layering or grouping that visually separates language areas and dependency regions
- Improve the node sizing model so the graph reads like a designed visualization instead of a rough scaffold

### 2. Add real graph interaction
- Clicking a node should highlight the node and nearby connections
- Add hover details for file/project/symbol metadata
- Add a selection panel or sidebar with the node's summary and related files
- Add zoom and pan capability for larger graphs
- Add graph filtering by language, file type, file path, and symbol type
- Add a way to expand/collapse nearby dependency groups

### 3. Connect AI chat to the selected graph context
- Use the currently selected node as the grounding context for chat prompts
- Add a richer context payload that includes node type, project, labels, and related edges
- Add prompt patterns for:
  - "what is this node?"
  - "show me the dependency path to X"
  - "what files are connected to this project?"
  - "which symbols are likely relevant to this feature?"
- Keep the AI grounded to the graph data and never invent relationships that are not in the cache

### 4. Add richer data mapping from HexQuery
- Capture more of the HexQuery output model in the graph layer
- Add file-to-file and function-to-function edge mappings where available
- Include dependency counts, symbol counts, and call graph relationships in node summaries
- Distinguish project-level, file-level, and symbol-level objects more clearly in the UI

### 5. Add better graph state and persistence
- Track the selected node across refreshes or reloads
- Save graph filter state and user preferences in the extension context
- Add a simple cache status indicator that reflects whether the graph is live or stale
- Consider a repo-local optional state file for graph view preferences

### 6. Harden the extension workflow
- Make the extension launch from either the repo root or the HexView folder reliably
- Add a clear user-facing message when no cache exists and how to generate one
- Add a "Generate sample cache" command for the fixture project
- Add a "Reload graph" action for live re-evaluation without restarting the extension host

### 7. Add testing for the UI contract
- Add tests for node selection and graph state transitions
- Add tests for context building for AI prompts
- Add tests for filter logic and selected-node behavior
- Add a lightweight smoke test around the generated graph model for the fixture cache

### 8. Prepare for a stronger AI model integration
- Add a clear abstraction boundary between the graph data layer and the AI layer
- Make it possible to swap model backends without changing the UI contract
- Keep the chat layer local-first, with no required external service for the core experience
- Add a model selection strategy for VS Code Copilot-backed or local-model scenarios

## Priority order

### Priority 1: Graph usability
1. Improve node layout and cluster readability
2. Add zoom/pan and node hover details
3. Improve selection and dependency highlighting

### Priority 2: AI grounding
4. Use selected nodes to build context-aware prompts
5. Add richer context payloads and edge summaries
6. Add graph-based explanations and dependency paths

### Priority 3: Workflow and robustness
7. Add generate/reload/cache-status commands
8. Harden launch behavior and missing-cache flows
9. Expand tests for UI interactions and prompt context

### Priority 4: Product polish
10. Add state persistence and UI preferences
11. Add more refined graph styling and interaction quality
12. Prepare for a richer model-backed integration path

## Definition of done for the next phase

HexView is ready to move from prototype to a mature graph explorer when all of the following are true:
- the graph is visually readable and stable on real project data
- node selection and filtering work reliably
- the AI panel is grounded in selected-node context
- no-cache and stale-cache states are handled clearly
- the interaction contract is covered by tests
- the extension can be launched consistently from the intended workspace setup

## Suggested next step

Begin with Priority 1 and implement a real graph layout + node selection model before investing in deeper AI features. That gives the project a stable visual foundation and prevents the model layer from being built on a poor interaction model.
