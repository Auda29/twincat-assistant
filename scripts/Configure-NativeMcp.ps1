param(
    [Parameter(Mandatory = $true)][string]$KnowledgeExecutable,
    [Parameter(Mandatory = $true)][string]$EngineeringEntrypoint,
    [string]$KnowledgeConfig,
    [string]$McpSettingsPath = (Join-Path $env:LOCALAPPDATA 'TwinCatAssistant\native-mcp.json')
)
$ErrorActionPreference = 'Stop'
function Assert-RegularFile([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'Choose absolute MCP paths.' }
    $taskItem = Get-Item -LiteralPath $Path
    if ($taskItem.PSIsContainer -or ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'MCP paths must select regular files.' }
}
function Write-ManagedJson([string]$Path,$Value) {
    $taskDirectory = Split-Path $Path -Parent
    if (-not [IO.Path]::IsPathRooted($Path) -or ((Get-Item -LiteralPath $taskDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Choose a regular absolute settings directory.' }
    $taskTemporary = Join-Path $taskDirectory ([Guid]::NewGuid().ToString('N')+'.tmp')
    [IO.File]::WriteAllText($taskTemporary,($Value | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path) {
            Assert-RegularFile $Path
            [IO.File]::Replace($taskTemporary,$Path,($Path+'.before-'+[Guid]::NewGuid().ToString('N')+'.json'))
        } else { [IO.File]::Move($taskTemporary,$Path) }
    } finally { if (Test-Path -LiteralPath $taskTemporary) { [IO.File]::Delete($taskTemporary) } }
}
$taskNativeChatPath = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant\native-chat.json'
if (-not [IO.Path]::IsPathRooted($McpSettingsPath) -or [IO.Path]::GetExtension($McpSettingsPath) -ne '.json' -or
    [string]::Equals([IO.Path]::GetFullPath($McpSettingsPath),[IO.Path]::GetFullPath($taskNativeChatPath),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a separate absolute MCP settings JSON file.'
}
if (Test-Path -LiteralPath $McpSettingsPath) {
    Assert-RegularFile $McpSettingsPath
    $taskExistingMcp = Get-Content -LiteralPath $McpSettingsPath -Raw | ConvertFrom-Json
    if (@($taskExistingMcp.PSObject.Properties.Name | Where-Object { $_ -notin @('knowledge','engineering') }).Count -or -not $taskExistingMcp.knowledge -or -not $taskExistingMcp.engineering) {
        throw 'The output file is not an existing native MCP settings file; it will not be replaced.'
    }
}
Assert-RegularFile $taskNativeChatPath
Assert-RegularFile $KnowledgeExecutable
Assert-RegularFile $EngineeringEntrypoint
$taskKnowledge = [ordered]@{ executable=[IO.Path]::GetFullPath($KnowledgeExecutable) }
if ($KnowledgeConfig) { Assert-RegularFile $KnowledgeConfig; $taskKnowledge.config=[IO.Path]::GetFullPath($KnowledgeConfig) }
$taskMcp = [ordered]@{ knowledge=$taskKnowledge; engineering=@{ entrypoint=[IO.Path]::GetFullPath($EngineeringEntrypoint) } }
Write-ManagedJson $McpSettingsPath $taskMcp
$taskNativeChat = Get-Content -LiteralPath $taskNativeChatPath -Raw | ConvertFrom-Json
$taskNativeChat | Add-Member -MemberType NoteProperty -Name McpSettingsPath -Value ([IO.Path]::GetFullPath($McpSettingsPath)) -Force
Write-ManagedJson $taskNativeChatPath $taskNativeChat
Write-Output 'Both native MCP servers are configured. Reconnect the Assistant to load them.'
if (-not $KnowledgeConfig) { Write-Output 'Knowledge tools will report an unavailable corpus until an activated corpus is configured.' }
Write-Output 'Engineering uses saved files from the PLC project selected in the IDE. No ADS/runtime backend or source writes are enabled.'
