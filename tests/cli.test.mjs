import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
const run = promisify(execFile);
const cli = resolve('src/cli.mjs');

test('operator help exposes the supervised upstream start command', async () => {
  const result = await run(process.execPath, [cli, '--help']);
  assert.match(result.stdout, /start --state/);
});

test('operator CLI initializes and diagnoses real isolated state from outside the checkout', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-cli-'));
  try {
    const state = join(parent, 'state');
    const settings = join(parent, 'settings.json');
    await writeFile(settings, '{}');
    const first = await run(process.execPath, [cli, 'init', '--settings', settings, '--state', state], { cwd: parent });
    assert.equal(JSON.parse(first.stdout).initialized, true);
    const doctor = await run(process.execPath, [cli, 'doctor', '--state', state], { cwd: parent });
    assert.equal(JSON.parse(doctor.stdout).configurationVerified, true);
    assert.equal(JSON.parse(doctor.stdout).nativeEngineering, 'unavailable');
    await assert.rejects(run(process.execPath, [cli, 'init', '--settings', settings, '--state', state]));
    await assert.rejects(run(process.execPath, [cli, 'doctor', '--state', state, '--unknown']));
  } finally { await rm(parent, { recursive: true, force: true }); }
});
