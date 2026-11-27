import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { transform } from 'esbuild';
import { z } from 'zod';
import { applySourceOverlay } from '../scripts/source-overlay.mjs';

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
    const setTwinCatSendPolicy = policy => { globalThis.__sendPolicy = policy; };
    const getChatDraftIdentityKey = stub, useGlobalSessionsStore = stub, resolveGlobalSessionDirectory = stub;
    const archiveSession = stub, unarchiveSession = stub, updateSessionTitle = stub, registerSessionDirectory = stub;
    const collectSessionTitleTurns = stub, generateSessionTitle = stub, generateAndSaveSessionTitle = stub;
    const runSessionTitleGeneration = stub, getRuntimeKey = stub, normalizePath = value => value;
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
  return { api: sandbox.module.exports, sendPolicy: () => sandbox.__sendPolicy,
    select(next) { state = next; for (const fn of subscribers) fn(state); },
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

test('view readiness masks Editor temporarily; closing the view revokes the grant', async () => {
  const { api } = await makeBridge();
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  approve(api);
  api.publishTwinCatView('a', 'C:/project', false);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  api.publishTwinCatView('a', 'C:/project', true);
  assert.equal(api.twinCatActiveProfile(), 'twincat-editor');
  const unpublish = api.publishTwinCatView('a', 'C:/project', true);
  unpublish();
  api.publishTwinCatView('a', 'C:/project', true);
  assert.equal(api.twinCatActiveProfile(), 'twincat-readonly');
  approve(api);
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
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context);
  api.requestTwinCatProfile('twincat-editor');
  const first = api.twinCatSource.peekProfileRequest('a');
  assert.equal(first.profile, 'twincat-editor');
  assert.equal(api.twinCatSource.peekProfileRequest('a').id, first.id);
  assert.equal(api.twinCatSource.peekProfileRequest('b'), null);
  assert.throws(api.requireTwinCatProfileSettled, { message: /Profilwechsel/, code: 'twincat_profile_switch_pending' });
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

  const cleanup = api.publishTwinCatView('a', 'C:/project', true);
  api.requestTwinCatProfile('twincat-editor');
  const closed = api.twinCatSource.peekProfileRequest('a');
  cleanup();
  api.publishTwinCatView('a', 'C:/project', true);
  assert.equal(api.twinCatProfilePending(), false);
  assert.equal(api.twinCatSource.peekProfileRequest('a'), null);
  assert.equal(api.twinCatSource.setEditorProfile({ sessionId: 'a', enabled: true, requestId: closed.id }), false);

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
  const { api, expire, sendPolicy } = await makeBridge();
  assert.equal(sendPolicy(), undefined);
  api.twinCatSource.activate();
  api.publishTwinCatView('a', 'C:/project', true);
  api.twinCatSource.setContext('a', context, 'exact-project');
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Analysis \(twincat-readonly\)/);
  approve(api);
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Editor \(twincat-editor\)/);
  approve(api, 'twincat-debug');
  assert.match(api.twinCatContextTexts('a')[0], /Active profile: Debug \(twincat-debug\)/);
  assert.equal(sendPolicy().profileForChat('a', 'C:/project'), 'twincat-debug');
  assert.equal(sendPolicy().profileForChat('a', 'C:/other'), 'twincat-readonly');
  assert.equal(sendPolicy().profileForChat('general', 'C:/project'), 'twincat-readonly');
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
  const review = output('packages/ui/src/lib/reviewFlow.ts');
  const queue = output('packages/ui/src/hooks/useQueuedMessageAutoSend.ts');
  assert.match(input, /currentAgentName = isTwinCatEmbedded\(\) \? twinCatProfile/);
  assert.match(controls, /if \(isTwinCatEmbedded\(\)\) return twinCatActiveProfile\(\)/);
  assert.match(controls, /agentName !== TWINCAT_ANALYSIS_PROFILE && agentName !== twinCatActiveProfile\(\)/);
  assert.match(mobile, /const twinCatProfile = React\.useSyncExternalStore/);
  assert.match(mobile, /twinCatEmbedded \? twinCatProfile/);
  assert.match(controls, /agent.name === TWINCAT_DEBUG_PROFILE \? 'Debug'/);
  assert.match(mobile, /twinCatProfile === TWINCAT_DEBUG_PROFILE \? 'Debug'/);
  assert.match(input, /if \(twinCatProfilePending\(\)\) return/);
  assert.match(input, /const handleQueueMessage = .*\n +if \(twinCatProfilePending\(\)\) return/);
  assert.match(input, /agent: isTwinCatEmbedded\(\) \? undefined : currentAgentName/);
  assert.match(queue, /twinCatProfilePending\(\) \|\| !isSelectedTwinCatChat\(sessionId, target.directory\)/);
  assert.match(dispatch, /selection.agent = sameChat \? twinCatActiveProfile\(\)/);
  assert.match(dispatch, /beforeOptimisticInsert: refreshTwinCatSelection/);
  assert.match(dispatch, /refreshTwinCatSelection\(\)[\s\S]*const commandContext/);
  assert.match(dispatch, /validateProfile,[\s\S]*text: params.content/);
  assert.match(client, /const prompt = .*\n +validateProfile\(\)/);
  assert.match(client, /policy\?\.requireSettled\(\)[\s\S]*const agent = policy \? policy.profileForChat\(/);
  assert.match(client, /policy\?\.contextTexts\(params.id, agent, policy.isSelectedChat/);
  assert.match(review, /beforeOptimisticInsert:[\s\S]*sendContext.unshift\(\.\.\.policy.contextTexts/);
  assert.match(client, /params.validateProfile\?\.\(\)\n +await call\("session.command"/);
});

test('queued prompts and slash commands wait for their selected chat and settled profile', { timeout: 10_000 }, async t => {
  const checkout = new URL('../local-data/upstream/openchamber/', import.meta.url);
  try {
    await readFile(new URL('.git/HEAD', checkout));
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return t.skip('Prepare the locked OpenChamber sources with node scripts/build-source-runtime.mjs --prepare');
  }
  const lock = JSON.parse(await readFile(new URL('../upstream/source-lock.json', import.meta.url), 'utf8'));
  const edits = JSON.parse(await readFile(new URL('../upstream/overlays/openchamber/changes.json', import.meta.url), 'utf8'));
  const load = async path => {
    const file = 'packages/ui/src/' + path;
    const original = execFileSync('git', ['show', lock.openchamber.revision + ':' + file],
      { cwd: fileURLToPath(checkout), encoding: 'utf8', windowsHide: true });
    return applySourceOverlay(original, edits.find(edit => edit.file === file));
  };
  const [store, queue, client] = await Promise.all([
    load('sync/session-ui-store.ts'), load('hooks/useQueuedMessageAutoSend.ts'), load('lib/opencode/client.ts'),
  ]);
  const section = (source, start, end) => {
    const from = source.indexOf(start);
    const to = source.indexOf(end, from + start.length);
    assert.ok(from >= 0 && to > from, start);
    return source.slice(from, to);
  };
  // Execute the overlay-applied hook, store action, router and client with
  // synchronous React scheduling and a fake transport at the SDK boundary.
  const { code } = await transform([
    section(store, 'export async function routeMessage(', 'type CapturedSendTarget'),
    'const { sendMessage } = {',
    section(store, '  sendMessage: async (', '  // createSession\n'),
    '}',
    'class TestClient {',
    section(client, '  private async applySendSelection(', '  /** Interrupts the running turn.'),
    '  assertRuntimeUnchanged() {}',
    '  async resolveSkillMentions() { return { attached: [], unresolved: [] } }',
    '  clientFor() { return transport }',
    '  async switchSessionAgent(_id, agent) { transport.agent = agent; await transport.selectionReady }',
    '}',
    'const opencodeClient = new TestClient()',
    queue.replace(/^import .*\n/gm, ''),
    'export { sendMessage }',
  ].join('\n'), { loader: 'ts', format: 'cjs', target: 'es2022' });

  for (const [content, interruption] of [
    ['queued prompt', 'chat changes during knowledge load'],
    ['/fixture queued command', 'chat changes during knowledge load'],
    ['queued prompt', 'chat changes during client setup'],
    ['/fixture queued command', 'chat changes during client setup'],
    ['queued prompt', 'profile becomes pending during client setup'],
    ['/fixture queued command', 'profile becomes pending during client setup'],
  ]) {
    await t.test(`${content}: ${interruption}`, { timeout: 2_000 }, async () => {
      const pauseKnowledge = interruption.endsWith('knowledge load');
      const switchChat = interruption.startsWith('chat changes');
      const { api, select, sendPolicy } = await makeBridge();
      const chatA = { currentSessionId: 'a', currentSessionDirectory: 'C:/project' };
      const chatB = { ...chatA, currentSessionId: 'b' };
      api.twinCatSource.activate();
      api.publishTwinCatView('a', chatA.currentSessionDirectory, true);
      api.twinCatSource.setContext('a', context);
      // Keep Analysis in both chats for the late selection race. Comparing
      // agent names alone would let the former chat receive the message.
      if (pauseKnowledge || !switchChat) approve(api);

      const target = { sessionId: 'a', directory: chatA.currentSessionDirectory, runtimeKey: 'runtime' };
      const queued = { id: 'follow-up', content, text: content, sendConfig: { providerID: 'provider', modelID: 'model' } };
      const queueState = { queuedMessages: { a: [queued] } };
      const hold = Promise.withResolvers();
      const paused = Promise.withResolvers();
      let finished = Promise.withResolvers();
      const received = [];
      const transport = {
        get selectionReady() {
          if (!pauseKnowledge) paused.resolve();
          return !pauseKnowledge ? hold.promise : undefined;
        },
        session: Object.fromEntries(['synthetic', 'prompt', 'command'].map(kind => [
          kind, payload => received.push({ kind, agent: transport.agent, ...payload }),
        ])),
      };
      const noop = () => {};
      const emptyStore = { getState: () => ({}) };
      const globals = {
        ...api, transport, getTwinCatSendPolicy: sendPolicy,
        module: { exports: {} }, console,
        Date, setTimeout, clearTimeout,
        get: () => currentChat,
        useSessionUIStore: Object.assign(selector => selector(currentChat), {
          getState: () => ({ sessionAbortFlags: new Map(), sendMessage: globals.module.exports.sendMessage }),
        }),
        useMessageQueueStore: Object.assign(selector => selector(queueState), {
          getState: () => ({
            getSendableQueue: () => queueState.queuedMessages.a,
            markSending: noop,
            clearSending: () => finished.resolve(),
            removeFromQueue: () => { queueState.queuedMessages = {}; },
          }),
        }),
        getMessageQueueKey: () => 'a', parseMessageQueueKey: () => target, getRuntimeKey: () => 'runtime',
        useAutoReviewStore: Object.assign(() => ({}), { getState: () => ({ isRunningForSession: () => false }) }),
        useDirectoryStore: () => target.directory,
        useDirectorySync: selector => selector({ message: {} }), useShallow: selector => selector,
        getDirectoryState: () => ({}), queuedContextToParts: () => [],
        createInputHistorySubmission: noop, createInputHistoryIdentity: noop,
        useConfigStore: emptyStore,
        useSelectionStore: { getState: () => ({
          getSessionAgentSelection: noop, saveSessionModelSelection: noop,
          saveSessionAgentSelection: noop, saveAgentModelVariantForSession: noop,
        }) },
        useSessionGoalArmStore: { getState: () => ({ consume: () => ({ armed: false }) }) },
        useViewportStore: emptyStore, getViewportSessionMemory: () => ({ lastUserMessageAt: 1 }),
        resolveVariantToRecord: noop, normalizePath: value => value,
        notifyMessageSent: noop, markPendingUserSendAnimation: noop,
        async fetchSessionKnowledge() {
          if (pauseKnowledge) {
            paused.resolve();
            await hold.promise;
          }
          return { text: '' };
        },
        resolveSendSelection: () => ({}),
        selectCommandsForDirectory: () => [{ name: 'fixture' }], selectSkillsForDirectory: () => [],
        useCommandsStore: emptyStore, useSkillsStore: emptyStore,
        optimisticSend: async input => {
          input.beforeOptimisticInsert?.();
          await input.send('prompt-id', input.context);
        },
        call: (_operation, run) => run(),
        ascendingId: () => 'context-id',
        assertProviderCircuitClosed: noop, recordProviderError: noop, recordProviderSuccess: noop,
      };
      let currentChat = chatA;
      let cursor = 0;
      const refs = [];
      globals.React = {
        useSyncExternalStore: (_subscribe, snapshot) => snapshot(),
        useMemo: factory => factory(),
        useRef: current => (refs[cursor++] ??= { current }),
        useState: initial => [initial, noop],
        useEffect: effect => effect(),
      };
      vm.runInNewContext(code, globals);
      const renderQueue = () => {
        cursor = 0;
        globals.module.exports.useQueuedMessageAutoSend(true);
      };

      renderQueue();
      await paused.promise;
      if (switchChat) {
        select(currentChat = chatB);
      } else {
        api.requestTwinCatProfile('twincat-debug');
      }
      renderQueue();
      hold.resolve();
      await finished.promise;
      assert.deepEqual(received, []);
      assert.equal(queueState.queuedMessages.a[0], queued);

      if (switchChat) {
        select(currentChat = chatA);
        api.publishTwinCatView('a', target.directory, true);
      }
      approve(api, 'twincat-debug');
      finished = Promise.withResolvers();
      renderQueue();
      await finished.promise;
      assert.deepEqual(received.map(record => record.kind), ['synthetic', content.startsWith('/') ? 'command' : 'prompt']);
      assert.ok(received.every(record => record.sessionID === 'a' && record.agent === 'twincat-debug'));
      assert.match(received[0].text, /Active profile: Debug[\s\S]*PLC project: project-key/);
      assert.deepEqual(queueState.queuedMessages, {});
    });
  }
});
