param(
 [Parameter(Mandatory)][string]$ExpectedSha,
 [Parameter(Mandatory)][long]$RunId,
 [Parameter(Mandatory)][string]$Application,
 [Parameter(Mandatory)][string]$RunnerDirectory
)
$ErrorActionPreference='Stop'
$repo='Auda29/twincat-assistant'
$label='alpha-integrated-20261106-82f6d9c1'
$run=(& gh api "repos/$repo/actions/runs/$RunId" | ConvertFrom-Json)
if ($LASTEXITCODE -or $run.head_sha -cne $ExpectedSha -or $run.head_branch -cne 'ci/alpha-artifact-20261105' -or $run.event -cne 'push' -or $run.path -cne '.github/workflows/windows-package.yml' -or $run.status -cne 'queued') { throw 'Expected exact queued push workflow not found' }
$jobs=(& gh api "repos/$repo/actions/runs/$RunId/jobs" | ConvertFrom-Json).jobs
if (@($jobs | Where-Object { $_.name -ceq 'reviewed-input' -and $_.status -ceq 'queued' -and $_.labels.Count -eq 1 -and $_.labels[0] -ceq $label }).Count -ne 1) { throw 'Expected uniquely labelled upload job not found' }
$runner=(Resolve-Path -LiteralPath $RunnerDirectory).Path
$applicationPath=(Resolve-Path -LiteralPath $Application).Path
$isolatedHome=Join-Path $runner 'isolated-home'
New-Item -ItemType Directory $isolatedHome | Out-Null
$hook=Join-Path $runner 'guard.ps1'
@"
if (`$env:GITHUB_SHA -cne '$ExpectedSha' -or `$env:GITHUB_REPOSITORY -cne '$repo' -or `$env:GITHUB_EVENT_NAME -cne 'push' -or `$env:GITHUB_JOB -cne 'reviewed-input' -or `$env:GITHUB_RUN_ID -cne '$RunId') { throw 'Unreviewed job refused before steps' }
"@ | Set-Content -LiteralPath $hook
function Invoke-IsolatedRunner([string]$arguments,[string]$registrationToken) {
 $start=[Diagnostics.ProcessStartInfo]::new()
 $start.FileName=Join-Path $runner 'bin/Runner.Listener.exe'
 $start.Arguments=$arguments; $start.WorkingDirectory=$runner
 $start.UseShellExecute=$false; $start.CreateNoWindow=$true
 $start.EnvironmentVariables.Clear()
 foreach ($key in @('SystemRoot','WINDIR','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramW6432','COMSPEC','PATHEXT','OS','PROCESSOR_ARCHITECTURE','NUMBER_OF_PROCESSORS','PATH')) {
  $value=[Environment]::GetEnvironmentVariable($key); if ($null -ne $value) { $start.EnvironmentVariables[$key]=$value }
 }
 foreach ($key in @('HOME','USERPROFILE','APPDATA','LOCALAPPDATA','TEMP','TMP')) { $start.EnvironmentVariables[$key]=$isolatedHome }
 $start.EnvironmentVariables['ALPHA_REVIEWED_APPLICATION']=$applicationPath
 $start.EnvironmentVariables['ACTIONS_RUNNER_HOOK_JOB_STARTED']=$hook
 if ($registrationToken) { $start.EnvironmentVariables['ACTIONS_RUNNER_INPUT_TOKEN']=$registrationToken }
 $process=[Diagnostics.Process]::Start($start); $process.WaitForExit()
 if ($process.ExitCode) { throw "Runner failed with exit $($process.ExitCode)" }
}
try {
 $token=(& gh api --method POST "repos/$repo/actions/runners/registration-token" | ConvertFrom-Json).token
 if (-not $token) { throw 'Runner registration token unavailable' }
 Invoke-IsolatedRunner "configure --unattended --url https://github.com/$repo --name alpha-integrated-82f6d9c1 --work _work --ephemeral --no-default-labels --labels $label --disableupdate" $token
 $token=$null
 Invoke-IsolatedRunner 'run --once' $null
} finally {
 $token=$null
 $registered=(& gh api "repos/$repo/actions/runners" | ConvertFrom-Json).runners | Where-Object name -CEQ 'alpha-integrated-82f6d9c1'
 foreach ($entry in $registered) {
  & gh api --method DELETE "repos/$repo/actions/runners/$($entry.id)" | Out-Null
  if ($LASTEXITCODE) { Write-Warning 'Temporary runner deregistration failed; remove this named runner before continuing.' }
 }
}
