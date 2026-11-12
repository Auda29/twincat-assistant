param(
 [Parameter(Mandatory=$true)][string]$FixtureDirectory,
 [string]$HostAssembly,
 [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Use Windows PowerShell -STA.'}
$root=Split-Path $PSScriptRoot -Parent
if(!$HostAssembly){$HostAssembly=Join-Path $root 'native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('local-data/startup-view-'+[guid]::NewGuid().ToString('N'))}
# Standalone synthetic fixture; no IDE attach or model request.
$FixtureDirectory=[IO.Path]::GetFullPath($FixtureDirectory)
$fixtureParent=[IO.Path]::GetFullPath((Join-Path $root 'local-data'))
if((Split-Path $FixtureDirectory -Parent) -cne $fixtureParent -or (Split-Path $FixtureDirectory -Leaf) -cnotmatch '^chat-startup-smoke-[a-zA-Z0-9-]+$'){throw 'Use an isolated startup fixture below local-data.'}
$fixtureConnection=Get-Content (Join-Path $FixtureDirectory 'connection.json') -Raw -Encoding UTF8|ConvertFrom-Json
$fixtureUri=[Uri]$fixtureConnection.origin
if($fixtureConnection.password -cne 'synthetic-product-ui-only-not-an-operator-credential' -or $fixtureUri.Scheme -cne 'http' -or $fixtureUri.Host -cne '127.0.0.1' -or $fixtureUri.Port -lt 1024 -or $fixtureUri.UserInfo -or $fixtureUri.AbsolutePath -cne '/' -or $fixtureUri.Query -or $fixtureUri.Fragment){throw 'Only the synthetic startup backend is accepted.'}
$HostAssembly=[IO.Path]::GetFullPath($HostAssembly)
$bin=Split-Path $HostAssembly -Parent
$oldOverride=$env:TWINCAT_ASSISTANT_TEST_CONFIG
$control=$null
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$value,[string]$label){if(!$value){throw ('FAIL '+$label)};$checks.Add($label);Write-Output ('PASS '+$label)}
$profileInfo=& (Join-Path $PSScriptRoot 'New-PrivateHostAcceptanceProfile.ps1') -ProfileId ('startup-' +[guid]::NewGuid().ToString('N')) | ConvertFrom-Json
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
function Field([string]$name){return ,($control.GetType().GetField($name,$flags).GetValue($control))}
function Make-Control {
 $script:control=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.AssistantControl'))
 $control.GetType().GetField('_autoConnectAttempted',$flags).SetValue($control,$true)
 (Field '_chatHealth').Stop();(Field '_automationKick').Stop();(Field '_chatReconnect').Stop()
}
function Put([string]$name,$value){$control.GetType().GetField($name,$flags).SetValue($control,$value)}
function Call([string]$name){$control.GetType().GetMethod($name,$flags).Invoke($control,@())}Add-Type -TypeDefinition @"
using System; using System.Threading; using System.Threading.Tasks; using System.Windows.Threading;
public static class StartupPump {
 public static int ConfigurationLoads, ConfigurationThread;
 static readonly ManualResetEventSlim ConfigurationGate = new ManualResetEventSlim(false);
 public static object LoadConfiguration(object value) {Interlocked.Increment(ref ConfigurationLoads);ConfigurationThread=Thread.CurrentThread.ManagedThreadId;ConfigurationGate.Wait();return value;}
 public static void ReleaseConfiguration() {ConfigurationGate.Set();}
 public static void ResetConfiguration() {ConfigurationGate.Reset();}
 public static Delegate ConfigurationFactory(Type type,object value) {return System.Linq.Expressions.Expression.Lambda(typeof(Func<>).MakeGenericType(type),System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Call(typeof(StartupPump).GetMethod("LoadConfiguration"),System.Linq.Expressions.Expression.Constant(value)),type)).Compile();}
 public static Delegate InvalidConfigurationFactory(Type type) {return System.Linq.Expressions.Expression.Lambda(typeof(Func<>).MakeGenericType(type),System.Linq.Expressions.Expression.Throw(System.Linq.Expressions.Expression.New(typeof(InvalidOperationException)),type)).Compile();}
 public static object Await(Task t) {var until=DateTime.UtcNow.AddSeconds(90);while(!t.IsCompleted){if(DateTime.UtcNow>until)throw new TimeoutException("Fixture await timed out"); var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(10);}t.GetAwaiter().GetResult();return t.GetType().IsGenericType?t.GetType().GetProperty("Result").GetValue(t,null):null;}
 public static Func<string> Constant(string s){return ()=>s;}
 public static Delegate ConstantObject(Type type, object value){return System.Linq.Expressions.Expression.Lambda(typeof(Func<>).MakeGenericType(type),System.Linq.Expressions.Expression.Constant(value,type)).Compile();}
}
"@ -ReferencedAssemblies WindowsBase,System.Core
function Await($task){[StartupPump]::Await($task)}
function Invoke-Private([string]$name,[object[]]$arguments=@()){$control.GetType().GetMethod($name,$flags).Invoke($control,$arguments)}
$window=$null
try{
 $assembly=[Reflection.Assembly]::LoadFrom($HostAssembly)
 $shell=[Reflection.Assembly]::LoadFrom($global:projectFixtureReferences['Microsoft.VisualStudio.Shell.15.0'])
 $thread=$shell.GetType('Microsoft.VisualStudio.Shell.ThreadHelper');$thread.GetMethod('SetUIThread',[Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null,@())
 $contextType=$thread.GetField('_joinableTaskContextCache',[Reflection.BindingFlags]'Static,NonPublic').FieldType
 $sync=New-Object Windows.Threading.DispatcherSynchronizationContext([Windows.Threading.Dispatcher]::CurrentDispatcher)
 [Threading.SynchronizationContext]::SetSynchronizationContext($sync)
 $context=[Activator]::CreateInstance($contextType)
 $thread.GetField('_joinableTaskContextCache',[Reflection.BindingFlags]'Static,NonPublic').SetValue($null,$context)
 Make-Control
 $configuration=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration'))
 $configuration.StateRoot=$profileInfo.StateRoot
 Put '_chatConfiguration' ([StartupPump]::ConfigurationFactory($configuration.GetType(),$configuration))
 $first=Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($false))
 $second=Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($false))
 Check (!$first.IsCompleted -and !$second.IsCompleted) 'configuration verification yields while its worker is pending'
 [Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Action]{[StartupPump]::ReleaseConfiguration()})|Out-Null
 $null=Await $first;$null=Await $second
 Check ([StartupPump]::ConfigurationLoads -eq 1 -and [StartupPump]::ConfigurationThread -ne [Threading.Thread]::CurrentThread.ManagedThreadId) 'parallel configuration requests share one background load and the dispatcher remains responsive'
 $defaultStore=Field '_chatBindings'
 $stateProperty=$defaultStore.GetType().GetProperty('StateRoot',$flags)
 $hotClock=[Diagnostics.Stopwatch]::StartNew()
 1..100|ForEach-Object {
  $null=Await (Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($false)))
  if($stateProperty.GetValue($defaultStore,$null) -cne $profileInfo.StateRoot){throw 'Default binding store changed state root'}
  if((Field '_chatWorkspace').Invoke() -cne (Join-Path $profileInfo.StateRoot 'workspace')){throw 'Default workspace changed state root'}
 }
 $hotClock.Stop()
 Check ([StartupPump]::ConfigurationLoads -eq 1) '100 configuration, binding-root and workspace lookups do not reload or hash the package'
 Write-Output ('Cached native lookups (100 iterations): '+[Math]::Round($hotClock.Elapsed.TotalMilliseconds,1)+' ms')
 $null=Await (Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($true)))
 Check ([StartupPump]::ConfigurationLoads -eq 2) 'explicit reconnect reloads and verifies configuration'
 $configurationFactory=Field '_chatConfiguration'
 Put '_chatConfiguration' ([StartupPump]::InvalidConfigurationFactory($configuration.GetType()))
 $refused=$false;$oldSnapshotRefused=$false
 try{$null=Await (Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($true)))}catch{$refused=$true}
 try{$null=Invoke-Private 'ConnectedChatConfiguration'}catch{$oldSnapshotRefused=$true}
 Check ($refused -and $oldSnapshotRefused) 'a failed refreshed configuration cannot fall back to the previous connection snapshot'
 Put '_chatConfiguration' $configurationFactory
 $null=Await (Invoke-Private 'GetChatConfigurationAsync' ([object[]]@($true)))
 $workspace=Join-Path $FixtureDirectory 'state/workspace'
 Put '_chatWorkspace' ([StartupPump]::Constant($workspace))
 Put '_automating' $true
 $bindingType=$assembly.GetType('TwinCatAssistant.Native.ChatBindingStore')
 $binding=[Activator]::CreateInstance($bindingType,[object[]]@([string](Join-Path $profileInfo.CredentialDirectory 'threads'),[string](Split-Path $workspace -Parent)))
 Put '_chatBindings' $binding
 $connectionInfo=Get-Content (Join-Path $FixtureDirectory 'connection.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $origin=$connectionInfo.origin;Put '_origin' ([Uri]$origin)
 $window=New-Object Windows.Window;$window.Width=820;$window.Height=550;$window.Opacity=0.02;$window.ShowActivated=$false;$window.ShowInTaskbar=$false;$window.Content=$control;$window.Show()
 $browser=Field '_browser'
 $webType=[Microsoft.Web.WebView2.Core.CoreWebView2Environment]
 $environment=Await ($webType::CreateAsync($null,(Join-Path (Split-Path $profileInfo.ConfigurationPath -Parent) ('WebView2/'+$PID)),$null))
 Await ($browser.EnsureCoreWebView2Async($environment))|Out-Null
 $core=$browser.CoreWebView2
 (Field '_chatCredentials').Save('synthetic-product-ui-only-not-an-operator-credential')
 Check ((Await(Invoke-Private 'SignInBrowserAsync' ([object[]]@([int]([Uri]$origin).Port)))) -eq $true) 'synthetic native login accepted before navigation'
 $core.Navigate($origin)
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do{Await ([Threading.Tasks.Task]::Delay(100))|Out-Null;$ok=Await($core.ExecuteScriptAsync('!!window.__twinCatSource'))}while($ok -ne 'true' -and [DateTime]::UtcNow -lt $deadline)
 Check ($ok -eq 'true') 'source contract loaded'
 $bundle=[IO.File]::ReadAllText((Join-Path $root 'native/IdeHost/obj/EmbeddedChat.js'))
 Await ($core.ExecuteScriptAsync($bundle+';window.__fixtureInstall=false;TwinCatEmbedded.installEmbeddedChat().then(()=>window.__fixtureInstall=true);'))|Out-Null
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do{Await ([Threading.Tasks.Task]::Delay(100))|Out-Null;$ok=Await($core.ExecuteScriptAsync('window.__fixtureInstall && window.__twinCatChat.state().authenticated'))}while($ok -ne 'true' -and [DateTime]::UtcNow -lt $deadline)
 Check ($ok -eq 'true') 'synthetic local chat authenticated'
 $connection=Field '_chatConnection';$generation=$connection.Begin();$connection.Navigating([UInt64]1);$connection.Completed([UInt64]1,$true,200,$null)
 Put '_chatAdapterReady' $true
 $selection=Await (Invoke-Private 'ReadChatSelectionAsync');Put '_chatSelection' $selection
 Await ($core.ExecuteScriptAsync('window.__fixtureHoldMount=true;const originalQuery=document.querySelector.bind(document);document.querySelector=function(s){return window.__fixtureHoldMount && s.replace(/"/g,"")==="[data-testid=chat-input],.oc-draft-center"?null:originalQuery(s);};'))|Out-Null
 Await (Invoke-Private 'NewChatAsync' ([object[]]@($true)))|Out-Null
 Check ((Field '_chatBinding').IsGeneral -eq $true) 'valid chat metadata binds before the composer mounts'
 Check (!(Field '_chatPresented') -and !(Field '_browser').IsHitTestVisible -and !(Field '_browser').Focusable) 'rendering stays covered and input stays disabled until presentation is ready'
 Await ($core.ExecuteScriptAsync('window.__fixtureHoldMount=false'))|Out-Null


 $deadline=[DateTime]::UtcNow.AddSeconds(12)
 do{Await ([Threading.Tasks.Task]::Delay(150))|Out-Null;Await(Invoke-Private 'PollChatSelectionStateAsync')|Out-Null}while(!(Field '_chatPresented') -and [DateTime]::UtcNow -lt $deadline)
 Check ((Field '_chatPresented') -eq $true) 'actual WebView creates, binds and presents a general chat'
 Check ((Field '_bannerText').Text -ne 'Zuerst eine Unterhaltung öffnen.') 'no false missing conversation error'
 $project=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeProjectIdentity'),@([int]$PID,'C:/Synthetic/Startup.sln','C:/Synthetic/Startup.plcproj'))
 $projectReader=[StartupPump]::ConstantObject($project.GetType(),$project)
 Put '_chatProject' $project;Put '_chatProjectReader' $projectReader;Put '_preparedProject' $project
 Put '_projectWorkspace' $workspace;Put '_projectWorkspaces' ([string[]]@($workspace));Put '_preparedMcpSettings' $null
 $config=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration'))
 $config.StateRoot=$profileInfo.StateRoot;$config.DotNetRoot=Join-Path $env:ProgramFiles 'dotnet'
 Put '_chatConfiguration' ([StartupPump]::ConstantObject($config.GetType(),$config))
 Put '_knowledgeChecked' ([DateTime]::UtcNow);Put '_autoProjectKey' $null;Put '_explicitGeneralRequested' $false
 (Field '_consentAttempted').Add('openai')|Out-Null
 Put '_chatPresented' $false;Put '_automating' $false
 Await ($core.ExecuteScriptAsync('window.__fixtureHoldMount=true'))|Out-Null
 Await (Invoke-Private 'RunAutomationAsync')|Out-Null
 Put '_automating' $true
 Check ((Invoke-Private 'HasCurrentChatBinding') -eq $true -and !(Field '_chatPresented')) 'project identity binds while its composer is still mounting'
 Await ($core.ExecuteScriptAsync('window.__fixtureHoldMount=false'))|Out-Null
 $deadline=[DateTime]::UtcNow.AddSeconds(12)
 do{Await ([Threading.Tasks.Task]::Delay(150))|Out-Null;Await(Invoke-Private 'PollChatSelectionStateAsync')|Out-Null}while(!(Field '_chatPresented') -and [DateTime]::UtcNow -lt $deadline)
 Check ((Invoke-Private 'HasCurrentChatBinding') -eq $true) 'automatic project startup binds its confirmed conversation'
 Check ((Field '_chatPresented') -eq $true) 'automatic project startup reaches the visible chat'
 $selected=(Field '_chatSelection').sessionId
 Put '_automating' $false;Await(Invoke-Private 'RunAutomationAsync')|Out-Null;Put '_automating' $true
 Check ((Field '_chatSelection').sessionId -ceq $selected) 'a later automation pass does not create another chat'
 Check ($null -eq (Field '_reader') -and $null -eq (Field '_chatSession')) 'no IDE or operator backend was used'
 # Exercise the real project callback boundary without a provider or IDE.
 # Ordinary tracker invalidation must retain observation state and session epoch.
 $operationGuard=$control.GetType().GetField('EngineeringBuildGuard',$flags -bor [Reflection.BindingFlags]::Static).GetValue($null)
 $guardType=$operationGuard.GetType()
 $buildId=[Guid]::NewGuid()
 Check ($guardType.GetMethod('TryBegin',$flags).Invoke($operationGuard,@($buildId))) 'synthetic operation reserves the process-wide engineering guard'
 try {
  Put '_engineeringProject' $project;Put '_engineeringBusy' $true;Put '_engineeringUncertain' $true;Put '_engineeringBuildId' $buildId
  $epoch=Field '_engineeringEpoch'
  Invoke-Private 'RenderEngineeringProject' @($null,$true)|Out-Null
  Check ((Field '_engineeringEpoch') -eq $epoch -and (Field '_engineeringProject').Equals($project) -and (Field '_engineeringBusy') -and (Field '_engineeringBuildId') -eq $buildId) 'transient editor invalidation preserves engineering observation and epoch'
  Invoke-Private 'RenderEngineeringProject' @($project,$false)|Out-Null
  Check ((Field '_engineeringEpoch') -eq $epoch -and (Field '_engineeringUncertain') -and (Field '_engineeringBuildId') -eq $buildId) 'same-project resolution retains unresolved operation state'
  Call 'DisposeEngineering'|Out-Null
  Check ((Field '_engineeringUncertain') -and (Field '_engineeringBuildId') -eq $buildId -and $guardType.GetProperty('HasUnresolved',$flags).GetValue($operationGuard,$null)) 'actual lifecycle revocation cannot erase an unresolved engineering operation'
 } finally { $guardType.GetMethod('Reconcile',$flags).Invoke($operationGuard,@($buildId))|Out-Null }
 [pscustomobject]@{passed=$checks.Count;checks=@($checks);hostSha256=(Get-FileHash -LiteralPath $HostAssembly -Algorithm SHA256).Hash.ToLowerInvariant();installedIdeAcceptance=$false}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
}finally{if($control){$control.Dispose()};if($window){$window.Close()};[AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver);$env:TWINCAT_ASSISTANT_TEST_CONFIG=$oldOverride}
