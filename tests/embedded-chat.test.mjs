import assert from 'node:assert/strict';
import test from 'node:test';
import { createChatAdapter } from '../src/embedded-chat.mjs';

function store(initial) {
  let value = initial;
  const listeners = new Set();
  return { getState: () => value, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    set(patch) { value = { ...value, ...patch }; for (const fn of listeners) fn(value); } };
}
function fixture() {
  const ui = store({});
  for (const key of ['setSidebarOpen', 'setDraftStartersVisible', 'setShowOpenCodeUpdateNotifications', 'setContextRailHiddenSurfaces', 'toggleSidebar']) {
    ui.getState()[key] = value => ui.set({ [key + 'Value']: value });
  }
  const sessions = store({ currentSessionId: 'session-one', currentSessionDirectory: '/isolated' });
  sessions.getState().createSession = async () => { sessions.set({ currentSessionId: 'session-two' }); return { id: 'session-two' }; };
  const input = store({ pendingGuestIssue: null });
  input.getState().setPendingGuestIssue = value => input.set({ pendingGuestIssue: value });
  return { ui, sessions, input, adapter: createChatAdapter({ ui, sessions, input, documentId: 'fixture-document' }) };
}
const item = { providerId: 'twincat-assistant', id: 'snapshot-one', title: 'FB_Motor / PLC_A', url: 'http://127.0.0.1:3123/', text: 'synthetic ST snapshot' };

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
