Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Compare-InventoryPath([string]$left, [string]$right) {
    $a = $left.Split('/'); $b = $right.Split('/')
    for ($i = 0; $i -lt [Math]::Min($a.Length, $b.Length); $i++) {
        $result = [StringComparer]::Ordinal.Compare($a[$i], $b[$i])
        if ($result -ne 0) { return $result }
    }
    return $a.Length.CompareTo($b.Length)
}
function Test-ApplicationPackage([string]$Directory) {
    if (-not [IO.Path]::IsPathRooted($Directory)) { throw 'PackageDirectory must be absolute.' }
    $root = (Resolve-Path -LiteralPath $Directory).Path
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package root is a reparse point.' }
    $manifestPath = Join-Path $root 'application-manifest.json'
    $items = @(Get-ChildItem -LiteralPath $root -Force -Recurse)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Package contains a reparse point.' }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or (Get-Item -LiteralPath $manifestPath).Length -gt 32MB) { throw 'Missing or oversized manifest.' }
    $m = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ((@($m.PSObject.Properties.Name | Sort-Object) -join ',') -cne 'architecture,files,platform,schemaVersion,version' -or $m.schemaVersion -ne 1 -or $m.platform -cne 'win32' -or $m.architecture -cnotin @('x64','arm64') -or $m.version -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$' -or $m.version.EndsWith('.')) { throw 'Invalid package identity.' }
    $files = @($m.files)
    if ($files.Count -eq 0) { throw 'Empty package.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $last = $null
    foreach ($entry in $files) {
        if ((@($entry.PSObject.Properties.Name | Sort-Object) -join ',') -cne 'path,sha256,size') { throw 'Invalid file entry.' }
        $path = [string]$entry.path
        if (-not $path -or $path.Contains('\') -or $path.Contains(':') -or @($path.Split('/') | Where-Object { -not $_ -or $_ -in @('.','..') -or $_.EndsWith('.') -or $_.EndsWith(' ') -or $_ -match '^(?i:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)' }).Count -or $path -ieq 'application-manifest.json') { throw "Invalid package path: $path" }
        if (-not $seen.Add($path) -or ($null -ne $last -and (Compare-InventoryPath $last $path) -ge 0)) { throw "Duplicate or unsorted package path: $path" }
        $last = $path
        if ([string]$entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $entry.size -isnot [ValueType] -or $entry.size -lt 0 -or [long]$entry.size -ne $entry.size) { throw "Invalid inventory: $path" }
        $file = Join-Path $root ($path.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $entry.size -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256) { throw "Missing or modified file: $path" }
    }
    foreach ($file in @($items | Where-Object { -not $_.PSIsContainer })) {
        $relative = $file.FullName.Substring($root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1).Replace('\','/')
        if ($relative -cne 'application-manifest.json' -and -not $seen.Contains($relative)) { throw "Unlisted file: $relative" }
    }
    return $m
}
