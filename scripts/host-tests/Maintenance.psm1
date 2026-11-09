# Reviewable preparation surface. No command runs merely by importing this module.
. "$PSScriptRoot/Maintenance.Core.ps1"
function Get-MmLiveSearchRoots($Inventory,[string]$LocalAppData) {
 $roots=New-Object Collections.Generic.List[string]
 foreach($hostFacts in @($Inventory.facts.hosts)) {
  $ide=Join-Path ([Environment]::ExpandEnvironmentVariables([string]$hostFacts.installRoot)) 'Common7\IDE'
  foreach($name in @('Extensions','CommonExtensions','ShellExtensions')){$roots.Add((Join-Path $ide $name))}
 }
 # Inspect only initialization search settings, never application settings,
 # MRU/project/chat/credential keys. No registry key is created or loaded.
 foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)) {
  foreach($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
   $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
   try {
    $shell=$base.OpenSubKey('Software\Beckhoff\TcXaeShell',$false)
    if($shell){try {
     foreach($name in @($shell.GetSubKeyNames() | Where-Object {$_ -match '^15\.0(?:_|$)'})) {
      $initialization=$shell.OpenSubKey($name+'\Initialization',$false)
      if($initialization){try {
       foreach($valueName in @('PkgDefSearchPath','ApplicationExtensionsFolder','ImageManifestSearchPath')) {
        $value=$initialization.GetValue($valueName,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if($null -ne $value) {
         if($value -isnot [string]){throw 'Unknown registry extension-search setting.'}
         foreach($part in $value.Split(';')) {
          if($part.Trim()){
           $path=[Environment]::ExpandEnvironmentVariables($part.Trim())
           if($path -match '\$|%'){throw 'Unresolved extension-search macro; explicit review required.'}
           Assert-MmPath $path; $roots.Add($path.TrimEnd('\'))
          }
         }
        }
       }
      }finally{$initialization.Dispose()}}
     }
    }finally{$shell.Dispose()}}
   }finally{$base.Dispose()}
  }
 }
 $privateBase=Join-Path $LocalAppData 'Beckhoff\TcXaeShell'
 if(Test-Path -LiteralPath $privateBase){
  Assert-MmPath $privateBase
  foreach($directory in @(Get-ChildItem -LiteralPath $privateBase -Directory -Force)) {
   Assert-MmPath $directory.FullName; $roots.Add((Join-Path $directory.FullName 'Extensions'))
  }
 }
 return @($roots.ToArray() | Sort-Object -Unique)
}
function Get-MmLiveFacts {
 if(-not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Platform -ne 'Win32NT'){throw 'Real maintenance requires 64-bit Windows PowerShell.'}
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $local=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
 $programs=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
 $inventoryScript=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Get-AssistantHostInventory.ps1'))
 $ps=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'System32\WindowsPowerShell\v1.0\powershell.exe'
 # No InventoryPath/TargetsPath or supplied facts can reach this public path.
 $json=& $ps -NoProfile -NonInteractive -File $inventoryScript -RequireAlphaTarget
 if($LASTEXITCODE){throw 'Read-only live alpha inventory refused maintenance.'}
 $inventory=($json | Out-String) | ConvertFrom-Json
 if($inventory.collection.mode -cne 'live-read-only' -or $inventory.facts.twinCat.version -cne '3.1.4024.78'){throw 'Exact live 4024.78 inventory required.'}
 $eligible=@($inventory.evaluation.hosts | Where-Object {$_.installerDecision -ceq 'eligible' -and $_.targetId -ceq 'tcxaeshell-x86-tc4024'})
 if($eligible.Count -ne 1 -or $eligible[0].hostId -cne 'tcxaeshell:TcXaeShell\15.0'){throw 'Exactly the registered x86 Shell target is required.'}
 $hostFacts=@($inventory.facts.hosts | Where-Object id -CEQ $eligible[0].hostId)
 if($hostFacts.Count -ne 1 -or -not $hostFacts[0].dte.registered -or -not $hostFacts[0].dte.matchesExecutable){throw 'Unambiguous matching host/DTE registration required.'}
 $root=[Environment]::ExpandEnvironmentVariables([string]$hostFacts[0].installRoot)
 $extension=Join-Path $root 'Common7\IDE\Extensions\TwinCatAssistant.Native.X86'
 $expectedExe=Join-Path $root 'Common7\IDE\TcXaeShell.exe'
 if([Environment]::ExpandEnvironmentVariables([string]$hostFacts[0].executable.path) -ine $expectedExe){throw 'Live executable/root mismatch.'}
 foreach($payload in @($hostFacts[0].assistantPayloads)) {
  if([Environment]::ExpandEnvironmentVariables([string]$payload.directory) -ine $extension){throw 'Another Assistant extension is discoverable; manual review required.'}
 }
 # Conservative: any generic relevant runtime is refused, even when it might
 # belong to unrelated work. Never infer quiescence from a missing command line.
 $processes=@(Get-CimInstance Win32_Process -ErrorAction Stop)
 $names=@('TcXaeShell.exe','TcXaeShell64.exe','devenv.exe','node.exe','opencode.exe','WindowsHost.exe','twincat-knowledge.exe','dotnet.exe')
 $active=$false; $unknown=$false
 foreach($process in $processes) {
  if($process.Name -in $names) {
   if(-not $process.ExecutablePath -or -not $process.CommandLine){$unknown=$true}else{$active=$true}
  }
 }
 $searchRoots=@(Get-MmLiveSearchRoots $inventory $local)
 return [pscustomobject]@{mode='live';operatorSid=$sid;localAppData=$local;systemDotNetRoot=(Join-Path $programs 'dotnet');hostRoot=$root;extension=$extension;marker=(Join-Path $root 'Common7\IDE\Extensions\extensions.configurationchanged');globalConfiguration=(Join-Path $local 'TwinCatAssistant\native-chat.json');searchRoots=$searchRoots;activeProcesses=$active;unknownProcesses=$unknown;webViewOverride=([bool]($env:WEBVIEW2_USER_DATA_FOLDER -or $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS))}
}
function Assert-MmPublicPlan([string]$PlanPath,[string]$ExpectedPlanSha256) {
 $p=Get-MmPlan $PlanPath $ExpectedPlanSha256
 if($p.mode -cne 'live' -or $p.source.guardDllSha256 -cne $script:MmGuardDll -or $p.source.manifestSha256 -cne $script:MmGuardManifest){throw 'Public maintenance refuses synthetic/unreviewed plans.'}
 return $p
}
function New-AssistantMaintenancePlan {
 [CmdletBinding()]
 param([Parameter(Mandatory)][string]$RunId,[Parameter(Mandatory)][string]$ConfigurationPath,[Parameter(Mandatory)][string]$PackageRoot,
       [Parameter(Mandatory)][ValidateSet('PREPARE_ONLY_NO_HOST_WRITE')][string]$Acknowledgement)
 # This still reads real facts. Tests invoke the internal engine instead; there
 # is no public synthetic switch or host/process/SID replacement parameter.
 New-MmPlan ([pscustomobject]@{runId=$RunId;configurationPath=$ConfigurationPath;packageRoot=$PackageRoot}) {Get-MmLiveFacts} $script:MmGuardDll $script:MmGuardManifest
}
function Enter-AssistantMaintenance {
 [CmdletBinding()]
 param([Parameter(Mandatory)][string]$PlanPath,[Parameter(Mandatory)][string]$ExpectedPlanSha256,
       [Parameter(Mandatory)][ValidateSet('I_ACCEPT_TEMPORARY_GLOBAL_MAINTENANCE_AND_RESTORE')][string]$OperatorOptIn)
 $null=Assert-MmPublicPlan $PlanPath $ExpectedPlanSha256
 Invoke-MmTransaction 'Enter' $PlanPath $ExpectedPlanSha256 {Get-MmLiveFacts}
}
function Restore-AssistantMaintenance {
 [CmdletBinding()]
 param([Parameter(Mandatory)][string]$PlanPath,[Parameter(Mandatory)][string]$ExpectedPlanSha256,
       [Parameter(Mandatory)][ValidateSet('I_ACCEPT_TEMPORARY_GLOBAL_MAINTENANCE_AND_RESTORE')][string]$OperatorOptIn)
 $null=Assert-MmPublicPlan $PlanPath $ExpectedPlanSha256
 # Explicit recovery; same live inventory/SID/profile/process gates as entry.
 # Never starts a backend or loads candidate code to restore the original.
 Invoke-MmTransaction 'Restore' $PlanPath $ExpectedPlanSha256 {Get-MmLiveFacts}
}
Export-ModuleMember -Function New-AssistantMaintenancePlan,Enter-AssistantMaintenance,Restore-AssistantMaintenance
