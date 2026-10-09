import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initializeState } from '../src/state.mjs';
import { prepareLaunch, runVersion } from '../src/launcher.mjs';

test('version gate uses actual bounded subprocess output and refuses wrong or stalled executables', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-version-'));
  try {
    const script = join(parent, 'version.mjs');
    await writeFile(script, 'console.log("opencode v2.0.23")');
    assert.equal(await runVersion(process.execPath, [script], {}, parent, '2.0.23'), '2.0.23');
    await assert.rejects(runVersion(process.execPath, [script], {}, parent, '2.0.99'), /version/);
    await writeFile(script, 'setInterval(() => {}, 1000)');
    await assert.rejects(runVersion(process.execPath, [script], {}, parent, '2.0.23', 100), /version/);
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('version subprocess cannot receive UI, provider or unrelated secrets', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-secret-version-'));
  try {
    const script = join(parent, 'probe.mjs');
    await writeFile(script, `if (['OPENCHAMBER_UI_PASSWORD','OPENAI_API_KEY','GITHUB_TOKEN','OPENCODE_CONFIG_CONTENT'].some(k => process.env[k])) process.exit(7); console.log('2.0.23');`);
    assert.equal(await runVersion(process.execPath, [script], { OPENCHAMBER_UI_PASSWORD: 'private-test-value', OPENAI_API_KEY: 'private-test-value', GITHUB_TOKEN: 'private-test-value', OPENCODE_CONFIG_CONTENT: '{}' }, parent, '2.0.23'), '2.0.23');
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('launch plan requires protected UI and binds the pinned upstreams to an isolated loopback profile', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-launch-'));
  try {
    const root = join(parent, 'runtime');
    await initializeState(root, {});
    const oc = join(parent, 'openchamber.mjs');
    const op = join(parent, 'opencode');
    await writeFile(oc, ''); await writeFile(op, '');
    await assert.rejects(prepareLaunch(root, { openchamber: oc, opencode: op, port: 3123 }, {}), /password/);
    const plan = await prepareLaunch(root, { openchamber: oc, opencode: op, port: 3123 }, { OPENCHAMBER_UI_PASSWORD: 'private-test-value', GITHUB_TOKEN: 'do-not-forward' });
    assert.ok(plan.command, 'upstream process launch must be implemented');
    assert.equal(plan.command, process.execPath);
    assert.deepEqual(plan.args, [oc, 'serve', '--foreground', '--host', '127.0.0.1', '--port', '3123']);
    assert.equal(plan.env.OPENCODE_BINARY, op);
    assert.equal(plan.env.OPENCHAMBER_UI_PASSWORD, 'private-test-value');
    assert.equal(plan.env.GITHUB_TOKEN, undefined);
    assert.equal(plan.cwd, join(root, 'workspace'));
  } finally { await rm(parent, { recursive: true, force: true }); }
});
