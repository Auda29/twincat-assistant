const nativeBridgeMessage = 'Not connected';

function readDirectory(state) {
  const directory = state?.directory ?? state?.projectDirectory ?? null;
  return typeof directory === 'string' && directory.trim() ? directory : null;
}

function readSession(state) {
  const session = state?.session;
  if (!session || typeof session !== 'object') return null;
  if (typeof session.id !== 'string' || !session.id.trim()) return null;
  return {
    id: session.id,
    title: typeof session.title === 'string' && session.title.trim() ? session.title : session.id,
    busy: session.busy === true,
  };
}

function readLifecycle(state, session) {
  const lifecycle = state?.lifecycle;
  if (lifecycle && lifecycle.sessionId === session?.id && typeof lifecycle.phase === 'string') {
    return lifecycle.phase;
  }
  return session?.busy ? 'started' : null;
}

function activityLabel(phase) {
  if (phase === 'started') return 'Running';
  if (phase === 'completed') return 'Completed';
  if (phase === 'failure') return 'Failed';
  return 'Unknown';
}

const themeVariables = {
  background: '--oc-background',
  foreground: '--oc-foreground',
  muted: '--oc-muted',
  border: '--oc-border',
  primary: '--oc-primary',
  primaryForeground: '--oc-primary-foreground',
  hover: '--oc-hover',
  focus: '--oc-focus',
  warningText: '--oc-warning-text',
  errorText: '--oc-error-text',
  font: '--oc-font',
  mono: '--oc-mono',
  radius: '--oc-radius',
};

// Maps host theme tokens to CSS custom properties. Unknown, non-string or
// oversized values are dropped so the stylesheet fallbacks stay in effect.
export function themeProperties(theme) {
  const tokens = theme?.tokens;
  const properties = {};
  if (!tokens || typeof tokens !== 'object') return properties;
  for (const [token, variable] of Object.entries(themeVariables)) {
    const value = tokens[token];
    if (typeof value === 'string' && value.trim() && value.length <= 200) properties[variable] = value;
  }
  return properties;
}

export function initialContextState() {
  return {
    directory: null,
    session: null,
    lifecycle: null,
    connection: null,
    error: null,
  };
}

export function buildViewModel(state = initialContextState()) {
  const directory = readDirectory(state);
  const session = readSession(state);
  const phase = readLifecycle(state, session);
  const verified = directory !== null && session !== null;
  const hostError = typeof state.error === 'string' && state.error.trim() ? state.error : null;

  return {
    directory,
    session,
    projectLabel: directory ?? 'No verified project context',
    sessionLabel: session?.title ?? 'No active session',
    bindingState: verified ? 'verified' : 'unbound',
    bindingKey: verified ? `${directory}::${session.id}` : null,
    engineeringState: 'blocked',
    engineeringMessage: verified
      ? 'Blocked until the native bridge connects'
      : 'Blocked: project and session context required',
    nativeBridgeMessage,
    hostConnectionMessage: state.connection?.connected === true
      ? 'OpenChamber host: connected.'
      : 'OpenChamber host: not confirmed.',
    hostErrorMessage: hostError,
    sessionActivity: activityLabel(phase),
    canAttach: verified && hostError === null,
  };
}

export function buildAttachRequest(state) {
  const view = buildViewModel(state);
  if (!view.canAttach || !view.directory || !view.session) return null;

  return {
    providerId: 'twincat-assistant',
    id: `twincat-context:${view.session.id}`,
    title: `TwinCAT context: ${view.session.title}`,
    url: 'about:blank',
    kind: 'issue',
    text: [
      'Verified OpenChamber context only.',
      `Project directory: ${view.directory}`,
      `Session: ${view.session.title} (${view.session.id})`,
      'Native TwinCAT bridge: not connected.',
    ].join('\n'),
    data: {
      projectDirectory: view.directory,
      sessionId: view.session.id,
      nativeBridge: 'not-connected',
    },
  };
}
