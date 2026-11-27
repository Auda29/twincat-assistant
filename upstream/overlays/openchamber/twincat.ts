import { useUIStore } from '@/stores/useUIStore';
import { useConfigStore } from '@/stores/useConfigStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { useInputStore } from '@/sync/input-store';
import { useI18nStore } from '@/lib/i18n';
import { opencodeClient, setTwinCatSendPolicy } from '@/lib/opencode/client';
import { z } from 'zod';
import type { ComposerReference } from '@/components/chat/composer/composerReferences';
import { buildExportFilename, formatSessionAsMarkdown, type SessionMessageRecord } from '@/lib/exportSession';
import React from 'react';
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Icon } from '@/components/icon/Icon';
import { getChatDraftIdentityKey, type ChatDraftIdentity } from '@/lib/chatDraftPersistence';
import { useGlobalSessionsStore, resolveGlobalSessionDirectory } from '@/stores/useGlobalSessionsStore';
import { archiveSession, unarchiveSession, updateSessionTitle } from '@/sync/session-actions';
import { registerSessionDirectory } from '@/sync/sync-refs';
import { collectSessionTitleTurns, generateSessionTitle } from '@/lib/sessionTitle';
import { generateAndSaveSessionTitle, runSessionTitleGeneration } from '@/sync/session-title-generation';
import { getRuntimeKey } from '@/lib/runtime-switch';
import { normalizePath } from '@/lib/pathNormalization';

function titleFailure(code: string) { return Object.assign(new Error('Chat title operation failed'), { code }); }
export function validateTwinCatGeneratedTitle(value: string): string {
  const title = value.normalize('NFC').trim();
  if (!title || /[\u0000-\u001f\u007f-\u009f\u202a-\u202e\u2066-\u2069\ufffd]|\p{Surrogate}/u.test(title)) throw titleFailure('invalid_title');
  // Preserve valid Unicode, including CJK titles. Reject malformed paired
  // punctuation observed in model output, rather than guessing missing text.
  const pairs = new Map([['【', '】'], ['「', '」'], ['『', '』'], ['（', '）'], ['［', '］'], ['｛', '｝']]);
  const closers = new Set(pairs.values());
  const stack: string[] = [];
  for (const char of title) {
    const close = pairs.get(char);
    if (close) stack.push(close);
    else if (closers.has(char) && stack.pop() !== char) throw titleFailure('invalid_title');
  }
  if (stack.length) throw titleFailure('invalid_title');
  return title;
}

type Reference = { id: string; title: string; revision: string | null; reference: string; mode: 'following' | 'manual' };
type Context = { project: string; object: string | null; treePath: string | null; sourceKind: string | null; reference?: string | null;
  objectKind?: string | null; owner?: string | null; member?: string | null };
let active = false;
const embeddingListeners = new Set<() => void>();
export const isTwinCatEmbedded = () => active;
// Text and file drafts already use the original per-chat stores. Keep the
// composer's linked references with that same identity instead of transferring
// the local React array to whichever conversation mounts next.
const referenceDrafts = new Map<string, ComposerReference[]>();
const referenceListeners = new Set<() => void>();
const emptyReferences: ComposerReference[] = [];
export function useTwinCatComposerReferences(identity: ChatDraftIdentity | null) {
  const [ordinary, setOrdinary] = React.useState<ComposerReference[]>([]);
  const key = active && identity ? getChatDraftIdentityKey(identity) : null;
  const references = React.useSyncExternalStore(
    React.useCallback((listener: () => void) => { referenceListeners.add(listener); return () => { referenceListeners.delete(listener); }; }, []),
    React.useCallback(() => key ? referenceDrafts.get(key) ?? emptyReferences : emptyReferences, [key]),
    () => emptyReferences,
  );
  const update = React.useCallback<React.Dispatch<React.SetStateAction<ComposerReference[]>>>((value) => {
    if (!key) { setOrdinary(value); return; }
    const before = referenceDrafts.get(key) ?? emptyReferences;
    // React defines SetStateAction as a value-or-updater union, not wire data.
    // oxlint-disable-next-line anti-slop/no-runtime-typeof
    const after = typeof value === 'function' ? value(before) : value;
    if (after === before) return;
    if (after.length) referenceDrafts.set(key, after); else referenceDrafts.delete(key);
    for (const listener of referenceListeners) listener();
  }, [key]);
  return [key ? references : ordinary, update] as const;
}
export const TWINCAT_ANALYSIS_PROFILE = 'twincat-readonly';
export const TWINCAT_EDITOR_PROFILE = 'twincat-editor';
export const TWINCAT_DEBUG_PROFILE = 'twincat-debug';
const privilegedProfiles = z.enum([TWINCAT_EDITOR_PROFILE, TWINCAT_DEBUG_PROFILE]);
const editorProfileRequest = z.object({ sessionId: z.string().min(1).max(256), enabled: z.boolean(),
  profile: privilegedProfiles.optional(), requestId: z.number().int().positive().optional() }).strict();
let editorProfile: { sessionId: string; directory: string; project: string; profile: z.infer<typeof privilegedProfiles> } | null = null;
let requestedProfile: { id: number; profile: string; sessionId: string; directory: string; project: string } | null = null;
let nextProfileRequestId = 0;
let profileRequestTimer: ReturnType<typeof setTimeout> | undefined;
const profileRequestTimeoutMs = 120_000;
function notifyTwinCatProfile() { for (const listener of embeddingListeners) listener(); }
function revokeTwinCatEditor() {
  if (!editorProfile) return;
  editorProfile = null;
  notifyTwinCatProfile();
}
// A stored chat or agent name cannot grant Editor; only the native host can
// enable it for the currently committed view and selection.
export function twinCatActiveProfile() {
  return editorProfile && matchesProfileContext(editorProfile) ? editorProfile.profile : TWINCAT_ANALYSIS_PROFILE;
}
export const twinCatProfilePending = () => requestedProfile !== null;
export function requireTwinCatProfileSettled() {
  if (twinCatProfilePending()) throw Object.assign(
    new Error(useI18nStore.getState().dictionary['chat.twincat.profile.switching']),
    { code: 'twincat_profile_switch_pending' as const },
  );
}
export function isSelectedTwinCatChat(sessionId: string, directory?: string | null) {
  const current = useSessionUIStore.getState();
  return current.currentSessionId === sessionId
    && (directory == null || normalizePath(current.currentSessionDirectory ?? '') === normalizePath(directory));
}
export function checkTwinCatSendTarget(sessionId: string, directory?: string | null, requireSelectedChat = false) {
  requireTwinCatProfileSettled();
  const selected = isSelectedTwinCatChat(sessionId, directory);
  if (requireSelectedChat && !selected) {
    throw Object.assign(new Error('TwinCAT chat selection changed before dispatch'), { code: 'twincat_chat_selection_changed' as const });
  }
  return selected;
}
export function isTwinCatSendDeferred(error: unknown) {
  return error instanceof Error && 'code' in error
    && (error.code === 'twincat_chat_selection_changed' || error.code === 'twincat_profile_switch_pending');
}
export function twinCatProfileForChat(sessionId: string, directory?: string | null) {
  return isSelectedTwinCatChat(sessionId, directory) ? twinCatActiveProfile() : TWINCAT_ANALYSIS_PROFILE;
}
export function TwinCatProfilePicker() {
  const profile = React.useSyncExternalStore(subscribeTwinCatEmbedding, twinCatActiveProfile, () => TWINCAT_ANALYSIS_PROFILE);
  const pending = React.useSyncExternalStore(subscribeTwinCatEmbedding, twinCatProfilePending, () => false);
  const eligible = React.useSyncExternalStore(subscribeTwinCatEmbedding, canRequestTwinCatProfile, () => false);
  const dictionary = useI18nStore(state => state.dictionary);
  const heading = dictionary['chat.twincat.profile.label'];
  const options = [
    { value: TWINCAT_ANALYSIS_PROFILE, label: dictionary['chat.twincat.profile.analysis'] },
    { value: TWINCAT_EDITOR_PROFILE, label: 'Editor' },
    { value: TWINCAT_DEBUG_PROFILE, label: 'Debug' },
  ];
  const label = options.find(option => option.value === profile)?.label;
  return React.createElement(DropdownMenu, null,
    React.createElement(DropdownMenuTrigger, {
      type: 'button',
      'aria-label': heading,
      className: 'flex h-8 min-w-0 cursor-pointer select-none items-center gap-1 transition-colors hover:opacity-70 focus-visible:outline focus-visible:outline-ring',
    },
    React.createElement('span', { className: 'typography-meta min-w-0 truncate font-medium text-foreground', role: 'status' },
      pending ? dictionary['chat.twincat.profile.switching'] : label),
    React.createElement(Icon, { name: 'arrow-down-s', className: 'size-3 shrink-0 text-muted-foreground' })),
    React.createElement(DropdownMenuContent, { side: 'top', align: 'end', className: 'w-[min(180px,calc(100vw-2rem))]' },
      React.createElement(DropdownMenuLabel, { className: 'typography-ui-header font-semibold text-foreground' }, heading),
      React.createElement(DropdownMenuRadioGroup, { value: profile, onValueChange: requestTwinCatProfile },
        options.map(option => React.createElement(DropdownMenuRadioItem, {
          key: option.value,
          value: option.value,
          disabled: option.value !== TWINCAT_ANALYSIS_PROFILE && !eligible,
          className: 'typography-meta',
        }, option.label))),
      !eligible && React.createElement('p', { className: 'px-2 py-1 typography-micro text-muted-foreground' },
        dictionary['chat.twincat.profile.projectRequired'])));
}
function currentProfileContext() {
  const selected = useSessionUIStore.getState();
  if (!active || !view?.ready || !view.sessionId || !view.directory || !context
    || selected.currentSessionId !== view.sessionId || selected.currentSessionDirectory !== view.directory
    || context.sessionId !== view.sessionId || !context.value?.project) return null;
  return { sessionId: view.sessionId, directory: view.directory, project: context.projectKey };
}
function matchesProfileContext(target: { sessionId: string; directory: string; project: string }) {
  const current = currentProfileContext();
  return current?.sessionId === target.sessionId && current.directory === target.directory && current.project === target.project;
}
function clearProfileRequest() {
  if (!requestedProfile) return;
  requestedProfile = null;
  clearTimeout(profileRequestTimer);
  notifyTwinCatProfile();
}
function canRequestTwinCatProfile() { return currentProfileContext() !== null; }
export function requestTwinCatProfile(profile: string) {
  if (profile !== TWINCAT_ANALYSIS_PROFILE && !privilegedProfiles.safeParse(profile).success) return;
  const target = currentProfileContext();
  if (!target || profile === (requestedProfile?.profile ?? twinCatActiveProfile())) return;
  const id = ++nextProfileRequestId;
  requestedProfile = { id, profile, ...target };
  revokeTwinCatEditor();
  clearTimeout(profileRequestTimer);
  profileRequestTimer = setTimeout(() => {
    if (requestedProfile?.id !== id) return;
    revokeTwinCatEditor();
    clearProfileRequest();
  }, profileRequestTimeoutMs);
  notifyTwinCatProfile();
}
// One native profile policy for menus, keyboard cycling and @ mentions.
// Keep the original catalog and stored profile IDs intact.
export function twinCatSelectableAgents<T extends { name: string }>(agents: T[]): T[] {
  return active ? agents.filter(agent => agent.name === twinCatActiveProfile()) : agents;
}
export function twinCatSelectablePanels<T extends { mode: string }>(panels: T[]): T[] {
  return active ? panels.filter(panel => panel.mode === 'context') : panels;
}
export function subscribeTwinCatEmbedding(listener: () => void) {
  embeddingListeners.add(listener);
  return () => { embeddingListeners.delete(listener); };
}
let startup: { ready: boolean } | null = null;
export function publishTwinCatStartup(ready: boolean) {
  const value = { ready };
  startup = value;
  return () => { if (startup === value) startup = null; };
}
type ExportLoader = (target: { directory: string; sessionID: string }) => Promise<SessionMessageRecord[] | null>;
let threadExport: { sessionId: string | null; title: string | null; load: ExportLoader } | null = null;
export function publishTwinCatThreadExport(sessionId: string | null, title: string | null, load: ExportLoader) {
  const value = { sessionId, title, load };
  threadExport = value;
  return () => { if (threadExport === value) threadExport = null; };
}
let context: { sessionId: string; projectKey: string; value: Context | null } | null = null;
let composer: { sessionId: string | null; references: Reference[]; clearFollowing: () => void } | null = null;
let view: { sessionId: string | null; directory: string | null; ready: boolean } | null = null;
const sent = new Map<string, Reference[]>();
const snapshotData = z.object({ revision: z.string().regex(/^[a-f0-9]{64}$/), reference: z.string().min(1).max(128).optional(),
  mode: z.enum(['following', 'manual']).optional() });

export function twinCatAttachmentMode(item: ComposerReference): 'following' | 'manual' | null {
  if (item.kind !== 'guest' || item.providerId !== 'twincat-assistant') return null;
  return snapshotData.safeParse(item.data).data?.mode ?? (item.id === 'Aktiver Baustein' ? 'following' : 'manual');
}

// Explicitly freeze the displayed revision. Revalidate it against the current
// composer so a click cannot pin a newer, unseen automatic snapshot instead.
export function keepTwinCatReference(items: ComposerReference[], reference: ComposerReference): ComposerReference[] {
  if (reference.kind !== 'guest' || twinCatAttachmentMode(reference) !== 'following') return items;
  const expected = snapshotData.safeParse(reference.data).data;
  if (!expected?.reference) return items;
  const current = items.find(item => item.kind === 'guest' && item.providerId === reference.providerId && item.id === reference.id);
  if (current?.kind !== 'guest') return items;
  const data = snapshotData.safeParse(current.data).data;
  if (data?.revision !== expected.revision || data?.reference !== expected.reference) return items;
  const kept: ComposerReference = { ...current, id: expected.reference,
    data: { revision: expected.revision, reference: expected.reference, mode: 'manual' } };
  return [...items.filter(item => item.kind !== 'guest' || item.providerId !== reference.providerId
    || (item.id !== reference.id && item.id !== expected.reference)), kept];
}

function references(items: readonly ComposerReference[]): Reference[] {
  return items.flatMap(item => {
    if (item.kind !== 'guest' || item.providerId !== 'twincat-assistant') return [];
    const data = snapshotData.safeParse(item.data).data;
    return [{ id: item.id, title: item.title, revision: data?.revision ?? null, reference: data?.reference ?? item.id,
      mode: twinCatAttachmentMode(item) ?? 'manual' }];
  });
}

// Observe the original composer's references; never recreate its chips or send
// path. Calls on the other OpenChamber surfaces are inert until the native host
// explicitly activates this source contract.
export function publishTwinCatComposer(sessionId: string | null, items: readonly ComposerReference[], clearFollowing: () => void) {
  const value = { sessionId, references: references(items), clearFollowing };
  composer = value;
  return () => { if (composer === value) composer = null; };
}

// Report the main chat's committed selection and its existing history-loader
// result. A mounted composer alone can still belong to the previous session.
export function publishTwinCatView(sessionId: string | null, directory: string | null, ready: boolean) {
  const eligible = canRequestTwinCatProfile();
  const value = { sessionId, directory, ready };
  view = value;
  if (requestedProfile && (sessionId !== requestedProfile.sessionId || directory !== requestedProfile.directory)) clearProfileRequest();
  if (editorProfile && (sessionId !== editorProfile.sessionId || directory !== editorProfile.directory)) revokeTwinCatEditor();
  if (editorProfile || eligible !== canRequestTwinCatProfile()) notifyTwinCatProfile();
  return () => {
    if (view !== value) return;
    const wasEligible = canRequestTwinCatProfile();
    view = null;
    clearProfileRequest();
    revokeTwinCatEditor();
    if (wasEligible) notifyTwinCatProfile();
  };
}

export function recordTwinCatSubmission(sessionId: string, items: readonly ComposerReference[]) {
  if (!active) return;
  const submitted = references(items);
  if (submitted.length) sent.set(sessionId, submitted);
}

export function twinCatContextTexts(sessionId: string | null, profile = sessionId === context?.sessionId
  ? twinCatActiveProfile() : TWINCAT_ANALYSIS_PROFILE, includeContext = true): string[] {
  if (!active || !sessionId) return [];
  const value = includeContext && context?.sessionId === sessionId ? context.value : null;
  const label = profile === TWINCAT_DEBUG_PROFILE ? 'Debug' : profile === TWINCAT_EDITOR_PROFILE ? 'Editor' : 'Analysis';
  return ['TwinCAT IDE context, metadata only. Observed when this message was composed.\n'
    + 'Active profile: ' + label + ' (' + profile + '). '
    + 'Earlier messages in this conversation may have used a different profile. Current tool permissions follow the active profile.'
    + (value ? '\nPLC project: ' + value.project + '\nActive object: ' + (value.object ?? 'none')
    + '\nObject kind: ' + (value.objectKind ?? 'unknown') + '\nOwning POU: ' + (value.owner ?? 'unknown')
    + '\nMember: ' + (value.member || 'POU body')
    + '\nTree path: ' + (value.treePath ?? 'none') + '\nSource observation: ' + (value.sourceKind ?? 'unknown') : '')
    + '\nThis metadata contains no source code and does not verify the unsaved editor buffer. '
    + 'Attached snapshots and saved files returned by engineering tools are separate sources.'];
}

export const twinCatSource = {
  version: 1,
  nativeIntro() { return useI18nStore.getState().dictionary['twincat.chat.empty.intro']; },
  get stores() { return { ui: useUIStore, sessions: useSessionUIStore, input: useInputStore, config: useConfigStore, locale: useI18nStore }; },
  activate() {
    if (active) return;
    active = true;
    setTwinCatSendPolicy({
      isSelectedChat: isSelectedTwinCatChat,
      requireSettled: requireTwinCatProfileSettled,
      profileForChat: twinCatProfileForChat,
      contextTexts: twinCatContextTexts,
    });
    // Invalidate immediately when the live selection moves, even before the
    // deferred view commits or if it later returns to the same conversation.
    useSessionUIStore.subscribe(state => {
      if (requestedProfile && (state.currentSessionId !== requestedProfile.sessionId
        || state.currentSessionDirectory !== requestedProfile.directory)) clearProfileRequest();
      if (editorProfile && (state.currentSessionId !== editorProfile.sessionId
        || state.currentSessionDirectory !== editorProfile.directory)) revokeTwinCatEditor();
    });
    for (const listener of embeddingListeners) listener();
  },
  peekProfileRequest(sessionId: string) {
    const request = requestedProfile;
    return request && request.sessionId === sessionId && matchesProfileContext(request)
      ? { id: request.id, profile: request.profile } : null;
  },
  ackProfileRequest(sessionId: string, id: number) {
    if (requestedProfile?.sessionId !== sessionId || requestedProfile.id !== id) return false;
    clearProfileRequest();
    return true;
  },
  activeProfile: twinCatActiveProfile,
  contextTexts: twinCatContextTexts,
  presentationReady() {
    const selection = useSessionUIStore.getState();
    return startup?.ready === true && useConfigStore.getState().isInitialized && view?.ready === true
      && view.sessionId === (selection.currentSessionId ?? null)
      && view.directory === (selection.currentSessionDirectory ?? null);
  },
  setEditorProfile(request: z.infer<typeof editorProfileRequest>) {
    const parsed = editorProfileRequest.safeParse(request);
    if (!parsed.success) { revokeTwinCatEditor(); return false; }
    request = parsed.data;
    if (!request.enabled) {
      if (editorProfile?.sessionId === request.sessionId) revokeTwinCatEditor();
      return true;
    }
    const profile = request.profile ?? TWINCAT_EDITOR_PROFILE;
    const target = currentProfileContext();
    if (!target || target.sessionId !== request.sessionId || !requestedProfile
      || requestedProfile.id !== request.requestId || requestedProfile.profile !== profile
      || !matchesProfileContext(requestedProfile)) return false;
    editorProfile = { ...target, profile };
    notifyTwinCatProfile(); return true;
  },
  setContext(sessionId: string, value: Context | null, projectKey = value?.project) {
    const before = twinCatActiveProfile();
    const eligible = canRequestTwinCatProfile();
    context = projectKey ? { sessionId, projectKey, value } : null;
    if (requestedProfile && (sessionId !== requestedProfile.sessionId || context?.projectKey !== requestedProfile.project)) clearProfileRequest();
    if (editorProfile && (sessionId !== editorProfile.sessionId || context?.projectKey !== editorProfile.project)) revokeTwinCatEditor();
    if (before !== twinCatActiveProfile() || eligible !== canRequestTwinCatProfile()) notifyTwinCatProfile();
    if (!value?.reference && composer?.sessionId === sessionId) composer.clearFollowing();
  },
  clearFollowing(sessionId: string) { if (composer?.sessionId === sessionId) composer.clearFollowing(); },
  async exportThread(sessionId: string, directory: string) {
    const exporter = threadExport;
    if (!active || !exporter || exporter.sessionId !== sessionId) throw new Error('Thread export is not ready');
    // Reuse the original history loader, including pagination and runtime-switch
    // checks. The rendered/virtualized transcript is never the export source.
    const records = await exporter.load({ sessionID: sessionId, directory });
    if (!records) throw new Error('Thread history could not be loaded');
    return { filename: buildExportFilename(exporter.title), markdown: formatSessionAsMarkdown(records, exporter.title) };
  },
  composer(sessionId: string) {
    return { observed: composer?.sessionId === sessionId, references: composer?.sessionId === sessionId ? composer.references : [],
      submitted: sent.get(sessionId) ?? [] };
  },
  async sessions(directory: string, includeArchived = false) {
    const items = [];
    const status = await opencodeClient.getHostSessionStatusSnapshot();
    const seen = new Set<string>();
    let cursor: string | undefined;
    do {
      const page = await opencodeClient.listSessionsPage({ directory, cursor, limit: 200 });
      for (const item of page.sessions) {
        // Populate the original action store, including archived records, so
        // rename/archive/restore retain upstream reconciliation and semantics.
        if (item.directory.replace(/\\/g, '/').toLowerCase() !== directory.replace(/\\/g, '/').toLowerCase()) throw new Error('Foreign project history');
        useGlobalSessionsStore.getState().upsertSession(item);
        registerSessionDirectory(item.id, directory);
        if (!item.parentID && (includeArchived || !item.time?.archived)) {
          const state = status?.sessions[item.id]?.status;
          items.push({ id: item.id, updated: item.time.updated, directory, title: item.title.slice(0, 512),
            archived: Boolean(item.time.archived), busy: Boolean(state && !['idle', 'completed', 'error', 'aborted'].includes(state)) });
        }
      }
      if (page.cursor.next && seen.has(page.cursor.next)) throw new Error('Project history cursor did not advance');
      if (page.cursor.next) seen.add(page.cursor.next);
      cursor = page.cursor.next;
      if (items.length > 20000) throw new Error('Project history exceeds the native metadata limit');
    } while (cursor);
    return items;
  },
  async manageSession(request: { id: string; directory: string; action: 'rename' | 'generate-title' | 'archive' | 'restore'; title?: string }, assertCurrent: () => void) {
    assertCurrent();
    const session = await opencodeClient.getSession(request.id, request.directory);
    const actual = resolveGlobalSessionDirectory(session);
    if (session.parentID || (actual && actual.replace(/\\/g, '/').toLowerCase() !== request.directory.replace(/\\/g, '/').toLowerCase()))
      throw new Error('Chat belongs to another project');
    assertCurrent();
    useGlobalSessionsStore.getState().upsertSession(session);
    registerSessionDirectory(session.id, request.directory);
    if (request.action === 'generate-title') {
      const exporter = threadExport;
      if (!active || !exporter) throw titleFailure('history_unavailable');
      if (session.time.archived) throw titleFailure('archived');
      const status = await opencodeClient.getHostSessionStatusSnapshot();
      const state = status?.sessions[request.id]?.status;
      if (!status || (state && !['idle', 'completed', 'error', 'aborted'].includes(state))) throw titleFailure('chat_busy');
      assertCurrent();
      const runtimeKey = getRuntimeKey();
      await runSessionTitleGeneration({ runtimeKey, directory: request.directory, sessionID: request.id }, async signal => {
        await generateAndSaveSessionTitle({
          signal,
          prepare: async () => {
            assertCurrent();
            const records = await exporter.load({ directory: request.directory, sessionID: request.id }).catch(() => { throw titleFailure('history_unavailable'); });
            assertCurrent();
            if (!records) throw titleFailure('history_unavailable');
            const turns = collectSessionTitleTurns(records, session.revert?.messageID);
            if (!turns.length) throw titleFailure('empty_history');
            return { session, turns };
          },
          generate: async (turns, signal) => {
            for (let attempt = 0; attempt < 2; attempt++) {
              assertCurrent(); signal.throwIfAborted();
              let title: string;
              try { title = await generateSessionTitle({ turns, directory: request.directory, sessionID: request.id, signal }); }
              catch (error) { signal.throwIfAborted(); throw titleFailure(error instanceof z.ZodError ? 'invalid_title' : 'provider_failed'); }
              try { return validateTwinCatGeneratedTitle(title); }
              catch (error) { if (attempt === 1) throw error; }
            }
            throw titleFailure('invalid_title');
          },
          readSession: () => { assertCurrent(); return opencodeClient.getSession(request.id, request.directory); },
          saveTitle: (title, signal) => { assertCurrent(); return updateSessionTitle(request.id, title, { directory: request.directory, expectedRuntimeKey: runtimeKey, signal }); },
        });
      });
    }
    else if (request.action === 'rename') await updateSessionTitle(request.id, request.title!, { directory: request.directory });
    else if (request.action === 'archive') {
      const status = await opencodeClient.getHostSessionStatusSnapshot();
      const state = status?.sessions[request.id]?.status;
      if (!status || (state && !['idle', 'completed', 'error', 'aborted'].includes(state))) throw new Error('Chat is active or its status is unknown');
      assertCurrent();
      if (!await archiveSession(request.id)) throw new Error('Archive failed');
    } else if (request.action === 'restore') {
      if (!await unarchiveSession(request.id)) throw new Error('Restore failed');
    } else throw new Error('Unsupported chat action');
    return true;
  },
};
