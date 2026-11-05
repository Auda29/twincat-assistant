param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Launch', 'Observe')][string]$Mode,
    [Parameter(Mandatory = $true)][string]$PreparedRun,
    [string]$CapturePath,
    [datetime]$OperatorLogonUtc,
    [switch]$OperatorAttestsFirstShellSinceLogon,
    [switch]$OperatorConfirmsRestoredEditorsPrepared,
    [datetime]$PreparingEndedUtc,
    [ValidateSet('yes', 'no', 'unknown')][string]$ToolboxReady = 'unknown',
    [ValidateSet('yes', 'no', 'unknown')][string]$EditorsRestored = 'unknown',
    [ValidateSet('yes', 'no', 'unknown')][string]$AssistantBound = 'unknown'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = [IO.Path]::GetFullPath($PreparedRun)
$local = [IO.Path]::GetFullPath((Join-Path $root 'local-data')) + [IO.Path]::DirectorySeparatorChar
if (!$run.StartsWith($local, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use only this worktree own synthetic acceptance run.' }
$solution = Join-Path $run 'synthetic/ContextChecks.sln'
$receiptPath = Join-Path $run 'cold-start-prepared.json'
$shell = 'C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe'
$payload = Join-Path (Split-Path $shell) 'Extensions/TwinCatAssistant.Native.X86'
function Get-Inputs {
    $files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
    $files += @(Get-ChildItem -LiteralPath (Join-Path $run 'synthetic') -File -Recurse -Force | Where-Object { $_.Extension -in @('.sln', '.tsproj', '.plcproj', '.TcPOU', '.TcTTO', '.suo') })
    $files += Get-Item -LiteralPath $shell
    return @($files | Sort-Object FullName | ForEach-Object { [pscustomobject]@{ path = $_.FullName; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
}
if ($Mode -eq 'Prepare') {
    if (!(Test-Path -LiteralPath $solution)) { throw 'First complete the synthetic host run and prepare its two restored editors and pane. No substitute fixture is accepted.' }
    if (!$OperatorConfirmsRestoredEditorsPrepared -or !@(Get-ChildItem -LiteralPath (Join-Path $run 'synthetic') -File -Recurse -Force -Filter '*.suo').Count) { throw 'Confirm two PLC editors and the Assistant pane were open at normal close, with saved solution-local IDE state.' }
    if (@(Get-Process TcXaeShell -ErrorAction SilentlyContinue).Count) { throw 'Prepare only after normal host close. Never close an unknown IDE for this script.' }
    $launch = Get-Content (Join-Path $run 'runner.jsonl') -First 1 | ConvertFrom-Json
    if ($launch.gate -ne 'launch' -or $launch.observed.suffix -notmatch '^AssistantAcceptance[0-9a-f]{32}$') { throw 'Invalid owned launch record.' }
    $receipt = [pscustomobject]@{ schemaVersion = 1; preparedUtc = [DateTime]::UtcNow.ToString('o'); suffix = $launch.observed.suffix; solution = $solution; inputs = @(Get-Inputs) }
    $receipt | ConvertTo-Json -Depth 6 | Set-Content $receiptPath -Encoding UTF8
    Write-Output 'Prepared only. No scheduled task, Run key, autostart, logoff or restart was registered.'
    exit 0
}
if (!(Test-Path -LiteralPath $receiptPath)) { throw 'Prepare after final coordinated payload installation, before the next actual logon.' }
$receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
if ($receipt.solution -ne $solution -or $receipt.suffix -notmatch '^AssistantAcceptance[0-9a-f]{32}$') { throw 'Invalid prepared receipt.' }
if ($Mode -eq 'Launch') {
    if (!$OperatorAttestsFirstShellSinceLogon -or $OperatorLogonUtc -eq [datetime]::MinValue) { throw 'Actual logon time and explicit first-Shell operator attestation required. A warm restart does not qualify.' }
    if ($OperatorLogonUtc.ToUniversalTime() -le ([datetime]$receipt.preparedUtc).ToUniversalTime() -or $OperatorLogonUtc.ToUniversalTime() -gt [DateTime]::UtcNow) { throw 'Logon must follow preparation and precede capture.' }
    if (@(Get-Process TcXaeShell -ErrorAction SilentlyContinue).Count) { throw 'An IDE is already running. Do not close it; this attempt cannot establish first start.' }
    if (@(Compare-Object ($receipt.inputs | ConvertTo-Json -Depth 5) (@(Get-Inputs) | ConvertTo-Json -Depth 5)).Count) { throw 'Payload or synthetic project/state changed. Reprepare before another real logon.' }
    $capture = Join-Path $run ('cold-start-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $capture | Out-Null
    $started = [DateTime]::UtcNow
    [xml]$manifest = Get-Content (Join-Path $payload 'extension.vsixmanifest')
    $owned = Start-Process -FilePath $shell -ArgumentList @('/RootSuffix', $receipt.suffix, ('"' + $solution + '"'), '/Log', ('"' + (Join-Path $capture 'ActivityLog.xml') + '"')) -WindowStyle Hidden -PassThru
    $record = [pscustomobject]@{
        schemaVersion = 1; startedUtc = $started.ToString('o'); pid = $owned.Id; processStartUtc = $owned.StartTime.ToUniversalTime().ToString('o')
        sessionId = $owned.SessionId; suffix = $receipt.suffix; solution = $solution; inputs = $receipt.inputs
        operatorLogonUtc = $OperatorLogonUtc.ToUniversalTime().ToString('o'); firstSinceLogon = 'operator-attested, not independently proven by process history'
        host = [Diagnostics.FileVersionInfo]::GetVersionInfo($shell).FileVersion
        payloadVersion = $manifest.PackageManifest.Metadata.Identity.Version
        xae = [Diagnostics.FileVersionInfo]::GetVersionInfo('C:\TwinCAT\3.1\Components\Base\TwinCAT XAE Base.dll').FileVersion
        os = [Environment]::OSVersion.Version.ToString(); evidenceKind = 'launch-only; UI completion and first-after-logon acceptance pending'
    }
    $record | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $capture 'launch.json') -Encoding UTF8
    Write-Output "Own PID $($owned.Id). CapturePath: $capture"
    exit 0
}
if (!$CapturePath) { throw 'Observe requires CapturePath returned by Launch.' }
$capture = [IO.Path]::GetFullPath($CapturePath)
if (!$capture.StartsWith($run + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'CapturePath must belong to this prepared run.' }
$record = Get-Content (Join-Path $capture 'launch.json') -Raw | ConvertFrom-Json
if ($PreparingEndedUtc -eq [datetime]::MinValue -or $PreparingEndedUtc.ToUniversalTime() -lt ([datetime]$record.startedUtc).ToUniversalTime() -or $PreparingEndedUtc.ToUniversalTime() -gt [DateTime]::UtcNow) { throw 'Record the actual observed end of Preparing Solution as a UTC timestamp.' }
[pscustomobject]@{
    evidenceKind = 'operator-observed UI; does not independently prove first Shell since logon'
    preparingEndedUtc = $PreparingEndedUtc.ToUniversalTime().ToString('o')
    elapsedMs = ($PreparingEndedUtc.ToUniversalTime() - ([datetime]$record.startedUtc).ToUniversalTime()).TotalMilliseconds
    toolboxReady = $ToolboxReady; editorsRestored = $EditorsRestored; assistantBound = $AssistantBound
} | ConvertTo-Json | Set-Content (Join-Path $capture 'observations.json') -Encoding UTF8
