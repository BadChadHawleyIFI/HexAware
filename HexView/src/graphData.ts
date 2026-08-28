export type GraphNode = {
  id: string;
  label: string;
  group: 'project' | 'file' | 'symbol';
  kind?: string;
  language?: string;
  x?: number;
  y?: number;
};

export type GraphEdge = {
  from: string;
  to: string;
  label?: string;
};

export type GraphModel = {
  nodes: GraphNode[];
  edges: GraphEdge[];
};

export type GraphFilterResult = {
  visibleNodes: GraphNode[];
  visibleEdges: GraphEdge[];
  visibleNodeIds: Set<string>;
};

export function filterGraphModel(model: GraphModel, filter: 'all' | 'project' | 'file' | 'symbol'): GraphFilterResult {
  const visibleNodeIds = new Set<string>();

  for (const node of model.nodes) {
    if (filter === 'all' || node.group === filter) {
      visibleNodeIds.add(node.id);
    }
  }

  const visibleNodes = model.nodes.filter(node => visibleNodeIds.has(node.id));
  const visibleEdges = model.edges.filter(edge => visibleNodeIds.has(edge.from) && visibleNodeIds.has(edge.to));

  return { visibleNodes, visibleEdges, visibleNodeIds };
}

/**
 * Builds the abstract node/edge graph from HexQuery overview data.
 * Node placement is a separate concern, handled by graphLayout.ts.
 */
export function buildGraphModel(raw: any): GraphModel {
  const byLanguage = Array.isArray(raw?.byLanguage) ? raw.byLanguage : [];
  const files = Array.isArray(raw?.files) ? raw.files : [];

  const nodes: GraphNode[] = [];
  const edges: GraphEdge[] = [];

  const languageEntries = byLanguage.filter((entry: any) => entry && entry.language);

  for (const languageEntry of languageEntries) {
    const language = String(languageEntry.language ?? 'unknown');
    const projectNodeId = `project:${language}`;

    nodes.push({
      id: projectNodeId,
      label: language,
      group: 'project',
      kind: 'language',
      language
    });

    const languageFiles = files.filter((file: any) => (file?.language ?? '').toLowerCase() === language.toLowerCase());
    for (let j = 0; j < languageFiles.length; j++) {
      const fileEntry = languageFiles[j];
      const fileKey = String(fileEntry?.file ?? `unknown-${j}`);
      const fileNodeId = `file:${fileKey}`;
      const fileLabel = fileKey.split('/').pop() ?? fileKey;

      nodes.push({
        id: fileNodeId,
        label: fileLabel,
        group: 'file',
        kind: 'file',
        language
      });

      edges.push({ from: projectNodeId, to: fileNodeId, label: 'contains' });

      const symbolCount = Math.max(0, Number(fileEntry?.functionCount ?? 0) + Number(fileEntry?.classCount ?? 0));
      if (symbolCount > 0) {
        const symbolNodeId = `symbol:${fileKey}`;
        nodes.push({
          id: symbolNodeId,
          label: `${symbolCount} symbol${symbolCount === 1 ? '' : 's'}`,
          group: 'symbol',
          kind: 'summary',
          language
        });
        edges.push({ from: fileNodeId, to: symbolNodeId, label: 'contains' });
      }
    }
  }

  return { nodes, edges };
}
