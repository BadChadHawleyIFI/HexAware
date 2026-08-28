import * as dagre from '@dagrejs/dagre';
import type { GraphModel, GraphNode } from './graphData';

export type NodeSize = { width: number; height: number };

export const NODE_SIZES: Record<GraphNode['group'], NodeSize> = {
  project: { width: 160, height: 52 },
  file: { width: 200, height: 46 },
  symbol: { width: 140, height: 38 }
};

const LARGE_GRAPH_THRESHOLD = 60;

/**
 * Assigns non-overlapping x/y coordinates to every node using a layered
 * (Sugiyama-style) layout, so project -> file -> symbol containment edges
 * naturally rank into readable top-to-bottom bands with no collisions.
 */
export function computeLayout(model: GraphModel): GraphModel {
  const graph = new dagre.graphlib.Graph();
  const isLarge = model.nodes.length > LARGE_GRAPH_THRESHOLD;

  graph.setGraph({
    rankdir: 'TB',
    nodesep: isLarge ? 70 : 50,
    ranksep: isLarge ? 130 : 100,
    marginx: 30,
    marginy: 30
  });
  graph.setDefaultEdgeLabel(() => ({}));

  for (const node of model.nodes) {
    const size = NODE_SIZES[node.group];
    graph.setNode(node.id, { width: size.width, height: size.height });
  }

  for (const edge of model.edges) {
    if (graph.hasNode(edge.from) && graph.hasNode(edge.to)) {
      graph.setEdge(edge.from, edge.to);
    }
  }

  dagre.layout(graph);

  const positionedNodes = model.nodes.map(node => {
    const position = graph.node(node.id);
    const size = NODE_SIZES[node.group];
    return {
      ...node,
      x: position ? position.x - size.width / 2 : 0,
      y: position ? position.y - size.height / 2 : 0
    };
  });

  return { nodes: positionedNodes, edges: model.edges };
}

export type Bounds = { minX: number; minY: number; maxX: number; maxY: number; width: number; height: number };

export function computeContentBounds(nodes: GraphNode[]): Bounds {
  if (nodes.length === 0) {
    return { minX: 0, minY: 0, maxX: 0, maxY: 0, width: 0, height: 0 };
  }

  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;

  for (const node of nodes) {
    const size = NODE_SIZES[node.group];
    const x = node.x ?? 0;
    const y = node.y ?? 0;
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x + size.width);
    maxY = Math.max(maxY, y + size.height);
  }

  return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
}
