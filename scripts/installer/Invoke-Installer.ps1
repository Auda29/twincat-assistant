[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Install','Rollback')][string]$Action,
      [Parameter(Mandatory)][string]$InstallRoot,
      [Parameter(Mandatory)][string]$HostExecutable,
      [Parameter(Mandatory)][string]$HostSha256,
      [Parameter(Mandatory)][string]$HostFileVersion,
      [Parameter(Mandatory)][string]$XaeFile,
      [Parameter(Mandatory)][string]$XaeFileVersion)
$ErrorActionPreference = 'Stop'
foreach ($p in @($InstallRoot,$HostExecutable,$XaeFile)) { if (-not [IO.Path]::IsPathRooted($p)) { throw 'All paths must be absolute.' } }
if ([IO.Path]::GetFileName($HostExecutable) -ine 'TcXaeShell.exe' -or $HostSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Explicit TcXaeShell executable and lowercase SHA256 required.' }
if (-not (Test-Path -LiteralPath $HostExecutable -PathType Leaf) -or (Get-FileHash -LiteralPath $HostExecutable -Algorithm SHA256).Hash.ToLowerInvariant() -cne $HostSha256 -or (Get-Item -LiteralPath $HostExecutable).VersionInfo.FileVersion -cne $HostFileVersion -or $HostFileVersion -notmatch '^15\.') { throw 'Host identity mismatch.' }
$stream = [IO.File]::OpenRead($HostExecutable)
try { $reader = New-Object IO.BinaryReader($stream); if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Invalid PE.' }; $stream.Position=0x3c; $offset=$reader.ReadInt32(); if ($offset -lt 64 -or $offset -gt $stream.Length-6) { throw 'Invalid PE offset.' }; $stream.Position=$offset; if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x14c) { throw 'Host must be x86.' } } finally { $stream.Dispose() }
if (-not (Test-Path -LiteralPath $XaeFile -PathType Leaf) -or [IO.Path]::GetFileName($XaeFile) -ine 'TwinCAT XAE Base.dll' -or (Get-Item -LiteralPath $XaeFile).VersionInfo.FileVersion -cne $XaeFileVersion) { throw 'XAE identity mismatch.' }
. "$PSScriptRoot/PackageContract.ps1"
$package = Test-ApplicationPackage (Join-Path $PSScriptRoot 'package')
$osArch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if (($package.architecture -eq 'x64' -and $osArch -ne 'AMD64') -or ($package.architecture -eq 'arm64' -and $osArch -ne 'ARM64')) { throw 'Package architecture does not match Windows.' }
foreach ($process in @(Get-Process -Name TcXaeShell -ErrorAction SilentlyContinue)) {
    if (-not $process.Path) { throw 'A running TcXaeShell path is unavailable; cannot verify host is closed.' }
    if ([string]::Equals([IO.Path]::GetFullPath($process.Path), [IO.Path]::GetFullPath($HostExecutable), [StringComparison]::OrdinalIgnoreCase)) { throw 'Close the selected TcXaeShell before changing the payload.' }
}
if (-not (Test-Path -LiteralPath $InstallRoot)) { New-Item -ItemType Directory -Path $InstallRoot | Out-Null }
if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot '.twincat-installer-fixture'))) {
    if (@(Get-ChildItem -LiteralPath $InstallRoot -Force).Count) { throw 'Installation root is not empty or owned.' }
    New-Item -ItemType Directory -Path (Join-Path $InstallRoot 'versions') | Out-Null
    Set-Content -LiteralPath (Join-Path $InstallRoot '.twincat-installer-fixture') -Value 'synthetic-only-v1'
}
& (Join-Path $PSScriptRoot 'Invoke-FixtureLifecycle.ps1') -Action $Action -TestRoot $InstallRoot -PackageDirectory (Join-Path $PSScriptRoot 'package')
Write-Host 'Versioned application files selected. Host extension registration and packaged startup are not yet integrated.'
