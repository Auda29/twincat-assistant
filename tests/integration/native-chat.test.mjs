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
import { configureNativeModel } from '../../src/native-chat-model.mjs';
import { consentProviders } from '../../src/embedded-chat.mjs';

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
    // Isolated fake credential: exercise catalog visibility without calling a model.
    const providerHeaders = { cookie, 'content-type': 'application/json',
      'x-opencode-directory': encodeURIComponent(join(root, 'state', 'workspace')), 'x-opencode-directory-encoding': 'uri' };
    const integrationDeadline = Date.now() + 20000;
    let registration;
    while (Date.now() < integrationDeadline) {
      const response = await fetch(`${origin}/api/integration`, { headers: providerHeaders });
      if (response.ok) registration = (await response.json()).data;
      if (registration?.some(item => item.id === 'openai')) break;
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    assert.ok(registration?.some(item => item.id === 'openai'), 'isolated provider integration did not become ready');
    const connected = await fetch(`${origin}/api/integration/openai/connect/key`, {
      method: 'POST', headers: providerHeaders, body: JSON.stringify({ key: 'synthetic-not-a-real-api-key' }),
    });
    assert.equal(connected.status, 204);
    const integrations = (await (await fetch(`${origin}/api/integration`, { headers: providerHeaders })).json()).data;
    const providers = (await (await fetch(`${origin}/api/provider`, { headers: providerHeaders })).json()).data;
    const models = (await (await fetch(`${origin}/api/model`, { headers: providerHeaders })).json()).data;
    assert.ok(integrations.some(item => item.id === 'openai' && item.connections.length > 0));
    assert.ok(!providers.some(item => item.id === 'openai'), 'deny policy must still hide the provider before consent');
    assert.equal(models.filter(item => item.providerID === 'openai').length, 0);
    assert.ok(consentProviders(integrations).some(item => item.id === 'openai'), 'connected provider must be independently selectable for explicit consent');
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
    await configureNativeModel({ state: join(root, 'state'), provider: 'openai', acceptSourceDisclosure: true });
    assert.equal((await inspectState(join(root, 'state'))).settings.model, undefined, 'provider consent must not invent a model');
    assert.equal((await fetch(`${origin}/api/debug/location`, { method: 'DELETE', headers: providerHeaders })).status, 204);
    const catalogDeadline = Date.now() + 20000;
    let availableModels = [];
    while (Date.now() < catalogDeadline) {
      availableModels = (await (await fetch(`${origin}/api/model`, { headers: providerHeaders })).json()).data;
      if (availableModels.some(model => model.providerID === 'openai' && model.enabled)) break;
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    assert.ok(availableModels.some(model => model.providerID === 'openai' && model.enabled), 'explicit provider-only consent must unlock the real upstream model catalog');
    await configureNativeModel({ state: join(root, 'state'), model: 'openai/synthetic-test', acceptSourceDisclosure: true });
    const directoryHeaders = { cookie, 'x-opencode-directory': encodeURIComponent(join(root, 'state', 'workspace')), 'x-opencode-directory-encoding': 'uri' };
    const refresh = await fetch(`${origin}/api/debug/location`, { method: 'DELETE', headers: directoryHeaders });
    assert.equal(refresh.status, 204, 'explicit model setup could not reload its isolated location');
    const modelDeadline = Date.now() + 20000;
    let runtimeConfig;
    while (Date.now() < modelDeadline) {
      const response = await fetch(`${origin}/api/config`, { headers: { cookie, 'x-opencode-directory': encodeURIComponent(join(root, 'state', 'workspace')), 'x-opencode-directory-encoding': 'uri' } });
      const entries = await response.json();
      runtimeConfig = Object.assign({}, ...entries.filter(entry => entry.info).map(entry => entry.info));
      if (runtimeConfig.model && typeof runtimeConfig.model === 'object') runtimeConfig.model = runtimeConfig.model.providerID + '/' + runtimeConfig.model.model;
      if (runtimeConfig.model === 'openai/synthetic-test') break;
      await new Promise(resolve => setTimeout(resolve, 300));
    }
    assert.equal(runtimeConfig.model, 'openai/synthetic-test', 'running upstream did not adopt the explicitly consented model policy; keys=' + Object.keys(runtimeConfig).join(','));
    assert.deepEqual(runtimeConfig.permissions[0], { action: '*', resource: '*', effect: 'deny' });
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
