import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { createManifest } from '../../scripts/application-manifest.mjs';
const run = promisify(execFile);
const files = ['runtime/node/node.exe','runtime/mcp/default-settings.json','runtime/mcp/knowledge/twincat-knowledge.exe','runtime/mcp/engineering/packages/mcp/dist/index.js','src/native-chat.mjs','src/native-mcp.mjs','src/native-knowledge.mjs','src/native-chat-model.mjs','src/package-runtime.json','src/package-integrity.mjs','scripts/application-manifest.mjs','native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe'];
test('host selector validates the installer record, manifest and runtime entries and follows rollback', { skip: process.platform !== 'win32', timeout: 60000 }, async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-selector-'));
  try {
    const roots = [join(parent, 'v1'), join(parent, 'v2')];
    for (const [index, root] of roots.entries()) {
      await mkdir(root);
      for (const file of files) { const target = join(root, file); await mkdir(dirname(target), { recursive: true }); await writeFile(target, 'fixture-' + file); }
      await createManifest(root, { version: '1.0.' + index, architecture: 'x64' });
    }
    const script = join(parent, 'check.ps1');
    await writeFile(script, `param($Source,$Selector)\n$ErrorActionPreference='Stop'\nAdd-Type -Path $Source -ReferencedAssemblies 'System.Web.Extensions','System.Core' -WarningAction SilentlyContinue\n$t=[AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetType('TwinCatAssistant.Native.NativeInstallationSelection', $false) } | Where-Object { $_ } | Select-Object -First 1\n$m=$t.GetMethod('SelectRoot',[Reflection.BindingFlags]'Static,NonPublic')\ntry { $v=$m.Invoke($null,@($Selector)); if ($null -ne $v) { [Console]::Out.Write($v) } } catch { [Console]::Error.Write('selection rejected'); exit 1 }\n`);
    const selection = join(parent, 'assistant-installation.json');
    const invoke = () => run('powershell.exe', ['-NoProfile','-File',script, resolve('native/IdeHost/NativeInstallationSelection.cs'), selection], { windowsHide: true });
    assert.equal((await invoke()).stdout, '');
    const select = async (root, version) => writeFile(selection, JSON.stringify({ schemaVersion: 1, targetId: 'tcxaeshell-x86-tc4024', version, applicationRoot: root }));
    await select(roots[0], '1.0.0'); assert.equal((await invoke()).stdout, roots[0]);
    await select(roots[1], '1.0.1'); assert.equal((await invoke()).stdout, roots[1]);
    await select(roots[0], '1.0.0'); assert.equal((await invoke()).stdout, roots[0]);
    await select(roots[0], 'wrong'); await assert.rejects(invoke());
    await select(roots[0], '1.0.0'); await writeFile(join(roots[0], 'runtime/node/node.exe'), 'tampered'); await assert.rejects(invoke());
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('compiled host keeps operator paths while selecting the installed runtime', { skip: process.platform !== 'win32' || !existsSync(resolve('native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll')), timeout: 60000 }, async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-host-selection-'));
  try {
    const root = join(parent, 'application'), host = join(parent, 'extension'), dotnet = join(parent, 'dotnet');
    await mkdir(root); await mkdir(host); await mkdir(dotnet);
    for (const file of files) { const target = join(root, file); await mkdir(dirname(target), { recursive: true }); await writeFile(target, 'fixture-' + file); }
    await createManifest(root, { version: '1.0.0', architecture: 'x64' });
    await writeFile(join(host, 'assistant-installation.json'), JSON.stringify({ schemaVersion: 1, targetId: 'tcxaeshell-x86-tc4024', version: '1.0.0', applicationRoot: root }));
    await writeFile(join(dotnet, 'dotnet.exe'), 'fixture');
    const mcp = join(parent, 'existing-mcp.json'); await writeFile(mcp, '{}');
    const config = join(parent, 'native-chat.json');
    const state = join(parent, 'existing-state');
    const old = { NodePath: join(parent, 'old-node.exe'), RepositoryRoot: join(parent, 'old-checkout'), StateRoot: state, DotNetRoot: dotnet, McpSettingsPath: mcp };
    await writeFile(config, JSON.stringify(old));
    const dll = join(host, 'TwinCatAssistant.IdeHost.dll'); await cp(resolve('native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'), dll);
    const script = join(parent, 'host-check.ps1');
    await writeFile(script, `param($Dll,$HostDir,$Config,$Local,$Programs)\n$ErrorActionPreference='Stop'\nAdd-Type -AssemblyName System.Web.Extensions,System.Net.Http,System.Security\n$a=[Reflection.Assembly]::LoadFrom($Dll)\n$t=$a.GetType('TwinCatAssistant.Native.NativeChatConfiguration',$true)\n$m=$t.GetMethod('LoadFrom',[Reflection.BindingFlags]'Static,NonPublic')\n$v=$m.Invoke($null,@($HostDir,$Config,$Local,$Programs))\n@{NodePath=$v.NodePath;RepositoryRoot=$v.RepositoryRoot;StateRoot=$v.StateRoot;DotNetRoot=$v.DotNetRoot;McpSettingsPath=$v.McpSettingsPath}|ConvertTo-Json -Compress\n`);
    const result = JSON.parse((await run('powershell.exe', ['-NoProfile','-File',script,dll,host,config,parent,parent], { windowsHide: true })).stdout);
    assert.deepEqual(result, { NodePath: join(root, 'runtime/node/node.exe'), RepositoryRoot: root, StateRoot: state, DotNetRoot: dotnet, McpSettingsPath: mcp });
    assert.deepEqual(JSON.parse(await readFile(config, 'utf8')), old);
    const fresh = JSON.parse((await run('powershell.exe', ['-NoProfile','-File',script,dll,host,join(parent, 'no-config.json'),parent,parent], { windowsHide: true })).stdout);
    assert.equal(fresh.StateRoot, join(parent, 'TwinCatAssistant/operator-state'));
    assert.equal(fresh.DotNetRoot, dotnet);
    assert.equal(fresh.McpSettingsPath, join(root, 'runtime/mcp/default-settings.json'));
    await writeFile(join(host, '.assistant-owner'), 'fixture');
    await rm(join(host, 'assistant-installation.json'));
    await assert.rejects(run('powershell.exe', ['-NoProfile','-File',script,dll,host,config,parent,parent], { windowsHide: true }));
  } finally { await rm(parent, { recursive: true, force: true }); }
});
