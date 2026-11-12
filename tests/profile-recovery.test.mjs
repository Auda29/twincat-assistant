import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initializeState, inspectState } from '../src/state.mjs';
import { createOpenCodeConfig } from '../src/config.mjs';
import { updateProfile } from '../src/profile-update.mjs';
const json = value => JSON.stringify(value, null, 2) + '\n';
for (const phase of ['journal', 'config', 'settings']) test('profile recovery after ' + phase + ' commit boundary preserves operator data', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-profile-recover-'));
  try {
    const root = join(parent, 'profile'), before = {}, after = { provider: 'openai', sourceCodeDisclosureAccepted: true };
    const paths = await initializeState(root, before);
    const sentinel = join(paths.data, 'chat-login-binding'); await writeFile(sentinel, 'keep');
    await writeFile(join(root, '.profile-update.json'), json({version:1,before:{settings:before,config:createOpenCodeConfig(before)},after:{settings:after,config:createOpenCodeConfig(after)}}));
    if (phase !== 'journal') await writeFile(paths.configPath, json(createOpenCodeConfig(after)));
    if (phase === 'settings') await writeFile(paths.settingsPath, json(after));
    assert.deepEqual((await inspectState(root)).settings, after);
    assert.equal(await readFile(sentinel, 'utf8'), 'keep');
    await assert.rejects(readFile(join(root, '.profile-update.json')), {code:'ENOENT'});
    await updateProfile(root, () => before);
    assert.deepEqual((await inspectState(root)).settings, before);
  } finally { await rm(parent,{recursive:true,force:true}); }
});
test('profile recovery refuses unknown policy, foreign live files and active or stale locks without rewriting', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-profile-reject-'));
  try {
    const root=join(parent,'profile'), settings={}; const paths=await initializeState(root,settings);
    const config=createOpenCodeConfig(settings), pair={settings,config};
    const journal=join(root,'.profile-update.json');
    await writeFile(journal,json({version:1,before:pair,after:pair}));
    const original=await readFile(paths.configPath);
    await writeFile(join(root,'model-consent.lock'),'');
    await assert.rejects(inspectState(root),{code:'EEXIST'});
    await rm(join(root,'model-consent.lock'));
    await writeFile(paths.configPath,json({...config,foreign:true}));
    await assert.rejects(inspectState(root),/Profile changed/);
    await writeFile(paths.configPath,original);
    await writeFile(journal,json({version:1,before:pair,after:{settings,config:{...config,foreign:true}}}));
    await assert.rejects(inspectState(root),/policy mismatch/);
    assert.deepEqual(await readFile(paths.configPath),original);
  } finally { await rm(parent,{recursive:true,force:true}); }
});

test('a killed profile writer leaves a lock; coordinated lock removal allows validated recovery', async () => {
  const parent=await mkdtemp(join(tmpdir(),'tc-profile-killed-'));
  let child;
  try {
    const root=join(parent,'profile'), before={}, after={provider:'openai',sourceCodeDisclosureAccepted:true};
    const paths=await initializeState(root,before);
    await writeFile(join(paths.data,'chat-sentinel'),'kept');
    const journal={version:1,before:{settings:before,config:createOpenCodeConfig(before)},after:{settings:after,config:createOpenCodeConfig(after)}};
    const code=`import {open} from 'node:fs/promises'; import {atomicJson} from ${JSON.stringify(pathToFileURL(join(process.cwd(),'src/profile-update.mjs')).href)}; const [root,config]=process.argv.slice(1); let journal=''; for await(const chunk of process.stdin) journal+=chunk; await open(root+'/model-consent.lock','wx'); await atomicJson(root+'/.profile-update.json',JSON.parse(journal)); await atomicJson(config,JSON.parse(journal).after.config); process.stdout.write('ready\\n'); setInterval(()=>{},1000);`;
    child=spawn(process.execPath,['--input-type=module','-e',code,root,paths.configPath],{windowsHide:true,stdio:['pipe','pipe','pipe']});
    child.stdin.end(JSON.stringify(journal));
    await Promise.race([once(child.stdout,'data'),once(child,'exit').then(()=>{throw new Error('Writer exited before crash boundary')}),new Promise((_,reject)=>{const timer=setTimeout(()=>reject(new Error('Writer boundary timeout')),15000); timer.unref();})]);
    const exited=once(child,'exit'); child.kill(); await exited; child=null;
    await assert.rejects(inspectState(root),{code:'EEXIST'});
    // This explicit action represents operator-established writer quiescence.
    await rm(join(root,'model-consent.lock'));
    assert.deepEqual((await inspectState(root)).settings,after);
    assert.equal(await readFile(join(paths.data,'chat-sentinel'),'utf8'),'kept');
  } finally { if(child) { const exited=once(child,'exit'); child.kill(); await exited; } await rm(parent,{recursive:true,force:true}); }
});

for (const endpoint of ['before','after']) for (const fault of ['writable','implicit-backend','missing-config']) {
  test(`journal ${endpoint} ${fault} scope refuses before any config/settings/journal mutation`, async () => {
    const parent=await mkdtemp(join(tmpdir(),'tc-journal-scope-'));
    try {
      const root=join(parent,'profile'), executable=join(parent,'mcp.mjs');await writeFile(executable,'// never executed');
      const config=join(parent,'engineering.json');
      if(fault!=='missing-config') await writeFile(config,json({readOnly:fault!=='writable',writeAllowlist:[],engineering:{enabled:true,...(fault==='implicit-backend'?{}:{backend:'configuredProjectFiles'})}}));
      const good={provider:'openai',sourceCodeDisclosureAccepted:true};
      const bad={...good,engineering:{entrypoint:executable,config}};
      const paths=await initializeState(root,good), sentinel=join(paths.data,'latest-chat-login-binding');await writeFile(sentinel,'latest');
      const pair=settings=>({settings,config:createOpenCodeConfig(settings)});
      const journal={version:1,before:pair(endpoint==='before'?bad:good),after:pair(endpoint==='after'?bad:good)};
      const pending=join(root,'.profile-update.json');await writeFile(pending,json(journal));
      // The live pair is valid. Merely inspecting live settings would miss the
      // unsafe, non-live endpoint which recovery must also validate.
      const files=[paths.configPath,paths.settingsPath,pending,sentinel];
      const bytes=await Promise.all(files.map(file=>readFile(file)));
      await assert.rejects(inspectState(root),fault==='writable'?/readOnly/:fault==='implicit-backend'?/configuredProjectFiles/:/ENOENT|bounded regular JSON/);
      assert.deepEqual(await Promise.all(files.map(file=>readFile(file))),bytes);
    } finally {await rm(parent,{recursive:true,force:true});}
  });
}

test('profile update refuses unsafe after scope before creating a journal',async()=>{
 const parent=await mkdtemp(join(tmpdir(),'tc-write-scope-'));
 try {
  const paths=await initializeState(join(parent,'profile'),{}), executable=join(parent,'mcp.mjs'),config=join(parent,'engineering.json');
  await writeFile(executable,'// never executed');await writeFile(config,json({readOnly:false,engineering:{enabled:true,backend:'configuredProjectFiles'}}));
  const bytes=await Promise.all([paths.configPath,paths.settingsPath].map(file=>readFile(file)));
  await assert.rejects(updateProfile(paths.root,()=>({engineering:{entrypoint:executable,config}})),/readOnly/);
  assert.deepEqual(await Promise.all([paths.configPath,paths.settingsPath].map(file=>readFile(file))),bytes);
  await assert.rejects(readFile(join(paths.root,'.profile-update.json')),{code:'ENOENT'});
 } finally {await rm(parent,{recursive:true,force:true});}
});
