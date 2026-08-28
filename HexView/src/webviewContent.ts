import * as vscode from 'vscode';
import * as crypto from 'node:crypto';
import { filterGraphModel, type GraphNode } from './graphData';
import { computeContentBounds, computeLayout, NODE_SIZES } from './graphLayout';
import { escapeHtml } from './htmlEscape';
import type { GraphStatus } from './hexQueryClient';

function nodeCenter(node: GraphNode): { x: number; y: number } {
  const size = NODE_SIZES[node.group];
  return { x: (node.x ?? 0) + size.width / 2, y: (node.y ?? 0) + size.height / 2 };
}

function renderEdges(edges: { from: string; to: string }[], nodeMap: Map<string, GraphNode>, selectedNodeId?: string): string {
  return edges.map(edge => {
    const fromNode = nodeMap.get(edge.from);
    const toNode = nodeMap.get(edge.to);
    if (!fromNode || !toNode) {
      return '';
    }

    const from = nodeCenter(fromNode);
    const to = nodeCenter(toNode);
    const midX = (from.x + to.x) / 2;
    const isSelectedEdge = !!selectedNodeId && (edge.from === selectedNodeId || edge.to === selectedNodeId);
    const isDimmed = !!selectedNodeId && !isSelectedEdge;
    const stroke = edge.from.startsWith('project:') ? '#5eead4' : '#60a5fa';
    const cssClass = isSelectedEdge ? 'edge-selected' : isDimmed ? 'edge-dimmed' : 'edge-normal';

    return '<path class="' + cssClass + '" data-from="' + escapeHtml(edge.from) + '" data-to="' + escapeHtml(edge.to) + '" ' +
      'd="M ' + from.x + ' ' + from.y + ' C ' + midX + ' ' + from.y + ', ' + midX + ' ' + to.y + ', ' + to.x + ' ' + to.y + '" ' +
      'stroke="' + stroke + '" stroke-width="' + (isSelectedEdge ? '2.5' : '1.5') + '" stroke-linecap="round" fill="none" ' +
      'opacity="' + (isSelectedEdge ? '1' : isDimmed ? '0.15' : '0.82') + '"/>';
  }).join('');
}

function renderNodes(nodes: GraphNode[], selectedNodeId: string | undefined, neighborIds: Set<string>): string {
  return nodes.map(node => {
    const size = NODE_SIZES[node.group];
    const isSelected = selectedNodeId === node.id;
    const isNeighbor = !!selectedNodeId && neighborIds.has(node.id);
    const isDimmed = !!selectedNodeId && !isSelected && !isNeighbor;
    const className = 'node node-' + node.group +
      (isSelected ? ' selected' : '') +
      (isNeighbor ? ' neighbor' : '') +
      (isDimmed ? ' dimmed' : '');

    return '<div class="' + className + '" data-group="' + node.group + '" data-label="' + escapeHtml(node.label) + '" ' +
      'data-node-id="' + escapeHtml(node.id) + '" style="left:' + (node.x ?? 0) + 'px; top:' + (node.y ?? 0) + 'px; ' +
      'width:' + size.width + 'px; height:' + size.height + 'px;">' + escapeHtml(node.label) + '</div>';
  }).join('');
}

export function getWebviewContent(webview: vscode.Webview, graphStatus: GraphStatus, selectedNodeId?: string): string {
  const nonce = crypto.randomBytes(16).toString('base64');
  const layoutModel = computeLayout(graphStatus.model);
  const visibleModel = filterGraphModel(layoutModel, 'all');
  const nodeMap = new Map(layoutModel.nodes.map(node => [node.id, node]));

  const neighborIds = new Set<string>();
  if (selectedNodeId) {
    for (const edge of layoutModel.edges) {
      if (edge.from === selectedNodeId) neighborIds.add(edge.to);
      if (edge.to === selectedNodeId) neighborIds.add(edge.from);
    }
  }

  const selectedNode = selectedNodeId ? nodeMap.get(selectedNodeId) : undefined;
  const edgeMarkup = renderEdges(visibleModel.visibleEdges, nodeMap, selectedNodeId);
  const nodeMarkup = renderNodes(visibleModel.visibleNodes, selectedNodeId, neighborIds);
  const bounds = computeContentBounds(layoutModel.nodes);
  const canvasWidth = Math.max(1200, Math.ceil(bounds.maxX) + 200);
  const canvasHeight = Math.max(800, Math.ceil(bounds.maxY) + 200);

  const statusText = graphStatus.status === 'connected' ? 'CONNECTED' : 'WAITING FOR CACHE';
  const selectionText = selectedNode ? 'Selected: ' + escapeHtml(selectedNode.label) : 'No node selected';
  const selectionCardBody = selectedNode
    ? '<div class="selection-name">' + escapeHtml(selectedNode.label) + '</div>' +
      '<div class="selection-subtitle">' + escapeHtml(selectedNode.group) + ' • ' +
      layoutModel.edges.filter(edge => edge.from === selectedNode.id || edge.to === selectedNode.id).length + ' connected</div>'
    : '<div>No node selected</div>';

  const readyMessage = graphStatus.status === 'connected'
    ? 'HexQuery is connected and the graph is populated from the structural cache.'
    : 'HexView is ready. Generate a HexAware cache to see a live graph.';

  return '<!DOCTYPE html>' +
    '<html lang="en">' +
    '<head>' +
    '<meta charset="UTF-8" />' +
    '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src ' + webview.cspSource + ' \'unsafe-inline\'; script-src \'nonce-' + nonce + '\';" />' +
    '<meta name="viewport" content="width=device-width, initial-scale=1.0" />' +
    '<title>HexView</title>' +
    '<style>' + getStyles() + '</style>' +
    '</head>' +
    '<body>' +
    '<div class="graph-panel">' +
    '<div class="graph-header">' +
    '<h2>HexAware Graph</h2>' +
    '<div class="graph-toolbar">' +
    '<button class="filter-btn active" data-filter="all">All</button>' +
    '<button class="filter-btn" data-filter="project">Projects</button>' +
    '<button class="filter-btn" data-filter="file">Files</button>' +
    '<button class="filter-btn" data-filter="symbol">Symbols</button>' +
    '</div>' +
    '<div class="graph-controls">' +
    '<input id="graph-search" class="graph-search" type="text" placeholder="Search node..." />' +
    '<button id="focus-selection" class="mini-btn" type="button">Focus</button>' +
    '<button id="clear-selection" class="mini-btn" type="button">Fit / Clear</button>' +
    '</div>' +
    '<span class="selection-badge" id="selection-badge">' + selectionText + '</span>' +
    '<span class="status">' + statusText + '</span>' +
    '</div>' +
    '<div class="selection-card' + (selectedNode ? '' : ' empty') + '" id="selection-card">' + selectionCardBody + '</div>' +
    '<div class="graph-area" id="graph-area">' +
    '<div class="graph-surface" id="graph-surface">' +
    '<svg viewBox="0 0 ' + canvasWidth + ' ' + canvasHeight + '" width="' + canvasWidth + '" height="' + canvasHeight + '" preserveAspectRatio="none">' + edgeMarkup + '</svg>' +
    (nodeMarkup || '<div class="node">No data yet</div>') +
    '</div>' +
    '</div>' +
    '</div>' +
    '<div class="chat-panel">' +
    '<div class="chat-header">HexView AI</div>' +
    '<div class="messages" id="messages">' +
    '<div class="message assistant">' + escapeHtml(readyMessage) + '</div>' +
    '</div>' +
    '<div class="composer">' +
    '<input id="prompt" type="text" placeholder="Ask about the graph..." />' +
    '<button id="send">Send</button>' +
    '</div>' +
    '</div>' +
    '<script nonce="' + nonce + '">' + getClientScript() + '</script>' +
    '</body>' +
    '</html>';
}

function getStyles(): string {
  return `
    :root {
      --bg: #0b1324;
      --bg-2: #101b2c;
      --panel: #0f172a;
      --panel-alt: #111827;
      --border: rgba(148, 163, 184, 0.22);
      --text: #e2e8f0;
      --muted: #94a3b8;
      --accent: #38bdf8;
      --accent-2: #34d399;
    }
    * { box-sizing: border-box; }
    html, body { margin: 0; height: 100%; font-family: "Segoe UI", sans-serif; background: var(--bg); color: var(--text); }
    body { display: flex; flex-direction: column; }
    .graph-panel { flex: 1; display: flex; flex-direction: column; background: linear-gradient(180deg, var(--bg) 0%, var(--bg-2) 100%); border-bottom: 1px solid var(--border); position: relative; overflow: hidden; min-height: 0; }
    .graph-header { flex: 0 0 auto; padding: 12px 16px; border-bottom: 1px solid var(--border); background: rgba(15, 23, 42, 0.72); display: flex; align-items: center; gap: 10px; }
    .graph-header h2 { margin: 0; font-size: 13px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--muted); }
    .graph-toolbar { display: flex; gap: 8px; align-items: center; }
    .filter-btn { border: 1px solid var(--border); background: rgba(148,163,184,0.08); color: var(--text); padding: 5px 10px; border-radius: 999px; font-size: 11px; letter-spacing: 0.05em; cursor: pointer; }
    .filter-btn.active { background: rgba(56,189,248,0.18); border-color: rgba(56,189,248,0.8); }
    .status { font-size: 11px; letter-spacing: 0.09em; text-transform: uppercase; color: var(--accent-2); font-weight: 700; margin-left: auto; }
    .selection-badge {
      padding: 5px 10px;
      border-radius: 999px;
      border: 1px solid var(--border);
      background: rgba(148,163,184,0.08);
      color: var(--muted);
      font-size: 11px;
      letter-spacing: 0.04em;
      white-space: nowrap;
    }
    .graph-controls { display: flex; align-items: center; gap: 8px; }
    .graph-search {
      width: 200px;
      background: rgba(15, 23, 42, 0.8);
      border: 1px solid var(--border);
      color: var(--text);
      border-radius: 10px;
      padding: 7px 10px;
      font-size: 12px;
    }
    .mini-btn {
      border: 1px solid var(--border);
      background: rgba(148,163,184,0.08);
      color: var(--text);
      border-radius: 8px;
      padding: 7px 10px;
      font-size: 11px;
      cursor: pointer;
      white-space: nowrap;
    }
    .mini-btn:hover { background: rgba(148,163,184,0.16); }
    .selection-card {
      flex: 0 0 auto;
      margin: 10px 14px 0 14px;
      border: 1px solid var(--border);
      border-radius: 12px;
      background: rgba(15, 23, 42, 0.68);
      padding: 10px 12px;
    }
    .selection-card.empty { color: var(--muted); }
    .selection-name { font-weight: 700; font-size: 13px; }
    .selection-subtitle { margin-top: 4px; color: var(--muted); font-size: 11px; letter-spacing: 0.04em; text-transform: uppercase; }
    .graph-area { flex: 1 1 auto; position: relative; overflow: hidden; cursor: grab; min-height: 0; }
    .graph-area.dragging { cursor: grabbing; }
    .graph-surface {
      position: absolute;
      top: 0;
      left: 0;
      transform-origin: 0 0;
      will-change: transform;
    }
    .graph-surface svg {
      position: absolute; top: 0; left: 0;
      pointer-events: none;
      overflow: visible;
    }
    .node {
      position: absolute;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 4px 12px;
      border-radius: 999px;
      border: 1px solid rgba(148, 163, 184, 0.5);
      background: rgba(17, 24, 39, 0.85);
      color: var(--text);
      font-size: 12px;
      font-weight: 600;
      text-align: center;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      box-shadow: 0 0 0 1px rgba(148, 163, 184, 0.06), 0 8px 18px rgba(15, 23, 42, 0.32);
      cursor: pointer;
      transition: border-color 0.15s ease, box-shadow 0.15s ease;
    }
    .node:hover { border-color: rgba(226, 232, 240, 0.7); }
    .node.selected {
      box-shadow: 0 0 0 1px rgba(56,189,248,0.9), 0 0 0 3px rgba(56,189,248,0.18), 0 12px 24px rgba(56,189,248,0.25);
      border-color: rgba(56,189,248,0.95);
      z-index: 5;
    }
    .node.neighbor {
      box-shadow: 0 0 0 1px rgba(52,211,153,0.8), 0 12px 18px rgba(52,211,153,0.18);
      border-color: rgba(52,211,153,0.88);
      z-index: 4;
    }
    .node.dimmed { opacity: 0.25; filter: saturate(0.6); }
    .node-project { border-color: rgba(96, 165, 250, 0.85); background: rgba(30, 64, 175, 0.22); font-size: 13px; }
    .node-file { border-color: rgba(52, 211, 153, 0.75); background: rgba(5, 80, 50, 0.24); }
    .node-symbol { border-color: rgba(251, 191, 36, 0.8); background: rgba(120, 53, 15, 0.26); font-size: 11px; }
    .edge-selected { filter: drop-shadow(0 0 6px rgba(56,189,248,0.7)); }
    .edge-dimmed { opacity: 0.12 !important; }
    .chat-panel { height: 260px; background: var(--panel); display: flex; flex-direction: column; border-top: 1px solid var(--border); }
    .chat-header { padding: 10px 14px; border-bottom: 1px solid var(--border); font-size: 12px; letter-spacing: 0.08em; text-transform: uppercase; color: var(--muted); }
    .messages { flex: 1; overflow: auto; padding: 12px; display: flex; flex-direction: column; gap: 8px; }
    .message { max-width: 75%; padding: 9px 12px; border-radius: 12px; line-height: 1.4; font-size: 13px; border: 1px solid var(--border); }
    .message.user { align-self: flex-end; background: rgba(59,130,246,0.18); }
    .message.assistant { align-self: flex-start; background: rgba(16,185,129,0.12); }
    .message p { margin: 0 0 6px 0; }
    .message p:last-child { margin-bottom: 0; }
    .message strong { font-weight: 700; }
    .message em { font-style: italic; }
    .message code { background: rgba(148,163,184,0.18); padding: 1px 5px; border-radius: 4px; font-family: "Cascadia Code", Consolas, monospace; font-size: 12px; }
    .message pre { background: rgba(15,23,42,0.55); padding: 8px 10px; border-radius: 8px; overflow-x: auto; margin: 6px 0; }
    .message pre code { background: none; padding: 0; }
    .message ul, .message ol { margin: 6px 0; padding-left: 18px; }
    .message li { margin: 2px 0; }
    .message h4, .message h5, .message h6 { margin: 4px 0; }
    .composer { display: flex; gap: 8px; padding: 12px; border-top: 1px solid var(--border); background: rgba(17,24,39,0.9); }
    .composer input { flex: 1; background: var(--panel-alt); color: var(--text); border: 1px solid var(--border); border-radius: 10px; padding: 10px 12px; font-size: 14px; }
    .composer button { background: linear-gradient(135deg, #38bdf8, #2563eb); color: white; border: none; border-radius: 10px; padding: 10px 18px; font-weight: 700; cursor: pointer; }
  `;
}

/**
 * Client-side script for the webview. Written with string concatenation
 * (never `${}` template interpolation) because this text lives inside a
 * TypeScript template literal one level up in getWebviewContent's caller.
 */
function getClientScript(): string {
  return [
    "const vscode = acquireVsCodeApi();",
    "const messages = document.getElementById('messages');",
    "const input = document.getElementById('prompt');",
    "const sendButton = document.getElementById('send');",
    "const searchInput = document.getElementById('graph-search');",
    "const clearButton = document.getElementById('clear-selection');",
    "const focusButton = document.getElementById('focus-selection');",
    "const graphArea = document.getElementById('graph-area');",
    "const surface = document.getElementById('graph-surface');",
    "const selectionBadge = document.getElementById('selection-badge');",
    "const selectionCard = document.getElementById('selection-card');",
    "let currentFilter = 'all';",
    "const viewState = { x: 0, y: 0, scale: 1 };",
    "",
    "function applyViewState() {",
    "  surface.style.transform = 'translate(' + viewState.x + 'px, ' + viewState.y + 'px) scale(' + viewState.scale + ')';",
    "}",
    "",
    "function updateSelectionPanel(node) {",
    "  selectionCard.textContent = '';",
    "  if (!node) {",
    "    selectionBadge.textContent = 'No node selected';",
    "    selectionCard.classList.add('empty');",
    "    const empty = document.createElement('div');",
    "    empty.textContent = 'No node selected';",
    "    selectionCard.appendChild(empty);",
    "    return;",
    "  }",
    "  const nodeId = node.dataset.nodeId;",
    "  const connectedCount = Array.from(document.querySelectorAll('svg path')).filter(p => p.dataset.from === nodeId || p.dataset.to === nodeId).length;",
    "  selectionBadge.textContent = 'Selected: ' + node.dataset.label;",
    "  selectionCard.classList.remove('empty');",
    "  const name = document.createElement('div');",
    "  name.className = 'selection-name';",
    "  name.textContent = node.dataset.label;",
    "  const subtitle = document.createElement('div');",
    "  subtitle.className = 'selection-subtitle';",
    "  subtitle.textContent = node.dataset.group + ' \u2022 ' + connectedCount + ' connected';",
    "  selectionCard.appendChild(name);",
    "  selectionCard.appendChild(subtitle);",
    "}",
    "",
    "function selectNode(nodeId) {",
    "  const selected = nodeId ? String(nodeId) : '';",
    "  const neighborIds = new Set();",
    "  if (selected) {",
    "    document.querySelectorAll('svg path').forEach(path => {",
    "      if (path.dataset.from === selected) neighborIds.add(path.dataset.to);",
    "      if (path.dataset.to === selected) neighborIds.add(path.dataset.from);",
    "    });",
    "  }",
    "",
    "  let selectedNode = null;",
    "  document.querySelectorAll('.node').forEach(node => {",
    "    const isSelected = node.dataset.nodeId === selected;",
    "    const isNeighbor = !!selected && neighborIds.has(node.dataset.nodeId);",
    "    node.classList.toggle('selected', isSelected);",
    "    node.classList.toggle('neighbor', isNeighbor);",
    "    node.classList.toggle('dimmed', !!selected && !isSelected && !isNeighbor);",
    "    if (isSelected) selectedNode = node;",
    "  });",
    "",
    "  document.querySelectorAll('svg path').forEach(path => {",
    "    const isSelectedEdge = !!selected && (path.dataset.from === selected || path.dataset.to === selected);",
    "    const isDimmed = !!selected && !isSelectedEdge;",
    "    path.classList.toggle('edge-selected', isSelectedEdge);",
    "    path.classList.toggle('edge-dimmed', isDimmed);",
    "  });",
    "",
    "  updateSelectionPanel(selectedNode);",
    "  vscode.postMessage({ command: 'selectNode', nodeId: selected || '' });",
    "}",
    "",
    "function syncEdgeGeometry() {",
    "  const nodesById = new Map(Array.from(document.querySelectorAll('.node')).map(node => [node.dataset.nodeId, node]));",
    "  Array.from(document.querySelectorAll('svg path')).forEach(path => {",
    "    const fromNode = nodesById.get(path.dataset.from);",
    "    const toNode = nodesById.get(path.dataset.to);",
    "    if (!fromNode || !toNode) { path.setAttribute('d', 'M 0 0'); return; }",
    "    const fromX = parseFloat(fromNode.style.left || '0') + (fromNode.offsetWidth || 120) / 2;",
    "    const fromY = parseFloat(fromNode.style.top || '0') + (fromNode.offsetHeight || 42) / 2;",
    "    const toX = parseFloat(toNode.style.left || '0') + (toNode.offsetWidth || 120) / 2;",
    "    const toY = parseFloat(toNode.style.top || '0') + (toNode.offsetHeight || 42) / 2;",
    "    const midX = (fromX + toX) / 2;",
    "    path.setAttribute('d', 'M ' + fromX + ' ' + fromY + ' C ' + midX + ' ' + fromY + ', ' + midX + ' ' + toY + ', ' + toX + ' ' + toY);",
    "  });",
    "}",
    "",
    "function computeVisibleBounds() {",
    "  const nodes = Array.from(document.querySelectorAll('.node')).filter(node => node.style.display !== 'none');",
    "  if (!nodes.length) return null;",
    "  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;",
    "  nodes.forEach(node => {",
    "    const x = parseFloat(node.style.left || '0');",
    "    const y = parseFloat(node.style.top || '0');",
    "    const w = node.offsetWidth || 120;",
    "    const h = node.offsetHeight || 40;",
    "    minX = Math.min(minX, x); minY = Math.min(minY, y);",
    "    maxX = Math.max(maxX, x + w); maxY = Math.max(maxY, y + h);",
    "  });",
    "  return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };",
    "}",
    "",
    "function fitToContent() {",
    "  const bounds = computeVisibleBounds();",
    "  if (!bounds || bounds.width <= 0 || bounds.height <= 0) return;",
    "  const padding = 70;",
    "  const availableWidth = Math.max(1, graphArea.clientWidth - padding * 2);",
    "  const availableHeight = Math.max(1, graphArea.clientHeight - padding * 2);",
    "  const scale = Math.min(1.1, Math.max(0.2, Math.min(availableWidth / bounds.width, availableHeight / bounds.height)));",
    "  const centerX = bounds.minX + bounds.width / 2;",
    "  const centerY = bounds.minY + bounds.height / 2;",
    "  viewState.scale = scale;",
    "  viewState.x = (graphArea.clientWidth / 2) - centerX * scale;",
    "  viewState.y = (graphArea.clientHeight / 2) - centerY * scale;",
    "  applyViewState();",
    "}",
    "",
    "function focusSelection() {",
    "  const selected = document.querySelector('.node.selected');",
    "  if (!selected) return;",
    "  const left = parseFloat(selected.style.left || '0');",
    "  const top = parseFloat(selected.style.top || '0');",
    "  const width = selected.offsetWidth || 180;",
    "  const height = selected.offsetHeight || 42;",
    "  const centerX = left + width / 2;",
    "  const centerY = top + height / 2;",
    "  viewState.scale = Math.max(viewState.scale, 1);",
    "  viewState.x = (graphArea.clientWidth / 2) - centerX * viewState.scale;",
    "  viewState.y = (graphArea.clientHeight / 2) - centerY * viewState.scale;",
    "  applyViewState();",
    "}",
    "",
    "function applyFilter(filter) {",
    "  currentFilter = filter;",
    "  document.querySelectorAll('.filter-btn').forEach(btn => btn.classList.toggle('active', btn.dataset.filter === filter));",
    "  const visibleNodeIds = new Set();",
    "  document.querySelectorAll('.node').forEach(node => {",
    "    const shouldShow = filter === 'all' || node.dataset.group === filter;",
    "    node.style.display = shouldShow ? 'flex' : 'none';",
    "    if (shouldShow) visibleNodeIds.add(node.dataset.nodeId);",
    "  });",
    "  document.querySelectorAll('svg path').forEach(path => {",
    "    const shouldShowEdge = visibleNodeIds.has(path.dataset.from) && visibleNodeIds.has(path.dataset.to);",
    "    path.style.display = shouldShowEdge ? 'block' : 'none';",
    "  });",
    "  syncEdgeGeometry();",
    "  fitToContent();",
    "}",
    "",
    "function addMessage(role, text, html) {",
    "  const el = document.createElement('div');",
    "  el.className = 'message ' + role;",
    "  if (html) { el.innerHTML = html; } else { el.textContent = text; }",
    "  messages.appendChild(el);",
    "  messages.scrollTop = messages.scrollHeight;",
    "}",
    "",
    "document.querySelectorAll('.filter-btn').forEach(button => {",
    "  button.addEventListener('click', () => applyFilter(button.dataset.filter));",
    "});",
    "",
    "document.querySelectorAll('.node').forEach(node => {",
    "  node.addEventListener('click', () => {",
    "    selectNode(node.dataset.nodeId);",
    "    focusSelection();",
    "  });",
    "});",
    "",
    "let isDragging = false;",
    "let dragStartX = 0, dragStartY = 0, startViewX = 0, startViewY = 0;",
    "graphArea.addEventListener('mousedown', (event) => {",
    "  if (event.target.closest('.node')) return;",
    "  isDragging = true;",
    "  dragStartX = event.clientX; dragStartY = event.clientY;",
    "  startViewX = viewState.x; startViewY = viewState.y;",
    "  graphArea.classList.add('dragging');",
    "});",
    "window.addEventListener('mousemove', (event) => {",
    "  if (!isDragging) return;",
    "  viewState.x = startViewX + (event.clientX - dragStartX);",
    "  viewState.y = startViewY + (event.clientY - dragStartY);",
    "  applyViewState();",
    "});",
    "window.addEventListener('mouseup', () => { isDragging = false; graphArea.classList.remove('dragging'); });",
    "",
    "graphArea.addEventListener('wheel', (event) => {",
    "  event.preventDefault();",
    "  const delta = event.deltaY > 0 ? -0.08 : 0.08;",
    "  viewState.scale = Math.min(2.2, Math.max(0.2, viewState.scale + delta));",
    "  applyViewState();",
    "}, { passive: false });",
    "",
    "window.addEventListener('resize', () => { syncEdgeGeometry(); fitToContent(); });",
    "",
    "searchInput.addEventListener('keydown', (event) => {",
    "  if (event.key !== 'Enter') return;",
    "  const query = searchInput.value.trim().toLowerCase();",
    "  if (!query) return;",
    "  const match = Array.from(document.querySelectorAll('.node')).find(node => node.dataset.label.toLowerCase().includes(query));",
    "  if (!match) return;",
    "  selectNode(match.dataset.nodeId);",
    "  focusSelection();",
    "});",
    "",
    "focusButton.addEventListener('click', () => focusSelection());",
    "clearButton.addEventListener('click', () => { selectNode(''); fitToContent(); });",
    "",
    "sendButton.addEventListener('click', () => {",
    "  const value = input.value.trim();",
    "  if (!value) return;",
    "  addMessage('user', value);",
    "  vscode.postMessage({ command: 'sendMessage', text: value });",
    "  input.value = '';",
    "});",
    "",
    "input.addEventListener('keydown', (event) => { if (event.key === 'Enter') sendButton.click(); });",
    "",
    "window.addEventListener('message', event => {",
    "  const message = event.data;",
    "  if (message.command === 'addMessage') addMessage(message.role, message.text, message.html);",
    "});",
    "",
    "syncEdgeGeometry();",
    "fitToContent();"
  ].join('\n');
}
