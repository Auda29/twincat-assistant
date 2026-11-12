param([string]$InstallRoot, [string]$Python)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This installer targets the Windows Assistant host. Use the source installation guide on other platforms.' }
$taskRepository = Split-Path $PSScriptRoot -Parent
$taskPin = Get-Content (Join-Path $taskRepository 'upstream\knowledge-lock.json') -Raw | ConvertFrom-Json
if ($taskPin.developmentOnly -eq $true) { throw 'Development-only Knowledge pins cannot be installed by the public release installer.' }
if ($taskPin.schemaVersion -ne 1 -or $taskPin.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unsupported Knowledge release pin.' }
if (-not $InstallRoot) { $InstallRoot = Join-Path $env:LOCALAPPDATA ('TwinCatAssistant\knowledge-runtime\' + $taskPin.version) }
if (-not [IO.Path]::IsPathRooted($InstallRoot)) { throw 'Choose an absolute Knowledge installation directory.' }
$taskInstall = [IO.Path]::GetFullPath($InstallRoot)
if (-not $Python) { $Python = $taskPin.python }
$taskPackages = Join-Path $taskInstall 'packages'
$taskEnvironment = Join-Path $taskInstall '.venv'
$taskPython = Join-Path $taskEnvironment 'Scripts\python.exe'
$taskExecutable = Join-Path $taskEnvironment 'Scripts\twincat-knowledge.exe'
$taskReceipt = Join-Path $taskInstall 'installation.json'

function Get-ReleaseHash([string]$File) {
    $taskHash = [Security.Cryptography.SHA256]::Create()
    $taskStream = [IO.File]::OpenRead($File)
    try { return [BitConverter]::ToString($taskHash.ComputeHash($taskStream)).Replace('-', '').ToLowerInvariant() }
    finally { $taskStream.Dispose(); $taskHash.Dispose() }
}

function Assert-ReleaseArtifacts {
    foreach ($taskArtifact in $taskPin.artifacts) {
        $taskFile = Join-Path $taskPackages $taskArtifact.name
        $taskItem = Get-Item -LiteralPath $taskFile
        if ($taskItem.PSIsContainer -or ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            (Get-ReleaseHash $taskFile) -ne $taskArtifact.sha256) {
            throw ('Knowledge release checksum mismatch: ' + $taskArtifact.name)
        }
    }
}

function Assert-InstalledVersion {
    $taskVersion = & $taskExecutable --version
    if ($LASTEXITCODE -ne 0 -or $taskVersion.Trim() -ne ('twincat-knowledge ' + $taskPin.version)) { throw 'Knowledge CLI version mismatch.' }
    $taskInspect = "import importlib.metadata,json,twincat_knowledge; print(json.dumps(dict(version=importlib.metadata.version('twincat-knowledge'),module=twincat_knowledge.__file__)))"
    $taskMetadata = & $taskPython -I -c $taskInspect
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge package inspection failed.' }
    $taskMetadata = $taskMetadata | ConvertFrom-Json
    if ($taskMetadata.version -ne $taskPin.version -or
        -not $taskMetadata.module.StartsWith($taskEnvironment + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Knowledge must use the pinned wheel from its isolated environment.'
    }
}

if (Test-Path -LiteralPath $taskInstall) {
    $taskDirectory = Get-Item -LiteralPath $taskInstall
    if (-not $taskDirectory.PSIsContainer -or ($taskDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        -not (Test-Path -LiteralPath $taskReceipt -PathType Leaf)) { throw 'Existing directory is not a completed managed Knowledge installation.' }
    $taskPrevious = Get-Content -LiteralPath $taskReceipt -Raw | ConvertFrom-Json
    if ($taskPrevious.version -ne $taskPin.version -or $taskPrevious.revision -ne $taskPin.revision) { throw 'Existing Knowledge installation belongs to a different release.' }
    Assert-ReleaseArtifacts
    Assert-InstalledVersion
} else {
    Get-Command gh, uv -ErrorAction Stop | Out-Null
    New-Item -ItemType Directory -Path $taskPackages -Force | Out-Null
    $taskDownload = @('release', 'download', $taskPin.tag, '--repo', $taskPin.repository, '--dir', $taskPackages)
    foreach ($taskArtifact in $taskPin.artifacts) { $taskDownload += @('--pattern', $taskArtifact.name) }
    & gh @taskDownload | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge release download failed. Keep the previous installation configured.' }
    Assert-ReleaseArtifacts
    & uv venv --python $Python $taskEnvironment | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge Python environment creation failed.' }
    $taskRuntimeRequirements=if($taskPin.runtimeRequirements){$taskPin.runtimeRequirements}else{'runtime-requirements.txt'}
    & uv pip sync --python $taskPython --require-hashes (Join-Path $taskPackages $taskRuntimeRequirements) | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge locked dependency installation failed.' }
    $taskWheel = @($taskPin.artifacts | Where-Object { $_.name.EndsWith('.whl') })
    if ($taskWheel.Count -ne 1) { throw 'Knowledge release pin must contain one wheel.' }
    & uv pip install --python $taskPython --no-deps (Join-Path $taskPackages $taskWheel[0].name) | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Knowledge wheel installation failed.' }
    Assert-InstalledVersion
    $taskRecord = [ordered]@{ version=$taskPin.version; revision=$taskPin.revision; tag=$taskPin.tag; artifacts=$taskPin.artifacts }
    [IO.File]::WriteAllText($taskReceipt, ($taskRecord | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

# Return paths only. Configuration and corpus state are a separate, explicit step.
[pscustomobject]@{ KnowledgeExecutable=$taskExecutable; PythonExecutable=$taskPython; Version=$taskPin.version; Revision=$taskPin.revision; InstallRoot=$taskInstall }
