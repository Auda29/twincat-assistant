[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Install','Rollback')][string]$Action,
    [Parameter(Mandatory)][string]$TestRoot,
    [string]$PackageDirectory
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PackageContract.ps1"
# This deliberately cannot operate on an existing installation: a fresh, explicitly
# initialized fixture root is required. It does not register an extension or start a host.
if (-not [IO.Path]::IsPathRooted($TestRoot)) { throw 'TestRoot must be absolute.' }
$root = (Resolve-Path -LiteralPath $TestRoot).Path
$marker = Join-Path $root '.twincat-installer-fixture'
if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -cne 'synthetic-only-v1') { throw 'Not an initialized synthetic fixture root.' }
if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Fixture root must not be a reparse point.' }
$versions = Join-Path $root 'versions'
$statePath = Join-Path $root 'state.json'
if (-not (Test-Path -LiteralPath $versions -PathType Container)) { throw 'Missing fixture versions directory.' }
if (@(Get-ChildItem -LiteralPath $versions -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -or ((Get-Item -LiteralPath $versions -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse points are forbidden.' }
$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { [pscustomobject]@{current=$null;previous=$null} }
if ($Action -eq 'Install') {
    $m = Test-ApplicationPackage $PackageDirectory
    $destination = Join-Path $versions $m.version
    if (Test-Path -LiteralPath $destination) { throw 'Version already exists; refusing overwrite.' }
    $temp = Join-Path $versions ('.staging-' + [guid]::NewGuid().ToString('N'))
    try {
        Copy-Item -LiteralPath $PackageDirectory -Destination $temp -Recurse
        $null = Test-ApplicationPackage $temp
        Move-Item -LiteralPath $temp -Destination $destination
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
    $next = @{ current=$m.version; previous=$state.current }
} else {
    if (-not $state.previous -or $state.previous -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'No valid previous version.' }
    $null = Test-ApplicationPackage (Join-Path $versions $state.previous)
    $next = @{ current=$state.previous; previous=$state.current }
}
# Write a same-directory replacement; versions and operator state are never deleted.
$tempState = Join-Path $root ('.state-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.File]::WriteAllText($tempState, ($next | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $statePath) { $backup = $tempState + '.bak'; try { [IO.File]::Replace($tempState, $statePath, $backup) } finally { if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force } } } else { [IO.File]::Move($tempState, $statePath) }
} finally { if (Test-Path -LiteralPath $tempState) { Remove-Item -LiteralPath $tempState -Force } }
