// Opt-in real-package regression. Inputs are already reviewed build artifacts;
// neither the installer entrypoint nor a registered IDE is invoked.
import assert from 'node:assert/strict';
import { spawn, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { once } from 'node:events';
import { readFile, writeFile, mkdir, lstat, readdir, realpath } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createServer } from 'node:net';
import { createHash, randomBytes } from 'node:crypto';
import { verifyManifest } from './application-manifest.mjs';
const run=promisify(execFile);
const [c0,c1,work]=process.argv.slice(2).map(resolvePath=>resolve(resolvePath));
if(process.platform!=='win32'||process.argv.length!==5) throw new Error('Supply actual C0 package, actual C1 package and fresh isolated output root on Windows');
const resume=process.env.TC_ROLLBACK_RESUME==='1';
if(createHash('sha256').update(await readFile(join(c0,'application-manifest.json'))).digest('hex')!=='e6fe29351f7d5c5100b6e92c9dafb7520efe00c3936d5c1f6e086c6bb77d2bc4') throw new Error('Use the actual first CI payload');
await verifyManifest(c0); await verifyManifest(c1); if(!resume) await mkdir(work);
const repo=resolve('.'), installation=join(work,'installation'), host=join(work,'synthetic-host'), state=join(work,'synthetic-state');
if(!resume) { await mkdir(join(host,'Common7/IDE/Extensions'),{recursive:true});
await writeFile(join(host,'Common7/IDE/Extensions/extensions.configurationchanged'),'synthetic-marker'); }
const env=Object.fromEntries(['SystemRoot','WINDIR','PATH','Path','PATHEXT','COMSPEC','TEMP','TMP','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','ProgramData','OS','PROCESSOR_ARCHITECTURE'].filter(k=>process.env[k]!==undefined).map(k=>[k,process.env[k]]));
env.DOTNET_ROOT=join(process.env.ProgramW6432??process.env.ProgramFiles,'dotnet');
const quote=s=>"'"+s.replaceAll("'","''")+"'";
async function transaction(action,pkg,inject) {
 const script=`$ErrorActionPreference='Stop'; . ${quote(join(repo,'scripts/installer/HostTransaction.ps1'))}; $target=@((Get-Content ${quote(join(repo,'native/host-targets.json'))} -Raw | ConvertFrom-Json).targets | Where-Object id -EQ 'tcxaeshell-x86-tc4024')[0]; Invoke-HostTransaction -Action ${action} -InstallRoot ${quote(installation)} -HostRoot ${quote(host)} -Interop 'C:/TwinCAT/3.1/Components/TcBlockDiagram/Common/TCatSysManagerLib.dll' -Package ${quote(pkg??c1)} -Target $target -PolicyStateRoot ${quote(state)} ${inject?'-InjectFailure '+quote(inject):''} | ConvertTo-Json -Compress`;
 const result=await run('powershell.exe',['-NoProfile','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{env,windowsHide:true,timeout:900000,maxBuffer:65536});
 return JSON.parse(result.stdout.trim());
}
const old=join(installation,'versions','0.1.0-dev.1','application'), current=join(installation,'versions','0.2.0-preview.2','application');
async function evalAt(root,body,args=[]) {
 const result=await run(join(root,'runtime/node/node.exe'),['--input-type=module','-e',body,...args],{env,windowsHide:true,cwd:work,timeout:600000,maxBuffer:65536});
 return JSON.parse(result.stdout.trim());
}
const moduleUrl=(root,name)=>pathToFileURL(join(root,'src',name+'.mjs')).href;
async function inspect(root,profile=state) { return evalAt(root,`const {inspectState}=await import(${JSON.stringify(moduleUrl(root,'state'))});const s=await inspectState(process.argv[1]);console.log(JSON.stringify({settings:s.settings,current:s.configurationCurrent}));`,[profile]); }
async function freePort(){const server=createServer();server.listen(0,'127.0.0.1');await once(server,'listening');const p=server.address().port;await new Promise(r=>server.close(r));return p;}
async function startup(root,profile=state) {
 const port=await freePort(); const p=spawn(join(root,'runtime/node/node.exe'),[join(root,'src/native-chat.mjs')],{env,windowsHide:true,cwd:work,stdio:['pipe','pipe','pipe']});
 const closed=once(p,'close');let text='',errors='';p.stdout.on('data',b=>text+=b);p.stderr.on('data',b=>errors+=b);
 p.stdin.write(JSON.stringify({state:profile,port,password:randomBytes(24).toString('hex'),mcpConfig:join(root,'runtime/mcp/default-settings.json')})+'\n');
 const deadline=Date.now()+300000;
 try {
  while(!text.includes('\n')&&p.exitCode===null&&Date.now()<deadline) await new Promise(r=>setTimeout(r,100));
  assert.deepEqual(JSON.parse(text.trim()||'null'),{status:'ready',port},errors);
  const response=await fetch(`http://127.0.0.1:${port}/auth/session`); assert.equal(response.status,401);
 } finally {p.stdin.end();let timer;await Promise.race([closed,new Promise(r=>timer=setTimeout(()=>{p.kill();r();},15000))]);clearTimeout(timer);await closed;}
}
async function projectAt(root) {
 return evalAt(root,`const {prepareNativeProject}=await import(${JSON.stringify(moduleUrl(root,'native-mcp'))});console.log(JSON.stringify(await prepareNativeProject(JSON.parse(process.argv[1]))));`,[JSON.stringify({state,mcpConfig:join(root,'runtime/mcp/default-settings.json'),solution:join(work,'Synthetic.sln'),project:join(work,'Synthetic.plcproj')})]);
}
async function helper(profile=state) {
 const p=spawn(join(current,'runtime/node/node.exe'),[join(current,'src/rollback-policy.mjs')],{env,windowsHide:true,cwd:work,stdio:['pipe','pipe','pipe']});
 const closed=once(p,'close');let text='',errors='';p.stdout.on('data',b=>text+=b);p.stderr.on('data',b=>errors+=b);
 p.stdin.write(JSON.stringify({state:profile,target:old,installation})+'\n');
 const deadline=Date.now()+600000;
 while(!text.includes('\n')&&p.exitCode===null&&Date.now()<deadline) await new Promise(r=>setTimeout(r,100));
 return {p,closed,text,errors,async finish(confirm=false){if(confirm)p.stdin.write('swap-complete\n');p.stdin.end();return (await closed)[0];}};
}
async function refused(profile=state) { const h=await helper(profile); const code=await h.finish();assert.notEqual(code,0);assert.equal(h.text,''); }
const record=resume?JSON.parse(await readFile(join(work,'evidence.json'),'utf8')):{kind:'real-C0-C1-policy-rollback',checks:[],sourceC0:JSON.parse(await readFile(join(c0,'ci-input-provenance.json'),'utf8')),operatorStateTouched:false,realIdeTouched:false};
async function pass(name,action){if(resume&&record.checks.some(c=>c.name===name&&c.status==='passed')){console.log('PRIOR PASS '+name);return;}await action();record.checks.push({name,status:'passed'});await writeFile(join(work,'evidence.json'),JSON.stringify(record,null,2)+'\n');console.log('PASS '+name);}
if(!resume) await pass('actual C0 package installed only into synthetic host',()=>transaction('Install',c0));
await writeFile(join(work,'Synthetic.sln'),'synthetic');await writeFile(join(work,'Synthetic.plcproj'),'<Project/>');
if(!resume) await pass('actual first CI C0 runtime protected startup and project workspace',async()=>{await startup(old);await projectAt(old);});
await pass('actual C1 package synthetic transaction update and protected runtime startup',async()=>{if(!resume || JSON.parse(await readFile(join(installation,'state.json'),'utf8')).current!=='0.2.0-preview.2') await transaction('Install',c1);await startup(current);await projectAt(current);});
const location=await projectAt(current), configFile=join(state,'config/opencode.json'), settingsFile=join(state,'settings.json');
const projectConfig=join(location.workspace,'opencode.json'), receiptFile=join(location.workspace,'project.json');
const sentinels=[join(state,'data','new-chat-after-C1'),join(state,'home','new-login-after-C1'),join(location.workspace,'new-binding-after-C1')];
for(const file of sentinels) await writeFile(file,'created after the C1 update and preserved');
const checkSentinels=async()=>{for(const f of sentinels)assert.equal(await readFile(f,'utf8'),'created after the C1 update and preserved');};
await pass('all four global scopes downgrade and are read by the actual old C0 runtime',async()=>{
 for(let bits=0;bits<4;bits++) {
  const profile=join(work,'scope-'+bits), engineering=join(work,'scope-engineering.json');
  await writeFile(engineering,JSON.stringify({readOnly:true,writeAllowlist:[],engineering:{enabled:true,backend:'configuredProjectFiles'}}));
  const settings={...(bits&2?{knowledge:{executable:join(current,'runtime/mcp/knowledge/twincat-knowledge.exe')}}:{}),...(bits&1?{engineering:{entrypoint:join(current,'runtime/mcp/engineering/packages/mcp/dist/index.js'),config:engineering}}:{}),provider:'openai',model:'openai/synthetic-model',sourceCodeDisclosureAccepted:true};
  await evalAt(current,`const {initializeState}=await import(${JSON.stringify(moduleUrl(current,'state'))});await initializeState(process.argv[1],JSON.parse(process.argv[2]));console.log('{}');`,[profile,JSON.stringify(settings)]);
  const h=await helper(profile);assert.equal(h.text,'{"status":"policy-ready"}\n',h.errors);
  for(const name of ['model-consent.lock','launcher.lock']) assert.ok(await lstat(join(profile,name)));
  assert.equal(await h.finish(true),0,'direct scope coordinator confirms its completed preparation');
  const status=await inspect(old,profile);assert.equal(status.current,true);assert.equal(status.settings.model,settings.model);assert.equal(status.settings.provider,'openai');
  if(bits&2)assert.equal(status.settings.knowledge.executable,join(old,'runtime/mcp/knowledge/twincat-knowledge.exe'));
  if(bits&1)assert.equal(status.settings.engineering.entrypoint,join(old,'runtime/mcp/engineering/packages/mcp/dist/index.js'));
 }
});
async function unchangedGlobal(action) {const before=await Promise.all([readFile(configFile),readFile(settingsFile)]);await action();const after=await Promise.all([readFile(configFile),readFile(settingsFile)]);assert.deepEqual(after,before);}
await pass('P2 global scope references including both pending-journal endpoints refuse without mutation',async()=>{
 const original=await Promise.all([readFile(configFile),readFile(settingsFile)]), journal=join(state,'.profile-update.json');
 for(const endpoint of ['live','before','after']) for(const fault of ['writable','implicit-backend','missing-config']) {
  const configPath=join(work,'p2-'+endpoint+'-'+fault+'.json');
  if(fault!=='missing-config')await writeFile(configPath,JSON.stringify({readOnly:fault!=='writable',writeAllowlist:[],engineering:{enabled:true,...(fault==='implicit-backend'?{}:{backend:'configuredProjectFiles'})}}));
  const good={settings:JSON.parse(original[1]),config:JSON.parse(original[0])};
  const settings={...good.settings,engineering:{entrypoint:join(current,'runtime/mcp/engineering/packages/mcp/dist/index.js'),config:configPath}};
  const config=await evalAt(current,`const {createOpenCodeConfig}=await import(${JSON.stringify(moduleUrl(current,'config'))});console.log(JSON.stringify(createOpenCodeConfig(JSON.parse(process.argv[1]))));`,[JSON.stringify(settings)]);
  try {
   if(endpoint==='live'){await writeFile(configFile,JSON.stringify(config));await writeFile(settingsFile,JSON.stringify(settings));}
   else await writeFile(journal,JSON.stringify({version:1,before:endpoint==='before'?{settings,config}:good,after:endpoint==='after'?{settings,config}:good}));
   const files=[configFile,settingsFile,...(endpoint==='live'?[]:[journal])],bytes=await Promise.all(files.map(f=>readFile(f)));
   await refused();assert.deepEqual(await Promise.all(files.map(f=>readFile(f))),bytes);await checkSentinels();
  } finally {await writeFile(configFile,original[0]);await writeFile(settingsFile,original[1]);if(endpoint!=='live'){const {unlink}=await import('node:fs/promises');await unlink(journal);}}
 }
});
await pass('unsupported engineering-only project refuses before global downgrade',async()=>{
 const [receiptBytes,configBytes]=await Promise.all([readFile(receiptFile),readFile(projectConfig)]);
 try {
  const receipt=JSON.parse(receiptBytes);delete receipt.settings.knowledge;await writeFile(receiptFile,JSON.stringify(receipt));
  const scoped=await evalAt(current,`const {createOpenCodeConfig}=await import(${JSON.stringify(moduleUrl(current,'config'))});const {analysisSystemPrompt,ANALYSIS_PROFILE}=await import(${JSON.stringify(moduleUrl(current,'agent-prompts'))});const c=createOpenCodeConfig(JSON.parse(process.argv[1]));c.agents[ANALYSIS_PROFILE].system=analysisSystemPrompt({knowledge:false,engineering:true,project:true});console.log(JSON.stringify({mcp:c.mcp,agents:c.agents}));`,[JSON.stringify({engineering:{entrypoint:receipt.settings.engineering.entrypoint,config:join(location.workspace,'engineering.json')}})]);
  await writeFile(projectConfig,JSON.stringify(scoped));
  const pending=join(state,'.profile-update.json');
  const globalPair={settings:JSON.parse(await readFile(settingsFile,'utf8')),config:JSON.parse(await readFile(configFile,'utf8'))};
  await writeFile(pending,JSON.stringify({version:1,before:globalPair,after:globalPair}));
  const pendingBytes=await readFile(pending);
  try{await unchangedGlobal(()=>refused());assert.deepEqual(await readFile(pending),pendingBytes);}finally{const {unlink}=await import('node:fs/promises');await unlink(pending);}
 } finally {await writeFile(receiptFile,receiptBytes);await writeFile(projectConfig,configBytes);}
});
await pass('unknown global prompt, widened rights and outstanding project journal refuse before mutation',async()=>{
 const original=await readFile(configFile), value=JSON.parse(original);
 const agent=Object.keys(value.agents)[0]; const changedPrompt=structuredClone(value);changedPrompt.agents[agent].system='unknown';
 const widened=structuredClone(value);widened.agents[agent].permissions.push({action:'edit',resource:'*',effect:'allow'});
 for(const modified of [{...value,foreign:true},changedPrompt,widened]) {
  await writeFile(configFile,JSON.stringify(modified));await unchangedGlobal(()=>refused());
 }
 await writeFile(configFile,original);
 const journal=join(location.workspace,'.configuration.json');
 const receipt=JSON.parse(await readFile(receiptFile,'utf8')), selected=receipt.settings;
 for(const value of [null,false,0,{version:1,solution:receipt.solution,project:receipt.project,before:selected,after:selected}]) {
  await writeFile(journal,JSON.stringify(value));await unchangedGlobal(()=>refused());await checkSentinels();
 }
 await projectAt(current);await assert.rejects(lstat(journal),{code:'ENOENT'});
});
await pass('C1 alone recovers a mixed C1-to-C0 profile journal before any completed rollback',async()=>{
 const before={settings:JSON.parse(await readFile(settingsFile,'utf8')),config:JSON.parse(await readFile(configFile,'utf8'))};
 const after=await evalAt(current,`const {createOpenCodeConfig}=await import(${JSON.stringify(moduleUrl(current,'config'))});const {C0_ANALYSIS_PROMPTS}=await import(${JSON.stringify(moduleUrl(current,'analysis-prompts-c0'))});const {ANALYSIS_PROFILE}=await import(${JSON.stringify(moduleUrl(current,'agent-prompts'))});const {join}=await import('node:path');const s=JSON.parse(process.argv[1]),old=process.argv[2];if(s.knowledge)s.knowledge.executable=join(old,'runtime/mcp/knowledge/twincat-knowledge.exe');if(s.engineering)s.engineering.entrypoint=join(old,'runtime/mcp/engineering/packages/mcp/dist/index.js');const config=createOpenCodeConfig(s,{node:join(old,'runtime/node/node.exe'),knowledgeClient:join(old,'src/knowledge-client.mjs')});config.agents[ANALYSIS_PROFILE].system=C0_ANALYSIS_PROMPTS.profile[2*Number(Boolean(s.knowledge))+Number(Boolean(s.engineering))];console.log(JSON.stringify({settings:s,config}));`,[JSON.stringify(before.settings),old]);
 // Canonicalize package path separators as the real downgrade writer does.
 if(after.settings.knowledge)after.settings.knowledge.executable=join(old,'runtime/mcp/knowledge/twincat-knowledge.exe');
 if(after.settings.engineering)after.settings.engineering.entrypoint=join(old,'runtime/mcp/engineering/packages/mcp/dist/index.js');
 const journal=join(state,'.profile-update.json');await writeFile(journal,JSON.stringify({version:1,before,after}));await writeFile(configFile,JSON.stringify(after.config));
 // The old runtime is deliberately NOT started on this C1-containing journal.
 const h=await helper();assert.equal(h.text,'{"status":"policy-ready"}\n',h.errors);await assert.rejects(lstat(journal),{code:'ENOENT'});assert.equal(await h.finish(),1);
 await projectAt(current);assert.equal((await inspect(current)).current,true);await checkSentinels();
});
await pass('rehashed target with unknown C0 source still refuses capability',async()=>{
 const sourceFile=join(old,'src/agent-prompts.mjs'), manifestFile=join(old,'application-manifest.json');
 const [bytes,manifestBytes]=await Promise.all([readFile(sourceFile),readFile(manifestFile)]);
 try {
  const changed=Buffer.concat([bytes,Buffer.from('\n// unknown source\n')]);await writeFile(sourceFile,changed);
  const manifest=JSON.parse(manifestBytes);const entry=manifest.files.find(f=>f.path==='src/agent-prompts.mjs');entry.size=changed.length;entry.sha256=createHash('sha256').update(changed).digest('hex');await writeFile(manifestFile,JSON.stringify(manifest,null,2)+'\n');
  await unchangedGlobal(()=>refused());
 } finally {await writeFile(sourceFile,bytes);await writeFile(manifestFile,manifestBytes);}
});
for(const fault of ['after-policy','after-swap']) await pass('caught '+fault+' failure keeps C1 selected with valid global/project state',async()=>{
 await assert.rejects(transaction('Rollback',null,fault));
 assert.equal(JSON.parse(await readFile(join(installation,'state.json'),'utf8')).current,'0.2.0-preview.2');
 await projectAt(current);assert.equal((await inspect(current)).current,true);await checkSentinels();
});
await pass('integrated rollback swaps real product files then starts actual old C0 on latest same state',async()=>{
 const result=await transaction('Rollback');assert.equal(result.current,'0.1.0-dev.1');
 await startup(old);await projectAt(old);assert.equal((await inspect(old)).current,true);await checkSentinels();
 for(const file of [join(state,'.profile-update.json'),join(location.workspace,'.configuration.json'),join(state,'model-consent.lock'),join(state,'launcher.lock')]) await assert.rejects(lstat(file),{code:'ENOENT'});
});
await verifyManifest(old);await verifyManifest(current);
record.currentManifestSha256=createHash('sha256').update(await readFile(join(current,'application-manifest.json'))).digest('hex');
record.completed=true;await writeFile(join(work,'evidence.json'),JSON.stringify(record,null,2)+'\n');console.log('Real-package rollback evidence: '+join(work,'evidence.json'));
