import { readdir, lstat, open, unlink, readFile, rm, realpath } from 'node:fs/promises';
import { join, resolve, dirname, isAbsolute } from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import { inspectState, readJsonFile, runtimeEnvironment } from './state.mjs';
import { atomicJson } from './profile-update.mjs';
import { readNativeMcpSettings } from './native-mcp.mjs';
import { knowledgePaths, knowledgeDirectory } from './knowledge-paths.mjs';
import { superviseWindows, acquireKnowledgeLease } from './windows-native.mjs';
import { probeServer } from './probe.mjs';
import { createOpenCodeConfig } from './config.mjs';

const capture = promisify(execFile);
const MAX_OUTPUT = 131072;
function absolute(value) {
  if (typeof value !== 'string' || !isAbsolute(value) || value.length > 2048 || value.includes('\0')) throw new Error('Invalid knowledge path');
  return resolve(value);
}
async function regular(path, directory = false) {
  const info = await lstat(path);
  if (info.isSymbolicLink() || (directory ? !info.isDirectory() : !info.isFile())) throw new Error('Redirected knowledge path');
  return info;
}
export async function discoverHelp(root = process.env.ProgramData && join(process.env.ProgramData, 'Microsoft/HelpLibrary2/Catalogs')) {
  if (!root) return [];
  try { await regular(root, true); } catch (error) { if (error.code === 'ENOENT') return []; throw error; }
  const catalogs = await readdir(root, { withFileTypes: true });
  if (catalogs.length > 256) throw new Error('Help discovery limit');
  const found = [];
  for (const catalog of catalogs.sort((a,b) => a.name.localeCompare(b.name))) {
    if (!catalog.isDirectory() || catalog.isSymbolicLink()) continue;
    const store = join(root, catalog.name, 'ContentStore');
    try { await regular(store, true); } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
    for (const [locale, language] of [['DE-DE','de'], ['EN-US','en']]) {
      const folder = join(store, locale);
      let files;
      try { await regular(folder, true); files = await readdir(folder, { withFileTypes:true }); }
      catch (error) { if (error.code === 'ENOENT') continue; throw error; }
      if (files.length > 4096) throw new Error('Help discovery limit');
      for (const file of files) {
        if (!file.isFile() || file.isSymbolicLink() || !new RegExp('^BKINFOSYS3.*_'+locale+'.*\\.mshc$', 'i').test(file.name)) continue;
        const path = await realpath(join(folder, file.name)), stat = await regular(path);
        found.push({ path, language, catalog:catalog.name, name:file.name, size:stat.size, modified:stat.mtimeMs });
        if (found.length > 32) throw new Error('Help discovery limit');
      }
    }
  }
  return found;
}
async function optionalJson(file) {
  try { return await readJsonFile(file); } catch (error) { if (!(await lstat(file).catch(e => { if (e.code==='ENOENT') return null; throw e; }))) return null; throw error; }
}
export async function knowledgeStatus(request, { catalogRoot } = {}) {
  await inspectState(absolute(request.state));
  const settings = await readNativeMcpSettings(absolute(request.mcpConfig));
  const paths = await knowledgePaths(request.state);
  if (settings.knowledge.config && resolve(settings.knowledge.config) !== resolve(paths.config))
    return { phase:'external', ready:false, candidates:[], language:null, topics:0 };
  const candidates = await discoverHelp(catalogRoot);
  const receipt = await optionalJson(paths.receipt);
  const active = await optionalJson(join(paths.state,'active.json'));
  let ready = false;
  if (receipt && active?.active?.manifest_sha256 === receipt.manifest && active.active.snapshot_id === receipt.snapshot) {
    const manifestFile = join(active.active.package_path, 'manifest.json');
    const info = await regular(manifestFile);
    ready = info.size < 65536 && createHash('sha256').update(await readFile(manifestFile)).digest('hex') === receipt.manifest;
  }
  const source = ready && candidates.find(candidate => candidate.path === receipt.source.path);
  const revision = name => Number(name?.match(/\.(\d+)\.mshc$/i)?.[1] ?? -1);
  const newer = ready && candidates.some(candidate => candidate.language===receipt.language
    && revision(candidate.name)>revision(receipt.source.name));
  const replaced = ready && !source && candidates.some(candidate => candidate.language===receipt.language);
  const update = Boolean(ready && (newer || replaced || (source && (source.size !== receipt.source.size || source.modified !== receipt.source.modified))));
  return { phase:ready ? (update ? 'update' : 'ready') : candidates.length ? 'available' : 'missing', ready,
    candidates, language:ready ? receipt.language : null, topics:ready ? receipt.topics : 0,
    revision:ready ? receipt.manifest : null, updateAvailable:update };
}
export async function runKnowledge(command, args, cwd, env, signal) {
  if (signal?.aborted) throw new Error('Cancelled');
  let output;
  if (process.platform === 'win32') {
    const overflow = new AbortController(); let size=0; const chunks=[];
    const result = await superviseWindows({ command, args, cwd, env }, AbortSignal.any([signal,overflow.signal]), () => {}, bytes => {
      size += bytes.length; if (size > MAX_OUTPUT) overflow.abort(); else chunks.push(bytes);
    });
    if (result.code !== 0 || result.cancelled || overflow.signal.aborted) throw new Error('Knowledge command failed');
    output = Buffer.concat(chunks);
  } else {
    output = (await capture(command,args,{cwd,env,signal,encoding:'buffer',maxBuffer:MAX_OUTPUT,timeout:1800000})).stdout;
  }
  return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(output));
}
export async function setupKnowledge(request, { signal = new AbortController().signal, emit = () => {}, catalogRoot, run = runKnowledge } = {}) {
  const status = await knowledgeStatus(request,{catalogRoot});
  if (status.phase === 'external') throw new Error('Existing external corpus configuration is preserved');
  let selectedPath;
  try { selectedPath=await realpath(absolute(request.source)); }
  catch { throw new Error('Select a discovered source and authorize local indexing'); }
  const source = status.candidates.find(candidate => candidate.path === selectedPath && candidate.language === request.language);
  if (!source || request.acceptLocalIndexing !== true) throw new Error('Select a discovered source and authorize local indexing');
  const paths = await knowledgePaths(request.state);
  const lockPath = join(paths.base,'setup.lock');
  const lease=process.platform==='win32'?await acquireKnowledgeLease(await realpath(request.state)):null;
  let lock;
  try {
    if(lease) {
      const previous=await optionalJson(lockPath);
      if(previous) {
        if(previous.version!==1||previous.kind!=='native-knowledge-setup')throw new Error('Unknown setup lock');
        await unlink(lockPath);
      }
      signal=AbortSignal.any([signal,lease.signal]);
    }
    lock = await open(lockPath,'wx',0o600);
  } catch(error){await lease?.release();throw error;}
  let job=null, keep=false;
  try {
  await lock.writeFile(JSON.stringify({version:1,kind:'native-knowledge-setup',pid:process.pid}));
  const activeBefore = await optionalJson(join(paths.state,'active.json'));
  let pending=null;
  if (activeBefore?.active?.package_path && dirname(dirname(resolve(activeBefore.active.package_path)))===resolve(paths.runs)) {
    pending=await optionalJson(join(dirname(activeBefore.active.package_path),'import.json'));
    if(pending && (pending.source.path!==source.path || pending.source.size!==source.size || pending.source.modified!==source.modified
      || pending.imported.manifest_sha256!==activeBefore.active.manifest_sha256))pending=null;
  }
  job = pending ? dirname(activeBefore.active.package_path) : join(paths.runs,randomUUID());
  keep = Boolean(pending);
    await knowledgeDirectory(job);
    const settings = await readNativeMcpSettings(request.mcpConfig);
    const env = runtimeEnvironment(request.state,{});
    const phase = name => { emit({ phase:name, ready:status.ready, language:source.language, topics:status.topics }); if (signal.aborted) throw new Error('Cancelled'); };
    const packagePath = join(job,'package');
    let imported=pending?.imported;
    if(!imported) {
    phase('indexing');
    imported = await run(settings.knowledge.executable, ['ingest','--format','infosys-mshc','--source',source.path,
      '--language',source.language,'--output',packagePath,'--rights-reference','local-assistant-user-authorized-indexing:'+new Date().toISOString(),'--json'], job,env,signal);
    if (imported.status !== 'ok' || !/^[a-f0-9]{64}$/.test(imported.manifest_sha256) || !Number.isSafeInteger(imported.topic_count) || imported.topic_count < 1)
      throw new Error('Import report invalid');
    const current = await regular(source.path);
    if (current.size !== source.size || current.mtimeMs !== source.modified) throw new Error('Help changed during indexing');
    await atomicJson(join(job,'import.json'),{source,imported});
    phase('validating');
    await run(settings.knowledge.executable,['corpus','validate',packagePath,'--expected-manifest-sha256',imported.manifest_sha256,
      '--cache-dir',paths.cache,'--json'],job,env,signal);
    phase('activating');
    // The UI disables ordinary Cancel during finalization. Losing the host still
    // aborts the owned process tree; a possibly published package is retained.
    const finish = AbortSignal.any([signal,AbortSignal.timeout(1800000)]);
    keep = true; // Activation may publish before a readback error; never delete a possibly selected package.
    await run(settings.knowledge.executable,['corpus','activate',packagePath,'--expected-manifest-sha256',imported.manifest_sha256,
      '--config',paths.config,'--json'],job,env,finish);
    }
    emit({phase:'verifying',ready:status.ready,language:source.language,topics:imported.topic_count});
    const server = createOpenCodeConfig({knowledge:{...settings.knowledge,config:paths.config}}).mcp.servers.knowledge;
    const probe = await probeServer('knowledge',server,{name:'tc_get_corpus_status',arguments:{}},{env,cwd:job,timeout:600000,signal});
    if (probe.result.isError || probe.result.structuredContent?.status !== 'ok') throw new Error('Corpus not available');
    const active = await readJsonFile(join(paths.state,'active.json'));
    if (active.active.manifest_sha256 !== imported.manifest_sha256) throw new Error('Activation readback failed');
    await atomicJson(paths.receipt,{version:1,source,language:source.language,topics:imported.topic_count,
      manifest:imported.manifest_sha256,snapshot:active.active.snapshot_id});
    return await knowledgeStatus(request,{catalogRoot});
  } finally {
    try {await lock.close(); await unlink(lockPath);} finally {await lease?.release();}
    if (job && !keep) {
      if (dirname(resolve(job)) !== resolve(paths.runs)) throw new Error('Unsafe cleanup path');
      const exists=await lstat(job).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
      if(exists){await regular(job,true); await rm(job,{recursive:true,force:true});}
    }
  }
}
async function main() {
  const input = createInterface({input:process.stdin,crlfDelay:Infinity});
  const abort = new AbortController(); let work, received=false, finished=false, size=0;
  process.stdin.on('data',chunk => { size+=chunk.length; if(size>16384){abort.abort();input.close();} });
  input.on('close',()=>{if(!finished) abort.abort();});
  input.on('line',line=>{
    if(received){abort.abort();return;} received=true;
    work=(async()=>{
      try {
        const request=JSON.parse(line.replace(/^\uFEFF/,''));
        if (!request || Object.keys(request).some(key=>!['mode','state','mcpConfig','source','language','acceptLocalIndexing'].includes(key))) throw new Error();
        absolute(request.state); absolute(request.mcpConfig);
        const emit=value=>process.stdout.write(JSON.stringify(value)+'\n');
        const value=request.mode==='status' ? await knowledgeStatus(request) : request.mode==='setup' ? await setupKnowledge(request,{signal:abort.signal,emit}) : null;
        if(!value)throw new Error(); emit({...value,complete:true});
      } catch { process.stdout.write(JSON.stringify({phase:abort.signal.aborted?'cancelled':'error',complete:true})+'\n'); }
      finally { finished=true; input.close();process.stdin.pause(); }
    })();
  });
  setTimeout(()=>{if(!received){abort.abort();input.close();process.stdin.destroy();}},10000).unref();
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
