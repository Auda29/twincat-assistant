param([string]$DotNetRoot)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dotnet=if($DotNetRoot){Join-Path $DotNetRoot 'dotnet.exe'}else{(Get-Command dotnet.exe -ErrorAction Stop).Source}
$sdkRoot=Join-Path (Split-Path -Parent $dotnet) 'sdk'
$csc=@(Get-ChildItem -LiteralPath $sdkRoot -Directory | Sort-Object Name -Descending | ForEach-Object {Join-Path $_.FullName 'Roslyn/bincore/csc.dll'} | Where-Object {Test-Path -LiteralPath $_}) | Select-Object -First 1
if(!$csc){throw 'A .NET SDK is required'}
$references='C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
$refs=@('mscorlib.dll','System.dll','System.Core.dll','System.Web.Extensions.dll')|ForEach-Object {'/reference:'+(Join-Path $references $_)}
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture=Join-Path $parent ('EngineeringFactoryFixture-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture)|Out-Null
try {
 foreach($mode in @('CURRENT','LEGACY_ONLY','FAIL_FACTORY9')) {
  $exe=Join-Path $fixture ($mode+'.exe')
  & $dotnet exec $csc /nologo /target:exe /langversion:latest /nowarn:0649 "/define:$mode" "/out:$exe" $refs (Join-Path $root 'native/IdeHost/EngineeringProviderSession.cs') (Join-Path $root 'native/Shared/EngineeringAuthorization.cs') (Join-Path $PSScriptRoot 'fixtures/EngineeringFactoryProbe.cs')
  if($LASTEXITCODE -ne 0){throw 'Factory fixture compilation failed'}
  & $exe
  if($LASTEXITCODE -ne 0){throw 'Factory fixture failed'}
 }
} finally {
 if([IO.Path]::GetFullPath((Split-Path $fixture -Parent)) -cne $parent -or (Split-Path $fixture -Leaf) -cnotmatch '^EngineeringFactoryFixture-[a-f0-9]{32}$'){throw 'Invalid fixture cleanup path'}
 Remove-Item -LiteralPath $fixture -Recurse -Force
}
