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
    const useI18nStore = select => select({ dictionary: {
      'chat.twincat.profile.label': 'Profil',
      'chat.twincat.profile.analysis': 'Analyse',
      'chat.twincat.profile.projectRequired': 'Open the project chat',
    } });
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
  const sandbox = { module: { exports: {} }, exports: {}, __sessionStore: store, __z: z };
  sandbox.exports = sandbox.module.exports;
  vm.runInNewContext(compiled.code, sandbox);
  return { api: sandbox.module.exports, select(next) { state = next; for (const fn of subscribers) fn(state); } };
}

const agents = [{ name: 'twincat-readonly' }, { name: 'twincat-editor' }, { name: 'other' }];
const names = values => Array.from(values, x => x.name);
const context = { project: 'project-key', object: 'A', treePath: 'A', sourceKind: 'live', member: null };

test('Editor requires native enable on the exact committed chat and revokes on identity changes', async () => {
  const { api, select } = await makeBridge();
  assert.deepEqual(names(api.twinCatSelectableAgents(agents)), names(agents));
  api.twinCatSource.activate();
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), false);
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'other', enabled: true }), false);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), true);
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
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), true);
  unpublish();
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.publishTwinCatView('a', 'C:/project', false);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.publishTwinCatView('a', 'C:/project', true);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  api.twinCatSource.setContext('a', null);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  api.twinCatSource.setContext('a', { ...context, project: 'other-project' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.twinCatSource.setContext('a', context);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), true);
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
    assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true }), true);
    assert.equal(api.twinCatSource.setEditorProfile(malformed), false);
    assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  }
});

test('a picker request is consumed once and cannot grant tools or follow another chat', async () => {
  const { api, select } = await makeBridge();
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);

  api.requestTwinCatProfile('twincat-editor');
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.takeProfileRequest('a'), 'twincat-editor');
  assert.equal(api.twinCatSource.takeProfileRequest('a'), null);
  api.requestTwinCatProfile('twincat-debug');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, profile: 'twincat-editor' }), false);
  assert.equal(api.twinCatSource.takeProfileRequest('a'), 'twincat-debug');

  api.requestTwinCatProfile('twincat-editor');
  select({ currentSessionId: 'b', currentSessionDirectory: 'C:/project' });
  select({ currentSessionId: 'a', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatSource.takeProfileRequest('a'), null);
});

test('Debug becomes active only through the bound native grant', async () => {
  const { api, select } = await makeBridge();
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  api.requestTwinCatProfile('twincat-debug');
  assert.equal(api.twinCatSource.takeProfileRequest('a'), 'twincat-debug');
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, profile: 'twincat-debug' }), true);
  assert.equal(api.twinCatSource.activeProfile(), 'twincat-debug');
  api.requestTwinCatProfile('twincat-readonly');
  assert.equal(api.twinCatSource.activeProfile(), 'twincat-readonly');
  assert.equal(api.twinCatSource.takeProfileRequest('a'), 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, profile: 'twincat-debug' }), true);
  select({ currentSessionId: 'b', currentSessionDirectory: 'C:/project' });
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
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
  assert.equal(api.twinCatSource.takeProfileRequest('a'), 'twincat-debug');
  assert.equal(api.TwinCatProfilePicker().children[1].children[1].props.value, 'twincat-readonly');
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, profile: 'twincat-debug' }), true);
  assert.equal(api.TwinCatProfilePicker().children[1].children[1].props.value, 'twincat-debug');
});

test('overlay routes composer, controls, live submission and mobile display through the active profile', async () => {
  const changes = JSON.parse(await readFile(new URL('../upstream/overlays/openchamber/changes.json', import.meta.url), 'utf8'));
  const output = file => changes.find(x => x.file === file).changes.map(pair => pair[1]).join('\n');
  const input = output('packages/ui/src/components/chat/ChatInput.tsx');
  const controls = output('packages/ui/src/components/chat/ModelControls.tsx');
  const mobile = output('packages/ui/src/components/chat/MobileAgentButton.tsx');
  assert.match(input, /currentAgentName = isTwinCatEmbedded\(\) \? twinCatProfile/);
  assert.match(controls, /if \(isTwinCatEmbedded\(\)\) return twinCatActiveProfile\(\)/);
  assert.match(controls, /agentName === TWINCAT_EDITOR_PROFILE && twinCatActiveProfile\(\) === TWINCAT_EDITOR_PROFILE/);
  assert.match(mobile, /const twinCatProfile = React\.useSyncExternalStore/);
  assert.match(mobile, /twinCatEmbedded \? twinCatProfile/);
});
