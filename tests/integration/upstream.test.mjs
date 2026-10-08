import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { createOpenCodeConfig } from '../../src/config.mjs';
import { initializeState, runtimeEnvironment, inspectState } from '../../src/state.mjs';
import { probeServer } from '../../src/probe.mjs';

const capture = promisify(execFile);
const knowledge = process.env.TC_KNOWLEDGE_CHECKOUT;
const engineering = process.env.TC_ENGINEERING_CHECKOUT;
assert.ok(knowledge && engineering, 'Set TC_KNOWLEDGE_CHECKOUT and TC_ENGINEERING_CHECKOUT to the documented pinned, built upstream checkouts');
const executable = join(knowledge, '.venv', process.platform === 'win32' ? 'Scripts/twincat-knowledge.exe' : 'bin/twincat-knowledge');
const python = join(knowledge, '.venv', process.platform === 'win32' ? 'Scripts/python.exe' : 'bin/python');

async function fixture(root) {
  const corpus = join(root, 'corpus'); await mkdir(corpus);
  const { stdout } = await capture(python, [resolve('tests/integration/prepare_knowledge.py'), knowledge, corpus], { cwd: root, timeout: 30000 });
  const config = JSON.parse(stdout).config;
  const project = join(root, 'Synthetic.plcproj');
  await writeFile(project, '<Project><ItemGroup><Compile Include="FB_Station.TcPOU" /></ItemGroup></Project>');
  await writeFile(join(root, 'FB_Station.TcPOU'), '<TcPlcObject><POU Name="FB_Station" Id="synthetic-test-object"><Declaration><![CDATA[FUNCTION_BLOCK FB_Station\nVAR_INPUT\n Timeout : TIME := T#5s;\nEND_VAR]]></Declaration><Implementation><ST><![CDATA[// synthetic code only\n]]></ST></Implementation></POU></TcPlcObject>');
  const adsConfig = join(root, 'engineering.json');
  await writeFile(adsConfig, JSON.stringify({ targetAmsNetId: '127.0.0.1.1.1', readOnly: true, writeAllowlist: [], engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [{ path: project, type: 'plc' }] } }));
  return { knowledge: { executable, config }, engineering: { entrypoint: join(engineering, 'packages/mcp/dist/index.js'), config: adsConfig } };
}

test('real modern knowledge and legacy engineering MCPs return structured synthetic content through the production operator client', { timeout: 60000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-upstream-mcp-'));
  try {
    const settings = await fixture(root);
    const config = createOpenCodeConfig(settings);
    const environment = runtimeEnvironment(root, settings);
    const docs = await probeServer('knowledge', config.mcp.servers.knowledge, { name: 'tc_search', arguments: { query: 'Fixture', limit: 2 } }, { env: environment, cwd: root });
    assert.equal(docs.protocol, '2026-07-28');
    assert.deepEqual(docs.toolNames, ['tc_browse_topics', 'tc_get_corpus_status', 'tc_get_topic', 'tc_search']);
    assert.equal(docs.result.structuredContent.status, 'ok');
    assert.match(JSON.stringify(docs.result.structuredContent), /Fixture topic/);
    assert.ok(docs.result.structuredContent.sources.length > 0);
    const code = await probeServer('engineering', config.mcp.servers.engineering, { name: 'plc_read_pou', arguments: { pou: 'FB_Station' } }, { env: environment, cwd: root });
    assert.equal(code.era, 'legacy');
    assert.match(JSON.stringify(code.result), /Timeout/);
    assert.match(JSON.stringify(code.result), /FUNCTION_BLOCK FB_Station/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

async function freePort() {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = server.address().port; await new Promise(resolve => server.close(resolve)); return port;
}

test('real OpenChamber and OpenCode boot from the CLI with protected UI and preserved isolated profile', { timeout: 90000, skip: process.platform === 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-upstream-web-'));
  const state = join(root, 'runtime');
  const password = randomBytes(32).toString('hex');
  const port = await freePort();
  let child;
  try {
    const settings = await fixture(root);
    await initializeState(state, settings);
    child = spawn(process.execPath, [resolve('src/cli.mjs'), 'start', '--state', state, '--port', String(port)], { cwd: resolve('.'), env: { ...process.env, OPENCHAMBER_UI_PASSWORD: password }, stdio: ['ignore', 'pipe', 'pipe'] });
    let logs = ''; child.stdout.on('data', data => { logs = (logs + data).slice(-100000); }); child.stderr.on('data', data => { logs = (logs + data).slice(-100000); });
    const end = Date.now() + 60000;
    let response;
    while (Date.now() < end && child.exitCode === null) {
      try { response = await fetch(`http://127.0.0.1:${port}/`, { signal: AbortSignal.timeout(1000) }); if (response.status === 200) break; } catch { /* bounded startup readiness */ }
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    assert.equal(response?.status, 200, `Upstream failed to boot; inspect local test logs (exit ${child.exitCode})`);
    const html = await response.text(); assert.match(html, /<html|<!doctype/i);
    const protectedResponse = await fetch(`http://127.0.0.1:${port}/api/settings`);
    assert.equal(protectedResponse.status, 401, 'UI password must protect API access');
    assert.ok(!logs.includes(password), 'UI credential must not be printed');
    const origin = `http://127.0.0.1:${port}`;
    const login = await fetch(`${origin}/auth/session`, { method: 'POST', headers: { 'content-type': 'application/json', origin }, body: JSON.stringify({ password }) });
    assert.equal(login.status, 200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    const sessionResponse = await fetch(`${origin}/api/session`, { method: 'POST', headers: { cookie, 'content-type': 'application/json' }, body: JSON.stringify({ location: { directory: join(state, 'workspace') }, title: 'Synthetic integration', agent: 'twincat-readonly' }) });
    assert.equal(sessionResponse.status, 200);
    const directory = join(state, 'workspace');
    const headers = { cookie, 'x-opencode-directory': directory };
    // OpenCode 2.0.23 initializes a cold location's config plugins lazily.
    // The normal agent-list request warms that location without invoking a model.
    const agentsResponse = await fetch(`${origin}/api/agent?directory=${encodeURIComponent(directory)}`, { headers });
    assert.equal(agentsResponse.status, 200);
    let catalogResponse;
    let catalog;
    const deadline = Date.now() + 15000;
    do {
      catalogResponse = await fetch(`${origin}/api/mcp?directory=${encodeURIComponent(directory)}`, { headers });
      catalog = await catalogResponse.json();
      if (catalog.data.map(server => server.name).sort().join(',') === 'engineering,knowledge') break;
      await new Promise(resolve => setTimeout(resolve, 250));
    } while (Date.now() < deadline);
    assert.equal(catalogResponse.status, 200);
    assert.deepEqual(catalog.data.map(server => server.name).sort(), ['engineering', 'knowledge']);
    const actual = JSON.parse(await readFile(join(state, 'config/opencode.json'), 'utf8'));
    assert.deepEqual(actual.experimental.policies, createOpenCodeConfig(settings).experimental.policies);
    await inspectState(state);
  } finally {
    if (child && child.exitCode === null) { const closed = once(child, 'close'); child.kill('SIGTERM'); await Promise.race([closed, new Promise((_, reject) => setTimeout(() => reject(new Error('Owned CLI did not stop')), 10000).unref())]); }
    await rm(root, { recursive: true, force: true });
  }
});
