# Synthetic files only. No Get-MmLiveFacts, native load, backend, IDE or installer.
[CmdletBinding()]
param([string]$EvidencePath,[switch]$ProfileOpaqueOnly)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Maintenance.Core.ps1"
$script:checks=New-Object Collections.Generic.List[object]
$script:fixtures=New-Object Collections.Generic.List[string]
$sandbox=Join-Path ([IO.Path]::GetTempPath().TrimEnd('\')) ('assistant-maintenance-synthetic-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($sandbox)
function Assert-Test($condition,[string]$message){if(-not $condition){throw $message}}
function Test-Refusal([scriptblock]$Work,[string]$Expected) {
 $failed=$false
 try { & $Work } catch { $failed=$true; if($Expected -and $_.Exception.Message -notlike ('*'+$Expected+'*')){throw ('Unexpected refusal: '+$_.Exception.Message)} }
 Assert-Test $failed 'Expected refusal did not occur.'
}
function Add-Check([string]$Name){$script:checks.Add([ordered]@{name=$Name;status='passed'});Write-Output ('PASS '+$Name)}
function New-Fixture {
 $root=Join-Path $sandbox ([guid]::NewGuid().ToString('N')); [void][IO.Directory]::CreateDirectory($root); $script:fixtures.Add($root)
 $local=Join-Path $root 'LocalAppData'; $hostRoot=Join-Path $root 'Shell'; $extension=Join-Path $hostRoot 'Common7\IDE\Extensions\TwinCatAssistant.Native.X86'
 [void][IO.Directory]::CreateDirectory($extension)
 1..11 | ForEach-Object {[IO.File]::WriteAllText((Join-Path $extension ('original-'+$_+'.dll')),('prototype-'+$_))}
 [IO.File]::WriteAllText((Join-Path $extension 'TCatSysManagerLib.dll'),'synthetic-local-interop')
 [IO.File]::WriteAllText((Join-Path $extension 'TwinCatAssistant.IdeHost.pkgdef'),'synthetic-original-registration')
 $marker=Join-Path (Split-Path $extension -Parent) 'extensions.configurationchanged'; [IO.File]::WriteAllText($marker,'original-marker-bytes')
 $profile=Join-Path $local 'TwinCatAssistant\host-tests\Synthetic_1'; [void][IO.Directory]::CreateDirectory($profile)
 $config=Join-Path $profile 'native-chat.json'; $dotnet=Join-Path $root 'System\dotnet'
 Write-MmAtomic $config ([ordered]@{StateRoot=(Join-Path $profile 'state');DotNetRoot=$dotnet}) -New
 [IO.File]::WriteAllText((Join-Path $local 'TwinCatAssistant\native-chat.json'),'operator-config-sentinel')
 foreach($name in @('chat','login','bindings','preferences')){[IO.File]::WriteAllText((Join-Path $profile ($name+'.sentinel')),('latest-'+$name))}
 $package=Join-Path $root 'input'; [void][IO.Directory]::CreateDirectory((Join-Path $package 'host\x86')); [IO.File]::WriteAllText((Join-Path $package 'host\x86\TwinCatAssistant.IdeHost.dll'),'synthetic-guard-DLL-not-executable')
 [IO.File]::WriteAllText((Join-Path $package 'host\x86\TwinCatAssistant.IdeHost.pkgdef'),'synthetic-candidate-registration')
 $entries=@(Get-MmTree $package | Where-Object kind -CEQ 'file' | ForEach-Object {[pscustomobject][ordered]@{path=$_.path;size=$_.size;sha256=$_.sha256}} | Sort-Object path)
 Write-MmAtomic (Join-Path $package 'application-manifest.json') ([ordered]@{schemaVersion=1;version='0.2.0-preview.2';platform='win32';architecture='x64';files=$entries}) -New
 # Distinct timestamps exercise precision and preservation, without touching ACLs.
 $ticks=([datetime]'2026-01-02T03:04:05Z').Ticks
 foreach($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)){[IO.File]::SetCreationTimeUtc($file.FullName,[datetime]::new($ticks,[DateTimeKind]::Utc));[IO.File]::SetLastWriteTimeUtc($file.FullName,[datetime]::new($ticks+1234567,[DateTimeKind]::Utc))}
 $facts=[pscustomobject]@{mode='synthetic';operatorSid='S-1-5-21-synthetic';localAppData=$local;systemDotNetRoot=$dotnet;hostRoot=$hostRoot;extension=$extension;marker=$marker;globalConfiguration=(Join-Path $local 'TwinCatAssistant\native-chat.json');searchRoots=@((Split-Path $extension -Parent));activeProcesses=$false;unknownProcesses=$false;webViewOverride=$false}
 $getFacts={return $facts}.GetNewClosure()
 $request=[pscustomobject]@{runId='run_1';configurationPath=$config;packageRoot=$package}
 return [pscustomobject]@{root=$root;facts=$facts;getFacts=$getFacts;request=$request;dll=(Get-MmHash (Join-Path $package 'host\x86\TwinCatAssistant.IdeHost.dll'));manifest=(Get-MmHash (Join-Path $package 'application-manifest.json'));profile=$profile;configRecord=(Get-MmFileRecord $config);original=(Get-MmTree $extension);markerRecord=(Get-MmFileRecord $marker);globalRecord=(Get-MmFileRecord $facts.globalConfiguration)}
}
function Prepare-Fixture($f) { return (New-MmPlan $f.request $f.getFacts $f.dll $f.manifest) }
function Invoke-Fixture($f,$p,[string]$Action='Enter',[string]$Fault='', [string]$Interrupt=''){Invoke-MmTransaction $Action $p.planPath $p.planSha256 $f.getFacts $Fault $Interrupt}
function Assert-Original($f) {
 Assert-MmTree $f.facts.extension $f.original
 Assert-MmFileRecord $f.facts.marker $f.markerRecord
 Assert-MmFileRecord $f.facts.globalConfiguration $f.globalRecord
 Assert-MmFileRecord $f.request.configurationPath $f.configRecord
 foreach($name in @('chat','login','bindings','preferences')){Assert-Test ([IO.File]::ReadAllText((Join-Path $f.profile ($name+'.sentinel'))) -ceq ('latest-'+$name)) 'Private latest state changed.'}
}

$opaque=New-Fixture
$stateDirectory=Join-Path $opaque.profile 'state'
[void][IO.Directory]::CreateDirectory($stateDirectory)
$opaqueFile=Join-Path $stateDirectory 'chat-login-data.json'
[IO.File]::WriteAllText($opaqueFile,'synthetic private data must remain opaque')
$opaqueHandle=[IO.File]::Open($opaqueFile,'Open','ReadWrite','None')
try {
 $opaquePlan=Prepare-Fixture $opaque
 Invoke-Fixture $opaque $opaquePlan
 Invoke-Fixture $opaque $opaquePlan 'Restore'
 Assert-Original $opaque
} finally { $opaqueHandle.Dispose() }
Assert-Test ([IO.File]::ReadAllText($opaqueFile) -ceq 'synthetic private data must remain opaque') 'Opaque state bytes changed.'
Add-Check 'exclusive state-content lock does not block prepare, enter or restore'
if($ProfileOpaqueOnly) {
 if($EvidencePath) {
  Assert-MmPath $EvidencePath
  $core=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Maintenance.Core.ps1')).Replace("`r`n","`n")
  Write-MmAtomic $EvidencePath ([ordered]@{kind='targeted-profile-opacity-regression';groups=1;status='passed';coreCanonicalLfSha256=(Get-MmTextHash $core);checks=$script:checks.ToArray();liveActions=$false}) -New
 }
 exit 0
}

$f=New-Fixture; $p=Prepare-Fixture $f; Assert-Original $f
$plan=Read-MmJson $p.planPath
Assert-Test (@($plan.original | Where-Object kind -CEQ 'file').Count -eq 13) 'Expected original thirteen files.'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $f.request.packageRoot 'host\x86\assistant-test-maintenance.json'))) 'Default package marked.'
Assert-MmKeys $plan.maintenanceMarker 'configurationPath,configurationSha256,operatorSid,schemaVersion,selectorSha256'
Invoke-Fixture $f $p
Assert-MmTree $f.facts.extension $plan.candidate
Assert-Test (Test-Path -LiteralPath (Join-Path $f.facts.extension 'assistant-test-maintenance.json')) 'Published host missing marker.'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path (Split-Path $f.facts.extension -Parent) 'original'))) 'Old pkgdef left under search root.'
Invoke-Fixture $f $p 'Restore'; Assert-Original $f
Invoke-Fixture $f $p 'Restore'; Assert-Original $f
Add-Check 'prepare/enter/restore/idempotent restore preserves 13 original files, config, state and marker timestamps'

$entryPoints=@('parking-original','after-original-rename','original-parked','publishing-candidate','after-candidate-rename','candidate-published','refreshing-marker','after-marker-touch','entered')
foreach($point in $entryPoints) {
 $f=New-Fixture; $p=Prepare-Fixture $f
 Test-Refusal {Invoke-Fixture $f $p 'Enter' $point} 'INJECTED-FAILURE'
 Assert-Original $f
 Assert-Test ((Read-MmJson (Join-Path (Split-Path $p.planPath -Parent) 'journal.json')).phase -ceq 'restored') 'Caught failure did not restore.'
}
Add-Check ('caught entry failure restores original at all '+$entryPoints.Count+' transition boundaries')
foreach($point in $entryPoints) {
 $f=New-Fixture; $p=Prepare-Fixture $f
 Test-Refusal {Invoke-Fixture $f $p 'Enter' '' $point} 'SIMULATED-INTERRUPTION'
 Test-Refusal {Invoke-Fixture $f $p} ''
 Invoke-Fixture $f $p 'Restore'; Assert-Original $f
}
Add-Check ('explicit recovery after all '+$entryPoints.Count+' entry interruption boundaries')
$restorePoints=@('withdrawing-candidate','after-withdraw-rename','candidate-withdrawn','restoring-original','after-restore-rename','original-restored','after-marker-restore','restored')
foreach($point in $restorePoints) {
 $f=New-Fixture; $p=Prepare-Fixture $f; Invoke-Fixture $f $p
 Test-Refusal {Invoke-Fixture $f $p 'Restore' '' $point} 'SIMULATED-INTERRUPTION'
 Invoke-Fixture $f $p 'Restore'; Assert-Original $f
}
Add-Check ('explicit recovery after all '+$restorePoints.Count+' restore interruption boundaries')

foreach($which in @('activeProcesses','unknownProcesses','operatorSid','hostRoot','localAppData','systemDotNetRoot','webViewOverride','searchRoots')) {
 foreach($action in @('Enter','Restore')) {
  $f=New-Fixture; $p=Prepare-Fixture $f
  if($action -ceq 'Restore'){Invoke-Fixture $f $p}
  $before=ConvertTo-MmJson (Get-MmTree $f.root)
  $value=$f.facts.$which
  if($value -is [bool]){$f.facts.$which=$true}elseif($which -ceq 'searchRoots'){$f.facts.$which=@('C:\changed-search-root')}else{$f.facts.$which='mismatched'}
  Test-Refusal {Invoke-Fixture $f $p $action} ''
  Assert-Test ((ConvertTo-MmJson (Get-MmTree $f.root)) -ceq $before) 'Refusal changed payload/config/journal bytes or timestamps.'
 }
}
Add-Check 'entry and restore refuse active/unknown processes, wrong SID/host/root/runtime/WebView before writes'

$f=New-Fixture; $p=Prepare-Fixture $f; $run=Split-Path $p.planPath -Parent
$facts=$f.facts; $park=Join-Path $run 'original'; $switch=[pscustomobject]@{enabled=$true}
$becameActive={ $facts.activeProcesses=$switch.enabled -and (Test-Path -LiteralPath $park); return $facts }.GetNewClosure()
Test-Refusal {Invoke-MmTransaction 'Enter' $p.planPath $p.planSha256 $becameActive} 'automatic restore refused'
Assert-Test (-not (Test-Path -LiteralPath $f.facts.extension) -and (Test-Path -LiteralPath $park)) 'Active-process refusal performed unsafe publication/restoration.'
$switch.enabled=$false; $facts.activeProcesses=$false
Invoke-Fixture $f $p 'Restore'; Assert-Original $f
Add-Check 'process appearing after original parking prevents publication and recovery writes until explicit quiescent restore'

foreach($which in @('backup','stage','application','plan','journal','profile','global','marker')) {
 $f=New-Fixture; $p=Prepare-Fixture $f; $run=Split-Path $p.planPath -Parent
 $target=switch($which){'backup'{Join-Path $run 'backup\original-1.dll'}'stage'{Join-Path $run 'stage\assistant-test-maintenance.json'}'application'{Join-Path $run 'application\host\x86\TwinCatAssistant.IdeHost.dll'}'plan'{$p.planPath}'journal'{Join-Path $run 'journal.json'}'profile'{$f.request.configurationPath}'global'{$f.facts.globalConfiguration}'marker'{$f.facts.marker}}
 [IO.File]::AppendAllText($target,'tamper')
 $before=ConvertTo-MmJson (Get-MmTree $f.root)
 Test-Refusal {Invoke-Fixture $f $p} ''
 Assert-Test ((ConvertTo-MmJson (Get-MmTree $f.root)) -ceq $before) 'Tamper refusal changed files.'
}
Add-Check 'tampered backup/stage/application/plan/journal/private/global/marker refuses before entry mutation'

foreach($which in @('backup','candidate','profile','global','journal-layout')) {
 $f=New-Fixture; $p=Prepare-Fixture $f; Invoke-Fixture $f $p; $run=Split-Path $p.planPath -Parent
 if($which -ceq 'journal-layout'){
  $j=Read-MmJson (Join-Path $run 'journal.json'); $j.phase='prepared'; Write-MmAtomic (Join-Path $run 'journal.json') $j
 } else {
  $target=switch($which){'backup'{Join-Path $run 'backup\original-1.dll'}'candidate'{Join-Path $f.facts.extension 'TwinCatAssistant.IdeHost.dll'}'profile'{$f.request.configurationPath}'global'{$f.facts.globalConfiguration}}
  [IO.File]::AppendAllText($target,'tamper')
 }
 $before=ConvertTo-MmJson (Get-MmTree $f.root)
 Test-Refusal {Invoke-Fixture $f $p 'Restore'} ''
 Assert-Test ((ConvertTo-MmJson (Get-MmTree $f.root)) -ceq $before) 'Refused restore wrote over evidence.'
}
Add-Check 'restore refuses changed backup/candidate/private/global and contradictory journal without overwriting evidence'

foreach($kind in @('partial-backup','writing','wrong-lock','hardlink','junction','profile-root')) {
 $f=New-Fixture; $p=Prepare-Fixture $f; $run=Split-Path $p.planPath -Parent
 switch($kind) {
  'partial-backup' {[IO.File]::Move((Join-Path $run 'backup\original-1.dll'),(Join-Path $run 'retained-missing-backup-file'))}
  'writing' {[IO.File]::WriteAllText((Join-Path $run 'journal.json.writing'),'interrupted')}
  'wrong-lock' {[IO.File]::WriteAllText((Join-Path $run 'maintenance.lock'),('f'*64))}
  'hardlink' {New-Item -ItemType HardLink -Path (Join-Path $f.profile 'linked-config') -Target $f.request.configurationPath | Out-Null}
  'junction' {[void][IO.Directory]::CreateDirectory((Join-Path $f.root 'foreign')); New-Item -ItemType Junction -Path (Join-Path $f.profile 'junction') -Target (Join-Path $f.root 'foreign') | Out-Null}
  'profile-root' {Write-MmAtomic $f.request.configurationPath ([ordered]@{StateRoot=(Join-Path $f.root 'wrong');DotNetRoot=$f.facts.systemDotNetRoot})}
 }
 $journalHash=Get-MmHash (Join-Path $run 'journal.json')
 Test-Refusal {Invoke-Fixture $f $p} ''
 Assert-Test ((Get-MmHash (Join-Path $run 'journal.json')) -ceq $journalHash) 'Preflight refusal changed journal.'
 Assert-MmTree $f.facts.extension $f.original
 Assert-MmFileRecord $f.facts.marker $f.markerRecord
}
Add-Check 'partial backup, pending atomic write, foreign lock, hardlink, junction and wrong private root refuse without payload/journal mutation'

foreach($kind in @('unknown','selector','owner','existing-marker')) {
 $f=New-Fixture
 $name=switch($kind){'unknown'{'assistant-foreign.json'}'selector'{'assistant-installation.json'}'owner'{'.assistant-owner'}'existing-marker'{'assistant-test-maintenance.json'}}
 [IO.File]::WriteAllText((Join-Path $f.facts.extension $name),'false')
 Test-Refusal {Prepare-Fixture $f} ''
 Assert-Test (-not (Test-Path -LiteralPath (Join-Path $f.facts.localAppData 'TwinCatAssistant\host-maintenance\run_1'))) 'Invalid metadata created run.'
}
Add-Check 'unknown/malformed original metadata and existing maintenance marker refuse before run creation'

$f=New-Fixture
Write-MmAtomic (Join-Path $f.facts.extension 'assistant-installation.json') ([ordered]@{schemaVersion=1;targetId='tcxaeshell-x86-tc4024';version='0.1.0-dev.1';applicationRoot=(Join-Path $f.root 'old-package')}) -New
[IO.File]::WriteAllText((Join-Path $f.facts.extension '.assistant-owner'),"tcxaeshell-x86-tc4024`n0.1.0-dev.1`n")
$f.original=Get-MmTree $f.facts.extension; $p=Prepare-Fixture $f; Invoke-Fixture $f $p; Invoke-Fixture $f $p 'Restore'; Assert-Original $f
Add-Check 'existing exact selector/ownership metadata is backed up and restored byte-for-byte'

$f=New-Fixture; $p=Prepare-Fixture $f
$lock=[IO.File]::Open((Join-Path $f.facts.hostRoot '.twincat-assistant-deployment.lock'),'OpenOrCreate','ReadWrite','None')
try{Test-Refusal {Invoke-Fixture $f $p} ''}finally{$lock.Dispose()}
Invoke-Fixture $f $p; Invoke-Fixture $f $p 'Restore'; Assert-Original $f
Add-Check 'production shared kernel lock blocks concurrent entry; released persistent lock needs no PID guessing'

$f=New-Fixture; $p=Prepare-Fixture $f
Test-Refusal {Invoke-MmTransaction 'Enter' $p.planPath ('0'*64) $f.getFacts} 'plan hash'
$module=Import-Module "$PSScriptRoot/Maintenance.psm1" -PassThru -Force
Test-Refusal {Enter-AssistantMaintenance -PlanPath $p.planPath -ExpectedPlanSha256 $p.planSha256 -OperatorOptIn I_ACCEPT_TEMPORARY_GLOBAL_MAINTENANCE_AND_RESTORE} 'synthetic/unreviewed'
foreach($name in @('New-AssistantMaintenancePlan','Enter-AssistantMaintenance','Restore-AssistantMaintenance')) {
 $parameters=(Get-Command $name).Parameters.Keys
 Assert-Test (-not @($parameters | Where-Object {$_ -match 'Facts|Synthetic|Inventory|Process|Sid|HostRoot'}).Count) 'Public entry exposed synthetic/live-fact substitution.'
}
Remove-Module $module
Add-Check 'public real surface rejects synthetic plan and exposes no fact/process/SID/host injection'

$record=[ordered]@{schemaVersion=1;scope='synthetic maintenance transaction engine only';completed=$true;groups=$script:checks.Count;checks=@($script:checks.ToArray());entryFaultBoundaries=$entryPoints;restoreInterruptionBoundaries=$restorePoints;actualIdeStarted=$false;backendStarted=$false;nativeAssemblyLoaded=$false;globalHostMutation=$false;operatorProfileTouched=$false;productionInstallerInvoked=$false;fixtureFilesRetained=$true}
if($EvidencePath){Write-MmAtomic ([IO.Path]::GetFullPath($EvidencePath)) $record -New}
Write-Output ('Completed '+$script:checks.Count+' synthetic groups; fixture files retained in '+$sandbox)
