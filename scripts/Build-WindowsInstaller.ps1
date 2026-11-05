[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/installer/HostTransaction.ps1"
$m = Test-ApplicationPackage $PackageDirectory
$null = Assert-HostPayload $PackageDirectory
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is required on the build machine.' }
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$output = Join-Path $OutputDirectory "TwinCAT-Assistant-$($m.version)-$($m.architecture)-setup.exe"
if (Test-Path -LiteralPath $output) { throw 'Refusing to overwrite installer.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('tc-installer-build-' + [guid]::NewGuid().ToString('N'))
$zip = $null
try {
 New-Item -ItemType Directory -Path $temp | Out-Null
 Copy-Item -LiteralPath $PackageDirectory -Destination (Join-Path $temp 'package') -Recurse
 $null = Test-ApplicationPackage (Join-Path $temp 'package')
 foreach ($name in @('Invoke-Installer.ps1','HostTransaction.ps1','PackageContract.ps1','RuntimePrerequisites.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "installer/$name") -Destination $temp }
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Get-AssistantHostInventory.ps1') -Destination $temp
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../native/host-targets.json') -Destination $temp
 $zip = Join-Path ([IO.Path]::GetTempPath()) ('tc-installer-' + [guid]::NewGuid().ToString('N') + '.zip')
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($temp,$zip)
 & $compiler /nologo /target:exe "/out:$output" "/resource:$zip,payload.zip" /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'installer/Bootstrap.cs')
 if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw 'Installer compilation failed.' }
 Get-Item -LiteralPath $output
} finally { if ($zip -and (Test-Path -LiteralPath $zip)) { Remove-Item -LiteralPath $zip -Force }; if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
