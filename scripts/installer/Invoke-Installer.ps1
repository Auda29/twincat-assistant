[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Install','Rollback')][string]$Action,
      [Parameter(Mandatory)][string]$InstallRoot,
      [string]$HostId)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/HostTransaction.ps1"
. "$PSScriptRoot/RuntimePrerequisites.ps1"
$inventoryScript = Join-Path $PSScriptRoot 'Get-AssistantHostInventory.ps1'
$targetsPath = Join-Path $PSScriptRoot 'host-targets.json'
# This command always collects local live facts. No supplied-inventory option is exposed.
$json = & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -File $inventoryScript -TargetsPath $targetsPath -RequireAlphaTarget
if ($LASTEXITCODE -ne 0) { throw 'No eligible alpha host found by live inventory.' }
$inventory = ($json | Out-String) | ConvertFrom-Json
if ($inventory.collection.mode -cne 'live-read-only') { throw 'Live host inventory required.' }
$eligible = @($inventory.evaluation.hosts | Where-Object { $_.installerDecision -eq 'eligible' -and $_.targetId -eq 'tcxaeshell-x86-tc4024' })
if ($HostId) { $eligible = @($eligible | Where-Object { $_.hostId -ceq $HostId }) }
if ($eligible.Count -ne 1) { throw 'Select exactly one eligible host with -HostId; see Get-AssistantHostInventory.ps1 output.' }
$hostFacts = @($inventory.facts.hosts | Where-Object { $_.id -ceq $eligible[0].hostId })
if ($hostFacts.Count -ne 1 -or @($hostFacts[0].running).Count) { throw 'Close the selected TcXaeShell and retry; the installer will not close it.' }
if ($hostFacts[0].id -cne 'tcxaeshell:TcXaeShell\15.0' -or -not $hostFacts[0].dte.registered -or -not $hostFacts[0].dte.matchesExecutable) { throw 'Host must be registry-discovered with a matching DTE registration.' }
if ($inventory.facts.twinCat.version -cne '3.1.4024.78') { throw 'Unsupported TwinCAT build.' }
$hostRoot = [Environment]::ExpandEnvironmentVariables([string]$hostFacts[0].installRoot)
$executable = [Environment]::ExpandEnvironmentVariables([string]$hostFacts[0].executable.path)
$interop = [Environment]::ExpandEnvironmentVariables([string]$inventory.facts.twinCat.automationInterop.path)
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry32)
try {
    $key = $registry.OpenSubKey('SOFTWARE\Beckhoff\TwinCAT3\3.1', $false)
    if (-not $key) { throw 'TwinCAT installation root is not registered.' }
    try { $twincatRoot = [string]$key.GetValue('InstallDir') } finally { $key.Dispose() }
} finally { $registry.Dispose() }
if (-not $twincatRoot -or -not [IO.Path]::IsPathRooted($twincatRoot)) { throw 'TwinCAT installation root is not registered.' }
$expectedInterop = Join-Path $twincatRoot 'Components\TcBlockDiagram\Common\TCatSysManagerLib.dll'
if (-not [string]::Equals([IO.Path]::GetFullPath($interop),[IO.Path]::GetFullPath($expectedInterop),[StringComparison]::OrdinalIgnoreCase)) { throw 'Interop is not from the registered TwinCAT installation.' }
if (-not [string]::Equals([IO.Path]::GetFullPath($executable),[IO.Path]::GetFullPath((Join-Path $hostRoot 'Common7\IDE\TcXaeShell.exe')),[StringComparison]::OrdinalIgnoreCase)) { throw 'Host executable is not in the inventoried installation.' }
foreach ($process in @(Get-Process -Name TcXaeShell -ErrorAction SilentlyContinue)) {
    if (-not $process.Path -or [string]::Equals($process.Path,$executable,[StringComparison]::OrdinalIgnoreCase)) { throw 'Close the selected TcXaeShell and retry; the installer will not close it.' }
}
$target = @((Get-Content $targetsPath -Raw | ConvertFrom-Json).targets | Where-Object { $_.id -ceq 'tcxaeshell-x86-tc4024' })
if ($target.Count -ne 1) { throw 'Host target is missing or ambiguous.' }
$expectedExtension = Join-Path $hostRoot $target[0].payload.installDirectory
foreach ($payload in @($hostFacts[0].assistantPayloads)) {
    $directory = [Environment]::ExpandEnvironmentVariables([string]$payload.directory)
    if (-not [string]::Equals($directory,$expectedExtension,[StringComparison]::OrdinalIgnoreCase)) { throw 'Another Assistant extension is already present; review it before installing.' }
}
$package = Join-Path $PSScriptRoot 'package'
if ($Action -eq 'Install') {
    $m = Test-ApplicationPackage $package
    $osArch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    if (($m.architecture -eq 'x64' -and $osArch -ne 'AMD64') -or ($m.architecture -eq 'arm64' -and $osArch -ne 'ARM64')) { throw 'Application architecture does not match Windows.' }
}
$runtimePackage = $package
if ($Action -eq 'Rollback') {
    $current = Get-Content -LiteralPath (Join-Path $InstallRoot 'state.json') -Raw | ConvertFrom-Json
    if (-not $current.previous -or $current.previous -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$' -or $current.previous.EndsWith('.')) { throw 'No valid previous version.' }
    $runtimePackage = Join-Path $InstallRoot "versions/$($current.previous)/application"
    $null = Test-ApplicationPackage $runtimePackage
}
$programs = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
Assert-ApplicationRuntimePrerequisites $runtimePackage (Join-Path $programs 'dotnet')
# Read only this same Windows user's native configuration. Executable paths,
# credentials and MCP commands from it are never passed to the helper.
$policyStateRoot = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/operator-state'
if ($Action -eq 'Rollback') {
    $nativeConfig = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/native-chat.json'
    if (Test-Path -LiteralPath $nativeConfig) {
        Assert-NoLinks $nativeConfig
        $nativeFile = Get-Item -LiteralPath $nativeConfig
        if ($nativeFile.Length -gt 65536) { throw 'Native configuration too large.' }
        $native = Get-Content -LiteralPath $nativeConfig -Raw | ConvertFrom-Json
        if (-not $native.StateRoot -or -not [IO.Path]::IsPathRooted([string]$native.StateRoot)) { throw 'Native StateRoot must be absolute.' }
        $policyStateRoot = [IO.Path]::GetFullPath([string]$native.StateRoot)
    }
}
Invoke-HostTransaction -PolicyStateRoot $policyStateRoot -Action $Action -InstallRoot $InstallRoot -HostRoot $hostRoot -Interop $interop -Package $package -Target $target[0]
Write-Host 'Host files registered with the selected packaged runtime. Real-host acceptance remains separate.'
