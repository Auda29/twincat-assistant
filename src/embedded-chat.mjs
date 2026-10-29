// Version-specific adapter for the *existing* OpenChamber chat. These exports
// were traced to the source at v2.1.1 (e302062e3be0686986594fddabdafd8a97c129e5).
// No upstream file is patched and no chat, model client or agent is duplicated.
export const EMBEDDED_ASSETS = Object.freeze([
  { path: '/assets/index-rxO-w0yQ.js', sha256: 'd7b60bc7722a484b53e89ac18e76d3f6d172fa1efc09cb7ad784af7ce96e28b8' },
  { path: '/assets/useAppFontEffects-DC1U1Z6i.js', sha256: '1a29973e760c994a97a8a64d29c24bdcfbc4f358d9c29919f07efeac0e40ff63' },
]);

// Integrations remain visible even when provider.use hides the model catalog.
// Project only public labels/IDs; never pass accounts, methods or credentials to native code.
export function consentProviders(integrations, webSearch = [], visible = []) {
  const excluded = new Set(webSearch.map(item => item.id));
  const candidates = [...integrations.filter(item => item.connections?.length > 0), ...visible];
  return [...new Map(candidates.filter(item => typeof item.id === 'string'
    && /^[a-zA-Z0-9_-]{1,80}$/.test(item.id) && !item.id.startsWith('mcp_') && !excluded.has(item.id))
    .map(item => [item.id, { id: item.id, name: typeof item.name === 'string' ? item.name.slice(0, 80) : item.id }])).values()]
    .sort((a, b) => a.id.localeCompare(b.id)).slice(0, 32);
}

// The effective upstream config is the authority for which provider the native
// consent enabled. Only plain provider IDs leave this function.
export function allowedProviders(entries) {
  const policies = (Array.isArray(entries) ? entries : []).flatMap(entry => entry?.info?.experimental?.policies ?? []);
  return [...new Set(policies.filter(rule => rule?.action === 'provider.use' && rule.effect === 'allow'
    && typeof rule.resource === 'string' && /^[a-zA-Z0-9_-]{1,80}$/.test(rule.resource)).map(rule => rule.resource))].slice(0, 8);
}

const SESSION_ID = /^[a-zA-Z0-9_:-]{1,160}$/;

export function mcpStates(entries) {
  return (Array.isArray(entries) ? entries : []).filter(entry => ['knowledge', 'engineering'].includes(entry?.name))
    .map(entry => ({ name: entry.name, status: ['connected', 'pending', 'disabled', 'failed', 'needs_auth'].includes(entry.status?.status)
      ? entry.status.status : 'failed' }));
}

// Native context bar colors follow the live OpenChamber theme. Values are
// normalized to #rrggbb through a canvas so any CSS color syntax works.
const THEME_VARIABLES = Object.freeze({ background: '--background', foreground: '--foreground', muted: '--muted-foreground',
  border: '--border', surface: '--surface-elevated', primary: '--primary', primaryForeground: '--primary-foreground',
  error: '--status-error', warning: '--status-warning', success: '--status-success' });
let themeCanvas = null;
export function readTheme(scope) {
  try {
    themeCanvas ??= scope.document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    const root = scope.getComputedStyle(scope.document.documentElement);
    const result = { dark: scope.document.documentElement.classList.contains('dark') };
    for (const [key, variable] of Object.entries(THEME_VARIABLES)) {
      const value = root.getPropertyValue(variable).trim();
      if (!value) return null;
      themeCanvas.clearRect(0, 0, 1, 1);
      themeCanvas.fillStyle = '#000';
      themeCanvas.fillStyle = value;
      themeCanvas.fillRect(0, 0, 1, 1);
      const [r, g, b] = themeCanvas.getImageData(0, 0, 1, 1).data;
      result[key] = '#' + [r, g, b].map(part => part.toString(16).padStart(2, '0')).join('');
    }
    return result;
  } catch { return null; }
}

// Staged POU chips live in the composer's own React state, not in a store.
// Reading them from the pinned chip markup lets a removed or sent chip clear
// the native "Angehängt" state. Each chip shows its reference ID in the
// muted label and ends with its remove button.
export function composerChips(scope) {
  const read = () => {
    const chips = new Map();
    for (const form of scope.document.querySelectorAll('form:has([data-testid="chat-input"])')) {
      for (const label of form.querySelectorAll('div[title] > button[type="button"] > span.text-muted-foreground')) {
        const remove = label.parentElement.parentElement.querySelector(':scope > span > button[type="button"]');
        const id = label.textContent.trim();
        if (id && remove && !chips.has(id)) chips.set(id, remove);
      }
    }
    return chips;
  };
  return {
    chips: () => [...read().keys()],
    remove: id => { const button = read().get(id); if (!button) return false; button.click(); return true; },
  };
}

export function createChatAdapter({ ui, sessions, input, config, reloadPolicy, providers = () => [], allowed = () => [], mcp = () => [], theme = () => null, permissionChanged = () => {}, authenticated = () => true, composer = null, documentId = crypto.randomUUID() }) {
  for (const store of [ui, sessions, input]) {
    if (typeof store?.getState !== 'function' || typeof store?.subscribe !== 'function') throw new Error('Incompatible chat store');
  }
  if (typeof sessions.getState().createSession !== 'function' || typeof input.getState().setPendingGuestIssue !== 'function') {
    throw new Error('Incompatible chat actions');
  }
  let epoch = 0;
  let selected = sessions.getState().currentSessionId;
  let permission = null;
  // Reference ID -> { title, revision, auto } for chips this adapter staged.
  // Presence is always re-read from the composer; this only adds revisions.
  const staged = new Map();
  let last = null;
  let creating = false;
  const pending = id => input.getState().pendingGuestIssue?.providerId === 'twincat-assistant' && input.getState().pendingGuestIssue.id === id;
  const present = () => {
    const chips = new Set(composer ? composer.chips() : staged.keys());
    return id => chips.has(id) || pending(id);
  };
  const attachments = () => {
    const has = present();
    return [...staged].filter(([id]) => has(id)).slice(-8).map(([id, value]) => ({ id, title: value.title, revision: value.revision }));
  };
  const clearPending = () => {
    if (input.getState().pendingGuestIssue?.providerId === 'twincat-assistant') input.getState().setPendingGuestIssue(null);
  };
  const unsubscribe = sessions.subscribe(state => {
    if (selected === state.currentSessionId) return;
    selected = state.currentSessionId;
    epoch++;
    permission = null;
    permissionChanged(false);
    staged.clear();
    last = null;
    clearPending();
  });
  ui.getState().setSidebarOpen(false);
  ui.getState().setDraftStartersVisible(false);
  ui.getState().setShowOpenCodeUpdateNotifications(false);
  ui.getState().setContextRailHiddenSurfaces(['git', 'pr', 'diff', 'walkthrough', 'linear', 'editor', 'terminal', 'notes', 'plan', 'browser', 'chat']);

  // The most recently staged chip, while it is still in the composer.
  const current = () => {
    const value = last && present()(last) ? staged.get(last) : null;
    return { attachedObject: value?.title ?? null, attachedRevision: value?.revision ?? null };
  };
  const state = () => {
    const value = sessions.getState();
    const model = config?.getState();
    return { version: 1, ready: true, authenticated: authenticated(), documentId, sessionId: value.currentSessionId ?? null,
      directory: value.currentSessionDirectory ?? null, epoch, creating, providers: providers(), allowedProviders: allowed(), mcp: mcp(),
      model: model?.currentProviderId && model?.currentModelId ? model.currentProviderId + '/' + model.currentModelId : null,
      ...current(), attachments: attachments(), theme: theme() };
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
      const auto = request.auto === true;
      const has = present();
      // Automatic attachment follows the active POU: it replaces the chip it
      // staged for a previous POU. Chips the operator attached stay.
      if (auto && composer) {
        for (const [id, value] of staged) if (value.auto && id !== item.id && has(id)) composer.remove(id);
      }
      // The same queue and reference type used by SDK host.attach. It stages a
      // visible composer chip. It never submits a prompt or invokes a model.
      // The composer replaces a chip with the same ID instead of adding one.
      input.getState().setPendingGuestIssue(item);
      const prior = has(item.id) ? staged.get(item.id) : null;
      staged.delete(item.id);
      staged.set(item.id, { title: item.title, revision: item.data?.revision ?? null, auto: auto && prior?.auto !== false });
      while (staged.size > 32) staged.delete(staged.keys().next().value);
      last = item.id;
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
    // Metadata only: lets the native host reopen the newest conversation it
    // already bound to a PLC project instead of creating a new one.
    recentSessions(directory) {
      if (typeof directory !== 'string' || !directory || directory.length > 2048) return [];
      const list = sessions.getState().getSessionsByDirectory?.(directory.replace(/\\/g, '/')) ?? [];
      return (Array.isArray(list) ? list : []).filter(item => typeof item?.id === 'string' && SESSION_ID.test(item.id))
        .map(item => ({ id: item.id, updated: Number(item.time?.updated) || 0 }))
        .sort((a, b) => b.updated - a.updated).slice(0, 200);
    },
    selectSession(id) {
      if (typeof id !== 'string' || !SESSION_ID.test(id) || typeof sessions.getState().setCurrentSession !== 'function') return false;
      if (selected !== id) sessions.getState().setCurrentSession(id);
      return sessions.getState().currentSessionId === id;
    },
    toggleChats() { ui.getState().toggleSidebar(); },
    openSettings() { ui.getState().setSettingsPage('providers'); ui.getState().setSettingsDialogOpen(true); },
    async refreshPolicy(expected, provider, model = null) {
      if (!matches(expected) || typeof reloadPolicy !== 'function') throw new Error('Chat selection changed');
      const directory = sessions.getState().currentSessionDirectory;
      await reloadPolicy(directory, provider, model);
      await config?.getState().loadProviders?.({ directory, fresh: true, source: 'twincat:provider-consent' });
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
  if (typeof chat.l?.getState().loadProviders !== 'function') throw new Error('Incompatible provider config store');
  // OpenChamber ships a German dictionary. Use it unless the operator already
  // chose a language in OpenChamber's own settings.
  try { if (!scope.localStorage.getItem('openchamber.i18n.v1')) chat.df?.getState().setLocale?.('de'); } catch { /* storage unavailable */ }
  let signedIn = false;
  let connectedProviders = [];
  let enabledProviders = [];
  let connectedMcp = [];
  let refreshingAuthentication = false;
  const refreshAuthentication = async () => {
    if (refreshingAuthentication) return;
    refreshingAuthentication = true;
    try {
      try { signedIn = (await scope.fetch('/auth/session', { redirect: 'error', signal: AbortSignal.timeout(1500) })).ok; }
      catch { signedIn = false; }
      if (!signedIn) { connectedProviders = []; enabledProviders = []; connectedMcp = []; return; }
      const directory = chat.z.getState().currentSessionDirectory ?? chat.l.getState().activeDirectoryKey;
      if (typeof directory !== 'string' || !/^(?:[a-z]:[\\/]|\/)/i.test(directory)) { connectedProviders = []; enabledProviders = []; connectedMcp = []; return; }
      const headers = { 'x-opencode-directory': encodeURIComponent(directory), 'x-opencode-directory-encoding': 'uri' };
      const read = async path => {
        const response = await scope.fetch(path, { headers, redirect: 'error', signal: AbortSignal.timeout(3000) });
        if (!response.ok) throw new Error('Provider metadata unavailable');
        const value = await response.json();
        const items = value.data ?? value;
        if (!Array.isArray(items) || items.length > 1000) throw new Error('Invalid provider metadata');
        return items;
      };
      const [integrations, search, effective] = await Promise.all([read('/api/integration'), read('/api/websearch/provider'), read('/api/config')]);
      connectedProviders = consentProviders(integrations, search, chat.l.getState().providers ?? []);
      enabledProviders = allowedProviders(effective);
      try { connectedMcp = mcpStates(await read('/api/mcp')); } catch { connectedMcp = []; }
    } catch { connectedProviders = []; enabledProviders = []; connectedMcp = []; }
    finally { refreshingAuthentication = false; }
  };
  await refreshAuthentication();
  const authTimer = scope.setInterval(refreshAuthentication, 3000);
  scope.addEventListener('pagehide', () => scope.clearInterval(authTimer), { once: true });
  const reloadPolicy = async (directory, provider, model) => {
    if (!directory) throw new Error('No isolated chat workspace');
    const headers = { 'x-opencode-directory': encodeURIComponent(directory), 'x-opencode-directory-encoding': 'uri' };
    const refresh = await scope.fetch('/api/debug/location', { method: 'DELETE', headers, signal: AbortSignal.timeout(5000) });
    if (refresh.status !== 204) throw new Error('Model policy reload failed');
    // Config watchers/location rebuilds can briefly return the previous policy.
    // Retry only bounded reads, never the consent write or location mutation.
    const deadline = Date.now() + 10000;
    do {
      const response = await scope.fetch('/api/config', { headers, signal: AbortSignal.timeout(Math.min(3000, Math.max(1, deadline - Date.now()))) });
      if (!response.ok) throw new Error('Model policy readback failed');
      const entries = await response.json();
      const info = Object.assign({}, ...entries.filter(entry => entry.info).map(entry => entry.info));
      const effectiveModel = typeof info.model === 'string' ? info.model : info.model?.providerID + '/' + info.model?.model;
      if ((!model || effectiveModel === model)
          && info.experimental?.policies?.some(rule => rule.action === 'provider.use' && rule.resource === provider && rule.effect === 'allow')
          && info.permissions?.some(rule => rule.action === '*' && rule.resource === '*' && rule.effect === 'deny')) return;
      await new Promise(resolve => scope.setTimeout(resolve, 250));
    } while (Date.now() < deadline);
    throw new Error('Model policy not confirmed');
  };
  let composerAllowed = false;
  const markComposer = () => {
    for (const form of scope.document.querySelectorAll('form:has([data-testid="chat-input"])')) form.inert = !composerAllowed;
  };
  const adapter = createChatAdapter({ ui: base.u, sessions: chat.z, input: chat.du, config: chat.l, reloadPolicy,
    providers: () => connectedProviders, allowed: () => enabledProviders, mcp: () => connectedMcp, theme: () => readTheme(scope), composer: composerChips(scope),
    permissionChanged: allowed => { composerAllowed = allowed; markComposer(); }, authenticated: () => signedIn });
  Object.defineProperty(scope, '__twinCatChat', { value: adapter, configurable: false, writable: false });
  scope.document.documentElement.dataset.twincatEmbedded = 'true';
  const style = scope.document.createElement('style');
  // The native context bar replaces OpenChamber's top bar: its new-chat and
  // session buttons would create conversations without a PLC binding.
  style.textContent = `html[data-twincat-embedded] .oc-draft-starters{display:none}
    html[data-twincat-embedded] .main-content-safe-area > .absolute.left-0.top-0.z-30,
    html[data-twincat-embedded] header.header-safe-area{display:none}
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
      intro.textContent = 'Öffne einen ST-Baustein, dann startet der passende Projektchat automatisch. Mit + oben beginnt ein allgemeiner Chat.';
      empty.append(intro);
    }
  };
  const observer = new MutationObserver(markSelection);
  observer.observe(scope.document.body, { childList: true, subtree: true });
  const stopSelection = chat.z.subscribe(markSelection);
  markSelection();
  scope.addEventListener('pagehide', () => { observer.disconnect(); stopSelection(); }, { once: true });
}
