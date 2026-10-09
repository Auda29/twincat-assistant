param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This inventory requires Windows.' }

function Get-PeArchitecture([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $stream.Position = $reader.ReadInt32() + 4
        switch ($reader.ReadUInt16()) { 0x014c { 'x86' }; 0x8664 { 'amd64' }; default { 'unknown' } }
    } finally { $stream.Dispose() }
}
function Get-Version([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $item = Get-Item -LiteralPath $Path
        [pscustomobject]@{ path = $item.FullName; fileVersion = $item.VersionInfo.FileVersion; architecture = Get-PeArchitecture $item.FullName }
    }
}
$shells = @(
    "${env:ProgramFiles(x86)}\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe",
    "$env:ProgramFiles\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe",
    "$env:ProgramFiles\Beckhoff\TcXaeShell64\Common7\IDE\TcXaeShell.exe"
) | Select-Object -Unique | ForEach-Object { Get-Version $_ }
$webview = foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
    try {
        $key = $base.OpenSubKey('SOFTWARE\Microsoft\EdgeUpdate\Clients')
        if ($key) {
            try { foreach ($name in $key.GetSubKeyNames()) { $client = $key.OpenSubKey($name); try {
                if ($client.GetValue('name') -match 'WebView2') { [pscustomobject]@{ version = $client.GetValue('pv'); registryView = "$view" } }
            } finally { $client.Dispose() } } } finally { $key.Dispose() }
        }
    } finally { $base.Dispose() }
}
$registered = foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, $view)
    try { foreach ($id in @('TcXaeShell.DTE.15.0', 'TcXaeShell.DTE.17.0', 'VisualStudio.DTE.16.0', 'VisualStudio.DTE.17.0')) {
        $key = $base.OpenSubKey("$id\CLSID")
        if ($key) { try { [pscustomobject]@{ progId = $id; clsid = $key.GetValue(''); registryView = "$view" } } finally { $key.Dispose() } }
    } } finally { $base.Dispose() }
}
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$visualStudio = if (Test-Path -LiteralPath $vswhere) { & $vswhere -all -products '*' -format json | ConvertFrom-Json | Select-Object displayName, installationVersion, installationPath }
$inventory = [ordered]@{
    recordedAt = [DateTimeOffset]::UtcNow.ToString('O')
    osVersion = [Environment]::OSVersion.VersionString
    elevated = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    shells = @($shells)
    twinCatXae = Get-Version 'C:\TwinCAT\3.1\Components\Base\TwinCAT XAE Base.dll'
    automationTypeLibrary = Test-Path -LiteralPath 'C:\TwinCAT\3.1\Components\Base\TCatSysManager.tlb'
    webView2 = @($webview)
    registeredIdeObjects = @($registered)
    visualStudio = @($visualStudio)
    dotnetSdks = @(& dotnet --list-sdks)
    runningHosts = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('TcXaeShell', 'devenv') } | Select-Object ProcessName, Id, Path)
}
$json = $inventory | ConvertTo-Json -Depth 6
if ($OutputPath) {
    $fullPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    [IO.File]::WriteAllText($fullPath, $json, [Text.UTF8Encoding]::new($false))
}
$json
