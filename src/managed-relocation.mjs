import { lstat, readFile, realpath } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { applicationRoot, requirePackage, verifyPackageIfPresent } from './package-integrity.mjs';
import { verifyOperationManifest } from './package-verification-scope.mjs';

const samePath = (a, b) => typeof a === 'string' && typeof b === 'string'
  && (process.platform === 'win32' ? resolve(a).toLowerCase() === resolve(b).toLowerCase() : resolve(a) === resolve(b));

export function currentPackageSettings(settings) {
  if (!requirePackage) return true;
  return (!settings.knowledge || samePath(settings.knowledge.executable, join(applicationRoot, 'runtime/mcp/knowledge/twincat-knowledge.exe')))
    && (!settings.engineering || samePath(settings.engineering.entrypoint, join(applicationRoot, 'runtime/mcp/engineering/packages/mcp/dist/index.js')));
}

// Called only after inspection has established the old managed configuration.
// Operator corpus, provider consent and every non-executable setting survive.
export function rebasePackageSettings(settings) {
  if (!requirePackage) return settings;
  return { ...settings,
    ...(settings.knowledge ? { knowledge: { ...settings.knowledge, executable: join(applicationRoot, 'runtime/mcp/knowledge/twincat-knowledge.exe') } } : {}),
    ...(settings.engineering ? { engineering: { ...settings.engineering, entrypoint: join(applicationRoot, 'runtime/mcp/engineering/packages/mcp/dist/index.js') } } : {}),
  };
}

// Only the installer's retained version directories are eligible. Manifests
// detect corruption, not publisher authenticity. Never execute an older binary.
export async function previousPackageRuntime(actual, settings) {
  if (!requirePackage) return null;
  const servers = actual?.mcp?.servers;
  const commands = [settings.knowledge?.bridge ? servers?.knowledge?.command : null, servers?.engineering?.command].filter(Array.isArray);
  const nodes = [...new Set(commands.map(command => command[0]))];
  // A CLI profile may invoke Knowledge directly, without a Node broker.
  const knowledgeRoot = typeof settings.knowledge?.executable === 'string' ? dirname(dirname(dirname(dirname(settings.knowledge.executable)))) : null;
  if (nodes.length > 1 || (nodes.length === 0 && !knowledgeRoot)) return null;
  const node = nodes.length ? nodes[0] : join(knowledgeRoot, 'runtime/node/node.exe');
  if (typeof node !== 'string' || samePath(node, process.execPath)) return null;
  const oldRoot = dirname(dirname(dirname(node)));
  const current = resolve(applicationRoot);
  if (!samePath(node, join(oldRoot, 'runtime', 'node', 'node.exe')) || !samePath(process.execPath, join(current, 'runtime', 'node', 'node.exe'))) return null;
  const versions = dirname(dirname(current));
  if (!samePath(dirname(dirname(oldRoot)), versions) || samePath(dirname(current), dirname(oldRoot))
      || samePath(dirname(current), versions) || samePath(dirname(oldRoot), versions)
      || !samePath(current, join(dirname(current), 'application')) || !samePath(oldRoot, join(dirname(oldRoot), 'application'))
      || !samePath(versions, join(dirname(versions), 'versions'))) return null;
  const installation = dirname(versions);
  for (const dir of [installation, versions, dirname(current), current, dirname(oldRoot), oldRoot]) {
    const stat = await lstat(dir);
    if (!stat.isDirectory() || stat.isSymbolicLink() || !samePath(await realpath(dir), dir)) throw new Error('Redirected installation version');
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
  const oldManifest = await verifyOperationManifest(oldRoot);
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
  if (settings.knowledge && !samePath(settings.knowledge.executable, oldKnowledge)) return null;
  if (settings.engineering && !samePath(settings.engineering.entrypoint, oldEngineering)) return null;
  return { node, knowledgeClient: join(oldRoot, 'src', 'knowledge-client.mjs') };
}
