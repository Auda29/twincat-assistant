Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ApplicationRuntimePrerequisites([string]$PackageDirectory, [string]$DotNetRoot) {
    if (-not [IO.Path]::IsPathRooted($PackageDirectory) -or -not [IO.Path]::IsPathRooted($DotNetRoot)) { throw 'Absolute package and .NET runtime paths required.' }
    $helper = Join-Path $PackageDirectory 'native/WindowsHost/bin/Release/net8.0-windows'
    foreach ($name in @('WindowsHost.exe','WindowsHost.dll','WindowsHost.deps.json','WindowsHost.runtimeconfig.json')) {
        $file = Join-Path $helper $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'The application package is missing its Windows helper.' }
        Assert-NoLinks $file
    }
    $runtime = Get-Content -LiteralPath (Join-Path $helper 'WindowsHost.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework.name -cne 'Microsoft.NETCore.App' -or $runtime.runtimeOptions.framework.version -notmatch '^8\.0\.\d+$') { throw 'Unsupported Windows helper runtime contract.' }
    $required = [version]$runtime.runtimeOptions.framework.version
    $dotnet = Join-Path $DotNetRoot 'dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'Install the matching .NET 8 runtime before installing TwinCAT Assistant.' }
    Assert-NoLinks $dotnet
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $dotnet; $info.Arguments = '--list-runtimes'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) { $process.Kill(); throw '.NET runtime discovery timed out.' }
        if ($process.ExitCode -ne 0) { throw '.NET runtime discovery failed.' }
        $versions = @([regex]::Matches($output.Result, '(?m)^Microsoft\.NETCore\.App (8\.0\.\d+) \[') | ForEach-Object { [version]$_.Groups[1].Value })
        if (-not @($versions | Where-Object { $_ -ge $required }).Count) { throw "Install the matching .NET 8 runtime ($required or newer 8.0 patch) before installing TwinCAT Assistant." }
    } finally { $process.Dispose() }
}
