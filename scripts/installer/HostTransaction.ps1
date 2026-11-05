Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/PackageContract.ps1"
function Assert-NoLinks([string]$path) {
    $item = Get-Item -LiteralPath $path -Force
    while ($item) { if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $($item.FullName)" }; $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent } }
}
function Assert-HostPayload([string]$package) {
    $payload = Join-Path $package 'host/x86'
    $manifest = Join-Path $payload 'extension.vsixmanifest'
    $assembly = Join-Path $payload 'TwinCatAssistant.IdeHost.dll'
    if (-not (Test-Path $manifest -PathType Leaf) -or -not (Test-Path $assembly -PathType Leaf)) { throw 'Package requires host/x86/extension.vsixmanifest and host/x86/TwinCatAssistant.IdeHost.dll.' }
    [xml]$xml = Get-Content -LiteralPath $manifest -Raw
    $identity = $xml.SelectSingleNode("//*[local-name()='Metadata']/*[local-name()='Identity']")
    $target = $xml.SelectSingleNode("//*[local-name()='InstallationTarget']")
    $arch = if ($target) { $target.SelectSingleNode("*[local-name()='ProductArchitecture']") } else { $null }
    if (-not $identity -or $identity.Id -cne 'TwinCatAssistant.Native.X86' -or -not $target -or $target.Id -cne 'Microsoft.VisualStudio.IntegratedShell' -or -not $arch -or $arch.InnerText -cne 'x86') { throw 'Invalid x86 VSIX identity or target.' }
    if (@(Get-ChildItem -LiteralPath $package -Recurse -Force -File | Where-Object { $_.Name -ieq 'TCatSysManagerLib.dll' -or $_.Extension -ieq '.vsix' }).Count) { throw 'Package must not contain Beckhoff interop or opaque VSIX archives.' }
    foreach ($reserved in @('.assistant-owner','assistant-installation.json')) { if (Test-Path (Join-Path $payload $reserved)) { throw 'Reserved installer metadata in host payload.' } }
    return $payload
}
function Assert-Interop([string]$hostDll, [string]$interop) {
    if (-not (Test-Path -LiteralPath $interop -PathType Leaf)) { throw 'Installed TwinCAT interop is missing.' }
    Assert-NoLinks $interop
    $local = [Reflection.AssemblyName]::GetAssemblyName($interop)
    # Isolate metadata inspection so neither the source file nor an assembly identity
    # stays loaded in the installer process across upgrades.
    $encodedPath = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($hostDll))
    $script = '$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(''' + $encodedPath + ''')); $a=[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($p)); $r=@($a.GetReferencedAssemblies() | Where-Object {$_.Name -eq ''TCatSysManagerLib''}); if ($r.Count -ne 1) { exit 2 }; [Console]::Out.Write($r[0].FullName)'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
    $info.Arguments = "-NoProfile -EncodedCommand $encoded"
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.EnvironmentVariables.Remove('PSModulePath')
    $process = [Diagnostics.Process]::Start($info)
    $required = $process.StandardOutput.ReadToEnd(); $errors = $process.StandardError.ReadToEnd(); $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or $required -cne $local.FullName) { throw 'Installed TwinCAT interop assembly identity does not match the host build.' }
}
function Get-TreeDigest([string]$path) {
    $files = @(Get-ChildItem -LiteralPath $path -Force -File -Recurse | Sort-Object FullName)
    $entries = foreach ($f in $files) { if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked payload rejected.' }; "$($f.FullName.Substring($path.Length).ToLowerInvariant())|$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)" }
    foreach ($d in @(Get-ChildItem -LiteralPath $path -Force -Directory -Recurse)) { if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked payload rejected.' } }
    return ($entries -join "`n")
}
function Assert-ExtensionSnapshot([string]$versionDir, [string]$interop, [string]$targetId, [string]$version) {
    $source = Join-Path $versionDir 'application/host/x86'
    $snapshot = Join-Path $versionDir 'extension'
    Assert-NoLinks $snapshot
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File -Force)
    $snapshotFiles = @(Get-ChildItem -LiteralPath $snapshot -Recurse -File -Force)
    if ($snapshotFiles.Count -ne $sourceFiles.Count + 3) { throw 'Extension snapshot inventory mismatch.' }
    foreach ($f in $sourceFiles) {
        $relative = $f.FullName.Substring($source.Length + 1)
        $copy = Join-Path $snapshot $relative
        if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or (Get-FileHash -LiteralPath $copy).Hash -cne (Get-FileHash -LiteralPath $f.FullName).Hash) { throw 'Extension snapshot modified.' }
    }
    if ((Get-Content (Join-Path $snapshot '.assistant-owner') -Raw) -cne "$targetId`n$version`n") { throw 'Extension ownership marker mismatch.' }
    if ((Get-FileHash -LiteralPath (Join-Path $snapshot 'TCatSysManagerLib.dll')).Hash -cne (Get-FileHash -LiteralPath $interop).Hash) { throw 'Installed interop changed; review required.' }
    $selection = Get-Content (Join-Path $snapshot 'assistant-installation.json') -Raw | ConvertFrom-Json
    if ($selection.schemaVersion -ne 1 -or $selection.targetId -cne $targetId -or $selection.version -cne $version -or $selection.applicationRoot -cne (Join-Path $versionDir 'application')) { throw 'Invalid application selection.' }
    $null = Get-TreeDigest $snapshot
}
function Assert-HostClosed([string]$hostRoot) {
    $executable = Join-Path $hostRoot 'Common7/IDE/TcXaeShell.exe'
    foreach ($process in @(Get-Process -Name TcXaeShell -ErrorAction SilentlyContinue)) {
        if (-not $process.Path -or [string]::Equals($process.Path,$executable,[StringComparison]::OrdinalIgnoreCase)) { throw 'Close the selected TcXaeShell and retry; the installer will not close it.' }
    }
}
function Invoke-HostTransaction {
    param([ValidateSet('Install','Rollback')][string]$Action,[string]$InstallRoot,[string]$HostRoot,[string]$Interop,[string]$Package,[object]$Target,[string]$InjectFailure)
    foreach ($p in @($InstallRoot,$HostRoot,$Interop)) { if (-not [IO.Path]::IsPathRooted($p)) { throw 'Absolute paths required.' } }
    $hostRoot = (Resolve-Path -LiteralPath $HostRoot).Path.TrimEnd('\')
    Assert-NoLinks $hostRoot
    $installRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    $extension = Join-Path $hostRoot $Target.payload.installDirectory
    $marker = Join-Path $hostRoot $Target.payload.configurationChangedMarker
    if ($Target.id -cne 'tcxaeshell-x86-tc4024' -or $Target.payload.installMethod -cne 'unpacked-extension-directory' -or $Target.payload.installDirectory -cne 'Common7\IDE\Extensions\TwinCatAssistant.Native.X86' -or $Target.payload.configurationChangedMarker -cne 'Common7\IDE\Extensions\extensions.configurationchanged') { throw 'Unsupported target paths.' }
    if ($installRoot.StartsWith((Join-Path $hostRoot 'Common7\IDE\Extensions') + '\',[StringComparison]::OrdinalIgnoreCase) -or $installRoot -eq (Join-Path $hostRoot 'Common7\IDE\Extensions') -or $installRoot -eq $hostRoot) { throw 'Install root must be outside host extensions.' }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Host configuration marker is missing.' }
    Assert-NoLinks $marker
    if (-not (Test-Path -LiteralPath $installRoot)) { New-Item -ItemType Directory -Path $installRoot | Out-Null }
    Assert-NoLinks $installRoot
    $owned = Join-Path $installRoot '.assistant-install-root'
    if (-not (Test-Path -LiteralPath $owned)) {
        if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count) { throw 'Refusing unowned installation root.' }
        [IO.File]::WriteAllText($owned, 'TwinCatAssistant installation v1')
    }
    if ((Get-Content -LiteralPath $owned -Raw) -cne 'TwinCatAssistant installation v1') { throw 'Invalid installation root marker.' }
    $hostLockPath = Join-Path $hostRoot '.twincat-assistant-deployment.lock'
    if (Test-Path -LiteralPath $hostLockPath) { Assert-NoLinks $hostLockPath }
    $hostLock = [IO.File]::Open($hostLockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $lock = $null
    try {
        $lockPath = Join-Path $installRoot '.lock'
        if (Test-Path -LiteralPath $lockPath) { Assert-NoLinks $lockPath }
        $lock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $journal = Join-Path $installRoot 'transaction.pending'
        if (Test-Path $journal) { throw 'Interrupted transaction requires manual recovery; see transaction.pending.' }
        $statePath = Join-Path $installRoot 'state.json'
        $state = if (Test-Path $statePath) { Get-Content $statePath -Raw | ConvertFrom-Json } else { $null }
        if ($state -and ($state.current -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$' -or $state.current.EndsWith('.'))) { throw 'Invalid installed version.' }
        if ($state -and ($state.hostRoot -cne $hostRoot -or $state.targetId -cne $Target.id)) { throw 'Installation belongs to another host.' }
        $versions = Join-Path $installRoot 'versions'
        if (-not (Test-Path $versions)) { New-Item -ItemType Directory $versions | Out-Null }
        Assert-NoLinks $versions
        if (Test-Path $extension) {
            Assert-NoLinks $extension
            if (-not $state -or -not (Test-Path (Join-Path $extension '.assistant-owner'))) { throw 'Existing extension is not owned; refusing overwrite.' }
            $saved = Join-Path $versions "$($state.current)\extension"
            if (-not (Test-Path $saved) -or (Get-TreeDigest $extension) -cne (Get-TreeDigest $saved)) { throw 'Installed extension differs from owned backup; refusing overwrite.' }
        } elseif ($state) { throw 'Managed extension is missing.' }
        if ($Action -eq 'Install') {
            $m = Test-ApplicationPackage $Package
            $payload = Assert-HostPayload $Package
            Assert-Interop (Join-Path $payload 'TwinCatAssistant.IdeHost.dll') $Interop
            $version = $m.version
            if ($version -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$' -or $version.EndsWith('.')) { throw 'Invalid version.' }
            $versionDir = Join-Path $versions $version
            if (Test-Path $versionDir) { throw 'Version already exists.' }
            New-Item -ItemType Directory $versionDir | Out-Null
            Copy-Item -LiteralPath $Package -Destination (Join-Path $versionDir 'application') -Recurse
            $null = Test-ApplicationPackage (Join-Path $versionDir 'application')
            Copy-Item -LiteralPath $payload -Destination (Join-Path $versionDir 'extension') -Recurse
            Copy-Item -LiteralPath $Interop -Destination (Join-Path $versionDir 'extension/TCatSysManagerLib.dll')
            [IO.File]::WriteAllText((Join-Path $versionDir 'extension/.assistant-owner'),"$($Target.id)`n$version`n")
            $selection = @{schemaVersion=1;targetId=$Target.id;applicationRoot=(Join-Path $versionDir 'application');version=$version}
            [IO.File]::WriteAllText((Join-Path $versionDir 'extension/assistant-installation.json'),($selection | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        } else {
            if (-not $state -or -not $state.previous -or $state.previous -cnotmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$') { throw 'No previous version.' }
            $version = [string]$state.previous
            $versionDir = Join-Path $versions $version
            $null = Test-ApplicationPackage (Join-Path $versionDir 'application')
            $payload = Assert-HostPayload (Join-Path $versionDir 'application')
            Assert-Interop (Join-Path $payload 'TwinCatAssistant.IdeHost.dll') $Interop
            if (-not (Test-Path (Join-Path $versionDir 'extension/.assistant-owner'))) { throw 'Previous extension snapshot missing.' }
            if ((Get-FileHash $Interop).Hash -cne (Get-FileHash (Join-Path $versionDir 'extension/TCatSysManagerLib.dll')).Hash) { throw 'Installed interop changed; rollback needs review.' }
        }
        Assert-ExtensionSnapshot $versionDir $Interop $Target.id $version
        $stage = Join-Path $hostRoot ('.assistant-stage-' + [guid]::NewGuid().ToString('N'))
        $old = Join-Path $hostRoot ('.assistant-old-' + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath (Join-Path $versionDir 'extension') -Destination $stage -Recurse
        if ((Get-TreeDigest $stage) -cne (Get-TreeDigest (Join-Path $versionDir 'extension'))) { throw 'Staged extension verification failed.' }
        $journalData = @{ hostRoot=$hostRoot; extension=$extension; stage=$stage; old=$old; version=$version }
        [IO.File]::WriteAllText($journal,($journalData | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        $movedOld = $false; $movedNew = $false; $markerTime = $null; $bak = $null
        try {
            Assert-HostClosed $hostRoot
            if (Test-Path $extension) { [IO.Directory]::Move($extension,$old); $movedOld = $true }
            [IO.Directory]::Move($stage,$extension); $movedNew = $true
            if ($InjectFailure -eq 'after-swap') { throw 'Injected failure.' }
            $markerTime = (Get-Item -LiteralPath $marker).LastWriteTimeUtc
            (Get-Item -LiteralPath $marker).LastWriteTimeUtc = [DateTime]::UtcNow
            $newState = @{ targetId=$Target.id;hostRoot=$hostRoot;current=$version;previous=$(if ($state) {$state.current} else {$null}) }
            $tempState = Join-Path $installRoot ('.state-' + [guid]::NewGuid().ToString('N'))
            [IO.File]::WriteAllText($tempState,($newState | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
            if (Test-Path $statePath) { $bak = $tempState + '.bak'; [IO.File]::Replace($tempState,$statePath,$bak) } else { [IO.File]::Move($tempState,$statePath) }
        } catch {
            if ($movedNew -and (Test-Path $extension)) { Remove-Item -LiteralPath $extension -Recurse -Force }
            if ($movedOld -and (Test-Path $old)) { [IO.Directory]::Move($old,$extension) }
            if ($markerTime) { (Get-Item -LiteralPath $marker).LastWriteTimeUtc = $markerTime }
            if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
            Remove-Item -LiteralPath $journal -Force
            if ($Action -eq 'Install' -and (Test-Path -LiteralPath $versionDir)) { Remove-Item -LiteralPath $versionDir -Recurse -Force }
            throw
        }
        if ($bak -and (Test-Path $bak)) { Remove-Item -LiteralPath $bak -Force }
        if (Test-Path $old) { Remove-Item -LiteralPath $old -Recurse -Force }
        Remove-Item -LiteralPath $journal -Force
        Write-Output $newState
    } finally { if ($lock) { $lock.Dispose() }; $hostLock.Dispose() }
}
