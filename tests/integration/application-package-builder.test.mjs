import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { cp, lstat, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { createManifest, verifyManifest } from '../../scripts/application-manifest.mjs';
const run = promisify(execFile);
const repo = resolve('.');
const npm = join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
const csc = join(process.env.SystemRoot ?? 'C:/Windows', 'Microsoft.NET/Framework/v4.0.30319/csc.exe');
const helper = join(repo, 'native/WindowsHost/bin/Release/net8.0-windows');
const sdk = resolve(process.env.TC_TEST_DOTNET_ROOT ?? 'local-data/dotnet8');
const enabled = process.platform === 'win32' && existsSync(csc) && existsSync(npm) && existsSync(join(helper, 'WindowsHost.exe')) && existsSync(join(sdk, 'dotnet.exe'));
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
async function write(path, text) { await mkdir(dirname(path), { recursive: true }); await writeFile(path, text); }
async function git(root, ...args) { return (await run('git', ['-c', 'core.hooksPath=' + join(root, '.no-fixture-hooks'), ...args], { cwd: root, windowsHide: true })).stdout.trim(); }
async function initGit(root) {
  await git(root, 'init', '-q'); await git(root, 'add', '.');
  await git(root, '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgsign=false', 'commit', '-qm', 'synthetic');
  return git(root, 'rev-parse', 'HEAD');
}

test('builder stages a bounded synthetic source build and verifies its output', { skip: !enabled, timeout: 180000 }, async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-package-build-'));
  try {
    const root = join(parent, 'checkout'); await mkdir(root);
    await cp(join(repo, 'src'), join(root, 'src'), { recursive: true });
    await cp(join(repo, 'node_modules'), join(root, 'node_modules'), { recursive: true });
    await git(root, 'init', '-q'); await git(root, 'add', 'src');
    for (const file of ['scripts/build-application-package.mjs','scripts/application-manifest.mjs','scripts/Build-ApplicationPackage.ps1']) {
      await mkdir(dirname(join(root, file)), { recursive: true }); await cp(join(repo, file), join(root, file));
    }
    const webPackage = join(parent, 'web-package');
    await write(join(webPackage, 'package.json'), JSON.stringify({ name: '@openchamber/web', version: '2.1.1', type: 'module', dependencies: {} }));
    await write(join(webPackage, 'README.md'), 'Synthetic fixture');
    const safeEnv = { ...Object.fromEntries(['SystemRoot','WINDIR','PATH','Path','PATHEXT','COMSPEC','TEMP','TMP','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramData','OS'].filter(k => process.env[k] !== undefined).map(k => [k, process.env[k]])), DOTNET_ROOT: sdk, HOME: parent, USERPROFILE: parent, APPDATA: parent, LOCALAPPDATA: parent, npm_config_cache: join(parent, 'cache'), npm_config_userconfig: join(parent, 'npmrc') };
    const packed = await run(process.execPath, [npm, 'pack', '--ignore-scripts', '--pack-destination', parent], { cwd: webPackage, env: safeEnv });
    const tarball = join(parent, packed.stdout.trim().split(/\r?\n/).at(-1));
    const manifest = { name: 'fixture-runtime', version: '0.1.0-fixture', private: true, type: 'module', dependencies: { '@openchamber/web': 'file:' + tarball.replaceAll('\\', '/') } };
    for (const dir of [root, join(root, 'upstream')]) {
      await mkdir(dir, { recursive: true }); await writeFile(join(dir, 'package.json'), JSON.stringify(manifest));
      await run(process.execPath, [npm, 'install', '--package-lock-only', '--ignore-scripts', '--no-audit', '--no-fund'], { cwd: dir, env: safeEnv });
    }
    const webRoot = join(root, 'local-data/upstream/openchamber'), codeRoot = join(root, 'local-data/upstream/opencode');
    await write(join(webRoot, 'LICENSE'), 'Synthetic license');
    await write(join(webRoot, 'packages/web/package.json'), await readFile(join(webPackage, 'package.json')));
    await write(join(webRoot, 'packages/web/bin/cli.js'), "console.log('2.1.1');\n");
    await write(join(webRoot, 'packages/web/server/index.js'), '// fixture server\n');
    await write(join(webRoot, 'packages/web/public/logo.txt'), 'fixture');
    const webRevision = await initGit(webRoot);
    await writeFile(join(webRoot, 'packages/web/server/index.js'), '// reviewed fixture server\n');
    await write(join(webRoot, 'packages/web/server/built-in-extensions/registry.json'), '{"version":1,"extensions":[]}');
    await write(join(codeRoot, 'LICENSE'), 'Synthetic license');
    const codeRevision = await initGit(codeRoot);
    const codeSource = join(parent, 'code.cs'); await writeFile(codeSource, 'class Program { static void Main() { System.Console.WriteLine("2.0.23"); } }');
    const codeRelative = 'local-data/upstream/opencode/packages/cli/dist/fixture/bin/opencode.exe';
    await mkdir(dirname(join(root, codeRelative)), { recursive: true });
    await run(csc, ['/nologo', '/target:exe', '/out:' + join(root, codeRelative), codeSource], { env: safeEnv });
    const cliRelative = 'local-data/upstream/openchamber/packages/web/bin/cli.js';
    const htmlRelative = 'local-data/upstream/openchamber/packages/web/dist/index.html'; await write(join(root, htmlRelative), 'synthetic UI');
    const lock = { version: 1, opencode: { version: '2.0.23', revision: codeRevision }, openchamber: { version: '2.1.1', revision: webRevision } };
    await write(join(root, 'upstream/source-lock.json'), JSON.stringify(lock));
    await cp(join(repo, 'upstream/knowledge-lock.json'), join(root, 'upstream/knowledge-lock.json'));
    const inputs = ['upstream/source-lock.json','upstream/overlays/openchamber/changes.json','upstream/overlays/openchamber/twincat.ts','upstream/overlays/openchamber/install.ts','tools/package-lock.json','scripts/build-source-runtime.mjs'];
    const overlay = [{ file: 'packages/web/server/index.js', changes: [['// fixture server', '// reviewed fixture server']] }];
    for (const file of inputs.slice(1)) await write(join(root, file), file.endsWith('changes.json') ? JSON.stringify(overlay) : 'synthetic input');
    const hashes = Object.fromEntries(await Promise.all([codeRelative, cliRelative, htmlRelative].map(async path => [path, digest(await readFile(join(root, path)))])));
    const inputHashes = Object.fromEntries(await Promise.all(inputs.map(async path => [path, digest(await readFile(join(root, path)))])));
    await write(join(root, 'artifacts/source-runtime/runtime.json'), JSON.stringify({ version: 1, sourceContract: 1, platform: process.platform, arch: process.arch, opencode: { path: codeRelative, ...lock.opencode }, openchamber: { path: cliRelative, ...lock.openchamber }, hashes, inputs: inputHashes }));
    const helperTarget = join(root, 'native/WindowsHost/bin/Release/net8.0-windows'); await mkdir(helperTarget, { recursive: true });
    for (const name of ['WindowsHost.exe','WindowsHost.dll','WindowsHost.deps.json','WindowsHost.runtimeconfig.json']) await cp(join(helper, name), join(helperTarget, name));
    const host = join(root, 'native/IdeHost/X86/bin/Release/net472');
    for (const name of ['extension.vsixmanifest','TwinCatAssistant.IdeHost.dll','TwinCatAssistant.IdeHost.pkgdef','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','Microsoft.Web.WebView2.WinForms.dll']) await write(join(host, name), name === 'TwinCatAssistant.IdeHost.dll' ? 'NativeInstallationSelection fixture' : 'fixture');
    await write(join(host, 'runtimes/win-x86/fixture.dll'), 'fixture');
    const knowledge = join(root, 'artifacts/application-inputs/knowledge'); await mkdir(knowledge, { recursive: true });
    const knowledgeSource = join(parent, 'knowledge.cs'); await writeFile(knowledgeSource, 'class Program { static void Main() { System.Console.WriteLine("twincat-knowledge 0.1.1"); } }');
    await run(csc, ['/nologo', '/target:exe', '/out:' + join(knowledge, 'twincat-knowledge.exe'), knowledgeSource], { env: safeEnv });
    await write(join(knowledge, 'LICENSE'), 'synthetic license');
    await createManifest(knowledge, { version: '0.1.1', architecture: process.arch });
    const engineering = join(root, 'artifacts/application-inputs/engineering');
    await write(join(engineering, 'packages/mcp/dist/index.js'), '// fixture');
    await write(join(engineering, 'LICENSE'), 'synthetic license');
    await createManifest(engineering, { version: '3c303837e6d6f72bec1b9d38e778a7c32c61ae5c', architecture: process.arch });
    await write(join(root, 'artifacts/application-inputs/node/LICENSE'), 'synthetic license');
    await write(join(root, 'artifacts/application-inputs/webview2/LICENSE.txt'), 'synthetic license');
    await write(join(root, 'artifacts/application-inputs/webview2/NOTICE.txt'), 'synthetic notice');
    const output = join(parent, 'output');
    await run('powershell.exe', ['-NoProfile','-File',join(root, 'scripts/Build-ApplicationPackage.ps1'),'-OutputDirectory',output], { cwd: parent, env: safeEnv, timeout: 120000, maxBuffer: 1024 * 1024 });
    const result = await verifyManifest(output);
    assert.equal(result.version, '0.1.0-fixture');
    assert.ok(result.files.some(file => file.path === 'runtime/opencode/opencode.exe'));
    assert.ok(result.files.some(file => file.path === 'runtime/node/node.exe'));
    assert.ok(result.files.some(file => file.path === 'host/x86/TwinCatAssistant.IdeHost.dll'));
    assert.ok(result.files.some(file => file.path === 'runtime/mcp/knowledge/twincat-knowledge.exe'));
    assert.ok(result.files.some(file => file.path === 'upstream/node_modules/@openchamber/web/dist/index.html'));
    assert.equal(await readFile(join(output, 'upstream/node_modules/@openchamber/web/dist/index.html'), 'utf8'), 'synthetic UI');
    assert.equal(await readFile(join(output, 'upstream/node_modules/@openchamber/web/server/index.js'), 'utf8'), '// reviewed fixture server\n');
    await writeFile(join(root, htmlRelative), 'changed after receipt');
    const refused = join(parent, 'refused');
    await assert.rejects(run('powershell.exe', ['-NoProfile','-File',join(root, 'scripts/Build-ApplicationPackage.ps1'),'-OutputDirectory',refused], { cwd: parent, env: safeEnv }));
    await assert.rejects(lstat(refused), { code: 'ENOENT' });
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 15, retryDelay: 200 }); }
});
