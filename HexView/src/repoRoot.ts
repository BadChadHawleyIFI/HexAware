import * as fs from 'node:fs';
import * as path from 'node:path';

export function exists(filePath: string): boolean {
  try {
    return fs.existsSync(filePath);
  } catch {
    return false;
  }
}

export function resolveRepoRoot(currentPath: string, extensionRoot?: string): string {
  const searchRoots = [currentPath];
  if (extensionRoot) {
    searchRoots.push(extensionRoot);
  }

  for (const base of searchRoots) {
    let dir = base;
    while (true) {
      const candidate = path.join(dir, 'HexGenerate', 'HexGenerate.csproj');
      if (exists(candidate)) {
        return dir;
      }

      const parent = path.dirname(dir);
      if (parent === dir) {
        break;
      }
      dir = parent;
    }
  }

  return currentPath;
}
