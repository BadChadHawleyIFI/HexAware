import * as path from 'node:path';
import * as vscode from 'vscode';
import { answerWithHexQueryAgent } from './aiAssistant';
import { buildAssistantReply, resolveMatchingFileNode } from './assistant';
import { HexViewLauncherProvider } from './hexViewLauncherProvider';
import { loadHexQueryGraph, queryFileSymbols, type GraphStatus } from './hexQueryClient';
import { renderMarkdownToSafeHtml } from './markdown';
import { getWebviewContent } from './webviewContent';

export function activate(context: vscode.ExtensionContext) {
  const disposable = vscode.commands.registerCommand('hexview.open', async () => {
    const panel = vscode.window.createWebviewPanel(
      'hexview',
      'HexView',
      vscode.ViewColumn.One,
      { enableScripts: true, localResourceRoots: [] }
    );

    let selectedNodeId: string | undefined;
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd();
    const extensionRoot = path.resolve(__dirname, '..');
    const graphStatus: GraphStatus = await loadHexQueryGraph(workspaceRoot, extensionRoot);

    panel.webview.html = getWebviewContent(panel.webview, graphStatus, selectedNodeId);

    panel.webview.onDidReceiveMessage(async (message) => {
      if (message.command === 'selectNode') {
        selectedNodeId = String(message.nodeId ?? '') || undefined;
        return;
      }

      if (message.command === 'sendMessage') {
        const userText = String(message.text ?? '').trim();
        let assistantReply = await answerWithHexQueryAgent(userText, graphStatus, selectedNodeId);

        if (!assistantReply) {
          const matchingFileNode = resolveMatchingFileNode(userText, graphStatus, selectedNodeId);
          const fileSymbolNames = matchingFileNode && graphStatus.cachePath && graphStatus.repoRoot
            ? await queryFileSymbols(graphStatus.cachePath, graphStatus.repoRoot, matchingFileNode.id.replace(/^file:/, ''))
            : undefined;
          assistantReply = buildAssistantReply(userText, graphStatus, selectedNodeId, fileSymbolNames);
        }

        panel.webview.postMessage({ command: 'addMessage', role: 'assistant', html: renderMarkdownToSafeHtml(assistantReply) });
      }
    });
  });

  context.subscriptions.push(disposable);
  context.subscriptions.push(vscode.window.registerWebviewViewProvider('hexview.launcher', new HexViewLauncherProvider()));
}

export function deactivate() { }


