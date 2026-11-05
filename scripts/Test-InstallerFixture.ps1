$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('twincat-installer-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $fixture = Join-Path $root 'fixture'
    New-Item -ItemType Directory -Path (Join-Path $fixture 'versions') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixture '.twincat-installer-fixture') -Value 'synthetic-only-v1'
    $data = Join-Path $fixture 'operator-data'
    New-Item -ItemType Directory -Path $data | Out-Null
    foreach ($name in @('config','chats','logins','bindings')) { Set-Content (Join-Path $data $name) "sentinel-$name" }
    function Make-Package($version) {
        $p = Join-Path $root "package-$version"
        New-Item -ItemType Directory -Path $p | Out-Null
        Set-Content (Join-Path $p 'payload.bin') "payload-$version"
        $hash = (Get-FileHash (Join-Path $p 'payload.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
        @{schemaVersion=1;version=$version;platform='win32';architecture='x64';files=@(@{path='payload.bin';size=(Get-Item (Join-Path $p 'payload.bin')).Length;sha256=$hash})} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $p 'application-manifest.json')
        return $p
    }
    $one = Make-Package '1.0.0'
    $two = Make-Package '1.1.0'
    $lifecycle = Join-Path $PSScriptRoot 'installer/Invoke-FixtureLifecycle.ps1'
    & $lifecycle Install $fixture $one
    & $lifecycle Install $fixture $two
    & $lifecycle Rollback $fixture
    $state = Get-Content (Join-Path $fixture 'state.json') -Raw | ConvertFrom-Json
    if ($state.current -ne '1.0.0' -or $state.previous -ne '1.1.0') { throw 'Rollback state mismatch.' }
    foreach ($name in @('config','chats','logins','bindings')) { if ((Get-Content (Join-Path $data $name) -Raw).Trim() -ne "sentinel-$name") { throw "Data changed: $name" } }
    $out = Join-Path $root 'output'
    & (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1') -PackageDirectory $one -OutputDirectory $out | Out-Null
    if (-not (Test-Path (Join-Path $out 'TwinCAT-Assistant-1.0.0-x64-setup.exe'))) { throw 'Installer not generated.' }
    $process = Start-Process -FilePath (Join-Path $out 'TwinCAT-Assistant-1.0.0-x64-setup.exe') -ArgumentList '-Action Invalid' -Wait -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $root 'bootstrap-error.txt')
    if ($process.ExitCode -eq 0) { throw 'Bootstrap accepted invalid action.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $source = Join-Path $root 'fake-host.cs'
    '[assembly:System.Reflection.AssemblyFileVersion("15.0.0.0")] class Host { static void Main() {} }' | Set-Content $source
    $fakeHost = Join-Path $root 'TcXaeShell.exe'
    & $compiler /nologo /platform:x86 "/out:$fakeHost" $source
    if ($LASTEXITCODE -ne 0) { throw 'Fake host build failed.' }
    $xae = Join-Path $root 'TwinCAT XAE Base.dll'
    & $compiler /nologo /target:library "/out:$xae" $source
    if ($LASTEXITCODE -ne 0) { throw 'Fake XAE build failed.' }
    $fakeHash = (Get-FileHash $fakeHost -Algorithm SHA256).Hash.ToLowerInvariant()
    $install = Join-Path $root 'isolated-install'
    $exe = Join-Path $out 'TwinCAT-Assistant-1.0.0-x64-setup.exe'
    $args = @('-Action','Install','-InstallRoot',('"' + $install + '"'),'-HostExecutable',('"' + $fakeHost + '"'),'-HostSha256',$fakeHash,'-HostFileVersion','15.0.0.0','-XaeFile',('"' + $xae + '"'),'-XaeFileVersion','15.0.0.0')
    $process = Start-Process -FilePath $exe -ArgumentList $args -Wait -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $root 'installer-error.txt')
    if ($process.ExitCode -ne 0) { throw "Executable installation failed: $(Get-Content (Join-Path $root 'installer-error.txt') -Raw)" }
    if ((Get-Content (Join-Path $install 'state.json') -Raw | ConvertFrom-Json).current -ne '1.0.0') { throw 'Executable state mismatch.' }
    New-Item -ItemType Directory -Path (Join-Path $install 'operator-data') | Out-Null
    Set-Content (Join-Path $install 'operator-data/chat') 'preserved'
    & (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1') -PackageDirectory $two -OutputDirectory $out | Out-Null
    $exe2 = Join-Path $out 'TwinCAT-Assistant-1.1.0-x64-setup.exe'
    $process = Start-Process -FilePath $exe2 -ArgumentList $args -Wait -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $root 'update-error.txt')
    if ($process.ExitCode -ne 0) { throw "Executable update failed: $(Get-Content (Join-Path $root 'update-error.txt') -Raw)" }
    $args[1] = 'Rollback'
    $process = Start-Process -FilePath $exe2 -ArgumentList $args -Wait -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $root 'rollback-error.txt')
    if ($process.ExitCode -ne 0 -or (Get-Content (Join-Path $install 'state.json') -Raw | ConvertFrom-Json).current -ne '1.0.0') { throw 'Executable rollback failed.' }
    if ((Get-Content (Join-Path $install 'operator-data/chat') -Raw).Trim() -ne 'preserved') { throw 'Executable lifecycle changed operator data.' }
    Set-Content (Join-Path $two 'payload.bin') 'tampered'
    $failed = $false
    try { & $lifecycle Install $fixture $two } catch { $failed = $true }
    if (-not $failed) { throw 'Tampered package accepted.' }
    $after = Get-Content (Join-Path $fixture 'state.json') -Raw | ConvertFrom-Json
    if ($after.current -ne '1.0.0') { throw 'Failure changed current version.' }
    $bad = Make-Package '2.0.0'
    Set-Content (Join-Path $bad 'unlisted.txt') 'unexpected'
    $failed = $false
    try { & $lifecycle Install $fixture $bad } catch { $failed = $true }
    if (-not $failed) { throw 'Unlisted file accepted.' }
    Remove-Item (Join-Path $bad 'unlisted.txt')
    $manifest = Get-Content (Join-Path $bad 'application-manifest.json') -Raw | ConvertFrom-Json
    $manifest.files[0].path = '../outside'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $bad 'application-manifest.json')
    $failed = $false
    try { & $lifecycle Install $fixture $bad } catch { $failed = $true }
    if (-not $failed) { throw 'Traversal accepted.' }
    Set-Content (Join-Path $fixture 'versions/1.1.0/payload.bin') 'tampered rollback target'
    $failed = $false
    try { & $lifecycle Rollback $fixture } catch { $failed = $true }
    if (-not $failed) { throw 'Corrupt rollback accepted.' }
    $after = Get-Content (Join-Path $fixture 'state.json') -Raw | ConvertFrom-Json
    if ($after.current -ne '1.0.0') { throw 'Failed rollback changed current version.' }
    Write-Host 'Synthetic first install, update, rollback, preservation and tamper checks passed.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
