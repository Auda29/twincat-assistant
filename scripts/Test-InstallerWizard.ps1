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
# Package reduction leaves empty npm scope directories. Framework ZIP uses
# backslash-terminated entries on Windows; extraction must recognize these.
[IO.Directory]::CreateDirectory((Join-Path $fixture 'package/upstream/node_modules/@clack')) | Out-Null
@'
param([Parameter(Mandatory)][ValidateSet('Install','Rollback')][string]$Action,
      [Parameter(Mandatory)][string]$InstallRoot)
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'package/upstream/node_modules/@clack') -PathType Container)) { throw 'Missing extracted empty Windows directory' }
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'package/forward-empty') -PathType Container)) { throw 'Missing extracted empty ZIP directory' }
if ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'package/forward-parent/data.txt')) -cne 'forward') { throw 'Changed forward-slash file' }
if ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'package/back-parent/data.txt')) -cne 'back') { throw 'Changed backslash file' }
Write-Output ('FIXTURE-ACTION:' + $Action)
Write-Output ('FIXTURE-ROOT:' + $InstallRoot)
if ($InstallRoot -eq 'failure') { Write-Error 'Synthetic installer failure'; exit 7 }
exit 0
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Invoke-Installer.ps1') -Encoding UTF8
$zip = Join-Path $root 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($fixture, $zip)
$stream = [IO.File]::Open($zip, [IO.FileMode]::Open)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Update)
try {
    $null = $archive.CreateEntry('package/forward-empty/')
    foreach ($entry in @(@('package/forward-parent/data.txt','forward'), @('package\back-parent\data.txt','back'))) {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry[0]).Open())
        try { $writer.Write($entry[1]) } finally { $writer.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$exe = Join-Path $root 'test.exe'
$info = Join-Path $root 'setup-info.json'
[IO.File]::WriteAllText($info, '{"schemaVersion":1,"version":"0.2.0-beta.1"}', [Text.UTF8Encoding]::new($false))
$preview = Join-Path $root 'PreviewWizard.cs'
[IO.File]::WriteAllText($preview, 'class PreviewWizard : InstallerWizard { public PreviewWizard(string root) : base(root) {} protected override bool ShowWithoutActivation { get { return true; } } }')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$PSScriptRoot/installer/assets/setup.ico" "/resource:$PSScriptRoot/installer/assets/setup.ico,setup.ico" "/out:$exe" "/resource:$zip,payload.zip" "/resource:$info,setup-info.json" (Join-Path $PSScriptRoot 'installer/Bootstrap.cs') $preview
if ($LASTEXITCODE -ne 0) { throw 'Wizard fixture compilation failed.' }
$assembly = [Reflection.Assembly]::LoadFile($exe)
$iconStream = $assembly.GetManifestResourceStream('setup.ico')
try {
    $iconBytes = [IO.MemoryStream]::new()
    try {
        $iconStream.CopyTo($iconBytes)
        if ([Convert]::ToBase64String($iconBytes.ToArray()) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'installer/assets/setup.ico')))) { throw 'Embedded setup icon changed.' }
    } finally { $iconBytes.Dispose() }
} finally { $iconStream.Dispose() }
$run = $assembly.GetType('Bootstrap').GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
$report = [WizardOutput]::Report
function Invoke-Fixture([string[]]$Arguments) {
    [WizardOutput]::Lines.Clear()
    $parameters = New-Object 'object[]' 2
    $parameters[0] = $Arguments; $parameters[1] = $report
    return $run.Invoke($null, $parameters)
}
$quotedRoot = 'C:\Synthetic folder\quoted"value\'
if ((Invoke-Fixture @('-Action','Install','-InstallRoot',$quotedRoot)) -ne 0) { throw ('Install failed: ' + ([WizardOutput]::Lines -join ' / ')) }
if (-not [WizardOutput]::Lines.Contains('FIXTURE-ROOT:' + $quotedRoot)) { throw 'Argument quoting changed the target.' }
if ((Invoke-Fixture @('-Action','Rollback','-InstallRoot','C:\Synthetic')) -ne 0 -or -not [WizardOutput]::Lines.Contains('FIXTURE-ACTION:Rollback')) { throw 'Rollback forwarding failed.' }
if ((Invoke-Fixture @('-Action','Install','-InstallRoot','failure')) -ne 7) { throw 'Installer failure exit code was lost.' }
if (-not ([WizardOutput]::Lines -match 'Synthetic installer failure')) { throw 'Installer stderr was lost.' }
if ((Invoke-Fixture @('-Action','Install')) -eq 0) { throw 'Missing parameters must fail noninteractively.' }
$extract = $assembly.GetType('Bootstrap').GetMethod('ExtractPayload', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($name in @('../escape.txt', '..\escape.txt')) {
    $stream = [IO.MemoryStream]::new()
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try { $null = $archive.CreateEntry($name) } finally { $archive.Dispose() }
    $stream.Position = 0; $refused = $false
    $parameters = New-Object 'object[]' 2
    $parameters[0] = $stream; $parameters[1] = [string](Join-Path $root 'refusal')
    $reason = 'no exception'
    try { $extract.Invoke($null, $parameters) }
    catch { $reason = $_.Exception.GetBaseException().Message; $refused = $reason -eq 'Invalid archive path' }
    finally { $stream.Dispose() }
    if (-not $refused -or (Test-Path -LiteralPath (Join-Path $root 'escape.txt'))) { throw ('Archive traversal was not refused: ' + $reason) }
}
$default = $assembly.GetType('Bootstrap').GetMethod('DefaultInstallRoot', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())
if (-not $default.EndsWith('\TwinCatAssistant')) { throw 'Missing default destination.' }
$displayRoot = Join-Path $root 'display'
$wizardType = $assembly.GetType('InstallerWizard')
$form = [Activator]::CreateInstance($assembly.GetType('PreviewWizard'), [object[]]@([string](Join-Path $displayRoot 'new')))
$form.ShowInTaskbar = $false; $form.Opacity = 0.01
$form.Show(); [Windows.Forms.Application]::DoEvents()
try {
    if (-not $form.Icon) { throw 'Wizard product icon missing.' }
    $shellIcon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
    $shellBitmap = $shellIcon.ToBitmap()
    $sourceIcon = [Drawing.Icon]::new((Join-Path $PSScriptRoot 'installer/assets/setup.ico'), $shellBitmap.Size)
    $sourceBitmap = $sourceIcon.ToBitmap()
    $wizardBitmap = $form.Icon.ToBitmap()
    try {
        if ($wizardBitmap.Size -ne $sourceBitmap.Size) { throw 'Wizard icon resolution differs from shell icon.' }
        for ($y=0; $y -lt $shellBitmap.Height; $y++) {
            for ($x=0; $x -lt $shellBitmap.Width; $x++) {
                if ($shellBitmap.GetPixel($x,$y).ToArgb() -ne $sourceBitmap.GetPixel($x,$y).ToArgb() -or $wizardBitmap.GetPixel($x,$y).ToArgb() -ne $sourceBitmap.GetPixel($x,$y).ToArgb()) { throw ('EXE or wizard icon differs at {0},{1}: shell={2} wizard={3} source={4}' -f $x,$y,$shellBitmap.GetPixel($x,$y),$wizardBitmap.GetPixel($x,$y),$sourceBitmap.GetPixel($x,$y)) }
            }
        }
    } finally { $shellBitmap.Dispose(); $shellIcon.Dispose(); $sourceBitmap.Dispose(); $sourceIcon.Dispose(); $wizardBitmap.Dispose() }
    Write-Host 'PASS: embedded product icon, actual Explorer EXE icon and wizard icon match.'
    $fields = [Reflection.BindingFlags]'Instance,NonPublic'
    $path = $wizardType.GetField('destination',$fields).GetValue($form)
    $actions = $wizardType.GetField('action',$fields).GetValue($form)
    if ($actions.Items.Count -ne 2 -or $actions.SelectedIndex -ne 0) { throw 'Unexpected wizard actions.' }
    $packageVersion = $wizardType.GetField('packageVersion',$fields).GetValue($form)
    $installedVersion = $wizardType.GetField('installedVersion',$fields).GetValue($form)
    $operation = $wizardType.GetField('operation',$fields).GetValue($form)
    $install = $wizardType.GetField('install',$fields).GetValue($form)
    if ($packageVersion.Text -notmatch '0\.2\.0-beta\.1' -or $operation.Text -notmatch 'Neuinstallation' -or -not $install.Enabled) { throw 'Fresh installation overview is wrong.' }
    [IO.Directory]::CreateDirectory($displayRoot) | Out-Null
    function Save-Wizard([string]$name) {
        $bitmap = [Drawing.Bitmap]::new($form.Width,$form.Height)
        try { $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,$form.Width,$form.Height)); $bitmap.Save((Join-Path $displayRoot ($name+'.png'))) }
        finally { $bitmap.Dispose() }
    }
    function Managed-Root([string]$name,[string]$current,[string]$previous) {
        $directory = Join-Path $displayRoot $name
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        [IO.File]::WriteAllText((Join-Path $directory '.assistant-install-root'),'TwinCatAssistant installation v1')
        [IO.File]::WriteAllText((Join-Path $directory 'state.json'),(@{current=$current;previous=$previous}|ConvertTo-Json))
        return $directory
    }
    Save-Wizard 'new'
    $update = Managed-Root 'update' '0.2.0-preview.6' '0.2.0-preview.5'
    $before = (Get-FileHash -LiteralPath (Join-Path $update 'state.json')).Hash
    $path.Text = $update
    if ($installedVersion.Text -notmatch '0\.2\.0-preview\.6' -or $operation.Text -cne 'Update: 0.2.0-preview.6 -> 0.2.0-beta.1' -or $install.Text -ne 'Aktualisieren' -or -not $install.Enabled) { throw 'Update overview is wrong.' }
    Save-Wizard 'update'
    $actions.SelectedIndex = 1
    if ($operation.Text -cne 'Wiederherstellung: 0.2.0-preview.6 -> 0.2.0-preview.5' -or -not $install.Enabled) { throw 'Rollback target is wrong.' }
    Save-Wizard 'rollback'
    if ((Get-FileHash -LiteralPath (Join-Path $update 'state.json')).Hash -cne $before) { throw 'Overview modified installation state.' }
    $actions.SelectedIndex = 0
    $same = Managed-Root 'same' '0.2.0-beta.1' '0.2.0-preview.6'
    $path.Text = $same
    if ($install.Enabled -or $operation.Text -notmatch 'bereits installiert') { throw 'Same-version install was offered.' }
    [IO.File]::WriteAllText((Join-Path $same 'transaction.pending'),'synthetic pending transaction')
    $wizardType.GetMethod('RefreshSelection',$fields).Invoke($form,@())
    if ($install.Enabled -or $operation.Text -notmatch 'unterbrochene Installation') { throw 'Pending transaction was not shown.' }
    $corrupt = Managed-Root 'corrupt' '0.2.0-preview.6' '0.2.0-preview.5'
    [IO.File]::WriteAllText((Join-Path $corrupt 'state.json'),'{invalid')
    $path.Text = $corrupt
    if ($install.Enabled -or $installedVersion.Text -notmatch 'nicht sicher') { throw 'Unknown status was presented as a new install.' }
    $unowned = Join-Path $displayRoot 'unowned'
    [IO.Directory]::CreateDirectory($unowned) | Out-Null
    [IO.File]::WriteAllText((Join-Path $unowned 'keep.txt'),'untouched')
    $path.Text = $unowned
    if ($install.Enabled -or [IO.File]::ReadAllText((Join-Path $unowned 'keep.txt')) -cne 'untouched') { throw 'Unowned folder was adopted or changed.' }
    $retained = Managed-Root 'retained' '0.2.0-preview.6' '0.2.0-beta.1'
    [IO.Directory]::CreateDirectory((Join-Path $retained 'versions/0.2.0-beta.1')) | Out-Null
    $path.Text = $retained
    if ($install.Enabled -or $operation.Text -notmatch 'bereits vorhanden') { throw 'Retained version was offered as a new install.' }
    Write-Host 'PASS: setup/current/rollback versions, fresh install/update detection, same/retained version refusal, pending/invalid/unowned status refusal, read-only overview.'
    Write-Host "Wizard screenshots: $displayRoot"
} finally { $form.Dispose() }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer/Bootstrap.manifest') -Raw
if ($manifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -cne 'requireAdministrator') { throw 'Missing UAC requirement.' }
Write-Host 'PASS: actual extraction of empty directories and files with both ZIP separators; traversal refusal with both separators; quoted arguments, Install/Rollback, error output/exit code, missing-argument refusal, wizard defaults, UAC manifest. No real installation invoked.'
Write-Host "Synthetic fixture retained: $root"
