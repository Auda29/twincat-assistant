import { lstat, readFile, realpath } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { applicationRoot, requirePackage, verifyPackageIfPresent } from './package-integrity.mjs';
import { verifyManifest } from '../scripts/application-manifest.mjs';

export function currentPackageSettings(settings) {
  if (!requirePackage) return true;
  return (!settings.knowledge || settings.knowledge.executable === join(applicationRoot, 'runtime/mcp/knowledge/twincat-knowledge.exe'))
    && (!settings.engineering || settings.engineering.entrypoint === join(applicationRoot, 'runtime/mcp/engineering/packages/mcp/dist/index.js'));
}

// Only the installer's retained version directories are eligible. Manifests
// detect corruption, not publisher authenticity. Never execute an older binary.
export async function previousPackageRuntime(actual, settings) {
  if (!requirePackage) return null;
  const servers = actual?.mcp?.servers;
  const commands = [servers?.knowledge?.command, servers?.engineering?.command].filter(Array.isArray);
  const nodes = [...new Set(commands.map(command => command[0]))];
  if (nodes.length !== 1 || typeof nodes[0] !== 'string' || nodes[0] === process.execPath) return null;
  const node = nodes[0];
  const oldRoot = dirname(dirname(dirname(node)));
  const current = resolve(applicationRoot);
  if (node !== join(oldRoot, 'runtime', 'node', 'node.exe') || process.execPath !== join(current, 'runtime', 'node', 'node.exe')) return null;
  const versions = dirname(dirname(current));
  if (dirname(dirname(oldRoot)) !== versions || dirname(current) === dirname(oldRoot)
      || dirname(current) === versions || dirname(oldRoot) === versions
      || current !== join(dirname(current), 'application') || oldRoot !== join(dirname(oldRoot), 'application')
      || versions !== join(dirname(versions), 'versions')) return null;
  const installation = dirname(versions);
  for (const dir of [installation, versions, dirname(current), current, dirname(oldRoot), oldRoot]) {
    const stat = await lstat(dir);
    if (!stat.isDirectory() || stat.isSymbolicLink() || resolve(await realpath(dir)) !== resolve(dir)) throw new Error('Redirected installation version');
  }
  const owner = join(installation, '.assistant-install-root');
  const state = join(installation, 'state.json');
  for (const path of [owner, state]) { const stat = await lstat(path); if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 65536) throw new Error('Invalid installation ownership'); }
  if (await readFile(owner, 'utf8') !== 'TwinCatAssistant installation v1') throw new Error('Invalid installation ownership');
  const selection = JSON.parse(await readFile(state, 'utf8'));
  if (selection.targetId !== 'tcxaeshell-x86-tc4024' || selection.current !== dirname(current).slice(versions.length + 1)) throw new Error('Installation selection mismatch');
  await verifyPackageIfPresent();
  const currentManifest = JSON.parse(await readFile(join(current, 'application-manifest.json'), 'utf8'));
  if (currentManifest.version !== selection.current) throw new Error('Current package identity mismatch');
  const oldManifest = await verifyManifest(oldRoot);
  if (oldManifest.platform !== process.platform || oldManifest.architecture !== process.arch
      || oldManifest.version !== dirname(oldRoot).slice(versions.length + 1)) throw new Error('Previous package identity mismatch');
  const marker = JSON.parse(await readFile(join(oldRoot, 'src', 'package-runtime.json'), 'utf8'));
  if (JSON.stringify(marker) !== JSON.stringify({ version: 1 })) throw new Error('Previous package marker mismatch');
  const required = ['runtime/node/node.exe', 'src/knowledge-client.mjs',
    ...(settings.knowledge ? ['runtime/mcp/knowledge/twincat-knowledge.exe'] : []),
    ...(settings.engineering ? ['runtime/mcp/engineering/packages/mcp/dist/index.js'] : [])];
  for (const manifest of [oldManifest, currentManifest]) {
    const paths = new Set(manifest.files.map(file => file.path));
    if (required.some(path => !paths.has(path))) throw new Error('Required packaged entrypoint missing from inventory');
  }
  const oldKnowledge = join(oldRoot, 'runtime/mcp/knowledge/twincat-knowledge.exe');
  const oldEngineering = join(oldRoot, 'runtime/mcp/engineering/packages/mcp/dist/index.js');
  if (settings.knowledge && settings.knowledge.executable !== oldKnowledge) return null;
  if (settings.engineering && settings.engineering.entrypoint !== oldEngineering) return null;
  return { node, knowledgeClient: join(oldRoot, 'src', 'knowledge-client.mjs') };
}
