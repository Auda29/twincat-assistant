param([string]$Root,[string]$State,[string]$Out)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions,System.Security,System.Net.Http
$dll=Join-Path $Root 'versions\0.0.0-scope-synthetic\application\host\x86\TwinCatAssistant.IdeHost.dll'
$a=[Reflection.Assembly]::LoadFrom($dll)
$t=$a.GetType('TwinCatAssistant.Native.NativeChatConfiguration',$true)
$m=$t.GetMethod('LoadFrom',[Reflection.BindingFlags]'Static,NonPublic')
$hostDir=[string](Join-Path $Root 'host')
$configFile=[string](Join-Path $Root 'config.json')
$c=$m.Invoke($null,[object[]]@($hostDir,$configFile,[string]$Root,[string]'C:\Program Files'))
$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$l.Start(); $port=$l.LocalEndpoint.Port; $l.Stop()
$startMethod=$a.GetType('TwinCatAssistant.Native.NativeChatSession').GetMethod('StartAsync')
$machine=($startMethod.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'AsyncStateMachineAttribute' }).StateMachineType
$move=$machine.GetMethod('MoveNext',[Reflection.BindingFlags]'Instance,NonPublic,Public')
$literal80=[BitConverter]::ToString($move.GetMethodBody().GetILAsByteArray()).Contains('20-80-38-01-00')
if (-not $literal80) { throw 'Compiled StartAsync no longer contains the 80000ms timeout' }
$s=[Activator]::CreateInstance($a.GetType('TwinCatAssistant.Native.NativeChatSession'))
$watch=[Diagnostics.Stopwatch]::StartNew()
$result=@{state=$State; timeoutMs=80000; success=$false; x86=([IntPtr]::Size -eq 4); compiledTimeout80=$literal80}
try {
 $s.StartAsync($c,$port,'synthetic-benchmark-password-123').GetAwaiter().GetResult() | Out-Null
 $result.startMs=$watch.ElapsedMilliseconds
 $result.protectedReady=$s.GetType().GetMethod('IsProtectedServiceReadyAsync').Invoke($null,@($port)).GetAwaiter().GetResult()
 $result.success=$s.IsRunning -and $result.protectedReady
} catch { $result.startMs=$watch.ElapsedMilliseconds; $result.error=$_.Exception.ToString() }
finally { $s.StopAsync().GetAwaiter().GetResult() | Out-Null; $result.stopped=(-not $s.IsRunning); $s.Dispose() }
$result | ConvertTo-Json -Depth 5 | Set-Content $Out
$result | ConvertTo-Json -Depth 5
if (-not $result.success) { exit 1 }
