param(
    [Parameter(Mandatory = $true)][string]$DotNetRoot,
    [string]$NodePath = (Get-Command node -ErrorAction Stop).Source,
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant\operator-state')
)
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskNode = [IO.Path]::GetFullPath($NodePath)
$taskSdk = [IO.Path]::GetFullPath($DotNetRoot)
$taskState = [IO.Path]::GetFullPath($StateRoot)
foreach ($taskFile in @($taskNode, (Join-Path $taskSdk 'dotnet.exe'),
    (Join-Path $taskRepository 'src\native-chat.mjs'),
    (Join-Path $taskRepository 'native\WindowsHost\bin\Release\net8.0-windows\WindowsHost.exe'),
    (Join-Path $taskRepository 'upstream\node_modules\@openchamber\web\bin\cli.js'),
    (Join-Path $taskRepository 'upstream\node_modules\@opencode\cli\bin\opencode.exe'))) {
    if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw "Missing prerequisite: $taskFile" }
}
$taskDirectory = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant'
[IO.Directory]::CreateDirectory($taskDirectory) | Out-Null
if ((Get-Item -LiteralPath $taskDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected configuration directory rejected.' }
$taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$taskAcl = New-Object Security.AccessControl.DirectorySecurity
$taskAcl.SetOwner($taskSid)
$taskAcl.SetAccessRuleProtection($true,$false)
$taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($taskSid,
    [Security.AccessControl.FileSystemRights]::FullControl,
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $taskDirectory -AclObject $taskAcl
$taskConfig = Join-Path $taskDirectory 'native-chat.json'
$taskValue = [ordered]@{ NodePath=$taskNode; RepositoryRoot=$taskRepository; StateRoot=$taskState; DotNetRoot=$taskSdk }
if (Test-Path -LiteralPath $taskConfig) {
    $taskExisting = Get-Content -LiteralPath $taskConfig -Raw | ConvertFrom-Json
    foreach ($taskKey in $taskValue.Keys) {
        if ($taskExisting.$taskKey -ne $taskValue[$taskKey]) { throw 'A different native chat setup already exists; inspect it before replacing it.' }
    }
    Write-Output 'Existing native chat setup matches. No credential was read or changed.'
    exit 0
}
$taskBytes = [Text.UTF8Encoding]::new($false).GetBytes(($taskValue | ConvertTo-Json))
$taskStream = [IO.File]::Open($taskConfig,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $taskStream.Write($taskBytes,0,$taskBytes.Length); $taskStream.Flush() } finally { $taskStream.Dispose() }
Write-Output "Native chat configured: $taskConfig"
Write-Output 'A new profile starts without a provider. Connect chat sets up a local password in the native window.'
