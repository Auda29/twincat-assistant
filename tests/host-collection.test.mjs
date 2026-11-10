import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
test('installed Package Manager does not turn classic 4024 into a 4026 installation', {skip:process.platform!=='win32'},async()=>{
  const path=new URL('../scripts/Get-AssistantHostInventory.ps1',import.meta.url);
  const source=await readFile(path,'utf8');
  // Exercise only the collector function with registry/file/command providers
  // replaced in this child process. No real collection, registry write or IDE.
  const select=name=>source.match(new RegExp(`function ${name}[^]*?(?=\\nfunction |\\n# -)`))?.[0];
  const functions=[select('Get-TwinCatFamily'),select('Get-TwinCatFacts')];
  assert.ok(functions.every(Boolean));
  const code=`$ErrorActionPreference='Stop'
function Get-RegistryValue($h,$v,$p,$n){if($n -eq 'TcVersion'){return $script:fixtureVersion};return 'C:\\Fixture'}
function Get-FileFacts($p){return $null}
function Protect-Path($p){return $p}
function Get-Command{param($Name,$ErrorAction);return @{Source='C:\\Fixture\\tcpkg.exe'}}
${functions.join('\n')}
$setup=@([pscustomobject]@{displayName='Beckhoff TwinCAT 3.1 (Build 4024)';displayVersion='3.1.4024.78';installDate='20261110';publisher='Beckhoff'})
$fixtureVersion='3.1.4024.78';$classic=Get-TwinCatFacts $setup
$fixtureVersion='3.1.4026.17';$modern=Get-TwinCatFacts $setup
@{classic=$classic.installationMechanism;modern=$modern.installationMechanism;managerPresent=$classic.tcpkg.found}|ConvertTo-Json -Compress`;
  const run=spawnSync('powershell.exe',['-NoProfile','-EncodedCommand',Buffer.from(code,'utf16le').toString('base64')],{encoding:'utf8',windowsHide:true});
  assert.equal(run.status,0,run.stderr);
  assert.deepEqual(JSON.parse(run.stdout),{classic:'classic-setup',modern:'package-manager',managerPresent:true});
});
