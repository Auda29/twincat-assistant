import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cp, lstat, mkdir, mkdtemp, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { createManifest, verifyManifest } from './application-manifest.mjs';
import { probeServer } from '../src/probe.mjs';
import { createOpenCodeConfig } from '../src/config.mjs';
import { patchKnowledgeValidation } from './knowledge-validation-overlay.mjs';
import { Client } from '@modelcontextprotocol/client';
import { StdioClientTransport } from '@modelcontextprotocol/client/stdio';

const root = fileURLToPath(new URL('../', import.meta.url));
let inputRoot = join(root, 'artifacts/application-inputs');
async function engineeringLock() {
  const lock=JSON.parse(await readFile(join(root,'upstream/engineering-lock.json'),'utf8'));
  if(lock.schemaVersion!==1 || !/^[a-f0-9]{40}$/.test(lock.revision ?? '') || lock.moduleVersion!=='1.0.0.0') throw new Error('Invalid reviewed Engineering lock');
  return lock;
}
const pyHash = '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3';
const sha = b => createHash('sha256').update(b).digest('hex');
const exists = p => lstat(p).then(() => true, e => { if (e.code === 'ENOENT') return false; throw e; });
const run = (exe, args, cwd, env, capture = false) => execFileSync(exe, args, { cwd, env, encoding: 'utf8', windowsHide: true, stdio: capture ? ['ignore','pipe','pipe'] : 'inherit', timeout: 600000 });
async function file(src, dst) { const s = await lstat(src); if (!s.isFile() || s.isSymbolicLink()) throw new Error('Required regular file: ' + src); await mkdir(dirname(dst), {recursive:true}); await cp(src,dst,{force:false,errorOnExist:true}); }
async function tree(src, dst, { workspaceLinks = false } = {}) {
  const s=await lstat(src); if(!s.isDirectory() || s.isSymbolicLink()) throw new Error('Redirected input directory: '+src);
  await mkdir(dst,{recursive:true});
  for(const name of await readdir(src)) {
    if(name === '.bin') continue;
    const a=join(src,name), b=join(dst,name), entry=await lstat(a);
    if(entry.isSymbolicLink()) { if(workspaceLinks && ['twincat-mcp','twincat-mcp-core','pi-twincat-ads'].includes(name)) continue; throw new Error('Unexpected link: '+a); }
    if(entry.isDirectory()) await tree(a,b); else if(entry.isFile()) await file(a,b); else throw new Error('Unexpected input: '+a);
  }
}
async function publish(name, version, work) {
  await mkdir(inputRoot,{recursive:true}); const target=join(inputRoot,name);
  if(await exists(target)) throw new Error('Output already exists: '+target);
  const scratch=await mkdtemp(join(inputRoot,'.build-'+name+'-'));
  try { const payload=join(scratch,'payload'); await mkdir(payload); await work(payload,scratch); await createManifest(payload,{version,architecture:process.arch}); await verifyManifest(payload); await rename(payload,target); console.log('Built '+target); }
  finally { await rm(scratch,{recursive:true,force:true,maxRetries:12,retryDelay:250}); }
}
function isolated(scratch) {
  const e=Object.fromEntries(['SystemRoot','WINDIR','PATH','Path','COMSPEC','PATHEXT','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramData','OS'].filter(k=>process.env[k]!==undefined).map(k=>[k,process.env[k]]));
  return {...e, HOME:scratch,USERPROFILE:scratch,APPDATA:scratch,LOCALAPPDATA:scratch,TEMP:scratch,TMP:scratch,npm_config_cache:join(scratch,'npm-cache'),npm_config_userconfig:join(scratch,'npmrc'),UV_CACHE_DIR:join(scratch,'uv-cache'),UV_NO_CONFIG:'1',GIT_CONFIG_NOSYSTEM:'1',GIT_CONFIG_GLOBAL:'NUL',GIT_TERMINAL_PROMPT:'0'};
}
async function engineering(checkout) {
  if(!checkout || !isAbsolute(checkout)) throw new Error('Engineering checkout must be absolute');
  const lock=await engineeringLock(), engineeringRevision=lock.revision;
  const revision=run('git',['rev-parse','HEAD'],checkout,isolated(root),true).trim();
  if(revision!==engineeringRevision) throw new Error('Engineering checkout revision mismatch');
  await publish('engineering',engineeringRevision,async(payload,scratch)=>{
    const env=isolated(scratch), source=join(scratch,'source');
    run('git',['clone','--no-hardlinks','--no-checkout',checkout,source],scratch,env);
    run('git',['-C',source,'checkout','--detach',engineeringRevision],scratch,env);
    // Use the pinned SDK and installed net472 targeting pack. Build only the
    // provider from the clean clone; no vendor or SDK assemblies are copied.
    const localDotnet=join(root,'local-data/dotnet8/dotnet.exe');
    const dotnet=process.env.DOTNET_ROOT ? join(process.env.DOTNET_ROOT,'dotnet.exe') : (await exists(localDotnet) ? localDotnet : 'dotnet');
    const dotnetEnv={...env, ...(isAbsolute(dotnet)?{DOTNET_ROOT:dirname(dotnet)}:{}), DOTNET_CLI_HOME:scratch,
      DOTNET_CLI_TELEMETRY_OPTOUT:'1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE:'1', DOTNET_GENERATE_ASPNET_CERTIFICATE:'false'};
    if(run(dotnet,['--version'],source,dotnetEnv,true).trim()!=='8.0.425') throw new Error('Engineering provider requires .NET SDK 8.0.425');
    run(dotnet,['build','native/Engineering.Host/TwinCatMcp.Engineering.Host.csproj','--configuration','Release','--nologo','-p:RestoreIgnoreFailedSources=true'],source,dotnetEnv);
    const dll=join(source,'native/Engineering.Host/bin/Release/net472/TwinCatMcp.Engineering.Host.dll');
    const moduleIdentity=run('powershell.exe',['-NoProfile','-NonInteractive','-Command',
      '$a=[Reflection.AssemblyName]::GetAssemblyName($env:TC_ENGINEERING_MODULE); $a.Name + "|" + $a.Version.ToString()'],source,{...env,TC_ENGINEERING_MODULE:dll},true).trim();
    if(moduleIdentity!=='TwinCatMcp.Engineering.Host|'+lock.moduleVersion) throw new Error('Engineering provider module identity mismatch');
    await file(dll,join(payload,'native/host/TwinCatMcp.Engineering.Host.dll'));
    await writeFile(join(payload,'native/host/receipt.json'),JSON.stringify({schemaVersion:1,revision:engineeringRevision,moduleVersion:lock.moduleVersion,sha256:sha(await readFile(dll))},null,2)+'\n');
    const npm=join(dirname(process.execPath),'node_modules/npm/bin/npm-cli.js');
    run(process.execPath,[npm,'ci','--no-audit','--no-fund'],source,env);
    run(process.execPath,[npm,'run','build'],source,env);
    // Resolve the two required workspaces in a fresh locked install. Pruning
    // the whole monorepo also retains the unrelated Pi workspace's dependencies.
    const production=join(scratch,'production'); await mkdir(production);
    for(const name of ['package.json','package-lock.json']) await file(join(source,name),join(production,name));
    for(const entry of await readdir(join(source,'packages'),{withFileTypes:true})) {
      if(entry.isDirectory() && await exists(join(source,'packages',entry.name,'package.json')))
        await file(join(source,'packages',entry.name,'package.json'),join(production,'packages',entry.name,'package.json'));
    }
    run(process.execPath,[npm,'ci','--omit=dev','--workspace=twincat-mcp','--workspace=twincat-mcp-core','--include-workspace-root=false','--no-audit','--no-fund'],production,env);
    await file(join(source,'LICENSE'),join(payload,'LICENSE'));
    await file(join(source,'package.json'),join(payload,'package.json'));
    await tree(join(production,'node_modules'),join(payload,'node_modules'),{workspaceLinks:true});
    for(const name of ['core','mcp']) {
      await file(join(source,'packages',name,'package.json'),join(payload,'packages',name,'package.json'));
      await tree(join(source,'packages',name,'dist'),join(payload,'packages',name,'dist'));
    }
    const core=join(payload,'node_modules/twincat-mcp-core');
    await file(join(source,'packages/core/package.json'),join(core,'package.json'));
    await tree(join(source,'packages/core/dist'),join(core,'dist'));
    await writeFile(join(payload,'build-provenance.json'),JSON.stringify({schemaVersion:1,revision:engineeringRevision,node:process.versions.node,packageLockSha256:sha(await readFile(join(source,'package-lock.json')))},null,2)+'\n');
    const moved=join(scratch,'relocated'); await rename(payload,moved);
    const project=join(scratch,'Synthetic.plcproj'), pou=join(scratch,'FB_Station.TcPOU'), config=join(scratch,'engineering.json');
    await writeFile(project,'<Project><ItemGroup><Compile Include="FB_Station.TcPOU" /></ItemGroup></Project>');
    await writeFile(pou,'<TcPlcObject><POU Name="FB_Station" Id="synthetic"><Declaration><![CDATA[FUNCTION_BLOCK FB_Station\nVAR_INPUT\n Timeout : TIME := T#5s;\nEND_VAR]]></Declaration><Implementation><ST><![CDATA[// synthetic\n]]></ST></Implementation></POU></TcPlcObject>');
    await writeFile(config,JSON.stringify({targetAmsNetId:'127.0.0.1.1.1',readOnly:true,writeAllowlist:[],engineering:{enabled:true,backend:'configuredProjectFiles',projectFiles:[{path:project,type:'plc'}]}}));
    const settings={engineering:{entrypoint:join(moved,'packages/mcp/dist/index.js'),config}};
    const result=await probeServer('engineering',createOpenCodeConfig(settings).mcp.servers.engineering,{name:'plc_read_pou',arguments:{pou:'FB_Station'}},{env,cwd:scratch,timeout:30000});
    if(!JSON.stringify(result.result).includes('Timeout')) throw new Error('Relocated Engineering fixture failed');
    await rename(moved,payload);
  });
}
async function download(url, target, expected) {
  const response=await fetch(url,{signal:AbortSignal.timeout(120000)}); if(!response.ok) throw new Error('Download failed: '+url);
  const bytes=Buffer.from(await response.arrayBuffer()); if(expected && sha(bytes)!==expected) throw new Error('Download checksum mismatch: '+url);
  await writeFile(target,bytes); return bytes;
}
async function probeKnowledge(server, env, cwd) {
  const transport = new StdioClientTransport({ command:server.command[0], args:server.command.slice(1), env, cwd, stderr:'pipe' });
  let stderr=''; transport.stderr?.on('data', chunk => { stderr=(stderr+chunk).slice(-2000); });
  const client = new Client({name:'synthetic-input-probe',version:'1.0.0'},{capabilities:{},versionNegotiation:{mode:{pin:'2026-07-28'}}});
  try {
    await client.connect(transport,{timeout:30000});
    const catalog=await client.listTools({}, {timeout:30000});
    if(!catalog.tools.some(tool=>tool.name==='tc_get_corpus_status')) throw new Error('Knowledge catalog missing status');
    const result=await client.callTool({name:'tc_get_corpus_status',arguments:{}},{timeout:30000});
    if(client.getNegotiatedProtocolVersion()!=='2026-07-28' || result.isError || result.structuredContent?.status!=='not_available') throw new Error('Knowledge corpus status failed');
  } catch(error) { throw new Error('Relocated Knowledge MCP probe failed: '+error.message+' '+stderr); }
  finally { await client.close().catch(()=>{}); await transport.close().catch(()=>{}); }
}
async function knowledge(artifacts, license, licenseSha, probeOnly=false) {
  if(!isAbsolute(artifacts)) throw new Error('Knowledge release artifact directory must be absolute');
  const lock=JSON.parse(await readFile(join(root,'upstream/knowledge-lock.json'),'utf8'));
  for(const artifact of lock.artifacts) {
    const path=join(artifacts,artifact.name), s=await lstat(path);
    if(!s.isFile() || s.isSymbolicLink() || sha(await readFile(path))!==artifact.sha256) throw new Error('Knowledge release artifact checksum mismatch: '+artifact.name);
  }
  if(!probeOnly && (!license || !isAbsolute(license) || !/^[a-f0-9]{64}$/.test(licenseSha ?? ''))) throw new Error('Knowledge release 0.1.1 wheel declares no license; provide a reviewed license file and its SHA-256 explicitly');
  if(!probeOnly && sha(await readFile(license))!==licenseSha) throw new Error('Knowledge license checksum mismatch');
  if(process.arch!=='x64') throw new Error('Only the pinned x64 Python embedded archive is currently supported');
  const build = async(payload,scratch)=>{
    const env=isolated(scratch), archive=join(scratch,'python.zip');
    await download('https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip',archive,pyHash);
    const python=join(payload,'python'); await mkdir(python);
    run('tar.exe',['-xf',archive,'-C',python],scratch,env);
    await writeFile(join(python,'python312._pth'),'python312.zip\n.\n../site-packages\n../site-packages/win32\n../site-packages/win32/lib\n../site-packages/pythonwin\n../site-packages/pywin32_system32\n');
    const uv=run('where.exe',['uv.exe'],scratch,env,true).trim().split(/\r?\n/)[0];
    if(!uv) throw new Error('uv is required to install pinned wheels');
    const site=join(payload,'site-packages'); await mkdir(site);
    run(uv,['pip','install','--target',site,'--python',join(python,'python.exe'),'--require-hashes','--no-deps','--only-binary',':all:','-r',join(artifacts,lock.runtimeRequirements ?? 'runtime-requirements.txt')],scratch,env);
    const wheel=lock.artifacts.find(a=>a.name.endsWith('.whl'));
    run(uv,['pip','install','--target',site,'--python',join(python,'python.exe'),'--no-deps',join(artifacts,wheel.name)],scratch,env);
    await rm(join(site,`twincat_knowledge-${lock.version}.dist-info/direct_url.json`),{force:true});
    // Console-script wrappers produced by installers may contain absolute paths.
    await rm(join(site,'Scripts'),{recursive:true,force:true});
    const validationOverlay = await patchKnowledgeValidation(site, lock.version);
    await writeFile(join(payload,'assistant-validation-overlay.json'),JSON.stringify(validationOverlay,null,2)+'\n');
    if(!probeOnly) await file(license,join(payload,'LICENSE'));
    await writeFile(join(payload,'build-provenance.json'),JSON.stringify({schemaVersion:1,revision:lock.revision,version:lock.version,developmentOnly:lock.developmentOnly===true,python:lock.python,pythonEmbedSha256:pyHash,artifacts:lock.artifacts,licenseSha256:probeOnly?null:licenseSha},null,2)+'\n');
    const csc=join(process.env.SystemRoot,'Microsoft.NET/Framework/v4.0.30319/csc.exe');
    run(csc,['/nologo','/target:exe','/out:'+join(payload,'twincat-knowledge.exe'),join(root,'scripts/KnowledgeLauncher.cs')],scratch,env);
    const moved=join(scratch,'relocated'); await rename(payload,moved);
    await createManifest(moved,{version:lock.version,architecture:process.arch});
    const version=run(join(moved,'twincat-knowledge.exe'),['--version'],scratch,env,true).trim();
    if(version!=='twincat-knowledge '+lock.version) throw new Error('Relocated Knowledge version mismatch');
    run(join(moved,'python/python.exe'),['-I','-B','-c','from twincat_knowledge.mcp_api.topic_server import run_topic_stdio'],scratch,env);
    const settings={knowledge:{executable:join(moved,'twincat-knowledge.exe')}};
    await probeKnowledge(createOpenCodeConfig(settings).mcp.servers.knowledge,env,scratch);
    await verifyManifest(moved);
    await rm(join(moved,'application-manifest.json'));
    await rename(moved,payload);
  };
  if(probeOnly) {
    await mkdir(inputRoot,{recursive:true}); const scratch=await mkdtemp(join(inputRoot,'.probe-knowledge-'));
    try { const payload=join(scratch,'payload'); await mkdir(payload); await build(payload,scratch); console.log('Relocated Knowledge corpus-status probe passed; no application input published.'); }
    finally { await rm(scratch,{recursive:true,force:true,maxRetries:12,retryDelay:250}); }
  } else await publish('knowledge',lock.version,build);
}
async function licenses() {
  if(process.arch!=='x64') throw new Error('Node license extraction currently supports x64');
  await mkdir(inputRoot,{recursive:true});
  for(const name of ['node','webview2']) if(await exists(join(inputRoot,name))) throw new Error('Output already exists: '+join(inputRoot,name));
  const scratch=await mkdtemp(join(inputRoot,'.build-licenses-'));
  try {
    const env=isolated(scratch), version=process.versions.node, zipName=`node-v${version}-win-x64.zip`, base=`https://nodejs.org/dist/v${version}/`;
    const sums=(await download(base+'SHASUMS256.txt',join(scratch,'SHASUMS256.txt'))).toString('utf8');
    const lines=sums.split(/\r?\n/).filter(l=>l.endsWith('  '+zipName));
    if(lines.length!==1 || !/^[a-f0-9]{64}  /.test(lines[0])) throw new Error('Official Node checksum entry missing');
    const zip=join(scratch,zipName); await download(base+zipName,zip,lines[0].slice(0,64));
    const extracted=join(scratch,'node-extracted'); await mkdir(extracted); run('tar.exe',['-xf',zip,'-C',extracted],scratch,env);
    const nodeDist=join(extracted,`node-v${version}-win-x64`);
    if(sha(await readFile(join(nodeDist,'node.exe')))!==sha(await readFile(process.execPath))) throw new Error('Running Node differs from the official distribution');
    const nuget=process.env.NUGET_PACKAGES || join(process.env.USERPROFILE,'.nuget/packages');
    const ver='1.0.4258.31', packageRoot=join(nuget,'microsoft.web.webview2',ver);
    const nugetMetadata=JSON.parse(await readFile(join(packageRoot,'.nupkg.metadata'),'utf8'));
    const projectLock=JSON.parse(await readFile(join(root,'native/IdeHost/X86/packages.lock.json'),'utf8'));
    const expected=projectLock.dependencies['.NETFramework,Version=v4.7.2']['Microsoft.Web.WebView2'];
    if(expected.resolved!==ver || nugetMetadata.contentHash!==expected.contentHash) throw new Error('WebView2 NuGet restore does not match the host lock');
    const nupkg=join(packageRoot,`microsoft.web.webview2.${ver}.nupkg`), hash=join(packageRoot,`microsoft.web.webview2.${ver}.nupkg.sha512`);
    const actual=createHash('sha512').update(await readFile(nupkg)).digest('base64');
    if(actual!==(await readFile(hash,'utf8')).trim()) throw new Error('WebView2 archive checksum mismatch');
    const localDotnet=join(root,'local-data/dotnet8/dotnet.exe');
    const dotnet=process.env.DOTNET_ROOT ? join(process.env.DOTNET_ROOT,'dotnet.exe') : (await exists(localDotnet) ? localDotnet : 'dotnet');
    run(dotnet,['nuget','verify',nupkg],scratch,{...env, ...(isAbsolute(dotnet)?{DOTNET_ROOT:dirname(dotnet)}:{}), DOTNET_CLI_HOME:scratch, DOTNET_GENERATE_ASPNET_CERTIFICATE:'false', DOTNET_CLI_TELEMETRY_OPTOUT:'1'});
    const extractedNuget=join(scratch,'webview2-extracted'); await mkdir(extractedNuget); run('tar.exe',['-xf',nupkg,'-C',extractedNuget],scratch,env);
    const node=join(scratch,'node'), web=join(scratch,'webview2'); await mkdir(node); await mkdir(web);
    await file(join(nodeDist,'LICENSE'),join(node,'LICENSE'));
    for(const name of ['LICENSE.txt','NOTICE.txt']) await file(join(extractedNuget,name),join(web,name));
    await rename(node,join(inputRoot,'node')); await rename(web,join(inputRoot,'webview2'));
    console.log('Sourced exact Node and locked WebView2 licenses.');
  } finally { await rm(scratch,{recursive:true,force:true}); }
}
const {values}=parseArgs({options:{component:{type:'string'},engineering:{type:'string'},'knowledge-artifacts':{type:'string'},'knowledge-license':{type:'string'},'knowledge-license-sha256':{type:'string'},'output-root':{type:'string'}},strict:true});
try {
  if(process.platform!=='win32' || process.arch!=='x64' || Number(process.versions.node.split('.')[0])<22) throw new Error('Build inputs with Windows x64 Node 22+');
  if(values['output-root']) { if(!isAbsolute(values['output-root']) || resolve(values['output-root'])!==values['output-root']) throw new Error('OutputRoot must be absolute and normalized'); inputRoot=values['output-root']; }
  const component=(values.component??'All').toLowerCase();
  if(!['all','engineering','knowledge','knowledgeprobe','licenses'].includes(component)) throw new Error('Unknown component');
  if(component==='all' && (!values['knowledge-license'] || !isAbsolute(values['knowledge-license']) || !/^[a-f0-9]{64}$/.test(values['knowledge-license-sha256'] ?? ''))) throw new Error('Knowledge release 0.1.1 wheel declares no license; provide a reviewed license file and its SHA-256 explicitly');
  if(component==='all') {
    if(sha(await readFile(values['knowledge-license']))!==values['knowledge-license-sha256']) throw new Error('Knowledge license checksum mismatch');
    const lock=JSON.parse(await readFile(join(root,'upstream/knowledge-lock.json'),'utf8'));
    if(!isAbsolute(values['knowledge-artifacts'])) throw new Error('Knowledge release artifact directory must be absolute');
    for(const artifact of lock.artifacts) {
      const path=join(values['knowledge-artifacts'],artifact.name), stat=await lstat(path);
      if(!stat.isFile() || stat.isSymbolicLink() || sha(await readFile(path))!==artifact.sha256) throw new Error('Knowledge release artifact checksum mismatch: '+artifact.name);
    }
    for(const name of ['engineering','knowledge','node','webview2']) if(await exists(join(inputRoot,name))) throw new Error('Output already exists: '+join(inputRoot,name));
  }
  if(component==='all' || component==='engineering') await engineering(values.engineering);
  if(component==='all' || component==='knowledge') await knowledge(values['knowledge-artifacts'],values['knowledge-license'],values['knowledge-license-sha256']);
  if(component==='knowledgeprobe') await knowledge(values['knowledge-artifacts'],undefined,undefined,true);
  if(component==='all' || component==='licenses') await licenses();
} catch(error) { console.error('Application input build failed: '+error.message); process.exitCode=1; }
