# Internal transaction engine. Public commands supply live facts, never caller fixtures.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:MmGuardDll='77e03ab221db44cd2a4eb442fa778f22de3da565940272312e540d85bac10ffd'
$script:MmGuardManifest='6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271'
function Assert-MmRegular([string]$Path) {
 $item=Get-Item -LiteralPath $Path -Force
 # Windows PowerShell 5's FileSystem provider obtains LinkType from link
 # metadata. Missing support is an error, never permission to follow a link.
 if($item.PSIsContainer -or -not $item.PSObject.Properties['LinkType'] -or $item.LinkType -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Maintenance requires a regular single-link file.'}
}
function Assert-MmPath([string]$Path) {
 if (-not [IO.Path]::IsPathRooted($Path) -or $Path.StartsWith('\\') -or $Path -cne [IO.Path]::GetFullPath($Path) -or $Path -match ':[^\\]' -or $Path.Length -gt 2048) { throw 'Noncanonical maintenance path.' }
 $p=$Path
 while ($p) {
  if (Test-Path -LiteralPath $p) { if ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Maintenance reparse path refused.' } }
  $p=Split-Path -Parent $p
 }
}
function Get-MmHash([string]$Path) {
 Assert-MmPath $Path; Assert-MmRegular $Path
 $stream=[IO.File]::Open($Path,'Open','Read','Read')
 try { $s=[Security.Cryptography.SHA256]::Create(); try { return ([BitConverter]::ToString($s.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $s.Dispose() } } finally { $stream.Dispose() }
}
function Get-MmTextHash([string]$Text) {
 $s=[Security.Cryptography.SHA256]::Create()
 try { return ([BitConverter]::ToString($s.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','').ToLowerInvariant() } finally { $s.Dispose() }
}
function ConvertTo-MmJson($Value) { return (($Value | ConvertTo-Json -Depth 20 -Compress)+"`n") }
function Read-MmJson([string]$Path,[int]$Limit=33554432) {
 [void](Get-MmHash $Path)
 if ((Get-Item -LiteralPath $Path).Length -gt $Limit) { throw 'Maintenance JSON too large.' }
 return ([IO.File]::ReadAllText($Path,(New-Object Text.UTF8Encoding($false,$true))) | ConvertFrom-Json)
}
function Assert-MmKeys($Value,[string]$Keys) {
 if (-not $Value -or (($Value.PSObject.Properties.Name | Sort-Object) -join ',') -cne $Keys) { throw 'Unknown maintenance metadata.' }
}
function Get-MmTree([string]$Root) {
 Assert-MmPath $Root
 if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw 'Maintenance tree missing.' }
 $entries=New-Object Collections.Generic.List[object]
 $walk={ param($dir,$prefix)
  foreach($e in @(Get-ChildItem -LiteralPath $dir -Force | Sort-Object Name)) {
   Assert-MmPath $e.FullName
   $relative=if($prefix){$prefix+'/'+$e.Name}else{$e.Name}
   if ($e.Name -match '[<>:"|?*\x00-\x1f]' -or $e.Name.EndsWith('.') -or $e.Name.EndsWith(' ')) { throw 'Unsafe maintenance tree name.' }
   if($e.PSIsContainer) { $entries.Add([pscustomobject][ordered]@{path=$relative;kind='directory';writeTicks=$e.LastWriteTimeUtc.Ticks;creationTicks=$e.CreationTimeUtc.Ticks}); & $walk $e.FullName $relative }
   else { $entries.Add([pscustomobject][ordered]@{path=$relative;kind='file';size=$e.Length;sha256=(Get-MmHash $e.FullName);writeTicks=$e.LastWriteTimeUtc.Ticks;creationTicks=$e.CreationTimeUtc.Ticks}) }
  }
 }
 & $walk $Root ''
 return $entries.ToArray()
}
function Assert-MmTree([string]$Root,$Expected) {
 if ((ConvertTo-MmJson (Get-MmTree $Root)) -cne (ConvertTo-MmJson @($Expected))) { throw 'Maintenance tree bytes or timestamps changed.' }
}
function Copy-MmTree([string]$Source,[string]$Destination) {
 $inventory=Get-MmTree $Source
 if(Test-Path -LiteralPath $Destination){throw 'Maintenance copy destination exists.'}
 [void][IO.Directory]::CreateDirectory($Destination)
 foreach($e in $inventory) {
  $dest=Join-Path $Destination ($e.path.Replace('/', '\'))
  if($e.kind -ceq 'directory') { [void][IO.Directory]::CreateDirectory($dest) }
  else { [IO.File]::Copy((Join-Path $Source ($e.path.Replace('/', '\'))),$dest,$false); [IO.File]::SetCreationTimeUtc($dest,[datetime]::new([long]$e.creationTicks,[DateTimeKind]::Utc)); [IO.File]::SetLastWriteTimeUtc($dest,[datetime]::new([long]$e.writeTicks,[DateTimeKind]::Utc)) }
 }
 foreach($e in @($inventory | Where-Object kind -CEQ 'directory' | Sort-Object path -Descending)) {
  $dest=Join-Path $Destination ($e.path.Replace('/', '\')); [IO.Directory]::SetCreationTimeUtc($dest,[datetime]::new([long]$e.creationTicks,[DateTimeKind]::Utc)); [IO.Directory]::SetLastWriteTimeUtc($dest,[datetime]::new([long]$e.writeTicks,[DateTimeKind]::Utc))
 }
 Assert-MmTree $Destination $inventory
}
function Write-MmAtomic([string]$Path,$Value,[switch]$New) {
 Assert-MmPath $Path
 if(Test-Path -LiteralPath ($Path+'.writing')) { throw 'Interrupted atomic metadata write; manual review required.' }
 if($New -and (Test-Path -LiteralPath $Path)){throw 'Metadata already exists.'}
 $bytes=[Text.Encoding]::UTF8.GetBytes((ConvertTo-MmJson $Value))
 $file=[IO.File]::Open($Path+'.writing','CreateNew','Write','None')
 try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
 if(Test-Path -LiteralPath $Path){
  # An actual backup path also avoids PowerShell 5 coercing null to an empty
  # string for File.Replace. Retain it as evidence; never infer a recovery phase.
  if(Test-Path -LiteralPath ($Path+'.previous')){[void](Get-MmHash ($Path+'.previous'))}
  [IO.File]::Replace($Path+'.writing',$Path,$Path+'.previous')
 }else{[IO.File]::Move($Path+'.writing',$Path)}
}
function Get-MmFileRecord([string]$Path) {
 if(-not (Test-Path -LiteralPath $Path)){return [ordered]@{present=$false}}
 $hash=Get-MmHash $Path; $f=Get-Item -LiteralPath $Path
 return [ordered]@{present=$true;sha256=$hash;writeTicks=$f.LastWriteTimeUtc.Ticks;creationTicks=$f.CreationTimeUtc.Ticks}
}
function Assert-MmFileRecord([string]$Path,$Expected) {
 if((ConvertTo-MmJson (Get-MmFileRecord $Path)) -cne (ConvertTo-MmJson $Expected)){throw 'Protected global file changed.'}
}
function Assert-MmProfile($Plan,$Facts) {
 if($Plan.operatorSid -cne $Facts.operatorSid){throw 'Maintenance SID mismatch.'}
 $prefix=Join-Path $Facts.localAppData 'TwinCatAssistant\host-tests'
 $config=[string]$Plan.configuration.path; Assert-MmPath $config
 if(-not $config.StartsWith($prefix+'\',[StringComparison]::Ordinal) -or $config.Substring($prefix.Length+1) -cnotmatch '^[a-zA-Z0-9_-]{1,64}\\native-chat\.json$'){throw 'Maintenance private configuration path mismatch.'}
 if((Get-MmHash $config) -cne $Plan.configuration.sha256){throw 'Maintenance private configuration hash mismatch.'}
 $c=Read-MmJson $config 65536; Assert-MmKeys $c 'DotNetRoot,StateRoot'
 if($c.StateRoot -cne (Join-Path (Split-Path $config -Parent) 'state') -or $c.StateRoot -cne $Plan.configuration.stateRoot -or $c.DotNetRoot -cne $Facts.systemDotNetRoot){throw 'Maintenance private state/system runtime mismatch.'}
 [void](Get-MmTree (Split-Path $config -Parent))
 if($Facts.webViewOverride){throw 'Inherited WebView override refused.'}
}
function Assert-MmFacts($Plan,[scriptblock]$GetFacts) {
 $f=& $GetFacts
 if($f.mode -cne $Plan.mode -or $f.activeProcesses -or $f.unknownProcesses){throw 'Maintenance requires inspected, quiescent IDE/backend owners.'}
 if($f.hostRoot -cne $Plan.host.root -or $f.extension -cne $Plan.host.extension -or $f.marker -cne $Plan.host.marker -or $f.globalConfiguration -cne $Plan.host.globalConfiguration){throw 'Live host selection changed.'}
 if((ConvertTo-MmJson @($f.searchRoots | Sort-Object -Unique)) -cne (ConvertTo-MmJson @($Plan.host.searchRoots | Sort-Object -Unique))){throw 'Live extension search paths changed.'}
 Assert-MmProfile $Plan $f
 Assert-MmFileRecord $Plan.host.globalConfiguration $Plan.globalConfiguration
 return $f
}
function Test-MmPackage([string]$Root,[string]$ManifestHash,[string]$DllHash) {
 if((Get-MmHash (Join-Path $Root 'application-manifest.json')) -cne $ManifestHash){throw 'Maintenance package manifest changed.'}
 $m=Read-MmJson (Join-Path $Root 'application-manifest.json'); Assert-MmKeys $m 'architecture,files,platform,schemaVersion,version'
 if($m.schemaVersion -isnot [int] -or $m.schemaVersion -ne 1 -or $m.platform -cne 'win32' -or $m.architecture -cne 'x64' -or $m.version -cne '0.2.0-preview.2'){throw 'Unsupported maintenance package.'}
 $actual=@(Get-MmTree $Root | Where-Object kind -CEQ 'file' | Where-Object path -CNE 'application-manifest.json' | ForEach-Object {[pscustomobject][ordered]@{path=$_.path;size=$_.size;sha256=$_.sha256}} | Sort-Object path)
 $expected=@($m.files | Sort-Object path)
 if((ConvertTo-MmJson $actual) -cne (ConvertTo-MmJson $expected)){throw 'Maintenance full package inventory mismatch.'}
 if(@($m.files | Where-Object {$_.path -match '(^|/)TCatSysManagerLib\.dll$|\.vsix$|(^|/)assistant-test-maintenance\.json$'}).Count){throw 'Maintenance package contains vendor interop/archive/default marker.'}
 if((Get-MmHash (Join-Path $Root 'host\x86\TwinCatAssistant.IdeHost.dll')) -cne $DllHash){throw 'Reviewed maintenance guard DLL required.'}
 return $m
}
function Assert-MmOriginalMetadata([string]$Root) {
 foreach($f in @(Get-ChildItem -LiteralPath $Root -Force -Recurse -File)) {
  if($f.Name -ceq 'assistant-test-maintenance.json'){throw 'Original already carries maintenance metadata.'}
  if(($f.Name -like 'assistant-*.json' -or $f.Name -like '.assistant-*') -and $f.Name -cnotin @('assistant-installation.json','.assistant-owner')){throw 'Unknown original Assistant metadata.'}
  if($f.Name -ceq 'assistant-installation.json') {
   $s=Read-MmJson $f.FullName 65536; Assert-MmKeys $s 'applicationRoot,schemaVersion,targetId,version'
   if($s.schemaVersion -isnot [int] -or $s.schemaVersion -ne 1 -or $s.targetId -cne 'tcxaeshell-x86-tc4024' -or $s.version -isnot [string] -or $s.version -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$'){throw 'Unknown original selector.'}; Assert-MmPath $s.applicationRoot
  }
  if($f.Name -ceq '.assistant-owner' -and [IO.File]::ReadAllText($f.FullName) -cnotmatch '\Atcxaeshell-x86-tc4024\n[0-9A-Za-z.+-]+\n\z'){throw 'Unknown original ownership marker.'}
 }
 $selectionPath=Join-Path $Root 'assistant-installation.json'; $ownerPath=Join-Path $Root '.assistant-owner'
 if((Test-Path -LiteralPath $selectionPath) -xor (Test-Path -LiteralPath $ownerPath)){throw 'Incomplete original ownership metadata.'}
 if(Test-Path -LiteralPath $selectionPath) {
  $s=Read-MmJson $selectionPath 65536
  if([IO.File]::ReadAllText($ownerPath) -cne ("tcxaeshell-x86-tc4024`n"+$s.version+"`n")){throw 'Original selector/owner disagree.'}
 }
}
function Assert-MmRunPaths($Plan,$Facts) {
 if($Plan.runId -cnotmatch '^[a-zA-Z0-9_-]{1,64}$'){throw 'Invalid maintenance run id.'}
 $run=Join-Path (Join-Path $Facts.localAppData 'TwinCatAssistant\host-maintenance') $Plan.runId
 if($Plan.runRoot -cne $run){throw 'Unexpected maintenance run root.'}
 foreach($p in @($run,$Plan.host.extension,$Plan.host.marker,$Plan.source.root,$Plan.configuration.path)) { Assert-MmPath $p }
 if([IO.Path]::GetPathRoot($run) -cne [IO.Path]::GetPathRoot($Plan.host.extension)){throw 'Same-volume directory rename required.'}
 if($run -match '(?i)(^|\\)Extensions(\\|$)' -or $Plan.source.root -match '(?i)(^|\\)Extensions(\\|$)'){throw 'Backups/staging/package must be outside every extension search directory.'}
 foreach($path in @($Facts.searchRoots)) {
  if($run.StartsWith($path.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $run -ieq $path){throw 'Maintenance run is inside an extension search path.'}
 }
 if($run.StartsWith($Plan.host.root+'\',[StringComparison]::OrdinalIgnoreCase) -or $Plan.source.root.StartsWith($run+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Overlapping maintenance roots.'}
}
function New-MmPlan($Request,[scriptblock]$GetFacts,[string]$DllHash,[string]$ManifestHash) {
 $facts=& $GetFacts
 if($facts.activeProcesses -or $facts.unknownProcesses){throw 'Maintenance preparation requires quiescence.'}
 $config=[string]$Request.configurationPath
 $plan=[ordered]@{schemaVersion=1;kind='assistant-maintenance-plan';mode=$facts.mode;runId=$Request.runId;runRoot=(Join-Path (Join-Path $facts.localAppData 'TwinCatAssistant\host-maintenance') $Request.runId);operatorSid=$facts.operatorSid;configuration=[ordered]@{path=$config;sha256=(Get-MmHash $config);stateRoot=(Join-Path (Split-Path $config -Parent) 'state')};host=[ordered]@{root=$facts.hostRoot;extension=$facts.extension;marker=$facts.marker;globalConfiguration=$facts.globalConfiguration;searchRoots=@($facts.searchRoots | Sort-Object -Unique)};source=[ordered]@{root=$Request.packageRoot;manifestSha256=$ManifestHash;guardDllSha256=$DllHash};globalConfiguration=(Get-MmFileRecord $facts.globalConfiguration);original=(Get-MmTree $facts.extension);hostMarker=(Get-MmFileRecord $facts.marker)}
 Assert-MmRunPaths ([pscustomobject]$plan) $facts
 Assert-MmProfile ([pscustomobject]$plan) $facts
 Assert-MmOriginalMetadata $facts.extension
 $null=Test-MmPackage $Request.packageRoot $ManifestHash $DllHash
 if(-not $plan.hostMarker.present){throw 'Existing host configuration marker required.'}
 if(Test-Path -LiteralPath $plan.runRoot){throw 'Maintenance run exists; explicit journal review required.'}
 [void](Assert-MmFacts ([pscustomobject]$plan) $GetFacts)
 [void][IO.Directory]::CreateDirectory($plan.runRoot)
 # Never touch the live extension or its marker during preparation.
 Copy-MmTree $facts.extension (Join-Path $plan.runRoot 'backup')
 [IO.File]::Copy($facts.marker,(Join-Path $plan.runRoot 'host-marker.original'),$false)
 $markerCopy=Join-Path $plan.runRoot 'host-marker.original'
 [IO.File]::SetCreationTimeUtc($markerCopy,[datetime]::new([long]$plan.hostMarker.creationTicks,[DateTimeKind]::Utc)); [IO.File]::SetLastWriteTimeUtc($markerCopy,[datetime]::new([long]$plan.hostMarker.writeTicks,[DateTimeKind]::Utc))
 [void](Assert-MmFacts ([pscustomobject]$plan) $GetFacts)
 Copy-MmTree $Request.packageRoot (Join-Path $plan.runRoot 'application')
 $null=Test-MmPackage (Join-Path $plan.runRoot 'application') $ManifestHash $DllHash
 [void](Assert-MmFacts ([pscustomobject]$plan) $GetFacts)
 $stage=Join-Path $plan.runRoot 'stage'
 Copy-MmTree (Join-Path $plan.runRoot 'application\host\x86') $stage
 # Local interop stays only in local backup/stage, never in the application distribution.
 $interop=Join-Path $facts.extension 'TCatSysManagerLib.dll'
 [void](Get-MmHash $interop); [IO.File]::Copy($interop,(Join-Path $stage 'TCatSysManagerLib.dll'),$false)
 foreach($reserved in @('.assistant-owner','assistant-installation.json','assistant-test-maintenance.json')) { if(Test-Path -LiteralPath (Join-Path $stage $reserved)){throw 'Reserved candidate host metadata.'} }
 $selector=[ordered]@{schemaVersion=1;targetId='tcxaeshell-x86-tc4024';version='0.2.0-preview.2';applicationRoot=(Join-Path $plan.runRoot 'application')}
 Write-MmAtomic (Join-Path $stage 'assistant-installation.json') $selector -New
 $marker=[ordered]@{schemaVersion=1;configurationPath=$config;configurationSha256=$plan.configuration.sha256;operatorSid=$plan.operatorSid;selectorSha256=(Get-MmHash (Join-Path $stage 'assistant-installation.json'))}
 Write-MmAtomic (Join-Path $stage 'assistant-test-maintenance.json') $marker -New
 $plan['selector']=$selector; $plan['maintenanceMarker']=$marker; $plan['candidate']=Get-MmTree $stage
 [void](Assert-MmFacts ([pscustomobject]$plan) $GetFacts)
 Assert-MmTree $facts.extension $plan.original
 Assert-MmFileRecord $facts.marker $plan.hostMarker
 $path=Join-Path $plan.runRoot 'plan.json'
 Write-MmAtomic $path $plan -New
 $hash=Get-MmHash $path
 Write-MmAtomic (Join-Path $plan.runRoot 'journal.json') ([ordered]@{schemaVersion=1;planSha256=$hash;phase='prepared';markerWriteTicks=0}) -New
 return [pscustomobject]@{planPath=$path;planSha256=$hash;mode=$facts.mode}
}
function Get-MmPlan([string]$PlanPath,[string]$ExpectedSha) {
 if($ExpectedSha -cnotmatch '^[0-9a-f]{64}$' -or (Get-MmHash $PlanPath) -cne $ExpectedSha){throw 'Exact independently reviewed plan hash required.'}
 $plan=Read-MmJson $PlanPath
 Assert-MmKeys $plan 'candidate,configuration,globalConfiguration,host,hostMarker,kind,maintenanceMarker,mode,operatorSid,original,runId,runRoot,schemaVersion,selector,source'
 if($plan.schemaVersion -isnot [int] -or $plan.schemaVersion -ne 1 -or $plan.kind -cne 'assistant-maintenance-plan' -or $PlanPath -cne (Join-Path $plan.runRoot 'plan.json')){throw 'Invalid maintenance plan.'}
 return $plan
}
function Assert-MmRecoveryInputs($Plan) {
 Assert-MmTree (Join-Path $Plan.runRoot 'backup') $Plan.original
 Assert-MmFileRecord (Join-Path $Plan.runRoot 'host-marker.original') $Plan.hostMarker
 $null=Test-MmPackage (Join-Path $Plan.runRoot 'application') $Plan.source.manifestSha256 $Plan.source.guardDllSha256
 foreach($name in @('stage','withdrawn')) {
  $p=Join-Path $Plan.runRoot $name
  if(Test-Path -LiteralPath $p){Assert-MmTree $p $Plan.candidate}
 }
 if(Test-Path -LiteralPath (Join-Path $Plan.runRoot 'original')){Assert-MmTree (Join-Path $Plan.runRoot 'original') $Plan.original}
}
function Assert-MmLayout($Plan,[string]$Phase) {
 $live=Test-Path -LiteralPath $Plan.host.extension
 $park=Test-Path -LiteralPath (Join-Path $Plan.runRoot 'original')
 $stage=Test-Path -LiteralPath (Join-Path $Plan.runRoot 'stage')
 $withdrawn=Test-Path -LiteralPath (Join-Path $Plan.runRoot 'withdrawn')
 $originalLive=$false; $candidateLive=$false
 if($live) {
  $tree=ConvertTo-MmJson (Get-MmTree $Plan.host.extension)
  $originalLive=$tree -ceq (ConvertTo-MmJson @($Plan.original))
  $candidateLive=$tree -ceq (ConvertTo-MmJson @($Plan.candidate))
  if(-not $originalLive -and -not $candidateLive){throw 'Altered live maintenance payload.'}
 }
 $beforePark=$originalLive -and -not $park -and $stage -and -not $withdrawn
 $parked=-not $live -and $park -and $stage -and -not $withdrawn
 $published=$candidateLive -and $park -and -not $stage -and -not $withdrawn
 $removed=-not $live -and $park -and -not $stage -and $withdrawn
 $restored=$originalLive -and -not $park -and ($stage -xor $withdrawn)
 $valid=switch -CaseSensitive ($Phase) {
  'prepared' {$beforePark}
  'parking-original' {$beforePark -or $parked}
  'original-parked' {$parked}
  'publishing-candidate' {$parked -or $published}
  'candidate-published' {$published}
  'refreshing-marker' {$published}
  'entered' {$published}
  'withdrawing-candidate' {$published -or $removed}
  'candidate-withdrawn' {$removed}
  'restoring-original' {$removed -or $parked -or $restored}
  'original-restored' {$restored}
  'restored' {$restored}
  default {$false}
 }
 if(-not $valid){throw 'Journal phase and exact directory layout disagree; manual recovery required.'}
}
function Set-MmPhase($Context,[string]$Phase) {
 [void](Assert-MmFacts $Context.plan $Context.getFacts)
 Write-MmAtomic (Join-Path $Context.plan.runRoot 'journal.json') ([ordered]@{schemaVersion=1;planSha256=$Context.hash;phase=$Phase;markerWriteTicks=$Context.markerTicks})
 Invoke-MmFault $Context $Phase
}
function Invoke-MmFault($Context,[string]$Point) {
 if($Context.interrupt -ceq $Point){throw ('SIMULATED-INTERRUPTION:'+ $Point)}
 if($Context.fault -ceq $Point){throw ('INJECTED-FAILURE:'+ $Point)}
}
function Move-MmDirectory($Context,[string]$From,[string]$To) {
 [void](Assert-MmFacts $Context.plan $Context.getFacts)
 Assert-MmPath $From; Assert-MmPath $To
 if(Test-Path -LiteralPath $To){throw 'Maintenance rename destination exists.'}
 [IO.Directory]::Move($From,$To)
 if($To -ceq (Join-Path $Context.plan.runRoot 'original')){Invoke-MmFault $Context 'after-original-rename'}
 elseif($From -ceq (Join-Path $Context.plan.runRoot 'stage')){Invoke-MmFault $Context 'after-candidate-rename'}
 elseif($To -ceq (Join-Path $Context.plan.runRoot 'withdrawn')){Invoke-MmFault $Context 'after-withdraw-rename'}
 elseif($From -ceq (Join-Path $Context.plan.runRoot 'original')){Invoke-MmFault $Context 'after-restore-rename'}
}
function Restore-MmCore($Context) {
 $p=$Context.plan
 [void](Assert-MmFacts $p $Context.getFacts); Assert-MmRecoveryInputs $p
 Assert-MmLiveMarker $p $Context.markerTicks
 $extension=$p.host.extension; $park=Join-Path $p.runRoot 'original'; $withdrawn=Join-Path $p.runRoot 'withdrawn'
 # No phase is guessed. The explicit restore operation validates every actual
 # directory against one complete recorded tree before touching any of them.
 if(Test-Path -LiteralPath $extension) {
  $tree=ConvertTo-MmJson (Get-MmTree $extension)
  if($tree -ceq (ConvertTo-MmJson @($p.original))) {
   if(Test-Path -LiteralPath $park){throw 'Ambiguous duplicate original; manual recovery required.'}
  } elseif($tree -ceq (ConvertTo-MmJson @($p.candidate))) {
   if(-not (Test-Path -LiteralPath $park) -or (Test-Path -LiteralPath $withdrawn)){throw 'Ambiguous candidate restoration layout.'}
   Set-MmPhase $Context 'withdrawing-candidate'
   Move-MmDirectory $Context $extension $withdrawn
   Set-MmPhase $Context 'candidate-withdrawn'
  } else {throw 'Unknown active payload; automated restore refused.'}
 }
 if(-not (Test-Path -LiteralPath $extension)) {
  if(-not (Test-Path -LiteralPath $park)){throw 'Original parking directory missing; manual recovery required.'}
  Set-MmPhase $Context 'restoring-original'
  Move-MmDirectory $Context $park $extension
  Set-MmPhase $Context 'original-restored'
 }
 Assert-MmTree $extension $p.original
 [void](Assert-MmFacts $p $Context.getFacts)
 # Marker bytes never change. Only the coordinator's recorded timestamp is
 # accepted; independent changes are never overwritten.
 [IO.File]::SetLastWriteTimeUtc($p.host.marker,[datetime]::new([long]$p.hostMarker.writeTicks,[DateTimeKind]::Utc))
 Invoke-MmFault $Context 'after-marker-restore'
 Assert-MmFileRecord $p.host.marker $p.hostMarker
 Set-MmPhase $Context 'restored'
}
function Assert-MmLiveMarker($Plan,[long]$MarkerTicks) {
 $current=Get-MmFileRecord $Plan.host.marker
 if(-not $current.present -or $current.sha256 -cne $Plan.hostMarker.sha256 -or $current.creationTicks -ne $Plan.hostMarker.creationTicks -or ($current.writeTicks -ne $Plan.hostMarker.writeTicks -and $current.writeTicks -ne $MarkerTicks)){throw 'Host marker changed outside recorded transaction.'}
}
function Read-MmJournal($Plan,[string]$ExpectedSha) {
 foreach($name in @('plan.json.writing','journal.json.writing')) {
  if(Test-Path -LiteralPath (Join-Path $Plan.runRoot $name)){throw 'Interrupted atomic metadata write; manual review required.'}
 }
 $j=Read-MmJson (Join-Path $Plan.runRoot 'journal.json') 65536
 Assert-MmKeys $j 'markerWriteTicks,phase,planSha256,schemaVersion'
 $phases=@('prepared','parking-original','original-parked','publishing-candidate','candidate-published','refreshing-marker','entered','withdrawing-candidate','candidate-withdrawn','restoring-original','original-restored','restored')
 if($j.schemaVersion -isnot [int] -or $j.schemaVersion -ne 1 -or $j.planSha256 -cne $ExpectedSha -or $j.phase -cnotin $phases -or ($j.markerWriteTicks -isnot [int] -and $j.markerWriteTicks -isnot [long]) -or $j.markerWriteTicks -lt 0 -or $j.markerWriteTicks -gt [datetime]::MaxValue.Ticks){throw 'Unknown interrupted maintenance journal; manual review required.'}
 Assert-MmLayout $Plan $j.phase
 Assert-MmLiveMarker $Plan ([long]$j.markerWriteTicks)
 return $j
}
function Invoke-MmTransaction([string]$Action,[string]$PlanPath,[string]$ExpectedSha,[scriptblock]$GetFacts,[string]$Fault,[string]$Interrupt) {
 $p=Get-MmPlan $PlanPath $ExpectedSha
 $facts=Assert-MmFacts $p $GetFacts; Assert-MmRunPaths $p $facts
 Assert-MmRecoveryInputs $p
 $null=Read-MmJournal $p $ExpectedSha
 $lockPath=Join-Path $p.runRoot 'maintenance.lock'
 if(Test-Path -LiteralPath $lockPath){
  [void](Get-MmHash $lockPath)
  if([IO.File]::ReadAllText($lockPath) -cne $ExpectedSha){throw 'Lock belongs to another reviewed plan.'}
 }
 # Kernel exclusivity, not PID/staleness heuristics. Persistent lock file is retained.
 [void](Assert-MmFacts $p $GetFacts)
 $hostLockPath=Join-Path $p.host.root '.twincat-assistant-deployment.lock'
 if(Test-Path -LiteralPath $hostLockPath){[void](Get-MmHash $hostLockPath)}
 # Same kernel lock as the production installer, with no content changes.
 $hostLock=[IO.File]::Open($hostLockPath,'OpenOrCreate','ReadWrite','None')
 $lock=$null
 try {
  $lock=[IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
  if($lock.Length) {
   $buffer=New-Object byte[] ([int]$lock.Length); [void]$lock.Read($buffer,0,$buffer.Length)
   if([Text.Encoding]::UTF8.GetString($buffer) -cne $ExpectedSha){throw 'Lock belongs to another reviewed plan.'}
  } else { $b=[Text.Encoding]::UTF8.GetBytes($ExpectedSha); $lock.Write($b,0,$b.Length); $lock.Flush($true) }
  [void](Assert-MmFacts $p $GetFacts); Assert-MmRecoveryInputs $p
  $journal=Read-MmJournal $p $ExpectedSha
  $context=@{plan=$p;hash=$ExpectedSha;getFacts=$GetFacts;fault=$Fault;interrupt=$Interrupt;markerTicks=[long]$journal.markerWriteTicks}
  if($Action -ceq 'Restore') { Restore-MmCore $context; return }
  if($journal.phase -cne 'prepared'){throw 'Pending/completed maintenance run: explicit restore required, never auto-enter.'}
  Assert-MmTree $p.host.extension $p.original
  Assert-MmFileRecord $p.host.marker $p.hostMarker
  Assert-MmTree (Join-Path $p.runRoot 'stage') $p.candidate
  try {
   Set-MmPhase $context 'parking-original'
   Move-MmDirectory $context $p.host.extension (Join-Path $p.runRoot 'original')
   Set-MmPhase $context 'original-parked'
   Set-MmPhase $context 'publishing-candidate'
   # A fully marked, hash-checked directory is the sole publication unit.
   Assert-MmTree (Join-Path $p.runRoot 'stage') $p.candidate
   Move-MmDirectory $context (Join-Path $p.runRoot 'stage') $p.host.extension
   Assert-MmTree $p.host.extension $p.candidate
   Set-MmPhase $context 'candidate-published'
   $context.markerTicks=[datetime]::UtcNow.Ticks
   Set-MmPhase $context 'refreshing-marker'
   [void](Assert-MmFacts $p $GetFacts)
   [IO.File]::SetLastWriteTimeUtc($p.host.marker,[datetime]::new($context.markerTicks,[DateTimeKind]::Utc))
   Invoke-MmFault $context 'after-marker-touch'
   Set-MmPhase $context 'entered'
  } catch {
   $failure=$_
   if($failure.Exception.Message.StartsWith('SIMULATED-INTERRUPTION:')){throw}
   # Recovery keeps the same SID/profile/process gates. If those no longer
   # pass, leave the journal intact for explicit quiescent/manual recovery.
   $context.fault=''; $context.interrupt=''
   try { Restore-MmCore $context } catch { throw ('Maintenance failed; automatic restore refused. Preserve plan/journal/backup for reviewed recovery. '+$_.Exception.Message) }
   throw $failure
  }
 } finally { if($lock){$lock.Dispose()}; $hostLock.Dispose() }
}
