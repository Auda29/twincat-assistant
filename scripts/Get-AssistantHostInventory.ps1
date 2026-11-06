<#
.SYNOPSIS
Read-only inventory of TwinCAT engineering hosts and their classification
against the Assistant host targets (native/host-targets.json).

.DESCRIPTION
Collection reads only registry values, file version resources, PE headers,
small manifest files, vswhere output and process metadata (PID, start time,
session). It never starts, attaches to, activates (COM/ROT) or stops an IDE,
never runs tcpkg or an installer, and writes nothing except -OutputPath.
Process command lines, solution paths, AMS Net IDs and user data are not
collected. Paths below the user profile are reported as %USERPROFILE%.

A matching classification is a static preflight result, not live acceptance.
See docs/HOST_ACCEPTANCE_MATRIX.md.

.PARAMETER InventoryPath
Evaluate a previously recorded or synthetic inventory (either a full output
document or a bare "facts" object) instead of collecting from this machine.
Works on any platform with PowerShell.

.PARAMETER RequireAlphaTarget
Exit with code 3 when no host is eligible for the first installable alpha.
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$TargetsPath,
    [string]$InventoryPath,
    [switch]$RequireAlphaTarget
)
$ErrorActionPreference = 'Stop'
$inventorySchemaVersion = 1
if (-not $TargetsPath) { $TargetsPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'native/host-targets.json' }

function Get-Prop($Object, [string]$Path) {
    $current = $Object
    foreach ($name in $Path.Split('.')) {
        if ($null -eq $current) { return $null }
        if ($current -is [System.Collections.IDictionary]) { $current = $current[$name]; continue }
        $property = $current.PSObject.Properties[$name]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }
    return $current
}

function ConvertTo-VersionOrNull([string]$Text) {
    if (-not $Text) { return $null }
    $match = [regex]::Match($Text, '^\s*(\d+(\.\d+){1,3})')
    if (-not $match.Success) { return $null }
    return [version]$match.Groups[1].Value
}

function Get-TwinCatFamily([string]$Version) {
    $match = [regex]::Match([string]$Version, '^3\.1\.(\d{4})(\.|$)')
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

# ---------------------------------------------------------------- collection

function Protect-Path([string]$Path) {
    if (-not $Path) { return $null }
    $userProfile = [Environment]::GetFolderPath('UserProfile')
    if ($userProfile -and $Path.StartsWith($userProfile, [StringComparison]::OrdinalIgnoreCase)) {
        return '%USERPROFILE%' + $Path.Substring($userProfile.Length)
    }
    return $Path
}

function Get-PeArchitecture([string]$Path) {
    try {
        $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            $reader = New-Object IO.BinaryReader($stream)
            $stream.Position = 0x3c
            $stream.Position = $reader.ReadInt32() + 4
            switch ($reader.ReadUInt16()) { 0x014c { return 'x86' } 0x8664 { return 'amd64' } 0xAA64 { return 'arm64' } default { return 'unknown' } }
        } finally { $stream.Dispose() }
    } catch { return 'unknown' }
}

function Get-FileFacts([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        path = Protect-Path $item.FullName
        name = $item.Name
        fileVersion = $item.VersionInfo.FileVersion
        productVersion = $item.VersionInfo.ProductVersion
        productName = $item.VersionInfo.ProductName
        architecture = Get-PeArchitecture $item.FullName
    }
}

function Read-SmallText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    if ((Get-Item -LiteralPath $Path).Length -gt 65536) { return $null }
    return [IO.File]::ReadAllText($Path)
}

function Open-RegistryKey([string]$Hive, [string]$View, [string]$SubKey) {
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]$Hive, [Microsoft.Win32.RegistryView]$View)
        return $base.OpenSubKey($SubKey, $false)
    } catch { return $null }
}

function Get-RegistryValue([string]$Hive, [string]$View, [string]$SubKey, [string]$Name) {
    $key = Open-RegistryKey $Hive $View $SubKey
    if (-not $key) { return $null }
    try { return $key.GetValue($Name) } finally { $key.Dispose() }
}

function Get-UninstallEntries {
    $entries = @()
    foreach ($view in 'Registry32', 'Registry64') {
        $key = Open-RegistryKey 'LocalMachine' $view 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
        if (-not $key) { continue }
        try {
            foreach ($name in $key.GetSubKeyNames()) {
                $entry = $key.OpenSubKey($name, $false)
                if (-not $entry) { continue }
                try {
                    $displayName = [string]$entry.GetValue('DisplayName')
                    if ($displayName) {
                        $entries += [pscustomobject]@{ displayName = $displayName; displayVersion = [string]$entry.GetValue('DisplayVersion')
                            publisher = [string]$entry.GetValue('Publisher'); installDate = [string]$entry.GetValue('InstallDate'); registryView = $view }
                    }
                } finally { $entry.Dispose() }
            }
        } finally { $key.Dispose() }
    }
    # Registry32/64 can alias the same key on a 32-bit OS.
    return @($entries | Sort-Object displayName, displayVersion, registryView -Unique)
}

function Get-WindowsFacts {
    $cv = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $raw = [string](Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'ProductName')
    $build = [string](Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'CurrentBuild')
    $product = $raw
    # Windows 11 keeps "Windows 10" in ProductName; the build number decides.
    if ($build -match '^\d+$' -and [int]$build -ge 22000 -and $raw -like 'Windows 10*') { $product = 'Windows 11' + $raw.Substring(10) }
    return [ordered]@{
        productName = $product
        productNameRaw = $raw
        displayVersion = [string](Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'DisplayVersion')
        build = $build
        ubr = Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'UBR'
        edition = [string](Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'EditionID')
        installationType = [string](Get-RegistryValue 'LocalMachine' 'Registry64' $cv 'InstallationType')
        architecture = if ([Environment]::Is64BitOperatingSystem) { 'amd64' } else { 'x86' }
        currentProcessArchitecture = if ([Environment]::Is64BitProcess) { 'amd64' } else { 'x86' }
    }
}

function Get-TwinCatFacts($Uninstall) {
    $version = $null; $versionSource = $null
    foreach ($view in 'Registry32', 'Registry64') {
        $value = Get-RegistryValue 'LocalMachine' $view 'SOFTWARE\Beckhoff\TwinCAT3\System' 'TcVersion'
        if ($value) { $version = [string]$value; $versionSource = "HKLM\SOFTWARE\Beckhoff\TwinCAT3\System\TcVersion ($view)"; break }
    }
    $installDir = $null
    foreach ($view in 'Registry32', 'Registry64') {
        $value = Get-RegistryValue 'LocalMachine' $view 'SOFTWARE\Beckhoff\TwinCAT3\3.1' 'InstallDir'
        if ($value) { $installDir = [string]$value; break }
    }
    if (-not $installDir) { $installDir = 'C:\TwinCAT\3.1\' }
    $setups = @($Uninstall | Where-Object { $_.displayName -match '^Beckhoff TwinCAT 3\.1 \(Build \d+\)' } |
        ForEach-Object { [ordered]@{ displayName = $_.displayName; displayVersion = $_.displayVersion; installDate = $_.installDate } })
    $remote = @($Uninstall | Where-Object { $_.displayName -match 'Remote Manager' -and $_.publisher -match 'Beckhoff' } |
        ForEach-Object { [ordered]@{ displayName = $_.displayName; displayVersion = $_.displayVersion } })
    $tcpkgPath = $null
    $command = Get-Command tcpkg -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $tcpkgPath = $command.Source }
    $mechanism = 'unknown'
    if ($tcpkgPath) { $mechanism = 'package-manager' } elseif ($setups.Count -gt 0) { $mechanism = 'classic-setup' }
    if (-not $version -and $setups.Count -gt 0) { $version = $setups[0].displayVersion; $versionSource = 'uninstall entry' }
    return [ordered]@{
        version = $version
        versionSource = $versionSource
        family = Get-TwinCatFamily $version
        installationMechanism = $mechanism
        setups = $setups
        xaeBase = Get-FileFacts (Join-Path $installDir 'Components\Base\TwinCAT XAE Base.dll')
        automationInterop = Get-FileFacts (Join-Path $installDir 'Components\TcBlockDiagram\Common\TCatSysManagerLib.dll')
        remoteManager = $remote
        tcpkg = [ordered]@{ found = [bool]$tcpkgPath; path = Protect-Path $tcpkgPath; executed = $false }
    }
}

function Get-PrerequisiteFacts($Uninstall) {
    $webView = @()
    foreach ($scope in @(@('LocalMachine', 'Registry32'), @('LocalMachine', 'Registry64'), @('CurrentUser', 'Registry64'))) {
        $key = Open-RegistryKey $scope[0] $scope[1] 'SOFTWARE\Microsoft\EdgeUpdate\Clients'
        if (-not $key) { continue }
        try {
            foreach ($name in $key.GetSubKeyNames()) {
                $client = $key.OpenSubKey($name, $false)
                try {
                    # The WebView2 runtime client id is fixed; the display name is localized.
                    if ($name -eq '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -and $client.GetValue('pv')) {
                        $webView += [pscustomobject]@{ version = [string]$client.GetValue('pv'); scope = "$($scope[0])/$($scope[1])" }
                    }
                } finally { if ($client) { $client.Dispose() } }
            }
        } finally { $key.Dispose() }
    }
    return [ordered]@{
        netFrameworkRelease = Get-RegistryValue 'LocalMachine' 'Registry64' 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' 'Release'
        webView2 = @($webView | Sort-Object version, scope -Unique)
    }
}

function Get-DteRegistration([string]$ProgId, [string]$ExecutablePath) {
    $result = [ordered]@{ progId = $ProgId; registered = $false; localServer = $null; matchesExecutable = $false }
    foreach ($view in 'Registry32', 'Registry64') {
        $clsid = Get-RegistryValue 'ClassesRoot' $view "$ProgId\CLSID" ''
        if (-not $clsid) { continue }
        $result.registered = $true
        $server = [string](Get-RegistryValue 'ClassesRoot' $view "CLSID\$clsid\LocalServer32" '')
        if ($server) {
            # LocalServer32 may be quoted, unquoted with spaces, or followed by switches.
            $exe = [regex]::Match($server.Trim(), '^"([^"]+)"|^(.+?\.exe)(\s|$)', 'IgnoreCase')
            $serverPath = if ($exe.Groups[1].Success) { $exe.Groups[1].Value } elseif ($exe.Groups[2].Success) { $exe.Groups[2].Value } else { $server.Trim() }
            $result.localServer = Protect-Path $serverPath
            if ($ExecutablePath -and [string]::Equals([IO.Path]::GetFullPath($serverPath), [IO.Path]::GetFullPath($ExecutablePath), [StringComparison]::OrdinalIgnoreCase)) {
                $result.matchesExecutable = $true
            }
        }
        if ($result.matchesExecutable) { break }
    }
    return $result
}

function Get-TwinCatIntegration([string]$IdeDirectory) {
    $root = Join-Path $IdeDirectory 'Extensions\Beckhoff Automation GmbH'
    $baseManifest = Read-SmallText (Join-Path $root 'TwinCAT XAE Base\extension.vsixmanifest')
    $supported = $null
    if ($baseManifest) {
        $match = [regex]::Match($baseManifest, '<VisualStudio\s+Version="([^"]+)"')
        if ($match.Success) { $supported = $match.Groups[1].Value }
    }
    $xaeBase = [bool]$baseManifest
    $xaePlc = Test-Path -LiteralPath (Join-Path $root 'TwinCAT XAE Plc\extension.vsixmanifest') -PathType Leaf
    return [ordered]@{ present = ($xaeBase -and $xaePlc); xaeBase = $xaeBase; xaePlc = $xaePlc; supportedVisualStudioVersion = $supported }
}

function Get-AssistantPayloads([string[]]$ExtensionDirectories) {
    $payloads = @()
    foreach ($directory in $ExtensionDirectories) {
        if (-not $directory -or -not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        foreach ($child in Get-ChildItem -LiteralPath $directory -Directory -ErrorAction SilentlyContinue) {
            $text = Read-SmallText (Join-Path $child.FullName 'extension.vsixmanifest')
            if (-not $text) { continue }
            $identity = [regex]::Match($text, '<Identity\s+[^>]*Id="(TwinCatAssistant\.[^"]+)"[^>]*Version="([^"]+)"')
            if (-not $identity.Success) { continue }
            $target = [regex]::Match($text, '<InstallationTarget\s+[^>]*Id="([^"]+)"[^>]*Version="([^"]+)"')
            $arch = [regex]::Match($text, '<ProductArchitecture>([^<]+)</ProductArchitecture>')
            $payloads += [ordered]@{
                directory = Protect-Path $child.FullName
                identity = $identity.Groups[1].Value
                version = $identity.Groups[2].Value
                installationTargetId = if ($target.Success) { $target.Groups[1].Value } else { $null }
                installationTargetVersion = if ($target.Success) { $target.Groups[2].Value } else { $null }
                productArchitecture = if ($arch.Success) { $arch.Groups[1].Value } else { $null }
            }
        }
    }
    return ,@($payloads)
}

function Get-RunningInstances([string]$ExecutablePath, $Processes) {
    $running = @()
    foreach ($process in $Processes) {
        $path = $null
        try { $path = $process.Path } catch { }
        if (-not $path -or -not [string]::Equals($path, $ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $start = $null
        try { $start = $process.StartTime.ToUniversalTime().ToString('o') } catch { }
        $running += [ordered]@{ pid = $process.Id; startTimeUtc = $start; sessionId = $process.SessionId }
    }
    return ,@($running)
}

function New-HostFacts([string]$Id, [string]$Kind, [string]$DisplayName, [string]$ProductVersion, [string]$InstallRoot, [string]$ExecutablePath, [string]$ProgIdPrefix, [string[]]$ExtraExtensionDirectories, $Processes) {
    $ide = Split-Path $ExecutablePath -Parent
    $executable = Get-FileFacts $ExecutablePath
    $msenv = Get-FileFacts (Join-Path $ide 'msenv.dll')
    $baseVersion = ConvertTo-VersionOrNull $(if ($msenv) { $msenv.fileVersion } else { $executable.fileVersion })
    if ($Kind -eq 'TcXaeShell' -and $executable.architecture -eq 'amd64') { $Kind = 'TcXaeShell64' }
    $major = if ($baseVersion) { $baseVersion.Major } else { $null }
    $marker = Join-Path $ide 'Extensions\extensions.configurationchanged'
    $markerFacts = [ordered]@{ present = $false }
    if (Test-Path -LiteralPath $marker -PathType Leaf) {
        $item = Get-Item -LiteralPath $marker
        $markerFacts = [ordered]@{ present = $true; length = $item.Length; lastWriteUtc = $item.LastWriteTimeUtc.ToString('o') }
    }
    return [ordered]@{
        id = $Id
        kind = $Kind
        displayName = $DisplayName
        productVersion = $ProductVersion
        installRoot = Protect-Path $InstallRoot
        executable = $executable
        shellBase = [ordered]@{ msenvVersion = if ($msenv) { $msenv.fileVersion } else { $null }; major = $major; minor = if ($baseVersion) { $baseVersion.Minor } else { $null } }
        dte = Get-DteRegistration ("$ProgIdPrefix.$major.0") $ExecutablePath
        twinCatIntegration = Get-TwinCatIntegration $ide
        assistantPayloads = Get-AssistantPayloads (@(Join-Path $ide 'Extensions') + @($ExtraExtensionDirectories))
        configurationChangedMarker = $markerFacts
        running = Get-RunningInstances $ExecutablePath $Processes
    }
}

function Get-HostFacts($Uninstall) {
    $hosts = @()
    $seen = @{}
    $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('TcXaeShell', 'TcXaeShell64', 'devenv') })
    $shellProduct = $Uninstall | Where-Object { $_.displayName -match '^Beckhoff TwinCAT XAE Shell' } | Select-Object -First 1
    $shellRoots = @()
    foreach ($view in 'Registry32', 'Registry64') {
        $beckhoff = Open-RegistryKey 'LocalMachine' $view 'SOFTWARE\Beckhoff'
        if (-not $beckhoff) { continue }
        try {
            foreach ($name in $beckhoff.GetSubKeyNames() | Where-Object { $_ -like 'TcXaeShell*' }) {
                $shellKey = $beckhoff.OpenSubKey($name, $false)
                try {
                    foreach ($version in $shellKey.GetSubKeyNames() | Where-Object { $_ -match '^\d+\.\d+$' }) {
                        $versionKey = $shellKey.OpenSubKey($version, $false)
                        try {
                            $dir = [string]$versionKey.GetValue('InstallDir')
                            if ($dir) { $shellRoots += [pscustomobject]@{ root = $dir; key = "$name\$version"; productVersion = [string]$versionKey.GetValue('Version') } }
                        } finally { $versionKey.Dispose() }
                    }
                } finally { $shellKey.Dispose() }
            }
        } finally { $beckhoff.Dispose() }
    }
    foreach ($fallback in @("${env:ProgramFiles(x86)}\Beckhoff\TcXaeShell", "$env:ProgramFiles\Beckhoff\TcXaeShell", "$env:ProgramFiles\Beckhoff\TcXaeShell64")) {
        if ($fallback -and -not $fallback.StartsWith('\')) { $shellRoots += [pscustomobject]@{ root = $fallback; key = $null; productVersion = $null } }
    }
    foreach ($shell in $shellRoots) {
        foreach ($exeName in 'TcXaeShell.exe', 'TcXaeShell64.exe') {
            $exe = Join-Path $shell.root "Common7\IDE\$exeName"
            if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
            $full = [IO.Path]::GetFullPath($exe)
            if ($seen.ContainsKey($full)) { continue }
            $seen[$full] = $true
            $version = $shell.productVersion
            if (-not $version -and $shellProduct) { $version = $shellProduct.displayVersion }
            $kind = if ($shell.key -like 'TcXaeShell64*' -or $exeName -eq 'TcXaeShell64.exe') { 'TcXaeShell64' } else { 'TcXaeShell' }
            $hosts += New-HostFacts ("tcxaeshell:" + $(if ($shell.key) { $shell.key } else { $full })) $kind 'TwinCAT XAE Shell' $version (Split-Path (Split-Path (Split-Path $full -Parent) -Parent) -Parent) $full 'TcXaeShell.DTE' @() $processes
        }
    }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        # Windows PowerShell emits a parsed JSON array as one pipeline object.
        $instances = (& $vswhere -all -prerelease -products '*' -format json | Out-String) | ConvertFrom-Json
        foreach ($instance in $instances) {
            $exe = Join-Path $instance.installationPath 'Common7\IDE\devenv.exe'
            if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
            $major = (ConvertTo-VersionOrNull $instance.installationVersion).Major
            $userExtensions = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ("Microsoft\VisualStudio\$major.0_" + $instance.instanceId + '\Extensions')
            $hostFacts = New-HostFacts ("vs:" + $instance.instanceId) 'VisualStudio' $instance.displayName $instance.installationVersion $instance.installationPath $exe 'VisualStudio.DTE' @($userExtensions) $processes
            $hostFacts['productId'] = $instance.productId
            $hosts += $hostFacts
        }
    }
    return ,@($hosts)
}

function Get-LiveFacts {
    if ($env:OS -ne 'Windows_NT') { throw 'Live collection requires Windows; use -InventoryPath to evaluate recorded facts.' }
    $uninstall = Get-UninstallEntries
    return [ordered]@{
        windows = Get-WindowsFacts
        twinCat = Get-TwinCatFacts $uninstall
        prerequisites = Get-PrerequisiteFacts $uninstall
        hosts = Get-HostFacts $uninstall
    }
}

# ---------------------------------------------------------------- evaluation

function Test-TargetMatch($Match, $HostFacts, [string]$Family) {
    $architecture = Get-Prop $HostFacts 'executable.architecture'
    $major = Get-Prop $HostFacts 'shellBase.major'
    if ((Get-Prop $Match 'hostKind') -and (Get-Prop $Match 'hostKind') -ne (Get-Prop $HostFacts 'kind')) { return $false }
    if ((Get-Prop $Match 'processArchitecture') -and (Get-Prop $Match 'processArchitecture') -ne $architecture) { return $false }
    if ($null -ne (Get-Prop $Match 'shellBaseMajor') -and [string](Get-Prop $Match 'shellBaseMajor') -ne [string]$major) { return $false }
    $majors = Get-Prop $Match 'shellBaseMajors'
    if ($null -ne $majors -and -not (@($majors | ForEach-Object { [string]$_ }) -contains [string]$major)) { return $false }
    if ((Get-Prop $Match 'twinCatFamily') -and (Get-Prop $Match 'twinCatFamily') -ne $Family) { return $false }
    return $true
}

function Get-HostEvaluation($Facts, $Targets) {
    $family = Get-Prop $Facts 'twinCat.family'
    $version = Get-Prop $Facts 'twinCat.version'
    $eligible = @(Get-Prop $Targets 'policy.installerEligibleClassifications')
    $gateIds = @(Get-Prop $Targets 'gates' | ForEach-Object { $_.id })
    $evaluations = @()
    foreach ($hostFacts in @(Get-Prop $Facts 'hosts')) {
        $reasons = @(); $warnings = @()
        $matched = @(@(Get-Prop $Targets 'targets') | Where-Object { Test-TargetMatch $_.match $hostFacts $family })
        # A target that names the TwinCAT family is more specific than one that does not.
        $target = $matched | Sort-Object { if (Get-Prop $_ 'match.twinCatFamily') { 0 } else { 1 } } | Select-Object -First 1
        $classification = 'unknown'
        if (-not $target) {
            $reasons += 'No known host target matches kind/architecture/shell generation/TwinCAT family.'
            if (-not $family) { $reasons += 'TwinCAT 3.1 build was not identified.' }
        } elseif ($target.status -ne 'alpha-target') {
            $classification = $target.status
            $reasons += [string]$target.reason
        } else {
            $classification = 'alpha-target'
            $match = $target.match
            $executable = Get-Prop $hostFacts 'executable'
            if ((Get-Prop $match 'executableName') -and (Get-Prop $executable 'name') -ne (Get-Prop $match 'executableName')) {
                $classification = 'unknown'; $reasons += 'Host executable name differs from the target.'
            }
            if ((Get-Prop $match 'executableFileVersion') -and (Get-Prop $executable 'fileVersion') -ne (Get-Prop $match 'executableFileVersion')) {
                $classification = 'unknown'; $reasons += "Host executable version '$(Get-Prop $executable 'fileVersion')' differs from '$(Get-Prop $match 'executableFileVersion')'."
            }
            if ($classification -eq 'alpha-target' -and (Get-Prop $match 'requiresTwinCatIntegration') -and -not (Get-Prop $hostFacts 'twinCatIntegration.present')) {
                $classification = 'not-usable'; $reasons += 'TwinCAT XAE Base/Plc integration was not found in this host.'
            }
            if ($classification -eq 'alpha-target') {
                $minimum = Get-Prop $target 'prerequisites.netFrameworkMinimumRelease'
                $release = Get-Prop $Facts 'prerequisites.netFrameworkRelease'
                if ($minimum -and (-not $release -or [int]$release -lt [int]$minimum)) {
                    $classification = 'prerequisite-missing'; $reasons += ".NET Framework release $release is below $minimum (4.7.2)."
                }
                if ((Get-Prop $target 'prerequisites.webView2Runtime.required') -and @(Get-Prop $Facts 'prerequisites.webView2').Count -eq 0) {
                    $classification = 'prerequisite-missing'; $reasons += 'No WebView2 runtime is registered.'
                }
            }
            if ($classification -eq 'alpha-target' -and -not (@(Get-Prop $target 'twinCat.verifiedBuilds') -contains $version)) {
                $classification = [string](Get-Prop $target 'twinCat.otherBuildsInFamily')
                if (-not $classification) { $classification = 'unknown' }
                $reasons += "TwinCAT $version is not a verified build of this target (verified: $((@(Get-Prop $target 'twinCat.verifiedBuilds')) -join ', '))."
            }
            if ($classification -eq 'alpha-target') { $reasons += "Matches $($target.id) with verified TwinCAT $version." }
            $dte = Get-Prop $hostFacts 'dte'
            if ((Get-Prop $match 'dteProgId') -and -not ((Get-Prop $dte 'registered') -and (Get-Prop $dte 'matchesExecutable'))) {
                $warnings += "$(Get-Prop $match 'dteProgId') is not registered for this executable; external read-only probes cannot select this host by ProgID."
            }
            if ((Get-Prop $Facts 'twinCat.installationMechanism') -ne (Get-Prop $target 'twinCat.installationMechanism')) {
                $warnings += "Installation mechanism '$(Get-Prop $Facts 'twinCat.installationMechanism')' differs from the verified '$(Get-Prop $target 'twinCat.installationMechanism')'."
            }
        }
        $payloads = @(Get-Prop $hostFacts 'assistantPayloads')
        $identities = @($payloads | ForEach-Object { Get-Prop $_ 'identity' })
        if ($identities.Count -gt 1) { $warnings += "$($identities.Count) Assistant payloads are discoverable in this host; keep exactly one (move backups outside the extension search path)." }
        $architecture = Get-Prop $hostFacts 'executable.architecture'
        foreach ($payload in $payloads) {
            $payloadArch = Get-Prop $payload 'productArchitecture'
            if ($payloadArch -and $payloadArch -ne $architecture) { $warnings += "Payload $(Get-Prop $payload 'identity') declares $payloadArch but the host runs $architecture." }
            if ($target -and (Get-Prop $target 'payload.identity') -and (Get-Prop $payload 'identity') -ne (Get-Prop $target 'payload.identity')) {
                $warnings += "Payload $(Get-Prop $payload 'identity') is not the payload for $($target.id)."
            }
        }
        $gates = [ordered]@{}
        foreach ($gate in $gateIds) {
            $status = if ($classification -eq 'alpha-target') { Get-Prop $target "acceptance.$gate.status" } elseif ($classification -eq 'blocked') { 'blocked' } else { 'open' }
            if (-not $status) { $status = 'open' }
            $gates[$gate] = $status
        }
        $openGates = @($gateIds | Where-Object { $gates[$_] -notin @('passed', 'passed-user-reported') })
        $evaluations += [ordered]@{
            hostId = Get-Prop $hostFacts 'id'
            kind = Get-Prop $hostFacts 'kind'
            architecture = $architecture
            targetId = if ($target) { $target.id } else { $null }
            classification = $classification
            installerDecision = if ($eligible -contains $classification) { 'eligible' } else { 'refuse' }
            installedPayloads = @($payloads | ForEach-Object { "$(Get-Prop $_ 'identity') $(Get-Prop $_ 'version')" })
            runningInstances = @(Get-Prop $hostFacts 'running').Count
            reasons = $reasons
            warnings = $warnings
            gates = $gates
            openGates = $openGates
        }
    }
    $eligibleHosts = @($evaluations | Where-Object { $_.installerDecision -eq 'eligible' } | ForEach-Object { $_.hostId })
    return [ordered]@{
        targetsSchemaVersion = Get-Prop $Targets 'schemaVersion'
        targetsRecordedAt = Get-Prop $Targets 'recordedAt'
        hosts = $evaluations
        eligibleHosts = $eligibleHosts
        eligibleForAlphaInstaller = ($eligibleHosts.Count -gt 0)
        liveAcceptance = $false
        notice = 'Static read-only preflight. A host classification is not live acceptance; open gates must be observed in that host.'
    }
}

# ---------------------------------------------------------------- main

$targets = [IO.File]::ReadAllText([IO.Path]::GetFullPath($TargetsPath)) | ConvertFrom-Json
if ($targets.schemaVersion -ne 1 -or $targets.kind -ne 'twincat-assistant-host-targets') { throw 'Unsupported host target description.' }
if ($InventoryPath) {
    $supplied = [IO.File]::ReadAllText([IO.Path]::GetFullPath($InventoryPath)) | ConvertFrom-Json
    $facts = if ($supplied.PSObject.Properties['facts']) { $supplied.facts } else { $supplied }
    $collection = [ordered]@{ mode = 'supplied'; source = [IO.Path]::GetFileName($InventoryPath) }
} else {
    $facts = Get-LiveFacts
    $collection = [ordered]@{ mode = 'live-read-only'; elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
}
$document = [ordered]@{
    schemaVersion = $inventorySchemaVersion
    kind = 'twincat-assistant-host-inventory'
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
    collection = $collection
    facts = $facts
    evaluation = Get-HostEvaluation $facts $targets
}
$json = $document | ConvertTo-Json -Depth 12
if ($OutputPath) {
    $fullPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    [IO.File]::WriteAllText($fullPath, $json, (New-Object Text.UTF8Encoding($false)))
}
$json
if ($RequireAlphaTarget -and -not $document.evaluation.eligibleForAlphaInstaller) { exit 3 }
