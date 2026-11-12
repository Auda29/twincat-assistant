param(
  [ValidateSet('All','Engineering','Knowledge','KnowledgeProbe','Licenses')][string]$Component = 'All',
  [string]$EngineeringCheckout = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data\upstream\engineering'),
  [string]$EngineeringArtifacts,
  [string]$KnowledgeArtifacts = (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant\knowledge-runtime\0.1.1\packages'),
  [string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\application-inputs'),
  [string]$KnowledgeLicense,
  [string]$KnowledgeLicenseSha256
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'build-application-inputs.mjs'), '--component', $Component, '--engineering', $EngineeringCheckout, '--knowledge-artifacts', $KnowledgeArtifacts, '--output-root', $OutputRoot)
if ($EngineeringArtifacts) { $arguments += @('--engineering-artifacts', $EngineeringArtifacts) }
if ($KnowledgeLicense) { $arguments += @('--knowledge-license', $KnowledgeLicense, '--knowledge-license-sha256', $KnowledgeLicenseSha256) }
& node @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application input build failed; inspect the specific error above.' }
