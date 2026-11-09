param(
 [string]$HostAssembly=(Join-Path $PSScriptRoot '../native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'),
 [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) ('local-data/project-setup-view-'+[guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Use Windows PowerShell -STA.'}
$root=Split-Path $PSScriptRoot -Parent
$HostAssembly=[IO.Path]::GetFullPath($HostAssembly)
$bin=Split-Path $HostAssembly -Parent
$oldOverride=$env:TWINCAT_ASSISTANT_TEST_CONFIG
$control=$null
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$value,[string]$label){if(!$value){throw ('FAIL '+$label)};$checks.Add($label);Write-Output ('PASS '+$label)}
$profileInfo=& (Join-Path $PSScriptRoot 'New-PrivateHostAcceptanceProfile.ps1') -ProfileId ('project-failure-'+[guid]::NewGuid().ToString('N')) | ConvertFrom-Json
$env:TWINCAT_ASSISTANT_TEST_CONFIG=$profileInfo.ConfigurationPath
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$assets=Get-Content (Join-Path $root 'native/IdeHost/X86/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoot=@($assets.packageFolders.PSObject.Properties)[0].Name
$global:projectFixtureReferences=@{}
foreach($package in @($assets.targets.PSObject.Properties)[0].Value.PSObject.Properties){
 foreach($reference in $package.Value.compile.PSObject.Properties){if($reference.Name.EndsWith('.dll')){
  $path=Join-Path $packageRoot ($package.Name.ToLowerInvariant()+'/'+$reference.Name)
  $global:projectFixtureReferences[[IO.Path]::GetFileNameWithoutExtension($path)]=$path
 }}
}
foreach($dll in Get-ChildItem -LiteralPath $bin -Filter *.dll){$global:projectFixtureReferences[$dll.BaseName]=$dll.FullName}
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
 $name=(New-Object Reflection.AssemblyName($eventArgs.Name)).Name
 if($global:projectFixtureReferences.ContainsKey($name)){return [Reflection.Assembly]::LoadFrom($global:projectFixtureReferences[$name])};return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Field([string]$name){$control.GetType().GetField($name,$flags).GetValue($control)}
function Make-Control {
 $script:control=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.AssistantControl'))
 $control.GetType().GetField('_autoConnectAttempted',$flags).SetValue($control,$true)
 (Field '_chatHealth').Stop();(Field '_automationKick').Stop();(Field '_chatReconnect').Stop()
}
function Put([string]$name,$value){$control.GetType().GetField($name,$flags).SetValue($control,$value)}
function Call([string]$name){$control.GetType().GetMethod($name,$flags).Invoke($control,@())}
try{
 $assembly=[Reflection.Assembly]::LoadFrom($HostAssembly)
 $shell=[Reflection.Assembly]::LoadFrom($global:projectFixtureReferences['Microsoft.VisualStudio.Shell.15.0'])
 $shell.GetType('Microsoft.VisualStudio.Shell.ThreadHelper').GetMethod('SetUIThread',[Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null,@())
 Make-Control
 $project=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeProjectIdentity'),@([int]1,'C:/Synthetic/Machine.sln','C:/Synthetic/PLC.plcproj'))
 Put '_chatProject' $project
 $connection=Field '_chatConnection';$generation=$connection.Begin();$connection.Navigating([UInt64]1);$connection.Completed([UInt64]1,$true,200,$null)
 $selection=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.ChatSelection'))
 $selection.authenticated=$true;$selection.presentationReady=$true;Put '_chatSelection' $selection;Put '_chatAdapterReady' $true
 $key=$control.GetType().GetMethod('ProjectKey',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@($project))
 Put '_mcpFailureKey' ($key+[char]0+'synthetic-config'+[char]0)
 Put '_mcpFailureMessage' 'Synthetic project setup is locked.'
 Call 'RenderChatBinding'
 Check ((Field '_splash').Visibility -eq [Windows.Visibility]::Collapsed) 'project setup failure ends the loading splash'
 Check ((Field '_strip').Visibility -eq [Windows.Visibility]::Visible) 'native actions remain reachable'
 Check ((Field '_bannerText').Text -eq 'Synthetic project setup is locked.') 'setup failure remains actionable'
 Check (!(Field '_bannerDismiss').IsVisible) 'setup failure cannot disappear while its block remains'
 Check ((Field '_toolsRow').Value.Text -eq 'Einrichtung fehlgeschlagen') 'diagnostic names project setup failure'
 Check ((Field '_knowledgeRow').Value.Text -eq 'Projektchat nicht eingerichtet') 'corpus readiness does not imply MCP availability'
 $control.GetType().GetMethod('OnChatReadinessTimeout',$flags).Invoke($control,[object[]]@($null,[EventArgs]::Empty))
 Check ($connection.Phase -eq 'loaded') 'project failure does not become a disconnected chat'
 Put '_mcpFailureKey' $null;Put '_projectSetupPending' $true
 $control.GetType().GetMethod('OnChatReadinessTimeout',$flags).Invoke($control,[object[]]@($null,[EventArgs]::Empty))
 Check ($connection.Phase -eq 'loaded') 'pending project setup does not become a disconnected chat'
 Check ($null -eq (Field '_reader') -and $null -eq (Field '_chatSession')) 'no IDE reader or backend was started'
 @{checks=@($checks);count=$checks.Count;hostSha256=(Get-FileHash $HostAssembly -Algorithm SHA256).Hash;ideStarted=$false;backendStarted=$false;operatorStateUsed=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
 Write-Output ('Evidence: '+$OutputDirectory)
}finally{
 if($control){$control.Dispose()}
 [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
 $env:TWINCAT_ASSISTANT_TEST_CONFIG=$oldOverride
}
