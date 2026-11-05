[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/installer/PackageContract.ps1"
$m = Test-ApplicationPackage $PackageDirectory
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
# No release artifact until host registration and redistribution are actually established.
throw "Package $($m.version) passed structural validation, but an installable release is blocked: host registration, prerequisites and redistribution approval are not integrated. See docs/INSTALLER_CONTRACT.md."
