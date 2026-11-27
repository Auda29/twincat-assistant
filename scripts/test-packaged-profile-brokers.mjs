import assert from 'node:assert/strict';
import { createHash, createHmac, randomBytes, randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { resolve } from 'node:path';
import { createInterface } from 'node:readline';
import { setTimeout as delay } from 'node:timers/promises';
import { ownedEngineeringProfile } from '../src/editor-tools.mjs';
import { startEditorSession } from '../src/editor-session-broker.mjs';
import { sourceTargetDiscovery } from '../tests/fixtures/editor-source-targets.mjs';

// The release fixture uses the actual packaged child and broker, but never
// opens an IDE, submits a PLC build or establishes an ADS connection.
const [runtimeDirectory, catalogDirectory = runtimeDirectory] = process.argv.slice(2);
if (process.platform !== 'win32' || !runtimeDirectory || process.argv.length > 4)
  throw new Error('Supply a Windows Engineering input directory and optionally a release catalog directory');
const entrypoint = resolve(runtimeDirectory, 'packages/mcp/dist/index.js');
const lock = JSON.parse(await readFile(new URL('../upstream/engineering-lock.json', import.meta.url), 'utf8'));
const catalogs = new Map();
for (const [profile, count] of Object.entries({ build: 9, editor: 16, debug: 15 })) {
  const { path, sha256 } = lock.catalogs[profile];
  const bytes = await readFile(resolve(catalogDirectory, path));
  assert.equal(createHash('sha256').update(bytes).digest('hex'), sha256, path);
  const names = JSON.parse(bytes).tools.map(tool => tool.name).sort();
  assert.equal(names.length, count);
  assert.deepEqual(names, [...ownedEngineeringProfile(profile).names].sort());
  catalogs.set(profile, names);
}
const approvalDelayMs = 32000;
const sessionId = randomUUID();
const pipeName = '\\\\.\\pipe\\twincat-assistant-engineering-' + randomBytes(32).toString('hex');
const key = randomBytes(32);
const bootstrap = { kind: 'twincat-assistant-engineering-bootstrap', protocolVersion: 1, pipeName, sessionId,
  capability: key.toString('hex'), expected: { idePid: 123, ideStartedUtc: '2026-01-01T00:00:00Z',
    solutionFile: 'C:\\Fixture\\Project.sln', projectFile: 'C:\\Fixture\\Project.plcproj', contextEpoch: 'fixture' } };
const observed = [];
let debugContextCalls = 0;
let pipeError;
const server = createServer(socket => {
  socket.on('error', () => {});
  const nonce = randomBytes(32).toString('hex');
  let clientNonce;
  const mac = (domain, payload) => {
    const digest = payload === undefined ? '' : '\n' + createHash('sha256').update(payload).digest('hex');
    return createHmac('sha256', key).update(`TCMCP1\n${domain}\n${sessionId}\n${nonce}\n${clientNonce}${digest}`).digest('hex');
  };
  socket.write(JSON.stringify({ protocolVersion: 1, sessionId, serverNonce: nonce }) + '\n');
  createInterface({ input: socket }).on('line', async line => {
    try {
      const frame = JSON.parse(line);
      if (!clientNonce) {
        clientNonce = frame.clientNonce;
        assert.equal(frame.proof, mac('client'));
        socket.write(JSON.stringify({ proof: mac('server') }) + '\n');
        return;
      }
      assert.equal(frame.proof, mac('request', frame.request));
      const request = JSON.parse(frame.request);
      assert.equal(request.sessionId, sessionId);
      assert.deepEqual(request.expected, bootstrap.expected);
      observed.push(request.operation);
      let result = null;
      if (request.operation === 'session.ping') result = { contextValidated: true, capabilities: {} };
      else if (request.operation === 'debug.context' && ++debugContextCalls === 2) {
        result = { loggedIn: true, grantId: '00000000-0000-0000-0000-000000000001', amsNetId: '1.2.3.4.5.6', adsPort: 851, symbolScope: 'all' };
      }
      else if (request.operation === 'source.create') {
        const { approvalExpiresUtc, ...creation } = request.arguments;
        const remaining = Date.parse(approvalExpiresUtc) - Date.now();
        assert.ok(remaining > 80000 && remaining <= 90000);
        assert.deepEqual(creation, { parentPath: 'POUs', name: 'FB_Fixture', kind: 'fb' });
        await delay(approvalDelayMs);
        assert.ok(Date.parse(approvalExpiresUtc) > Date.now());
        result = { source: 'automationInterface', persistence: 'fileWrittenProjectPendingSave', treePath: 'POUs^FB_Fixture', kind: 'pou' };
      }
      else if (request.operation === 'source.targets') {
        assert.deepEqual(request.arguments, {});
        result = sourceTargetDiscovery();
      }
      assert.ok(['session.ping', 'source.preview', 'source.create', 'source.targets', 'debug.context'].includes(request.operation));
      const response = JSON.stringify({ protocolVersion: 1, sessionId, operationId: request.operationId,
        status: result ? 'succeeded' : 'unsupported', provenance: {}, result });
      if (request.operation === 'source.targets') assert.ok(Buffer.byteLength(response) < 1024 * 1024);
      socket.end(JSON.stringify({ response, proof: mac('response', response) }) + '\n');
    } catch (error) { pipeError = error; socket.destroy(); }
  });
});
server.listen(pipeName);
await once(server, 'listening');
async function rpc(broker, method, params, timeoutMs = 8000) {
  const reply = await fetch(broker.url, { method: 'POST', signal: AbortSignal.timeout(timeoutMs),
    headers: { 'content-type': 'application/json', ...broker.configuration().headers },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params }) });
  assert.equal(reply.status, 200);
  return reply.json();
}
const call = (name, args = {}) => ({ name, arguments: args, _meta: { 'ai.opencode/sessionID': 'fixture_chat' } });
try {
  for (const [profile, names] of catalogs) {
    const startup = new AbortController();
    const startupTimer = setTimeout(() => startup.abort(), 15000);
    const broker = await startEditorSession({ node: process.execPath, entrypoint, bootstrap, workspace: resolve('.'),
      sessionId: 'fixture_chat', profile }, startup.signal).finally(() => clearTimeout(startupTimer));
    try {
      const response = await rpc(broker, 'tools/list', {});
      assert.deepEqual(response.result.tools.map(tool => tool.name).sort(), names);
      const preview = await rpc(broker, 'tools/call', call('plc_source_preview', { objectId: 'fixture', section: 'implementation', proposedText: '' }));
      assert.equal(Boolean(preview.result), profile === 'editor');
      if (profile === 'editor') {
        const discovery = await rpc(broker, 'tools/call', call('plc_source_targets'));
        const { structuredContent, content } = discovery.result;
        assert.equal(structuredContent.status, 'succeeded');
        assert.equal(structuredContent.result.targets.length, 500);
        assert.equal(structuredContent.result.truncated, false);
        assert.deepEqual(JSON.parse(content[0].text), structuredContent);
        const messageBytes = Buffer.byteLength(JSON.stringify(discovery) + '\n');
        assert.ok(messageBytes > 2 * 1024 * 1024, `Discovery response was only ${messageBytes} bytes`);
        console.log(`Packaged Editor returned 500 targets in a ${messageBytes}-byte MCP response.`);
      }
      const started = performance.now();
      const creation = await rpc(broker, 'tools/call',
        call('plc_source_create', { parentPath: 'POUs', name: 'FB_Fixture', kind: 'fb' }), 45000);
      assert.equal(Boolean(creation.result), profile === 'editor');
      if (profile === 'editor') {
        const elapsedMs = performance.now() - started;
        assert.ok(elapsedMs >= approvalDelayMs);
        const created = creation.result.structuredContent;
        assert.equal(created.status, 'succeeded');
        assert.equal(created.result.treePath, 'POUs^FB_Fixture');
        assert.equal(created.result.persistence, 'fileWrittenProjectPendingSave');
        console.log(`Packaged Editor completed creation after ${Math.round(elapsedMs)} ms.`);
      }
      const state = await rpc(broker, 'tools/call', call('plc_debug_state'));
      assert.equal(Boolean(state.result), profile === 'debug');
      if (profile === 'debug') {
        assert.equal(JSON.parse(state.result.content[0].text).status, 'online_session_not_verified');
        const allState = await rpc(broker, 'tools/call', call('plc_debug_state'));
        assert.deepEqual(JSON.parse(allState.result.content[0].text), { status: 'online', symbolScope: 'all' });
      }
    } finally { await broker.close(); }
  }
  assert.deepEqual(observed, ['session.ping', 'session.ping', 'source.preview', 'source.targets', 'source.create',
    'session.ping', 'debug.context', 'debug.context']);
  console.log('Packaged build, Editor and Debug brokers passed catalog and bound operation checks.');
} catch (error) {
  throw pipeError ?? error;
} finally {
  server.close();
  key.fill(0);
}
