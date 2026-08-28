import * as vscode from 'vscode';

/**
 * Activity Bar launcher: an (empty) sidebar view that immediately fires the
 * `hexview.open` command and collapses itself again, so the icon behaves like
 * a button that opens HexView in the main editor area rather than a persistent panel.
 */
export class HexViewLauncherProvider implements vscode.WebviewViewProvider {
  resolveWebviewView(webviewView: vscode.WebviewView): void {
    // No content is ever shown, so skip setting html/options — one less render before we close.
    const launch = () => {
      vscode.commands.executeCommand('hexview.open');
      vscode.commands.executeCommand('workbench.action.closeSidebar');
    };

    if (webviewView.visible) {
      launch();
    }
    webviewView.onDidChangeVisibility(() => {
      if (webviewView.visible) {
        launch();
      }
    });
  }
}
