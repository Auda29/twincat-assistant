param()
$ErrorActionPreference = 'Stop'
# Synthetic embedded script only. No UAC, host discovery, installer or operator data.
Add-Type -AssemblyName System.IO.Compression.FileSystem,System.Windows.Forms
Add-Type @'
using System.Collections.Generic;
public static class WizardOutput {
 public static readonly List<string> Lines = new List<string>();
 public static readonly System.Action<string> Report = Capture;
 public static void Capture(string line) { lock (Lines) { Lines.Add(line); } }
}
'@
$root = Join-Path ([IO.Path]::GetTempPath()) ('tc-wizard-test-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $root 'fixture'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
@'
param([Parameter(Mandatory)][ValidateSet('Install','Rollback')][string]$Action,
      [Parameter(Mandatory)][string]$InstallRoot)
Write-Output ('FIXTURE-ACTION:' + $Action)
Write-Output ('FIXTURE-ROOT:' + $InstallRoot)
if ($InstallRoot -eq 'failure') { Write-Error 'Synthetic installer failure'; exit 7 }
exit 0
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Invoke-Installer.ps1') -Encoding UTF8
$zip = Join-Path $root 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($fixture, $zip)
$exe = Join-Path $root 'test.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$exe" "/resource:$zip,payload.zip" (Join-Path $PSScriptRoot 'installer/Bootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw 'Wizard fixture compilation failed.' }
$assembly = [Reflection.Assembly]::LoadFile($exe)
$run = $assembly.GetType('Bootstrap').GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
$report = [WizardOutput]::Report
function Invoke-Fixture([string[]]$Arguments) {
    [WizardOutput]::Lines.Clear()
    $parameters = New-Object 'object[]' 2
    $parameters[0] = $Arguments; $parameters[1] = $report
    return $run.Invoke($null, $parameters)
}
$quotedRoot = 'C:\Synthetic folder\quoted"value\'
if ((Invoke-Fixture @('-Action','Install','-InstallRoot',$quotedRoot)) -ne 0) { throw 'Install failed.' }
if (-not [WizardOutput]::Lines.Contains('FIXTURE-ROOT:' + $quotedRoot)) { throw 'Argument quoting changed the target.' }
if ((Invoke-Fixture @('-Action','Rollback','-InstallRoot','C:\Synthetic')) -ne 0 -or -not [WizardOutput]::Lines.Contains('FIXTURE-ACTION:Rollback')) { throw 'Rollback forwarding failed.' }
if ((Invoke-Fixture @('-Action','Install','-InstallRoot','failure')) -ne 7) { throw 'Installer failure exit code was lost.' }
if (-not ([WizardOutput]::Lines -match 'Synthetic installer failure')) { throw 'Installer stderr was lost.' }
if ((Invoke-Fixture @('-Action','Install')) -eq 0) { throw 'Missing parameters must fail noninteractively.' }
$form = [Activator]::CreateInstance($assembly.GetType('InstallerWizard'))
try {
    $fields = [Reflection.BindingFlags]'Instance,NonPublic'
    $path = $form.GetType().GetField('destination',$fields).GetValue($form)
    if (-not $path.Text.EndsWith('\TwinCatAssistant')) { throw 'Missing default destination.' }
    $actions = $form.GetType().GetField('action',$fields).GetValue($form)
    if ($actions.Items.Count -ne 2 -or $actions.SelectedIndex -ne 0) { throw 'Unexpected wizard actions.' }
} finally { $form.Dispose() }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer/Bootstrap.manifest') -Raw
if ($manifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -cne 'requireAdministrator') { throw 'Missing UAC requirement.' }
Write-Host 'PASS: quoted arguments, Install/Rollback, error output/exit code, missing-argument refusal, wizard defaults, UAC manifest. No real installation invoked.'
Write-Host "Synthetic fixture retained: $root"
