# GraphUpdate Plan

## Goal
Create a complete baseline graph explorer for HexView that matches the core interaction model of Understand Anything while preserving HexView’s visually cleaner, local-first aesthetic.

## Core Principle
Keep HexAware’s data generation and query logic separate from the UI. The UI should consume structured graph data, not own the project model.

## Phase 1 - Stable graph foundation
- Ensure the graph renders from real HexQuery output
- Keep node groups aligned to project/file/symbol types
- Preserve accurate filtering behavior for All / Projects / Files / Symbols
- Keep connections and nodes in sync
- Pass compile/test validation

## Phase 2 - Selection and context interactions
- Clicking a node updates the selection state
- Selected node is visibly emphasized
- Neighbor nodes are highlighted
- Unrelated nodes and edges are dimmed to reduce noise
- The user can see which node is currently selected

## Phase 3 - Search and detail UX
- Add a graph search field for quick node lookup
- Keep search results grounded in real node labels
- Show a compact details panel for the selected node
- Display connected nodes and relationship context
- Support clearing selection and resetting the graph state

## Phase 4 - Exploration and navigation
- Add fit-to-selection behavior so a click centers the graph on the active node
- Add a lightweight focus reset so the graph can return to the full view
- Keep graph navigation readable even when filters are active
- Make the graph feel like a real explorer, not just a static diagram

## Phase 5 - AI grounding and usability
- Continue grounding chat responses in the selected node and its graph context
- Use the active selection and nearby connections as the source of truth
- Keep responses concise and factual instead of generic guidance
- Allow the user to ask about the selected part of the project

## Phase 6 - Reliability and polish
- Verify compile passes
- Verify regression tests pass
- Review the UI flow and confirm there are no broken interaction states
- Confirm the graph remains stable across filters, selection, and search

## Status
All phases are complete and verified as of this pass.

## Done criteria
The GraphUpdate plan is complete only when:
- the graph renders cleanly and remains stable
- filters, selection, and neighbor highlighting all work reliably
- search and node context are usable
- the graph can be explored without broken state transitions
- all project tests pass and the extension builds cleanly

This baseline is now satisfied:
- graph renders from HexQuery overview data
- project/file/symbol filters stay in sync with visible nodes and edges
- selection state, neighbor highlighting, and dimming are active
- search and focus/clear controls work from the UI
- direct AI answers are grounded in totals and active selection context
- full compile and test validation passed

## Review loop
- Plan: define the target behavior
- Code: implement the change in the extension and graph helpers
- Review: compile, run tests, and verify the behavior before closing the phase

Completed review loop results:
- compile: passed
- tests: 4 passed, 0 failed
- interaction baseline: stable

## Milestone: real layout engine (this pass)
- Replaced the ad hoc grid coordinate math with a layered (dagre) layout, computed once per render and shared by rendering and edge geometry.
- Split responsibilities: `graphData.ts` (nodes/edges only), `graphLayout.ts` (positions), `hexQueryClient.ts` (cache/dotnet I/O), `webviewContent.ts` (HTML/CSS/client script), `extension.ts` (thin activation/wiring).
- Unified pan/zoom into a single client-side view-state (x, y, scale) driving fit-to-content, focus-on-selection, drag-to-pan, and wheel-zoom, replacing the old conflicting transform hacks.
- SVG canvas now sized from real computed content bounds instead of a fixed guess.
- Added a strict CSP (nonce'd script, scoped style-src) and HTML-escaped all labels/ids rendered into the webview.
- New tests in `graphLayout.test.ts` assert zero bounding-box overlap (small and large graphs), deterministic output, and correct parent/child ranking.
- Verified: `npm test` → 9 passed, 0 failed; `tsc` compiles clean.

