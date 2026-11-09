param(
    [string]$ShellExecutable = 'C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe',
    [string]$Runner = (Join-Path $PSScriptRoot '../native/HostAcceptance/bin/Release/net472/HostAcceptance.exe')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('local-data/host-acceptance-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
function Get-Fingerprint($file) {
    try {
        $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $sha.Dispose() }
        } finally { $stream.Dispose() }
        return [pscustomobject]@{ path = $file.FullName; size = $file.Length; sha256 = $digest; status = 'read' }
    } catch {
        return [pscustomobject]@{ path = $file.FullName; size = $file.Length; sha256 = $null; status = 'unreadable-or-concurrently-locked' }
    }
}
function Get-ProtectedFingerprints {
    $paths = @(
        (Join-Path (Split-Path $ShellExecutable) 'Extensions/TwinCatAssistant.Native.X86'),
        (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/native-chat'),
        (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/operator-state')
    )
    $records = @()
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](node_modules|\.cache|cache|logs|log)[\\/]' }) {
                # Record digests only, never configuration values or credentials.
                $records += Get-Fingerprint $file
            }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant') -File -ErrorAction SilentlyContinue) {
        $records += Get-Fingerprint $file
    }
    return @($records | Sort-Object path)
}
if (!(Test-Path -LiteralPath $Runner -PathType Leaf)) { throw 'Build native/HostAcceptance first. See docs/HOST_ACCEPTANCE_20261105.md.' }
$beforeProcesses = @(Get-Process TcXaeShell -ErrorAction SilentlyContinue | Select-Object Id, StartTime)
$before = @(Get-ProtectedFingerprints)
$before | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'protected-before.json') -Encoding UTF8
$beforeProcesses | ConvertTo-Json | Set-Content (Join-Path $run 'processes-before.json') -Encoding UTF8
Push-Location $root
try {
    & $Runner $ShellExecutable (Join-Path $run 'synthetic') 2>&1 | Tee-Object -FilePath (Join-Path $run 'runner.jsonl')
    $runnerExit = $LASTEXITCODE
} finally { Pop-Location }
$after = @(Get-ProtectedFingerprints)
$after | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'protected-after.json') -Encoding UTF8
$difference = @(Compare-Object ($before | ConvertTo-Json -Depth 4) ($after | ConvertTo-Json -Depth 4))
$unreadable = @($before + $after | Where-Object { $_.status -ne 'read' }).Count
$preservedProcesses = @($beforeProcesses | ForEach-Object {
    $process = Get-Process -Id $_.Id -ErrorAction SilentlyContinue
    [pscustomobject]@{ pid = $_.Id; preserved = ($null -ne $process -and $process.StartTime -eq $_.StartTime) }
})
$summary = [pscustomobject]@{
    schemaVersion = 1; utc = [DateTime]::UtcNow.ToString('o'); runnerExit = $runnerExit
    protectedFiles = $before.Count; protectedFilesUnchanged = $(if ($unreadable) { $null } else { $difference.Count -eq 0 })
    unreadableFingerprints = $unreadable
    preexistingProcesses = $preservedProcesses
    scope = 'own-root-suffix-host/direct-reader only; no UI, installed tracker, chat or cold-start acceptance'
}
$summary | ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $run 'summary.json')
Write-Output "Evidence: $run"
if ($unreadable -gt 0 -or $difference.Count -gt 0 -or @($preservedProcesses | Where-Object { !$_.preserved }).Count -gt 0) { exit 4 }
exit $runnerExit
