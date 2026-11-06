param([string]$HostAssembly = (Join-Path $PSScriptRoot '../native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions,System.Security,System.Net.Http
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($HostAssembly))
$type = $assembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration', $true)
$load = $type.GetMethod('LoadFrom', [Reflection.BindingFlags]'Static,NonPublic')
$variable = 'TWINCAT_ASSISTANT_TEST_CONFIG'
$old = [Environment]::GetEnvironmentVariable($variable, 'Process')
$local = [Environment]::GetFolderPath('LocalApplicationData')
$profile = Join-Path $local ('TwinCatAssistant\host-tests\config-check-' + [guid]::NewGuid().ToString('N'))
$configPath = Join-Path $profile 'native-chat.json'
$run = Join-Path (Split-Path $PSScriptRoot -Parent) ('local-data\private-host-config-' + [guid]::NewGuid().ToString('N'))
$hostDirectory = Join-Path $run 'host'
$package = Join-Path $run 'package'
$programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { [Environment]::GetFolderPath('ProgramFiles') }
$dotnet = Join-Path $programFiles 'dotnet'
$checks = [Collections.Generic.List[string]]::new()
function Assert([bool]$ok, [string]$name) { if (!$ok) { throw "FAIL $name" }; $checks.Add($name); Write-Output "PASS $name" }
function Reject([scriptblock]$action, [string]$name) { $rejected=$false; try { & $action | Out-Null } catch { $rejected=$true }; Assert $rejected $name }
function Load-Config { $load.Invoke($null, [object[]]@([string]$hostDirectory,[string]$configPath,[string]$local,[string]$programFiles)) }
function Write-Config($fields) { [IO.File]::WriteAllText($configPath, ($fields | ConvertTo-Json -Compress)) }
New-Item -ItemType Directory -Force $profile,$hostDirectory,$package | Out-Null
try {
    [Environment]::SetEnvironmentVariable($variable,$null,'Process')
    Assert ($type::ConfigurationPath -eq (Join-Path $local 'TwinCatAssistant\native-chat.json')) 'default configuration path unchanged'
    Assert ($type::CredentialDirectory -eq (Join-Path $local 'TwinCatAssistant\native-chat')) 'default credential/preferences/bindings directory unchanged'
    Assert ($type::WebViewUserDataFolder -eq (Join-Path $local ('TwinCatAssistant\WebView2\' + $PID))) 'default WebView path unchanged'
    # Pure synthetic selector/package. These files are never executed.
    $names = @('runtime/node/node.exe','runtime/mcp/default-settings.json','runtime/mcp/knowledge/twincat-knowledge.exe','runtime/mcp/engineering/packages/mcp/dist/index.js','src/native-chat.mjs','src/native-mcp.mjs','src/native-knowledge.mjs','src/native-chat-model.mjs','src/package-runtime.json','src/package-integrity.mjs','scripts/application-manifest.mjs','native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe')
    $entries = foreach ($name in $names) {
        $file=Join-Path $package $name
        New-Item -ItemType Directory -Force (Split-Path $file -Parent) | Out-Null
        [IO.File]::WriteAllText($file, 'synthetic configuration fixture only')
        @{path=$name; size=(Get-Item -LiteralPath $file).Length; sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    [IO.File]::WriteAllText((Join-Path $package 'application-manifest.json'), (@{schemaVersion=1;version='private-config-fixture';platform='win32';architecture='ia32';files=@($entries)} | ConvertTo-Json -Depth 5))
    [IO.File]::WriteAllText((Join-Path $hostDirectory 'assistant-installation.json'), (@{schemaVersion=1;targetId='tcxaeshell-x86-tc4024';version='private-config-fixture';applicationRoot=$package} | ConvertTo-Json))
    $valid = @{StateRoot=(Join-Path $profile 'state');DotNetRoot=$dotnet}
    Write-Config $valid
    [Environment]::SetEnvironmentVariable($variable,$configPath,'Process')
    foreach ($key in @('WEBVIEW2_USER_DATA_FOLDER','WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS')) {
        $before=[Environment]::GetEnvironmentVariable($key,'Process')
        try {
            [Environment]::SetEnvironmentVariable($key,$run,'Process')
            Reject { $type.GetProperty('ConfigurationPath').GetValue($null, $null) } ('reject inherited browser override '+$key)
        } finally { [Environment]::SetEnvironmentVariable($key,$before,'Process') }
    }
    Assert ($type::ConfigurationPath -eq $configPath) 'explicit process override selects private config'
    Assert ($type::CredentialDirectory -eq (Join-Path $profile 'native-chat')) 'private credentials preferences and bindings'
    Assert ($type::WebViewUserDataFolder -eq (Join-Path $profile ('WebView2\' + $PID))) 'private WebView cookies and browser state'
    $config = Load-Config
    Assert ($config.StateRoot -eq $valid.StateRoot) 'exact private backend NativeMCP and Knowledge StateRoot'
    Assert ($config.RepositoryRoot -eq $package -and $config.NodePath -eq (Join-Path $package 'runtime\node\node.exe')) 'installer selector retains package ownership'
    Assert ($config.McpSettingsPath -eq (Join-Path $package 'runtime\mcp\default-settings.json')) 'installer package MCP settings selected'
    Assert ($config.DotNetRoot -eq $dotnet) 'system dotnet selected'
    [Environment]::SetEnvironmentVariable($variable,$null,'Process')
    $legacyState=Join-Path $run 'legacy-state'
    Write-Config @{StateRoot=$legacyState;DotNetRoot=$dotnet;NodePath='ignored-by-selector';RepositoryRoot='ignored-by-selector'}
    $legacy=Load-Config
    Assert ($legacy.StateRoot -eq $legacyState -and $legacy.RepositoryRoot -eq $package) 'default legacy config preserves state and installer selection'
    Remove-Item -LiteralPath $configPath
    $legacy=Load-Config
    Assert ($legacy.StateRoot -eq (Join-Path $local 'TwinCatAssistant\operator-state')) 'default missing config retains installed operator-state fallback'
    Write-Config $valid
    [Environment]::SetEnvironmentVariable($variable,$configPath,'Process')
    foreach ($bad in @('relative.json', (Join-Path $local 'TwinCatAssistant\native-chat.json'), (Join-Path $profile '..\native-chat.json'), (Join-Path $profile 'other.json'))) {
        [Environment]::SetEnvironmentVariable($variable,$bad,'Process')
        Reject { $type.GetProperty('CredentialDirectory').GetValue($null, $null) } ('reject foreign/relative/noncanonical override ' + $bad)
    }
    [Environment]::SetEnvironmentVariable($variable,$configPath,'Process')
    Write-Config @{StateRoot=(Join-Path $run 'foreign-state');DotNetRoot=$dotnet}
    Reject { Load-Config } 'reject foreign StateRoot'
    Write-Config @{StateRoot=$valid.StateRoot;DotNetRoot=$run}
    Reject { Load-Config } 'reject non-system dotnet'
    Write-Config @{StateRoot=$valid.StateRoot;DotNetRoot=$dotnet;RepositoryRoot=$run}
    Reject { Load-Config } 'reject package override fields'
    Write-Config $valid
    $selector=Join-Path $hostDirectory 'assistant-installation.json'
    Move-Item -LiteralPath $selector -Destination ($selector+'.saved')
    Reject { Load-Config } 'reject missing selector without global fallback'
    Move-Item -LiteralPath ($selector+'.saved') -Destination $selector
    $credentialDir=Join-Path $profile 'native-chat'
    $foreign=Join-Path $run 'foreign'
    New-Item -ItemType Directory $foreign | Out-Null
    New-Item -ItemType Junction -Path $credentialDir -Target $foreign | Out-Null
    Reject { $type.GetProperty('CredentialDirectory').GetValue($null, $null) } 'reject redirected credential directory before use'
    # Delete only the verified junction itself. Never recursively follow it.
    [IO.Directory]::Delete($credentialDir)
    New-Item -ItemType Directory $credentialDir | Out-Null
    $sentinel=Join-Path $foreign 'sentinel'
    [IO.File]::WriteAllText($sentinel,'synthetic foreign sentinel')
    $link=Join-Path $credentialDir 'preferences.json'
    New-Item -ItemType HardLink -Path $link -Target $sentinel | Out-Null
    Reject { $type.GetProperty('CredentialDirectory').GetValue($null, $null) } 'reject hardlinked private preferences'
    Remove-Item -LiteralPath $link
    $state=Join-Path $profile 'state'
    New-Item -ItemType Junction -Path $state -Target $foreign | Out-Null
    Reject { Load-Config } 'reject redirected NativeMCP and Knowledge state'
    [IO.Directory]::Delete($state)
    $webview=Join-Path $profile 'WebView2'
    New-Item -ItemType Junction -Path $webview -Target $foreign | Out-Null
    Reject { $type.GetProperty('WebViewUserDataFolder').GetValue($null, $null) } 'reject redirected WebView cookies directory'
    [IO.Directory]::Delete($webview)
    Assert ([IO.File]::ReadAllText($sentinel) -eq 'synthetic foreign sentinel') 'foreign synthetic sentinel unchanged'
    $original=Join-Path $profile 'config-original.json'
    Move-Item -LiteralPath $configPath -Destination $original
    New-Item -ItemType HardLink -Path $configPath -Target $original | Out-Null
    Reject { $type.GetProperty('ConfigurationPath').GetValue($null, $null) } 'reject hardlinked config'
    Remove-Item -LiteralPath $configPath
    Move-Item -LiteralPath $original -Destination $configPath
    Remove-Item -LiteralPath $configPath
    Reject { $type.GetProperty('ConfigurationPath').GetValue($null, $null) } 'reject missing private config without fallback'
    @{checks=@($checks);count=$checks.Count;hostAssembly=[IO.Path]::GetFullPath($HostAssembly);hostSha256=(Get-FileHash -LiteralPath $HostAssembly).Hash;ideStarted=$false;backendStarted=$false;globalConfigWritten=$false} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'result.json') -Encoding UTF8
    Write-Output "Evidence: $run\result.json"
} finally { [Environment]::SetEnvironmentVariable($variable,$old,'Process') }



