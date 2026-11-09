import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { cp, lstat, mkdir, readFile, writeFile } from 'node:fs/promises';
import { isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { verifyManifest } from './application-manifest.mjs';

// Outside-IDE acceptance only. Exact packages remain unchanged. A fresh private
// profile and synthetic installation are retained with the evidence.
const run = promisify(execFile);
const workspace = fileURLToPath(new URL('../', import.meta.url));
const [oldInput, currentInput, output, ...flags] = process.argv.slice(2);
if (process.platform !== 'win32' || !oldInput || !currentInput || !output || new Set(flags).size !== flags.length || flags.some(flag => !['--reuse-staging', '--maintenance'].includes(flag)))
  throw new Error('Usage: node scripts/Measure-PrivatePackagedNative.mjs <reviewed-C0-package> <current-package> <fresh-local-data-directory> [--reuse-staging] [--maintenance]');
const reuse = flags.includes('--reuse-staging');
const maintenance = flags.includes('--maintenance');
const root = resolve(output);
const child = relative(resolve(workspace, 'local-data'), root);
if (!child || child.startsWith('..') || isAbsolute(child)) throw new Error('Output must stay in this checkout local-data');
const hash = async path => createHash('sha256').update(await readFile(path)).digest('hex');
const inputs = [resolve(oldInput), resolve(currentInput)];
const manifests = [];
for (const input of inputs) manifests.push(await verifyManifest(input));
assert.notEqual(manifests[0].version, manifests[1].version, 'Distinct real package versions are required');
if (!reuse) await mkdir(root); // Never adopt an existing test or operator directory by default.
const installation = join(root, 'synthetic-installation');
const packages = manifests.map(manifest => join(installation, 'versions', manifest.version, 'application'));
for (let i = 0; i < inputs.length; i++) {
  if (!reuse) {
    console.log('Copying verified package', manifests[i].version);
    await cp(inputs[i], packages[i], { recursive: true, errorOnExist: true, force: false });
  } else {
    for (const directory of [root, installation, join(installation, 'versions'), join(installation, 'versions', manifests[i].version), packages[i]])
      assert.ok((await lstat(directory)).isDirectory() && !(await lstat(directory)).isSymbolicLink(), 'Redirected staging refused');
    assert.equal(await hash(join(packages[i], 'application-manifest.json')), await hash(join(inputs[i], 'application-manifest.json')), 'Staging manifest differs from reviewed input');
  }
  await verifyManifest(packages[i]);
}
const results = reuse ? join(root, 'retry-' + randomUUID()) : root;
if (reuse) await mkdir(results);
const host = join(results, 'copied-host');
await cp(join(packages[1], 'host/x86'), host, { recursive: true, errorOnExist: true, force: false });
await writeFile(join(host, 'assistant-installation.json'), JSON.stringify({ schemaVersion: 1, targetId: 'tcxaeshell-x86-tc4024', version: manifests[1].version, applicationRoot: packages[1] }));
await writeFile(join(installation, '.assistant-install-root'), 'TwinCatAssistant installation v1');
const profileId = 'combined-native-' + randomUUID().replaceAll('-', '');
const prepare = await run('powershell.exe', ['-NoProfile', '-File', join(workspace, 'scripts/New-PrivateHostAcceptanceProfile.ps1'), '-ProfileId', profileId], { windowsHide: true });
const profile = JSON.parse(prepare.stdout.replace(/^\uFEFF/, ''));
if (maintenance) await writeFile(join(host, 'assistant-test-maintenance.json'), JSON.stringify({
  schemaVersion: 1, configurationPath: profile.ConfigurationPath,
  configurationSha256: await hash(profile.ConfigurationPath), operatorSid: profile.OperatorSid,
  selectorSha256: await hash(join(host, 'assistant-installation.json')),
}));
await writeFile(join(installation, 'state.json'), JSON.stringify({ targetId: 'tcxaeshell-x86-tc4024', current: manifests[1].version, previous: manifests[0].version, policyUserSid: profile.OperatorSid, policyStateRoot: profile.StateRoot }));
const { initializeState } = await import(pathToFileURL(join(packages[0], 'src/state.mjs')));
await initializeState(profile.StateRoot, { knowledge: { executable: join(packages[0], 'runtime/mcp/knowledge/twincat-knowledge.exe') } });
const sentinel = join(profile.StateRoot, 'workspace/synthetic-history.txt');
await writeFile(sentinel, 'private combined acceptance history');
const nativeResultPath = join(results, 'native-result.json');
const report = {
  synthetic: true, outsideIde: true, installedHostAcceptance: false, maintenanceMarker: maintenance,
  sourceCheckpoint: (await run('git', ['rev-parse', 'HEAD'], { cwd: workspace })).stdout.trim(),
  harnessSha256: await hash(fileURLToPath(import.meta.url)),
  fixtureSha256: await hash(join(workspace, 'tests/fixtures/private-packaged-native.ps1')),
  profileId, inputManifests: await Promise.all(inputs.map(async (path, i) => ({ path, version: manifests[i].version, sha256: await hash(join(path, 'application-manifest.json')) }))),
};
let launchError;
try {
  await run(join(process.env.WINDIR, 'SysWOW64/WindowsPowerShell/v1.0/powershell.exe'), ['-NoProfile', '-File', join(workspace, 'tests/fixtures/private-packaged-native.ps1'), '-HostDirectory', host, '-OutputFile', nativeResultPath], {
    windowsHide: true, timeout: 115000,
    env: { ...process.env, TWINCAT_ASSISTANT_TEST_CONFIG: profile.ConfigurationPath },
  });
} catch (error) { launchError = error; }
try { report.native = JSON.parse((await readFile(nativeResultPath, 'utf8')).replace(/^\uFEFF/, '')); }
catch { if (launchError) throw launchError; throw new Error('Native result was not written'); }
report.sentinelPreserved = await readFile(sentinel, 'utf8') === 'private combined acceptance history';
const settings = JSON.parse(await readFile(join(profile.StateRoot, 'settings.json'), 'utf8'));
const actual = JSON.parse(await readFile(join(profile.StateRoot, 'config/opencode.json'), 'utf8'));
const { createOpenCodeConfig } = await import(pathToFileURL(join(packages[1], 'src/config.mjs')));
report.exactCurrentPolicy = JSON.stringify(actual) === JSON.stringify(createOpenCodeConfig(settings, { node: join(packages[1], 'runtime/node/node.exe'), knowledgeClient: join(packages[1], 'src/knowledge-client.mjs') }));
report.rebasedKnowledgePath = settings.knowledge.executable === join(packages[1], 'runtime/mcp/knowledge/twincat-knowledge.exe');
report.packageBytesUnchanged = true;
for (const path of [...inputs, ...packages]) await verifyManifest(path);
await writeFile(join(results, 'report.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
assert.ok(report.native.success && report.native.stopped && report.sentinelPreserved && report.exactCurrentPolicy && report.rebasedKnowledgePath, 'Combined private native acceptance failed');
if (maintenance) assert.ok(report.native.maintenanceGuard && report.native.missingOverrideRefused, 'Maintenance marker did not enforce the private process override');
if (launchError) throw launchError;
console.log('PASS', join(results, 'report.json'));
