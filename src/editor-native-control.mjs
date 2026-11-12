import { isAbsolute, join, resolve } from 'node:path';
import { startEditorSession, registerEditorSession, validateEditorBootstrap } from './editor-session-broker.mjs';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const CHAT_ID = /^[a-zA-Z0-9_:-]{1,160}$/;
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const samePath = (left, right) => process.platform === 'win32'
  ? resolve(left).toLowerCase() === resolve(right).toLowerCase() : resolve(left) === resolve(right);
function absolute(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 2048 && !value.includes('\0') && isAbsolute(value);
}
function exact(value, keys) {
  return object(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

// JSON.parse alone accepts duplicate object keys. Reject them at every depth so
// the native owner and receiver cannot interpret the same command differently.
export function parseEditorControl(line) {
  let index = 0;
  const whitespace = () => { while (index < line.length && ' \t\r\n'.includes(line[index])) index++; };
  function string() {
    if (line[index] !== '"') throw new Error('Invalid editor control');
    const start = index++;
    while (index < line.length) {
      if (line[index] === '\\') { index += 2; continue; }
      if (line[index++] === '"') return JSON.parse(line.slice(start, index));
    }
    throw new Error('Invalid editor control');
  }
  function value(depth) {
    if (depth > 16) throw new Error('Invalid editor control');
    whitespace();
    if (line[index] === '{') {
      index++; whitespace(); const keys = new Set();
      if (line[index] === '}') { index++; return; }
      for (;;) {
        whitespace(); const key = string();
        if (keys.has(key)) throw new Error('Invalid editor control');
        keys.add(key); whitespace(); if (line[index++] !== ':') throw new Error('Invalid editor control');
        value(depth + 1); whitespace(); const separator = line[index++];
        if (separator === '}') return;
        if (separator !== ',') throw new Error('Invalid editor control');
      }
    }
    if (line[index] === '[') {
      index++; whitespace(); if (line[index] === ']') { index++; return; }
      for (;;) {
        value(depth + 1); whitespace(); const separator = line[index++];
        if (separator === ']') return;
        if (separator !== ',') throw new Error('Invalid editor control');
      }
    }
    if (line[index] === '"') { string(); return; }
    const start = index;
    while (index < line.length && !',]} \t\r\n'.includes(line[index])) index++;
    if (start === index) throw new Error('Invalid editor control');
  }
  value(0); whitespace();
  if (index !== line.length) throw new Error('Invalid editor control');
  return JSON.parse(line);
}

export function validateEditorControl(command, { packaged, applicationRoot }) {
  if (!object(command) || !UUID.test(command.requestId) || command.requestId === '00000000-0000-0000-0000-000000000000')
    throw new Error('Invalid editor control');
  if (command.kind === 'editor.revoke') {
    if (!exact(command, ['kind', 'requestId'])) throw new Error('Invalid editor control');
    return command;
  }
  if (command.kind !== 'editor.bind' || !exact(command, ['kind', 'requestId', 'bootstrap', 'workspace', 'sessionId', 'entrypoint'])
      || !absolute(command.workspace) || !absolute(command.entrypoint) || typeof command.sessionId !== 'string' || !CHAT_ID.test(command.sessionId))
    throw new Error('Invalid editor control');
  validateEditorBootstrap(command.bootstrap);
  if (packaged && !samePath(command.entrypoint, join(applicationRoot, 'runtime', 'mcp', 'engineering', 'packages', 'mcp', 'dist', 'index.js')))
    throw new Error('Invalid editor control');
  return command;
}

// The queue owns at most one live registration. Once closing begins it aborts
// in-progress startup immediately, then waits for broker-first revocation.
export function createEditorNativeControl(options) {
  const { origin, password, node, packaged, applicationRoot, report } = options;
  if (!absolute(node) || !absolute(applicationRoot) || (packaged && !samePath(node, join(applicationRoot, 'runtime', 'node', 'node.exe'))))
    throw new Error('Invalid editor runtime');
  const start = options.startBroker ?? startEditorSession;
  const register = options.register ?? registerEditorSession;
  const lifetime = new AbortController();
  let registration, queue = Promise.resolve(), count = 0, closing = false, closeWork;
  const maxQueued = 8;
  async function revoke() {
    const remove = registration;
    registration = undefined;
    if (remove) await remove();
  }
  function enqueue(command) {
    validateEditorControl(command, { packaged, applicationRoot });
    if (closing || count >= maxQueued) throw new Error('Invalid editor control');
    count++;
    const work = queue.then(async () => {
      if (closing) return;
      try {
        await revoke();
        if (command.kind === 'editor.revoke') { if (!closing) report({ status: 'editor-revoked', requestId: command.requestId }); return; }
        const broker = await start({ node, entrypoint: command.entrypoint, bootstrap: command.bootstrap,
          workspace: command.workspace, sessionId: command.sessionId }, lifetime.signal);
        try {
          if (closing) { await broker.close(); return; }
          registration = await register({ origin, password, workspace: command.workspace, broker }, lifetime.signal);
          if (closing) { await revoke(); return; }
          report({ status: 'editor-ready', requestId: command.requestId });
        } catch (error) { await broker.close().catch(() => {}); throw error; }
      } catch {
        if (!closing) report({ status: 'editor-failed', requestId: command.requestId });
      }
    }).finally(() => { count--; });
    queue = work.catch(() => {});
  }
  function close() {
    if (closeWork) return closeWork;
    closing = true;
    lifetime.abort();
    closeWork = queue.then(revoke).catch(() => {});
    return closeWork;
  }
  options.signal?.addEventListener('abort', close, { once: true });
  if (options.signal?.aborted) void close();
  return Object.freeze({ enqueue, async close() { options.signal?.removeEventListener('abort', close); await close(); } });
}

// The native owner sends UTF-8 newline frames. Check each line's bytes before
// decoding, including when many lines arrive in a single read.
export function readBoundedControlLines(stream, onLine, onClose, limit = 32768) {
  let pending = Buffer.alloc(0), closed = false;
  const finish = error => {
    if (closed) return;
    closed = true; pending.fill(0); pending = Buffer.alloc(0);
    onClose(error);
  };
  stream.on('data', chunk => {
    if (closed) return;
    let start = 0;
    try {
      while (start < chunk.length) {
        const newline = chunk.indexOf(10, start);
        const end = newline < 0 ? chunk.length : newline;
        if (pending.length + end - start > limit) throw new Error('Control line too large');
        const previous = pending;
        pending = Buffer.concat([previous, chunk.subarray(start, end)]);
        previous.fill(0);
        if (newline < 0) break;
        let frame = pending;
        if (frame.length && frame[frame.length - 1] === 13) frame = frame.subarray(0, frame.length - 1);
        const line = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(frame);
        pending.fill(0); pending = Buffer.alloc(0);
        onLine(line);
        start = newline + 1;
      }
    } catch { finish(new Error('Invalid control line')); }
  });
  stream.on('end', () => finish(pending.length ? new Error('Incomplete control line') : undefined));
  stream.on('error', () => finish(new Error('Control input failed')));
  stream.on('close', () => finish());
  return () => finish();
}
