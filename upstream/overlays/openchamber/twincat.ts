import { useUIStore } from '@/stores/useUIStore';
import { useConfigStore } from '@/stores/useConfigStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { useInputStore } from '@/sync/input-store';
import { useI18nStore } from '@/lib/i18n';
import { opencodeClient } from '@/lib/opencode/client';
import { z } from 'zod';
import type { ComposerReference } from '@/components/chat/composer/composerReferences';

type Reference = { id: string; title: string; revision: string | null; reference: string };
type Context = { project: string; object: string | null; treePath: string | null; sourceKind: string | null; reference?: string | null };
let active = false;
let context: { sessionId: string; value: Context } | null = null;
let composer: { sessionId: string | null; references: Reference[] } | null = null;
const sent = new Map<string, Reference[]>();
const snapshotData = z.object({ revision: z.string().regex(/^[a-f0-9]{64}$/), reference: z.string().max(128).optional() });

function references(items: readonly ComposerReference[]): Reference[] {
  return items.flatMap(item => {
    if (item.kind !== 'guest' || item.providerId !== 'twincat-assistant') return [];
    const data = snapshotData.safeParse(item.data).data;
    return [{ id: item.id, title: item.title, revision: data?.revision ?? null, reference: data?.reference ?? item.id }];
  });
}

// Observe the original composer's references; never recreate its chips or send
// path. Calls on the other OpenChamber surfaces are inert until the native host
// explicitly activates this source contract.
export function publishTwinCatComposer(sessionId: string | null, items: readonly ComposerReference[]) {
  const value = { sessionId, references: references(items) };
  composer = value;
  return () => { if (composer === value) composer = null; };
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
    + '\nTree path: ' + (value.treePath ?? 'none') + '\nSource observation: ' + (value.sourceKind ?? 'unknown')
    + '\nThis metadata contains no source code and does not verify the unsaved editor buffer. '
    + 'Attached snapshots and saved files returned by engineering tools are separate sources.'];
}

export const twinCatSource = {
  version: 1,
  stores: { ui: useUIStore, sessions: useSessionUIStore, input: useInputStore, config: useConfigStore, locale: useI18nStore },
  activate() { active = true; },
  setContext(sessionId: string, value: Context | null) { context = value ? { sessionId, value } : null; },
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
