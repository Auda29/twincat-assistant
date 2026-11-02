import assert from 'node:assert/strict';
import test from 'node:test';
import { allowedProviders, createChatAdapter, consentProviders, mcpStates, storedProjectSessions } from '../src/embedded-chat.mjs';

test('native MCP status discloses only reviewed server names and connection states', () => {
  assert.deepEqual(mcpStates([{ name:'knowledge', status:{status:'connected'} },
    { name:'engineering', status:{status:'failed',error:'private connection detail'} },
    { name:'unreviewed', status:{status:'connected'} }]),
  [{name:'knowledge',status:'connected'},{name:'engineering',status:'failed'}]);
  assert.deepEqual(mcpStates(null),[]);
  assert.deepEqual(mcpStates([{ name: 'knowledge', status: { status: 'new-runtime-state' } }]), [{ name: 'knowledge', status: 'unknown' }]);
});

function store(initial) {
  let value = initial;
  const listeners = new Set();
  return { getState: () => value, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    set(patch) { value = { ...value, ...patch }; for (const fn of listeners) fn(value); } };
}
function fixture(options = {}) {
  const ui = store({});
  for (const key of ['setSidebarOpen', 'setDraftStartersVisible', 'setShowOpenCodeUpdateNotifications', 'setContextRailHiddenSurfaces', 'toggleSidebar']) {
    ui.getState()[key] = value => ui.set({ [key + 'Value']: value, ...(key === 'setSidebarOpen' ? { isSidebarOpen: value } : {}) });
  }
  const sessions = store({ currentSessionId: 'session-one', currentSessionDirectory: '/isolated' });
  sessions.getState().createSession = async () => { sessions.set({ currentSessionId: 'session-two' }); return { id: 'session-two' }; };
  const input = store({ pendingGuestIssue: null });
  input.getState().setPendingGuestIssue = value => input.set({ pendingGuestIssue: value });
  return { ui, sessions, input, adapter: createChatAdapter({ ui, sessions, input, documentId: 'fixture-document', ...options }) };
}
const item = { providerId: 'twincat-assistant', id: 'snapshot-one', title: 'FB_Motor / PLC_A', url: 'http://127.0.0.1:3123/', text: 'synthetic ST snapshot' };

test('consent choices retain connected providers hidden by policy and expose no account data', () => {
  const connected = id => ({ id, name: id, connections: [{ type: 'credential', id: 'private-account', label: 'private-user' }], methods: ['private-method'] });
  const choices = consentProviders([connected('openai'), connected('mcp_private'), connected('search'), { id: 'not-connected', connections: [] }], [{ id: 'search' }]);
  assert.deepEqual(choices, [{ id: 'openai', name: 'openai' }]);
  assert.ok(!JSON.stringify(choices).includes('private'));
});

test('composer removal clears staged state while successful submission is a separate observation', () => {
  let references = [], submitted = [];
  const contexts = [];
  const source = { composer: () => ({ references, submitted }), setContext: (...args) => contexts.push(args) };
  const { adapter, input, sessions } = fixture({ source });
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit({ ...expected, context: { project: 'PLC_A', object: 'FB_Motor', treePath: 'POUs^FB_Motor', sourceKind: 'saved snapshot' } });
  assert.equal(contexts.at(-1)[1].object, 'FB_Motor');
  adapter.attach({ ...expected, item: { ...item, data: { revision: 'one' } } });
  assert.equal(adapter.state().attachmentState, 'staging');
  input.getState().setPendingGuestIssue(null);
  references = [{ id: item.id, title: item.title, revision: 'one' }];
  assert.equal(adapter.state().attachmentState, 'staged');
  references.push({ id: 'other', title: 'FB_Other / PLC_A', revision: 'two' });
  assert.equal(adapter.state().attachedRevision, 'one', 'the active POU is resolved among several staged references');
  references = [];
  assert.equal(adapter.state().attachedRevision, null, 'removing the actual composer chip removes the header state');
  assert.equal(adapter.state().attachmentState, 'none');
  submitted = [{ id: item.id, title: item.title, revision: 'one' }];
  assert.equal(adapter.state().attachmentState, 'submitted');
  assert.equal(adapter.state().attachedRevision, null, 'sent source is not a pending attachment');
  assert.equal(adapter.state().submittedRevision, 'one');
  sessions.set({ currentSessionId: 'session-two' });
  assert.deepEqual(contexts.at(-1), ['', null], 'context metadata is revoked on session switch');
});

test('stored history is authoritative even before the UI cache loads, and errors remain errors', async () => {
  const { adapter, sessions } = fixture({ loadSessions: async () => [{ id: 'persisted', updated: 23 }] });
  sessions.getState().getSessionsByDirectory = () => [];
  assert.deepEqual(adapter.recentSessions('/isolated'), []);
  assert.deepEqual(await adapter.storedSessions('/isolated'), [{ id: 'persisted', updated: 23, directory: '/isolated' }]);
  const failed = fixture({ loadSessions: async () => { throw new Error('runtime unavailable'); } }).adapter;
  await assert.rejects(failed.storedSessions('/isolated'), /unavailable/);
});

test('project history management is limited to reviewed metadata and the unchanged native selection', async () => {
  const calls = [];
  const source = { setContext() {}, composer: () => ({ references: [], submitted: [] }), async manageSession(request, assertCurrent) { assertCurrent(); calls.push(request); return true; } };
  const { adapter, sessions } = fixture({ source, loadSessions: async () => [{ id: 'older', updated: 7, title: 'Alter Chat', archived: false, busy: true }] });
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'PLC_A' };
  adapter.permit(expected);
  assert.equal(adapter.state().canManage, true);
  assert.deepEqual(await adapter.storedSessions('/isolated', true), [{ id: 'older', updated: 7, title: 'Alter Chat', archived: false, busy: true, directory: '/isolated' }]);
  await adapter.manageSession(expected, { id: 'older', directory: '/isolated', action: 'rename', title: ' Neuer Titel ', unreviewed: 'ignored' });
  assert.deepEqual(calls, [{ id: 'older', directory: '/isolated', action: 'rename', title: 'Neuer Titel' }]);
  for (const request of [
    { id: 'foreign', directory: '/isolated', action: 'archive' },
    { id: 'older', directory: '/another-project', action: 'archive' },
    { id: 'older', directory: '/isolated', action: 'delete' },
    { id: 'older', directory: '/isolated', action: 'rename', title: '\n' },
  ]) await assert.rejects(adapter.manageSession(expected, request));
  sessions.set({ currentSessionId: 'session-two' });
  await assert.rejects(adapter.manageSession(expected, { id: 'older', directory: '/isolated', action: 'archive' }), /changed/);
  assert.equal(calls.length, 1);
});

test('pending manual context stays with its chat, while automatic context never transfers', () => {
  const { adapter, sessions, input, ui } = fixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'PLC_A' };
  adapter.permit(expected); adapter.attach({ ...expected, item });
  sessions.set({ currentSessionId: 'session-two' });
  assert.equal(input.getState().pendingGuestIssue, null);
  sessions.set({ currentSessionId: 'session-one' });
  assert.equal(input.getState().pendingGuestIssue.text, item.text);
  input.getState().setPendingGuestIssue({ ...item, id: 'Aktiver Baustein', data: { mode: 'following' } });
  sessions.set({ currentSessionId: 'session-two' }); sessions.set({ currentSessionId: 'session-one' });
  assert.equal(input.getState().pendingGuestIssue, null);
  ui.set({ isSidebarOpen: true });
  assert.equal(ui.getState().setSidebarOpenValue, false, 'legacy navigation cannot reopen the generic sidebar');
});

test('generated chat titles require an explicit action on a reviewed idle conversation', async () => {
  const calls = [];
  const source = { setContext() {}, async manageSession(request, assertCurrent) { assertCurrent(); calls.push(request); return true; } };
  const { adapter } = fixture({ source, loadSessions: async () => [
    { id: 'older', title: 'PLC_A', updated: 1, archived: false, busy: false },
    { id: 'busy', updated: 2, busy: true }, { id: 'archived', updated: 3, archived: true },
  ] });
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'PLC_A' };
  adapter.permit(expected);
  await assert.rejects(adapter.manageSession(expected, { id: 'older', directory: '/isolated', action: 'generate-title' }));
  await adapter.storedSessions('/isolated', true);
  assert.equal(calls.length, 0, 'loading history never calls a model');
  await adapter.manageSession(expected, { id: 'older', directory: '/isolated', action: 'generate-title', title: 'untrusted', prompt: 'ignored' });
  assert.deepEqual(calls, [{ id: 'older', directory: '/isolated', action: 'generate-title' }]);
  for (const id of ['busy', 'archived']) await assert.rejects(adapter.manageSession(expected, { id, directory: '/isolated', action: 'generate-title' }));
  assert.equal(calls.length, 1);
});

test('late history from a previous project cannot authorize management in the next project', async () => {
  let finish;
  let mutations = 0;
  const source = { setContext() {}, manageSession: async () => { mutations++; } };
  const { adapter } = fixture({ source, loadSessions: () => new Promise(resolve => { finish = resolve; }) });
  const before = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'PLC_A' };
  adapter.permit(before);
  const loading = adapter.storedSessions('/PLC_A');
  const after = { ...before, contextKey: 'PLC_B' };
  adapter.permit(after);
  finish([{ id: 'old', updated: 5 }]); await loading;
  await assert.rejects(adapter.manageSession(after, { id: 'old', directory: '/PLC_A', action: 'archive' }));
  assert.equal(mutations, 0);
});

test('project chat creation preserves the original automatic title path', async () => {
  const { adapter, sessions } = fixture();
  let title;
  sessions.getState().createSession = async value => { title = value; return { id: 'new' }; };
  await adapter.newChat({ title: null, directory: '/isolated' });
  assert.equal(title, undefined);
});

test('source auto-chip retains POU identity through replacement and submission with identical titles', () => {
  let references = [], submitted = [];
  const source = { composer: () => ({ references, submitted }), setContext() {} };
  const { adapter, input } = fixture({ source });
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  const context = reference => ({ project: 'PLC_A', object: 'FB_Motor', treePath: reference, sourceKind: 'snapshot', reference });
  const auto = (reference, revision) => {
    adapter.permit({ ...expected, context: context(reference) });
    adapter.attach({ ...expected, item: { ...item, id: 'Aktiver Baustein', data: { revision, reference } } });
    assert.equal(adapter.state().attachmentState, 'staging');
    references = [{ id: 'Aktiver Baustein', title: item.title, revision, reference }];
    input.getState().setPendingGuestIssue(null);
  };
  auto('folder-a/FB_Motor', 'revision-a');
  assert.equal(adapter.state().attachmentState, 'staged');
  submitted = [...references];
  references = [];
  assert.equal(adapter.state().attachmentState, 'submitted');
  auto('folder-b/FB_Motor', 'revision-b');
  assert.equal(adapter.state().attachments.length, 1);
  assert.equal(adapter.state().attachedRevision, 'revision-b');
  assert.equal(adapter.state().submittedRevision, null, 'same-title submission from another POU is not current');
  assert.equal(adapter.state().submittedAttachments[0].reference, 'folder-a/FB_Motor');
  references = [];
  assert.equal(adapter.state().attachmentState, 'none', 'removed chips are not retained by the adapter registry');
});

test('all historical project locations participate in restoration in stored update order', async () => {
  const { adapter } = fixture({ loadSessions: async directory => [{ id: directory.endsWith('old') ? 'latest' : 'earlier', updated: directory.endsWith('old') ? 100 : 20 }] });
  assert.deepEqual(await adapter.storedSessions(['/project/current', '/project/old']), [
    { id: 'latest', updated: 100, directory: '/project/old' },
    { id: 'earlier', updated: 20, directory: '/project/current' },
  ]);
});

test('stored session pagination reads every page without starting directory-scoped MCP locations', async () => {
  const requests = [];
  const list = await storedProjectSessions(async (url, options) => {
    requests.push({ url, options });
    return Response.json(requests.length === 1
      ? { data: [{ id: 'one', time: { updated: 7 } }], cursor: { next: 'second' } }
      : { data: [{ id: 'archived', time: { archived: 1 } }, { id: 'two', time: { updated: 5 } }], cursor: {} });
  }, 'C:/isolated/project');
  assert.deepEqual(list, [{ id: 'one', updated: 7 }, { id: 'two', updated: 5 }]);
  assert.ok(requests.every(request => request.url.startsWith('/api/session?') && !request.options.headers));
  assert.equal(new URL('http://127.0.0.1' + requests[1].url).searchParams.get('cursor'), 'second');
  await assert.rejects(storedProjectSessions(async () => new Response('', { status: 503 }), '/isolated'), /unavailable/);
});

test('embedded adapter stages explicit context only for the exact permitted selection and never prompts', () => {
  const { adapter, input, ui } = fixture();
  assert.equal(input.getState().pendingGuestIssue, null);
  assert.equal(ui.getState().setSidebarOpenValue, false);
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'revision-one' };
  assert.throws(() => adapter.attach({ ...expected, item }), /binding/);
  assert.equal(adapter.permit(expected), true);
  assert.deepEqual(adapter.attach({ ...expected, item }), { sessionId: 'session-one', epoch: 0, attachedObject: item.title });
  assert.deepEqual(input.getState().pendingGuestIssue, item);
});

test('session changes including away-and-back invalidate pending attachments and previous consent', () => {
  const { adapter, input, sessions } = fixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit(expected); adapter.attach({ ...expected, item });
  sessions.set({ currentSessionId: 'session-two' });
  assert.equal(input.getState().pendingGuestIssue, null);
  sessions.set({ currentSessionId: 'session-one' });
  assert.equal(adapter.state().epoch, 2);
  assert.equal(adapter.permit(expected), false);
  assert.throws(() => adapter.attach({ ...expected, item }), /binding/);
});

test('wrong revision, oversized source and an occupied composer queue fail without partial transfer', () => {
  const { adapter, input } = fixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit(expected);
  assert.throws(() => adapter.attach({ ...expected, contextKey: 'two', item }), /binding/);
  assert.throws(() => adapter.attach({ ...expected, documentId: 'another-document', item }), /binding/);
  assert.throws(() => adapter.attach({ ...expected, item: { ...item, text: 'x'.repeat(16001) } }), /Invalid/);
  assert.equal(input.getState().pendingGuestIssue, null);
  input.getState().setPendingGuestIssue({ providerId: 'another-extension' });
  assert.throws(() => adapter.attach({ ...expected, item }), /pending/);
  adapter.dispose();
  assert.equal(input.getState().pendingGuestIssue.providerId, 'another-extension');
});

test('new project chat uses upstream session creation without submitting any source', async () => {
  const { adapter, input } = fixture();
  const state = await adapter.newChat({ title: 'PLC_A / FB_Motor', directory: '/isolated' });
  assert.equal(state.sessionId, 'session-two');
  assert.equal(input.getState().pendingGuestIssue, null);
});

test('effective policy readback exposes only explicitly allowed provider IDs', () => {
  const entries = [{ info: { experimental: { policies: [{ action: 'provider.use', resource: '*', effect: 'deny' },
    { action: 'provider.use', resource: 'openai', effect: 'allow' }, { action: 'provider.use', resource: 'bad id', effect: 'allow' },
    { action: '*', resource: '*', effect: 'deny' }] }, provider: { openai: { options: { apiKey: 'private-key' } } } } }, { info: {} }];
  assert.deepEqual(allowedProviders(entries), ['openai']);
  assert.deepEqual(allowedProviders(null), []);
});

test('native host can reopen the newest known conversation by metadata only', () => {
  const { adapter, sessions, input } = fixture();
  let requested;
  sessions.getState().getSessionsByDirectory = directory => {
    requested = directory;
    return [{ id: 'old', time: { updated: 1 }, title: 'private title' }, { id: 'bad id!', time: { updated: 9 } }, { id: 'new', time: { updated: 5 } }];
  };
  sessions.getState().setCurrentSession = id => sessions.set({ currentSessionId: id });
  assert.deepEqual(adapter.recentSessions(String.raw`C:\state\workspace`), [{ id: 'new', updated: 5 }, { id: 'old', updated: 1 }]);
  assert.equal(requested, 'C:/state/workspace');
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit(expected); adapter.attach({ ...expected, item });
  assert.equal(adapter.selectSession('not valid!'), false);
  assert.equal(adapter.selectSession('old'), true);
  assert.equal(adapter.state().sessionId, 'old');
  assert.equal(input.getState().pendingGuestIssue, null, 'switching conversations drops the staged native reference');
  assert.equal(adapter.permit(expected), false);
});

// The composer consumes the staged reference into its own chip list.
function composerFixture() {
  const chips = [];
  const composer = { chips: () => [...chips], remove(id) { const at = chips.indexOf(id); if (at < 0) return false; chips.splice(at, 1); return true; } };
  const parts = fixture();
  const adapter = createChatAdapter({ ui: parts.ui, sessions: parts.sessions, input: parts.input, composer, documentId: 'fixture-document' });
  const consume = () => {
    const staged = parts.input.getState().pendingGuestIssue;
    parts.input.getState().setPendingGuestIssue(null);
    if (staged && !chips.includes(staged.id)) chips.push(staged.id);
  };
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit(expected);
  const stage = (id, revision, auto = false) => {
    adapter.attach({ ...expected, auto, item: { ...item, id, title: id + ' / PLC_A', data: { revision } } });
    consume();
  };
  return { ...parts, adapter, chips, stage };
}

test('a chip removed from the composer no longer counts as attached', () => {
  const { adapter, chips, stage } = composerFixture();
  stage('FB_A', 'rev-a');
  assert.equal(adapter.state().attachedRevision, 'rev-a');
  assert.deepEqual(adapter.state().attachments, [{ id: 'FB_A', title: 'FB_A / PLC_A', revision: 'rev-a', reference: 'FB_A', mode: 'manual' }]);
  chips.splice(0, 1);
  assert.equal(adapter.state().attachedRevision, null);
  assert.deepEqual(adapter.state().attachments, []);
  stage('FB_A', 'rev-a');
  assert.equal(adapter.state().attachedRevision, 'rev-a', 'the same revision can be attached again');
});

test('a staged reference counts as attached before the composer shows its chip', () => {
  const { adapter, input } = composerFixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.attach({ ...expected, item: { ...item, id: 'FB_A', data: { revision: 'rev-a' } } });
  assert.equal(input.getState().pendingGuestIssue.id, 'FB_A');
  assert.equal(adapter.state().attachedRevision, 'rev-a');
});

test('automatic attachment reuses one chip slot so a POU switch replaces it in place', () => {
  const { adapter, chips, stage, input } = composerFixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  const auto = (reference, revision) => {
    adapter.attach({ ...expected, auto: true, item: { ...item, id: 'Aktiver Baustein', title: reference + ' / PLC_A', data: { revision, reference } } });
    const staged = input.getState().pendingGuestIssue;
    input.getState().setPendingGuestIssue(null);
    if (!chips.includes(staged.id)) chips.push(staged.id);
  };
  stage('FB_Manual', 'rev-m');
  auto('FB_A@PLC_A', 'rev-a');
  auto('FB_B@PLC_A', 'rev-b');
  assert.deepEqual(chips, ['FB_Manual', 'Aktiver Baustein']);
  assert.deepEqual(adapter.state().attachments.map(value => value.reference), ['FB_Manual', 'FB_B@PLC_A']);
  assert.equal(adapter.state().attachedObject, 'FB_B@PLC_A / PLC_A');
});

test('a newer own reference replaces one the composer has not consumed yet', () => {
  const { adapter, input } = composerFixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.attach({ ...expected, auto: true, item: { ...item, id: 'Aktiver Baustein', data: { revision: 'rev-a', reference: 'FB_A' } } });
  adapter.attach({ ...expected, auto: true, item: { ...item, id: 'Aktiver Baustein', data: { revision: 'rev-b', reference: 'FB_B' } } });
  assert.equal(input.getState().pendingGuestIssue.data.reference, 'FB_B');
  assert.deepEqual(adapter.state().attachments.map(value => value.revision), ['rev-b']);
});

test('switching conversations forgets staged chips', () => {
  const { adapter, sessions, stage } = composerFixture();
  stage('FB_A', 'rev-a');
  sessions.set({ currentSessionId: 'session-two' });
  assert.deepEqual(adapter.state().attachments, []);
  assert.equal(adapter.state().attachedObject, null);
});

test('operational adapter readiness precedes history presentation and MCP observations remain explicit', () => {
  let presentationReady = false;
  let observation = { directory: '/isolated', observed: true, stale: false };
  const { adapter } = fixture({ source: { presentationReady: () => presentationReady, composer: () => ({ references: [] }) },
    mcp: () => [{ name: 'knowledge', status: 'connected' }], mcpObservation: () => observation });
  assert.equal(adapter.state().ready, true);
  assert.equal(adapter.state().presentationReady, false);
  presentationReady = true;
  assert.equal(adapter.state().presentationReady, true);
  observation = { ...observation, stale: true };
  assert.equal(adapter.state().mcp[0].status, 'connected', 'last confirmed catalog is retained');
  assert.equal(adapter.state().mcpObservation.stale, true, 'retained catalog is not represented as a fresh read');
});

test('automatic staging cannot overwrite an unconsumed manual item and can resume once consumed', () => {
  const { adapter, input } = fixture();
  const expected = { sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one' };
  adapter.permit(expected);
  adapter.attach({ ...expected, item: { ...item, data: { mode: 'manual' } } });
  const manual = input.getState().pendingGuestIssue;
  const auto = { ...expected, auto: true, item: { ...item, id: 'Aktiver Baustein', data: { mode: 'following' } } };
  assert.equal(adapter.state().manualAttachmentPending, true);
  assert.throws(() => adapter.attach(auto), /manual attachment is still pending/);
  assert.equal(input.getState().pendingGuestIssue, manual);
  adapter.clearFollowing();
  assert.equal(input.getState().pendingGuestIssue, manual, 'disabling following preserves pending manual source');
  input.getState().setPendingGuestIssue(null);
  adapter.attach(auto);
  assert.equal(adapter.state().manualAttachmentPending, false);
  assert.equal(adapter.state().attachments.at(-1).mode, 'following');
});

test('losing active context clears following references through the original composer without removing manual snapshots', () => {
  let references = [{ id: 'manual', title: 'Kept', revision: 'old', mode: 'manual' },
    { id: 'Aktiver Baustein', title: 'Current', revision: 'new', mode: 'following' }];
  const source = { composer: () => ({ references }), setContext() {}, clearFollowing() { references = references.filter(item => item.mode !== 'following'); } };
  const { adapter } = fixture({ source });
  adapter.permit({ sessionId: 'session-one', epoch: 0, documentId: 'fixture-document', contextKey: 'one',
    context: { project: 'PLC_A', object: null, treePath: null, sourceKind: null, reference: null } });
  assert.deepEqual(adapter.state().attachments.map(item => item.id), ['manual']);
});
