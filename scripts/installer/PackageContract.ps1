Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Test-ApplicationPackage([string]$Directory) {
    if (-not [IO.Path]::IsPathRooted($Directory)) { throw 'PackageDirectory must be absolute.' }
    $root = (Resolve-Path -LiteralPath $Directory).Path
    $manifestPath = Join-Path $root 'application-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Missing application-manifest.json.' }
    $items = @(Get-ChildItem -LiteralPath $root -Force -Recurse)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Package contains a reparse point.' }
    $m = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($m.schemaVersion -ne 1 -or $m.platform -cne 'windows' -or $m.architecture -cne 'x86' -or $m.version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Invalid package identity.' }
    $files = @($m.files)
    if ($files.Count -eq 0) { throw 'Empty package.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $files) {
        $path = [string]$entry.path
        if ($path -cnotmatch '^[A-Za-z0-9_+.-]+(?:/[A-Za-z0-9_+.-]+)*$' -or @($path.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' -or $_.EndsWith('.') }).Count -or $path -ieq 'application-manifest.json') { throw "Invalid package path: $path" }
        if (-not $seen.Add($path)) { throw "Duplicate package path: $path" }
        if ([string]$entry.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "Invalid SHA256: $path" }
        $file = Join-Path $root ($path.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256) { throw "Missing or modified file: $path" }
    }
    foreach ($file in @($items | Where-Object { -not $_.PSIsContainer })) {
        $relative = $file.FullName.Substring($root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1).Replace('\','/')
        if ($relative -ine 'application-manifest.json' -and -not $seen.Contains($relative)) { throw "Unlisted file: $relative" }
    }
    return $m
}
