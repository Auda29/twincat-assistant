param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
# Inspect resources and invoke only the extraction helper in a fresh temp root.
# Never invoke Main/Run, run the installer transaction or inspect a host.
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
} finally { $archive.Dispose(); $stream.Dispose() }
$extract = $assembly.GetType('Bootstrap').GetMethod('ExtractPayload', [Reflection.BindingFlags]'Static,NonPublic')
if (-not $extract) { throw 'Installer extraction helper is missing.' }
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$extracted = Join-Path $tempParent ('tc-installer-extraction-' + [guid]::NewGuid().ToString('N'))
try {
    $stream = $assembly.GetManifestResourceStream('payload.zip')
    $parameters = New-Object 'object[]' 2
    $parameters[0] = $stream; $parameters[1] = [string]$extracted
    try { $null = $extract.Invoke($null, $parameters) } finally { $stream.Dispose() }
    . (Join-Path $PSScriptRoot 'installer/PackageContract.ps1')
    $actual = Test-ApplicationPackage (Join-Path $extracted 'package')
    if ($actual.version -cne $manifest.version -or $actual.files.Count -ne $manifest.files.Count) { throw 'Extracted package identity changed.' }
    Write-Host "Installer payload verified: version $($manifest.version), $($manifest.files.Count) application files, all sizes and SHA256 hashes match. Actual EXE extraction and extracted manifest verification passed. Installer Main/Run was not executed."
} finally {
    if ([IO.Path]::GetFullPath((Split-Path $extracted -Parent)) -cne $tempParent -or (Split-Path $extracted -Leaf) -cnotmatch '^tc-installer-extraction-[a-f0-9]{32}$') { throw 'Unexpected extraction cleanup path.' }
    if (Test-Path -LiteralPath $extracted) { Remove-Item -LiteralPath $extracted -Recurse -Force }
}
