param([string]$EngineeringInput)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:ENGINEERING_TEST_DOTNET) { $env:ENGINEERING_TEST_DOTNET } else { (Get-Command dotnet.exe -ErrorAction Stop).Source }
$sdkRoot = Join-Path (Split-Path -Parent $dotnet) 'sdk'
$csc = @(Get-ChildItem -LiteralPath $sdkRoot -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
if (-not $csc) { throw 'A .NET SDK with the Roslyn compiler is required.' }
$references = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).ProviderPath.TrimEnd('\')
$fixture = Join-Path $tempRoot ('EngineeringChildFixture-' + [guid]::NewGuid().ToString('N'))
$exe = Join-Path $tempRoot ('EngineeringChildProbe-' + [guid]::NewGuid().ToString('N') + '.exe')
function Write-FixtureManifest([string]$Package) {
    $entries = @(Get-ChildItem -LiteralPath (Join-Path $Package 'runtime') -File -Recurse | ForEach-Object {
        @{path=$_.FullName.Substring($Package.Length+1).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    [IO.File]::WriteAllText((Join-Path $Package 'application-manifest.json'), (@{files=$entries} | ConvertTo-Json -Depth 8 -Compress), (New-Object Text.UTF8Encoding($false)))
}
try {
    $synthetic = Join-Path $fixture 'synthetic'
    New-Item -ItemType Directory -Force (Join-Path $synthetic 'runtime\node'), (Join-Path $synthetic 'runtime\mcp\engineering\packages\mcp\dist') | Out-Null
    Copy-Item -LiteralPath (Get-Command node.exe).Source -Destination (Join-Path $synthetic 'runtime\node\node.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\EngineeringMcpChildProbe.js') -Destination (Join-Path $synthetic 'runtime\mcp\engineering\packages\mcp\dist\index.js')
    Write-FixtureManifest $synthetic
    $refArgs = @('mscorlib.dll','System.dll','System.Core.dll','System.Web.Extensions.dll','System.Security.dll','Microsoft.CSharp.dll') | ForEach-Object { '/reference:' + (Join-Path $references $_) }
    & $dotnet exec $csc /nologo /target:exe /langversion:latest "/out:$exe" $refArgs (Join-Path $root 'native\IdeHost\EngineeringMcpChild.cs') (Join-Path $root 'native\IdeHost\EngineeringHostTransport.cs') (Join-Path $PSScriptRoot 'fixtures\EngineeringMcpChildProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
    & $exe $synthetic
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic probe failed.' }
    if ($EngineeringInput) {
        $source = (Resolve-Path -LiteralPath $EngineeringInput).ProviderPath
        if (-not (Test-Path -LiteralPath (Join-Path $source 'packages\mcp\dist\index.js') -PathType Leaf)) { throw 'Engineering input has no MCP entry.' }
        if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Engineering input is redirected.' }
        if (Get-ChildItem -LiteralPath $source -Recurse -Force -Attributes ReparsePoint | Select-Object -First 1) { throw 'Engineering input contains a redirected path.' }
        $actual = Join-Path $fixture 'actual'
        $destination = Join-Path $actual 'runtime\mcp\engineering'
        New-Item -ItemType Directory -Force $destination, (Join-Path $actual 'runtime\node') | Out-Null
        Copy-Item -LiteralPath (Get-Command node.exe).Source -Destination (Join-Path $actual 'runtime\node\node.exe')
        Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
        Write-FixtureManifest $actual
        & $exe $actual real
        if ($LASTEXITCODE -ne 0) { throw 'Packaged engineering MCP probe failed.' }
    }
} finally {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $fixture) {
        $resolved = (Resolve-Path -LiteralPath $fixture).ProviderPath.TrimEnd('\')
        $expected = [IO.Path]::GetFullPath($fixture).TrimEnd('\')
        if (-not $resolved.Equals($expected, [StringComparison]::OrdinalIgnoreCase) -or
            -not $resolved.StartsWith($tempRoot + '\EngineeringChildFixture-', [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetDirectoryName($resolved)).Equals($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing to remove an unexpected fixture path.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
