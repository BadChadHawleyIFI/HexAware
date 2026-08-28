import * as path from 'node:path';
import * as fs from 'node:fs';
import { execFile } from 'node:child_process';
import type { GraphModel } from './graphData';
import { buildGraphModel } from './graphData';
import { exists, resolveRepoRoot } from './repoRoot';

export type GraphTotals = {
  totalFiles?: number;
  totalFunctions?: number;
  totalClasses?: number;
  totalVariables?: number;
};

export type GraphStatus = {
  source: string;
  status: 'connected' | 'no-cache';
  model: GraphModel;
  totals?: GraphTotals;
  debug?: string[];
  cachePath?: string;
  repoRoot?: string;
};

/** Loads the HexQuery overview for the active workspace and converts it into a graph model. */
export async function loadHexQueryGraph(workspaceRoot: string, extensionRoot: string): Promise<GraphStatus> {
  const repoRoot = resolveRepoRoot(workspaceRoot, extensionRoot);
  const fixtureCache = path.join(repoRoot, 'fixtures', 'SampleLegacyApp', '.ha', 'HexAware-cache.db');
  const repoCache = path.join(repoRoot, '.ha', 'HexAware-cache.db');
  const workspaceCache = path.join(workspaceRoot, '.ha', 'HexAware-cache.db');
  const cacheCandidates = [fixtureCache, repoCache, workspaceCache];

  const existingCache = cacheCandidates.find(candidate => exists(candidate));
  if (existingCache) {
    const status = await tryLoadFromCache(existingCache, repoRoot, cacheCandidates);
    if (status) {
      return status;
    }
  }

  const generatedCache = await ensureDemoCacheExists(repoRoot);
  if (generatedCache) {
    const status = await tryLoadFromCache(generatedCache, repoRoot, cacheCandidates.concat(generatedCache));
    if (status) {
      return status;
    }
  }

  return buildNoCacheStatus(cacheCandidates);
}

async function tryLoadFromCache(cachePath: string, repoRoot: string, debugCandidates: string[]): Promise<GraphStatus | undefined> {
  try {
    const raw = await executeHexQuery(['--overview', '--cache', cachePath], repoRoot);
    if (raw && raw.byLanguage) {
      return { source: 'HexQuery', status: 'connected', model: buildGraphModel(raw), totals: resolveTotals(raw), debug: debugCandidates, cachePath, repoRoot };
    }
  } catch (error) {
    console.warn('[HexView] HexQuery failed for cache:', cachePath, error);
  }
  return undefined;
}

function buildNoCacheStatus(debugCandidates: string[]): GraphStatus {
  return {
    source: 'fallback',
    status: 'no-cache',
    debug: debugCandidates,
    model: buildGraphModel({
      byLanguage: [{ language: 'unknown', fileCount: 1, functions: 0, classes: 0, variables: 0, sections: 0 }],
      files: [{ file: 'No cache found', language: 'unknown', functionCount: 0, classCount: 0 }]
    }),
    totals: { totalFiles: 0, totalFunctions: 0, totalClasses: 0, totalVariables: 0 }
  };
}

function resolveTotals(raw: any): GraphTotals {
  return {
    totalFiles: Number(raw?.totalFiles ?? 0),
    totalFunctions: Number(raw?.totalFunctions ?? 0),
    totalClasses: Number(raw?.totalClasses ?? 0),
    totalVariables: Number(raw?.totalVariables ?? 0)
  };
}

export function resolveDotNetExecutable(): string {
  const candidates: string[] = [];

  const envPath = process.env.PATH || '';
  for (const entry of envPath.split(path.delimiter)) {
    if (entry) {
      candidates.push(path.join(entry, 'dotnet.exe'));
      candidates.push(path.join(entry, 'dotnet'));
    }
  }

  candidates.push('C:\\Program Files\\dotnet\\dotnet.exe', '/usr/share/dotnet/dotnet', '/usr/local/share/dotnet/dotnet');

  return candidates.find(candidate => fs.existsSync(candidate)) || 'dotnet';
}

async function ensureDemoCacheExists(repoRoot: string): Promise<string | null> {
  const sampleSln = path.join(repoRoot, 'fixtures', 'SampleLegacyApp', 'SampleLegacyApp.sln');
  if (!exists(sampleSln)) {
    return null;
  }

  const generatedCache = path.join(repoRoot, 'fixtures', 'SampleLegacyApp', '.ha', 'HexAware-cache.db');
  if (exists(generatedCache)) {
    return generatedCache;
  }

  const hexGenerateProject = path.join(repoRoot, 'HexGenerate', 'HexGenerate.csproj');
  const dotnetExecutable = resolveDotNetExecutable();

  await new Promise<void>((resolve, reject) => {
    execFile(dotnetExecutable, ['run', '--project', hexGenerateProject, '--', '--sln', sampleSln, '--output', generatedCache], { cwd: repoRoot }, (error, _stdout, stderr) => {
      if (error) {
        reject(new Error(stderr || error.message));
        return;
      }
      resolve();
    });
  });

  return exists(generatedCache) ? generatedCache : null;
}

/** Runs HexQuery and returns raw stdout text, without assuming the output is JSON (e.g. --docs is plain text). */
export function runHexQueryCommand(args: string[], repoRoot: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const hexQueryProject = path.join(repoRoot, 'HexQuery', 'HexQuery.csproj');
    const dotnetExecutable = resolveDotNetExecutable();
    execFile(dotnetExecutable, ['run', '--project', hexQueryProject, '--', ...args], { cwd: repoRoot, timeout: 30000, maxBuffer: 5 * 1024 * 1024 }, (error, stdout, stderr) => {
      if (error) {
        reject(new Error(stderr || error.message));
        return;
      }
      resolve((stdout || '').trim());
    });
  });
}

async function executeHexQuery(args: string[], cwd: string): Promise<any> {
  const text = await runHexQueryCommand(args, cwd);
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`Failed to parse HexQuery output: ${text}`);
  }
}

const MAX_TOOL_ARG_ITEMS = 12;
const MAX_TOOL_ARG_LENGTH = 300;

/** Keeps model-supplied HexQuery CLI args well-formed and drops any --cache flag along with its value. */
export function sanitizeToolArgs(rawArgs: unknown): string[] {
  if (!Array.isArray(rawArgs)) {
    return [];
  }

  const cleaned = rawArgs
    .filter((item): item is string => typeof item === 'string')
    .map(item => item.slice(0, MAX_TOOL_ARG_LENGTH))
    .map(item => item.trim())
    .filter(item => item.length > 0);

  const result: string[] = [];
  for (let i = 0; i < cleaned.length; i++) {
    if (cleaned[i].toLowerCase() === '--cache') {
      i++; // also drop the value that follows this flag
      continue;
    }
    result.push(cleaned[i]);
  }

  return result.slice(0, MAX_TOOL_ARG_ITEMS);
}

export type FileSymbolNames = { functions: string[]; classes: string[] };

/** Fetches the real function/class names for one file on demand, for direct chat answers. */
export async function queryFileSymbols(cachePath: string, repoRoot: string, filePath: string): Promise<FileSymbolNames | undefined> {
  try {
    const raw = await executeHexQuery(['--file', filePath, '--cache', cachePath], repoRoot);
    const match = Array.isArray(raw) ? raw[0] : undefined;
    if (!match) {
      return undefined;
    }
    const functions = Array.isArray(match.functions) ? match.functions.map((f: any) => String(f?.name ?? '')).filter(Boolean) : [];
    const classes = Array.isArray(match.classes) ? match.classes.map((c: any) => String(c?.name ?? '')).filter(Boolean) : [];
    return { functions, classes };
  } catch (error) {
    console.warn('[HexView] HexQuery --file failed for:', filePath, error);
    return undefined;
  }
}

