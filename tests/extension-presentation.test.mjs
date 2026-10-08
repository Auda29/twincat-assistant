import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildAttachRequest,
  buildViewModel,
  initialContextState,
} from '../extensions/twincat-assistant/panel/presentation.mjs';

test('missing project or session fails closed and offers no attach action', () => {
  const view = buildViewModel(initialContextState());

  assert.equal(view.projectLabel, 'No verified project context');
  assert.equal(view.sessionLabel, 'No active session');
  assert.equal(view.bindingState, 'unbound');
  assert.equal(view.engineeringState, 'blocked');
  assert.equal(view.engineeringMessage, 'Blocked: verified project and session context are required.');
  assert.equal(view.nativeBridgeMessage, 'Not connected: native TwinCAT bridge unavailable.');
  assert.equal(view.canAttach, false);
  assert.equal(buildAttachRequest(initialContextState()), null);
});

test('verified host context remains read-only and attachable without claiming IDE binding', () => {
  const state = {
    directory: 'C:\\TwinCAT\\Plant <Line>',
    session: { id: 'session-42', title: 'Review <candidate>', busy: false },
    lifecycle: { sessionId: 'session-42', phase: 'completed' },
    connection: { connected: true, account: 'host-user' },
  };
  const view = buildViewModel(state);

  assert.equal(view.projectLabel, state.directory);
  assert.equal(view.sessionLabel, state.session.title);
  assert.equal(view.bindingState, 'verified');
  assert.equal(view.engineeringState, 'blocked');
  assert.equal(view.engineeringMessage, 'Blocked: native TwinCAT bridge is not connected.');
  assert.equal(view.nativeBridgeMessage, 'Not connected: native TwinCAT bridge unavailable.');
  assert.equal(view.sessionActivity, 'Completed');
  assert.equal(view.canAttach, true);

  assert.deepEqual(buildAttachRequest(state), {
    providerId: 'twincat-assistant',
    id: 'twincat-context:session-42',
    title: 'TwinCAT context: Review <candidate>',
    url: 'about:blank',
    kind: 'issue',
    text: 'Verified OpenChamber context only.\nProject directory: C:\\TwinCAT\\Plant <Line>\nSession: Review <candidate> (session-42)\nNative TwinCAT bridge: not connected.',
    data: {
      projectDirectory: 'C:\\TwinCAT\\Plant <Line>',
      sessionId: 'session-42',
      nativeBridge: 'not-connected',
    },
  });
});

test('session switch rebinds only to the newest verified host context', () => {
  const view = buildViewModel({
    directory: '/projects/one',
    session: { id: 's-one', title: 'One', busy: true },
    lifecycle: { sessionId: 's-one', phase: 'started' },
  });
  const switched = buildViewModel({
    directory: '/projects/two',
    session: { id: 's-two', title: 'Two', busy: false },
    lifecycle: { sessionId: 's-two', phase: 'failure' },
  });

  assert.equal(view.bindingKey, '/projects/one::s-one');
  assert.equal(view.sessionActivity, 'Running');
  assert.equal(switched.bindingKey, '/projects/two::s-two');
  assert.equal(switched.sessionActivity, 'Failed');
  assert.notDeepEqual(
    buildAttachRequest({ directory: view.directory, session: view.session }),
    buildAttachRequest({ directory: switched.directory, session: switched.session }),
  );
});

test('bridge and host errors are explicit without becoming readiness', () => {
  const view = buildViewModel({
    directory: '/projects/one',
    session: { id: 's-one', title: 'One', busy: false },
    error: 'Host unavailable',
  });

  assert.equal(view.hostErrorMessage, 'Host unavailable');
  assert.equal(view.engineeringState, 'blocked');
  assert.equal(view.canAttach, false);
});
