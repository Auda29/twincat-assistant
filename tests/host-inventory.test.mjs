import assert from 'node:assert/strict';
import { spawnSync, execFileSync } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

// Host-target contracts and synthetic host classification. These checks are a
// static preflight, never live host acceptance (docs/HOST_ACCEPTANCE_MATRIX.md).
const root = fileURLToPath(new URL('..', import.meta.url));
const script = join(root, 'scripts', 'Get-AssistantHostInventory.ps1');
const targets = JSON.parse(await readFile(join(root, 'native', 'host-targets.json'), 'utf8'));

function findPowerShell() {
  const preferred = process.env.TC_TEST_POWERSHELL ? [process.env.TC_TEST_POWERSHELL] : [];
  for (const candidate of [...preferred, ...(process.platform === 'win32' ? ['powershell.exe', 'pwsh'] : ['pwsh'])]) {
    const probe = spawnSync(candidate, ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.Major'], { encoding: 'utf8' });
    if (probe.status === 0) return candidate;
  }
  return null;
}
const powershell = findPowerShell();
const skipShell = powershell ? false : 'PowerShell is not available';

function runScript(args) {
  const result = spawnSync(powershell, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script, ...args],
    { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  return { status: result.status, stderr: result.stderr, document: result.stdout.trim() ? JSON.parse(result.stdout) : null };
}

let scratch;
test.before(async () => { scratch = await mkdtemp(join(tmpdir(), 'tc-host-inventory-')); });
test.after(async () => { if (scratch) await rm(scratch, { recursive: true, force: true }); });

let counter = 0;
async function evaluate(facts, extra = []) {
  const path = join(scratch, `facts-${counter++}.json`);
  await writeFile(path, JSON.stringify(facts));
  return runScript(['-InventoryPath', path, ...extra]);
}

// Sanitized shape of the verified 2026-11-05 machine.
function shell(overrides = {}) {
  return {
    id: 'tcxaeshell:TcXaeShell\\15.0', kind: 'TcXaeShell', displayName: 'TwinCAT XAE Shell', productVersion: '1.22.0.0',
    executable: { name: 'TcXaeShell.exe', fileVersion: '15.0.0.0', architecture: 'x86' },
    shellBase: { msenvVersion: '15.0.35826.140 built by: D15.9', major: 15, minor: 0 },
    dte: { progId: 'TcXaeShell.DTE.15.0', registered: true, matchesExecutable: true },
    twinCatIntegration: { present: true, xaeBase: true, xaePlc: true, supportedVisualStudioVersion: '15.0' },
    assistantPayloads: [{ identity: 'TwinCatAssistant.Native.X86', version: '0.1.9', productArchitecture: 'x86',
      installationTargetId: 'Microsoft.VisualStudio.IntegratedShell', installationTargetVersion: '[15.0,17.0)' }],
    running: [],
    ...overrides,
  };
}
function facts({ version = '3.1.4024.78', mechanism = 'classic-setup', hosts = [shell()], netfx = 533509, webView2 = [{ version: '156.0.4314.58' }] } = {}) {
  const family = /^3\.1\.(\d{4})/.exec(version ?? '')?.[1] ?? null;
  return {
    windows: { productName: 'Windows 11 Enterprise', displayVersion: '25H2', build: '26200', architecture: 'amd64' },
    twinCat: { version, family, installationMechanism: mechanism },
    prerequisites: { netFrameworkRelease: netfx, webView2 },
    hosts,
  };
}
const only = document => { assert.equal(document.evaluation.hosts.length, 1); return document.evaluation.hosts[0]; };

test('target description keeps exactly one installer-eligible alpha target', () => {
  assert.equal(targets.schemaVersion, 1);
  assert.deepEqual(targets.policy.installerEligibleClassifications, ['alpha-target']);
  const alpha = targets.targets.filter(target => target.status === 'alpha-target');
  assert.deepEqual(alpha.map(target => target.id), ['tcxaeshell-x86-tc4024']);
  assert.deepEqual(alpha[0].twinCat.verifiedBuilds, ['3.1.4024.78']);
  assert.equal(alpha[0].match.processArchitecture, 'x86');
  for (const claim of ['TcXaeShell64', 'TwinCAT 3.1.4026', 'full Visual Studio']) assert.ok(targets.policy.noGlobalSupportClaims.includes(claim));
  const ids = new Set(targets.targets.map(target => target.id));
  assert.equal(ids.size, targets.targets.length);
  for (const target of targets.targets) assert.ok(['alpha-target', 'unaccepted', 'blocked', 'out-of-scope'].includes(target.status), target.id);
});

test('alpha acceptance records every gate with a defined status and evidence', () => {
  const statuses = Object.keys(targets.gateStatuses);
  const alpha = targets.targets.find(target => target.status === 'alpha-target');
  assert.deepEqual(Object.keys(alpha.acceptance).sort(), targets.gates.map(gate => gate.id).sort());
  for (const [gate, record] of Object.entries(alpha.acceptance)) {
    assert.ok(statuses.includes(record.status), gate);
    assert.match(record.evidence, /^docs\/[A-Z_]+\.md$/, gate);
    if (!['passed', 'passed-user-reported'].includes(record.status)) assert.ok(record.status === 'open' || record.open, `${gate} must say what is open`);
  }
  // The known restored-editor hang has no established cause and keeps this gate open.
  assert.equal(alpha.acceptance['cold-start-restored-editors'].status, 'open');
  assert.notEqual(alpha.acceptance['update-rollback'].status, 'passed');
});

test('payload installation targets match the source VSIX manifests', async () => {
  const manifest = async architecture => {
    const text = await readFile(join(root, 'native', 'IdeHost', architecture, 'source.extension.vsixmanifest'), 'utf8');
    const [, id, versionRange] = text.match(/<InstallationTarget\s+Id="([^"]+)"\s+Version="([^"]+)"/);
    return { identity: text.match(/<Identity\s+Id="([^"]+)"/)[1], id, versionRange, productArchitecture: text.match(/<ProductArchitecture>([^<]+)</)[1] };
  };
  const byId = id => targets.targets.find(target => target.id === id);
  for (const [architecture, targetId] of [['X86', 'tcxaeshell-x86-tc4024'], ['Amd64', 'tcxaeshell64-amd64-tc4026']]) {
    const { identity, ...installationTarget } = await manifest(architecture);
    assert.equal(byId(targetId).payload.identity, identity);
    assert.deepEqual(byId(targetId).payload.vsixInstallationTarget, installationTarget);
  }
});

test('inventory script is read-only by construction', async () => {
  const source = (await readFile(script, 'utf8')).replace(/<#[\s\S]*?#>/, '').replace(/#.*$/gm, '');
  for (const forbidden of [/Start-Process/i, /Stop-Process/i, /Invoke-Expression|\biex\b/i, /-ComObject/i, /GetActiveObject|GetRunningObjectTable|BindToMoniker/i,
    /Set-Item|New-ItemProperty|Remove-Item|Rename-Item|Move-Item|Copy-Item|Clear-Content|Set-Content|Add-Content/i,
    /CreateSubKey|DeleteSubKey|\.SetValue\(|DeleteValue/i, /&\s*\$?tcpkg|tcpkg(\.exe)?\s+(list|config|install|update|uninstall)/i,
    /OpenSubKey\([^)]*\$true/i, /Add-Type/i, /\.Kill\(|CloseMainWindow/i]) {
    assert.doesNotMatch(source, forbidden);
  }
  const writes = source.match(/WriteAll\w+\(/g) ?? [];
  assert.equal(writes.length, 1, 'only -OutputPath may be written');
});

test('verified TcXaeShell x86 with TwinCAT 4024.78 is the alpha target, without claiming acceptance', { skip: skipShell }, async () => {
  const { status, document } = await evaluate(facts(), ['-RequireAlphaTarget']);
  assert.equal(status, 0);
  assert.equal(document.collection.mode, 'supplied');
  const host = only(document);
  assert.equal(host.classification, 'alpha-target');
  assert.equal(host.installerDecision, 'eligible');
  assert.equal(host.targetId, 'tcxaeshell-x86-tc4024');
  assert.deepEqual(host.warnings, []);
  assert.equal(document.evaluation.liveAcceptance, false);
  assert.ok(host.openGates.includes('cold-start-restored-editors'));
  assert.ok(host.openGates.includes('update-rollback'));
  const alpha = targets.targets.find(target => target.id === 'tcxaeshell-x86-tc4024');
  for (const [gate, record] of Object.entries(alpha.acceptance)) assert.equal(host.gates[gate], record.status);
});

test('another 4024 sub-build is only an unverified candidate and is refused', { skip: skipShell }, async () => {
  const { status, document } = await evaluate(facts({ version: '3.1.4024.62' }), ['-RequireAlphaTarget']);
  assert.equal(status, 3);
  const host = only(document);
  assert.equal(host.classification, 'alpha-target-unverified-build');
  assert.equal(host.installerDecision, 'refuse');
  assert.match(host.reasons.join(' '), /3\.1\.4024\.62.*not a verified build/);
});

test('4026, Shell64 and full Visual Studio variants are not released by the alpha target', { skip: skipShell }, async () => {
  const cases = [
    [facts({ version: '3.1.4026.17', mechanism: 'package-manager' }), 'tcxaeshell-x86-tc4026', 'unaccepted'],
    [facts({ version: '3.1.4026.17', mechanism: 'package-manager', hosts: [shell({ id: 'shell64', kind: 'TcXaeShell64', executable: { name: 'TcXaeShell64.exe', fileVersion: '17.0.0.0', architecture: 'amd64' },
      shellBase: { major: 17 }, assistantPayloads: [] })] }), 'tcxaeshell64-amd64-tc4026', 'blocked'],
    [facts({ hosts: [shell({ id: 'vs:2019', kind: 'VisualStudio', executable: { name: 'devenv.exe', fileVersion: '16.11.37827.8', architecture: 'x86' }, shellBase: { major: 16 }, assistantPayloads: [] })] }),
      'visualstudio-2017-2019-x86', 'unaccepted'],
    [facts({ version: '3.1.4026.17', hosts: [shell({ id: 'vs:2022', kind: 'VisualStudio', executable: { name: 'devenv.exe', fileVersion: '17.14.0', architecture: 'amd64' }, shellBase: { major: 17 }, assistantPayloads: [] })] }),
      'visualstudio-2022-amd64', 'unaccepted'],
    [facts({ version: '3.1.4026.17', hosts: [shell({ id: 'vs:2026', kind: 'VisualStudio', executable: { name: 'devenv.exe', fileVersion: '18.0.0', architecture: 'amd64' }, shellBase: { major: 18 }, assistantPayloads: [] })] }),
      'visualstudio-2026', 'out-of-scope'],
  ];
  for (const [input, targetId, classification] of cases) {
    const host = only((await evaluate(input)).document);
    assert.equal(host.targetId, targetId);
    assert.equal(host.classification, classification);
    assert.equal(host.installerDecision, 'refuse');
    assert.ok(host.reasons.length > 0);
    assert.ok(Object.values(host.gates).every(gate => gate === (classification === 'blocked' ? 'blocked' : 'open')), targetId);
  }
});

test('missing integration, prerequisites or TwinCAT identity fail closed', { skip: skipShell }, async () => {
  const cases = [
    [facts({ hosts: [shell({ twinCatIntegration: { present: false, xaeBase: false, xaePlc: false } })] }), 'not-usable', /XAE Base\/Plc integration/],
    [facts({ netfx: 461308 }), 'prerequisite-missing', /\.NET Framework/],
    [facts({ webView2: [] }), 'prerequisite-missing', /WebView2/],
    [facts({ version: null }), 'unknown', /not identified/],
    [facts({ hosts: [shell({ executable: { name: 'TcXaeShell.exe', fileVersion: '15.0.1.0', architecture: 'x86' } })] }), 'unknown', /executable version/],
  ];
  for (const [input, classification, reason] of cases) {
    const host = only((await evaluate(input)).document);
    assert.equal(host.classification, classification);
    assert.equal(host.installerDecision, 'refuse');
    assert.match(host.reasons.join(' '), reason);
  }
});

test('installation-state anomalies are reported as warnings', { skip: skipShell }, async () => {
  const amd64Payload = { identity: 'TwinCatAssistant.Native.Amd64', version: '0.1.9', productArchitecture: 'amd64' };
  const input = facts({ mechanism: 'unknown', hosts: [shell({ dte: { registered: false }, assistantPayloads: [...shell().assistantPayloads, amd64Payload] })] });
  const host = only((await evaluate(input)).document);
  assert.equal(host.classification, 'alpha-target');
  const warnings = host.warnings.join('\n');
  assert.match(warnings, /2 Assistant payloads are discoverable/);
  assert.match(warnings, /declares amd64 but the host runs x86/);
  assert.match(warnings, /not the payload for tcxaeshell-x86-tc4024/);
  assert.match(warnings, /TcXaeShell\.DTE\.15\.0 is not registered/);
  assert.match(warnings, /Installation mechanism 'unknown'/);
});

test('a recorded full inventory document can be re-evaluated', { skip: skipShell }, async () => {
  const first = (await evaluate(facts())).document;
  const second = (await evaluate(first)).document;
  assert.deepEqual(second.evaluation.hosts, first.evaluation.hosts);
  const none = await evaluate(facts({ hosts: [] }), ['-RequireAlphaTarget']);
  assert.equal(none.status, 3);
  assert.equal(none.document.evaluation.eligibleForAlphaInstaller, false);
});

function hostProcesses() {
  const output = execFileSync('tasklist.exe', ['/FO', 'CSV', '/NH'], { encoding: 'utf8' });
  return output.split(/\r?\n/).filter(line => /^"(TcXaeShell|TcXaeShell64|devenv)\.exe"/i.test(line))
    .map(line => line.split('","')[1]).sort().join(',');
}

test('live collection is read-only and keeps user paths out of the record', { skip: process.platform !== 'win32' || skipShell, timeout: 60000 }, async () => {
  const before = hostProcesses();
  const { status, document, stderr } = runScript([]);
  assert.equal(status, 0, stderr);
  assert.equal(hostProcesses(), before, 'no IDE process may be started or stopped');
  assert.equal(document.kind, 'twincat-assistant-host-inventory');
  assert.equal(document.collection.mode, 'live-read-only');
  assert.equal(document.facts.twinCat.tcpkg.executed, false);
  assert.ok(Array.isArray(document.facts.hosts));
  assert.equal(document.evaluation.hosts.length, document.facts.hosts.length);
  assert.equal(document.evaluation.liveAcceptance, false);
  if (Number(document.facts.windows.build) >= 22000) assert.match(document.facts.windows.productName, /^Windows 11/);
  assert.ok(!JSON.stringify(document).toLowerCase().includes(homedir().toLowerCase().replace(/\\/g, '\\\\')), 'user profile paths must be redacted');
  for (const host of document.facts.hosts) assert.ok(host.running.every(instance => Object.keys(instance).sort().join() === 'pid,sessionId,startTimeUtc'));
});

test('in-process host classification mirrors the alpha target description', async () => {
  const source = await readFile(join(root, 'native', 'Shared', 'HostIdentity.cs'), 'utf8');
  const alpha = targets.targets.find(target => target.status === 'alpha-target');
  assert.equal(source.match(/AlphaTargetId = "([^"]+)"/)[1], alpha.id);
  assert.equal(source.match(/AlphaExecutableVersion = "([^"]+)"/)[1], alpha.match.executableFileVersion);
  const builds = [...source.match(/AlphaVerifiedTwinCatBuilds = \{([^}]*)\}/)[1].matchAll(/"([^"]+)"/g)].map(match => match[1]);
  assert.deepEqual(builds, alpha.twinCat.verifiedBuilds);
  for (const target of targets.targets.filter(target => target.status !== 'alpha-target')) {
    assert.ok(source.includes(`"${target.id}", "${target.status}"`), `${target.id} must classify as ${target.status} in process`);
  }
});
