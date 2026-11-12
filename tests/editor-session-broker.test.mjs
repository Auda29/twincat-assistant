import assert from 'node:assert/strict';
import test from 'node:test';
import { resolve } from 'node:path';
import { createServer } from 'node:http';
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { EDITOR_TOOL_NAMES, startEditorSession, validateEditorBootstrap, registerEditorSession } from '../src/editor-session-broker.mjs';

const bootstrap = () => ({ kind: 'twincat-assistant-engineering-bootstrap', protocolVersion: 1,
  sessionId: '12345678-1234-1234-1234-123456789abc', capability: 'a'.repeat(64),
  pipeName: '\\\\.\\pipe\\twincat-assistant-engineering-' + 'b'.repeat(64),
  expected: { idePid: 1234, ideStartedUtc: '2026-11-12T10:00:00.1234567Z', solutionFile: 'C:\\fixture\\Fixture.sln',
    projectFile: 'C:\\fixture\\PLC\\PLC.plcproj', contextEpoch: 'epoch-1' } });
const options = () => ({ node: process.execPath, entrypoint: resolve('tests/fixtures/editor-engineering-child.mjs'),
  workspace: resolve('.'), sessionId: 'ses_editor_1', bootstrap: bootstrap() });
async function rpc(broker, method, params, headers = {}) {
  const response = await fetch(broker.url, { method: 'POST', headers: { 'content-type': 'application/json', ...broker.configuration().headers, ...headers },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params }) });
  return { response, value: response.status === 200 ? await response.json() : null };
}
const call = (name, sessionId = 'ses_editor_1', args = {}) => ({ name, arguments: args, _meta: { 'ai.opencode/sessionID': sessionId } });

test('Editor bootstrap accepts only the bounded native capability envelope', () => {
  assert.equal(validateEditorBootstrap(bootstrap()).expected.idePid, 1234);
  for (const change of [{ capability: 'bad' }, { extra: true }, { sessionId: '00000000-0000-0000-0000-000000000000' },
    { pipeName: 'http://127.0.0.1' }, { expected: { ...bootstrap().expected, projectFile: '..\\PLC.plcproj' } }])
    assert.throws(() => validateEditorBootstrap({ ...bootstrap(), ...change }), /Invalid/);
});

test('Editor remote MCP connects the real SDK, gates exact chat metadata, and strips the private host channel', { timeout: 15000 }, async () => {
  process.env.TC_EDITOR_TEST_PRIVATE = 'not-for-engineering';
  const broker = await startEditorSession(options());
  delete process.env.TC_EDITOR_TEST_PRIVATE;
  const client = new Client({ name: 'editor-test', version: '1' }, { capabilities: {}, versionNegotiation: { mode: 'legacy' } });
  const transport = new StreamableHTTPClientTransport(new URL(broker.url), { requestInit: { headers: broker.configuration().headers } });
  try {
    await client.connect(transport);
    const catalog = await client.listTools();
    assert.deepEqual(catalog.tools.map(tool => tool.name), EDITOR_TOOL_NAMES);
    const result = await client.callTool(call('tc_session_describe', 'ses_editor_1', { probe: 'scope' }));
    const value = JSON.parse(result.content[0].text);
    assert.equal(value.arguments.probe, 'scope');
    assert.equal(value.secretInArguments, false);
    assert.equal(value.secretInEnvironment, false);
    assert.equal(value.inheritedSentinel, false);
    assert.ok(!JSON.stringify(broker).includes(bootstrap().capability));
    assert.ok(!JSON.stringify(broker.configuration()).includes(bootstrap().capability));
    for (const params of [call('plc_source_apply', 'ses_analysis'), call('plc_source_apply'), call('plc_source_read'), call('plc_write'), call('bash'), { name: 'tc_session_describe', arguments: {} }])
      await assert.rejects(client.callTool(params));
    assert.equal((await rpc(broker, 'tools/list', {}, { Authorization: 'Bearer wrong' })).response.status, 403);
    assert.equal((await rpc(broker, 'tools/list', {}, { Origin: 'http://evil.invalid' })).response.status, 403);
    assert.ok((await rpc(broker, 'resources/read', { uri: 'file:///C:/secret' })).value.error);
    const calls = await Promise.all(['tc_session_describe', 'tc_project_association'].map(name => client.callTool(call(name, 'ses_editor_1', { delay: true }))));
    assert.deepEqual(calls.map(item => JSON.parse(item.content[0].text).name), ['tc_session_describe', 'tc_project_association']);
  } finally { await client.close(); await broker.close(); }
  await assert.rejects(fetch(broker.url));
  assert.throws(() => broker.configuration());
});

test('revocation and child exit fail closed without retrying a pending call', { timeout: 15000 }, async () => {
  const controller = new AbortController();
  const broker = await startEditorSession(options(), controller.signal);
  const pending = rpc(broker, 'tools/call', call('tc_build_project', 'ses_editor_1', { delay: true }));
  controller.abort();
  await assert.rejects(pending);
  await broker.close();
  const failed = await startEditorSession(options());
  await assert.rejects(rpc(failed, 'tools/call', call('tc_session_describe', 'ses_editor_1', { exit: true })));
  await failed.close();
  assert.throws(() => failed.configuration());
});

test('protected registration keeps frontend authentication in a memory-only Core override and revokes before deletion', { timeout: 15000 }, async () => {
  const broker = await startEditorSession(options());
  const requests = [];
  const service = createServer(async (req, res) => {
    let body = ''; for await (const chunk of req) body += chunk;
    requests.push({ method: req.method, url: req.url, headers: req.headers, body: body ? JSON.parse(body) : null });
    if (req.url === '/auth/session') { res.writeHead(200, { 'set-cookie': 'fixture=session; HttpOnly' }).end('{}'); return; }
    if (req.method === 'DELETE') assert.throws(() => broker.configuration());
    res.writeHead(204).end();
  });
  await new Promise(resolve => service.listen(0, '127.0.0.1', resolve));
  try {
    const remove = await registerEditorSession({ origin: 'http://127.0.0.1:' + service.address().port, password: 'synthetic-password', workspace: broker.workspace, broker });
    assert.equal(requests[1].url, '/api/experimental/mcp/editor');
    assert.equal(requests[1].method, 'PUT');
    assert.equal(requests[1].body.config.oauth, false);
    assert.equal(requests[1].headers['x-opencode-directory'], encodeURIComponent(broker.workspace));
    assert.ok(requests[1].body.config.headers.Authorization.startsWith('Bearer '));
    assert.ok(!JSON.stringify(requests).includes(bootstrap().capability));
    await remove();
    assert.equal(requests[2].method, 'DELETE');
  } finally { await broker.close(); await new Promise(resolve => service.close(resolve)); }
});
