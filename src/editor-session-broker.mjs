import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { randomBytes, timingSafeEqual } from 'node:crypto';
import { isAbsolute, win32 } from 'node:path';
import { EDITOR_TOOL_NAMES } from './editor-tools.mjs';
export { EDITOR_TOOL_NAMES } from './editor-tools.mjs';
const TOOLS = new Set(EDITOR_TOOL_NAMES);
const FRAME_LIMIT = 512 * 1024;
const REQUEST_LIMIT = 256 * 1024;
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/;
const CHAT_ID = /^[a-zA-Z0-9_:-]{1,160}$/;
const unavailable = () => new Error('Editor session unavailable; do not retry a submitted mutation.');
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function exact(value, keys) {
  if (!object(value) || Object.keys(value).length !== keys.length || keys.some(key => !Object.hasOwn(value, key)))
    throw new Error('Invalid Editor session binding');
}
function absolute(value) {
  if (typeof value !== 'string' || !value || value.length > 2048 || value.includes('\0') || !isAbsolute(value))
    throw new Error('Invalid Editor session path');
}

export function validateEditorBootstrap(value) {
  exact(value, ['kind', 'protocolVersion', 'pipeName', 'sessionId', 'capability', 'expected']);
  const expected = value.expected;
  exact(expected, ['idePid', 'ideStartedUtc', 'solutionFile', 'projectFile', 'contextEpoch']);
  if (value.kind !== 'twincat-assistant-engineering-bootstrap' || value.protocolVersion !== 1
      || !UUID.test(value.sessionId) || value.sessionId === '00000000-0000-0000-0000-000000000000'
      || !/^\\\\\.\\pipe\\twincat-assistant-engineering-[a-f0-9]{64}$/.test(value.pipeName)
      || typeof value.capability !== 'string' || !/^[a-f0-9]{64}$/.test(value.capability)
      || !Number.isSafeInteger(expected.idePid) || expected.idePid <= 0
      || typeof expected.ideStartedUtc !== 'string' || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?Z$/.test(expected.ideStartedUtc)
      || !Number.isFinite(Date.parse(expected.ideStartedUtc))
      || typeof expected.contextEpoch !== 'string' || !expected.contextEpoch || expected.contextEpoch.length > 256
      || expected.contextEpoch.includes('\0')) throw new Error('Invalid Editor session binding');
  for (const path of [expected.solutionFile, expected.projectFile]) {
    if (typeof path !== 'string' || path.length > 2048 || path.includes('\0') || !win32.isAbsolute(path)
        || win32.normalize(path) !== path) throw new Error('Invalid Editor session binding');
  }
  if (Buffer.byteLength(JSON.stringify(value) + '\n') > 4096) throw new Error('Invalid Editor session binding');
  return value;
}

// The only process receiving the host capability is this owned engineering child.
// Neither the Core MCP process nor an HTTP client receives that capability.
async function ownedEngineering({ node, entrypoint, bootstrap, workspace }, signal) {
  const env = {};
  for (const key of ['SystemRoot', 'WINDIR', 'TEMP', 'TMP']) if (process.env[key]) env[key] = process.env[key];
  const child = spawn(node, [entrypoint, '--host-bootstrap-stdin'], {
    cwd: workspace, env, shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'ignore'],
  });
  let stopped = false, pending, buffered = Buffer.alloc(0), nextId = 0, queue = Promise.resolve(), queued = 0;
  let onStopped = () => {};
  const closed = new Promise(resolve => child.once('close', resolve));
  function stop() {
    if (stopped) return;
    stopped = true;
    pending?.reject(unavailable()); pending = undefined;
    buffered.fill(0); buffered = Buffer.alloc(0);
    child.stdin.destroy(); child.stdout.destroy(); child.kill();
    signal?.removeEventListener('abort', stop);
    onStopped();
  }
  child.on('error', stop); child.on('close', stop); child.stdin.on('error', stop);
  child.stdout.on('data', bytes => {
    if (stopped) return;
    buffered = Buffer.concat([buffered, bytes]);
    if (buffered.length > FRAME_LIMIT) { stop(); return; }
    const newline = buffered.indexOf(10);
    if (newline < 0) return;
    const selected = pending; pending = undefined;
    if (!selected || newline !== buffered.length - 1) { stop(); selected?.reject(unavailable()); return; }
    try {
      const text = new TextDecoder('utf-8', { fatal: true }).decode(buffered.subarray(0, newline));
      buffered = Buffer.alloc(0);
      selected.resolve(JSON.parse(text));
    } catch { selected.reject(unavailable()); stop(); }
  });
  signal?.addEventListener('abort', stop, { once: true });
  if (signal?.aborted) stop();
  async function exchange(value, limit = REQUEST_LIMIT) {
    if (stopped || pending) throw unavailable();
    const bytes = Buffer.from(JSON.stringify(value) + '\n');
    if (bytes.length > limit) { bytes.fill(0); throw unavailable(); }
    let timer;
    try {
      const result = new Promise((resolve, reject) => { pending = { resolve, reject }; });
      timer = setTimeout(stop, 60000);
      child.stdin.write(bytes, () => bytes.fill(0));
      return await result;
    } finally { clearTimeout(timer); }
  }
  async function request(method, params) {
    const id = ++nextId;
    const value = await exchange({ jsonrpc: '2.0', id, method, params });
    if (!object(value) || Object.keys(value).length !== 3 || value.jsonrpc !== '2.0' || value.id !== id
        || !Object.hasOwn(value, 'result') || !object(value.result)) { stop(); throw unavailable(); }
    return value.result;
  }
  try {
    const ready = await exchange(bootstrap, 4096);
    exact(ready, ['kind', 'protocolVersion', 'sessionId']);
    if (ready.kind !== 'twincat-assistant-engineering-ready' || ready.protocolVersion !== 1 || ready.sessionId !== bootstrap.sessionId)
      throw unavailable();
    const initialized = await request('initialize', { protocolVersion: '2024-11-05', capabilities: {},
      clientInfo: { name: 'twincat-assistant-editor-owner', version: '1.0.0' } });
    if (initialized.protocolVersion !== '2024-11-05' || !object(initialized.capabilities)) throw unavailable();
    child.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');
    const catalog = await request('tools/list', {});
    if (!Array.isArray(catalog.tools) || catalog.nextCursor !== undefined || catalog.tools.length !== TOOLS.size
        || new Set(catalog.tools.map(tool => tool.name)).size !== TOOLS.size
        || catalog.tools.some(tool => !TOOLS.has(tool.name) || !object(tool.inputSchema))) throw unavailable();
    return {
      catalog: { tools: catalog.tools },
      isClosed: () => stopped,
      onClose(callback) { onStopped = callback; if (stopped) callback(); },
      call(name, args) {
        if (stopped || queued >= 8) return Promise.reject(unavailable());
        queued++;
        const result = queue.then(() => request('tools/call', { name, arguments: args }));
        queue = result.catch(() => {});
        return result.finally(() => queued--);
      },
      async close() { stop(); await closed; },
    };
  } catch { stop(); await closed; throw unavailable(); }
}

// This token authenticates only the bounded MCP frontend. Core receives it in
// an ephemeral runtime override. It is never a host capability or tool argument.
export async function startEditorSession(options, signal) {
  for (const path of [options.node, options.entrypoint, options.workspace]) absolute(path);
  if (typeof options.sessionId !== 'string' || !CHAT_ID.test(options.sessionId)) throw new Error('Invalid Editor chat session');
  validateEditorBootstrap(options.bootstrap);
  const backend = await ownedEngineering(options, signal);
  const token = randomBytes(32).toString('hex');
  const expectedAuthorization = Buffer.from('Bearer ' + token);
  let closing = false, closeWork, origin, active = 0;
  const sockets = new Set();
  const server = createServer({ maxHeaderSize: 8192, requestTimeout: 65000, headersTimeout: 5000 }, (req, res) => {
    const finish = (status, value) => {
      if (res.destroyed) return;
      res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      res.end(value === undefined ? undefined : JSON.stringify(value));
    };
    const auth = Buffer.from(typeof req.headers.authorization === 'string' ? req.headers.authorization : '');
    if (closing || backend.isClosed()) { finish(503); return; }
    if (req.headers.origin !== undefined || req.headers.host !== new URL(origin).host || req.url !== '/mcp'
        || auth.length !== expectedAuthorization.length || !timingSafeEqual(auth, expectedAuthorization)) { finish(403); return; }
    if (req.method !== 'POST') { finish(405); return; }
    if (!(req.headers['content-type'] ?? '').startsWith('application/json') || active >= 16) { finish(400); return; }
    active++;
    let bytes = 0, chunks = [];
    req.on('data', chunk => {
      bytes += chunk.length;
      if (bytes > REQUEST_LIMIT) { chunks = []; req.destroy(); } else chunks.push(chunk);
    });
    req.on('error', () => {});
    res.once('close', () => active--);
    req.on('end', async () => {
      let id = null;
      try {
        const message = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
        chunks = [];
        if (!object(message) || message.jsonrpc !== '2.0' || typeof message.method !== 'string'
            || Object.keys(message).some(key => !['jsonrpc', 'id', 'method', 'params'].includes(key))) throw unavailable();
        if (!Object.hasOwn(message, 'id')) {
          if (message.method !== 'notifications/initialized' && message.method !== 'notifications/cancelled') throw unavailable();
          finish(202); return;
        }
        id = message.id;
        if (!(typeof id === 'string' && id.length <= 160) && !Number.isSafeInteger(id)) throw unavailable();
        let result;
        switch (message.method) {
          case 'initialize':
            result = { protocolVersion: '2024-11-05', capabilities: { tools: {} }, serverInfo: { name: 'twincat-assistant-editor', version: '1.0.0' } };
            break;
          case 'ping': result = {}; break;
          case 'tools/list': result = backend.catalog; break;
          case 'tools/call': {
            const params = message.params;
            if (!object(params) || !TOOLS.has(params.name) || !object(params.arguments ?? {})
                || params._meta?.['ai.opencode/sessionID'] !== options.sessionId
                || Object.keys(params).some(key => !['name', 'arguments', '_meta'].includes(key))) throw unavailable();
            result = await backend.call(params.name, params.arguments ?? {});
            break;
          }
          default: throw unavailable();
        }
        if (closing || backend.isClosed()) throw unavailable();
        finish(200, { jsonrpc: '2.0', id, result });
      } catch { finish(200, { jsonrpc: '2.0', id, error: { code: -32603, message: unavailable().message } }); }
    });
  });
  server.maxConnections = 24;
  server.on('connection', socket => { sockets.add(socket); socket.once('close', () => sockets.delete(socket)); });
  const close = () => {
    if (closeWork) return closeWork;
    if (closing) return Promise.resolve();
    closing = true;
    signal?.removeEventListener('abort', close);
    expectedAuthorization.fill(0);
    for (const socket of sockets) socket.destroy();
    closeWork = Promise.all([backend.close(), new Promise(resolve => server.close(resolve))]).then(() => {});
    return closeWork;
  };
  backend.onClose(() => { void close(); });
  try {
    if (closing || backend.isClosed()) throw unavailable();
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    if (closing) { await new Promise(resolve => server.close(resolve)); throw unavailable(); }
    origin = 'http://127.0.0.1:' + server.address().port;
    signal?.addEventListener('abort', close, { once: true });
    if (signal?.aborted || backend.isClosed()) { await close(); throw unavailable(); }
    return Object.freeze({ sessionId: options.sessionId, workspace: options.workspace, url: origin + '/mcp', close,
      configuration() {
        if (closing) throw unavailable();
        return { type: 'remote', url: origin + '/mcp', oauth: false, protocol: 'legacy', codemode: false,
          headers: { Authorization: 'Bearer ' + token }, timeout: { startup: 15000, catalog: 15000, execution: 65000 } };
      },
    });
  } catch { await close(); throw unavailable(); }
}

// Call only from the trusted native-chat owner, never from browser JavaScript.
// Core's experimental MCP add/remove API changes location-memory overrides and
// does not write a configuration file or credential database.
export async function registerEditorSession({ origin, password, workspace, broker }, signal) {
  const url = new URL(origin);
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || url.username || url.password
      || url.pathname !== '/' || url.search || url.hash || workspace !== broker.workspace
      || typeof password !== 'string' || password.length < 12) throw new Error('Invalid protected Editor runtime');
  absolute(workspace);
  let login;
  try {
    login = await fetch(new URL('/auth/session', url), { method: 'POST', redirect: 'error',
      headers: { 'content-type': 'application/json', origin: url.origin }, body: JSON.stringify({ password }),
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15000)]) : AbortSignal.timeout(15000) });
  } catch { await broker.close(); throw unavailable(); }
  await login.body?.cancel();
  if (!login.ok) { await broker.close(); throw unavailable(); }
  const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  if (!cookie) { await broker.close(); throw unavailable(); }
  const endpoint = new URL('/api/experimental/mcp/editor', url);
  // OpenChamber forwards the directory header to Core's exact location context.
  const headers = { cookie, origin: url.origin, 'content-type': 'application/json',
    'x-opencode-directory': encodeURIComponent(workspace), 'x-opencode-directory-encoding': 'uri' };
  async function remove() {
    await broker.close();
    try {
      const response = await fetch(endpoint, { method: 'DELETE', headers, redirect: 'error', signal: AbortSignal.timeout(15000) });
      await response.body?.cancel();
      if (!response.ok && response.status !== 404) throw unavailable();
    } catch { throw new Error('Editor revoked; runtime catalog cleanup unavailable.'); }
  }
  try {
    const response = await fetch(endpoint, { method: 'PUT', headers, redirect: 'error',
      body: JSON.stringify({ config: broker.configuration() }),
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15000)]) : AbortSignal.timeout(15000) });
    await response.body?.cancel();
    if (!response.ok) throw unavailable();
    return remove;
  } catch { await remove().catch(() => {}); throw unavailable(); }
}
