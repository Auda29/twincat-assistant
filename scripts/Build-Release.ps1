param([Parameter(Mandatory)][string]$DotNetRoot)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if($env:TWINCAT_ASSISTANT_TEST_CONFIG){throw 'Release builds refuse inherited host-test overrides'}
 if((& git status --porcelain --untracked-files=no)){throw 'Release source must be clean'}
 $version=(Get-Content package.json -Raw|ConvertFrom-Json).version
 if($version -cnotmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid release version'}
 $interop='C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TCatSysManagerLib.dll'
 if(!(Test-Path -LiteralPath $interop)){throw 'Trusted native build machine with installed TwinCAT interop is required; it is never uploaded or bundled'}
 if(Test-Path artifacts/application-inputs){throw 'Use a fresh dedicated checkout; existing build inputs are never overwritten'}
 $output=Join-Path $root ('artifacts/release/'+$version)
 if(Test-Path -LiteralPath $output){throw 'Release output already exists'}
 foreach($command in @('node','npm','git','gh','uv')){if(!(Get-Command $command -ErrorAction SilentlyContinue)){throw "Missing build prerequisite: $command"}}
 function Step([string]$name,[scriptblock]$action){Write-Host "START $name";& $action;if($LASTEXITCODE -ne 0){throw "$name failed"};Write-Host "PASS $name"}
 Step 'operator dependencies' { npm ci --no-audit --no-fund }
 Step 'build tools' { npm ci --prefix tools --no-audit --no-fund }
 Step 'reference runtime dependencies' { npm ci --prefix upstream --no-audit --no-fund }
 Step 'source runtime' { node scripts/build-source-runtime.mjs }
 Step 'native build' { & scripts/Build-Native.ps1 -DotNetRoot $DotNetRoot -Architecture X86 }
 Step 'operator regression suite' { node --test --test-concurrency=1 tests/*.test.mjs }
 Step 'native chat startup' { node scripts/test-native-chat-startup.mjs }
 Step 'installer recovery and data preservation' { & powershell.exe -NoProfile -File scripts/Test-InstallerFixture.ps1 }
 Step 'installer wizard' { & powershell.exe -NoProfile -STA -File scripts/Test-InstallerWizard.ps1 }
 $engineering=Join-Path $root 'local-data/release-engineering'
 Step 'Engineering source' { git clone https://github.com/Auda29/twincat-mcp-mono.git $engineering }
 Step 'Engineering pin' { git -C $engineering checkout --detach 3c303837e6d6f72bec1b9d38e778a7c32c61ae5c }
 $knowledge=Join-Path $root 'local-data/release-knowledge';[IO.Directory]::CreateDirectory($knowledge)|Out-Null
 $lock=Get-Content upstream/knowledge-lock.json -Raw|ConvertFrom-Json
 Step 'Knowledge release inputs' { gh release download $lock.tag --repo $lock.repository --dir $knowledge --pattern '*.whl' --pattern 'runtime-requirements.txt' --pattern 'SHA256SUMS.txt' }
 $license=(Resolve-Path upstream/permissions/knowledge-0.1.1.txt).Path
 $licenseSha=(Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash.ToLowerInvariant()
 Step 'reproducible MCP and license inputs' { & scripts/Build-ApplicationInputs.ps1 -Component All -EngineeringCheckout $engineering -KnowledgeArtifacts $knowledge -KnowledgeLicense $license -KnowledgeLicenseSha256 $licenseSha }
 $env:TC_KNOWLEDGE_INPUT=(Resolve-Path artifacts/application-inputs/knowledge).Path
 Step 'Knowledge validator equivalence' { node --test tests/integration/knowledge-validation-overlay.test.mjs }
 $app=Join-Path $root 'artifacts/release-application'
 Step 'application package' { node scripts/build-application-package.mjs $app }
 Step 'packaged runtime and both MCPs' { node scripts/test-built-application.mjs $app }
 Step 'installer' { & scripts/Build-WindowsInstaller.ps1 -PackageDirectory $app -OutputDirectory $output }
 $exe=Get-ChildItem -LiteralPath $output -Filter '*.exe'|Select-Object -First 1
 Step 'actual EXE extraction' { & powershell.exe -NoProfile -File scripts/Test-WindowsInstallerPayload.ps1 -InstallerPath $exe.FullName }
 $source=(& git rev-parse HEAD).Trim()
 if((& git status --porcelain --untracked-files=no)){throw 'Tracked source changed during release build'}
 $provenance=Join-Path $output 'BUILD_PROVENANCE.json'
 @{version=$version;sourceCommit=$source;runId=$env:GITHUB_RUN_ID;buildKind='trusted-windows-source-build';manifestSha256=(Get-FileHash (Join-Path $app 'application-manifest.json')).Hash.ToLowerInvariant();realHostInstallation=$false;realProviderLogin=$false}|ConvertTo-Json|Set-Content -LiteralPath $provenance -Encoding UTF8
 @($exe.FullName,$provenance)|ForEach-Object{(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}|Set-Content (Join-Path $output 'SHA256.txt') -Encoding ASCII
 Write-Host "Release candidate: $output"
}finally{Pop-Location}
