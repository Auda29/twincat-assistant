import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { mkdtemp, rm, lstat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { inspectState } from '../../src/state.mjs';

test('native owner starts pinned protected chat and stdin EOF stops its process tree', {
  skip: process.platform !== 'win32', timeout: 100000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-native-chat-'));
  const password = randomBytes(32).toString('hex'); // isolated synthetic fixture only
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = server.address().port; await new Promise(resolve => server.close(resolve));
  const origin = `http://127.0.0.1:${port}`;
  let child, closed;
  let output = '', errors = '';
  try {
    child = spawn(process.execPath, [resolve('src/native-chat.mjs')], {
      cwd: resolve('.'), windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, UNRELATED_PROVIDER_CREDENTIAL: 'must-not-be-forwarded' },
    });
    closed = once(child, 'close');
    const ready = new Promise((resolve, reject) => {
      child.stdout.on('data', data => {
        output += data;
        if (output.includes('\n')) {
          try { resolve(JSON.parse(output.split('\n')[0])); } catch { reject(new Error('Invalid control response')); }
        }
      });
      child.once('close', () => reject(new Error('Native launcher exited before readiness')));
    });
    child.stderr.on('data', data => { errors = (errors + data).slice(-32768); });
    child.stdin.write(JSON.stringify({ state: join(root, 'state'), port, password }) + '\n');
    assert.deepEqual(await ready, { status: 'ready', port });
    const duplicate = spawn(process.execPath, [resolve('src/native-chat.mjs')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    const duplicateClosed = once(duplicate, 'close');
    let duplicateOutput = '';
    duplicate.stdout.on('data', data => { duplicateOutput += data; });
    duplicate.stderr.resume();
    duplicate.stdin.write(JSON.stringify({ state: join(root, 'refused-profile'), port, password }) + '\n');
    const [duplicateCode] = await duplicateClosed;
    assert.equal(duplicateCode, 1);
    assert.equal(duplicateOutput.trim(), '{"status":"failed"}');
    await assert.rejects(lstat(join(root, 'refused-profile')), { code: 'ENOENT' });
    assert.equal((await fetch(`${origin}/api/settings`)).status, 401);
    const login = await fetch(`${origin}/auth/session`, {
      method: 'POST', headers: { 'content-type': 'application/json', origin }, body: JSON.stringify({ password }),
    });
    assert.equal(login.status, 200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    // Reproduce the pinned browser's environment bootstrap, not just its login.
    // It uses the legacy root on first load despite the relocated data root.
    for (const directory of [join(root, 'state', 'home', '.config', 'openchamber', 'chats'), join(root, 'state', 'openchamber', 'chats')]) {
      const headers = { cookie, 'x-opencode-directory': encodeURIComponent(directory), 'x-opencode-directory-encoding': 'uri' };
      for (const endpoint of ['/api/config', '/api/location', '/api/form', '/api/permission/request']) {
        const response = await fetch(`${origin}${endpoint}`, { headers, signal: AbortSignal.timeout(20000) });
        assert.equal(response.status, 200, `${endpoint} could not initialize the isolated chat workspace`);
        await response.arrayBuffer();
      }
    }
    const status = await inspectState(join(root, 'state'));
    assert.equal(status.modelCallsEnabled, false);
    assert.equal(status.nativeEngineering, 'unavailable');
    assert.equal(status.corpus, 'not_configured');
    assert.deepEqual(Object.keys(status.settings), []);
    assert.ok(!output.includes(password) && !errors.includes(password), 'credential leaked into launcher output');
    child.stdin.end(); // model the IDE/process owner closing its private control pipe
    await closed;
    await assert.rejects(lstat(join(root, 'state', 'launcher.lock')), { code: 'ENOENT' });
    const deadline = Date.now() + 8000;
    let reachable = true;
    while (Date.now() < deadline) {
      try { await fetch(origin, { signal: AbortSignal.timeout(300) }); }
      catch { reachable = false; break; }
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.equal(reachable, false, 'chat listener survived owner EOF');
  } finally {
    if (child?.exitCode === null) child.kill();
    if (closed) await closed;
    await rm(root, { recursive: true, force: true });
  }
});

test('native control rejects malformed or missing credentials without echoing input', { timeout: 15000 }, async () => {
  const child = spawn(process.execPath, [resolve('src/native-chat.mjs')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const closed = once(child, 'close');
  let output = '', errors = '';
  child.stdout.on('data', value => { output += value; }); child.stderr.on('data', value => { errors += value; });
  child.stdin.end('{"password":"synthetic-private-value"}\n');
  const [code] = await closed;
  assert.equal(code, 1);
  assert.equal(output.trim(), '{"status":"failed"}');
  assert.ok(!errors.includes('synthetic-private-value'));
});
