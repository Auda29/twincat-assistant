// Version-specific adapter for the *existing* OpenChamber chat. These exports
// were traced to the source at v2.1.1 (e302062e3be0686986594fddabdafd8a97c129e5).
// No upstream file is patched and no chat, model client or agent is duplicated.
export const EMBEDDED_ASSETS = Object.freeze([
  { path: '/assets/index-rxO-w0yQ.js', sha256: 'd7b60bc7722a484b53e89ac18e76d3f6d172fa1efc09cb7ad784af7ce96e28b8' },
  { path: '/assets/useAppFontEffects-DC1U1Z6i.js', sha256: '1a29973e760c994a97a8a64d29c24bdcfbc4f358d9c29919f07efeac0e40ff63' },
]);

export function createChatAdapter({ ui, sessions, input, config, reloadPolicy, permissionChanged = () => {}, authenticated = () => true, documentId = crypto.randomUUID() }) {
  for (const store of [ui, sessions, input]) {
    if (typeof store?.getState !== 'function' || typeof store?.subscribe !== 'function') throw new Error('Incompatible chat store');
  }
  if (typeof sessions.getState().createSession !== 'function' || typeof input.getState().setPendingGuestIssue !== 'function') {
    throw new Error('Incompatible chat actions');
  }
  let epoch = 0;
  let selected = sessions.getState().currentSessionId;
  let permission = null;
  let attachment = null;
  let creating = false;
  const clearPending = () => {
    if (input.getState().pendingGuestIssue?.providerId === 'twincat-assistant') input.getState().setPendingGuestIssue(null);
  };
  const unsubscribe = sessions.subscribe(state => {
    if (selected === state.currentSessionId) return;
    selected = state.currentSessionId;
    epoch++;
    permission = null;
    permissionChanged(false);
    attachment = null;
    clearPending();
  });
  ui.getState().setSidebarOpen(false);
  ui.getState().setDraftStartersVisible(false);
  ui.getState().setShowOpenCodeUpdateNotifications(false);
  ui.getState().setContextRailHiddenSurfaces(['git', 'pr', 'diff', 'walkthrough', 'linear', 'editor', 'terminal', 'notes', 'plan', 'browser', 'chat']);

  const state = () => {
    const value = sessions.getState();
    const model = config?.getState();
    return { version: 1, ready: true, authenticated: authenticated(), documentId, sessionId: value.currentSessionId ?? null,
      directory: value.currentSessionDirectory ?? null, epoch, creating,
      model: model?.currentProviderId && model?.currentModelId ? model.currentProviderId + '/' + model.currentModelId : null,
      attachedObject: attachment?.title ?? null, attachedRevision: attachment?.revision ?? null };
  };
  const matches = expected => expected && expected.documentId === documentId && expected.sessionId && expected.sessionId === selected && expected.epoch === epoch;
  return Object.freeze({
    state,
    permit(expected) { permission = matches(expected) ? { ...expected } : null; if (!permission) clearPending(); permissionChanged(!!permission); return !!permission; },
    attach(request) {
      if (!matches(request) || !matches(permission) || request.contextKey !== permission.contextKey) throw new Error('Chat binding changed');
      const item = request.item;
      if (!item || item.providerId !== 'twincat-assistant' || typeof item.text !== 'string' || item.text.length > 16000
          || typeof item.title !== 'string' || item.title.length > 200 || typeof item.id !== 'string' || item.id.length > 128) {
        throw new Error('Invalid POU attachment');
      }
      if (input.getState().pendingGuestIssue) throw new Error('Another attachment is still pending');
      // The same queue and reference type used by SDK host.attach. It stages a
      // visible composer chip. It never submits a prompt or invokes a model.
      input.getState().setPendingGuestIssue(item);
      attachment = { title: item.title, revision: item.data?.revision ?? null };
      return { sessionId: selected, epoch, attachedObject: item.title };
    },
    async newChat({ title, directory }) {
      if (creating || typeof title !== 'string' || title.length > 200 || typeof directory !== 'string' || !directory) throw new Error('Invalid new chat');
      creating = true;
      try {
        const result = await sessions.getState().createSession(title, directory);
        if (!result?.id) throw new Error('Chat creation failed');
        return state();
      } finally { creating = false; }
    },
    toggleChats() { ui.getState().toggleSidebar(); },
    openSettings() { ui.getState().setSettingsPage('providers'); ui.getState().setSettingsDialogOpen(true); },
    async refreshPolicy(expected, model) {
      if (!matches(expected) || typeof reloadPolicy !== 'function') throw new Error('Chat selection changed');
      await reloadPolicy(sessions.getState().currentSessionDirectory, model);
      if (!matches(expected)) throw new Error('Chat selection changed');
      return true;
    },
    dispose() { unsubscribe(); clearPending(); permission = null; permissionChanged(false); },
  });
}

export async function installEmbeddedChat(scope = window) {
  if (scope.__twinCatChat?.state) return;
  if (scope.location.protocol !== 'http:' || scope.location.hostname !== '127.0.0.1') throw new Error('Loopback chat required');
  for (const asset of EMBEDDED_ASSETS) {
    const response = await scope.fetch(asset.path, { redirect: 'error', signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error('Pinned chat assets unavailable');
    const bytes = await response.arrayBuffer();
    if (bytes.byteLength > 8000000) throw new Error('Chat asset too large');
    const digest = [...new Uint8Array(await scope.crypto.subtle.digest('SHA-256', bytes))].map(x => x.toString(16).padStart(2, '0')).join('');
    if (digest !== asset.sha256) throw new Error('Chat version changed; rebuild the native integration');
  }
  const base = await import(/* @vite-ignore */ new URL(EMBEDDED_ASSETS[0].path, scope.location.origin).href);
  const chat = await import(/* @vite-ignore */ new URL(EMBEDDED_ASSETS[1].path, scope.location.origin).href);
  let signedIn = false;
  const refreshAuthentication = async () => {
    try { signedIn = (await scope.fetch('/auth/session', { redirect: 'error', signal: AbortSignal.timeout(1500) })).ok; }
    catch { signedIn = false; }
  };
  await refreshAuthentication();
  const authTimer = scope.setInterval(refreshAuthentication, 3000);
  scope.addEventListener('pagehide', () => scope.clearInterval(authTimer), { once: true });
  const reloadPolicy = async (directory, model) => {
    if (!directory) throw new Error('No isolated chat workspace');
    const headers = { 'x-opencode-directory': encodeURIComponent(directory), 'x-opencode-directory-encoding': 'uri' };
    const refresh = await scope.fetch('/api/debug/location', { method: 'DELETE', headers, signal: AbortSignal.timeout(5000) });
    if (refresh.status !== 204) throw new Error('Model policy reload failed');
    const response = await scope.fetch('/api/config', { headers, signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error('Model policy readback failed');
    const entries = await response.json();
    const info = Object.assign({}, ...entries.filter(entry => entry.info).map(entry => entry.info));
    const effectiveModel = typeof info.model === 'string' ? info.model : info.model?.providerID + '/' + info.model?.model;
    if (effectiveModel !== model || !info.permissions?.some(rule => rule.action === '*' && rule.resource === '*' && rule.effect === 'deny')) {
      throw new Error('Model policy not confirmed');
    }
  };
  let composerAllowed = false;
  const markComposer = () => {
    for (const form of scope.document.querySelectorAll('form:has([data-testid="chat-input"])')) form.inert = !composerAllowed;
  };
  const adapter = createChatAdapter({ ui: base.u, sessions: chat.z, input: chat.du, config: chat.m, reloadPolicy,
    permissionChanged: allowed => { composerAllowed = allowed; markComposer(); }, authenticated: () => signedIn });
  Object.defineProperty(scope, '__twinCatChat', { value: adapter, configurable: false, writable: false });
  scope.document.documentElement.dataset.twincatEmbedded = 'true';
  const style = scope.document.createElement('style');
  style.textContent = `html[data-twincat-embedded] .oc-draft-starters{display:none}
    html[data-twincat-chat-selection="empty"] .oc-draft-center h1,
    html[data-twincat-chat-selection="empty"] form:has([data-testid="chat-input"]){display:none}
    .tc-chat-intro{max-width:28rem;font:inherit;font-size:14px;line-height:1.6;color:var(--muted-foreground);text-wrap:balance}`;
  scope.document.head.append(style);
  const markSelection = () => {
    markComposer();
    scope.document.documentElement.dataset.twincatChatSelection = chat.z.getState().currentSessionId ? 'selected' : 'empty';
    const empty = scope.document.querySelector('.oc-draft-center');
    if (empty && !empty.querySelector('.tc-chat-intro')) {
      const intro = scope.document.createElement('p');
      intro.className = 'tc-chat-intro';
      intro.textContent = 'Starte oben einen Projektchat für den aktiven ST-Baustein oder öffne eine zugeordnete Unterhaltung im Verlauf.';
      empty.append(intro);
    }
  };
  const observer = new MutationObserver(markSelection);
  observer.observe(scope.document.body, { childList: true, subtree: true });
  const stopSelection = chat.z.subscribe(markSelection);
  markSelection();
  scope.addEventListener('pagehide', () => { observer.disconnect(); stopSelection(); }, { once: true });
}
