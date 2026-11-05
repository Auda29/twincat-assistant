$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/installer/HostTransaction.ps1"
. "$PSScriptRoot/installer/RuntimePrerequisites.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) ('tc-prerequisites-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
try {
    $package = Join-Path $root 'package'
    $helper = Join-Path $package 'native/WindowsHost/bin/Release/net8.0-windows'
    New-Item -ItemType Directory $helper -Force | Out-Null
    foreach ($name in @('WindowsHost.exe','WindowsHost.dll','WindowsHost.deps.json')) { Set-Content -LiteralPath (Join-Path $helper $name) -Value 'fixture' }
    Set-Content -LiteralPath (Join-Path $helper 'WindowsHost.runtimeconfig.json') -Value '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}'
    $before = Get-TreeDigest $package
    $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    foreach ($version in @('8.0.33','10.0.11')) {
        $runtime = Join-Path $root $version
        New-Item -ItemType Directory $runtime | Out-Null
        $source = Join-Path $runtime 'fixture.cs'
        Set-Content -LiteralPath $source -Value ('class Runtime { static void Main() { System.Console.WriteLine("Microsoft.NETCore.App ' + $version + ' [fixture]"); } }')
        & $csc /nologo /target:exe "/out:$runtime/dotnet.exe" $source
        if ($LASTEXITCODE) { throw 'Prerequisite fixture compilation failed.' }
        $failed = $false
        try { Assert-ApplicationRuntimePrerequisites $package $runtime } catch { $failed = $true }
        if ($failed -ne ($version -ne '8.0.33')) { throw "Wrong .NET prerequisite decision for $version." }
    }
    $missing = Join-Path $root 'missing'
    $failed = $false
    try { Assert-ApplicationRuntimePrerequisites $package $missing } catch { $failed = $true }
    if (-not $failed -or (Test-Path -LiteralPath $missing)) { throw 'Missing runtime was accepted or modified.' }
    if ((Get-TreeDigest $package) -cne $before) { throw 'Preflight changed the application package.' }
    Write-Host 'Installer prerequisite tests passed: .NET 8 accepted; newer-major-only and missing runtimes refused without writes.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
