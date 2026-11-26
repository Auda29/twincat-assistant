param([string]$DotNetRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($DotNetRoot) { Join-Path $DotNetRoot 'dotnet.exe' } else { (Get-Command dotnet.exe).Source }
$sdk = (& $dotnet --version).Trim()
$csc = Join-Path (Split-Path -Parent $dotnet) "sdk/$sdk/Roslyn/bincore/csc.dll"
$references = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '/reference:' + (Join-Path $references $_) }
$exe = Join-Path ([IO.Path]::GetTempPath()) ('EngineeringDirtyStateProbe-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $dotnet exec $csc /nologo /target:exe /langversion:latest "/out:$exe" $refs (Join-Path $root 'native/IdeHost/EngineeringDirtyState.cs') (Join-Path $PSScriptRoot 'fixtures/EngineeringDirtyStateProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Dirty-state fixture compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Dirty-state fixture failed.' }
} finally {
    Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue
}
