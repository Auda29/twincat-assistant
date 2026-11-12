import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { createServer } from 'node:net';
import { randomBytes } from 'node:crypto';
import { startNativeChat } from '../../src/native-chat.mjs';
import { startEditorSession, registerEditorSession } from '../../src/editor-session-broker.mjs';

test('pinned Core connects an ephemeral Editor MCP in only its selected location and removes it without persisting credentials', {
  skip: process.platform !== 'win32', timeout: 100000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-editor-core-'));
  const state = join(root, 'state'), workspace = join(state, 'workspace');
  const password = randomBytes(32).toString('hex');
  const socket = createServer();
  await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
  const port = socket.address().port;
  await new Promise(resolve => socket.close(resolve));
  const origin = 'http://127.0.0.1:' + port;
  const controller = new AbortController();
  let runtime, broker, remove;
  try {
    await new Promise((resolve, reject) => {
      runtime = startNativeChat({ state, port, password }, controller.signal, resolve, { runtime: process.env.TC_EDITOR_RUNTIME ?? 'distribution' });
      runtime.catch(reject);
    });
    const before = await readFile(join(state, 'config', 'opencode.json'), 'utf8');
    // Native project workspaces override only Analysis; the global Editor agent
    // must survive that overlay without widening the project's Analysis rules.
    const globalConfig = JSON.parse(before);
    await writeFile(join(workspace, 'opencode.json'), JSON.stringify({ agents: {
      'twincat-readonly': globalConfig.agents['twincat-readonly'],
    } }));
    const hostSecret = randomBytes(32).toString('hex');
    broker = await startEditorSession({ node: process.execPath, entrypoint: resolve('tests/fixtures/editor-engineering-child.mjs'),
      workspace, sessionId: 'ses_core_fixture', bootstrap: { kind: 'twincat-assistant-engineering-bootstrap', protocolVersion: 1,
        sessionId: '12345678-1234-1234-1234-123456789abc', capability: hostSecret,
        pipeName: '\\\\.\\pipe\\twincat-assistant-engineering-' + 'b'.repeat(64),
        expected: { idePid: 1234, ideStartedUtc: '2026-11-12T10:00:00Z', solutionFile: 'C:\\fixture\\Fixture.sln',
          projectFile: 'C:\\fixture\\PLC\\PLC.plcproj', contextEpoch: 'fixture' } } });
    const frontendSecret = broker.configuration().headers.Authorization;
    remove = await registerEditorSession({ origin, password, workspace, broker }, controller.signal);
    const login = await fetch(origin + '/auth/session', { method: 'POST', headers: { origin, 'content-type': 'application/json' }, body: JSON.stringify({ password }) });
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    const headers = { cookie, 'x-opencode-directory': encodeURIComponent(workspace), 'x-opencode-directory-encoding': 'uri' };
    let agents = [], editor, analysis;
    const agentsDeadline = Date.now() + 20000;
    while (Date.now() < agentsDeadline) {
      agents = (await (await fetch(origin + '/api/agent', { headers })).json()).data;
      editor = agents.find(agent => (agent.id ?? agent.name) === 'twincat-editor');
      analysis = agents.find(agent => (agent.id ?? agent.name) === 'twincat-readonly');
      if (editor && analysis) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(editor && analysis, 'Core must load both managed profiles: ' + agents.map(agent => agent.id ?? agent.name).join(','));
    const effect = (agent, action) => agent.permissions.findLast(rule => rule.action === '*' || rule.action === action)?.effect;
    assert.equal(effect(editor, 'editor_tc_build_project'), 'allow');
    assert.equal(effect(editor, 'editor_plc_source_apply'), 'deny');
    assert.equal(effect(analysis, 'editor_plc_source_apply'), 'deny');
    assert.equal(effect(editor, 'bash'), 'deny');
    let connected = false;
    const deadline = Date.now() + 20000;
    while (Date.now() < deadline) {
      const response = await fetch(origin + '/api/mcp', { headers });
      assert.equal(response.status, 200);
      const body = await response.json();
      assert.ok(!JSON.stringify(body).includes(frontendSecret));
      connected = body.data.some(entry => entry.name === 'editor' && entry.status.status === 'connected');
      if (connected) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(connected, 'Core must connect the live remote MCP after runtime registration');
    const config = await (await fetch(origin + '/api/config', { headers })).text();
    assert.ok(!config.includes(frontendSecret));
    assert.ok(!config.includes(hostSecret));
    assert.equal(await readFile(join(state, 'config', 'opencode.json'), 'utf8'), before);
    const foreign = await (await fetch(origin + '/api/mcp', { headers: { cookie,
      'x-opencode-directory': encodeURIComponent(join(state, 'home', '.config', 'openchamber', 'chats')), 'x-opencode-directory-encoding': 'uri' } })).json();
    assert.ok(!foreign.data.some(entry => entry.name === 'editor'));
    await remove(); remove = null;
    const after = await (await fetch(origin + '/api/mcp', { headers })).json();
    assert.ok(!after.data.some(entry => entry.name === 'editor'));
    assert.equal(await readFile(join(state, 'config', 'opencode.json'), 'utf8'), before);
  } finally {
    await remove?.().catch(() => {});
    await broker?.close();
    controller.abort(); await runtime?.catch(() => {});
    // The fixture root is the exact newly-created temporary directory.
    await rm(root, { recursive: true, force: true });
  }
});
