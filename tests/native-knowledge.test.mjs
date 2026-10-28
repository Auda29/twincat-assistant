import assert from 'node:assert/strict';
import test from 'node:test';
import {mkdtemp,mkdir,writeFile,readFile,readdir,rm,symlink} from 'node:fs/promises';
import {join,dirname,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {createHash} from 'node:crypto';
import {discoverHelp,knowledgeStatus,setupKnowledge} from '../src/native-knowledge.mjs';
import {initializeState} from '../src/state.mjs';
import {acquireKnowledgeLease} from '../src/windows-native.mjs';

async function fixture(){
  const root=await mkdtemp(join(tmpdir(),'tc-help-setup-')),state=join(root,'state'),catalogRoot=join(root,'Catalogs');
  await initializeState(state,{});
  const mcpConfig=join(root,'mcp.json'),executable=join(root,'knowledge.exe'),entrypoint=join(root,'engineering.mjs');
  await writeFile(executable,'fixture');await writeFile(entrypoint,'fixture');
  await writeFile(mcpConfig,JSON.stringify({knowledge:{executable},engineering:{entrypoint}}));
  const folder=join(catalogRoot,'Fixture/ContentStore/DE-DE');await mkdir(folder,{recursive:true});
  const source=join(folder,'BKINFOSYS3_TEST_DE-DE.1.mshc');await writeFile(source,'synthetic fixture metadata');
  return {root,state,mcpConfig,catalogRoot,source};
}
async function cleanup(root){assert.equal(dirname(resolve(root)),resolve(tmpdir()));await rm(root,{recursive:true,force:true});}
test('help discovery reports every revision without choosing or traversing redirected stores',async()=>{
  const f=await fixture();
  try{
    await writeFile(join(dirname(f.source),'BKINFOSYS3_TEST_DE-DE.2.mshc'),'new revision');
    const found=await discoverHelp(f.catalogRoot);assert.equal(found.length,2);assert.ok(found.every(item=>item.language==='de'));
    assert.deepEqual(await discoverHelp(join(f.root,'absent')),[]);
    const redirected=join(f.catalogRoot,'Redirect');await symlink(join(f.catalogRoot,'Fixture'),redirected,process.platform==='win32'?'junction':'dir');
    assert.equal((await discoverHelp(f.catalogRoot)).length,2);
  }finally{await cleanup(f.root);}
});
test('knowledge status prepares a stable private config and preserves an external corpus',async()=>{
  const f=await fixture();
  try{
    const first=await knowledgeStatus(f,{catalogRoot:f.catalogRoot});assert.equal(first.phase,'available');assert.equal(first.ready,false);
    const config=await readFile(join(f.state,'knowledge/knowledge.toml'),'utf8');
    await knowledgeStatus(f,{catalogRoot:f.catalogRoot});assert.equal(await readFile(join(f.state,'knowledge/knowledge.toml'),'utf8'),config);
    const settings=JSON.parse(await readFile(f.mcpConfig,'utf8'));const external=join(f.root,'external.toml');await writeFile(external,'[paths]\n');
    settings.knowledge.config=external;await writeFile(f.mcpConfig,JSON.stringify(settings));
    assert.equal((await knowledgeStatus(f,{catalogRoot:f.catalogRoot})).phase,'external');
    await assert.rejects(setupKnowledge({...f,language:'de',acceptLocalIndexing:true},{catalogRoot:f.catalogRoot}),/external/);
    assert.equal(await readFile(external,'utf8'),'[paths]\n');
  }finally{await cleanup(f.root);}
});
test('cancelled import cleans its own partial package and never activates it',async()=>{
  const f=await fixture();const abort=new AbortController();
  try{
    const operation=setupKnowledge({...f,language:'de',acceptLocalIndexing:true},{catalogRoot:f.catalogRoot,signal:abort.signal,
      run:async(command,args,cwd,env,signal)=>{assert.equal(env.OPENAI_API_KEY,undefined);await writeFile(join(cwd,'partial'),'partial');
        setTimeout(()=>abort.abort(),10);await new Promise((resolve,reject)=>signal.addEventListener('abort',()=>reject(new Error('Cancelled')),{once:true}));}});
    await assert.rejects(operation,/Cancelled/);
    assert.deepEqual(await readdir(join(f.state,'knowledge/runs')),[]);
    await assert.rejects(readFile(join(f.state,'knowledge/state/active.json')),error=>error.code==='ENOENT');
    await assert.rejects(readFile(join(f.state,'knowledge/setup.lock')),error=>error.code==='ENOENT');
    await assert.rejects(setupKnowledge({...f,source:join(f.root,'foreign.mshc'),language:'de',acceptLocalIndexing:true},{catalogRoot:f.catalogRoot}),/discovered/);
  }finally{await cleanup(f.root);}
});
test('a newer installed revision offers an update while retaining the verified active index',async()=>{
  const f=await fixture();
  try {
    await knowledgeStatus(f,{catalogRoot:f.catalogRoot});
    const source=(await discoverHelp(f.catalogRoot))[0];
    const base=join(f.state,'knowledge'),packagePath=join(base,'runs/unit/package');await mkdir(packagePath,{recursive:true});
    const manifest='{"synthetic":true}';const pin=createHash('sha256').update(manifest).digest('hex');await writeFile(join(packagePath,'manifest.json'),manifest);
    await writeFile(join(base,'state/active.json'),JSON.stringify({active:{package_path:packagePath,manifest_sha256:pin,snapshot_id:'unit'}}));
    await writeFile(join(base,'receipt.json'),JSON.stringify({version:1,source,language:'de',topics:1,manifest:pin,snapshot:'unit'}));
    assert.equal((await knowledgeStatus(f,{catalogRoot:f.catalogRoot})).phase,'ready');
    await writeFile(join(dirname(f.source),'BKINFOSYS3_TEST_DE-DE.2.mshc'),'newly installed help');
    const changed=await knowledgeStatus(f,{catalogRoot:f.catalogRoot});assert.equal(changed.phase,'update');assert.equal(changed.ready,true);assert.equal(changed.revision,pin);
  } finally { await cleanup(f.root); }
});
test('Windows knowledge ownership excludes another import and reclaims a stale owned marker',{skip:process.platform!=='win32'},async()=>{
  const f=await fixture();let lease;
  try {
    lease=await acquireKnowledgeLease(f.state);
    await assert.rejects(acquireKnowledgeLease(f.state),/running|unavailable/);
    await lease.release();lease=null;
    await knowledgeStatus(f,{catalogRoot:f.catalogRoot});
    await writeFile(join(f.state,'knowledge/setup.lock'),JSON.stringify({version:1,kind:'native-knowledge-setup',pid:1234}));
    const abort=new AbortController();abort.abort();
    await assert.rejects(setupKnowledge({...f,language:'de',acceptLocalIndexing:true},{catalogRoot:f.catalogRoot,signal:abort.signal}),/Cancelled/);
    await assert.rejects(readFile(join(f.state,'knowledge/setup.lock')),error=>error.code==='ENOENT');
    lease=await acquireKnowledgeLease(f.state);await lease.release();lease=null;
  } finally {if(lease)await lease.release();await cleanup(f.root);}
});
