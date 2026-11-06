param([Parameter(Mandatory=$true)][string]$HostDirectory,[Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference='Stop'
if (-not $env:TWINCAT_ASSISTANT_TEST_CONFIG) { throw 'An explicit private process configuration is required.' }
Add-Type -AssemblyName System.Web.Extensions,System.Security,System.Net.Http
$dll=Join-Path $HostDirectory 'TwinCatAssistant.IdeHost.dll'
$assembly=[Reflection.Assembly]::LoadFrom($dll)
$configuration=$assembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration',$true)
$marked=Test-Path -LiteralPath (Join-Path $HostDirectory 'assistant-test-maintenance.json')
$missingOverrideRefused=$false
if($marked) {
 $field=$configuration.GetField('MaintenanceMarkerFileName')
 if(-not $field -or $field.GetRawConstantValue() -cne 'assistant-test-maintenance.json') { throw 'The reviewed maintenance-capable native assembly is required.' }
 $privateOverride=$env:TWINCAT_ASSISTANT_TEST_CONFIG
 try {
  [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$null,'Process')
  try { $configuration.GetMethod('Load').Invoke($null,@()) | Out-Null } catch { $missingOverrideRefused=$true }
 } finally { [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$privateOverride,'Process') }
 if(-not $missingOverrideRefused) { throw 'Marked host accepted an unconfigured launch.' }
}
# Public Load resolves the selector beside this exact copied assembly. It must
# never read the operator configuration or use a development repository path.
$config=$configuration.GetMethod('Load').Invoke($null,@())
$profile=Split-Path -Parent $env:TWINCAT_ASSISTANT_TEST_CONFIG
if ($config.StateRoot -cne (Join-Path $profile 'state')) { throw 'Unexpected private StateRoot.' }
if ($configuration::CredentialDirectory -cne (Join-Path $profile 'native-chat')) { throw 'Unexpected credential directory.' }
if ($configuration::WebViewUserDataFolder -cne (Join-Path $profile ('WebView2\'+$PID))) { throw 'Unexpected WebView directory.' }
$sessionType=$assembly.GetType('TwinCatAssistant.Native.NativeChatSession',$true)
$start=$sessionType.GetMethod('StartAsync')
$machine=($start.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'AsyncStateMachineAttribute' }).StateMachineType
$move=$machine.GetMethod('MoveNext',[Reflection.BindingFlags]'Instance,NonPublic,Public')
$native80=[BitConverter]::ToString($move.GetMethodBody().GetILAsByteArray()).Contains('20-80-38-01-00')
if (-not $native80 -or [IntPtr]::Size -ne 4) { throw 'The real x86 80000ms native deadline is required.' }
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop()
$session=[Activator]::CreateInstance($sessionType)
$clock=[Diagnostics.Stopwatch]::StartNew()
$sha=[Security.Cryptography.SHA256]::Create()
try { $assemblyHash=[BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($dll))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
$result=@{success=$false;privateConfiguration=$true;maintenanceGuard=$marked;missingOverrideRefused=$missingOverrideRefused;x86=$true;compiledTimeoutMs=80000;assemblySha256=$assemblyHash;stateRoot=$config.StateRoot;applicationRoot=$config.RepositoryRoot;credentialDirectory=$configuration::CredentialDirectory;webViewDirectory=$configuration::WebViewUserDataFolder}
try {
 $session.StartAsync($config,$port,('synthetic-'+[guid]::NewGuid().ToString('N'))).GetAwaiter().GetResult() | Out-Null
 $result.startMs=$clock.ElapsedMilliseconds
 $result.protectedReady=$sessionType.GetMethod('IsProtectedServiceReadyAsync').Invoke($null,@($port)).GetAwaiter().GetResult()
 $result.success=$session.IsRunning -and $result.protectedReady -and $result.startMs -lt 80000
} catch { $result.startMs=$clock.ElapsedMilliseconds; $result.error=$_.Exception.ToString() }
finally {
 try { $session.StopAsync().GetAwaiter().GetResult() | Out-Null; $result.stopped=(-not $session.IsRunning) }
 finally { $session.Dispose(); $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputFile }
}
if (-not $result.success -or -not $result.stopped) { exit 1 }
