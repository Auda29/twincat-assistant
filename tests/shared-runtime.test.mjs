import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { supervise } from '../src/supervisor.mjs';
import { runtimeEnvironment } from '../src/state.mjs';

test('owned runtime cancellation reaps the shared Knowledge backend and its active frontend', {timeout:30000}, async () => {
  const root = await mkdtemp(join(tmpdir(),'tc-owned-knowledge-'));
  const stop = new AbortController(); let running;
  try {
    const base = join(root,'knowledge'); await mkdir(base);
    const knowledge = {executable:process.execPath,config:join(base,'knowledge.toml'),bridge:join(base,'broker.json'),startupTimeout:15000};
    await writeFile(knowledge.config,'[paths]\n');
    await writeFile(join(root,'serve'),'import('+JSON.stringify(pathToFileURL(resolve('tests/fixtures/knowledge-server.mjs')).href)+');');
    const ready = join(root,'ready.json');
    const env = runtimeEnvironment(root,{},process.env); env.TC_ASSISTANT_KNOWLEDGE = JSON.stringify({root,knowledge});
    running = supervise({command:process.execPath,args:[resolve('src/shared-chat-runtime.mjs'),resolve('tests/fixtures/shared-runtime-client.mjs'),knowledge.bridge,ready],cwd:root,env},root,stop.signal);
    let pids;
    for (const deadline=Date.now()+15000;Date.now()<deadline;) {
      try {pids=JSON.parse(await readFile(ready,'utf8'));break;}catch(error){if(!['ENOENT'].includes(error.code))throw error;}
      await new Promise(resolve=>setTimeout(resolve,50));
    }
    assert.ok(pids?.backendPid,'the actual shared backend must start inside the owned process tree');
    stop.abort(); await running;
    for (const pid of [pids.backendPid,pids.clientPid]) assert.throws(()=>process.kill(pid,0),'owned process survived cancellation');
  } finally {stop.abort();await running?.catch(()=>{});await rm(root,{recursive:true,force:true,maxRetries:10,retryDelay:100});}
});
