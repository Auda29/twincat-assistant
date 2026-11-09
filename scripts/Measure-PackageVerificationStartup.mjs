import { cp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { resolve, join, relative, isAbsolute } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { createManifest } from './application-manifest.mjs';

// Manual acceptance test. All mutations stay in this worktree's local-data.
// Source inputs, installed configuration and the IDE are never modified.
const run = promisify(execFile);
const workspace = fileURLToPath(new URL('../', import.meta.url));
const [oldInput, newInput, output, reuse] = process.argv.slice(2);
if (!oldInput || !newInput || !output || process.platform !== 'win32')
  throw new Error('Usage: node scripts/Measure-PackageVerificationStartup.mjs <C0-application> <input-application> <output-in-local-data> [--reuse-staging]');
const base = resolve(output);
const child = relative(resolve(workspace, 'local-data'), base);
if (!child || child.startsWith('..') || isAbsolute(child)) throw new Error('Output must be a child of this worktree local-data');
if (reuse && reuse !== '--reuse-staging') throw new Error('Unknown option');
const install = join(base, 'install');
const old = join(install, 'versions/0.1.0-dev.1/application');
const current = join(install, 'versions/0.0.0-scope-synthetic/application');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const digest = async file => sha(await readFile(file));
const inputHash = await digest(join(resolve(newInput), 'application-manifest.json'));
const oldHash = await digest(join(resolve(oldInput), 'application-manifest.json'));
if (reuse) {
  // Only reuse our explicitly synthetic staging, never an issued package.
  if (JSON.parse(await readFile(join(current, 'application-manifest.json'), 'utf8')).version !== '0.0.0-scope-synthetic'
      || await digest(join(old, 'application-manifest.json')) !== oldHash) throw new Error('Unexpected staging identity');
} else {
  await mkdir(base); // Refuse to adopt an existing directory by default.
  for (const [from, to] of [[resolve(oldInput), old], [resolve(newInput), current]]) {
    console.log('copying', to);
    await cp(from, to, { recursive: true, errorOnExist: true, force: false });
  }
}
const results = join(base, 'run-' + Date.now());
await mkdir(results);
await cp(join(workspace, 'tests/fixtures/package-verification-native.ps1'), join(results, 'native.ps1'));
await mkdir(join(install, 'host'), { recursive: true });
await writeFile(join(install, '.assistant-install-root'), 'TwinCatAssistant installation v1');
await writeFile(join(install, 'state.json'), JSON.stringify({ targetId: 'tcxaeshell-x86-tc4024', current: '0.0.0-scope-synthetic' }));
await writeFile(join(install, 'host/assistant-installation.json'), JSON.stringify({ schemaVersion: 1, targetId: 'tcxaeshell-x86-tc4024', version: '0.0.0-scope-synthetic', applicationRoot: current }));
await cp(join(workspace, 'src'), join(current, 'src'), { recursive: true });
const changed = ['package-integrity', 'managed-relocation', 'native-chat', 'native-mcp', 'launcher', 'package-verification-scope'];
const optimized = new Map(await Promise.all(changed.map(async name => [name, await readFile(join(workspace, 'src', name + '.mjs'), 'utf8')])));
const trace = join(results, 'inventory.jsonl');
const manifestSource = await readFile(join(workspace, 'scripts/application-manifest.mjs'), 'utf8');
// Identical measurement instrumentation in both variants; full algorithm intact.
const instrumented = "import { appendFileSync } from 'node:fs';\n" + manifestSource
  .replace('export async function verifyManifest(root) {', `export async function verifyManifest(root) { appendFileSync(${JSON.stringify(trace)}, JSON.stringify({event:'start',root,at:Date.now()})+'\\n');`)
  .replace("  if (JSON.stringify(manifest.files) !== JSON.stringify(actual)) throw new Error('Package inventory mismatch');", `  if (JSON.stringify(manifest.files) !== JSON.stringify(actual)) throw new Error('Package inventory mismatch'); appendFileSync(${JSON.stringify(trace)}, JSON.stringify({event:'complete',root,at:Date.now()})+'\\n');`);
await writeFile(join(current, 'scripts/application-manifest.mjs'), instrumented);
const report = {
  synthetic: true, sourceCheckpoint: (await run('git', ['rev-parse', 'HEAD'], { cwd: workspace })).stdout.trim(),
  baselineScopedFilesRef: 'dda5221', oldInputManifestSha256: oldHash, inputManifestSha256: inputHash,
  nativeAssemblySha256: await digest(join(current, 'host/x86/TwinCatAssistant.IdeHost.dll')),
  manifestAlgorithmSha256: sha(manifestSource), results, variants: {},
};
const { initializeState } = await import(pathToFileURL(join(old, 'src/state.mjs')));
const powershell = 'C:/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe';
async function launch(name, state) {
  await writeFile(trace, '');
  await writeFile(join(install, 'config.json'), JSON.stringify({ StateRoot: state, DotNetRoot: String.raw`C:\Program Files\dotnet` }));
  const out = join(results, name + '.json');
  try {
    await run(powershell, ['-NoProfile', '-File', join(results, 'native.ps1'), install, name, out], { timeout: 110000, windowsHide: true });
  } catch (error) {
    // Preserve the real Native80 failure, but do not hide a broken test driver.
    try { await readFile(out); } catch { throw error; }
  }
  const result = JSON.parse((await readFile(out, 'utf8')).replace(/^\uFEFF/, ''));
  const events = (await readFile(trace, 'utf8')).trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
  await cp(trace, join(results, name + '-inventory.jsonl'));
  result.inventories = Object.fromEntries(['start', 'complete'].map(event => [event, {
    current: events.filter(x => x.event === event && resolve(x.root) === resolve(current)).length,
    previous: events.filter(x => x.event === event && resolve(x.root) === resolve(old)).length,
  }]));
  return result;
}
for (const variant of ['baseline', 'optimized']) {
  for (const name of changed) {
    let source = optimized.get(name);
    if (variant === 'baseline' && name !== 'package-verification-scope')
      source = (await run('git', ['show', `dda5221:src/${name}.mjs`], { cwd: workspace, maxBuffer: 1024 * 1024 })).stdout;
    if (name === 'package-integrity') source = source.replace('const requirePackage = false;', 'const requirePackage = true;');
    await writeFile(join(current, 'src', name + '.mjs'), source);
  }
  await rm(join(current, 'application-manifest.json'));
  const manifest = await createManifest(current, { version: '0.0.0-scope-synthetic', architecture: 'x64' });
  await cp(join(current, 'application-manifest.json'), join(results, variant + '-application-manifest.json'));
  const sourceFiles = manifest.files.filter(x => x.path.startsWith('src/') || x.path.startsWith('scripts/'));
  const state = join(results, variant + '-state');
  await initializeState(state, { knowledge: { executable: join(old, 'runtime/mcp/knowledge/twincat-knowledge.exe') } });
  await writeFile(join(state, 'workspace/sentinel.txt'), 'preserve this synthetic history');
  const beforeConfigSha256 = await digest(join(state, 'config/opencode.json'));
  const beforeSettingsSha256 = await digest(join(state, 'settings.json'));
  console.log(variant, 'actual x86 NativeChatSession.StartAsync with 80000ms');
  const result = await launch(variant, state);
  Object.assign(result, {
    sourceFiles, sourceInventorySha256: sha(JSON.stringify(sourceFiles)),
    applicationManifestSha256: await digest(join(current, 'application-manifest.json')),
    beforeConfigSha256, beforeSettingsSha256,
    afterConfigSha256: await digest(join(state, 'config/opencode.json')),
    afterSettingsSha256: await digest(join(state, 'settings.json')),
    sentinelPreserved: await readFile(join(state, 'workspace/sentinel.txt'), 'utf8') === 'preserve this synthetic history',
  });
  if (variant === 'optimized') {
    const { createOpenCodeConfig } = await import(pathToFileURL(join(current, 'src/config.mjs')));
    const settings = JSON.parse(await readFile(join(state, 'settings.json'), 'utf8'));
    const actual = JSON.parse(await readFile(join(state, 'config/opencode.json'), 'utf8'));
    result.exactC1Configuration = JSON.stringify(actual) === JSON.stringify(createOpenCodeConfig(settings, {
      node: join(current, 'runtime/node/node.exe'), knowledgeClient: join(current, 'src/knowledge-client.mjs'),
    }));
    result.packagePathRebased = settings.knowledge.executable === join(current, 'runtime/mcp/knowledge/twincat-knowledge.exe');
  }
  report.variants[variant] = result;
  await writeFile(join(results, 'report.json'), JSON.stringify(report, null, 2));
  console.log(variant, JSON.stringify({ ...result, sourceFiles: undefined }));
}
if (!report.variants.optimized.success || report.variants.optimized.startMs >= 80000
    || !report.variants.optimized.exactC1Configuration || !report.variants.optimized.packagePathRebased
    || report.variants.optimized.inventories.complete.current !== 1 || report.variants.optimized.inventories.complete.previous !== 1)
  throw new Error('Optimized native acceptance failed; see ' + join(results, 'report.json'));
console.log('PASS', join(results, 'report.json'));
