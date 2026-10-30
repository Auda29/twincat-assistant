import { useUIStore } from '@/stores/useUIStore';
import { useConfigStore } from '@/stores/useConfigStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { useInputStore } from '@/sync/input-store';
import { useI18nStore } from '@/lib/i18n';
import { opencodeClient } from '@/lib/opencode/client';
import { z } from 'zod';
import type { ComposerReference } from '@/components/chat/composer/composerReferences';

type Reference = { id: string; title: string; revision: string | null; reference: string; mode: 'following' | 'manual' };
type Context = { project: string; object: string | null; treePath: string | null; sourceKind: string | null; reference?: string | null;
  objectKind?: string | null; owner?: string | null; member?: string | null };
let active = false;
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
  activate() { active = true; },
  presentationReady() {
    const selection = useSessionUIStore.getState();
    return useConfigStore.getState().isInitialized && view?.ready === true
      && view.sessionId === (selection.currentSessionId ?? null)
      && view.directory === (selection.currentSessionDirectory ?? null);
  },
  setContext(sessionId: string, value: Context | null) {
    context = value ? { sessionId, value } : null;
    if (!value?.reference && composer?.sessionId === sessionId) composer.clearFollowing();
  },
  clearFollowing(sessionId: string) { if (composer?.sessionId === sessionId) composer.clearFollowing(); },
  composer(sessionId: string) {
    return { observed: composer?.sessionId === sessionId, references: composer?.sessionId === sessionId ? composer.references : [],
      submitted: sent.get(sessionId) ?? [] };
  },
  async sessions(directory: string) {
    const items = [];
    let cursor: string | undefined;
    do {
      const page = await opencodeClient.listSessionsPage({ directory, cursor, limit: 200 });
      items.push(...page.sessions.filter(item => !item.parentID && !item.time?.archived)
        .map(item => ({ id: item.id, updated: item.time.updated, directory })));
      if (page.cursor.next === cursor && cursor) throw new Error('Project history cursor did not advance');
      cursor = page.cursor.next;
      if (items.length > 20000) throw new Error('Project history exceeds the native metadata limit');
    } while (cursor);
    return items;
  },
};
