import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const windows = process.platform === 'win32';
const capture = path.resolve('scripts/Capture-HostColdStart.ps1');
function invoke(args) {
  return spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', capture, ...args], { encoding: 'utf8', timeout: 15000 });
}
function fixture() {
  mkdirSync('local-data', { recursive: true });
  return mkdtempSync(path.resolve('local-data/host-capture-guard-'));
}
test('cold capture refuses paths outside owned local-data before host access', { skip: !windows }, () => {
  const result = invoke(['-Mode', 'Prepare', '-PreparedRun', path.resolve('docs')]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /own synthetic acceptance run/);
});
test('cold preparation refuses incomplete synthetic runs', { skip: !windows }, () => {
  const result = invoke(['-Mode', 'Prepare', '-PreparedRun', fixture()]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /First complete the synthetic host run/);
});
test('cold launch refuses missing coordinated preparation', { skip: !windows }, () => {
  const result = invoke(['-Mode', 'Launch', '-PreparedRun', fixture()]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Prepare after final coordinated payload/);
});
test('a prepared receipt cannot turn a warm launch into cold acceptance', { skip: !windows }, () => {
  const root = fixture();
  writeFileSync(path.join(root, 'cold-start-prepared.json'), JSON.stringify({
    schemaVersion: 1, suffix: 'AssistantAcceptance' + 'a'.repeat(32),
    solution: path.join(root, 'synthetic/ContextChecks.sln'),
  }));
  const result = invoke(['-Mode', 'Launch', '-PreparedRun', root]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Actual logon time and explicit first-Shell/);
});
test('capture scripts parse in Windows PowerShell 5.1', { skip: !windows }, () => {
  const code = "$bad=$false; foreach($f in @('scripts/Build-HostAcceptance.ps1','scripts/Test-IsolatedHostAcceptance.ps1','scripts/Capture-HostColdStart.ps1')) {$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path (Get-Location) $f),[ref]$tokens,[ref]$errors);if($errors.Count){$bad=$true;$errors|ForEach-Object{Write-Output $_.Message}}};if($bad){exit 1}";
  const result = spawnSync('powershell.exe', ['-NoProfile', '-EncodedCommand', Buffer.from(code, 'utf16le').toString('base64')], { encoding: 'utf8', timeout: 15000 });
  assert.equal(result.status, 0, result.stdout + result.stderr);
});
