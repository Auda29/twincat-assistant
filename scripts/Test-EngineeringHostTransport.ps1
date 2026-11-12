$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path ([IO.Path]::GetTempPath()) ('EngineeringTransportProbe-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $csc /nologo /target:exe "/out:$exe" /r:System.Web.Extensions.dll (Join-Path $root 'native\IdeHost\EngineeringHostTransport.cs') (Join-Path $PSScriptRoot 'fixtures\EngineeringHostTransportProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Probe failed.' }
} finally {
    Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue
}
