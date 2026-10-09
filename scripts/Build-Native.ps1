param(
    [Parameter(Mandatory = $true)][string]$DotNetRoot,
    [ValidateSet('Both', 'X86', 'Amd64')][string]$Architecture = 'Both',
    [string]$TwinCatInteropPath = 'C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TCatSysManagerLib.dll'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Select an installed .NET SDK root.' }
Push-Location $root
$keys = @('DOTNET_ROOT', 'MSBuildSDKsPath', 'MSBuildEnableWorkloadResolver', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')
$old = @{}
foreach ($key in $keys) { $old[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
try {
    $env:DOTNET_ROOT = [IO.Path]::GetFullPath($DotNetRoot)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $version = (& $dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -ne '8.0.425') { throw 'This native prototype requires the pinned SDK 8.0.425.' }
    $env:MSBuildSDKsPath = Join-Path $DotNetRoot "sdk\$version\Sdks"
    $env:MSBuildEnableWorkloadResolver = 'false'
    & $dotnet build native/WindowsHost/WindowsHost.csproj --configuration Release /p:RestoreLockedMode=true --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows helper build failed.' }
    & $dotnet build native/ReadOnlyProbe/ReadOnlyProbe.csproj --configuration Release /p:RestoreLockedMode=true --nologo "/p:TwinCatInteropPath=$TwinCatInteropPath"
    if ($LASTEXITCODE -ne 0) { throw 'Read-only probe build failed.' }
    & $dotnet restore native/Tests/Native.Tests.csproj --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Native policy restore failed.' }
    & $dotnet run --project native/Tests/Native.Tests.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Native policy regression failed.' }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Visual Studio 2022 MSBuild (x64) is required for VSIX packaging.' }
    $hosts = if ($Architecture -eq 'Both') { @('X86', 'Amd64') } else { @($Architecture) }
    foreach ($hostArchitecture in $hosts) {
        & $msbuild "native/IdeHost/$hostArchitecture/IdeHost.$hostArchitecture.csproj" /restore /p:RestoreLockedMode=true /p:Configuration=Release /v:minimal /nologo "/p:TwinCatInteropPath=$TwinCatInteropPath"
        if ($LASTEXITCODE -ne 0) { throw "$hostArchitecture host build failed." }
    }
} finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $old[$key], 'Process') }
    Pop-Location
}
