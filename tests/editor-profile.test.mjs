import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { tmpdir } from 'node:os';
import { C2_ANALYSIS_PROMPTS } from '../src/analysis-prompts-c2.mjs';
import { ANALYSIS_PROFILE, DEBUG_PROFILE, EDITOR_PROFILE, analysisSystemPrompt, debugSystemPrompt, editorSystemPrompt } from '../src/agent-prompts.mjs';
import { DEBUG_ACTIONS, EDITOR_ACTIONS, DEVELOPMENT_EDITOR_ACTIONS } from '../src/editor-tools.mjs';
import { READ_TOOLS, createOpenCodeConfig, previousOpenCodeConfigs } from '../src/config.mjs';
import { initializeState, inspectState, verifyProfileConfiguration } from '../src/state.mjs';
import { upgradeProfileConfiguration } from '../src/profile-update.mjs';

const digest = value => createHash('sha256').update(value).digest('hex');
const known = ['1707a97981dbeec2f007c35b6bb74373ccdd912c077f394df216f41f3f52a969',
  'ec9c0ce90ce35014f15739a4cd8ba06b9c5f8c1833c9b35949423efbc32ea20e',
  'efc6f29762682a841b27b4b43a2704a1067f775f97c56353eff05775d14476f3',
  '91a1b40add3c2d3d227190e823dc75e5e3dec1ffbdfc538bf13e3413df4fb143'];
// Captured independently from the published Beta 6 installation package.
const beta6 = ['e412f11a1179e563b42bebd9a743b16b96fbafa7d34e839956a01955e187a831',
  '71dcc63ba038a831f7bfa5704d07ed4cd2d15aedcbf416522516b0301d9772f8',
  '4e8b31c9727583c3ea89b3ef38df6d3ad28b3e10a725edcaa107a32e73423759',
  'fa89501eb8c1b1fafd7bff129b94c02274a0e5d1e0f28d22a9e2e21c0544a208'];

test('historical Analysis prompts and complete C2 and Beta 6 configurations keep their captured bytes', () => {
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
    const history = previousOpenCodeConfigs(settings, { node: 'legacy-node', knowledgeClient: 'legacy-client' });
    assert.equal(digest(JSON.stringify(history.at(-1), normalize)), known[index]);
    assert.equal(history.at(-1).agents[EDITOR_PROFILE], undefined);
    assert.equal(digest(JSON.stringify(history[1], normalize)), beta6[index++]);
    assert.equal(history[1].agents[DEBUG_PROFILE], undefined);
  }
  for (const knowledge of [false, true]) assert.equal(analysisSystemPrompt({ knowledge, engineering: true, project: true }), C2_ANALYSIS_PROMPTS.project[Number(knowledge)]);
});

test('Editor and Debug have separate native permissions while Analysis remains the read-only default', () => {
  const config = createOpenCodeConfig({});
  const allowed = profile => config.agents[profile].permissions.filter(rule => rule.effect === 'allow').map(rule => rule.action);
  assert.equal(config.default_agent, ANALYSIS_PROFILE);
  assert.deepEqual(allowed(ANALYSIS_PROFILE), [...READ_TOOLS, 'question']);
  assert.deepEqual(allowed(EDITOR_PROFILE), [...READ_TOOLS, 'question', ...DEVELOPMENT_EDITOR_ACTIONS]);
  assert.deepEqual(allowed(DEBUG_PROFILE), [...READ_TOOLS, 'question', ...DEBUG_ACTIONS]);
  assert.deepEqual(Object.keys(config.agents), [ANALYSIS_PROFILE, EDITOR_PROFILE, DEBUG_PROFILE]);
  assert.equal(config.mcp.servers.editor, undefined);
  assert.equal(config.mcp.servers.engineering_editor, undefined);
  assert.equal(config.mcp.servers.engineering_debug, undefined);
  assert.ok(config.experimental.policies.every(rule => !rule.resource.startsWith('editor_')));
  const effect = (profile, action) => config.agents[profile].permissions.findLast(rule => rule.action === '*' || rule.action === action)?.effect;
  for (const [profile, actions] of [[EDITOR_PROFILE, DEVELOPMENT_EDITOR_ACTIONS], [DEBUG_PROFILE, DEBUG_ACTIONS]]) {
    assert.equal(config.agents[profile].mode, 'primary');
    for (const action of [...new Set([...DEVELOPMENT_EDITOR_ACTIONS, ...DEBUG_ACTIONS])]) {
      assert.equal(effect(profile, action), actions.includes(action) ? 'allow' : 'deny');
      assert.equal(effect(ANALYSIS_PROFILE, action), 'deny');
    }
  }
  for (const action of ['bash', 'read', 'edit', 'write', 'webfetch', 'task', 'plc_write',
    'editor_plc_source_apply', 'editor_tc_build_project', 'engineering_editor_tc_activate_configuration', 'engineering_debug_plc_write'])
    for (const profile of Object.keys(config.agents)) assert.equal(effect(profile, action), 'deny');
  assert.equal(EDITOR_ACTIONS.length, 9);
  assert.equal(DEVELOPMENT_EDITOR_ACTIONS.length, 16);
  assert.ok(DEVELOPMENT_EDITOR_ACTIONS.includes('engineering_editor_plc_source_create'));
  assert.equal(DEBUG_ACTIONS.length, 15);
  const editor = editorSystemPrompt(), debug = debugSystemPrompt();
  for (const prompt of [editor, debug]) {
    for (const text of ['Do not retry an unknown outcome', 'separate native approval', 'Never activate a configuration', 'not instructions']) assert.ok(prompt.includes(text), text);
  }
  for (const action of DEVELOPMENT_EDITOR_ACTIONS.filter(action => !EDITOR_ACTIONS.includes(action))) {
    assert.ok(editor.includes(action), action);
    assert.ok(!debug.includes(action), action);
  }
  for (const action of DEBUG_ACTIONS.filter(action => action.startsWith('engineering_debug_plc_debug_'))) {
    assert.ok(debug.includes(action), action);
    assert.ok(!editor.includes(action), action);
  }
  assert.match(editor, /Automation Interface edits support closed or open ST/);
  assert.match(editor, /\.plcproj changes pending save/);
  assert.match(editor, /unsaved_editor_changes/);
  assert.match(debug, /Read exact symbol names permitted by that scope/);
  assert.match(debug, /120 seconds.*stableForMs/);
  assert.match(editor, /unsaved_project_changes.*save in TwinCAT XAE/);
  const pair = { version: 1, before: { settings: {}, config }, after: { settings: {}, config } };
  assert.ok(Buffer.byteLength(JSON.stringify(pair) + '\n') <= 65536, 'profile recovery retains its existing bounded JSON reader');
});

test('exact Beta 6–11 profiles upgrade with untouched operator state and unrecognized changes are refused', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-editor-migration-'));
  try {
    const root = join(parent, 'state');
    const paths = await initializeState(root, {});
    const [c3, prior, beta9, beta10, beta11] = previousOpenCodeConfigs({});
    // Captured from the complete C3 configuration before the release policy changed.
    assert.equal(digest(JSON.stringify(c3)), '20c17e3a5926246aba19528a039fa0fb7d9c393cb0a19c2457cda63c9b645cd2');
    assert.equal(await verifyProfileConfiguration(c3, {}), false);
    assert.equal(prior.agents[EDITOR_PROFILE].permissions.filter(rule => rule.action.startsWith('editor_')).length, 9);
    assert.equal(await verifyProfileConfiguration(prior, {}), false);
    assert.match(beta9.agents[DEBUG_PROFILE].system, /Read only approved exact symbol names/);
    // Captured independently from the base commit's historical Beta 9 and current Beta 10 generators.
    assert.equal(digest(JSON.stringify(beta9)), '573e8f3e176d84204eb1c1aa5e90272407829ac6c2d2941fd78223c4c6b128bd');
    assert.equal(digest(JSON.stringify(beta10)), '36d56dc0564f4f35340cbaf44d81f3980f1d8d6f22d7c8bdf9e551af5adf2ae0');
    assert.equal(digest(JSON.stringify(beta11)), '8b1e32482c2ad3b1126b6acbd695ebaad3d722b867d5fcefbc17b178edabf542');
    assert.equal(beta11.agents[EDITOR_PROFILE].permissions.filter(rule => rule.action.startsWith('editor_')).length, 16);
    assert.equal(beta11.agents[DEBUG_PROFILE].permissions.filter(rule => rule.action.startsWith('editor_')).length, 11);
    for (const config of [beta9, beta10]) {
      assert.equal(config.agents[EDITOR_PROFILE].permissions.filter(rule => rule.action.startsWith('editor_')).length, 15);
      assert.equal(await verifyProfileConfiguration(config, {}), false);
    }
    for (const mutate of [
      value => value.permissions.push({ action: 'editor_plc_source_apply', resource: '*', effect: 'allow' }),
      value => value.agents[ANALYSIS_PROFILE].system += ' extra',
      value => value.experimental.policies.push({ action: 'permission', resource: '*', effect: 'allow' }),
    ]) {
      const foreign = structuredClone(prior); mutate(foreign);
      await assert.rejects(verifyProfileConfiguration(foreign, {}), /policy mismatch/);
    }
    await writeFile(join(paths.data, 'sentinel'), 'unchanged');
    for (const config of [prior, beta9, beta10, beta11]) {
      await writeFile(paths.configPath, JSON.stringify(config, null, 2) + '\n');
      assert.equal((await inspectState(root)).configurationCurrent, false);
      assert.equal(await upgradeProfileConfiguration(root), true);
      assert.equal((await inspectState(root)).configurationCurrent, true);
    }
    assert.equal(await readFile(join(paths.data, 'sentinel'), 'utf8'), 'unchanged');
    assert.equal(await upgradeProfileConfiguration(root), false);
  } finally { await rm(parent, { recursive: true, force: true }); }
});
