import { cp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { resolve, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { createManifest } from './application-manifest.mjs';
const run=promisify(execFile);
// Manual, Windows-only acceptance measurement. Inputs are read-only; all writes
// stay in a new synthetic staging tree. No installation, IDE, provider or PLC use.
const [oldInput, newInput, output] = process.argv.slice(2);
if (!oldInput || !newInput || !output || process.platform !== 'win32') throw new Error('Usage: node scripts/Measure-PackageVerificationStartup.mjs <C0-application> <input-application> <new-output-directory>');
const base=resolve(output);
await mkdir(base); // Refuse to adopt an existing directory.
await cp(resolve('tests/fixtures/package-verification-native.ps1'),join(base,'native.ps1'));
const install=join(base,'install');
const old=join(install,'versions/0.1.0-dev.1/application');
const current=join(install,'versions/0.0.0-scope-synthetic/application');
const input=resolve(newInput);
console.log('input manifest',createHash('sha256').update(await readFile(join(input,'application-manifest.json'))).digest('hex'));
await mkdir(install,{recursive:true});
for(const [from,to] of [[resolve(oldInput),old],[input,current]]) {
 console.log('copy',to);
 await cp(from,to,{recursive:true,errorOnExist:true,force:false});
}
await cp(resolve('src'),join(current,'src'),{recursive:true});
const changed=['package-integrity','managed-relocation','native-chat','native-mcp','launcher'];
for(const name of changed) {
 const {stdout}=await run('git',['show',`dda5221:src/${name}.mjs`],{maxBuffer:1024*1024});
 await writeFile(join(current,'src',name+'.mjs'),name==='package-integrity'?stdout.replace('const requirePackage = false;','const requirePackage = true;'):stdout);
}
await mkdir(join(install,'host'),{recursive:true});
await writeFile(join(install,'.assistant-install-root'),'TwinCatAssistant installation v1');
await writeFile(join(install,'state.json'),JSON.stringify({targetId:'tcxaeshell-x86-tc4024',current:'0.0.0-scope-synthetic'}));
await writeFile(join(install,'host/assistant-installation.json'),JSON.stringify({schemaVersion:1,targetId:'tcxaeshell-x86-tc4024',version:'0.0.0-scope-synthetic',applicationRoot:current}));
// The trace is outside the inventory. Both measurements use the same instrumentation.
const trace=join(base,'inventory.jsonl');
const manifestSource=await readFile(resolve('scripts/application-manifest.mjs'),'utf8');
const instrumented="import { appendFileSync } from 'node:fs';\n"+manifestSource.replace('export async function verifyManifest(root) {',`export async function verifyManifest(root) { appendFileSync(${JSON.stringify(trace)}, JSON.stringify({event:'start',root,at:Date.now()})+'\\n');`).replace("  if (JSON.stringify(manifest.files) !== JSON.stringify(actual)) throw new Error('Package inventory mismatch');", `  if (JSON.stringify(manifest.files) !== JSON.stringify(actual)) throw new Error('Package inventory mismatch'); appendFileSync(${JSON.stringify(trace)}, JSON.stringify({event:'complete',root,at:Date.now()})+'\\n');`);
await writeFile(join(current,'scripts/application-manifest.mjs'),instrumented);
await rm(join(current,'application-manifest.json'));
await createManifest(current,{version:'0.0.0-scope-synthetic',architecture:'x64'});
console.log('baseline manifested');
const {initializeState}=await import(pathToFileURL(join(old,'src/state.mjs')));
for(const name of ['before','after']) {
 const state=join(base,name+'-state');
 await initializeState(state,{knowledge:{executable:join(old,'runtime/mcp/knowledge/twincat-knowledge.exe')}});
}
await writeFile(join(install,'config.json'),JSON.stringify({StateRoot:join(base,'before-state'),DotNetRoot:String.raw`C:\Program Files\dotnet`}));
await writeFile(trace,'');
console.log('baseline native starting');
try {console.log((await run('C:/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe',['-NoProfile','-File',join(base,'native.ps1'),install,'before',join(base,'before.json')],{timeout:110000})).stdout);}catch(e){console.log('baseline result',e.stdout||e.message);}
await cp(trace,join(base,'before-inventory.jsonl')); await writeFile(trace,'');
for(const name of changed.concat('package-verification-scope')) {
 let source=await readFile(resolve('src',name+'.mjs'),'utf8');
 if(name==='package-integrity') source=source.replace('const requirePackage = false;','const requirePackage = true;');
 await writeFile(join(current,'src',name+'.mjs'),source);
}
await rm(join(current,'application-manifest.json'));
await createManifest(current,{version:'0.0.0-scope-synthetic',architecture:'x64'});
await writeFile(join(install,'config.json'),JSON.stringify({StateRoot:join(base,'after-state'),DotNetRoot:String.raw`C:\Program Files\dotnet`}));
console.log('scoped native starting');
try {console.log((await run('C:/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe',['-NoProfile','-File',join(base,'native.ps1'),install,'after',join(base,'after.json')],{timeout:110000})).stdout);}catch(e){console.log('after result',e.stdout||e.message);}
await cp(trace,join(base,'after-inventory.jsonl'));
