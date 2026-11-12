import test from 'node:test';
import assert from 'node:assert/strict';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, mkdtemp, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { once } from 'node:events';
const run=promisify(execFile);
const root=resolve('.');
const csc=join(process.env.SystemRoot ?? 'C:/Windows','Microsoft.NET/Framework/v4.0.30319/csc.exe');

test('input builder refuses missing release license before creating outputs', {skip:process.platform!=='win32'}, async()=>{
  const parent=await mkdtemp(join(tmpdir(),'tc-input-refuse-'));
  try {
    const output=join(parent,'inputs');
    await assert.rejects(run('powershell.exe',['-NoProfile','-File',join(root,'scripts/Build-ApplicationInputs.ps1'),'-Component','All','-OutputRoot',output],{cwd:parent}),/reviewed redistribution permission file/);
    await assert.rejects(readFile(join(output,'knowledge','application-manifest.json')),{code:'ENOENT'});
    await assert.rejects(readFile(join(output,'engineering','application-manifest.json')),{code:'ENOENT'});
  } finally { await rm(parent,{recursive:true,force:true}); }
});

test('relative Knowledge launcher forwards arguments and streams after relocation', {skip:process.platform!=='win32',timeout:30000}, async()=>{
  const parent=await mkdtemp(join(tmpdir(),'tc-python-launch-'));
  let child;
  try {
    const before=join(parent,'before'), python=join(before,'python'); await mkdir(python,{recursive:true});
    const source=join(parent,'fixture.cs');
    await writeFile(source,'using System; class Fixture { static int Main(string[] a) { Console.WriteLine(string.Join("|",a)); Console.WriteLine(Console.ReadLine()); Console.ReadLine(); return 0; } }');
    await run(csc,['/nologo','/target:exe','/out:'+join(python,'python.exe'),source],{cwd:parent});
    await run(csc,['/nologo','/target:exe','/out:'+join(before,'twincat-knowledge.exe'),join(root,'scripts/KnowledgeLauncher.cs')],{cwd:parent});
    const after=join(parent,'moved'); await rename(before,after);
    child=spawn(join(after,'twincat-knowledge.exe'),['serve','a path','quote"arg'],{cwd:parent,stdio:['pipe','pipe','pipe'],windowsHide:true});
    let output=''; child.stdout.on('data',chunk=>{output+=chunk;});
    child.stdin.write('synthetic line\n');
    const deadline=Date.now()+5000;
    while(!output.includes('synthetic line') && Date.now()<deadline) await new Promise(r=>setTimeout(r,25));
    assert.match(output,/synthetic line/, 'MCP output must stream before stdin closes');
    child.stdin.end(); const [code]=await once(child,'exit');
    assert.equal(code,0); assert.equal(output.replaceAll('\r\n','\n'),'-I|-B|-m|twincat_knowledge|serve|a path|quote"arg\nsynthetic line\n');
  } finally { if(child && child.exitCode===null) { child.stdin.end(); await Promise.race([once(child,'exit'),new Promise(r=>setTimeout(r,3000))]); if(child.exitCode===null) child.kill(); } await rm(parent,{recursive:true,force:true,maxRetries:10,retryDelay:100}); }
});
