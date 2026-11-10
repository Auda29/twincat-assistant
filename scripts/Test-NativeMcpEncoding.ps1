param(
 [string]$HostAssembly=(Join-Path $PSScriptRoot '../native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'),
 [string]$NodePath=(Get-Command node.exe).Source,
 [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) ('local-data/mcp-encoding-'+[guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$originalEncoding=[Console]::OutputEncoding
$checks=@()
function Check([bool]$value,[string]$label){if(!$value){throw ('FAIL '+$label)};$script:checks+=$label;Write-Output ('PASS '+$label)}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$repository=Join-Path $OutputDirectory 'repository';$state=Join-Path $OutputDirectory 'state'
New-Item -ItemType Directory -Path (Join-Path $repository 'src'),(Join-Path $repository 'native/WindowsHost/bin/Release/net8.0-windows'),(Join-Path $state 'projects') -Force|Out-Null
# These two files satisfy metadata checks only; neither is executed.
[IO.File]::WriteAllText((Join-Path $repository 'src/native-chat.mjs'),'// Synthetic existence marker.')
[IO.File]::WriteAllText((Join-Path $repository 'native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe'),'Synthetic existence marker.')
$fixture='const chunks=[];for await(const b of process.stdin)chunks.push(b);const input=JSON.parse(Buffer.concat(chunks).toString("utf8"));const workspace=input.state+"\\projects\\synthetic";process.stdout.write(JSON.stringify({configured:true,workspace,workspaces:[workspace],project:input.project,solution:input.solution,source:"saved-project-files"})+"\n");'
[IO.File]::WriteAllText((Join-Path $repository 'src/native-mcp.mjs'),$fixture,[Text.UTF8Encoding]::new($false))
$mcp=Join-Path $OutputDirectory 'mcp.json';[IO.File]::WriteAllText($mcp,'{}')
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($HostAssembly))
$config=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration'))
$config.NodePath=$NodePath;$config.RepositoryRoot=$repository;$config.StateRoot=$state;$config.McpSettingsPath=$mcp
$config.DotNetRoot=Join-Path $env:ProgramFiles 'dotnet'
$solution=Join-Path $OutputDirectory 'Prüfung_ß\Anlage_ÄÖÜ.sln';$projectPath=Join-Path $OutputDirectory 'Prüfung_ß\PLC_äöü.plcproj'
$project=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeProjectIdentity'),@([int]1,[string]$solution,[string]$projectPath))
$prepare=$assembly.GetType('TwinCatAssistant.Native.NativeMcpProject').GetMethod('PrepareAsync',[Reflection.BindingFlags]'Static,NonPublic')
try{
 foreach($codePage in @(850,1252,65001)){
  [Console]::OutputEncoding=[Text.Encoding]::GetEncoding($codePage)
  $task=$prepare.Invoke($null,[object[]]@($config,$project,$null));$result=$task.GetAwaiter().GetResult()
  Check ($result.configured -and $result.project -ceq $projectPath -and $result.solution -ceq $solution) ('UTF-8 project paths survive caller code page '+$codePage)
 }
 Check (!(Test-Path -LiteralPath $solution) -and !(Test-Path -LiteralPath $projectPath)) 'no solution or PLC source was read or created'
 @{count=$checks.Count;checks=$checks;hostSha256=(Get-FileHash -LiteralPath $HostAssembly -Algorithm SHA256).Hash;usesSyntheticProtocolPeer=$true;ideStarted=$false;operatorStateUsed=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
}finally{[Console]::OutputEncoding=$originalEncoding}
