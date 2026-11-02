import { useUIStore } from '@/stores/useUIStore';
import { useConfigStore } from '@/stores/useConfigStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { useInputStore } from '@/sync/input-store';
import { useI18nStore } from '@/lib/i18n';
import { opencodeClient } from '@/lib/opencode/client';
import { z } from 'zod';
import type { ComposerReference } from '@/components/chat/composer/composerReferences';
import { buildExportFilename, formatSessionAsMarkdown, type SessionMessageRecord } from '@/lib/exportSession';
import React from 'react';
import { getChatDraftIdentityKey, type ChatDraftIdentity } from '@/lib/chatDraftPersistence';
import { useGlobalSessionsStore, resolveGlobalSessionDirectory } from '@/stores/useGlobalSessionsStore';
import { archiveSession, unarchiveSession, updateSessionTitle } from '@/sync/session-actions';
import { registerSessionDirectory } from '@/sync/sync-refs';

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
// One native profile policy for menus, keyboard cycling and @ mentions.
// Keep the original catalog and stored profile IDs intact.
export function twinCatSelectableAgents<T extends { name: string }>(agents: T[]): T[] {
  return active ? agents.filter(agent => agent.name === TWINCAT_ANALYSIS_PROFILE) : agents;
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
let context: { sessionId: string; value: Context } | null = null;
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
  const value = { sessionId, directory, ready };
  view = value;
  return () => { if (view === value) view = null; };
}

export function recordTwinCatSubmission(sessionId: string, items: readonly ComposerReference[]) {
  if (!active) return;
  const submitted = references(items);
  if (submitted.length) sent.set(sessionId, submitted);
}

export function twinCatContextTexts(sessionId: string | null): string[] {
  if (!active || !sessionId || context?.sessionId !== sessionId) return [];
  const value = context.value;
  return ['TwinCAT IDE context, metadata only. Observed when this message was composed.\n'
    + 'PLC project: ' + value.project + '\nActive object: ' + (value.object ?? 'none')
    + '\nObject kind: ' + (value.objectKind ?? 'unknown') + '\nOwning POU: ' + (value.owner ?? 'unknown')
    + '\nMember: ' + (value.member || 'POU body')
    + '\nTree path: ' + (value.treePath ?? 'none') + '\nSource observation: ' + (value.sourceKind ?? 'unknown')
    + '\nThis metadata contains no source code and does not verify the unsaved editor buffer. '
    + 'Attached snapshots and saved files returned by engineering tools are separate sources.'];
}

export const twinCatSource = {
  version: 1,
  stores: { ui: useUIStore, sessions: useSessionUIStore, input: useInputStore, config: useConfigStore, locale: useI18nStore },
  activate() {
    if (active) return;
    active = true;
    for (const listener of embeddingListeners) listener();
  },
  presentationReady() {
    const selection = useSessionUIStore.getState();
    return startup?.ready === true && useConfigStore.getState().isInitialized && view?.ready === true
      && view.sessionId === (selection.currentSessionId ?? null)
      && view.directory === (selection.currentSessionDirectory ?? null);
  },
  setContext(sessionId: string, value: Context | null) {
    context = value ? { sessionId, value } : null;
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
  async manageSession(request: { id: string; directory: string; action: 'rename' | 'archive' | 'restore'; title?: string }, assertCurrent: () => void) {
    assertCurrent();
    const session = await opencodeClient.getSession(request.id, request.directory);
    const actual = resolveGlobalSessionDirectory(session);
    if (session.parentID || (actual && actual.replace(/\\/g, '/').toLowerCase() !== request.directory.replace(/\\/g, '/').toLowerCase()))
      throw new Error('Chat belongs to another project');
    assertCurrent();
    useGlobalSessionsStore.getState().upsertSession(session);
    registerSessionDirectory(session.id, request.directory);
    if (request.action === 'rename') await updateSessionTitle(request.id, request.title!, { directory: request.directory });
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
