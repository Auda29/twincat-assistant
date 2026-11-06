param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
# Read resources only. Do not invoke Main, run the installer or inspect a host.
$assembly = [Reflection.Assembly]::LoadFile($installer)
$stream = $assembly.GetManifestResourceStream('payload.zip')
if (-not $stream) { throw 'Installer payload resource is missing.' }
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.EndsWith('/')) { continue }
        if ($entries.ContainsKey($name)) { throw "Duplicate installer entry: $name" }
        $entries[$name] = $entry
    }
    $manifestEntry = $entries['package/application-manifest.json']
    if (-not $manifestEntry) { throw 'Embedded application manifest is missing.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.schemaVersion -ne 1) { throw 'Unsupported embedded manifest.' }
    $expectedCount = 1
    foreach ($file in $manifest.files) {
        $name = 'package/' + $file.path
        $entry = $entries[$name]
        if (-not $entry -or $entry.Length -ne $file.size) { throw "Missing or wrong-sized embedded file: $name" }
        $content = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($content)).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose(); $content.Dispose() }
        if ($digest -cne $file.sha256) { throw "Embedded file hash mismatch: $name" }
        $expectedCount++
    }
    $packaged = @($entries.Keys | Where-Object { $_.StartsWith('package/') })
    if ($packaged.Count -ne $expectedCount) { throw 'Embedded application contains unlisted files.' }
    foreach ($name in @('Invoke-Installer.ps1','HostTransaction.ps1','PackageContract.ps1','RuntimePrerequisites.ps1','Get-AssistantHostInventory.ps1','host-targets.json')) {
        if (-not $entries.ContainsKey($name)) { throw "Missing installer support file: $name" }
    }
    if (@($manifest.files | Where-Object path -CEQ 'src/rollback-policy.mjs').Count -and -not $entries.ContainsKey('PolicyRollback.ps1')) { throw 'Policy-capable package requires its installer rollback coordinator.' }
    if (@($manifest.files | Where-Object path -CEQ 'src/rollback-policy.mjs').Count -and -not $entries.ContainsKey('NativeProfile.ps1')) { throw 'Policy-capable package requires native profile isolation preflight.' }
    Write-Host "Installer payload verified: version $($manifest.version), $($manifest.files.Count) application files, all sizes and SHA256 hashes match. Installer was not executed."
} finally { $archive.Dispose(); $stream.Dispose() }
