# HexView Plan

## Objective

Create a visual explorer for HexAware that renders a graph-based understanding of a legacy codebase or database and provides a chat surface for natural-language questions grounded in that graph.

## Principle

Keep the HexAware graph engine independent from the UI. The graph extraction and query logic should stay in HexGenerate/HexQuery. The visualizer should consume that structured output and provide the IDE-facing experience.

## Milestone 1 - Initial plugin scaffold

- create the VS Code extension project
- add an extension command to open a panel
- render a central graph area with a bottom chat panel
- verify the plugin loads and the webview works

## Milestone 2 - Real graph data integration

- call HexQuery or read its generated SQLite cache
- convert the data into nodes and edges for a graph renderer
- support project/file/function/class expansion
- add selection and detail panes

## Milestone 3 - Better visualization

- add search/filter controls
- cluster nodes by project, file, or type
- highlight incoming/outgoing dependency paths
- support expand/collapse for subgraphs

## Milestone 4 - Chat + AI context

- wire chat to a model endpoint or Copilot context
- pass only the selected graph context to the model
- allow prompts such as "what depends on this table?" or "who calls this method?"
- keep responses grounded in node/edge evidence

## Milestone 5 - IDE flexibility

- maintain the VS Code plugin as the main surface
- keep the graph engine portable for other hosts
- design a future Visual Studio version from the same shared contract

## Recommended starting model

Use MAI-Code-1.1-Flash as the initial grounding model for local chat-enabled experiences while the visual graph is being wired up. The long-term direction should be to use the best available Copilot model when the extension runs inside the Microsoft ecosystem.

## Suggested phases

1. plugin shell
2. graph rendering stub
3. HexQuery data ingestion
4. chat grounding layer
5. polished UX and IDE integration

## Success criteria

- plugin opens a graph-like main panel
- chat input is visible and functional
- graph context can be supplied by HexQuery output
- AI replies are grounded in selected context instead of raw global noise
- the visualizer remains layered and IDE-agnostic at the core
