import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { connect } from 'node:net';
import { startKnowledgeBroker, knowledgeResultForModel } from '../src/knowledge-broker.mjs';
import { knowledgeBrokerAvailable, readBrokerDescriptor } from '../src/knowledge-client.mjs';
import { createOpenCodeConfig } from '../src/config.mjs';
import { probeServer } from '../src/probe.mjs';
import { Client } from '@modelcontextprotocol/client';
import { StdioClientTransport } from '@modelcontextprotocol/client/stdio';

test('structured Knowledge results expose complete IDs, article bodies and sources as model text', () => {
  const input = { content: [{ type: 'text', text: 'count=3' }], structuredContent: { status: 'ok', data: { items: [{ topic_id: 'exact-id', body: 'article text', source_uri: 'source' }] } }, isError: false, resultType: 'complete' };
  const result = knowledgeResultForModel(input);
  assert.deepEqual(JSON.parse(result.content[0].text),input.structuredContent,'even a first-text-only consumer receives IDs, bodies and citations');
  assert.deepEqual(JSON.parse(result.content.at(-1).text), input.structuredContent);
  assert.deepEqual(result.structuredContent, input.structuredContent);
  assert.deepEqual(knowledgeResultForModel(result), result, 'do not duplicate an existing text payload');
  assert.deepEqual(input.content, [{ type: 'text', text: 'count=3' }]);
  assert.equal(knowledgeResultForModel({content:[]}).content.length, 0);
  assert.throws(() => knowledgeResultForModel({structuredContent:{text:'x'.repeat(1048576)}}), /limit/);
});

test('concurrent native contexts share one Knowledge backend, retain it after disconnect and reload an activated revision once', {timeout:30000}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-shared-'));
  let broker;
  try {
    const base = join(root, 'knowledge'); await mkdir(base); await mkdir(join(base, 'state'));
    const config = join(base, 'knowledge.toml'), bridge = join(base, 'broker.json'), starts = join(root, 'starts.txt');
    await writeFile(config, '[paths]\n');
    const knowledge = { executable:process.execPath, config, bridge, startupTimeout:15000 };
    broker = await startKnowledgeBroker({root, knowledge, cwd:root, env:{...process.env,TC_TEST_KNOWLEDGE_STARTS:starts}, backendCommand:[process.execPath, resolve('tests/fixtures/knowledge-server.mjs')]});
    assert.equal(await knowledgeBrokerAvailable(bridge), true);
    const server = createOpenCodeConfig({knowledge}).mcp.servers.knowledge;
    const search = query => probeServer('knowledge',server,{name:'tc_search',arguments:{query}}, {cwd:root,env:process.env});
    const results = await Promise.all(['first','second','third'].map(search));
    assert.equal(broker.statistics.starts,1);
    for (let i=0;i<results.length;i++) {
      const text = results[i].result.content.at(-1).text;
      assert.equal(JSON.parse(text).data.items[0].topic_id,'fixture-topic-1');
      assert.equal(JSON.parse(text).data.items[0].text,['first','second','third'][i]);
      assert.deepEqual(results[i].toolNames.sort(),['tc_browse_topics','tc_get_corpus_status','tc_get_topic','tc_search']);
    }
    const warm = Date.now(); await search('warm');
    assert.equal(broker.statistics.starts,1,'closing frontends must retain the shared corpus');
    assert.ok(Date.now()-warm<5000,'warm requests must not load the corpus again');
    const cancelled = new Client({name:'cancel-test',version:'1.0.0'},{capabilities:{},versionNegotiation:{mode:{pin:'2026-07-28'}}});
    const cancelledTransport = new StdioClientTransport({command:server.command[0],args:server.command.slice(1),cwd:root,stderr:'ignore'});
    try {
      await cancelled.connect(cancelledTransport);
      const abort = new AbortController();
      const slow = cancelled.callTool({name:'tc_search',arguments:{query:'slow'}},{signal:abort.signal});
      const timer = setTimeout(()=>abort.abort(),100);
      try { await assert.rejects(slow); } finally { clearTimeout(timer); }
      assert.match(JSON.stringify((await search('after-cancel')).result),/after-cancel/);
      assert.equal(broker.statistics.starts,1,'one cancelled frontend must not restart the shared backend');
      await assert.rejects(cancelled.callTool({name:'unreviewed_write',arguments:{}}));
    } finally {await cancelled.close();await cancelledTransport.close();}
    await writeFile(join(base,'state','active.json'),JSON.stringify({active:{manifest_sha256:'new-revision'}}));
    await Promise.all([search('new-a'),search('new-b')]);
    assert.equal(broker.statistics.starts,2,'only one replacement backend after activation');
    assert.equal((await readFile(starts,'utf8')).trim().split('\n').length,2);
    const descriptor = await readBrokerDescriptor(bridge);
    const unauthorized = connect(descriptor.endpoint); unauthorized.on('error',()=>{});
    await new Promise(resolve => { unauthorized.once('connect',()=>unauthorized.write(JSON.stringify({token:'0'.repeat(64),mode:'mcp'})+'\n')); unauthorized.once('close',resolve); });
    assert.equal(broker.statistics.starts,2,'unauthenticated clients cannot access a corpus');
    await broker.close(); broker=null;
    assert.equal(await knowledgeBrokerAvailable(bridge),false);
  } finally { await broker?.close(); await rm(root,{recursive:true,force:true}); }
});

test('the broker loads a configured corpus at start before any chat asks for its catalog', {timeout:30000}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-warm-'));
  let broker;
  try {
    const base = join(root, 'knowledge'); await mkdir(base); await mkdir(join(base, 'state'));
    const config = join(base, 'knowledge.toml'), bridge = join(base, 'broker.json'), starts = join(root, 'starts.txt');
    await writeFile(config, '[paths]\n');
    const knowledge = { executable:process.execPath, config, bridge, startupTimeout:15000 };
    broker = await startKnowledgeBroker({root, knowledge, cwd:root, env:{...process.env,TC_TEST_KNOWLEDGE_STARTS:starts}, backendCommand:[process.execPath, resolve('tests/fixtures/knowledge-server.mjs')]});
    const deadline = Date.now() + 10000;
    while (Date.now() < deadline && (await readFile(starts,'utf8').catch(() => '')).trim() === '') await new Promise(done => setTimeout(done, 50));
    assert.equal(broker.statistics.starts,1,'the backend starts without a client request');
    const server = createOpenCodeConfig({knowledge}).mcp.servers.knowledge;
    await probeServer('knowledge',server,{name:'tc_search',arguments:{query:'after-warm'}}, {cwd:root,env:process.env});
    assert.equal(broker.statistics.starts,1,'the first chat reuses the preloaded corpus');
  } finally { await broker?.close(); await rm(root,{recursive:true,force:true}); }
});

test('a broker without a configured corpus starts quietly and loads nothing', {timeout:30000}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-empty-'));
  let broker;
  try {
    const base = join(root, 'knowledge'); await mkdir(base);
    const knowledge = { executable:process.execPath, config:join(base, 'knowledge.toml'), bridge:join(base, 'broker.json'), startupTimeout:15000 };
    broker = await startKnowledgeBroker({root, knowledge, cwd:root, env:process.env, backendCommand:[process.execPath, resolve('tests/fixtures/knowledge-server.mjs')]});
    await new Promise(done => setTimeout(done, 300));
    assert.equal(broker.statistics.starts,0);
    assert.equal(await knowledgeBrokerAvailable(knowledge.bridge), true);
  } finally { await broker?.close(); await rm(root,{recursive:true,force:true}); }
});
