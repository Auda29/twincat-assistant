param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
$builder = Join-Path $PSScriptRoot 'build-application-package.mjs'
& node $builder $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Application package build failed.' }
