param([Parameter(Mandatory = $true)][string]$DotNetRoot)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path $PSScriptRoot -Parent
$taskRoot = Join-Path $taskRepo ('local-data\chat-checks-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskRoot) | Out-Null
Add-Type -AssemblyName System.Web.Extensions,System.Security,System.Net.Http
$taskAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $taskRepo 'native\IdeHost\X86\bin\Release\net472\TwinCatAssistant.IdeHost.dll'))
function Assert-Chat([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "Native chat check failed: $Name" }
    Write-Output "PASS $Name"
}
$taskCredentialPath = [string](Join-Path $taskRoot 'credential')
$taskCredentials = $taskAssembly.GetType('TwinCatAssistant.Native.NativeChatCredentials').GetConstructor([type[]]@([string])).Invoke([object[]]@($taskCredentialPath))
# Synthetic fixture value, never a user's credential or the installed profile.
$taskPassword = 'synthetic-private-' + [char]0x00E4 + [char]0x03A9 + [char]0xD83D + [char]0xDD12 + [Guid]::NewGuid().ToString('N')
$taskCredentials.Save($taskPassword)
Assert-Chat ($taskCredentials.Load() -eq $taskPassword) 'current-user protected credential round-trip'
$taskCipher = [IO.File]::ReadAllBytes((Join-Path $taskRoot 'credential\ui-password.bin'))
Assert-Chat (-not [Text.Encoding]::UTF8.GetString($taskCipher).Contains($taskPassword)) 'credential is not stored as plaintext'
$taskConfig = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration'))
$taskConfig.NodePath = (Get-Command node -ErrorAction Stop).Source
$taskConfig.RepositoryRoot = $taskRepo
$taskConfig.DotNetRoot = [IO.Path]::GetFullPath($DotNetRoot)
$taskConfig.StateRoot = Join-Path $taskRoot 'state'
$taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$taskListener.Start(); $taskPort = $taskListener.LocalEndpoint.Port; $taskListener.Stop()
$taskSession = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.NativeChatSession'))
try {
    $taskSession.StartAsync($taskConfig,$taskPort,$taskPassword).GetAwaiter().GetResult() | Out-Null
    Assert-Chat $taskSession.IsRunning 'real C# owner starts the pinned protected runtime'
    $taskReady = $taskSession.GetType().GetMethod('IsProtectedServiceReadyAsync').Invoke($null,[object[]]@($taskPort))
    Assert-Chat ($taskReady.GetAwaiter().GetResult()) 'C# readiness observes the protected service'
    $taskOrigin = 'http://127.0.0.1:' + $taskPort
    $taskLoginBody = [Text.Encoding]::UTF8.GetBytes((@{password=$taskPassword} | ConvertTo-Json -Compress))
    $taskLogin = Invoke-WebRequest -UseBasicParsing -Uri ($taskOrigin+'/auth/session') -Method Post -ContentType 'application/json; charset=utf-8' -Headers @{Origin=$taskOrigin} -Body $taskLoginBody
    Assert-Chat ($taskLogin.StatusCode -eq 200) 'non-ASCII password reaches the real login unchanged'
    $taskSession.StopAsync().GetAwaiter().GetResult() | Out-Null
    Assert-Chat (-not $taskSession.IsRunning) 'C# owner stop finishes'
    $taskReady = $taskSession.GetType().GetMethod('IsProtectedServiceReadyAsync').Invoke($null,[object[]]@($taskPort))
    Assert-Chat (-not $taskReady.GetAwaiter().GetResult()) 'owned HTTP listener stops with its C# owner'
    Write-Output 'Native chat transport/credential checks passed. No IDE or user profile was modified.'
} finally { $taskSession.Dispose(); $taskPassword = $null }
