import * as vscode from 'vscode';
import { runHexQueryCommand, sanitizeToolArgs, type GraphStatus } from './hexQueryClient';

const TOOL_NAME = 'hexquery';
const MAX_TOOL_ITERATIONS = 6;
const MAX_DOCS_LENGTH = 6000;

let cachedDocs: string | undefined;

async function getHexQueryDocs(repoRoot: string): Promise<string> {
  if (cachedDocs) {
    return cachedDocs;
  }

  try {
    cachedDocs = (await runHexQueryCommand(['--docs'], repoRoot)).slice(0, MAX_DOCS_LENGTH);
  } catch {
    cachedDocs = 'Flags: --overview, --file <path>, --method <name>, --class <name>, --search <text>, ' +
      '--entrypoints, --hotspots, --projects, --project <name>, --packages, --assemblies, --assembly <name>.';
  }

  return cachedDocs;
}

function buildTool(): vscode.LanguageModelChatTool {
  return {
    name: TOOL_NAME,
    description: 'Runs the local HexQuery CLI against this project\'s structural cache and returns its output. ' +
      'This is the only source of facts about the codebase — use it for any question about files, functions, ' +
      'classes, variables, symbols, dependencies, projects, or packages, even if it takes multiple calls.',
    inputSchema: {
      type: 'object',
      properties: {
        args: {
          type: 'array',
          items: { type: 'string' },
          description: 'HexQuery CLI flags and values, e.g. ["--file", "VbLib/BillingPage.aspx.vb"] or ["--search", "CalculateTax"]. Never include --cache.'
        }
      },
      required: ['args']
    }
  };
}

/**
 * Answers a natural-language question by letting a Copilot/LM model decide which HexQuery
 * commands to run (possibly several), grounding every answer in real tool output.
 * Returns undefined if no language model is available, so callers can fall back gracefully.
 */
export async function answerWithHexQueryAgent(
  userText: string,
  graphStatus: GraphStatus,
  selectedNodeId: string | undefined
): Promise<string | undefined> {
  if (!graphStatus.cachePath || !graphStatus.repoRoot || typeof vscode.lm?.selectChatModels !== 'function') {
    return undefined;
  }

  const cachePath = graphStatus.cachePath;
  const repoRoot = graphStatus.repoRoot;

  const preferred = await vscode.lm.selectChatModels({ vendor: 'copilot' });
  const model = preferred[0] ?? (await vscode.lm.selectChatModels())[0];
  if (!model) {
    return undefined;
  }

  const docs = await getHexQueryDocs(repoRoot);
  const selectedNode = selectedNodeId ? graphStatus.model.nodes.find(node => node.id === selectedNodeId) : undefined;
  const selectionNote = selectedNode
    ? `The user currently has "${selectedNode.label}" (${selectedNode.group}) selected in the graph.`
    : 'No node is currently selected in the graph.';

  const instructions = [
    'You are HexView\'s assistant, embedded in a VS Code graph explorer for a codebase analyzed by the HexAware toolchain.',
    'Answer the user\'s question using ONLY facts obtained by calling the "hexquery" tool. Never guess file, function, or class names from general knowledge.',
    'You may call the tool multiple times to gather everything needed before giving a final answer.',
    'The cache path is configured automatically — never pass a --cache flag yourself.',
    selectionNote,
    'Below is the HexQuery CLI\'s own --docs output describing every available flag:',
    docs
  ].join('\n');

  const messages: vscode.LanguageModelChatMessage[] = [
    vscode.LanguageModelChatMessage.User(instructions + '\n\nQuestion: ' + userText)
  ];

  for (let iteration = 0; iteration < MAX_TOOL_ITERATIONS; iteration++) {
    let response: vscode.LanguageModelChatResponse;
    try {
      response = await model.sendRequest(messages, { tools: [buildTool()] });
    } catch (error) {
      console.warn('[HexView] Language model request failed:', error);
      return undefined;
    }

    let text = '';
    const toolCalls: vscode.LanguageModelToolCallPart[] = [];
    for await (const part of response.stream) {
      if (part instanceof vscode.LanguageModelTextPart) {
        text += part.value;
      } else if (part instanceof vscode.LanguageModelToolCallPart) {
        toolCalls.push(part);
      }
    }

    if (toolCalls.length === 0) {
      return text.trim() || undefined;
    }

    messages.push(vscode.LanguageModelChatMessage.Assistant(toolCalls));

    const resultParts: vscode.LanguageModelToolResultPart[] = [];
    for (const call of toolCalls) {
      const args = sanitizeToolArgs((call.input as { args?: unknown } | undefined)?.args);
      let output: string;
      try {
        output = await runHexQueryCommand([...args, '--cache', cachePath], repoRoot);
      } catch (error) {
        output = `Error running HexQuery: ${error instanceof Error ? error.message : String(error)}`;
      }
      resultParts.push(new vscode.LanguageModelToolResultPart(call.callId, [new vscode.LanguageModelTextPart(output || '(no output)')]));
    }
    messages.push(vscode.LanguageModelChatMessage.User(resultParts));
  }

  return 'I gathered data from HexQuery but ran out of steps before finishing — try a more specific question.';
}
