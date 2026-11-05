import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';
import { createManifest } from '../scripts/application-manifest.mjs';
const run = promisify(execFile);

test('relocation accepts only inventoried sibling versions and exact packaged entry paths', {skip:process.platform !== 'win32',timeout:120000}, async () => {
  const temp = await mkdtemp(join(tmpdir(),'tc-relocation-contract-'));
  try {
    const install = join(temp,'install'), versions = join(install,'versions');
    const old = join(versions,'1.0.0','application'), current = join(versions,'2.0.0','application');
    for (const [root,version] of [[old,'1.0.0'],[current,'2.0.0']]) {
      for (const dir of ['src','scripts','runtime/node','runtime/mcp/knowledge','runtime/mcp/engineering/packages/mcp/dist']) await mkdir(join(root,dir),{recursive:true});
      for (const file of ['managed-relocation.mjs','package-integrity.mjs']) await cp(resolve('src',file),join(root,'src',file));
      const integrity = join(root,'src/package-integrity.mjs');
      await writeFile(integrity,(await readFile(integrity,'utf8')).replace('const requirePackage = false;', 'const requirePackage = true;'));
      await cp(resolve('scripts/application-manifest.mjs'),join(root,'scripts/application-manifest.mjs'));
      await cp(process.execPath,join(root,'runtime/node/node.exe'));
      await writeFile(join(root,'src/package-runtime.json'),' {"version":1} ');
      await writeFile(join(root,'src/knowledge-client.mjs'),'// fixture');
      await writeFile(join(root,'runtime/mcp/knowledge/twincat-knowledge.exe'),'fixture');
      await writeFile(join(root,'runtime/mcp/engineering/packages/mcp/dist/index.js'),'fixture');
      await createManifest(root,{version,architecture:process.arch});
    }
    await writeFile(join(install,'.assistant-install-root'),'TwinCatAssistant installation v1');
    await writeFile(join(install,'state.json'),JSON.stringify({targetId:'tcxaeshell-x86-tc4024',current:'2.0.0'}));
    const config = {mcp:{servers:{knowledge:{command:[join(old,'runtime/node/node.exe'),join(old,'src/knowledge-client.mjs'),'broker']}}}};
    const settings = {knowledge:{executable:join(old,'runtime/mcp/knowledge/twincat-knowledge.exe')}};
    const code = `const {previousPackageRuntime}=await import(${JSON.stringify('file:///')}+process.argv[1].replaceAll('\\\\','/')); console.log(JSON.stringify(await previousPackageRuntime(JSON.parse(process.argv[2]),JSON.parse(process.argv[3]))));`;
    const check = async (c=config,s=settings) => JSON.parse((await run(join(current,'runtime/node/node.exe'),['--input-type=module','-e',code,join(current,'src/managed-relocation.mjs'),JSON.stringify(c),JSON.stringify(s)],{windowsHide:true})).stdout);
    assert.equal((await check()).node,join(old,'runtime/node/node.exe'));
    const currentOnly = `const x=await import(${JSON.stringify('file:///')}+process.argv[1].replaceAll('\\\\','/')); console.log(x.currentPackageSettings(JSON.parse(process.argv[2])));`;
    const currentResult = await run(join(current,'runtime/node/node.exe'),['--input-type=module','-e',currentOnly,join(current,'src/managed-relocation.mjs'),JSON.stringify(settings)],{windowsHide:true});
    assert.equal(currentResult.stdout.trim(),'false', 'current Node cannot make an old or foreign MCP path current');
    const foreign = structuredClone(config); foreign.mcp.servers.knowledge.command[0] = join(temp,'foreign','runtime/node/node.exe');
    assert.equal(await check(foreign),null);
    const wrong = structuredClone(settings); wrong.knowledge.executable = join(temp,'foreign.exe');
    assert.equal(await check(config,wrong),null);
    const source=join(old,'src/knowledge-client.mjs'); await writeFile(source,'changed');
    await assert.rejects(check()); await writeFile(source,'// fixture');
    await writeFile(join(install,'state.json'),JSON.stringify({targetId:'tcxaeshell-x86-tc4024',current:'1.0.0'}));
    await assert.rejects(check());
  } finally { await rm(temp,{recursive:true,force:true,maxRetries:15,retryDelay:200}); }
});
