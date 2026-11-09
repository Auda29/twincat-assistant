Set-StrictMode -Version Latest
function Start-PolicyRollback([string]$CurrentApplication,[string]$TargetApplication,[string]$StateRoot,[string]$InstallRoot) {
    # Validate every byte before executing the CURRENT package. Never load any
    # executable, module or path supplied by an operator configuration.
    $null = Test-ApplicationPackage $CurrentApplication
    $script = Join-Path $CurrentApplication 'src/rollback-policy.mjs'
    $node = Join-Path $CurrentApplication 'runtime/node/node.exe'
    if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw 'Use the new C1 installer/runtime for policy-aware rollback; the C0 installer cannot downgrade C1.' }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$node; $start.Arguments='"'+$script+'"'
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.EnvironmentVariables.Clear()
    foreach ($key in @('SystemRoot','WINDIR','SystemDrive','ProgramFiles','ProgramFiles(x86)','ProgramW6432','COMSPEC','PATHEXT','PATH','TEMP','TMP')) {
        $value=[Environment]::GetEnvironmentVariable($key)
        if ($null -ne $value) { $start.EnvironmentVariables[$key]=$value }
    }
    $programs=if ($env:ProgramW6432) {$env:ProgramW6432} else {$env:ProgramFiles}
    $start.EnvironmentVariables['DOTNET_ROOT']=Join-Path $programs 'dotnet'
    $process=[Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.WriteLine((@{state=$StateRoot;target=$TargetApplication;installation=$InstallRoot}|ConvertTo-Json -Compress))
        $read=$process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(600000) -or $read.Result -cne '{"status":"policy-ready"}' -or $process.HasExited) { throw 'Policy downgrade did not reach its validated ready boundary; retain C1 for recovery.' }
        return $process
    } catch {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
        throw
    }
}
function Stop-PolicyRollback($Process,[bool]$Complete) {
    if ($Complete) { $Process.StandardInput.WriteLine('swap-complete') }
    $Process.StandardInput.Close()
    if (-not $Process.WaitForExit(10000)) { throw 'Policy helper has not exited; retain transaction.pending and recover with C1.' }
    if ($Complete -and $Process.ExitCode -ne 0) { throw 'Policy helper did not confirm completion; retain transaction.pending.' }
    $Process.Dispose()
}
