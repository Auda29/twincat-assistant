import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { transform } from 'esbuild';
import { z } from 'zod';

async function makeBridge() {
  const source = await readFile(new URL('../upstream/overlays/openchamber/twincat.ts', import.meta.url), 'utf8');
  const stripped = source.replace(/^import .*;\r?\n/gm, '');
  const prelude = `
    const stub = new Proxy(function () { return stub }, { get() { return stub }, apply() { return stub } });
    const React = {
      createElement: (type, props, ...children) => ({ type, props, children }),
      useSyncExternalStore: (_subscribe, snapshot) => snapshot(),
    };
    const z = globalThis.__z;
    const DropdownMenu = 'menu', DropdownMenuContent = 'popup', DropdownMenuLabel = 'label',
      DropdownMenuRadioGroup = 'radio-group', DropdownMenuRadioItem = 'radio-item', DropdownMenuTrigger = 'trigger', Icon = 'icon';
    const useSessionUIStore = globalThis.__sessionStore;
    const useUIStore = stub, useConfigStore = stub, useInputStore = stub;
    const locale = { dictionary: {
      'chat.twincat.profile.label': 'Profil',
      'chat.twincat.profile.analysis': 'Analyse',
      'chat.twincat.profile.switching': 'Profilwechsel …',
      'chat.twincat.profile.projectRequired': 'Open the project chat',
    } };
    const useI18nStore = Object.assign(select => select(locale), { getState: () => locale });
    const opencodeClient = stub, buildExportFilename = stub, formatSessionAsMarkdown = stub;
    const getChatDraftIdentityKey = stub, useGlobalSessionsStore = stub, resolveGlobalSessionDirectory = stub;
    const archiveSession = stub, unarchiveSession = stub, updateSessionTitle = stub, registerSessionDirectory = stub;
    const collectSessionTitleTurns = stub, generateSessionTitle = stub, generateAndSaveSessionTitle = stub;
    const runSessionTitleGeneration = stub, getRuntimeKey = stub;
  `;
  const compiled = await transform(prelude + stripped, { loader: 'ts', format: 'cjs', target: 'es2022' });
  let state = { currentSessionId: 'a', currentSessionDirectory: 'C:/project' };
  const subscribers = new Set();
  const store = { getState: () => state, subscribe: fn => { subscribers.add(fn); return () => subscribers.delete(fn); } };
  const timers = new Map();
  let timerId = 0;
  const sandbox = { module: { exports: {} }, exports: {}, __sessionStore: store, __z: z,
    setTimeout(callback, delay) { timers.set(++timerId, { callback, delay }); return timerId; },
    clearTimeout(id) { timers.delete(id); } };
  sandbox.exports = sandbox.module.exports;
  vm.runInNewContext(compiled.code, sandbox);
  return { api: sandbox.module.exports, select(next) { state = next; for (const fn of subscribers) fn(state); },
    expire() { for (const { callback, delay } of [...timers.values()]) { assert.equal(delay, 120_000); callback(); } } };
}

const agents = [{ name: 'twincat-readonly' }, { name: 'twincat-editor' }, { name: 'other' }];
const names = values => Array.from(values, x => x.name);
const context = { project: 'project-key', object: 'A', treePath: 'A', sourceKind: 'live', member: null };
function approve(api, profile = 'twincat-editor') {
  api.requestTwinCatProfile(profile);
  const request = api.twinCatSource.peekProfileRequest('a');
  assert.ok(request);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, profile, requestId: request.id }), true);
  assert.equal(api.twinCatSource.ackProfileRequest('a', request.id), true);
}

test('Editor requires native enable on the exact committed chat and revokes on identity changes', async () => {
  const { api, select } = await makeBridge();
  assert.deepEqual(names(api.twinCatSelectableAgents(agents)), names(agents));
  api.twinCatSource.activate();
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), false);
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'other', enabled: true }), false);
  approve(api);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  assert.deepEqual(names(api.twinCatSelectableAgents(agents)), ['twincat-editor']);
  api.twinCatSource.setContext('a', { ...context, object: 'B', treePath: 'B', reference: 'new-revision' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  select({ currentSessionId: 'b', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  select({ currentSessionId: 'a', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.deepEqual(names(api.twinCatSelectableAgents(agents)), ['twincat-readonly']);
});

test('view readiness masks Editor temporarily; project change and explicit revocation remove it', async () => {
  const { api } = await makeBridge();
  api.twinCatSource.activate();
  const unpublish = api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  approve(api);
  unpublish();
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.publishTwinCatView('a', 'C:/project', false);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.publishTwinCatView('a', 'C:/project', true);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  api.twinCatSource.setContext('a', null, context.project);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  api.twinCatSource.setContext('a', { ...context, project: 'other-project' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  approve(api);
  api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: false });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
});

test('malformed native profile requests refuse and revoke an existing grant', async () => {
  const { api } = await makeBridge();
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  for (const malformed of [null, {}, { sessionId: 7, enabled: true }, { sessionId: 'a', enabled: 'true' },
    { sessionId: 'a', enabled: true, extra: 'ignored?' }, { sessionId: 'x'.repeat(257), enabled: true }]) {
    approve(api);
    assert.equal(api.twinCatSource.setEditorProfile(malformed), false);
    assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  }
});

test('requests survive repeated peeks and only the exact latest acknowledgement clears pending', async () => {
  const { api, select } = await makeBridge();
  api.twinCatSource.activate();
  const cleanup = api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  api.requestTwinCatProfile('twincat-editor');
  const first = api.twinCatSource.peekProfileRequest('a');
  assert.equal(first.profile, 'twincat-editor');
  assert.equal(api.twinCatSource.peekProfileRequest('a').id, first.id);
  assert.equal(api.twinCatSource.peekProfileRequest('b'), null);
  assert.throws(api.requireTwinCatProfileSettled, /Profilwechsel/);
  cleanup();
  api.publishTwinCatView('a', 'C:/project', false);
  api.twinCatSource.setContext('a', null, context.project);
  assert.equal(api.twinCatSource.peekProfileRequest('a'), null);
  assert.equal(api.twinCatProfilePending(), true);
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', { ...context, object: 'B' });
  assert.equal(api.twinCatSource.peekProfileRequest('a').id, first.id);

  api.requestTwinCatProfile('twincat-debug');
  const second = api.twinCatSource.peekProfileRequest('a');
  assert.ok(second.id > first.id);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, requestId: first.id }), false);
  assert.equal(api.twinCatSource.ackProfileRequest('a', first.id), false);
  assert.equal(api.twinCatSource.ackProfileRequest('b', second.id), false);
  assert.equal(api.twinCatSource.ackProfileRequest('a', second.id), true);
  assert.doesNotThrow(api.requireTwinCatProfileSettled);

  api.requestTwinCatProfile('twincat-editor');
  select({ currentSessionId: 'b', currentSessionDirectory: 'C:/project' });
  select({ currentSessionId: 'a', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatSource.peekProfileRequest('a'), null);
  api.requestTwinCatProfile('twincat-debug');
  select({ currentSessionId: 'a', currentSessionDirectory: 'C:/different' });
  select({ currentSessionId: 'a', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatSource.peekProfileRequest('a'), null);
});

test('profile context tracks Analysis, Editor and Debug within one conversation', async () => {
  const { api, expire } = await makeBridge();
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context, 'exact-project');
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Analysis \(twincat-readonly\)/);
  approve(api);
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Editor \(twincat-editor\)/);
  approve(api, 'twincat-debug');
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Debug \(twincat-debug\)/);
  assert.match(api.twinCatContextTexts('a')[0], /Earlier messages.*Current tool permissions follow the active profile/);
  assert.match(api.twinCatContextTexts('a')[0], /metadata only.*[\s\S]+contains no source code and does not verify the unsaved editor buffer/);
  assert.match(api.twinCatContextTexts('general')[0], /Active profile: Analysis/);
  api.requestTwinCatProfile('twincat-readonly');
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  expire();
  assert.equal(api.twinCatSource.peekProfileRequest('a'), null);
  assert.doesNotThrow(api.requireTwinCatProfileSettled);
  approve(api, 'twincat-debug');
  api.twinCatSource.setContext('a', context, 'another-project-with-the-same-name');
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.requestTwinCatProfile('twincat-editor');
  api.twinCatSource.setContext('a', null);
  assert.equal(api.twinCatProfilePending(), false);
});

test('the shared profile menu marks only the approved profile and explains disabled choices', async () => {
  const { api } = await makeBridge();
  api.twinCatSource.activate();
  let [trigger, popup] = api.TwinCatProfilePicker().children;
  let [, group, hint] = popup.children;
  assert.equal(trigger.type, 'trigger');
  assert.equal(popup.props.side, 'top');
  assert.equal(hint.children[0], 'Open the project chat');
  assert.deepEqual(Array.from(group.children[0], item => item.props.disabled), [false, true, true]);

  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  [, popup] = api.TwinCatProfilePicker().children;
  group = popup.children[1];
  group.props.onValueChange('twincat-debug');
  assert.equal(api.twinCatSource.peekProfileRequest('a').profile, 'twincat-debug');
  assert.equal(api.TwinCatProfilePicker().children[1].children[1].props.value, 'twincat-readonly');
  approve(api, 'twincat-debug');
  assert.equal(api.TwinCatProfilePicker().children[1].children[1].props.value, 'twincat-debug');
});

test('overlay routes composer, controls, live submission and mobile display through the active profile', async () => {
  const changes = JSON.parse(await readFile(new URL('../upstream/overlays/openchamber/changes.json', import.meta.url), 'utf8'));
  const output = file => changes.find(x => x.file === file).changes.map(pair => pair[1]).join('\n');
  const input = output('packages/ui/src/components/chat/ChatInput.tsx');
  const controls = output('packages/ui/src/components/chat/ModelControls.tsx');
  const mobile = output('packages/ui/src/components/chat/MobileAgentButton.tsx');
  const dispatch = output('packages/ui/src/sync/session-ui-store.ts');
  const client = output('packages/ui/src/lib/opencode/client.ts');
  assert.match(input, /currentAgentName = isTwinCatEmbedded\(\) \? twinCatProfile/);
  assert.match(controls, /if \(isTwinCatEmbedded\(\)\) return twinCatActiveProfile\(\)/);
  assert.match(controls, /agentName !== TWINCAT_ANALYSIS_PROFILE && agentName !== twinCatActiveProfile\(\)/);
  assert.match(mobile, /const twinCatProfile = React\.useSyncExternalStore/);
  assert.match(mobile, /twinCatEmbedded \? twinCatProfile/);
  assert.match(controls, /agent.name === TWINCAT_DEBUG_PROFILE \? 'Debug'/);
  assert.match(mobile, /twinCatProfile === TWINCAT_DEBUG_PROFILE \? 'Debug'/);
  assert.match(input, /if \(twinCatProfilePending\(\)\) return/);
  assert.match(dispatch, /selection.agent = sameChat \? twinCatActiveProfile\(\)/);
  assert.match(dispatch, /beforeOptimisticInsert: refreshTwinCatSelection/);
  assert.match(dispatch, /refreshTwinCatSelection\(\)[\s\S]*const commandContext/);
  assert.match(dispatch, /validateProfile,[\s\S]*text: params.content/);
  assert.match(client, /const prompt = .*\n +validateProfile\(\)/);
  assert.match(client, /params.validateProfile\?\.\(\)\n +await call\("session.command"/);
});
