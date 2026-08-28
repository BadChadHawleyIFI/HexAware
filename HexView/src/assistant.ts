import type { GraphNode } from './graphData';
import type { FileSymbolNames, GraphStatus } from './hexQueryClient';

/** Resolves which file node a question is about: the current selection, or a file name mentioned in the text. */
export function resolveMatchingFileNode(userText: string, graphStatus: GraphStatus, selectedNodeId?: string): GraphNode | undefined {
  const selectedNode = selectedNodeId ? graphStatus.model.nodes.find(node => node.id === selectedNodeId) : undefined;
  if (selectedNode && selectedNode.group === 'file') {
    return selectedNode;
  }

  const match = /(?:in|for|about)\s+([A-Za-z0-9_.\\/-]+)$/i.exec(userText);
  const requestedFileName = match ? match[1] : undefined;
  if (!requestedFileName) {
    return undefined;
  }

  return graphStatus.model.nodes.find(node => node.group === 'file' && (
    node.label.toLowerCase() === requestedFileName.toLowerCase() ||
    node.id.toLowerCase().endsWith(requestedFileName.toLowerCase())
  ));
}

export function buildAssistantReply(userText: string, graphStatus: GraphStatus, selectedNodeId?: string, fileSymbolNames?: FileSymbolNames): string {
  if (!userText) {
    return 'Please enter a question to explore the graph.';
  }

  const normalized = userText.toLowerCase();
  if (graphStatus.status === 'no-cache') {
    const checked = graphStatus.debug?.length ? ` Checked: ${graphStatus.debug.join(' | ')}` : '';
    return `No HexAware cache is available yet. Generate a cache with hex-generate or run the sample fixture so the graph can populate from live data.${checked}`;
  }

  const selectedNode = selectedNodeId ? graphStatus.model.nodes.find(node => node.id === selectedNodeId) : undefined;
  const matchingFileNode = resolveMatchingFileNode(userText, graphStatus, selectedNodeId);
  const projectSymbolTotal = (graphStatus.totals?.totalFunctions ?? 0) + (graphStatus.totals?.totalClasses ?? 0);

  const asksToList = /what are|which|list|show|name(?:s)? of/.test(normalized);
  const asksForCount = /how many|count|total/.test(normalized);

  const directCounts = [
    { pattern: 'variable', key: 'totalVariables', label: 'variable' },
    { pattern: 'variables', key: 'totalVariables', label: 'variable' },
    { pattern: 'function', key: 'totalFunctions', label: 'function' },
    { pattern: 'functions', key: 'totalFunctions', label: 'function' },
    { pattern: 'class', key: 'totalClasses', label: 'class' },
    { pattern: 'classes', key: 'totalClasses', label: 'class' },
    { pattern: 'symbol', key: 'symbol', label: 'symbol' },
    { pattern: 'symbols', key: 'symbol', label: 'symbol' },
    { pattern: 'file', key: 'totalFiles', label: 'file' },
    { pattern: 'files', key: 'totalFiles', label: 'file' }
  ] as const;

  if (asksToList || asksForCount) {
    for (const item of directCounts) {
      if (!normalized.includes(item.pattern)) {
        continue;
      }

      if (item.key === 'symbol') {
        if (matchingFileNode) {
          if (asksToList && fileSymbolNames) {
            return describeNamedSymbols(matchingFileNode.label, 'symbol', [...fileSymbolNames.functions, ...fileSymbolNames.classes]);
          }

          const summaryNode = graphStatus.model.nodes.find(node =>
            node.group === 'symbol' && graphStatus.model.edges.some(edge => edge.from === matchingFileNode.id && edge.to === node.id)
          );
          const symbolMatch = summaryNode ? /^\s*(\d+)\s+symbol(?:s)?\s*$/i.exec(summaryNode.label) : null;
          const symbolCount = symbolMatch ? Number(symbolMatch[1]) : 0;
          const plural = symbolCount === 1 ? '' : 's';
          return `There are ${symbolCount} symbol${plural} in ${matchingFileNode.label}.`;
        }

        const value = projectSymbolTotal;
        const plural = value === 1 ? '' : 's';
        return `There are ${value} symbol${plural} in this project.`;
      }

      if (matchingFileNode && asksToList && fileSymbolNames && (item.key === 'totalFunctions' || item.key === 'totalClasses')) {
        const names = item.key === 'totalFunctions' ? fileSymbolNames.functions : fileSymbolNames.classes;
        return describeNamedSymbols(matchingFileNode.label, item.label, names);
      }

      const value = graphStatus.totals?.[item.key as keyof NonNullable<GraphStatus['totals']>] ?? 0;
      const plural = value === 1 ? '' : 's';
      return `There are ${value} ${item.label}${plural} in this project.`;
    }
  }

  const selectionContext = selectedNode ? ` The currently selected node is "${selectedNode.label}" (${selectedNode.group}).` : '';
  const relatedEdges = selectedNodeId ? graphStatus.model.edges.filter(edge => edge.from === selectedNodeId || edge.to === selectedNodeId).slice(0, 3) : [];
  const relatedContext = relatedEdges.length
    ? ` Related edges: ${relatedEdges.map(edge => `${edge.from} -> ${edge.to}`).join('; ')}.`
    : '';

  if (normalized.includes('billing') || normalized.includes('invoice')) {
    return `The graph is grounded in the HexQuery overview.${selectionContext} I would inspect the billing-related files and trace the dependency path from the project entry points outward.${relatedContext}`;
  }

  if (normalized.includes('project') || normalized.includes('overview')) {
    return `I would start with the project language groups and then expand the highest-connectivity file nodes to identify the real implementation hotspots.${selectionContext}${relatedContext}`;
  }

  return `HexQuery is the source of truth for this graph.${selectionContext} I would start by identifying the relevant language/project nodes for "${userText}" and then explain the dependency path and the most relevant files involved.${relatedContext}`;
}

function describeNamedSymbols(fileLabel: string, kindLabel: string, names: string[]): string {
  if (names.length === 0) {
    return `${fileLabel} has no ${kindLabel}s in the structural cache.`;
  }
  return `The ${kindLabel}s in ${fileLabel} are: ${names.join(', ')}.`;
}
