import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, rm, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initializeState, inspectState } from '../src/state.mjs';
import { configureNativeModel, validateModelConsent } from '../src/native-chat-model.mjs';

test('model setup requires explicit consent and refuses payloads containing credentials or source', () => {
  const value = { state: join(tmpdir(), 'synthetic'), model: 'openai/synthetic-test', acceptSourceDisclosure: true };
  assert.deepEqual(validateModelConsent(value), value);
  for (const patch of [{ acceptSourceDisclosure: false }, { model: '' }, { password: 'not-accepted' }, { source: 'not-accepted' }]) {
    assert.throws(() => validateModelConsent({ ...value, ...patch }));
  }
});

test('model consent updates only a verified isolated profile and keeps all engineering permissions denied', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-native-model-'));
  try {
    const state = join(root, 'state');
    const paths = await initializeState(state, {});
    assert.equal((await inspectState(state)).modelCallsEnabled, false);
    await configureNativeModel({ state, model: 'openai/synthetic-test', acceptSourceDisclosure: true });
    const status = await inspectState(state);
    assert.equal(status.settings.model, 'openai/synthetic-test');
    assert.equal(status.modelCallsEnabled, true);
    const config = JSON.parse(await readFile(paths.configPath));
    assert.deepEqual(config.permissions[0], { action: '*', resource: '*', effect: 'deny' });
    assert.equal(config.experimental.policies.filter(p => p.action === 'provider.use' && p.effect === 'allow').length, 1);
    assert.ok(config.experimental.policies.some(p => p.action === 'provider.use' && p.resource === 'openai' && p.effect === 'allow'));
    await writeFile(paths.configPath, '{}');
    await assert.rejects(configureNativeModel({ state, model: 'anthropic/synthetic-test', acceptSourceDisclosure: true }), /modified|mismatch/);
    assert.equal(await readFile(paths.configPath, 'utf8'), '{}');
  } finally { await rm(root, { recursive: true, force: true }); }
});
