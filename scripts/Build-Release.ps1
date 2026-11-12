param([Parameter(Mandatory)][string]$DotNetRoot,[string]$BuildDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
$previousDotNetRoot=$env:DOTNET_ROOT
try {
 if($env:TWINCAT_ASSISTANT_TEST_CONFIG){throw 'Release builds refuse inherited host-test overrides'}
 $lock=Get-Content upstream/knowledge-lock.json -Raw|ConvertFrom-Json
 if($lock.developmentOnly){throw 'Knowledge pin is a private development candidate. Public release requires a reviewed upstream release/artifact pin; use the explicit candidate input builder for development.'}
 $engineeringLock=Get-Content upstream/engineering-lock.json -Raw|ConvertFrom-Json
 if($engineeringLock.schemaVersion -ne 1 -or $engineeringLock.revision -cnotmatch '^[a-f0-9]{40}$' -or $engineeringLock.moduleVersion -ne '1.0.0.0'){throw 'Invalid reviewed Engineering lock'}
 if(!$engineeringLock.tag -or !$lock.tag){throw 'Public builds require versioned releases for both MCPs'}
 if((& git status --porcelain --untracked-files=no)){throw 'Release source must be clean'}
 $version=(Get-Content package.json -Raw|ConvertFrom-Json).version
 if($version -cnotmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid release version'}
 $interop='C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TCatSysManagerLib.dll'
 if(!(Test-Path -LiteralPath $interop)){throw 'Trusted native build machine with installed TwinCAT interop is required; it is never uploaded or bundled'}
 if(!$BuildDirectory){$BuildDirectory=Join-Path $root 'artifacts'}
 $BuildDirectory=[IO.Path]::GetFullPath($BuildDirectory)
 $inputRoot=Join-Path $BuildDirectory 'application-inputs'
 if(Test-Path -LiteralPath $inputRoot){throw 'Existing build inputs are never overwritten; use a fresh BuildDirectory'}
 $output=Join-Path $BuildDirectory ('release/'+$version)
 if(Test-Path -LiteralPath $output){throw 'Release output already exists'}
 [IO.Directory]::CreateDirectory($BuildDirectory)|Out-Null
 foreach($command in @('node','npm','git','gh','uv')){if(!(Get-Command $command -ErrorAction SilentlyContinue)){throw "Missing build prerequisite: $command"}}
 $taskCompletedSteps=[Collections.Generic.List[string]]::new()
 function Step([string]$name,[scriptblock]$action){Write-Host "START $name";& $action;if($LASTEXITCODE -ne 0){throw "$name failed"};$taskCompletedSteps.Add($name);Write-Host "PASS $name"}
 Step 'operator dependencies' { npm ci --no-audit --no-fund }
 Step 'build tools' { npm ci --prefix tools --no-audit --no-fund }
 Step 'reference runtime dependencies' { npm ci --prefix upstream --no-audit --no-fund }
 Step 'source runtime' { node scripts/build-source-runtime.mjs }
 Step 'native build' { & scripts/Build-Native.ps1 -DotNetRoot $DotNetRoot -Architecture X86 }
 Step 'operator regression suite' { node --test --test-concurrency=1 tests/*.test.mjs }
 Step 'native chat startup' { node scripts/test-native-chat-startup.mjs }
 Step 'installer recovery and data preservation' { & powershell.exe -NoProfile -File scripts/Test-InstallerFixture.ps1 }
 Step 'installer wizard' { & powershell.exe -NoProfile -STA -File scripts/Test-InstallerWizard.ps1 }
 $engineering=Join-Path $BuildDirectory 'release-engineering'
 Step 'Engineering source' { git clone https://github.com/Auda29/twincat-mcp-mono.git $engineering }
 Step 'Engineering pin' { git -C $engineering checkout --detach $engineeringLock.revision }
 Step 'Engineering published source' {
  $taskReleasedCommit=(& git -C $engineering rev-parse ('refs/tags/'+$engineeringLock.tag+'^{commit}')).Trim()
  if($LASTEXITCODE -ne 0 -or $taskReleasedCommit -cne $engineeringLock.revision){throw 'Engineering release tag differs from the reviewed source pin'}
  $taskRelease=& gh release view $engineeringLock.tag --repo $engineeringLock.repository --json isDraft | ConvertFrom-Json
  if($LASTEXITCODE -ne 0 -or $taskRelease.isDraft -ne $false){throw 'Engineering release is not published'}
 }
 $engineeringArtifacts=Join-Path $BuildDirectory 'release-engineering-assets';[IO.Directory]::CreateDirectory($engineeringArtifacts)|Out-Null
 $taskEngineeringDownload=@('release','download',$engineeringLock.tag,'--repo',$engineeringLock.repository,'--dir',$engineeringArtifacts)
 foreach($artifact in $engineeringLock.releaseArtifacts){$taskEngineeringDownload+=@('--pattern',$artifact.name)}
 Step 'Engineering release inputs' { gh @taskEngineeringDownload }
 $knowledge=Join-Path $BuildDirectory 'release-knowledge';[IO.Directory]::CreateDirectory($knowledge)|Out-Null
 $taskDownloadArgs=@('release','download',$lock.tag,'--repo',$lock.repository,'--dir',$knowledge)
 foreach($artifact in $lock.artifacts){$taskDownloadArgs+=@('--pattern',$artifact.name)}
 Step 'Knowledge release inputs' { gh @taskDownloadArgs }
 Step 'Knowledge published source' {
  $taskRelease=& gh release view $lock.tag --repo $lock.repository --json isDraft,targetCommitish | ConvertFrom-Json
  if($LASTEXITCODE -ne 0 -or $taskRelease.isDraft -ne $false -or $taskRelease.targetCommitish -cne $lock.revision){throw 'Knowledge release differs from the reviewed source pin'}
 }
 $license=(Resolve-Path ('upstream/permissions/knowledge-'+$lock.version+'.txt')).Path
 $licenseSha=(Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash.ToLowerInvariant()
 $env:DOTNET_ROOT=[IO.Path]::GetFullPath($DotNetRoot)
 Step 'reproducible MCP and license inputs' { & scripts/Build-ApplicationInputs.ps1 -Component All -EngineeringCheckout $engineering -EngineeringArtifacts $engineeringArtifacts -KnowledgeArtifacts $knowledge -KnowledgeLicense $license -KnowledgeLicenseSha256 $licenseSha -OutputRoot $inputRoot }
 Step 'Engineering owned child' { & powershell.exe -NoProfile -File scripts/Test-EngineeringMcpChild.ps1 -EngineeringInput (Join-Path $inputRoot 'engineering') }
 $env:TC_KNOWLEDGE_INPUT=(Resolve-Path (Join-Path $inputRoot 'knowledge')).Path
 Step 'Knowledge validator equivalence' { node --test tests/integration/knowledge-validation-overlay.test.mjs }
 $app=Join-Path $BuildDirectory 'release-application'
 Step 'application package' { node scripts/build-application-package.mjs $app $inputRoot }
 Step 'packaged runtime and both MCPs' { node scripts/test-built-application.mjs $app }
 Step 'installer' { & scripts/Build-WindowsInstaller.ps1 -PackageDirectory $app -OutputDirectory $output }
 $exe=Get-ChildItem -LiteralPath $output -Filter '*.exe'|Select-Object -First 1
 Step 'actual EXE extraction' { & powershell.exe -NoProfile -File scripts/Test-WindowsInstallerPayload.ps1 -InstallerPath $exe.FullName }
 $source=(& git rev-parse HEAD).Trim()
 if((& git status --porcelain --untracked-files=no)){throw 'Tracked source changed during release build'}
 $provenance=Join-Path $output 'BUILD_PROVENANCE.json'
 $taskBuildKind=if($env:GITHUB_RUN_ID){'trusted-windows-source-build'}else{'local-windows-source-build'}
 @{version=$version;sourceCommit=$source;runId=$env:GITHUB_RUN_ID;buildKind=$taskBuildKind;checks=@($taskCompletedSteps);manifestSha256=(Get-FileHash (Join-Path $app 'application-manifest.json')).Hash.ToLowerInvariant();realHostInstallation=$false;realProviderLogin=$false}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $provenance -Encoding UTF8
 @($exe.FullName,$provenance)|ForEach-Object{(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}|Set-Content (Join-Path $output 'SHA256.txt') -Encoding ASCII
 Write-Host "Release candidate: $output"
}finally{$env:DOTNET_ROOT=$previousDotNetRoot;Pop-Location}
