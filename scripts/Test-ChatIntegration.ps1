param([Parameter(Mandatory=$true)][string]$DotNetRoot)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell.exe -NoProfile -STA.' }
# Actual built host + WebView2 + pinned runtime. Only the editor snapshots and
# private paths are synthetic. No running IDE, provider credential or model call.
$taskRepo = Split-Path $PSScriptRoot -Parent
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('tc-chat-integration-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskRoot) | Out-Null
$taskBin = Join-Path $taskRepo 'native/IdeHost/X86/bin/Release/net472'
$taskAssets = Get-Content (Join-Path $taskRepo 'native/IdeHost/X86/obj/project.assets.json') -Raw | ConvertFrom-Json
$taskPackageRoot = @($taskAssets.packageFolders.PSObject.Properties)[0].Name
$global:taskIntegrationRefs = @{}
foreach ($taskPackage in (@($taskAssets.targets.PSObject.Properties)[0].Value).PSObject.Properties) {
    foreach ($taskReference in $taskPackage.Value.compile.PSObject.Properties) {
        if ($taskReference.Name.EndsWith('.dll')) {
            $taskPath = Join-Path $taskPackageRoot ($taskPackage.Name.ToLowerInvariant() + '/' + $taskReference.Name)
            $global:taskIntegrationRefs[[IO.Path]::GetFileNameWithoutExtension($taskPath)] = $taskPath
        }
    }
}
foreach ($taskDll in Get-ChildItem $taskBin -Filter '*.dll') { $global:taskIntegrationRefs[$taskDll.BaseName] = $taskDll.FullName }
$taskResolver = [ResolveEventHandler] { param($sender,$eventArgs)
    $taskName = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name
    if ($global:taskIntegrationRefs.ContainsKey($taskName)) { return [Reflection.Assembly]::LoadFrom($global:taskIntegrationRefs[$taskName]) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($taskResolver)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new([Windows.Threading.Dispatcher]::CurrentDispatcher))
$taskAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'TwinCatAssistant.IdeHost.dll'))
$taskShell = [Reflection.Assembly]::LoadFrom($global:taskIntegrationRefs['Microsoft.VisualStudio.Shell.15.0'])
$taskThreadHelper = $taskShell.GetType('Microsoft.VisualStudio.Shell.ThreadHelper')
$taskThreadHelper.GetMethod('SetUIThread',[Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null,@())
# Standalone test process has no VS package service provider. Initialize its
# *own* JTF context on this dispatcher, never a running IDE's static state.
$taskContextField = $taskThreadHelper.GetField('_joinableTaskContextCache',[Reflection.BindingFlags]'Static,NonPublic')
$taskContext = [Activator]::CreateInstance($taskContextField.FieldType,[object[]]@([Threading.Thread]::CurrentThread,[Threading.SynchronizationContext]::Current))
$taskContextField.SetValue($null,$taskContext)
$taskFlags = [Reflection.BindingFlags]'Instance,NonPublic'
$taskCount = 0
function Assert-Integration([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "Chat integration failed: $Name" }
    $script:taskCount++; Write-Output "PASS $Name"
}
function Pump([int]$Milliseconds) {
    $taskFrame = New-Object Windows.Threading.DispatcherFrame
    $taskTimer = New-Object Windows.Threading.DispatcherTimer
    $taskTimer.Interval = [TimeSpan]::FromMilliseconds($Milliseconds)
    $taskTimer.add_Tick({ $taskFrame.Continue = $false }.GetNewClosure())
    $taskTimer.Start()
    try { [Windows.Threading.Dispatcher]::PushFrame($taskFrame) } finally { $taskTimer.Stop() }
}
function Await-Task($Task) {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(85)
    while (-not $Task.IsCompleted) { if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Chat integration operation timed out' }; Pump 50 }
    $Task.GetAwaiter().GetResult()
}
function Field([string]$Name) { $taskControl.GetType().GetField($Name,$taskFlags).GetValue($taskControl) }
function Set-Field([string]$Name,$Value) { $taskControl.GetType().GetField($Name,$taskFlags).SetValue($taskControl,$Value) }
function Invoke-Chat([string]$Name) { Await-Task ($taskControl.GetType().GetMethod($Name,$taskFlags).Invoke($taskControl,@())) | Out-Null }
function JS([string]$Code) { Await-Task ($taskBrowser.CoreWebView2.ExecuteScriptAsync($Code)) }
function Wait-JS([string]$Code,[int]$Seconds=25) {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { if ((JS $Code) -eq 'true') { return }; Pump 100 } while ([DateTime]::UtcNow -lt $taskDeadline)
    Write-Output ('CATALOG METADATA '+(JS 'JSON.stringify(window.__fixtureCatalogDetails?.())'))
    throw ('Embedded chat did not reach expected condition: '+$Code)
}
function Wait-Native([scriptblock]$Condition,[string]$Name,[int]$Seconds=40) {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        Invoke-Chat 'PollChatSelectionAsync'
        if (& $Condition) { return }
        Pump 250
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    Write-Output ('BANNER '+(Field '_bannerText').Text+' | STATUS '+(Field '_chatStatus').Text+' | MODEL '+(Field '_modelStatus').Text)
    throw ('Native automation did not reach: '+$Name)
}
function Set-Snapshot([string]$Project,[string]$Implementation='bRun := TRUE; // synthetic context fixture') {
    $global:taskIntegrationSnapshot = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorSnapshot'))
    $taskIdentityType = $taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorIdentity')
    $taskIdentityConstructor = $taskIdentityType.GetConstructor([type[]]@([int],[string],[string],[string],[string]))
    $global:taskIntegrationSnapshot.Identity = $taskIdentityConstructor.Invoke([object[]]@(
        [int]$PID,[string](Join-Path $taskRoot 'ContextChecks.sln'),[string](Join-Path $taskRoot ($Project+'.plcproj')),
        [string](Join-Path $taskRoot ($Project+'/FB_Motor.TcPOU')),[string]('TIPC/'+$Project+'/POUs/FB_Motor')))
    $global:taskIntegrationSnapshot.ObjectName = 'FB_Motor'
    $global:taskIntegrationSnapshot.Declaration = 'FUNCTION_BLOCK FB_Motor'
    $global:taskIntegrationSnapshot.Implementation = $Implementation
    $global:taskIntegrationSnapshot.SourceKind = 'Automation Interface snapshot'
    $taskControl.GetType().GetMethod('RenderContext',$taskFlags).Invoke($taskControl,[object[]]@($global:taskIntegrationSnapshot,$null))
}
$taskConfig = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.NativeChatConfiguration'))
$taskConfig.NodePath = (Get-Command node -ErrorAction Stop).Source
$taskConfig.RepositoryRoot = $taskRepo; $taskConfig.DotNetRoot = [IO.Path]::GetFullPath($DotNetRoot)
$taskConfig.StateRoot = Join-Path $taskRoot 'state'
$taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$taskListener.Start(); $taskPort = $taskListener.LocalEndpoint.Port; $taskListener.Stop()
$taskPassword = 'synthetic-' + [Guid]::NewGuid().ToString('N')
$taskSession = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.NativeChatSession'))
$taskControl = $null; $taskWindow = $null
try {
    Await-Task ($taskSession.StartAsync($taskConfig,$taskPort,$taskPassword)) | Out-Null
    $taskControl = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.AssistantControl'))
    Set-Field '_chatBrowserProfile' (Join-Path $taskRoot 'browser')
    $taskStoreType = $taskAssembly.GetType('TwinCatAssistant.Native.ChatBindingStore')
    Set-Field '_chatBindings' ($taskStoreType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $taskRoot 'bindings'))))
    $taskWorkspaceValue = Join-Path $taskConfig.StateRoot 'workspace'
    Set-Field '_chatWorkspace' ([Func[string]]{ return $taskWorkspaceValue }.GetNewClosure())
    Set-Field '_chatConfiguration' ([Func[TwinCatAssistant.Native.NativeChatConfiguration]]{ return $taskConfig }.GetNewClosure())
    $taskCredentialsType = $taskAssembly.GetType('TwinCatAssistant.Native.NativeChatCredentials')
    $taskCredentials = $taskCredentialsType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $taskRoot 'credential')))
    $taskCredentials.Save($taskPassword)
    Set-Field '_chatCredentials' $taskCredentials
    Set-Field '_chatSnapshotReader' ([Func[TwinCatAssistant.Native.ActiveEditorSnapshot]]{ return $global:taskIntegrationSnapshot })
    (Field '_port').Text = [string]$taskPort
    Set-Snapshot 'PLC_A'
    $taskWindow = New-Object Windows.Window
    $taskWindow.Content=$taskControl; $taskWindow.Width=640; $taskWindow.Height=900
    $taskWindow.ShowActivated=$false; $taskWindow.ShowInTaskbar=$false; $taskWindow.Left=-10000
    $taskWindow.Show()
    $taskStartupDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do { Pump 100; $taskBrowser=Field '_browser' } while (-not $taskBrowser.CoreWebView2 -and [DateTime]::UtcNow -lt $taskStartupDeadline)
    Assert-Integration ($taskBrowser.CoreWebView2 -and (Field '_autoConnectAttempted')) 'opening the native window connects chat without a separate connect click'
    Wait-JS "window.__twinCatInstall==='ready' && window.__twinCatChat.state().authenticated"
    Assert-Integration ((JS 'document.body.innerText.includes("Unlock OpenChamber")') -eq 'false') 'native sign-in authenticates without showing the password screen'
    Wait-Native { (Field '_attachPouButton').IsEnabled } 'automatic project chat for PLC_A'
    Assert-Integration (Field '_attachPouButton').IsEnabled 'opening a POU creates and binds its project chat automatically'
    $taskFirstSession = (Field '_chatSelection').sessionId
    Assert-Integration ((JS '(()=>{const h=document.querySelector("header.header-safe-area");const c=document.querySelector(".main-content-safe-area > .absolute.left-0.top-0.z-30");return !!h && getComputedStyle(h).display==="none" && (!c || getComputedStyle(c).display==="none")})()') -eq 'true') 'OpenChamber top bar with unbound new-chat buttons is hidden'
    Assert-Integration ((JS 'document.body.innerText.includes("synthetic context fixture")') -eq 'false') 'opening and binding a chat do not disclose source'
    Invoke-Chat 'AttachClickAsync'; Pump 800
    Assert-Integration ((JS 'document.body.innerText.includes("FB_Motor / PLC_A")') -eq 'true') 'explicit native attach produces a real upstream composer chip'
    Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((Field '_attachLabel').Text.StartsWith('Angeh') -and -not (Field '_attachPouButton').IsEnabled) 'attach button reports the staged revision'
    Assert-Integration ((JS '[...document.querySelectorAll("[role=dialog]")].some(e=>e.innerText.includes("Add project directory"))') -eq 'false') 'private chat workspace avoids the generic first-run project picker'
    Assert-Integration ((Field '_bannerText').Text.Contains('Modellanbieter') -and (Field '_banner').Visibility -eq [Windows.Visibility]::Visible) 'missing provider is explained with one action'
    JS "window.__fixtureProviderConnected=false;fetch('/api/integration/openai/connect/key',{method:'POST',headers:{'content-type':'application/json','x-opencode-directory':encodeURIComponent(window.__twinCatChat.state().directory),'x-opencode-directory-encoding':'uri'},body:JSON.stringify({key:'synthetic-not-a-real-api-key'})}).then(r=>window.__fixtureProviderConnected=r.status===204)" | Out-Null
    Wait-JS 'window.__fixtureProviderConnected'
    Wait-Native { @((Field '_chatSelection').allowedProviders) -contains 'openai' } 'automatic provider enablement' 60
    Assert-Integration ((Field '_modelStatus').Text.Contains('(freigegeben)') -and (Field '_banner').Visibility -ne [Windows.Visibility]::Visible) 'connecting a provider in OpenChamber enables it automatically and is read back'
    JS "import('/assets/useAppFontEffects-DC1U1Z6i.js').then(m=>{window.__fixtureCatalog=()=>m.l.getState().providers.map(p=>({id:p.id,count:p.models.length}));window.__fixtureCatalogDetails=()=>({active:m.l.getState().activeDirectoryKey,selected:window.__twinCatChat.state().directory,providers:window.__fixtureCatalog(),snapshots:Object.entries(m.l.getState().directoryScoped).map(([key,v])=>({key,providers:v.providers.map(p=>p.id)}))});})" | Out-Null
    Wait-JS "window.__fixtureCatalog?.().some(p=>p.id==='openai' && p.count>0)"
    Assert-Integration ((JS "window.__fixtureCatalog().some(p=>p.id==='openai' && p.count>0)") -eq 'true') 'real upstream model picker catalog is populated after provider-only consent'
    JS ("window.__fixtureShell=null;fetch('/api/session/"+$taskFirstSession+"/shell',{method:'POST',headers:{'content-type':'application/json'},body:'{}'}).then(r=>r.json()).then(r=>window.__fixtureShell=r.code)") | Out-Null
    Wait-JS 'window.__fixtureShell!==null'
    Assert-Integration ((JS 'window.__fixtureShell') -eq '"TWINCAT_READ_ONLY"') 'native host blocks the direct shell route even while the project chat is bound'
    $taskBindings = @(Get-ChildItem (Join-Path $taskRoot 'bindings') -Filter '*.json')
    Assert-Integration ($taskBindings.Count -eq 1 -and -not (Get-Content $taskBindings[0].FullName -Raw).Contains('synthetic context fixture')) 'persistent thread binding contains metadata but no source'
    Set-Snapshot 'PLC_B'
    Wait-Native { (Field '_chatSelection').sessionId -ne $taskFirstSession -and (Field '_attachPouButton').IsEnabled } 'automatic switch to a PLC_B chat'
    Assert-Integration ((Field '_chatBinding').ProjectFile.EndsWith('PLC_B.plcproj')) 'switching PLC projects opens a separate chat for the new project'
    JS ("window.__fixturePrompt=null;fetch('/api/session/"+$taskFirstSession+"/message',{method:'POST',headers:{'content-type':'application/json'},body:'{}'}).then(r=>window.__fixturePrompt=r.status)") | Out-Null
    Wait-JS 'window.__fixturePrompt!==null'
    Assert-Integration ((JS 'window.__fixturePrompt') -eq '403') 'native network boundary blocks prompts for a mismatched project'
    $taskSecondSession = (Field '_chatSelection').sessionId
    $taskSelectionJson = (New-Object Web.Script.Serialization.JavaScriptSerializer).Serialize($taskFirstSession)
    JS ("window.__twinCatChat.selectSession("+$taskSelectionJson+")") | Out-Null
    Pump 800; Invoke-Chat 'PollChatSelectionAsync'; Invoke-Chat 'BindCurrentChatAsync'
    Assert-Integration (-not (Field '_attachPouButton').IsEnabled -and (Field '_chatSelection').sessionId -eq $taskFirstSession) 'an existing project thread cannot be silently rebound to another PLC'
    Assert-Integration ((Field '_bannerText').Text.Contains('PLC_A') -and (Field '_statusText').Text.Contains('PLC_A')) 'a foreign project chat is explained as read-only'
    Set-Snapshot 'PLC_A'
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskFirstSession -and (Field '_chatBinding').ProjectFile.EndsWith('PLC_A.plcproj') } 'return to PLC_A chat'
    Set-Snapshot 'PLC_B'
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskSecondSession } 'reopen newest PLC_B chat'
    Assert-Integration (@(Get-ChildItem (Join-Path $taskRoot 'bindings') -Filter '*.json').Count -eq 2) 'returning to a project reopens its newest conversation instead of creating another'
    Set-Snapshot 'PLC_A'
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskFirstSession } 'reopen PLC_A chat'
    $taskBrowser.CoreWebView2.Reload()
    Wait-JS "window.__twinCatInstall==='ready' && window.__twinCatChat.state().authenticated"
    Pump 1500; Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((JS 'document.body.innerText.includes("Could not initialize workspace")') -eq 'false') 'full page reload keeps the prepared workspace available'
    $taskPreviousDocument = JS 'window.__twinCatChat.state().documentId'
    Await-Task ($taskSession.StopAsync()) | Out-Null
    $taskReconnectDeadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        Pump 200
        $taskOwnedRuntime = Field '_chatSession'
        if ($taskOwnedRuntime -and $taskOwnedRuntime.IsRunning -and (Field '_chatAdapterReady')) { break }
    } while ([DateTime]::UtcNow -lt $taskReconnectDeadline)
    Assert-Integration ($taskOwnedRuntime -and $taskOwnedRuntime.IsRunning) 'native host automatically restarts a lost protected runtime'
    Wait-JS ("window.__twinCatInstall==='ready' && window.__twinCatChat.state().authenticated && window.__twinCatChat.state().documentId!=="+$taskPreviousDocument)
    Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((JS 'window.__twinCatChat.state().attachedObject===null') -eq 'true') 'reconnection does not replay source disclosure'
    $taskPreferencesType = $taskAssembly.GetType('TwinCatAssistant.Native.NativeChatPreferences')
    $taskPreferences = $taskPreferencesType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string](Join-Path $taskRoot 'preferences')))
    Set-Field '_chatPreferences' $taskPreferences
    Assert-Integration (-not $taskPreferences.AutoAttach) 'automatic attachment is off in a fresh preference profile'
    (Field '_moreButton').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    Pump 100
    Assert-Integration (Field '_menuPopup').IsOpen 'the native overflow button opens its menu'
    $taskAutoOption = @((Field '_menuItems').Children | Where-Object {
        $_ -is [Windows.Controls.Button] -and $_.Content.Children[-1].Text.StartsWith('Aktiven Baustein automatisch')
    })
    if ($taskAutoOption.Count -ne 1) { throw 'The native menu must expose exactly one automatic attachment option.' }
    $taskAutoOption[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    Wait-Native { (JS 'window.__twinCatChat.state().attachedObject') -eq '"FB_Motor / PLC_A"' } 'auto-attach of the active POU'
    $taskSavedPreferences = $taskPreferencesType.GetMethod('Load').Invoke($null,[object[]]@([string](Join-Path $taskRoot 'preferences')))
    Assert-Integration ($taskPreferences.AutoAttach -and $taskSavedPreferences.AutoAttach) 'the real menu action enables and persists automatic attachment'
    Assert-Integration ((JS 'document.body.innerText.includes("FB_Motor / PLC_A")') -eq 'true') 'auto-attach stages the active POU when the operator enabled it'
    $taskAutoRevision = JS 'window.__twinCatChat.state().attachedRevision'
    Set-Snapshot 'PLC_A' 'bRun := FALSE; // edited synthetic fixture'
    Wait-Native { (JS 'window.__twinCatChat.state().attachedRevision') -ne $taskAutoRevision } 'auto-attach of a changed revision'
    Assert-Integration $true 'auto-attach restages a changed POU revision'
    Invoke-Chat 'ToggleAutoAttachAsync'
    Assert-Integration (-not $taskPreferences.AutoAttach -and (Field '_banner').Visibility -eq [Windows.Visibility]::Visible -and
        (Field '_bannerText').Text.Contains('ist aus')) 'turning automatic attachment off shows its visible confirmation'
    (Field '_bannerDismiss').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    Pump 100
    Assert-Integration ((Field '_noticeText') -eq $null -and (Field '_banner').Visibility -eq [Windows.Visibility]::Collapsed) 'the native dismiss button clears a visible notice'
    $global:taskIntegrationSnapshot = $null
    $taskControl.GetType().GetMethod('RenderContext',$taskFlags).Invoke($taskControl,[object[]]@($null,'Synthetic no-editor state'))
    Invoke-Chat 'NewProjectChatAsync'; Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((Field '_chatBinding').IsGeneral) 'a general chat can be started without any active ST editor'
    Assert-Integration ($taskControl.GetType().GetMethod('CanPromptCurrentChat',$taskFlags).Invoke($taskControl,@())) 'general chat accepts normal conversation without a project wizard'
    Assert-Integration (-not (Field '_attachPouButton').IsEnabled) 'general chat does not authorize native source disclosure'
    Set-Snapshot 'PLC_A'
    JS ("window.__twinCatChat.selectSession("+$taskSelectionJson+")") | Out-Null
    Pump 800; Invoke-Chat 'PollChatSelectionAsync'; Invoke-Chat 'AttachClickAsync'; Pump 500
    $taskEvidence = Join-Path $taskRepo 'local-data/chat-integration-views'
    [IO.Directory]::CreateDirectory($taskEvidence) | Out-Null
    foreach ($taskWidth in @(390,768,1100)) {
        $taskWindow.Width = $taskWidth; Pump 700
        $taskScreenshot = [IO.File]::Create((Join-Path $taskEvidence ('chat-'+$taskWidth+'.png')))
        try { Await-Task ($taskBrowser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png,$taskScreenshot)) | Out-Null }
        finally { $taskScreenshot.Dispose() }
    }
    Write-Output "$taskCount native embedded integration checks passed. Synthetic editor snapshots; no IDE/compiler/model acceptance claimed."
    Write-Output ('Fixture evidence: '+$taskRoot)
} finally {
    if ($taskControl) { $taskOwnedRuntime = Field '_chatSession'; if ($taskOwnedRuntime) { Await-Task ($taskOwnedRuntime.StopAsync()) | Out-Null } }
    if ($taskControl) { $taskControl.Dispose() }
    if ($taskWindow) { $taskWindow.Close() }
    Await-Task ($taskSession.StopAsync()) | Out-Null
    $taskPassword=$null
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($taskResolver)
}
