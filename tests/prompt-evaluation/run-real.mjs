// Opt-in Windows real-provider acceptance; starts only new synthetic sessions.
// Uses the runtime-owned existing credential database without reading secrets.
// TC_EVAL_MAIN and TC_ASSISTANT_WINDOWS_HELPER must select reviewed artifacts.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { tmpdir } from 'node:os';
import { createServer } from 'node:net';
import { randomUUID, createHash } from 'node:crypto';
import { spawn, execFileSync } from 'node:child_process';
import { runtimeEnvironment, initializeState } from '../../src/state.mjs';
import { createOpenCodeConfig } from '../../src/config.mjs';
import { composeCase, loadCases } from './evaluate.mjs';
const main = process.env.TC_EVAL_MAIN;
if(!main) throw new Error('TC_EVAL_MAIN must select the reviewed main checkout');
const operator=join(process.env.LOCALAPPDATA,'TwinCatAssistant/operator-state');
const consent=JSON.parse(await readFile(join(operator,'settings.json'),'utf8'));
if(consent.provider!=='github-copilot'||consent.sourceCodeDisclosureAccepted!==true)throw new Error('Existing provider consent required');
const root=join(tmpdir(),'tc-prompt-evaluation-'+randomUUID());
await initializeState(root,{provider:consent.provider,sourceCodeDisclosureAccepted:true});
const receipt=JSON.parse(await readFile(join(main,'artifacts/source-runtime/runtime.json'),'utf8'));
for(const path of [receipt.opencode.path,receipt.openchamber.path,'local-data/upstream/openchamber/packages/web/dist/index.html']) {
 if(createHash('sha256').update(await readFile(join(main,path))).digest('hex')!==receipt.hashes[path])throw new Error('Runtime receipt hash mismatch');
}
const lock=JSON.parse(await readFile('upstream/source-lock.json'));
for(const name of ['opencode','openchamber'])if(receipt[name].revision!==lock[name].revision)throw new Error('Pinned revision mismatch');
const env=runtimeEnvironment(root,{provider:consent.provider,sourceCodeDisclosureAccepted:true});
// Runtime owns credential loading. Never read or copy authentication storage.
env.XDG_DATA_HOME=join(operator,'data');
env.OPENCODE_BINARY=join(main,receipt.opencode.path);
const password=randomUUID()+randomUUID();env.OPENCHAMBER_UI_PASSWORD=password;
const socket=createServer();await new Promise(r=>socket.listen(0,'127.0.0.1',r));const port=socket.address().port;await new Promise(r=>socket.close(r));const origin='http://127.0.0.1:'+port;
const child=spawn(process.execPath,[join(main,receipt.openchamber.path),'serve','--foreground','--host','127.0.0.1','--port',String(port)],{cwd:join(root,'workspace'),env,windowsHide:true,stdio:'ignore'});
let cookie;
async function api(path,body,directory=join(root,'workspace')) {
 const r=await fetch(origin+path+(path.includes("?")?"&":"?")+"location[directory]="+encodeURIComponent(directory),{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json',origin,...(cookie?{cookie}:{}),'x-opencode-directory':encodeURIComponent(directory),'x-opencode-directory-encoding':'uri'},...(body===undefined?{}:{body:JSON.stringify(body)})});
 if(!r.ok)throw new Error('Runtime HTTP '+r.status+' at '+path);
 if(path==='/auth/session') {cookie=r.headers.get('set-cookie').split(';')[0];return;}
 return r.status===204?null:r.json();
}
try {
 for(let i=0;i<180;i++){try{await api('/auth/session',{password});break;}catch{await new Promise(r=>setTimeout(r,500));}}
 if(!cookie)throw new Error('Protected runtime not ready');
 console.log('Protected runtime ready; fixture '+root);
 await api('/api/config'); await api('/api/mcp'); await new Promise(r=>setTimeout(r,3000));
 if(!(await api('/api/provider')).data.some(p=>p.id===consent.provider))throw new Error('Consented provider unavailable');


 const models=(await api('/api/model')).data;
 const selected=models.find(m=>m.providerID===consent.provider&&m.id==='gpt-6.1-sol');
 if(!selected)throw new Error('Consented gpt-6.1-sol model unavailable');
 const model={providerID:selected.providerID,id:selected.id,variant:'high'};
 console.log('Selected '+model.providerID+'/'+model.id+' high');
 const fixture=await loadCases();
 const output=resolve('tests/prompt-evaluation/evidence/2026-11-05-c1');await mkdir(output,{recursive:true});
 let answers={};try{answers=JSON.parse(await readFile(join(output,'answers.json')));}catch{}
 for(const c of fixture.cases){ if(process.env.TC_EVAL_CASE && !process.env.TC_EVAL_CASE.split(',').includes(c.id))continue;
 const dir=join(root,'workspace',c.id);await mkdir(dir,{recursive:true});
 const input=composeCase(fixture,c); const caseFile=join(dir,'case.json'),trace=join(dir,'tools.jsonl');
 await writeFile(caseFile,JSON.stringify(input,null,2));
 const config=createOpenCodeConfig({provider:consent.provider,sourceCodeDisclosureAccepted:true});
 config.agents['twincat-readonly'].system=input.system;
 for(const scope of ['knowledge','engineering']) if(c.scope[scope])config.mcp.servers[scope]={type:'local',protocol:'legacy',codemode:false,command:[process.execPath,resolve('tests/prompt-evaluation/fixture-mcp.mjs'),caseFile,scope,trace]};
 await writeFile(join(dir,'opencode.json'),JSON.stringify(config,null,2));
 const actual=await api('/api/config',undefined,dir);await api('/api/agent',undefined,dir);let mcp; for(let i=0;i<100;i++){mcp=await api('/api/mcp',undefined,dir);if(mcp.data.length===Object.keys(config.mcp.servers).length&&mcp.data.every(s=>s.status.status==='connected'))break;await new Promise(r=>setTimeout(r,300));} if(mcp.data.length!==Object.keys(config.mcp.servers).length||mcp.data.some(s=>s.status.status!=='connected'))throw new Error('Fixture MCP not connected '+c.id+JSON.stringify(mcp)); let agent;for(let i=0;i<60;i++){try{agent=await api('/api/agent/twincat-readonly',undefined,dir);break;}catch{await new Promise(r=>setTimeout(r,300));}} if(agent.data?.system!==input.system)throw new Error('Actual system prompt differs '+c.id); await new Promise(r=>setTimeout(r,1000)); await writeFile(join(output,c.id+'-runtime.json'),JSON.stringify({actual,mcp,agent},null,2));
 const session=(await api('/api/session',{title:'Synthetic prompt acceptance '+c.id,agent:'twincat-readonly',model,location:{directory:dir}},dir)).data;
 await api('/api/session/'+session.id+'/prompt',{text:[...input.parts,input.question].join('\n\n')},dir);
 let messages,done=false;
 for(let i=0;i<240;i++){
 await new Promise(r=>setTimeout(r,1000));
 messages=await api('/api/session/'+session.id+'/message',undefined,dir);
 const list=messages.data??messages;

 const state=(await api('/api/session/'+session.id,undefined,dir)).data;
 if(state?.outcome==='succeeded' && list.some(m=>m.type==='assistant'&&m.content?.some(p=>p.type==='text'))){done=true;break;}
 if(state?.outcome==='failed')throw new Error('Synthetic session failed '+c.id);
 }
 if(!done){await writeFile(join(output,c.id+'-diagnostic.json'),JSON.stringify(messages,null,2));throw new Error('Model timeout '+c.id);}
 const list=messages.data??messages;
 const text=list.filter(m=>m.type==='assistant').flatMap(m=>(m.content??[]).filter(p=>p.type==='text').map(p=>p.text)).join('\n\n');
 answers[c.id]=text;
 let tools=[];try{tools=(await readFile(trace,'utf8')).trim().split('\n').map(JSON.parse);}catch{}
 await writeFile(join(output,c.id+'.json'),JSON.stringify({id:c.id,date:new Date().toISOString(),model,sessionId:session.id,input,tools,messages: list.map(m=>m.type==='assistant'?{id:m.id,type:m.type,model:m.model,time:m.time,finish:m.finish,tokens:m.tokens,content:m.content.filter(p=>p.type!=='reasoning').map(({state,...p})=>p)}:m)},null,2));
 await writeFile(join(output,'answers.json'),JSON.stringify(answers,null,2));
 console.log('Captured '+c.id+' ('+tools.length+' tools, '+text.length+' chars)');
 }
 await writeFile(join(output,'run.json'),JSON.stringify({date:new Date().toISOString(),model,receipt,fixtureRoot:root,credentialHandling:'Runtime loaded original credential storage; evaluator never read credential values',sessionDatabase:'Existing operator database, own new synthetic sessions only'},null,2));

} finally {try{execFileSync('taskkill',['/PID',String(child.pid),'/T','/F'],{stdio:'ignore',windowsHide:true});}catch{child.kill();}}
