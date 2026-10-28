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
import { initializeState, runtimeEnvironment } from '../../src/state.mjs';
import { prepareNativeProject } from '../../src/native-mcp.mjs';
import { probeServer } from '../../src/probe.mjs';
import { startKnowledgeBroker } from '../../src/knowledge-broker.mjs';
import { inspectState } from '../../src/state.mjs';
import { knowledgeBrokerStatus, knowledgeBrokerAvailable } from '../../src/knowledge-client.mjs';

const capture = promisify(execFile);
const knowledge = process.env.TC_KNOWLEDGE_CHECKOUT;
const engineering = process.env.TC_ENGINEERING_CHECKOUT;
const available = Boolean(knowledge && engineering);

test('native project locations connect both real MCPs and read only their configured PLC', {
  skip: !available, timeout: 150000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-native-mcp-real-'));
  let child, closed, broker;
  try {
    const corpus = join(root, 'corpus'); await mkdir(corpus);
    const executable = join(knowledge, '.venv', process.platform === 'win32' ? 'Scripts/twincat-knowledge.exe' : 'bin/twincat-knowledge');
    const python = join(knowledge, '.venv', process.platform === 'win32' ? 'Scripts/python.exe' : 'bin/python');
    const fixture = JSON.parse((await capture(python, [resolve('tests/integration/prepare_knowledge.py'), knowledge, corpus], { timeout: 30000 })).stdout);
    const settingsFile = join(root, 'mcp.json');
    await writeFile(settingsFile, JSON.stringify({ knowledge: { executable, config: fixture.config },
      engineering: { entrypoint: join(engineering, 'packages/mcp/dist/index.js') } }));
    const state = join(root, 'state'); await initializeState(state, {});
    const solution = join(root, 'ContextChecks.sln'); await writeFile(solution, 'synthetic solution');
    const locations = [];
    for (const name of ['PLC_A', 'PLC_B']) {
      const folder = join(root, name); await mkdir(folder);
      const project = join(folder, name + '.plcproj');
      await writeFile(project, '<Project><ItemGroup><Compile Include="FB_Station.TcPOU" /></ItemGroup></Project>');
      await writeFile(join(folder, 'FB_Station.TcPOU'), '<TcPlcObject><POU Name="FB_Station" Id="{013d444c-37b8-4280-a11c-930ae7545600}">'
        + '<Declaration><![CDATA[FUNCTION_BLOCK FB_Station]]></Declaration><Implementation><ST><![CDATA['
        + '// ' + name + '_ONLY\nbRun := TRUE;]]></ST></Implementation></POU></TcPlcObject>');
      const location = await prepareNativeProject({ state, mcpConfig: settingsFile, solution, project });
      const config = JSON.parse(await readFile(join(location.workspace, 'opencode.json'), 'utf8'));
      const options = { env: runtimeEnvironment(state, {}), cwd: location.workspace };
      const code = await probeServer('engineering', config.mcp.servers.engineering,
        { name: 'plc_read_pou', arguments: { pou: 'FB_Station' } }, options);
      assert.match(JSON.stringify(code.result), new RegExp(name + '_ONLY'));
      assert.ok(!JSON.stringify(code.result).includes((name === 'PLC_A' ? 'PLC_B' : 'PLC_A') + '_ONLY'));
      const foreign = await probeServer('engineering', config.mcp.servers.engineering,
        { name: 'plc_read_pou', arguments: { pou: 'FB_Station', project: join(root, name === 'PLC_A' ? 'PLC_B/PLC_B.plcproj' : 'PLC_A/PLC_A.plcproj') } }, options);
      assert.ok(!JSON.stringify(foreign.result).includes('_ONLY'), 'caller cannot select an unconfigured PLC by supplying its path');
      locations.push(location);
    }
    const config = JSON.parse(await readFile(join(locations[0].workspace, 'opencode.json'), 'utf8'));
    broker = await startKnowledgeBroker({root:state,knowledge:(await inspectState(state)).settings.knowledge,
      env:runtimeEnvironment(state,{}),cwd:locations[0].workspace});
    const docs = await probeServer('knowledge', config.mcp.servers.knowledge,
      { name: 'tc_search', arguments: { query: 'Fixture', limit: 2 } }, { env: runtimeEnvironment(state, {}), cwd: locations[0].workspace });
    assert.equal(docs.result.structuredContent.status, 'ok');
    assert.ok(docs.result.structuredContent.sources.length > 0);
    assert.deepEqual(JSON.parse(docs.result.content.at(-1).text),docs.result.structuredContent,'model text includes actual search hits and source IDs');
    const topicId=docs.result.structuredContent.data.items[0].id;
    const topic=await probeServer('knowledge',config.mcp.servers.knowledge,
      {name:'tc_get_topic',arguments:{topic_id:topicId}}, {env:runtimeEnvironment(state,{}),cwd:locations[0].workspace});
    assert.equal(JSON.parse(topic.result.content.at(-1).text).data.topic_id,topicId,'the exact returned search ID opens its source topic');
    const emptyCorpus = await probeServer('knowledge', { ...config.mcp.servers.knowledge,
      command:[executable,'serve','--transport','stdio'] },
    {name:'tc_get_corpus_status',arguments:{}}, {env:runtimeEnvironment(state,{}),cwd:locations[0].workspace});
    assert.equal(emptyCorpus.result.structuredContent.status,'not_available','an unconfigured server must not claim documentation readiness');
    await broker.close(); broker=null;

    if (process.platform !== 'win32') return; // pinned native launcher uses opencode.exe
    const net = createServer(); net.listen(0, '127.0.0.1'); await once(net, 'listening');
    const port = net.address().port; await new Promise(resolve => net.close(resolve));
    const password = randomBytes(32).toString('hex');
    child = spawn(process.execPath, [resolve('src/native-chat.mjs')], { windowsHide: true, stdio: ['pipe','pipe','pipe'] });
    closed = once(child, 'close'); child.stderr.resume();
    const ready = new Promise((resolve, reject) => {
      let output = '';
      child.stdout.on('data', data => { output += data; if (output.includes('\n')) {
        try { const value = JSON.parse(output.split('\n')[0]); value.status === 'ready' ? resolve(value) : reject(new Error('Native MCP runtime failed')); }
        catch (error) { reject(error); }
      } });
      child.once('close', () => reject(new Error('Native MCP runtime exited')));
    });
    child.stdin.write(JSON.stringify({ state, port, password, mcpConfig: settingsFile })+'\n');
    await ready;
    const origin = 'http://127.0.0.1:'+port;
    const login = await fetch(origin+'/auth/session', { method:'POST', headers:{'content-type':'application/json',origin}, body:JSON.stringify({password}) });
    assert.equal(login.status,200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    for (const location of locations) {
      const headers = { cookie, 'x-opencode-directory':encodeURIComponent(location.workspace), 'x-opencode-directory-encoding':'uri' };
      assert.equal((await fetch(origin+'/api/agent', {headers})).status,200);
      let catalog=[];
      const deadline=Date.now()+25000;
      do {
        const response=await fetch(origin+'/api/mcp',{headers});
        assert.equal(response.status,200); catalog=(await response.json()).data;
        if (catalog.filter(server=>server.status.status==='connected').length===2) break;
        await new Promise(resolve=>setTimeout(resolve,300));
      } while(Date.now()<deadline);
      assert.deepEqual(catalog.map(server=>[server.name,server.status.status]).sort(), [['engineering','connected'],['knowledge','connected']]);
    }
    const bridge=join(state,'knowledge/broker.json');
    assert.equal((await knowledgeBrokerStatus(bridge)).backendStarts,1,'both actual runtime project contexts share one loaded corpus');
    child.kill(); await closed; // deliberately lose the Node owner with both contexts connected
    const stopped=Date.now()+10000;
    while(await knowledgeBrokerAvailable(bridge)&&Date.now()<stopped)await new Promise(resolve=>setTimeout(resolve,100));
    assert.equal(await knowledgeBrokerAvailable(bridge),false,'shared Knowledge must not survive native owner death');
  } finally {
    await broker?.close();
    if (child?.exitCode===null) child.kill();
    if (closed) await closed;
    await rm(root,{recursive:true,force:true,maxRetries:10,retryDelay:100});
  }
});
