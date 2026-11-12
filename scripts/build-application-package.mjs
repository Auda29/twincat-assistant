import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cp, lstat, mkdir, mkdtemp, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runtimePaths } from '../src/runtime-paths.mjs';
import { createManifest, verifyManifest } from './application-manifest.mjs';
import { runVersion } from '../src/launcher.mjs';
import { applySourceOverlay } from './source-overlay.mjs';
import { trimChatPackage } from './trim-chat-package.mjs';
import { isReviewedKnowledgeOverlay } from './knowledge-validation-overlay.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const git = (cwd, ...args) => execFileSync('git', args, { cwd, encoding: 'utf8', windowsHide: true }).trim();
async function regular(path) {
  const s = await lstat(path);
  if (!s.isFile() || s.isSymbolicLink()) throw new Error('Required regular file missing: ' + path);
}
async function copyFile(source, target) {
  await regular(source); await mkdir(dirname(target), { recursive: true }); await cp(source, target, { errorOnExist: true, force: false });
}
async function copyTree(source, target) {
  const s = await lstat(source);
  if (!s.isDirectory() || s.isSymbolicLink()) throw new Error('Required directory missing or redirected: ' + source);
  await mkdir(target, { recursive: true });
  for (const name of await readdir(source)) {
    const from = join(source, name), to = join(target, name), entry = await lstat(from);
    if (entry.isSymbolicLink()) throw new Error('Redirected build asset: ' + from);
    if (entry.isDirectory()) await copyTree(from, to);
    else if (entry.isFile()) await copyFile(from, to);
    else throw new Error('Unsupported build asset: ' + from);
  }
}
async function removeBinDirectories(path) {
  for (const entry of await readdir(path, { withFileTypes: true })) {
    const child = join(path, entry.name);
    if (entry.name === '.bin') { await rm(child, { recursive: true, force: true }); continue; }
    if (entry.isDirectory() && !entry.isSymbolicLink()) await removeBinDirectories(child);
  }
}
function run(command, args, cwd, env) { execFileSync(command, args, { cwd, env, stdio: 'inherit', windowsHide: true }); }

// Package-only exclusions. Keep the development distribution and lockfiles
// intact for compatibility checks. They describe installation inputs; the final
// application manifest describes the delivered files.
export async function reduceDeliveryFiles(stage) {
  const modules = join(stage, 'upstream/node_modules');
  const web = join(modules, '@openchamber/web');
  const pkg = JSON.parse(await readFile(join(web, 'package.json'), 'utf8'));
  if (pkg.dependencies?.['@opencode/cli']) throw new Error('Chat now depends on the distribution Core');
  // The launcher supplies runtime/opencode/opencode.exe explicitly. No runtime
  // dependency may silently start referring to the redundant distribution CLI.
  const lock = JSON.parse(await readFile(join(stage, 'upstream/package-lock.json'), 'utf8'));
  for (const [path, entry] of Object.entries(lock.packages ?? {})) {
    if (path && entry.dependencies?.['@opencode/cli']) throw new Error('Distribution Core is required by ' + path);
  }
  const scope = join(modules, '@opencode');
  for (const entry of await readdir(scope, { withFileTypes: true }).catch(error => { if (error.code === 'ENOENT') return []; throw error; })) {
    if (entry.name === 'cli' || entry.name.startsWith('cli-')) {
      if (!entry.isDirectory() || entry.isSymbolicLink()) throw new Error('Unexpected distribution CLI entry');
      await rm(join(scope, entry.name), { recursive: true });
    }
  }
  // Upstream tracks test fixtures next to server modules. These are neither
  // runtime imports nor notices; keep every license and production module.
  async function testsOnly(path) {
    for (const entry of await readdir(path, { withFileTypes: true })) {
      const child = join(path, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Redirected Chat server entry');
      if (entry.isDirectory()) await testsOnly(child);
      else if (/\.(test|spec)\.[cm]?js$/.test(entry.name)) await rm(child);
    }
  }
  await testsOnly(join(web, 'server'));
  const reduction = await trimChatPackage(stage);
  if (reduction.removedServerFiles || reduction.removedPackages.length) console.log('Chat package reduction: ' + JSON.stringify(reduction));
}

// The input manifest authenticates these bytes within the assembled input;
// this separate receipt binds the module to the reviewed source revision.
export async function verifyEngineeringModule(input, lock) {
  if (lock?.schemaVersion !== 1 || !/^[a-f0-9]{40}$/.test(lock.revision ?? '') || lock.moduleVersion !== '1.0.0.0') throw new Error('Invalid reviewed Engineering lock');
  const module = join(input, 'native/host/TwinCatMcp.Engineering.Host.dll');
  const receiptPath = join(input, 'native/host/receipt.json');
  await regular(module); await regular(receiptPath);
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  if (Object.keys(receipt).sort().join(',') !== 'moduleVersion,revision,schemaVersion,sha256' || receipt.schemaVersion !== 1
      || receipt.revision !== lock.revision || receipt.moduleVersion !== lock.moduleVersion
      || !/^[a-f0-9]{64}$/.test(receipt.sha256 ?? '')
      || createHash('sha256').update(await readFile(module)).digest('hex') !== receipt.sha256) throw new Error('Engineering provider receipt mismatch');
  const identity = execFileSync('powershell.exe', ['-NoProfile','-NonInteractive','-Command',
    '$a=[Reflection.AssemblyName]::GetAssemblyName($env:TC_ENGINEERING_MODULE); $a.Name + "|" + $a.Version.ToString()'],
    { env: {...process.env, TC_ENGINEERING_MODULE:module}, encoding:'utf8', windowsHide:true, timeout:10000 }).trim();
  if (identity !== 'TwinCatMcp.Engineering.Host|' + lock.moduleVersion) throw new Error('Engineering provider module identity mismatch');
  return module;
}

export async function buildApplicationPackage(output, inputs) {
  if (!isAbsolute(output) || resolve(output) !== output) throw new Error('OutputDirectory must be absolute and normalized');
  if (process.platform !== 'win32' || !['x64', 'arm64', 'ia32'].includes(process.arch) || Number(process.versions.node.split('.')[0]) < 22) throw new Error('Build with Windows Node 22+ for the backend architecture');
  if (await lstat(output).then(() => true, e => { if (e.code === 'ENOENT') return false; throw e; })) throw new Error('OutputDirectory must not already exist');
  const runtime = await runtimePaths('source'); // checks the pinned source receipt and its inputs
  const ownSources = git(root, 'ls-files', '--', 'src').split(/\r?\n/).filter(Boolean);
  if (!ownSources.includes('src/native-chat.mjs') || !ownSources.includes('src/package-integrity.mjs')
      || ownSources.some(file => !/^src\/[a-z0-9-]+\.mjs$/.test(file))) throw new Error('Unexpected Core source inventory');
  const receipt = JSON.parse(await readFile(join(root, 'artifacts/source-runtime/runtime.json'), 'utf8'));
  const lock = JSON.parse(await readFile(join(root, 'upstream/source-lock.json'), 'utf8'));
  const knowledgeLock = JSON.parse(await readFile(join(root, 'upstream/knowledge-lock.json'), 'utf8'));
  const engineeringLock = JSON.parse(await readFile(join(root, 'upstream/engineering-lock.json'), 'utf8'));
  const inputRoot = inputs ?? join(root, 'artifacts/application-inputs');
  if (!isAbsolute(inputRoot) || resolve(inputRoot) !== inputRoot) throw new Error('Input directory must be absolute and normalized');
  const knowledgeInput = join(inputRoot, 'knowledge');
  const engineeringInput = join(inputRoot, 'engineering');
  const hostInput = join(root, 'native/IdeHost/X86/bin/Release/net472');
  const nodeLicense = join(inputRoot, 'node/LICENSE');
  const knowledgeManifest = await verifyManifest(knowledgeInput);
  const engineeringManifest = await verifyManifest(engineeringInput);
  const knowledgeOverlay = JSON.parse(await readFile(join(knowledgeInput, 'assistant-validation-overlay.json')));
  if (!isReviewedKnowledgeOverlay(knowledgeOverlay,
      createHash('sha256').update(await readFile(join(knowledgeInput, 'site-packages/twincat_knowledge/domain/provenance.py'))).digest('hex')))
    throw new Error('Rebuild Knowledge inputs with the reviewed startup validation overlay');
  if (knowledgeManifest.version !== knowledgeLock.version || engineeringManifest.version !== engineeringLock.revision
      || knowledgeManifest.architecture !== process.arch || engineeringManifest.architecture !== process.arch) throw new Error('MCP input version or backend architecture mismatch');
  const engineeringModule = await verifyEngineeringModule(engineeringInput, engineeringLock);
  for (const item of [join(knowledgeInput, 'twincat-knowledge.exe'), join(knowledgeInput, 'LICENSE'), join(engineeringInput, 'packages/mcp/dist/index.js'), join(engineeringInput, 'LICENSE'), nodeLicense,
    join(inputRoot, 'webview2/LICENSE.txt'), join(inputRoot, 'webview2/NOTICE.txt'),
    join(hostInput, 'extension.vsixmanifest'), join(hostInput, 'TwinCatAssistant.IdeHost.dll'), join(hostInput, 'TwinCatAssistant.IdeHost.pkgdef')]) await regular(item);
  if (!(await readFile(join(hostInput, 'TwinCatAssistant.IdeHost.dll'))).includes(Buffer.from('NativeInstallationSelection'))) throw new Error('Build the host with the installation selector');
  if ((await lstat(nodeLicense)).size === 0) throw new Error('Node license is empty');
  const sourceWebRoot = join(root, 'local-data/upstream/openchamber');
  const web = join(sourceWebRoot, 'packages/web');
  for (const [path, spec] of [[sourceWebRoot, lock.openchamber], [join(root, 'local-data/upstream/opencode'), lock.opencode]])
    if (git(path, 'rev-parse', 'HEAD') !== spec.revision) throw new Error('Unexpected upstream revision');
  // Accept only exact reviewed overlay edits to tracked server files. Untracked
  // files are not copied; the generated UI dist is bound by the source receipt.
  const overlay = JSON.parse(await readFile(join(root, 'upstream/overlays/openchamber/changes.json'), 'utf8'));
  const edits = new Map(overlay.map(edit => [edit.file, edit]));
  const modified = git(sourceWebRoot, 'diff', '--name-only', 'HEAD', '--', 'packages/web/bin', 'packages/web/server', 'packages/web/public', 'packages/web/package.json').split(/\r?\n/).filter(Boolean);
  for (const file of modified) {
    if (!edits.has(file)) throw new Error('Unreviewed Chat server change: ' + file);
    const original = execFileSync('git', ['show', lock.openchamber.revision + ':' + file], { cwd: sourceWebRoot, encoding: 'utf8', windowsHide: true });
    const expected = await applySourceOverlay(original, edits.get(file));
    const actual = await readFile(join(sourceWebRoot, file), 'utf8');
    if (actual.replace(/\r\n/g, '\n') !== expected.replace(/\r\n/g, '\n')) throw new Error('Unreviewed Chat overlay: ' + file);
  }
  const tracked = git(sourceWebRoot, 'ls-files', '--', 'packages/web/bin', 'packages/web/server', 'packages/web/public').split(/\r?\n/).filter(Boolean);
  if (!tracked.includes('packages/web/bin/cli.js') || !tracked.some(p => p.startsWith('packages/web/server/'))) throw new Error('Incomplete tracked Chat server');
  for (const item of tracked) if (!/^packages\/web\/(bin|server|public)\//.test(item) || item.includes('..')) throw new Error('Invalid tracked Chat path');
  for (const name of ['WindowsHost.exe', 'WindowsHost.dll', 'WindowsHost.deps.json', 'WindowsHost.runtimeconfig.json']) await regular(join(root, 'native/WindowsHost/bin/Release/net8.0-windows', name));
  await regular(join(web, 'dist/index.html'));
  const npm = join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
  await regular(npm);
  const parent = dirname(output);
  const stage = await mkdtemp(join(parent, '.twincat-package-'));
  try {
    const scratch = join(stage, '.build-scratch');
    await mkdir(scratch);
    const buildEnv = Object.fromEntries(['SystemRoot', 'WINDIR', 'PATH', 'Path', 'COMSPEC', 'PATHEXT', 'SystemDrive', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramData', 'OS', 'DOTNET_ROOT', 'DOTNET_ROOT_X86', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64'].filter(k => process.env[k] !== undefined).map(k => [k, process.env[k]]));
    Object.assign(buildEnv, { HOME: scratch, USERPROFILE: scratch, APPDATA: scratch, LOCALAPPDATA: scratch, TEMP: scratch, TMP: scratch,
      npm_config_cache: join(scratch, 'npm-cache'), npm_config_userconfig: join(scratch, 'npmrc') });
    for (const file of ['package.json', 'package-lock.json', 'upstream/package.json', 'upstream/package-lock.json', 'upstream/source-lock.json', 'upstream/knowledge-lock.json', 'upstream/engineering-lock.json', 'scripts/application-manifest.mjs'])
      await copyFile(join(root, file), join(stage, file));
    for (const file of ownSources) await copyFile(join(root, file), join(stage, file));
    const integrity = join(stage, 'src/package-integrity.mjs');
    const original = await readFile(integrity, 'utf8');
    if (!original.includes('const requirePackage = false;')) throw new Error('Unexpected package integrity source');
    await writeFile(integrity, original.replace('const requirePackage = false;', 'const requirePackage = true;'));
    await writeFile(join(stage, 'src/package-runtime.json'), '{"version":1}\n', { flag: 'wx' });
    for (const prefix of ['', 'upstream']) run(process.execPath, [npm, 'ci', '--omit=dev', '--no-audit', '--no-fund'], join(stage, prefix), buildEnv);
    const packagedWeb = join(stage, 'upstream/node_modules/@openchamber/web');
    const sourcePkg = JSON.parse(await readFile(join(web, 'package.json'), 'utf8'));
    const installedPkg = JSON.parse(await readFile(join(packagedWeb, 'package.json'), 'utf8'));
    const sourceDependencies = { ...sourcePkg.dependencies };
    if (sourceDependencies['@openchamber/sdk'] === 'workspace:*') {
      const sourceSdk = JSON.parse(await readFile(join(sourceWebRoot, 'packages/sdk/package.json'), 'utf8'));
      const installedSdk = JSON.parse(await readFile(join(stage, 'upstream/node_modules/@openchamber/sdk/package.json'), 'utf8'));
      if (sourceSdk.version !== lock.openchamber.version || installedSdk.version !== sourceSdk.version) throw new Error('Chat SDK version mismatch');
      sourceDependencies['@openchamber/sdk'] = sourceSdk.version;
    }
    if (sourcePkg.version !== lock.openchamber.version || JSON.stringify(sourceDependencies) !== JSON.stringify(installedPkg.dependencies)) throw new Error('Source Chat dependencies differ from locked distribution');
    for (const name of ['bin', 'server', 'public', 'dist']) await rm(join(packagedWeb, name), { recursive: true, force: true });
    for (const item of tracked) await copyFile(join(sourceWebRoot, item), join(packagedWeb, item.slice('packages/web/'.length)));
    // Guest hosting and its generated extension registry are not delivered.
    await copyTree(join(web, 'dist'), join(packagedWeb, 'dist'));
    await reduceDeliveryFiles(stage);
    await copyFile(runtime.opencode, join(stage, 'runtime/opencode/opencode.exe'));
    await copyFile(process.execPath, join(stage, 'runtime/node/node.exe'));
    await copyFile(nodeLicense, join(stage, 'licenses/node/LICENSE'));
    await copyFile(join(inputRoot, 'webview2/LICENSE.txt'), join(stage, 'licenses/webview2/LICENSE.txt'));
    await copyFile(join(inputRoot, 'webview2/NOTICE.txt'), join(stage, 'licenses/webview2/NOTICE.txt'));
    await copyTree(knowledgeInput, join(stage, 'runtime/mcp/knowledge'));
    await copyTree(engineeringInput, join(stage, 'runtime/mcp/engineering'));
    await writeFile(join(stage, 'runtime/mcp/default-settings.json'), '{"knowledge":{},"engineering":{}}\n');
    for (const file of ['extension.vsixmanifest', 'TwinCatAssistant.IdeHost.dll', 'TwinCatAssistant.IdeHost.pkgdef', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'Microsoft.Web.WebView2.WinForms.dll'])
      await copyFile(join(hostInput, file), join(stage, 'host/x86', file));
    await copyFile(engineeringModule, join(stage, 'host/x86/TwinCatMcp.Engineering.Host.dll'));
    await copyTree(join(hostInput, 'runtimes/win-x86'), join(stage, 'host/x86/runtimes/win-x86'));
    await copyFile(join(hostInput, 'en/TwinCatAssistant.IdeHost.resources.dll'), join(stage, 'host/x86/en/TwinCatAssistant.IdeHost.resources.dll'));
    for (const name of ['WindowsHost.exe', 'WindowsHost.dll', 'WindowsHost.deps.json', 'WindowsHost.runtimeconfig.json']) await copyFile(join(root, 'native/WindowsHost/bin/Release/net8.0-windows', name), join(stage, 'native/WindowsHost/bin/Release/net8.0-windows', name));
    for (const name of ['opencode', 'openchamber']) {
      const checkout = join(root, 'local-data/upstream', name);
      const license = git(checkout, 'ls-files', '--', 'LICENSE', 'LICENSE.md', 'LICENSE.txt').split(/\r?\n/).filter(Boolean);
      if (!license.length) throw new Error('Upstream license missing: ' + name);
      git(checkout, 'diff', '--exit-code', 'HEAD', '--', ...license);
      for (const file of license) await copyFile(join(checkout, file), join(stage, 'licenses', name, file));
    }
    await writeFile(join(stage, 'runtime/source-receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
    await removeBinDirectories(join(stage, 'node_modules')); await removeBinDirectories(join(stage, 'upstream/node_modules'));
    await runVersion(join(stage, 'runtime/opencode/opencode.exe'), ['--version'], buildEnv, stage, lock.opencode.version);
    await runVersion(process.execPath, [join(packagedWeb, 'bin/cli.js'), '--version'], buildEnv, stage, lock.openchamber.version);
    const knowledgeVersion = execFileSync(join(stage, 'runtime/mcp/knowledge/twincat-knowledge.exe'), ['--version'], { cwd: stage, env: buildEnv, encoding: 'utf8', timeout: 10000, windowsHide: true }).trim();
    if (knowledgeVersion !== 'twincat-knowledge ' + knowledgeLock.version) throw new Error('Knowledge MCP version verification failed');
    const helperVersion = execFileSync(join(stage, 'native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe'), ['--version'], { cwd: stage, env: buildEnv, encoding: 'utf8', timeout: 10000, windowsHide: true }).trim();
    if (helperVersion !== '1.0.0') throw new Error('Windows helper version verification failed');
    await rm(scratch, { recursive: true, force: true });
    const pkg = JSON.parse(await readFile(join(root, 'package.json'), 'utf8'));
    await createManifest(stage, { version: pkg.version, architecture: process.arch });
    const manifest = await verifyManifest(stage);
    if (manifest.files.some(file => file.path.toLowerCase().endsWith('.vsix') || file.path.split('/').at(-1).toLowerCase() === 'tcatsysmanagerlib.dll'))
      throw new Error('Package must not contain VSIX archives or TwinCAT interop');
    await rename(stage, output); // both directories share a parent/volume
    console.log('Application package staged at ' + output);
  } finally { await rm(stage, { recursive: true, force: true }); }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { if (![3,4].includes(process.argv.length)) throw new Error('Expected output directory and optional input directory'); await buildApplicationPackage(process.argv[2], process.argv[3]); }
  catch (error) { console.error('Application package build failed: ' + error.message); process.exitCode = 1; }
}
