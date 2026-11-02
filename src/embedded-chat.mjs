// Adapter for the existing OpenChamber chat. Source builds expose the reviewed,
// versioned contract; distribution compatibility uses exports traced to v2.1.1
// (e302062e3be0686986594fddabdafd8a97c129e5). No chat or agent is duplicated.
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
// OpenChamber colors the composer's agent with a syntax color picked by a name
// hash (an inline var(--syntax-...) style), so the read-only agent could look
// like a red string. Around the agent label these variables are the accent.
const AGENT_COLOR_VARIABLES = Object.freeze(['--syntax-keyword', '--syntax-type', '--syntax-function', '--syntax-number',
  '--syntax-string', '--syntax-operator', '--syntax-variable', '--status-success']);

// Compatibility with the installed 0.1.9 distribution. Source builds use the
// existing OpenCode SDK wrapper instead. This is a global session-list route;
// reading history does not start an MCP location for every stored chat.
export async function storedProjectSessions(fetch, directory, includeArchived = false) {
  const items = [];
  const seen = new Set();
  let cursor;
  do {
    const query = new URLSearchParams({ directory, limit: '200' });
    if (cursor) query.set('cursor', cursor);
    const response = await fetch('/api/session?' + query, { redirect: 'error', signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw new Error('Stored project history unavailable');
    const page = await response.json();
    if (!Array.isArray(page.data)) throw new Error('Invalid stored project history');
    items.push(...page.data.filter(item => !item.parentID && (includeArchived || !item.time?.archived)).map(item => ({ id: item.id, updated: item.time?.updated,
      ...(typeof item.title === 'string' ? { title: item.title.slice(0, 512) } : {}),
      ...(includeArchived ? { archived: Boolean(item.time?.archived) } : {}) })));
    cursor = page.cursor?.next;
    if (cursor && (typeof cursor !== 'string' || seen.has(cursor))) throw new Error('Invalid project history cursor');
    if (cursor) seen.add(cursor);
    if (items.length > 20000) throw new Error('Project history exceeds the native metadata limit');
  } while (cursor);
  return items;
}

export function mcpStates(entries) {
  return (Array.isArray(entries) ? entries : []).filter(entry => ['knowledge', 'engineering'].includes(entry?.name))
    .map(entry => ({ name: entry.name, status: ['connected', 'pending', 'disabled', 'failed', 'needs_auth'].includes(entry.status?.status)
      ? entry.status.status : 'unknown' }));
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
// the native "AngehÃ¤ngt" state. Each chip shows its ID in the muted label.
export function composerChips(scope) {
  return {
    chips() {
      const ids = new Set();
      for (const form of scope.document.querySelectorAll('form:has([data-testid="chat-input"])')) {
        for (const label of form.querySelectorAll('div[title] > button[type="button"] > span.text-muted-foreground')) {
          const id = label.textContent.trim();
          if (id) ids.add(id);
        }
      }
      return [...ids];
    },
  };
}

export function createChatAdapter({ ui, sessions, input, config, source = null, loadSessions, reloadPolicy, providers = () => [], allowed = () => [], mcp = () => [], mcpObservation = () => null, theme = () => null, permissionChanged = () => {}, authenticated = () => true, composer = null, documentId = crypto.randomUUID() }) {
  for (const store of [ui, sessions, input]) {
    if (typeof store?.getState !== 'function' || typeof store?.subscribe !== 'function') throw new Error('Incompatible chat store');
  }
  if (typeof sessions.getState().createSession !== 'function' || typeof input.getState().setPendingGuestIssue !== 'function') {
    throw new Error('Incompatible chat actions');
  }
  let epoch = 0;
  let selected = sessions.getState().currentSessionId;
  let permission = null;
  // Chip ID -> { title, revision, reference } for chips this adapter staged.
  // Presence is always re-read from the composer; this only adds revisions.
  // Automatic attachment reuses one chip ID, so the composer replaces that
  // chip in place on every POU switch instead of adding another one.
  const staged = new Map();
  const queuedDrafts = new Map();
  const knownHistory = new Map();
  let creating = false;
  const pending = id => input.getState().pendingGuestIssue?.providerId === 'twincat-assistant' && input.getState().pendingGuestIssue.id === id;
  const present = () => {
    const chips = new Set(composer ? composer.chips() : staged.keys());
    return id => chips.has(id) || pending(id);
  };
  const fallbackAttachments = () => {
    const has = present();
    return [...staged].filter(([id]) => has(id)).slice(-8)
      .map(([id, value]) => ({ id, title: value.title, revision: value.revision, reference: value.reference, mode: value.mode }));
  };
  const clearPending = () => {
    if (input.getState().pendingGuestIssue?.providerId === 'twincat-assistant') input.getState().setPendingGuestIssue(null);
  };
  const clearFollowing = () => {
    const queued = input.getState().pendingGuestIssue;
    if (queued?.providerId === 'twincat-assistant' && (queued.data?.mode === 'following' || queued.id === 'Aktiver Baustein')) clearPending();
    source?.clearFollowing?.(selected);
    staged.delete('Aktiver Baustein');
  };
  const unsubscribe = sessions.subscribe(state => {
    if (selected === state.currentSessionId) return;
    const queued = input.getState().pendingGuestIssue;
    if (queued?.providerId === 'twincat-assistant' && queued.id !== 'Aktiver Baustein' && queued.data?.mode !== 'following') queuedDrafts.set(selected, queued);
    else queuedDrafts.delete(selected);
    selected = state.currentSessionId;
    epoch++;
    permission = null;
    permissionChanged(false);
    staged.clear();
    source?.setContext('', null);
    clearPending();
    const restore = queuedDrafts.get(selected);
    if (restore) { queuedDrafts.delete(selected); input.getState().setPendingGuestIssue(restore); }
  });
  const unsubscribeUI = ui.subscribe(value => { if (value.isSidebarOpen) ui.getState().setSidebarOpen(false); });
  ui.getState().setSidebarOpen(false);
  ui.getState().setDraftStartersVisible(false);
  ui.getState().setShowOpenCodeUpdateNotifications(false);
  ui.getState().setContextRailHiddenSurfaces(['git', 'pr', 'diff', 'walkthrough', 'linear', 'editor', 'terminal', 'notes', 'plan', 'browser', 'chat']);

  const state = () => {
    const value = sessions.getState();
    const model = config?.getState();
    const observation = source?.composer(value.currentSessionId);
    const queued = input.getState().pendingGuestIssue;
    const pending = queued?.providerId === 'twincat-assistant' ? queued : null;
    const metadata = item => ({ id: item.id, title: item.title, revision: item.revision ?? null, mode: item.mode ?? (item.id === 'Aktiver Baustein' ? 'following' : 'manual'),
      reference: item.reference ?? staged.get(item.id)?.reference ?? item.id });
    // The source composer is authoritative. The registry only supplies metadata
    // for the older distribution, whose composer exposes chip IDs via markup.
    const references = source ? (observation?.references ?? []).map(metadata) : fallbackAttachments();
    const byId = new Map(references.map(item => [item.id, item]));
    if (pending) byId.set(pending.id, metadata({ ...pending, ...pending.data }));
    const attachments = [...byId.values()].slice(-8);
    const submittedAttachments = (observation?.submitted ?? []).map(metadata).slice(-8);
    const context = permission?.context;
    const title = context?.object ? context.object + ' / ' + context.project : null;
    const current = items => context?.reference ? items.filter(item => item.reference === context.reference).at(-1)
      : title ? items.filter(item => item.title === title).at(-1) : items.at(-1);
    const attached = current(attachments);
    const submitted = current(submittedAttachments);
    // Source builds report committed history readiness. The older distribution
    // retains the native host's markup and project-binding checks.
    return { version: 1, ready: true, presentationReady: source ? source.presentationReady?.() === true : true,
      authenticated: authenticated(), canExport: typeof source?.exportThread === 'function', canManage: typeof source?.manageSession === 'function', documentId, sessionId: value.currentSessionId ?? null,
      directory: value.currentSessionDirectory ?? null, epoch, creating, providers: providers(), allowedProviders: allowed(), mcp: mcp(), mcpObservation: mcpObservation(),
      manualAttachmentPending: !!pending && pending.id !== 'Aktiver Baustein' && pending.data?.mode !== 'following',
      model: model?.currentProviderId && model?.currentModelId ? model.currentProviderId + '/' + model.currentModelId : null,
      attachmentState: attached ? (pending?.id === attached.id ? 'staging' : 'staged') : submitted ? 'submitted' : 'none',
      attachedObject: attached?.title ?? null, attachedRevision: attached?.revision ?? null,
      submittedObject: submitted?.title ?? null, submittedRevision: submitted?.revision ?? null,
      attachments, submittedAttachments, theme: theme() };
  };
  const matches = expected => expected && expected.documentId === documentId && expected.sessionId && expected.sessionId === selected && expected.epoch === epoch;
  return Object.freeze({
    state,
    async exportThread(expected) {
      if (!authenticated() || !matches(expected) || typeof source?.exportThread !== 'function') throw new Error('Thread export is not available');
      const directory = sessions.getState().currentSessionDirectory;
      if (typeof directory !== 'string' || !directory) throw new Error('Thread directory is unavailable');
      const result = await source.exportThread(selected, directory);
      if (!authenticated() || !matches(expected) || sessions.getState().currentSessionDirectory !== directory)
        throw new Error('Chat selection changed during export');
      if (!result || typeof result.markdown !== 'string' || result.markdown.length > 8 * 1024 * 1024
        || typeof result.filename !== 'string' || result.filename.length > 200 || !result.filename.endsWith('.md')
        || /[\\/\x00-\x1f]/.test(result.filename)) throw new Error('Thread export is invalid or too large');
      return result;
    },
    clearFollowing,
    permit(expected) {
      const next = matches(expected) ? { ...expected } : null;
      if (permission?.contextKey !== next?.contextKey) knownHistory.clear();
      permission = next;
      if (!permission) clearPending();
      const context = permission?.context;
      if (context && (Object.keys(context).some(key => !['project', 'object', 'treePath', 'sourceKind', 'reference', 'objectKind', 'owner', 'member'].includes(key))
        || typeof context.project !== 'string' || context.project.length > 200
        || (context.reference != null && (typeof context.reference !== 'string' || context.reference.length > 128))
        || ['object', 'treePath', 'sourceKind', 'objectKind', 'owner', 'member'].some(key => context[key] != null && (typeof context[key] !== 'string' || context[key].length > 2048)))) {
        permission = null;
        source?.setContext('', null);
        permissionChanged(false);
        throw new Error('Invalid native context metadata');
      }
      if (!context?.reference) clearFollowing();
      source?.setContext(selected, context ?? null);
      permissionChanged(!!permission);
      return !!permission;
    },
    attach(request) {
      if (!matches(request) || !matches(permission) || request.contextKey !== permission.contextKey) throw new Error('Chat binding changed');
      const item = request.item;
      if (!item || item.providerId !== 'twincat-assistant' || typeof item.text !== 'string' || item.text.length > 16000
          || typeof item.title !== 'string' || item.title.length > 200 || typeof item.id !== 'string' || item.id.length > 128) {
        throw new Error('Invalid POU attachment');
      }
      // A newer TwinCAT reference replaces an own one the composer has not
      // consumed yet (fast POU switches); another extension's item stays.
      const queued = input.getState().pendingGuestIssue;
      if (queued && queued.providerId !== 'twincat-assistant') throw new Error('Another attachment is still pending');
      if (queued && queued.id !== item.id && queued.id !== 'Aktiver Baustein' && queued.data?.mode !== 'following')
        throw new Error('A manual attachment is still pending');
      // The same queue and reference type used by SDK host.attach. It stages a
      // visible composer chip. It never submits a prompt or invokes a model.
      // The composer replaces a chip with the same ID instead of adding one.
      input.getState().setPendingGuestIssue(item);
      if (queued && queued.id !== item.id && !composer?.chips().includes(queued.id)) staged.delete(queued.id);
      staged.delete(item.id);
      staged.set(item.id, { title: item.title, revision: item.data?.revision ?? null, mode: item.data?.mode ?? (request.auto ? 'following' : 'manual'),
        reference: typeof item.data?.reference === 'string' ? item.data.reference.slice(0, 128) : item.id });
      while (staged.size > 32) staged.delete(staged.keys().next().value);
      return { sessionId: selected, epoch, attachedObject: item.title };
    },
    async newChat({ title, directory }) {
      if (creating || (title != null && (typeof title !== 'string' || title.length > 200)) || typeof directory !== 'string' || !directory) throw new Error('Invalid new chat');
      creating = true;
      try {
        const result = await sessions.getState().createSession(title ?? undefined, directory);
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
    async storedSessions(directory, includeArchived = false) {
      const historyContext = permission?.contextKey;
      const directories = Array.isArray(directory) ? directory : [directory];
      if (!directories.length || directories.length > 64 || directories.some(value => typeof value !== 'string' || !value || value.length > 2048) || typeof loadSessions !== 'function')
        throw new Error('Stored project history unavailable');
      const list = [];
      for (const value of [...new Set(directories)]) {
        const path = value.replace(/\\/g, '/');
        const items = await loadSessions(path, includeArchived);
        if (!Array.isArray(items)) throw new Error('Invalid stored project history');
        list.push(...items.map(item => ({ ...item, directory: path })));
        if (list.length > 20000) throw new Error('Project history exceeds the native metadata limit');
      }
      const result = list.filter(item => typeof item?.id === 'string' && SESSION_ID.test(item.id))
        .map(item => ({ id: item.id, updated: Number(item.updated) || 0, directory: item.directory,
          ...(typeof item.title === 'string' ? { title: item.title.slice(0, 512) } : {}),
          ...(typeof item.archived === 'boolean' ? { archived: item.archived } : {}),
          ...(typeof item.busy === 'boolean' ? { busy: item.busy } : {}) }))
        .sort((a, b) => b.updated - a.updated);
      for (const item of result) knownHistory.set(item.id, { ...item, contextKey: historyContext });
      return result;
    },
    async manageSession(expected, request) {
      const assertCurrent = () => {
        if (!authenticated() || !matches(expected) || !permission || !matches(permission) || permission.contextKey !== expected.contextKey)
          throw new Error('Chat selection changed');
      };
      assertCurrent();
      const target = knownHistory.get(request?.id);
      if (!target || target.contextKey !== expected.contextKey || target.directory !== request.directory || typeof source?.manageSession !== 'function')
        throw new Error('Chat is outside the reviewed history');
      if (!['rename', 'generate-title', 'archive', 'restore'].includes(request.action)) throw new Error('Unsupported chat action');
      if (request.action === 'generate-title' && (target.archived || target.busy)) throw new Error('Chat is archived or busy');
      if (request.action === 'rename' && (typeof request.title !== 'string' || !request.title.trim() || request.title.length > 512 || /[\x00-\x1f\x7f]/.test(request.title)))
        throw new Error('Invalid chat title');
      return await source.manageSession({ id: target.id, directory: target.directory, action: request.action,
        ...(request.action === 'rename' ? { title: request.title.trim() } : {}) }, assertCurrent);
    },
    selectSession(id, directory) {
      if (typeof id !== 'string' || !SESSION_ID.test(id) || typeof sessions.getState().setCurrentSession !== 'function') return false;
      if (selected !== id) sessions.getState().setCurrentSession(id, directory);
      return sessions.getState().currentSessionId === id;
    },
    toggleChats() { ui.getState().setSidebarOpen(false); },
    openSettings() { ui.getState().setSettingsPage('providers'); ui.getState().setSettingsDialogOpen(true); },
    async refreshPolicy(expected, provider, model = null) {
      if (!matches(expected) || typeof reloadPolicy !== 'function') throw new Error('Chat selection changed');
      const directory = sessions.getState().currentSessionDirectory;
      await reloadPolicy(directory, provider, model);
      await config?.getState().loadProviders?.({ directory, fresh: true, source: 'twincat:provider-consent' });
      if (!matches(expected)) throw new Error('Chat selection changed');
      return true;
    },
    dispose() { unsubscribe(); unsubscribeUI(); clearPending(); source?.setContext('', null); permission = null; permissionChanged(false); },
  });
}

export async function installEmbeddedChat(scope = window) {
  if (scope.__twinCatChat?.state) return;
  if (scope.location.protocol !== 'http:' || scope.location.hostname !== '127.0.0.1') throw new Error('Loopback chat required');
  if (scope.__twinCatSourceReady) {
    let timeout;
    try {
      await Promise.race([scope.__twinCatSourceReady, new Promise((_, reject) => {
        timeout = scope.setTimeout(() => reject(new Error('Source chat initialization timed out')), 15000);
      })]);
    } finally { scope.clearTimeout(timeout); }
  }
  const source = scope.__twinCatSource;
  if (source && source.version !== 1) throw new Error('Incompatible TwinCAT source contract');
  for (const asset of source ? [] : EMBEDDED_ASSETS) {
    const response = await scope.fetch(asset.path, { redirect: 'error', signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error('Pinned chat assets unavailable');
    const bytes = await response.arrayBuffer();
    if (bytes.byteLength > 8000000) throw new Error('Chat asset too large');
    const digest = [...new Uint8Array(await scope.crypto.subtle.digest('SHA-256', bytes))].map(x => x.toString(16).padStart(2, '0')).join('');
    if (digest !== asset.sha256) throw new Error('Chat version changed; rebuild the native integration');
  }
  const base = source ? { u: source.stores.ui } : await import(/* @vite-ignore */ new URL(EMBEDDED_ASSETS[0].path, scope.location.origin).href);
  const chat = source ? { z: source.stores.sessions, du: source.stores.input, l: source.stores.config, df: source.stores.locale }
    : await import(/* @vite-ignore */ new URL(EMBEDDED_ASSETS[1].path, scope.location.origin).href);
  source?.activate();
  if (typeof chat.l?.getState().loadProviders !== 'function') throw new Error('Incompatible provider config store');
  // OpenChamber ships a German dictionary. Use it unless the operator already
  // chose a language in OpenChamber's own settings.
  try { if (!scope.localStorage.getItem('openchamber.i18n.v1')) chat.df?.getState().setLocale?.('de'); } catch { /* storage unavailable */ }
  let signedIn = false;
  let connectedProviders = [];
  let enabledProviders = [];
  let connectedMcp = [];
  let mcpDirectory = null;
  let mcpObserved = false;
  let mcpStale = true;
  const currentDirectory = () => chat.z.getState().currentSessionDirectory ?? chat.l.getState().activeDirectoryKey;
  const resetMcp = directory => { mcpDirectory = directory; connectedMcp = []; mcpObserved = false; mcpStale = true; };
  let refreshingAuthentication = false;
  const refreshAuthentication = async () => {
    if (refreshingAuthentication) return;
    refreshingAuthentication = true;
    try {
      try { signedIn = (await scope.fetch('/auth/session', { redirect: 'error', signal: AbortSignal.timeout(1500) })).ok; }
      catch { signedIn = false; mcpStale = true; return; }
      if (!signedIn) { connectedProviders = []; enabledProviders = []; resetMcp(null); return; }
      const directory = currentDirectory();
      if (typeof directory !== 'string' || !/^(?:[a-z]:[\\/]|\/)/i.test(directory)) { connectedProviders = []; enabledProviders = []; resetMcp(null); return; }
      if (mcpDirectory !== directory) { resetMcp(directory); enabledProviders = []; }
      const headers = { 'x-opencode-directory': encodeURIComponent(directory), 'x-opencode-directory-encoding': 'uri' };
      const read = async path => {
        const response = await scope.fetch(path, { headers, redirect: 'error', signal: AbortSignal.timeout(3000) });
        if (!response.ok) throw new Error('Provider metadata unavailable');
        const value = await response.json();
        const items = value.data ?? value;
        if (!Array.isArray(items) || items.length > 1000) throw new Error('Invalid provider metadata');
        return items;
      };
      // Provider discovery is independent of the MCP catalog. A failed read is
      // unknown, never an empty authoritative catalog or a reconnect request.
      const [providerRead, mcpRead] = await Promise.allSettled([
        Promise.all([read('/api/integration'), read('/api/websearch/provider'), read('/api/config')]), read('/api/mcp'),
      ]);
      if (currentDirectory() !== directory) return;
      if (providerRead.status === 'fulfilled') {
        const [integrations, search, effective] = providerRead.value;
        connectedProviders = consentProviders(integrations, search, chat.l.getState().providers ?? []);
        enabledProviders = allowedProviders(effective);
      } else { connectedProviders = []; enabledProviders = []; }
      if (mcpRead.status === 'fulfilled') {
        connectedMcp = mcpStates(mcpRead.value); mcpObserved = true; mcpStale = false;
      } else { mcpStale = true; }
    } catch { connectedProviders = []; enabledProviders = []; mcpStale = true; }
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
  const loadSessions = source ? (directory, archived) => source.sessions(directory, archived) : (directory, archived) => storedProjectSessions(scope.fetch.bind(scope), directory, archived);
  const adapter = createChatAdapter({ ui: base.u, sessions: chat.z, input: chat.du, config: chat.l, source, loadSessions, reloadPolicy,
    providers: () => connectedProviders, allowed: () => enabledProviders,
    mcp: () => mcpDirectory === currentDirectory() ? connectedMcp : [],
    mcpObservation: () => ({ directory: currentDirectory() ?? null, observed: mcpDirectory === currentDirectory() && mcpObserved,
      stale: !signedIn || mcpDirectory !== currentDirectory() || mcpStale }),
    theme: () => readTheme(scope), composer: source ? null : composerChips(scope),
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
    .tc-chat-intro{max-width:28rem;font:inherit;font-size:14px;line-height:1.6;color:var(--muted-foreground);text-wrap:balance}
    html[data-twincat-embedded] .model-controls__agent-trigger,
    html[data-twincat-embedded] :has(> .model-controls__agent-label){${AGENT_COLOR_VARIABLES.map(name => name + ':var(--primary)').join(';')}}`;
  scope.document.head.append(style);
  const markSelection = () => {
    markComposer();
    scope.document.documentElement.dataset.twincatChatSelection = chat.z.getState().currentSessionId ? 'selected' : 'empty';
    const empty = scope.document.querySelector('.oc-draft-center');
    if (empty && !empty.querySelector('.tc-chat-intro')) {
      const intro = scope.document.createElement('p');
      intro.className = 'tc-chat-intro';
      intro.textContent = 'Ã–ffne einen ST-Baustein, dann startet der passende Projektchat automatisch. Mit + oben beginnt ein allgemeiner Chat.';
      empty.append(intro);
    }
  };
  const observer = new MutationObserver(markSelection);
  observer.observe(scope.document.body, { childList: true, subtree: true });
  const stopSelection = chat.z.subscribe(markSelection);
  markSelection();
  scope.addEventListener('pagehide', () => { observer.disconnect(); stopSelection(); }, { once: true });
}
