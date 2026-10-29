import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { mkdtemp, cp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { once } from 'node:events';

test('source runtime retains distribution sessions, durable inputs and provider login on a separate data copy', {
  skip: process.platform !== 'win32', timeout: 150000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-session-copy-'));
  const password = randomBytes(32).toString('hex');
  const owners = [];
  async function start(state, mode) {
    const socket = createServer(); socket.listen(0, '127.0.0.1'); await once(socket, 'listening');
    const port = socket.address().port; await new Promise(resolve => socket.close(resolve));
    const child = spawn(process.execPath, [resolve('src/native-chat.mjs')], { windowsHide: true,
      env: { ...process.env, TWINCAT_ASSISTANT_RUNTIME: mode }, stdio: ['pipe', 'pipe', 'pipe'] });
    const closed = once(child, 'close');
    const owner = { child, closed }; owners.push(owner);
    child.stderr.resume();
    let output = '';
    const ready = new Promise((resolve, reject) => {
      child.stdout.on('data', value => { output += value; if (output.includes('\n')) resolve(JSON.parse(output.split('\n')[0])); });
      child.once('close', () => reject(new Error(mode + ' runtime failed before readiness')));
    });
    child.stdin.write(JSON.stringify({ state, port, password }) + '\n');
    assert.equal((await ready).status, 'ready');
    const origin = `http://127.0.0.1:${port}`;
    const login = await fetch(origin + '/auth/session', { method: 'POST', headers: { 'content-type': 'application/json', origin }, body: JSON.stringify({ password }) });
    assert.equal(login.status, 200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    const headers = { cookie, 'content-type': 'application/json' };
    const request = (path, options = {}) => fetch(origin + path, { ...options, headers: { ...headers, ...options.headers }, signal: AbortSignal.timeout(15000) });
    const deadline = Date.now() + 30000;
    while (true) {
      const response = await request('/api/session?limit=1');
      if (response.ok) break;
      if (Date.now() > deadline) throw new Error('Session store did not become ready');
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    return { ...owner, request };
  }
  try {
    const original = join(root, 'distribution-state');
    const baseline = await start(original, 'distribution');
    const created = await baseline.request('/api/session', { method: 'POST', body: JSON.stringify({
      title: 'Existing conversation', agent: 'twincat-readonly', location: { directory: join(original, 'workspace') },
    }) });
    assert.equal(created.status, 200);
    const session = (await created.json()).data;
    const admitted = await baseline.request(`/api/session/${session.id}/prompt`, { method: 'POST',
      body: JSON.stringify({ text: 'Synthetic retained user message', resume: false }) });
    assert.equal(admitted.status, 200);
    const inbox = await (await baseline.request(`/api/session/${session.id}/inbox`)).json();
    assert.match(JSON.stringify(inbox), /Synthetic retained user message/);
    const providerHeaders = { 'x-opencode-directory': encodeURIComponent(join(original, 'workspace')), 'x-opencode-directory-encoding': 'uri' };
    assert.equal((await baseline.request('/api/integration/openai/connect/key', { method: 'POST', headers: providerHeaders,
      body: JSON.stringify({ key: 'synthetic-not-a-provider-credential' }) })).status, 204);
    baseline.child.stdin.end(); await baseline.closed;
    const copy = join(root, 'source-state');
    await cp(original, copy, { recursive: true, errorOnExist: true, force: false });
    const source = await start(copy, 'source');
    const restored = await (await source.request(`/api/session/${session.id}`)).json();
    assert.equal(restored.data.id, session.id);
    assert.equal(restored.data.title, session.title);
    assert.deepEqual(await (await source.request(`/api/session/${session.id}/inbox`)).json(), inbox);
    const integrations = await (await source.request('/api/integration', { headers: providerHeaders })).json();
    assert.ok(integrations.data.some(item => item.id === 'openai' && item.connections.length > 0));
    source.child.stdin.end(); await source.closed;
  } finally {
    for (const owner of owners) {
      if (owner.child.exitCode === null) owner.child.stdin.end();
      await owner.closed;
    }
    await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
  }
});
