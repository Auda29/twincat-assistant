import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { transform } from 'esbuild';

async function makeBridge() {
  const source = await readFile(new URL('../upstream/overlays/openchamber/twincat.ts', import.meta.url), 'utf8');
  const stripped = source.replace(/^import .*;\r?\n/gm, '');
  const prelude = `
    const stub = new Proxy(function () { return stub }, { get() { return stub }, apply() { return stub } });
    const React = stub, z = stub;
    const useSessionUIStore = globalThis.__sessionStore;
    const useUIStore = stub, useConfigStore = stub, useInputStore = stub, useI18nStore = stub;
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
  const sandbox = { module: { exports: {} }, exports: {}, __sessionStore: store };
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
  assert.deepEqual(names(api.twinCatSelectableAgents(agents)), ['twincat-readonly', 'twincat-editor']);
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
