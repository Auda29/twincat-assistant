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
        @{schemaVersion=1;version=$version;platform='windows';architecture='x86';files=@(@{path='payload.bin';sha256=$hash})} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $p 'application-manifest.json')
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
