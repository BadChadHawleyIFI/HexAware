import test from 'node:test';
import assert from 'node:assert/strict';
import { buildGraphModel } from './graphData';
import { computeLayout, NODE_SIZES } from './graphLayout';

function rectanglesOverlap(
  a: { x: number; y: number; width: number; height: number },
  b: { x: number; y: number; width: number; height: number }
): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x &&
    a.y < b.y + b.height && a.y + a.height > b.y;
}

function buildSampleRaw() {
  return {
    byLanguage: [
      { language: 'vbnet', fileCount: 3, functions: 3, classes: 2, variables: 0, sections: 0 },
      { language: 'csharp', fileCount: 3, functions: 4, classes: 1, variables: 0, sections: 0 }
    ],
    files: [
      { file: 'A.vb', language: 'vbnet', functionCount: 1, classCount: 1 },
      { file: 'B.vb', language: 'vbnet', functionCount: 1, classCount: 1 },
      { file: 'C.vb', language: 'vbnet', functionCount: 1, classCount: 1 },
      { file: 'D.cs', language: 'csharp', functionCount: 2, classCount: 1 },
      { file: 'E.cs', language: 'csharp', functionCount: 1, classCount: 1 },
      { file: 'F.cs', language: 'csharp', functionCount: 1, classCount: 0 }
    ]
  };
}

test('computeLayout produces non-overlapping node positions', () => {
  const model = buildGraphModel(buildSampleRaw() as any);
  const layout = computeLayout(model);

  const rects = layout.nodes.map(node => {
    const size = NODE_SIZES[node.group];
    return { x: node.x ?? 0, y: node.y ?? 0, width: size.width, height: size.height };
  });

  for (let i = 0; i < rects.length; i++) {
    for (let j = i + 1; j < rects.length; j++) {
      assert.equal(rectanglesOverlap(rects[i], rects[j]), false, `nodes ${i} and ${j} must not overlap`);
    }
  }
});

test('computeLayout is deterministic for identical input', () => {
  const model = buildGraphModel(buildSampleRaw() as any);
  const first = computeLayout(model);
  const second = computeLayout(model);

  assert.deepEqual(
    first.nodes.map(node => ({ id: node.id, x: node.x, y: node.y })),
    second.nodes.map(node => ({ id: node.id, x: node.x, y: node.y }))
  );
});

test('computeLayout ranks project nodes above their file and symbol children', () => {
  const model = buildGraphModel(buildSampleRaw() as any);
  const layout = computeLayout(model);
  const byId = new Map(layout.nodes.map(node => [node.id, node]));

  for (const edge of layout.edges) {
    const from = byId.get(edge.from);
    const to = byId.get(edge.to);
    assert.ok(from && to, 'edge endpoints must exist in the layout');
    assert.ok((from!.y ?? 0) < (to!.y ?? 0), `${edge.from} should rank above ${edge.to}`);
  }
});

test('computeLayout scales to a larger graph without collisions', () => {
  const byLanguage = [
    { language: 'vbnet', fileCount: 20, functions: 20, classes: 20, variables: 0, sections: 0 },
    { language: 'csharp', fileCount: 20, functions: 20, classes: 20, variables: 0, sections: 0 }
  ];
  const files = [];
  for (let i = 0; i < 20; i++) {
    files.push({ file: `Vb${i}.vb`, language: 'vbnet', functionCount: 1, classCount: 1 });
    files.push({ file: `Cs${i}.cs`, language: 'csharp', functionCount: 1, classCount: 1 });
  }

  const model = buildGraphModel({ byLanguage, files } as any);
  const layout = computeLayout(model);

  const rects = layout.nodes.map(node => {
    const size = NODE_SIZES[node.group];
    return { x: node.x ?? 0, y: node.y ?? 0, width: size.width, height: size.height };
  });

  let overlapCount = 0;
  for (let i = 0; i < rects.length; i++) {
    for (let j = i + 1; j < rects.length; j++) {
      if (rectanglesOverlap(rects[i], rects[j])) {
        overlapCount++;
      }
    }
  }

  assert.equal(overlapCount, 0, 'large graphs must remain collision-free');
});
