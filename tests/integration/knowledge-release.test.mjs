import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { initializeState, runtimeEnvironment } from '../../src/state.mjs';
import { createOpenCodeConfig } from '../../src/config.mjs';
import { startKnowledgeBroker } from '../../src/knowledge-broker.mjs';
import { probeServer } from '../../src/probe.mjs';

const executable = process.env.TC_KNOWLEDGE_EXECUTABLE;
const checkout = process.env.TC_KNOWLEDGE_CHECKOUT;
const pin = JSON.parse(await readFile(new URL('../../upstream/knowledge-lock.json', import.meta.url), 'utf8'));
const capture = promisify(execFile);

test('pinned Knowledge wheel reports its release and returns searchable topics through the shared Assistant broker', {
  skip: !executable || !checkout, timeout: 90000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-release-'));
  let broker;
  try {
    const python = join(dirname(executable), process.platform === 'win32' ? 'python.exe' : 'python');
    const version = await capture(executable, ['--version'], { windowsHide: true });
    assert.equal(version.stdout.trim(), 'twincat-knowledge ' + pin.version);
    const metadata = await capture(python, ['-I', '-c', "import importlib.metadata; print(importlib.metadata.version('twincat-knowledge'))"], { windowsHide: true });
    assert.equal(metadata.stdout.trim(), pin.version, 'installed package metadata must match the CLI');
    const corpus = join(root, 'corpus');
    await mkdir(corpus);
    const fixture = JSON.parse((await capture(python,
      ['-I', resolve('tests/integration/prepare_knowledge.py'), checkout, corpus],
      { windowsHide: true, timeout: 30000 })).stdout);
    const knowledge = { executable, config: fixture.config, bridge: join(root, 'state', 'knowledge', 'broker.json') };
    const state = join(root, 'state');
    await initializeState(state, { knowledge });
    const env = runtimeEnvironment(state, {});
    const config = createOpenCodeConfig({ knowledge });
    const direct = createOpenCodeConfig({ knowledge: { executable, config: fixture.config } });
    const status = await probeServer('knowledge', direct.mcp.servers.knowledge,
      { name: 'tc_get_corpus_status', arguments: {} }, { cwd: root, env });
    assert.equal(status.server.version, pin.version, 'actual MCP initialization must report the pinned release');
    assert.equal(status.protocol, pin.protocol);
    assert.equal(status.result.structuredContent.status, 'ok');
    assert.deepEqual(status.toolNames.sort(), ['tc_browse_topics', 'tc_find_libraries', 'tc_find_symbols', 'tc_get_corpus_status',
      'tc_get_library', 'tc_get_symbol', 'tc_get_topic', 'tc_lookup_error', 'tc_search']);
    broker = await startKnowledgeBroker({ root: state, knowledge, env, cwd: root });
    const search = await probeServer('knowledge', config.mcp.servers.knowledge,
      { name: 'tc_search', arguments: { query: 'Fixture', limit: 2 } }, { cwd: root, env });
    const result = search.result.structuredContent;
    assert.equal(result.status, 'ok');
    assert.ok(result.data.items.length > 0);
    assert.deepEqual(JSON.parse(search.result.content[0].text), result, 'the model receives complete hit IDs and sources');
    const topicId = result.data.items[0].id;
    const topic = await probeServer('knowledge', config.mcp.servers.knowledge,
      { name: 'tc_get_topic', arguments: { topic_id: topicId } }, { cwd: root, env });
    assert.equal(topic.result.structuredContent.status, 'ok');
    assert.equal(topic.result.structuredContent.data.topic_id, topicId);
    assert.equal(JSON.parse(topic.result.content[0].text).data.topic_id, topicId);
    assert.equal(broker.statistics.starts, 1, 'both calls share the same Knowledge process');
  } finally {
    await broker?.close();
    await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
  }
});
