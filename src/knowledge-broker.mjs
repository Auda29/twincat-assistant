import { createServer } from 'node:net';
import { lstat, readFile, unlink } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { randomBytes, createHash, timingSafeEqual } from 'node:crypto';
import { Client } from '@modelcontextprotocol/client';
import { StdioClientTransport } from '@modelcontextprotocol/client/stdio';
import { Server } from '@modelcontextprotocol/server';
import { serveStdio, StdioServerTransport } from '@modelcontextprotocol/server/stdio';
import { READ_TOOLS } from './config.mjs';
import { atomicJson } from './profile-update.mjs';
import { knowledgeDirectory } from './knowledge-paths.mjs';

const tools = new Set(READ_TOOLS.filter(name => name.startsWith('knowledge_')).map(name => name.slice(10)));

// A public custom Transport negotiates on its existing pipe. The SDK's base
// StdioClientTransport negotiates on a disposable sibling process, which would
// load the entire corpus a second time and can orphan Windows console children.
class KnowledgeTransport {
  constructor(options) {
    this.child = new StdioClientTransport(options);
    this.child.onmessage = message => this.onmessage?.(message);
    this.child.onerror = error => this.onerror?.(error);
    this.child.onclose = () => this.onclose?.();
  }
  start() { return this.child.start(); }
  send(message) { return this.child.send(message); }
  close() { return this.child.close(); }
}

// OpenCode consumes text content. Keep the original structured result as well,
// and expose its complete bounded payload, including topic IDs and sources.
export function knowledgeResultForModel(result) {
  if (result.structuredContent === undefined) return result;
  const text = JSON.stringify(result.structuredContent);
  if (Buffer.byteLength(text) > 1048576) throw new Error('Knowledge result exceeds the adapter limit');
  const content = result.content ?? [];
  // A consumer that takes only the first text block must still get the full
  // result. The upstream text blocks contain only the redundant count summary.
  return { ...result, content: [{ type:'text', text }, ...content.filter(item => item.type !== 'text')] };
}

async function boundedFile(path, missing = false) {
  try {
    const stat = await lstat(path);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 65536) throw new Error('Invalid knowledge state file');
    return await readFile(path, 'utf8');
  } catch (error) { if (missing && error.code === 'ENOENT') return ''; throw error; }
}

export async function startKnowledgeBroker({ root, knowledge, env, cwd, backendCommand }) {
  const base = join(root, 'knowledge'); await knowledgeDirectory(base);
  if (knowledge.bridge !== join(base, 'broker.json')) throw new Error('Unexpected broker descriptor location');
  const token = randomBytes(32).toString('hex'), id = randomBytes(16).toString('hex');
  const endpoint = process.platform === 'win32' ? '\\\\.\\pipe\\tc-knowledge-' + id : join(tmpdir(), 'tc-knowledge-' + id + '.sock');
  const sockets = new Set(), handles = new Set();
  let backend, transport, revision, closing = false, queue = Promise.resolve(), queued = 0;
  const statistics = { starts: 0, calls: 0 };
  async function currentRevision() {
    const config = knowledge.config ? await boundedFile(knowledge.config) : '';
    const managed = knowledge.config === join(base, 'knowledge.toml');
    const active = managed ? await boundedFile(join(base, 'state', 'active.json'), true) : '';
    return createHash('sha256').update(config + '\0' + active).digest('hex');
  }
  async function closeBackend() {
    const previous = backend, previousTransport = transport;
    backend = transport = null; revision = null;
    await previous?.close().catch(() => {});
    await previousTransport?.close().catch(() => {});
  }
  function withBackend(action) {
    if (closing || queued >= 128) return Promise.reject(new Error('Knowledge unavailable or busy'));
    queued++;
    const work = queue.then(async () => {
      if (closing) throw new Error('Knowledge broker stopped');
      const selected = await currentRevision();
      if (!backend || selected !== revision) {
        await closeBackend();
        const command = backendCommand ?? [knowledge.executable, 'serve', '--transport', 'stdio', ...(knowledge.config ? ['--config', knowledge.config] : [])];
        transport = new KnowledgeTransport({ command: command[0], args: command.slice(1), env, cwd, stderr: 'ignore', maxBufferSize: 2097152 });
        const client = new Client({ name: 'twincat-assistant-shared-knowledge', version: '0.1.9' }, {
          capabilities: {}, listMaxPages: 4, versionNegotiation: { mode: { pin: '2026-07-28' } },
        });
        backend = client;
        client.onclose = () => { if (backend === client) backend = null; };
        statistics.starts++;
        try { await client.connect(transport, { timeout: knowledge.startupTimeout ?? 600000 }); revision = selected; }
        catch (error) { await closeBackend(); throw error; }
      }
      return await action(backend);
    });
    queue = work.catch(() => {});
    return work.finally(() => queued--);
  }
  function createFrontend() {
    const server = new Server({ name: 'twincat-assistant-knowledge', version: '0.1.9' }, { capabilities: { tools: {} } });
    server.setRequestHandler('tools/list', () => withBackend(async client => {
      const catalog = await client.listTools({}, { timeout: 15000 });
      const selected = catalog.tools.filter(tool => tools.has(tool.name));
      if (new Set(selected.map(tool => tool.name)).size !== tools.size || selected.length !== tools.size)
        throw new Error('Unexpected Knowledge tool catalog');
      return { tools: selected };
    }));
    server.setRequestHandler('tools/call', (request, ctx) => {
      const params = request.params;
      if (!tools.has(params.name)) throw new Error('Knowledge tool not allowed');
      statistics.calls++;
      return withBackend(async client => knowledgeResultForModel(await client.callTool(
        { name: params.name, arguments: params.arguments ?? {} }, { timeout: 30000, signal: ctx.mcpReq.signal })));
    });
    return server;
  }
  const listener = createServer(socket => {
    if (closing || sockets.size >= 32) { socket.destroy(); return; }
    sockets.add(socket); socket.on('error', () => socket.destroy());
    socket.once('close', () => sockets.delete(socket));
    socket.setTimeout(3000, () => socket.destroy());
    let opening = Buffer.alloc(0);
    const authenticate = bytes => {
      opening = Buffer.concat([opening, bytes]);
      const newline = opening.indexOf(10);
      if (newline < 0) { if (opening.length > 4096) socket.destroy(); return; }
      socket.pause(); socket.removeListener('data', authenticate); socket.setTimeout(0);
      try {
        if (newline > 4096) throw new Error();
        const hello = JSON.parse(opening.subarray(0, newline).toString('utf8'));
        if (typeof hello.token !== 'string' || hello.token.length !== token.length
            || !timingSafeEqual(Buffer.from(hello.token), Buffer.from(token))) throw new Error();
        if (hello.mode === 'ping') { socket.end('ok\n'); return; }
        if (hello.mode === 'status') { socket.end(JSON.stringify({ backendStarts:statistics.starts,backendConnected:Boolean(backend),clients:sockets.size })+'\n'); return; }
        if (hello.mode !== 'mcp') throw new Error();
        const rest = opening.subarray(newline + 1); if (rest.length) socket.unshift(rest);
        const handle = serveStdio(createFrontend, { legacy: 'reject', transport: new StdioServerTransport(socket, socket, { maxBufferSize: 1048576 }), onerror: () => socket.destroy() });
        handles.add(handle);
        socket.once('close', () => { handles.delete(handle); void handle.close().catch(() => {}); });
        socket.resume();
      } catch { socket.destroy(); }
    };
    socket.on('data', authenticate);
  });
  try {
    await new Promise((resolve, reject) => { listener.once('error', reject); listener.listen(endpoint, resolve); });
    await boundedFile(knowledge.bridge, true); // refuse redirected or oversized stale descriptors
    await atomicJson(knowledge.bridge, { version: 1, endpoint, token });
  } catch (error) { listener.close(); throw error; }
  return { statistics, async close() {
    if (closing) return; closing = true;
    for (const socket of sockets) socket.destroy();
    await Promise.allSettled([...handles].map(handle => handle.close()));
    await closeBackend();
    await new Promise(resolve => listener.close(resolve));
    const selected = JSON.parse(await boundedFile(knowledge.bridge, true) || '{}');
    if (selected.token === token) await unlink(knowledge.bridge);
  } };
}
