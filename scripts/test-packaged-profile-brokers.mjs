import assert from 'node:assert/strict';
import { createHash, createHmac, randomBytes, randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { createServer } from 'node:net';
import { resolve } from 'node:path';
import { createInterface } from 'node:readline';
import { ownedEngineeringProfile } from '../src/editor-tools.mjs';
import { startEditorSession } from '../src/editor-session-broker.mjs';

// The release fixture uses the actual packaged child and broker, but never
// opens an IDE, submits a PLC build or establishes an ADS connection.
if (process.platform !== 'win32' || process.argv.length !== 3) throw new Error('Supply a Windows Engineering input directory');
const entrypoint = resolve(process.argv[2], 'packages/mcp/dist/index.js');
const sessionId = randomUUID();
const pipeName = '\\\\.\\pipe\\twincat-assistant-engineering-' + randomBytes(32).toString('hex');
const key = randomBytes(32);
const bootstrap = { kind: 'twincat-assistant-engineering-bootstrap', protocolVersion: 1, pipeName, sessionId,
  capability: key.toString('hex'), expected: { idePid: 123, ideStartedUtc: '2026-01-01T00:00:00Z',
    solutionFile: 'C:\\Fixture\\Project.sln', projectFile: 'C:\\Fixture\\Project.plcproj', contextEpoch: 'fixture' } };
const observed = [];
let debugContextCalls = 0;
const server = createServer(socket => {
  socket.on('error', () => {});
  const nonce = randomBytes(32).toString('hex');
  let clientNonce;
  const mac = (domain, payload) => {
    const digest = payload === undefined ? '' : '\n' + createHash('sha256').update(payload).digest('hex');
    return createHmac('sha256', key).update(`TCMCP1\n${domain}\n${sessionId}\n${nonce}\n${clientNonce}${digest}`).digest('hex');
  };
  socket.write(JSON.stringify({ protocolVersion: 1, sessionId, serverNonce: nonce }) + '\n');
  createInterface({ input: socket }).on('line', line => {
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
      assert.ok(['session.ping', 'source.preview', 'debug.context'].includes(request.operation));
      const response = JSON.stringify({ protocolVersion: 1, sessionId, operationId: request.operationId,
        status: result ? 'succeeded' : 'unsupported', provenance: {}, result });
      socket.end(JSON.stringify({ response, proof: mac('response', response) }) + '\n');
    } catch { socket.destroy(); }
  });
});
server.listen(pipeName);
await once(server, 'listening');
async function rpc(broker, method, params) {
  const reply = await fetch(broker.url, { method: 'POST', signal: AbortSignal.timeout(8000),
    headers: { 'content-type': 'application/json', ...broker.configuration().headers },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params }) });
  assert.equal(reply.status, 200);
  return reply.json();
}
const call = (name, args = {}) => ({ name, arguments: args, _meta: { 'ai.opencode/sessionID': 'fixture_chat' } });
try {
  for (const profile of ['build', 'editor', 'debug']) {
    const broker = await startEditorSession({ node: process.execPath, entrypoint, bootstrap, workspace: resolve('.'),
      sessionId: 'fixture_chat', profile }, AbortSignal.timeout(15000));
    try {
      const response = await rpc(broker, 'tools/list', {});
      assert.deepEqual(response.result.tools.map(tool => tool.name).sort(), [...ownedEngineeringProfile(profile).names].sort());
      const preview = await rpc(broker, 'tools/call', call('plc_source_preview', { objectId: 'fixture', section: 'implementation', proposedText: '' }));
      assert.equal(Boolean(preview.result), profile === 'editor');
      const state = await rpc(broker, 'tools/call', call('plc_debug_state'));
      assert.equal(Boolean(state.result), profile === 'debug');
      if (profile === 'debug') {
        assert.equal(JSON.parse(state.result.content[0].text).status, 'online_session_not_verified');
        const allState = await rpc(broker, 'tools/call', call('plc_debug_state'));
        assert.deepEqual(JSON.parse(allState.result.content[0].text), { status: 'online', symbolScope: 'all' });
      }
    } finally { await broker.close(); }
  }
  assert.deepEqual(observed, ['session.ping', 'session.ping', 'source.preview', 'session.ping', 'debug.context', 'debug.context']);
  console.log('Packaged build, Editor and Debug brokers passed catalog and bound operation checks.');
} finally {
  server.close();
  key.fill(0);
}
