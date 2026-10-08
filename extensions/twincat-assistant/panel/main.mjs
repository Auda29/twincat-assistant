import { connectHost } from '@openchamber/sdk';
import { buildAttachRequest, buildViewModel, initialContextState } from './presentation.mjs';

const elements = {
  project: document.getElementById('project'),
  session: document.getElementById('session'),
  binding: document.getElementById('binding'),
  activity: document.getElementById('activity'),
  hostConnection: document.getElementById('host-connection'),
  engineering: document.getElementById('engineering'),
  nativeBridge: document.getElementById('native-bridge'),
  hostError: document.getElementById('host-error'),
  attach: document.getElementById('attach'),
  operation: document.getElementById('operation'),
};

let state = initialContextState();
let operationMessage = '';
let host = null;
let disposed = false;
let contextRevision = 0;
let pendingAttach = null;
const unsubscribers = [];
const contextChangedMessage = 'Context changed; reattach the current context.';

function text(element, value) {
  element.textContent = value ?? '';
}

function render() {
  const view = buildViewModel(state);
  text(elements.project, view.projectLabel);
  text(elements.session, view.sessionLabel);
  text(elements.binding, view.bindingState === 'verified' ? `Verified (${view.bindingKey})` : 'Unbound');
  text(elements.activity, view.sessionActivity);
  text(elements.hostConnection, view.hostConnectionMessage);
  text(elements.engineering, view.engineeringMessage);
  text(elements.nativeBridge, view.nativeBridgeMessage);
  text(elements.hostError, view.hostErrorMessage ? `Host error: ${view.hostErrorMessage}` : '');
  text(elements.operation, operationMessage);
  elements.attach.disabled = !view.canAttach || pendingAttach !== null;
}

function errorMessage(error) {
  return error instanceof Error && error.message ? error.message : String(error);
}

function setHostError(error) {
  if (disposed) return;
  state = { ...state, error: errorMessage(error) };
  operationMessage = '';
  render();
}

function updateContext(patch, { authoritative = false } = {}) {
  if (disposed) return;
  const before = buildViewModel(state);
  let next = { ...state, ...patch, error: null };
  const directoryChanged = Object.prototype.hasOwnProperty.call(patch, 'directory')
    && patch.directory !== state.directory;
  if (directoryChanged && !authoritative) {
    // Directory and session arrive on independent SDK channels. Never expose
    // a mixed directory/session pair while waiting for the fresh session.
    next = { ...next, session: null, lifecycle: null };
  }
  if (Object.prototype.hasOwnProperty.call(patch, 'session') && patch.session === null) {
    next = { ...next, lifecycle: null };
  }
  state = next;
  const after = buildViewModel(state);
  if (before.directory !== after.directory || before.session?.id !== after.session?.id) {
    contextRevision += 1;
  }
  operationMessage = '';
  render();
}

function teardown() {
  if (disposed) return;
  disposed = true;
  contextRevision += 1;
  pendingAttach = null;
  for (const unsubscribe of unsubscribers.splice(0)) unsubscribe();
  if (host) host.dispose();
}

function connect() {
  try {
    host = connectHost();
  } catch (error) {
    setHostError(error);
    return;
  }

  unsubscribers.push(host.onReady((context) => updateContext({
    directory: context.directory,
    session: context.session,
    lifecycle: null,
    connection: context.connection,
  }, { authoritative: true })));
  unsubscribers.push(host.onDirectory((directory) => updateContext({ directory })));
  unsubscribers.push(host.onSession((session) => updateContext({ session })));
  unsubscribers.push(host.onSessionLifecycle((lifecycle) => updateContext({ lifecycle })));
  unsubscribers.push(host.onConnection((connection) => updateContext({ connection })));
}

elements.attach.addEventListener('click', () => {
  if (!host || disposed || pendingAttach !== null) return;
  const view = buildViewModel(state);
  const request = buildAttachRequest(state);
  if (!request) {
    setHostError('Verified project and session context are required before attaching.');
    return;
  }
  pendingAttach = {
    directory: request.data.projectDirectory,
    sessionId: request.data.sessionId,
    bindingKey: view.bindingKey,
    contextRevision,
  };
  operationMessage = 'Attaching verified context…';
  render();
  const token = pendingAttach;
  void Promise.resolve().then(() => host.attach(request)).then(() => {
    finishAttach(token, null);
  }).catch((error) => {
    finishAttach(token, error);
  });
});

function finishAttach(token, error) {
  if (disposed || pendingAttach !== token) return;
  pendingAttach = null;
  const view = buildViewModel(state);
  const sameContext = view.directory === token.directory
    && view.session?.id === token.sessionId
    && view.bindingKey === token.bindingKey
    && contextRevision === token.contextRevision;
  if (!sameContext) {
    operationMessage = contextChangedMessage;
    render();
    return;
  }
  if (error) {
    setHostError(error);
    return;
  }
  operationMessage = 'Verified context attached.';
  render();
}

window.addEventListener('pagehide', teardown, { once: true });
window.addEventListener('unload', teardown, { once: true });
render();
connect();
