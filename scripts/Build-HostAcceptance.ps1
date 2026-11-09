param(
    [string]$DotNetRoot = 'C:\Program Files\dotnet',
    [string]$SdkVersion = '10.0.303'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($SdkVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'An exact installed SDK version is required.' }
$buildRoot = Join-Path $root ('local-data/host-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildRoot | Out-Null
@{ sdk = @{ version = $SdkVersion; rollForward = 'disable' } } | ConvertTo-Json | Set-Content (Join-Path $buildRoot 'global.json') -Encoding UTF8
# SDK selection is local to this standalone acceptance runner. Do not edit global.json.
Push-Location $buildRoot
try {
    & (Join-Path $DotNetRoot 'dotnet.exe') build (Join-Path $root 'native/HostAcceptance/HostAcceptance.csproj') -c Release --nologo /p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Host acceptance runner build failed.' }
} finally { Pop-Location }
