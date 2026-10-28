import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, mkdir, writeFile, readFile, realpath, rm } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';

const capture=promisify(execFile);
test('native MCP setup preserves host paths and refuses to replace its own host configuration', {skip:process.platform!=='win32'}, async()=>{
  const root=await mkdtemp(join(tmpdir(),'tc-native-mcp-setup-'));
  try {
    const directory=join(root,'TwinCatAssistant'); await mkdir(directory);
    const host=join(directory,'native-chat.json');
    const original={NodePath:process.execPath,RepositoryRoot:resolve('.'),StateRoot:join(root,'state'),DotNetRoot:join(root,'sdk')};
    await writeFile(host,JSON.stringify(original));
    const executable=join(root,'knowledge.exe'), entrypoint=join(root,'engineering.mjs');
    await writeFile(executable,'synthetic'); await writeFile(entrypoint,'synthetic');
    const args=['-NoProfile','-ExecutionPolicy','Bypass','-File',resolve('scripts/Configure-NativeMcp.ps1'),
      '-KnowledgeExecutable',executable,'-EngineeringEntrypoint',entrypoint];
    // Windows PowerShell's first cmdlet load can exceed 15 s on a busy hosted
    // runner. This bounds the harness, not MCP startup or search execution.
    const options={env:{...process.env,LOCALAPPDATA:root},windowsHide:true,timeout:30000};
    const scriptRefused=error=>error.killed!==true&&Number.isInteger(error.code)&&error.code!==0;
    await capture('powershell.exe',args,options);
    const configured=JSON.parse(await readFile(host,'utf8'));
    const {McpSettingsPath,...unchanged}=configured;
    assert.deepEqual(unchanged,original);
    assert.deepEqual(JSON.parse(await readFile(McpSettingsPath,'utf8')),
      {knowledge:{executable:await realpath(executable)},engineering:{entrypoint:await realpath(entrypoint)}});
    await assert.rejects(capture('powershell.exe',[...args,'-McpSettingsPath',host],options),scriptRefused);
    assert.deepEqual(JSON.parse(await readFile(host,'utf8')),configured);
    const unrelated=join(directory,'unrelated.json'); await writeFile(unrelated,'{"other":"retain"}');
    await assert.rejects(capture('powershell.exe',[...args,'-McpSettingsPath',unrelated],options),scriptRefused);
    assert.equal(await readFile(unrelated,'utf8'),'{"other":"retain"}');
  } finally { await rm(root,{recursive:true,force:true}); }
});
