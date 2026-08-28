import test from 'node:test';
import assert from 'node:assert/strict';
import * as path from 'node:path';
import { buildAssistantReply } from './assistant';
import { buildGraphModel, filterGraphModel, type GraphNode, type GraphEdge } from './graphData';
import { resolveRepoRoot } from './repoRoot';

test('buildGraphModel converts HexQuery overview into graph nodes and edges', () => {
  const input = {
    totalFiles: 3,
    totalFunctions: 5,
    totalClasses: 2,
    totalVariables: 1,
    byLanguage: [
      { language: 'csharp', fileCount: 2, functions: 3, classes: 2, variables: 1, sections: 0 },
      { language: 'vbnet', fileCount: 1, functions: 2, classes: 0, variables: 0, sections: 0 }
    ],
    files: [
      { file: 'CSharpLib/Caller.cs', language: 'csharp', functionCount: 2, classCount: 1 },
      { file: 'VbLib/DerivedClass.vb', language: 'vbnet', functionCount: 2, classCount: 0 }
    ]
  };

  const model = buildGraphModel(input as any);

  const nodes = model.nodes as GraphNode[];
  const edges = model.edges as GraphEdge[];

  assert.ok(nodes.some(node => node.id === 'project:csharp' && node.label === 'csharp'));
  assert.ok(nodes.some(node => node.id === 'file:CSharpLib/Caller.cs' && node.group === 'file'));
  assert.ok(edges.some(edge => edge.from === 'project:csharp' && edge.to === 'file:CSharpLib/Caller.cs'));
  assert.equal(nodes.length >= 4, true);
  assert.equal(edges.length >= 2, true);
});

test('resolveRepoRoot prefers the actual extension folder over an incorrect VS Code workspace root', () => {
  const repoRoot = path.resolve(__dirname, '..', '..');
  const wrongWorkspaceRoot = 'C:\\Users\\chhaw\\AppData\\Local\\Programs\\Microsoft VS Code';

  const result = resolveRepoRoot(wrongWorkspaceRoot, path.join(repoRoot, 'HexView'));

  assert.equal(result, repoRoot);
});

test('filterGraphModel keeps nodes and edges aligned for project/file/symbol filters', () => {
  const model = buildGraphModel({
    byLanguage: [{ language: 'csharp', fileCount: 1, functions: 1, classes: 1, variables: 2, sections: 0 }],
    files: [{ file: 'src/Test.cs', language: 'csharp', functionCount: 1, classCount: 1 }]
  } as any);

  const visible = filterGraphModel(model, 'file');

  assert.deepEqual(visible.visibleNodes.map((node: GraphNode) => node.group), ['file']);
  assert.equal(visible.visibleEdges.length, 0);
  assert.equal(visible.visibleNodeIds.has('project:csharp'), false);
  assert.equal(visible.visibleNodeIds.has('file:src/Test.cs'), true);
});

test('buildAssistantReply answers direct totals questions from graph data', () => {
  const reply = buildAssistantReply('How many variables are in the project', {
    source: 'HexQuery',
    status: 'connected',
    model: buildGraphModel({
      byLanguage: [{ language: 'csharp', fileCount: 2, functions: 3, classes: 2, variables: 7, sections: 0 }],
      files: [{ file: 'src/Test.cs', language: 'csharp', functionCount: 2, classCount: 1 }]
    } as any),
    totals: { totalFiles: 2, totalFunctions: 3, totalClasses: 2, totalVariables: 7 }
  });

  assert.match(reply, /There are 7 variables in this project/);
});

test('buildAssistantReply answers selected-file symbol counts from the graph', () => {
  const model = buildGraphModel({
    byLanguage: [{ language: 'vbnet', fileCount: 1, functions: 3, classes: 2, variables: 0, sections: 0 }],
    files: [{ file: 'VbLib/BillingPage.aspx', language: 'vbnet', functionCount: 2, classCount: 1 }]
  } as any);

  const reply = buildAssistantReply('How many symbols are in BillingPage.aspx', {
    source: 'HexQuery',
    status: 'connected',
    model,
    totals: { totalFiles: 1, totalFunctions: 2, totalClasses: 1, totalVariables: 0 }
  }, 'file:VbLib/BillingPage.aspx');

  assert.match(reply, /There are 3 symbols/);
});

