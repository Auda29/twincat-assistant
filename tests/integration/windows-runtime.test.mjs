import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { initializeState, inspectState } from '../../src/state.mjs';

test('real pinned Windows runtime boots with protected UI, isolated session and owned shutdown', {
  skip: process.platform !== 'win32', timeout: 100000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-windows-upstream-'));
  const password = randomBytes(32).toString('hex');
  const net = createServer();
  net.listen(0, '127.0.0.1'); await once(net, 'listening');
  const port = net.address().port; await new Promise(resolve => net.close(resolve));
  const origin = `http://127.0.0.1:${port}`;
  const state = join(root, 'state');
  let child;
  let closed;
  try {
    await initializeState(state, {}); // no provider, corpus, MCP or customer project
    child = spawn(process.execPath, [resolve('src/cli.mjs'), 'start', '--state', state, '--port', String(port)], {
      cwd: resolve('.'), env: { ...process.env, OPENCHAMBER_UI_PASSWORD: password },
      stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
    });
    closed = once(child, 'close');
    let logs = '';
    child.stdout.on('data', data => { logs = (logs + data).slice(-100000); });
    child.stderr.on('data', data => { logs = (logs + data).slice(-100000); });
    const deadline = Date.now() + 65000;
    let response;
    while (Date.now() < deadline && child.exitCode === null) {
      try { response = await fetch(origin, { signal: AbortSignal.timeout(1000) }); if (response.status === 200) break; } catch { }
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    if (response?.status !== 200) {
      // Fixture contains no source or provider credential; redact its UI secret even on failure.
      console.error(logs.replaceAll(password, '[REDACTED]'));
    }
    assert.equal(response?.status, 200, `Pinned Windows runtime did not become ready (exit ${child.exitCode})`);
    assert.match(await response.text(), /<html|<!doctype/i);
    assert.equal((await fetch(`${origin}/api/settings`)).status, 401);
    assert.ok(!logs.includes(password), 'UI password leaked into process logs');
    const login = await fetch(`${origin}/auth/session`, { method: 'POST',
      headers: { 'content-type': 'application/json', origin }, body: JSON.stringify({ password }) });
    assert.equal(login.status, 200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    const session = await fetch(`${origin}/api/session`, { method: 'POST', headers: { cookie, 'content-type': 'application/json' },
      body: JSON.stringify({ location: { directory: join(state, 'workspace') }, title: 'Synthetic Windows bootstrap', agent: 'twincat-readonly' }) });
    assert.equal(session.status, 200);
    const status = await inspectState(state);
    assert.equal(status.modelCallsEnabled, false);
    assert.equal(status.nativeEngineering, 'unavailable');
    assert.ok(await readFile(join(state, 'launcher.lock')));
  } finally {
    if (child?.exitCode === null && child.signalCode === null) child.kill(); // deliberate Node-owner death, not an IDE shutdown
    if (closed) await closed;
    const deadline = Date.now() + 10000;
    let listening = true;
    while (listening && Date.now() < deadline) {
      try { await fetch(origin, { signal: AbortSignal.timeout(500) }); } catch { listening = false; }
      if (listening) await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.equal(listening, false, 'Owned upstream listener survived Node-owner death');
    // Windows can release directory handles shortly after the owned listener
    // exits. Retry only cleanup; the independent shutdown assertion above must
    // still pass, and a persistent lock remains a test failure.
    await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
  }
});
