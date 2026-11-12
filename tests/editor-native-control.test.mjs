import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { PassThrough } from 'node:stream';
import { resolve, join } from 'node:path';
import { createServer } from 'node:http';
import { createEditorNativeControl, parseEditorControl, readBoundedControlLines, validateEditorControl } from '../src/editor-native-control.mjs';
import { runNativeChatControl } from '../src/native-chat.mjs';

const bootstrap = () => ({ kind: 'twincat-assistant-engineering-bootstrap', protocolVersion: 1,
  sessionId: '12345678-1234-1234-1234-123456789abc', capability: 'a'.repeat(64),
  pipeName: '\\\\.\\pipe\\twincat-assistant-engineering-' + 'b'.repeat(64),
  expected: { idePid: 1234, ideStartedUtc: '2026-11-12T10:00:00.1234567Z', solutionFile: 'C:\\fixture\\Fixture.sln',
    projectFile: 'C:\\fixture\\PLC\\PLC.plcproj', contextEpoch: 'epoch-1' } });
const command = (id = '11111111-1111-4111-8111-111111111111') => ({ kind: 'editor.bind', requestId: id,
  bootstrap: bootstrap(), workspace: resolve('.'), sessionId: 'ses_editor_1', entrypoint: resolve('tests/fixtures/editor-engineering-child.mjs') });
const opts = { packaged: false, applicationRoot: resolve('.') };
const tick = () => new Promise(resolve => setImmediate(resolve));
async function until(predicate) { for (let i = 0; i < 150; i++) { if (predicate()) return; await new Promise(resolve => setTimeout(resolve, 20)); } throw new Error('Timed out'); }

test('editor control rejects ambiguous, invalid and arbitrary packaged entrypoints', () => {
  assert.deepEqual(validateEditorControl(parseEditorControl(JSON.stringify(command())), opts), command());
  for (const line of [JSON.stringify({ ...command(), extra: 1 }), '{"kind":"editor.revoke","kind":"editor.revoke","requestId":"11111111-1111-4111-8111-111111111111"}',
    JSON.stringify({ ...command(), bootstrap: { ...bootstrap(), capability: 'bad' } }), JSON.stringify({ ...command(), workspace: 'relative' }),
    JSON.stringify({ kind: 'unexpected', requestId: command().requestId })]) {
    assert.throws(() => validateEditorControl(parseEditorControl(line), opts));
  }
  assert.throws(() => validateEditorControl(command(), { ...opts, packaged: true }));
  assert.doesNotThrow(() => validateEditorControl({ ...command(), entrypoint: join(opts.applicationRoot, 'runtime', 'mcp', 'engineering', 'packages', 'mcp', 'dist', 'index.js') }, { ...opts, packaged: true }));
});

test('control lines enforce strict UTF-8 and independent byte limits with multiple frames', async () => {
  const stream = new PassThrough(), lines = [], closes = [];
  readBoundedControlLines(stream, line => lines.push(line), error => closes.push(error), 8);
  stream.write(Buffer.from('12345678\n1234')); stream.write(Buffer.from('567\r\n')); stream.end();
  await once(stream, 'close');
  assert.deepEqual(lines, ['12345678', '1234567']);
  assert.equal(closes.length, 1);
  assert.equal(closes[0], undefined);
  for (const bytes of [Buffer.from('123456789\n'), Buffer.from([0xc3, 0x28, 0x0a]), Buffer.from('partial')]) {
    const input = new PassThrough(); let error;
    readBoundedControlLines(input, () => {}, e => { error = e; }, 8);
    input.end(bytes); await once(input, 'close'); assert.ok(error);
  }
});

test('binds serialize, revoke prior registration first, and never disclose capability in acknowledgments', async () => {
  const events = [], reports = [];
  const owner = createEditorNativeControl({ ...opts, origin: 'http://127.0.0.1:1234', password: 'synthetic-password', node: process.execPath,
    report: item => reports.push(item),
    startBroker: async value => { events.push('start:' + value.sessionId); return { workspace: value.workspace, close: async () => events.push('close') }; },
    register: async ({ broker }) => { events.push('register'); return async () => { await broker.close(); events.push('delete'); }; },
  });
  const second = { ...command('22222222-2222-4222-8222-222222222222'), sessionId: 'ses_editor_2' };
  owner.enqueue(command()); owner.enqueue(second);
  owner.enqueue({ kind: 'editor.revoke', requestId: '33333333-3333-4333-8333-333333333333' });
  await until(() => reports.length === 3);
  assert.deepEqual(events, ['start:ses_editor_1', 'register', 'close', 'delete', 'start:ses_editor_2', 'register', 'close', 'delete']);
  assert.deepEqual(reports.map(x => x.status), ['editor-ready', 'editor-ready', 'editor-revoked']);
  assert.ok(!JSON.stringify(reports).includes(bootstrap().capability));
  await owner.close();
  assert.throws(() => owner.enqueue(command()));
});

test('real broker is registered through protected runtime and revoked on owner abort', { timeout: 15000 }, async () => {
  const events = [], reports = [];
  const server = createServer(async (req, res) => {
    let body = ''; for await (const chunk of req) body += chunk;
    events.push({ method: req.method, url: req.url, body });
    if (req.url === '/auth/session') { res.writeHead(200, { 'set-cookie': 'fixture=session; HttpOnly' }).end('{}'); return; }
    res.writeHead(204).end();
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const controller = new AbortController();
  const owner = createEditorNativeControl({ ...opts, origin: `http://127.0.0.1:${server.address().port}`, password: 'synthetic-password',
    node: process.execPath, signal: controller.signal, report: item => reports.push(item) });
  try {
    owner.enqueue(command());
    await until(() => reports.length === 1);
    assert.equal(reports[0].status, 'editor-ready');
    assert.deepEqual(events.map(x => x.method), ['POST', 'PUT']);
    assert.ok(!JSON.stringify(events).includes(bootstrap().capability));
    controller.abort(); await owner.close();
    assert.deepEqual(events.map(x => x.method), ['POST', 'PUT', 'DELETE']);
    assert.ok(!JSON.stringify(reports).includes(bootstrap().capability));
  } finally { await owner.close(); await new Promise(resolve => server.close(resolve)); }
});

test('failed startup reports only request ID and closes broker', async () => {
  const reports = [], events = [];
  const owner = createEditorNativeControl({ ...opts, origin: 'http://127.0.0.1:1234', password: 'synthetic-password', node: process.execPath,
    report: item => reports.push(item), startBroker: async () => ({ close: async () => events.push('closed') }),
    register: async () => { throw new Error(bootstrap().capability); },
  });
  owner.enqueue(command()); await until(() => reports.length === 1);
  assert.deepEqual(reports, [{ status: 'editor-failed', requestId: command().requestId }]);
  assert.deepEqual(events, ['closed']); await owner.close();
});


test('abort during a pending bind closes the late broker without publishing it', async () => {
  const events = [], reports = [], controller = new AbortController();
  let finish;
  const owner = createEditorNativeControl({ ...opts, origin: 'http://127.0.0.1:1234', password: 'synthetic-password', node: process.execPath,
    signal: controller.signal, report: item => reports.push(item),
    startBroker: () => new Promise(resolve => { finish = () => resolve({ close: async () => events.push('closed') }); }),
    register: async () => { events.push('published'); },
  });
  owner.enqueue(command()); await until(() => Boolean(finish)); controller.abort(); finish();
  await owner.close(); assert.deepEqual(events, ['closed']); assert.deepEqual(reports, []);
});

test('the sequential queue is bounded while a bind is pending', async () => {
  let finish;
  const owner = createEditorNativeControl({ ...opts, origin: 'http://127.0.0.1:1234', password: 'synthetic-password', node: process.execPath,
    report: () => {}, startBroker: () => new Promise(resolve => { finish = () => resolve({ close: async () => {} }); }),
    register: async () => async () => {},
  });
  owner.enqueue(command()); await until(() => Boolean(finish));
  for (let i = 0; i < 7; i++) owner.enqueue({ kind: 'editor.revoke', requestId: command().requestId });
  assert.throws(() => owner.enqueue({ kind: 'editor.revoke', requestId: command().requestId }));
  const closed = owner.close(); finish(); await closed;
});


test('native stdin startup, bind, revoke and EOF drive the real broker lifecycle', { timeout: 15000 }, async () => {
  const events = [], reports = [], input = new PassThrough(); let failures = 0;
  const server = createServer(async (req, res) => {
    for await (const chunk of req) { void chunk; }
    events.push(req.method);
    if (req.url === '/auth/session') { res.writeHead(200, { 'set-cookie': 'fixture=session; HttpOnly' }).end('{}'); return; }
    res.writeHead(204).end();
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  try {
    runNativeChatControl({ input, report: value => reports.push(value), onFailure: () => { failures++; },
      startChat: async (request, signal, report, options) => {
        options.onReady({ packaged: false }); report({ status: 'ready', port: request.port });
        if (!signal.aborted) await new Promise(resolve => signal.addEventListener('abort', resolve, { once: true }));
      },
    });
    input.write(JSON.stringify({ state: resolve('synthetic-state'), port: server.address().port, password: 'synthetic-password' }) + '\n');
    await until(() => reports.length === 1);
    input.write(JSON.stringify(command()) + '\n');
    await until(() => reports.length === 2);
    assert.equal(reports[1].status, 'editor-ready');
    const revocation = { kind: 'editor.revoke', requestId: '33333333-3333-4333-8333-333333333333' };
    input.write(JSON.stringify(revocation) + '\n');
    await until(() => reports.length === 3);
    assert.equal(reports[2].status, 'editor-revoked');
    assert.deepEqual(events, ['POST', 'PUT', 'DELETE']);
    input.end(); await once(input, 'close');
    assert.equal(failures, 0);
    assert.ok(!JSON.stringify(reports).includes(bootstrap().capability));
  } finally { input.destroy(); await new Promise(resolve => server.close(resolve)); }
});


test('unknown extra stdin control aborts the runtime without echoing credentials', async () => {
  const reports = [], input = new PassThrough(); let failures = 0, stopped = false;
  runNativeChatControl({ input, report: value => reports.push(value), onFailure: () => { failures++; },
    startChat: async (request, signal, report, options) => {
      options.onReady({ packaged: false }); report({ status: 'ready', port: request.port });
      if (!signal.aborted) await new Promise(resolve => signal.addEventListener('abort', resolve, { once: true }));
      stopped = true;
    },
  });
  input.write(JSON.stringify({ state: resolve('synthetic-state'), port: 1234, password: 'synthetic-password' }) + '\n');
  await until(() => reports.length === 1);
  input.write('{"kind":"unexpected","requestId":"11111111-1111-4111-8111-111111111111","secret":"synthetic-private"}\n');
  await until(() => stopped);
  assert.ok(failures > 0); assert.equal(reports.length, 1); assert.ok(!JSON.stringify(reports).includes('synthetic-private'));
});
