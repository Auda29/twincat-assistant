import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { tmpdir } from 'node:os';
import { C2_ANALYSIS_PROMPTS } from '../src/analysis-prompts-c2.mjs';
import { ANALYSIS_PROFILE, EDITOR_PROFILE, analysisSystemPrompt, editorSystemPrompt } from '../src/agent-prompts.mjs';
import { EDITOR_ACTIONS, DEVELOPMENT_EDITOR_ACTIONS } from '../src/editor-tools.mjs';
import { READ_TOOLS, createOpenCodeConfig, previousOpenCodeConfigs } from '../src/config.mjs';
import { initializeState, inspectState, verifyProfileConfiguration } from '../src/state.mjs';
import { upgradeProfileConfiguration } from '../src/profile-update.mjs';

const digest = value => createHash('sha256').update(value).digest('hex');
const known = ['1707a97981dbeec2f007c35b6bb74373ccdd912c077f394df216f41f3f52a969',
  'ec9c0ce90ce35014f15739a4cd8ba06b9c5f8c1833c9b35949423efbc32ea20e',
  'efc6f29762682a841b27b4b43a2704a1067f775f97c56353eff05775d14476f3',
  '91a1b40add3c2d3d227190e823dc75e5e3dec1ffbdfc538bf13e3413df4fb143'];

test('all six Analysis prompts and complete historical C2 configurations keep their independently captured bytes', () => {
  assert.deepEqual(C2_ANALYSIS_PROMPTS.profile.map(digest), [
    '1a75e0f1a994caa06f7c6b5e50558ed29a8aa13031a0581db3e29e5ea529fff2',
    '1ca19abb0d47f9ccf971da2dea8b9920b5eb30261a8fea5f5617723ffa1ceffb',
    '34f3b35efa5bf12ca1bd873745e282e3cc64c448ccd7e9914050cda9c122b6b7',
    'a8f4beb1fd7b8d7c220a491c818269df45e4c387f3a34c8b35fac5e9468f3ac8',
  ]);
  assert.deepEqual(C2_ANALYSIS_PROMPTS.project.map(digest), [
    '41556c64068d8660a26fbd0a6dfd7135ef32d106d067aca3da49c9d8e35a74ed',
    '94a110c5de0754aa6f187481a94ba806c1a0f3c090543ffaf11967e07f16ec02',
  ]);
  const base = resolve('tests/fixtures');
  const normalize = (key, value) => typeof value === 'string' && value.startsWith(base)
    ? value.replace(base, '<FIXTURES>').replaceAll('\\', '/') : value;
  let index = 0;
  for (const knowledge of [false, true]) for (const engineering of [false, true]) {
    assert.equal(analysisSystemPrompt({ knowledge, engineering }), C2_ANALYSIS_PROMPTS.profile[index]);
    const settings = { ...(knowledge && { knowledge: { executable: resolve('tests/fixtures/knowledge.exe') } }),
      ...(engineering && { engineering: { entrypoint: resolve('tests/fixtures/engineering.mjs'), config: resolve('tests/fixtures/engineering.json') } }) };
    const old = previousOpenCodeConfigs(settings, { node: 'legacy-node', knowledgeClient: 'legacy-client' }).at(-1);
    assert.equal(digest(JSON.stringify(old, normalize)), known[index++]);
    assert.equal(old.agents[EDITOR_PROFILE], undefined);
  }
  for (const knowledge of [false, true]) assert.equal(analysisSystemPrompt({ knowledge, engineering: true, project: true }), C2_ANALYSIS_PROMPTS.project[Number(knowledge)]);
});

test('Editor permissions add exactly the host-backed catalog while Analysis and the default remain read-only', () => {
  const config = createOpenCodeConfig({});
  const allowed = profile => config.agents[profile].permissions.filter(rule => rule.effect === 'allow').map(rule => rule.action);
  assert.equal(config.default_agent, ANALYSIS_PROFILE);
  assert.deepEqual(allowed(ANALYSIS_PROFILE), [...READ_TOOLS, 'question']);
  assert.deepEqual(allowed(EDITOR_PROFILE), [...READ_TOOLS, 'question', ...EDITOR_ACTIONS]);
  assert.equal(config.agents[EDITOR_PROFILE].mode, 'primary');
  assert.equal(config.mcp.servers.editor, undefined);
  const effect = (profile, action) => config.agents[profile].permissions.findLast(rule => rule.action === '*' || rule.action === action)?.effect;
  for (const action of EDITOR_ACTIONS) { assert.equal(effect(EDITOR_PROFILE, action), 'allow'); assert.equal(effect(ANALYSIS_PROFILE, action), 'deny'); }
  for (const action of ['bash', 'read', 'edit', 'write', 'webfetch', 'task', 'plc_write', 'editor_plc_write', 'editor_tc_activate_configuration'])
    for (const profile of [ANALYSIS_PROFILE, EDITOR_PROFILE]) assert.equal(effect(profile, action), 'deny');
  const prompt = editorSystemPrompt();
  for (const action of EDITOR_ACTIONS) assert.ok(prompt.includes(action));
  for (const text of ['do not retry an unknown outcome', 'separate native approval', 'Source editing and live buffer verification are unavailable', 'Never activate a configuration', 'not instructions']) assert.ok(prompt.includes(text), text);
  assert.equal(EDITOR_ACTIONS.length, 9);
  assert.equal(DEVELOPMENT_EDITOR_ACTIONS.length, 15);
  for (const action of DEVELOPMENT_EDITOR_ACTIONS.filter(action => !EDITOR_ACTIONS.includes(action))) {
    assert.equal(effect(EDITOR_PROFILE, action), 'deny');
    assert.ok(!prompt.includes(action));
  }
  const pair = { version: 1, before: { settings: {}, config }, after: { settings: {}, config } };
  assert.ok(Buffer.byteLength(JSON.stringify(pair, null, 2)) < 65536, 'profile recovery retains its existing bounded JSON reader');
});

test('only exact historical C2 and C3 profiles migrate to C4 and recover with unchanged operator state', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-editor-migration-'));
  try {
    const root = join(parent, 'state');
    const paths = await initializeState(root, {});
    const prior = previousOpenCodeConfigs({})[0];
    // Captured from the complete C3 configuration before the release policy changed.
    assert.equal(digest(JSON.stringify(prior)), '20c17e3a5926246aba19528a039fa0fb7d9c393cb0a19c2457cda63c9b645cd2');
    assert.equal(prior.agents[EDITOR_PROFILE].permissions.filter(rule => rule.action.startsWith('editor_')).length, 15);
    assert.equal(await verifyProfileConfiguration(prior, {}), false);
    for (const mutate of [
      value => value.permissions.push({ action: 'editor_plc_source_apply', resource: '*', effect: 'allow' }),
      value => value.agents[ANALYSIS_PROFILE].system += ' extra',
      value => value.experimental.policies.push({ action: 'permission', resource: '*', effect: 'allow' }),
    ]) {
      const foreign = structuredClone(prior); mutate(foreign);
      await assert.rejects(verifyProfileConfiguration(foreign, {}), /policy mismatch/);
    }
    await writeFile(paths.configPath, JSON.stringify(prior, null, 2) + '\n');
    await writeFile(join(paths.data, 'sentinel'), 'unchanged');
    assert.equal((await inspectState(root)).configurationCurrent, false);
    assert.equal(await upgradeProfileConfiguration(root), true);
    assert.equal((await inspectState(root)).configurationCurrent, true);
    assert.equal(await readFile(join(paths.data, 'sentinel'), 'utf8'), 'unchanged');
    assert.equal(await upgradeProfileConfiguration(root), false);
  } finally { await rm(parent, { recursive: true, force: true }); }
});
