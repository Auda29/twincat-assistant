import { cp, readFile, writeFile, mkdir, readdir, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { createManifest, verifyManifest } from './application-manifest.mjs';
// This is deliberately a one-candidate import, not automatic adoption of inputs.
const [input, output] = process.argv.slice(2).map(p => resolve(p));
const expected = '3c261754d608232077d087df9aca0fa909382789e1a0f8de7d69e96b3068a294';
const base = 'cebd35a46daa0cf457f443af7008a4a1f8142ced';
const sha = value => createHash('sha256').update(value).digest('hex');
if (sha(await readFile(join(input,'application-manifest.json'))) !== expected) throw new Error('Unreviewed staging input');
const manifest = await verifyManifest(input);
if (manifest.files.some(f => /(^|\/)tcatsysmanagerlib\.dll$|\.vsix$/i.test(f.path))) throw new Error('Vendor interop or archive refused');
execFileSync('git',['diff','--exit-code',base,'HEAD','--','native','upstream','package.json','package-lock.json'],{stdio:'inherit'});
await mkdir(output); // refuses existing output
await cp(input,output,{recursive:true});
for (const name of await readdir(join(output,'src'))) await rm(join(output,'src',name));
const sources=execFileSync('git',['ls-files','--','src'],{encoding:'utf8'}).trim().split(/\r?\n/);
for(const path of sources) await cp(path,join(output,path));
const integrity=join(output,'src/package-integrity.mjs');
await writeFile(integrity,(await readFile(integrity,'utf8')).replace('const requirePackage = false;','const requirePackage = true;'));
await writeFile(join(output,'src/package-runtime.json'),'{"version":1}\n');
await rm(join(output,'application-manifest.json'));
await writeFile(join(output,'ci-input-provenance.json'),JSON.stringify({schemaVersion:1,sourceCommit:process.env.GITHUB_SHA ?? execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),baseCommit:base,baseManifestSha256:expected,dependency:'Native x86 DLL built on the verified TwinCAT 4024.78 workstation; vendor interop excluded. Runtime and MCP provenance retained in package.'},null,2)+'\n');
await createManifest(output,{version:manifest.version,architecture:manifest.architecture});
await verifyManifest(output);
console.log('Reviewed application input staged and manifest verified');
