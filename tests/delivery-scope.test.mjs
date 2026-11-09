import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, mkdir, mkdtemp, writeFile, access, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { reduceDeliveryFiles } from '../scripts/build-application-package.mjs';
const overlay = JSON.parse(await readFile(new URL('../upstream/overlays/openchamber/changes.json', import.meta.url)));
const server = overlay.find(e => e.file === 'packages/web/server/index.js');
const serverSource = await readFile(new URL('../upstream/overlays/openchamber/' + server.replacement, import.meta.url), 'utf8');
const gate = serverSource.match(/const app = express\(\);[\s\S]*?(?=\s*const serverStartedAt)/)?.[0];
assert.ok(gate, 'product middleware precedes startup routes');

test('delivery boundary refuses excluded routes before proxy, keeps chat and attachment contracts', async () => {
  let middleware;
  Function('express', gate)(() => ({ use(fn) { middleware = fn; } }));
  function request(path) {
    let status = 200, next = false;
    middleware({ path }, { status(value) { status = value; return this; }, json() {} }, () => { next = true; });
    return { status, next };
  }
  for (const path of ['/api/terminal/create', '/api/browser-control/claim', '/api/dev-servers',
    '/api/github/accounts', '/api/git/worktree', '/api/gitlab/accounts', '/api/linear/issues',
    '/api/scheduled-tasks', '/api/routing', '/api/guests', '/api/extensions', '/api/openchamber-control',
    '/api/spaces', '/api/tunnels', '/api/relay', '/api/pairing', '/auth/pairing',
    '/api/dictation', '/api/tts', '/api/notifications/push', '/api/notifications/auto-accept',
    '/api/fs/write', '/api/fs/exec/job', '/api/fs/delete', '/api/fs/rename', '/api/fs/clone', '/api/fs/mkdir', '/api/fs/reveal',
    '/api/opencode/upgrade', '/api/opencode/upgrade-status', '/api/opencode/install-v2',
    '/api/openchamber/update-check', '/api/openchamber/update-install', '/API/FS/WRITE', '/api/fs/%77rite', '/api//fs/write/']) {
    assert.deepEqual(request(path), { status: 404, next: false }, path);
  }
  for (const path of ['/auth/session', '/api/integration', '/api/provider/openai/auth', '/api/config',
    '/api/session/a/interrupt', '/api/session/a/message', '/api/event/ws', '/api/global/event/ws',
    '/api/message-queue', '/api/mcp', '/api/fs/read', '/api/fs/raw', '/api/fs/stat', '/api/fs/upload',
    '/api/openchamber/models-metadata', '/api/twincat/product', '/api/notifications/stream', '/api/fs/writer']) {
    assert.deepEqual(request(path), { status: 200, next: true }, path);
  }
  assert.deepEqual(request('/api/fs/%ZZ'), { status: 400, next: false });
  assert.doesNotMatch(serverSource, /import .*createTerminalRuntime/);
  const startup = await readFile(new URL('../upstream/overlays/openchamber/server/startup-pipeline-runtime.js', import.meta.url), 'utf8');
  assert.doesNotMatch(startup, /createTerminalRuntime|createDictationRuntime/);
});

test('package reduction removes redundant distribution Core and server tests, retains modules and notices', async () => {
  const stage = await mkdtemp(join(tmpdir(), 'assistant-scope-'));
  async function put(path, value = '') { const p = join(stage, path); await mkdir(join(p, '..'), { recursive: true }); await writeFile(p, value); }
  try {
    await put('upstream/node_modules/@openchamber/web/package.json', '{"dependencies":{}}');
    await put('upstream/package-lock.json', '{"packages":{"":{"dependencies":{"@opencode/cli":"2.0.23"}}}}');
    for (const p of ['upstream/node_modules/@opencode/cli/bin/opencode.exe', 'upstream/node_modules/@opencode/cli-windows-x64/opencode.exe',
      'upstream/node_modules/@openchamber/web/server/lib/queue.test.js', 'upstream/node_modules/@openchamber/web/server/lib/queue.js',
      'upstream/node_modules/@openchamber/web/server/LICENSE', 'upstream/node_modules/@opencode/client/index.js', 'runtime/opencode/opencode.exe']) await put(p);
    await reduceDeliveryFiles(stage);
    for (const p of ['upstream/node_modules/@opencode/cli', 'upstream/node_modules/@opencode/cli-windows-x64', 'upstream/node_modules/@openchamber/web/server/lib/queue.test.js']) await assert.rejects(access(join(stage, p)));
    for (const p of ['upstream/node_modules/@openchamber/web/server/lib/queue.js', 'upstream/node_modules/@openchamber/web/server/LICENSE', 'upstream/node_modules/@opencode/client/index.js', 'runtime/opencode/opencode.exe']) await access(join(stage, p));
    await put('upstream/package-lock.json', '{"packages":{"node_modules/new-consumer":{"dependencies":{"@opencode/cli":"2.0.23"}}}}');
    await assert.rejects(reduceDeliveryFiles(stage), /Distribution Core is required/);
  } finally { await rm(stage, { recursive: true, force: true }); }
});
