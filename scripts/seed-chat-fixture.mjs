// Test-only history. Never open an operator database or copy customer messages.
import { Database } from 'bun:sqlite';
import { basename, dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { lstatSync } from 'node:fs';
import { randomUUID } from 'node:crypto';

const [rootValue, sessionId] = process.argv.slice(2);
if (!rootValue || !sessionId || !/^ses_[a-zA-Z0-9_-]+$/.test(sessionId)) throw new Error('Private fixture session required');
const root = resolve(rootValue);
if (dirname(root).toLowerCase() !== resolve(tmpdir()).toLowerCase() || !basename(root).startsWith('tc-chat-integration-'))
  throw new Error('Only a direct integration-fixture directory is allowed');
let target = root;
for (const part of ['', 'state', 'data', 'opencode', 'opencode.db']) {
  if (part) target = join(target, part);
  if (lstatSync(target).isSymbolicLink()) throw new Error('Redirected fixture path refused');
}
const db = new Database(target, { readwrite: true, create: false });
try {
  db.exec('PRAGMA busy_timeout = 5000');
  if (!db.query('SELECT id FROM session_v2 WHERE id = ?').get(sessionId)
      || db.query('SELECT COUNT(*) AS n FROM session_message WHERE session_id = ?').get(sessionId).n !== 0)
    throw new Error('Only a new empty fixture session may be seeded');
  const now = Date.now() - 60_000;
  const messages = [
    ['user', { time: { created: now }, text: 'Prüfe den PRG-Aufruf und den Rückzug.', files: [], agents: [], skills: [] }],
    ['assistant', { time: { created: now + 100, streamed: now + 200, completed: now + 200 },
      agent: 'twincat-readonly', model: { providerID: 'fixture', id: 'fixture-model' },
      content: [{ type: 'text', text: 'Synthetischer vorhandener Chat: PRG-Aufruf und Rückzug geprüft.' }],
      finish: 'stop', cost: 0, tokens: { input: 2, output: 3, reasoning: 0, cache: { read: 0, write: 0 } } }],
  ];
  db.transaction(() => {
    const insert = db.query('INSERT INTO session_message (id, session_id, type, seq, time_created, time_updated, data) VALUES (?, ?, ?, ?, ?, ?, ?)');
    for (const [index, [type, data]] of messages.entries())
      insert.run('msg_' + randomUUID().replaceAll('-', ''), sessionId, type, index + 1, now + index * 100, now + index * 100, JSON.stringify(data));
    db.query('UPDATE session_v2 SET title = ?, time_updated = ?, time_idle = ?, idle_outcome = ? WHERE id = ?')
      .run('Historischer Projektchat', now + 200, now + 200, 'succeeded', sessionId);
  })();
  console.log('Seeded two synthetic completed messages in the private integration fixture.');
} finally { db.close(); }
