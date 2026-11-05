import assert from 'node:assert/strict';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { cp, lstat, mkdir, mkdtemp, readFile, rename, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { existsSync } from 'node:fs';
import { test } from 'node:test';
import { createManifest } from '../../scripts/application-manifest.mjs';

const run = promisify(execFile);
const repo = resolve('.');
const sdk = resolve(process.env.TC_TEST_DOTNET_ROOT ?? 'local-data/dotnet8');
const helper = resolve('native/WindowsHost/bin/Release/net8.0-windows');
const canRunFixture = process.platform === 'win32' && existsSync(join(sdk, 'dotnet.exe')) && existsSync(join(helper, 'WindowsHost.exe'));
const env = Object.fromEntries(['SystemRoot','WINDIR','PATH','Path','PATHEXT','COMSPEC','TEMP','TMP','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432','ProgramData','ALLUSERSPROFILE','OS','PROCESSOR_ARCHITECTURE'].filter(k => process.env[k] !== undefined).map(k => [k, process.env[k]]));
env.DOTNET_ROOT = sdk;

async function freePort() {
  const server = createServer(); await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port; await new Promise(r => server.close(r)); return port;
}
async function fixture(parent) {
  const stage = join(parent, 'stage'); await mkdir(stage);
  await cp(join(repo, 'src'), join(stage, 'src'), { recursive: true });
  await cp(join(repo, 'node_modules'), join(stage, 'node_modules'), { recursive: true, filter: source => !source.split(/[\\/]/).includes('.bin') });
  await mkdir(join(stage, 'scripts'), { recursive: true });
  await cp(join(repo, 'scripts/application-manifest.mjs'), join(stage, 'scripts/application-manifest.mjs'));
  await cp(join(repo, 'package.json'), join(stage, 'package.json'));
  await mkdir(join(stage, 'upstream'), { recursive: true });
  await cp(join(repo, 'upstream/source-lock.json'), join(stage, 'upstream/source-lock.json'));
  const integrity = join(stage, 'src/package-integrity.mjs');
  await writeFile(integrity, (await readFile(integrity, 'utf8')).replace('const requirePackage = false;', 'const requirePackage = true;'));
  await writeFile(join(stage, 'src/package-runtime.json'), '{"version":1}\n');
  const fixtureDir = join(parent, 'fixture'); await mkdir(fixtureDir);
  await writeFile(join(fixtureDir, 'fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><AssemblyName>opencode</AssemblyName></PropertyGroup></Project>');
  await writeFile(join(fixtureDir, 'Program.cs'), 'System.Console.WriteLine("2.0.23");');
  await run(join(sdk, 'dotnet.exe'), ['build', join(fixtureDir, 'fixture.csproj'), '-o', join(fixtureDir, 'out'), '--nologo'], { env: { ...env, USERPROFILE: parent, HOMEDRIVE: parent.slice(0, 2), HOMEPATH: parent.slice(2), APPDATA: parent, LOCALAPPDATA: parent, NUGET_PACKAGES: join(parent, 'nuget'), DOTNET_CLI_HOME: parent, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false' }, cwd: parent });
  await mkdir(join(stage, 'runtime/opencode'), { recursive: true });
  await mkdir(join(stage, 'runtime/node'), { recursive: true });
  await cp(process.execPath, join(stage, 'runtime/node/node.exe'));
  const mcp = join(stage, 'runtime/mcp');
  await mkdir(join(mcp, 'knowledge'), { recursive: true });
  await mkdir(join(mcp, 'engineering/packages/mcp/dist'), { recursive: true });
  await writeFile(join(mcp, 'knowledge/twincat-knowledge.exe'), 'synthetic fixture');
  await writeFile(join(mcp, 'engineering/packages/mcp/dist/index.js'), '// synthetic fixture');
  await writeFile(join(mcp, 'default-settings.json'), '{"knowledge":{},"engineering":{}}');
  for (const name of ['opencode.exe','opencode.dll','opencode.deps.json','opencode.runtimeconfig.json']) await cp(join(fixtureDir, 'out', name), join(stage, 'runtime/opencode', name));
  const destination = join(stage, 'native/WindowsHost/bin/Release/net8.0-windows'); await mkdir(destination, { recursive: true });
  for (const name of ['WindowsHost.exe','WindowsHost.dll','WindowsHost.deps.json','WindowsHost.runtimeconfig.json']) await cp(join(helper, name), join(destination, name));
  const web = join(stage, 'upstream/node_modules/@openchamber/web/bin'); await mkdir(web, { recursive: true });
  await writeFile(join(web, 'cli.js'), `import http from 'node:http';
    if (process.argv.includes('--version')) console.log('2.1.1');
    else { const port = Number(process.argv[process.argv.indexOf('--port') + 1]);
      http.createServer((req,res) => { res.writeHead(401, {'content-type':'application/json'}); res.end(JSON.stringify({authenticated:false,locked:true})); }).listen(port, '127.0.0.1'); }
  `);
  await writeFile(join(stage, 'upstream/node_modules/@openchamber/web/package.json'), '{"type":"module"}');
  await createManifest(stage, { version: '0.1.0-fixture', architecture: process.arch });
  const moved = join(parent, 'relocated'); await rename(stage, moved); return moved;
}

test('relocated package resolves and starts the protected launcher with synthetic state and processes', { skip: !canRunFixture, timeout: 120000 }, async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-package-'));
  let child;
  try {
    const root = await fixture(parent);
    const lookup = await run(process.execPath, ['--input-type=module', '-e', `const x=await import(${JSON.stringify('file:///' )}+process.argv[1].replaceAll('\\\\','/')); console.log(JSON.stringify(await x.runtimePaths()));`, join(root, 'src/runtime-paths.mjs')], { env, cwd: parent });
    const paths = JSON.parse(lookup.stdout); assert.equal(paths.opencode, join(root, 'runtime/opencode/opencode.exe')); assert.equal(paths.sourceContract, 1);
    const node = join(root, 'runtime/node/node.exe');
    const corpus = join(parent, 'corpus.toml'); await writeFile(corpus, '# synthetic corpus configuration');
    const mcpSettings = join(parent, 'old-mcp.json');
    await writeFile(mcpSettings, JSON.stringify({ knowledge: { executable: join(parent, 'removed-dev-knowledge.exe'), config: corpus }, engineering: { entrypoint: join(parent, 'removed-dev-engineering.js') } }));
    const resolved = await run(node, ['--input-type=module', '-e', `const x=await import(${JSON.stringify('file:///')}+process.argv[2].replaceAll('\\\\','/')); console.log(JSON.stringify(await x.readNativeMcpSettings(process.argv[3])));`, 'fixture', join(root, 'src/native-mcp.mjs'), mcpSettings], { env, cwd: parent, timeout: 30000 });
    const settings = JSON.parse(resolved.stdout);
    assert.equal(settings.knowledge.executable, join(root, 'runtime/mcp/knowledge/twincat-knowledge.exe')); assert.equal(settings.knowledge.config, corpus);
    assert.equal(settings.engineering.entrypoint, join(root, 'runtime/mcp/engineering/packages/mcp/dist/index.js'));
    await assert.rejects(run(process.execPath, ['--input-type=module', '-e', `const x=await import(${JSON.stringify('file:///')}+process.argv[1].replaceAll('\\\\','/')); await x.runtimePaths('distribution');`, join(root, 'src/runtime-paths.mjs')], { env, cwd: parent }));
    const state = join(parent, 'state'); const port = await freePort();
    child = spawn(node, [join(root, 'src/native-chat.mjs')], { env, cwd: parent, stdio: ['pipe','pipe','pipe'], windowsHide: true });
    let out = '', err = ''; child.stdout.on('data', d => { out += d; }); child.stderr.on('data', d => { err += d; });
    child.stdin.write(JSON.stringify({ state, port, password: 'synthetic-password-123' }) + '\n');
    const limit = Date.now() + 30000;
    while (!out.includes('\n') && child.exitCode === null && Date.now() < limit) await new Promise(r => setTimeout(r, 100));
    assert.deepEqual(JSON.parse(out.trim()), { status: 'ready', port }, err);
    const response = await fetch(`http://127.0.0.1:${port}/auth/session`); assert.equal(response.status, 401);
    const history = join(state, 'data', 'synthetic-history'); await writeFile(history, 'preserved');
    child.stdin.end(); await once(child, 'exit'); assert.equal(await readFile(history, 'utf8'), 'preserved');
    child = spawn(node, [join(root, 'src/native-chat.mjs')], { env, cwd: parent, stdio: ['pipe','pipe','pipe'], windowsHide: true });
    out = ''; err = ''; child.stdout.on('data', d => { out += d; }); child.stderr.on('data', d => { err += d; });
    child.stdin.write(JSON.stringify({ state, port, password: 'synthetic-password-123' }) + '\n');
    const restartLimit = Date.now() + 30000;
    while (!out.includes('\n') && child.exitCode === null && Date.now() < restartLimit) await new Promise(r => setTimeout(r, 100));
    assert.deepEqual(JSON.parse(out.trim()), { status: 'ready', port }, err);
    assert.equal(await readFile(history, 'utf8'), 'preserved');
    child.stdin.end(); await once(child, 'exit');
    await writeFile(join(root, 'src/commands.mjs'), 'tampered');
    await assert.rejects(run(process.execPath, [join(root, 'src/cli.mjs'), '--help'], { env, cwd: parent }));
    assert.equal(await readFile(history, 'utf8'), 'preserved');
    await rm(join(root, 'application-manifest.json')); await rm(join(root, 'src/package-runtime.json'));
    await assert.rejects(run(process.execPath, [join(root, 'src/cli.mjs'), '--help'], { env, cwd: parent }));
  } finally {
    if (child && child.exitCode === null) { child.stdin.end(); await Promise.race([once(child, 'exit'), new Promise(r => setTimeout(r, 5000))]); if (child.exitCode === null) child.kill(); }
    await rm(parent, { recursive: true, force: true, maxRetries: 15, retryDelay: 200 });
  }
});

test('builder rejects missing source assets without creating output', { skip: process.platform !== 'win32' }, async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-build-refuse-')); const output = join(parent, 'output');
  try {
    const checkout = join(parent, 'checkout'); await mkdir(checkout);
    await cp(join(repo, 'src'), join(checkout, 'src'), { recursive: true });
    await mkdir(join(checkout, 'scripts'));
    for (const name of ['Build-ApplicationPackage.ps1', 'build-application-package.mjs', 'application-manifest.mjs']) await cp(join(repo, 'scripts', name), join(checkout, 'scripts', name));
    await symlink(join(repo, 'node_modules'), join(checkout, 'node_modules'), 'junction');
    const builder = join(checkout, 'scripts/Build-ApplicationPackage.ps1');
    await assert.rejects(run('powershell.exe', ['-NoProfile','-File',builder,'-OutputDirectory',output], { cwd: parent, env }), /Source runtime has not been built/);
    await assert.rejects(lstat(output), { code: 'ENOENT' });
    await mkdir(output); await writeFile(join(output, 'keep'), 'untouched');
    await assert.rejects(run('powershell.exe', ['-NoProfile','-File',builder,'-OutputDirectory',output], { cwd: parent, env }));
    assert.equal(await readFile(join(output, 'keep'), 'utf8'), 'untouched');
  } finally { await rm(parent, { recursive: true, force: true }); }
});
