import { readFile, lstat, realpath, readdir, open, unlink } from 'node:fs/promises';
import { join, dirname, resolve, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { createInterface } from 'node:readline';
import { verifyManifest } from '../scripts/application-manifest.mjs';
import { applicationRoot, requirePackage, verifyPackageIfPresent } from './package-integrity.mjs';
import { readJsonFile, statePaths, inspectState } from './state.mjs';
import { createOpenCodeConfig, validateSettings } from './config.mjs';
import { ANALYSIS_PROFILE, analysisSystemPrompt, previousAnalysisPrompts } from './agent-prompts.mjs';
import { C0_ANALYSIS_PROMPTS } from './analysis-prompts-c0.mjs';
import { recoverProfileLocked, withProfileLock, writeProfilePair } from './profile-update.mjs';
import { projectKey, withProjectLock, updateProjectConfiguration } from './project-workspace.mjs';

// Actual first CI 46f96a source, UTF-8 with CRLF normalized to LF. Full package
// byte inventories are verified first. No source from the target is imported.
const C0_SOURCES = {
 'agent-prompts':'0a551dda22e22dc14e81bcfb9be22cc33ffd12a71971a5ef25a335af6458c02e',
 config:'bdda27eb5457a708696f0ccc702fda6f62cde94c9c9879ca910021c633559c06',
 'native-mcp':'257b08ecc06a277c4e6977b77c11c8793ec115e3bd00da131a236b479bd63e7d',
 'project-workspace':'61af816e19d4bc095fe20e047f96131add63034687736653ae43416770fcc6df',
 state:'fb620bf4edd81924aa6d6aa4aab0945a679c0a7b47f0e5d9fec38b1a9d650005',
 'managed-relocation':'d2c4a092349ea852533cd96b39681664f65f7577204dba777cecec216c81304b',
 'profile-update':'fb201beee32d47a83cbd38fb06f24fc8b54a3187d3a14b1b1176814282dffea1',
};
const hash = text => createHash('sha256').update(text).digest('hex');
const same = (a,b) => JSON.stringify(a) === JSON.stringify(b);
const equalPath = (a,b) => typeof a === 'string' && typeof b === 'string'
  && (process.platform === 'win32' ? resolve(a).toLowerCase() === resolve(b).toLowerCase() : resolve(a) === resolve(b));
const source = async (root,name) => (await readFile(join(root,'src',name+'.mjs'),'utf8')).replaceAll('\r\n','\n');
async function directory(path) {
 const s=await lstat(path);
 if (!s.isDirectory() || s.isSymbolicLink() || !equalPath(await realpath(path),path)) throw new Error('Redirected rollback directory');
}
async function exists(path) {
 try { await lstat(path); return true; } catch(e) { if(e.code==='ENOENT') return false; throw e; }
}
export async function verifyRollbackTarget(target, installation) {
 if (!requirePackage || !equalPath(process.execPath,join(applicationRoot,'runtime/node/node.exe'))) throw new Error('Use the currently installed verified runtime');
 await verifyPackageIfPresent();
 for (const p of [installation,join(installation,'versions'),dirname(applicationRoot),applicationRoot,dirname(target),target]) await directory(p);
 if (!equalPath(dirname(dirname(target)),join(installation,'versions'))
     || !equalPath(dirname(dirname(applicationRoot)),join(installation,'versions'))
     || !equalPath(target,join(dirname(target),'application')) || equalPath(target,applicationRoot)) throw new Error('Rollback requires distinct owned sibling packages');
 const owner=await lstat(join(installation,'.assistant-install-root'));
 if(!owner.isFile() || owner.isSymbolicLink() || owner.size>128) throw new Error('Invalid rollback owner marker');
 if ((await readFile(join(installation,'.assistant-install-root'),'utf8')) !== 'TwinCatAssistant installation v1') throw new Error('Unowned rollback installation');
 const selected=await readJsonFile(join(installation,'state.json'));
 const current=await verifyManifest(applicationRoot), old=await verifyManifest(target);
 if (selected.targetId!=='tcxaeshell-x86-tc4024' || selected.current!==current.version || selected.previous!==old.version
   || dirname(target).split(/[\\/]/).at(-1)!==old.version || dirname(applicationRoot).split(/[\\/]/).at(-1)!==current.version
   || old.architecture!==current.architecture || old.platform!==current.platform) throw new Error('Rollback selection mismatch');
 if (!same(await readJsonFile(join(target,'src/package-runtime.json')),{version:1})) throw new Error('Invalid rollback package marker');
 const required=['runtime/node/node.exe','src/knowledge-client.mjs','runtime/mcp/knowledge/twincat-knowledge.exe','runtime/mcp/engineering/packages/mcp/dist/index.js'];
 for(const m of [current,old]) if(required.some(p=>!m.files.some(f=>f.path===p))) throw new Error('Missing rollback entrypoint');
 for(const [name,digest] of Object.entries(C0_SOURCES)) if(hash(await source(target,name))!==digest) throw new Error('Unknown C0 target capability');
 // Current generator must still implement the exact reviewed C0 non-prompt
 // contract, not just expose a similarly named API or version.
 const factory=await source(applicationRoot,'config');
 const a=factory.indexOf('export function createOpenCodeConfig('), b=factory.indexOf('// Exact configurations',a);
 if(a<0 || b<0 || hash(factory.slice(a,b))!=='08c59b7ba2903856e2263bf20ffc86a286e9515e380f588ad8661dc2e3206479') throw new Error('Unreviewed configuration generator');
 return {current:applicationRoot,target};
}
function settingsAt(settings, root) {
 validateSettings(settings);
 return {...settings,
  ...(settings.knowledge ? {knowledge:{...settings.knowledge,executable:join(root,'runtime/mcp/knowledge/twincat-knowledge.exe')}} : {}),
  ...(settings.engineering ? {engineering:{...settings.engineering,entrypoint:join(root,'runtime/mcp/engineering/packages/mcp/dist/index.js')}} : {})};
}
function runtime(root) { return {node:join(root,'runtime/node/node.exe'),knowledgeClient:join(root,'src/knowledge-client.mjs')}; }
function generated(settings, root, project, system) {
 const config=createOpenCodeConfig(settings,runtime(root));
 const scope={knowledge:Boolean(settings.knowledge),engineering:Boolean(settings.engineering),project};
 config.agents[ANALYSIS_PROFILE].system=system ?? analysisSystemPrompt(scope);
 return project ? {mcp:config.mcp,agents:config.agents} : config;
}
function c0(settings, project) {
 return C0_ANALYSIS_PROMPTS[project?'project':'profile'][project?Number(Boolean(settings.knowledge)):2*Number(Boolean(settings.knowledge))+Number(Boolean(settings.engineering))];
}
function rootsFor(settings, packages) {
 const matches=[packages.current,packages.target].filter(root=>same(settingsAt(settings,root),settings));
 if(!matches.length) throw new Error('Foreign executable settings');
 return matches;
}
function known(actual, settings, project, packages) {
 return rootsFor(settings,packages).some(root=>[analysisSystemPrompt({knowledge:Boolean(settings.knowledge),engineering:Boolean(settings.engineering),project}),
  ...previousAnalysisPrompts({knowledge:Boolean(settings.knowledge),engineering:Boolean(settings.engineering),project})]
  .some(system=>same(actual,generated(settings,root,project,system))));
}
async function projectPlan(parent, entry, packages) {
 const workspace=join(parent,entry); await directory(workspace);
 const receipt=await readJsonFile(join(workspace,'project.json'));
 if(![1,2].includes(receipt.version) || Object.keys(receipt).some(k=>!['version','solution','project','settings'].includes(k))
   || !isAbsolute(receipt.solution??'') || !isAbsolute(receipt.project??'')) throw new Error('Unknown project receipt');
 if(await exists(join(workspace,'.configuration.json'))) throw new Error('Recover pending project journal with C1 before rollback');
 const selected=receipt.settings;
 if(!selected || Object.keys(selected).some(k=>!['knowledge','engineering'].includes(k))
   || !selected.knowledge || !selected.engineering || Object.keys(selected.engineering).join(',')!=='entrypoint') throw new Error('C0 native project requires both MCPs');
 const engineeringConfig=join(workspace,'engineering.json');
 const engineering=await readJsonFile(engineeringConfig);
 const expected={targetAmsNetId:'127.0.0.1.1.1',readOnly:true,writeAllowlist:[],engineering:{enabled:true,backend:'configuredProjectFiles',projectFiles:[{path:receipt.project,type:'plc'}]}};
 if(!same(engineering,expected)) throw new Error('Project saved-file boundary modified');
 const full=value=>({...value,engineering:{...value.engineering,config:engineeringConfig}});
 const actual=await readJsonFile(join(workspace,'opencode.json'));
 if(!known(actual,full(selected),true,packages)) throw new Error('Unknown project policy');
 const after=settingsAt(full(selected),packages.target); delete after.engineering.config;
 const configuration=value=>generated(full(value),packages.target,true,c0(full(value),true));
 const previous=(value,config)=>known(config,full(value),true,packages)?[config]:[];
 return {workspace,receipt,selected,after,configuration,previous,key:projectKey(receipt.solution,receipt.project)};
}
export async function holdPolicyRollback({state,target,installation}, ready) {
 for(const value of [state,target,installation]) if(typeof value!=='string' || !isAbsolute(value) || value.length>2048 || value.includes('\0')) throw new Error('Absolute rollback paths required');
 const packages=await verifyRollbackTarget(resolve(target),resolve(installation));
 const paths=statePaths(state);
 try { await lstat(paths.root); } catch(e) { if(e.code==='ENOENT') return ready(); throw e; }
 await directory(paths.root);
 const launcherPath=join(paths.root,'launcher.lock');
 const launcher=await open(launcherPath,'wx',0o600);
 try {
  return await withProfileLock(state,async()=>{
   const parent=join(paths.root,'projects');
   let entries=[];
   try { await directory(parent); entries=await readdir(parent,{withFileTypes:true}); } catch(e) { if(e.code!=='ENOENT') throw e; }
   const dirs=entries.filter(e=>e.isDirectory() || e.isSymbolicLink());
   if(dirs.length>256 || dirs.some(e=>!e.isDirectory() || !/^[a-f0-9]{64}$/.test(e.name))) throw new Error('Unknown project directory');
   // Discover keys before acquiring all locks, then rebuild/validate each plan
   // under those locks and require the directory inventory to remain unchanged.
   const receipts=await Promise.all(dirs.map(e=>readJsonFile(join(parent,e.name,'project.json'))));
   const keys=[...new Set(receipts.map(r=>projectKey(r.solution,r.project)))].sort();
   async function locked(index) {
    if(index<keys.length) return withProjectLock(parent,keys[index],()=>locked(index+1));
    const fresh=(await readdir(parent).catch(e=>{if(e.code==='ENOENT')return [];throw e;})).filter(n=>/^[a-f0-9]{64}$/.test(n)).sort();
    if(!same(fresh,dirs.map(e=>e.name).sort())) throw new Error('Project inventory changed during rollback');
    const plans=[];
    for(const entry of dirs) {
     const plan=await projectPlan(parent,entry.name,packages);
     if(!keys.includes(plan.key)) throw new Error('Project lock identity changed');
     plans.push(plan);
    }
    // A pre-existing global journal can itself contain a downgrade. Do not
    // recover it until every project scope/journal has passed preflight.
    await recoverProfileLocked(state);
    const current=await inspectState(state,{recovery:false});
    const before={settings:current.settings,config:await readJsonFile(paths.configPath)};
    if(!known(before.config,before.settings,false,packages)) throw new Error('Unknown global policy');
    const settings=settingsAt(before.settings,packages.target);
    const after={settings,config:generated(settings,packages.target,false,c0(settings,false))};
    await writeProfilePair(state,before,after);
    for(const p of plans) await updateProjectConfiguration(p.workspace,p.receipt,p.after,p.configuration,p.previous,()=>false);
    return ready(); // installer keeps this promise pending until swap is confirmed
   }
   return locked(0);
  });
 } finally { await launcher.close(); await unlink(launcherPath); }
}
async function main() {
 const lines=createInterface({input:process.stdin,crlfDelay:Infinity});
 const iterator=lines[Symbol.asyncIterator]();
 const first=await iterator.next();
 if(first.done || first.value.length>8192) throw new Error('Bounded rollback request required');
 const request=JSON.parse(first.value);
 if(Object.keys(request).sort().join(',')!=='installation,state,target') throw new Error('Unknown rollback request');
 try {
  await holdPolicyRollback(request,async()=>{
   process.stdout.write('{"status":"policy-ready"}\n');
   const result=await iterator.next();
   if(result.done || result.value!=='swap-complete') throw new Error('Installer swap not confirmed; only C1 may recover');
  });
 } finally { lines.close(); }
}
if(process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
 main().catch(()=>{process.stderr.write('Policy rollback refused or interrupted; retain journals and use C1 recovery.\n');process.exitCode=1;});
}
