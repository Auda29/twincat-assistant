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
function Set-Snapshot([string]$Project) {
    $global:taskIntegrationSnapshot = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorSnapshot'))
    $taskIdentityType = $taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorIdentity')
    $taskIdentityConstructor = $taskIdentityType.GetConstructor([type[]]@([int],[string],[string],[string],[string]))
    $global:taskIntegrationSnapshot.Identity = $taskIdentityConstructor.Invoke([object[]]@(
        [int]$PID,[string](Join-Path $taskRoot 'ContextChecks.sln'),[string](Join-Path $taskRoot ($Project+'.plcproj')),
        [string](Join-Path $taskRoot ($Project+'/FB_Motor.TcPOU')),[string]('TIPC/'+$Project+'/POUs/FB_Motor')))
    $global:taskIntegrationSnapshot.ObjectName = 'FB_Motor'
    $global:taskIntegrationSnapshot.Declaration = 'FUNCTION_BLOCK FB_Motor'
    $global:taskIntegrationSnapshot.Implementation = 'bRun := TRUE; // synthetic context fixture'
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
    Wait-JS "window.__twinCatInstall==='ready'"
    Assert-Integration (-not (Field '_newChatButton').IsEnabled) 'project chat stays disabled before protected login'
    $taskSerializer = New-Object Web.Script.Serialization.JavaScriptSerializer
    $taskPasswordJson = $taskSerializer.Serialize($taskPassword)
    JS ("window.__fixtureLogin=null;fetch('/auth/session',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({password:"+$taskPasswordJson+"})}).then(r=>window.__fixtureLogin=r.status)") | Out-Null
    Wait-JS 'window.__fixtureLogin===200'
    $taskBrowser.CoreWebView2.Reload()
    Wait-JS "window.__twinCatInstall==='ready' && window.__twinCatChat.state().authenticated"
    Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration (Field '_newChatButton').IsEnabled 'authenticated pinned chat adapter is usable'
    Invoke-Chat 'NewProjectChatAsync'
    Invoke-Chat 'PollChatSelectionAsync'
    if (-not (Field '_attachPouButton').IsEnabled) { Write-Output ('STATUS '+(Field '_bindingStatus').Text) }
    Assert-Integration (Field '_attachPouButton').IsEnabled 'new upstream session is bound to the exact native project'
    $taskFirstSession = (Field '_chatSelection').sessionId
    Assert-Integration ((JS 'document.body.innerText.includes("synthetic context fixture")') -eq 'false') 'opening and binding a chat do not disclose source'
    Invoke-Chat 'AttachActivePouAsync'; Pump 800
    Assert-Integration ((JS 'document.body.innerText.includes("FB_Motor / PLC_A")') -eq 'true') 'explicit native attach produces a real upstream composer chip'
    Assert-Integration ((JS '[...document.querySelectorAll("[role=dialog]")].some(e=>e.innerText.includes("Add project directory"))') -eq 'false') 'private chat workspace avoids the generic first-run project picker'
    JS "window.__fixtureProviderConnected=false;fetch('/api/integration/openai/connect/key',{method:'POST',headers:{'content-type':'application/json','x-opencode-directory':encodeURIComponent(window.__twinCatChat.state().directory),'x-opencode-directory-encoding':'uri'},body:JSON.stringify({key:'synthetic-not-a-real-api-key'})}).then(r=>window.__fixtureProviderConnected=r.status===204)" | Out-Null
    Wait-JS 'window.__fixtureProviderConnected'
    Wait-JS "window.__twinCatChat.state().providers.some(p=>p.id==='openai')"
    Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((Field '_allowModelButton').IsEnabled -and -not (Field '_chatSelection').model) 'connected provider can be explicitly allowed before any model is selected'
    Invoke-Chat 'AllowSelectedModelAsync'
    if (-not (Field '_modelStatus').Text.Contains('zugelassen')) { Write-Output ('CONSENT METADATA '+(JS 'JSON.stringify({phase:window.__twinCatPolicy,state:window.__twinCatChat?.state()})')) }
    Assert-Integration ((Field '_modelStatus').Text.Contains('zugelassen')) 'explicit native model consent is adopted and read back by the running upstream'
    JS "import('/assets/useAppFontEffects-DC1U1Z6i.js').then(m=>{window.__fixtureCatalog=()=>m.l.getState().providers.map(p=>({id:p.id,count:p.models.length}));window.__fixtureCatalogDetails=()=>({active:m.l.getState().activeDirectoryKey,selected:window.__twinCatChat.state().directory,providers:window.__fixtureCatalog(),snapshots:Object.entries(m.l.getState().directoryScoped).map(([key,v])=>({key,providers:v.providers.map(p=>p.id)}))});})" | Out-Null
    Wait-JS "window.__fixtureCatalog?.().some(p=>p.id==='openai' && p.count>0)"
    Assert-Integration ((JS "window.__fixtureCatalog().some(p=>p.id==='openai' && p.count>0)") -eq 'true') 'real upstream model picker catalog is populated after provider-only consent'
    JS ("window.__fixtureShell=null;fetch('/api/session/"+$taskFirstSession+"/shell',{method:'POST',headers:{'content-type':'application/json'},body:'{}'}).then(r=>r.json()).then(r=>window.__fixtureShell=r.code)") | Out-Null
    Wait-JS 'window.__fixtureShell!==null'
    Assert-Integration ((JS 'window.__fixtureShell') -eq '"TWINCAT_READ_ONLY"') 'native host blocks the direct shell route even while the project chat is bound'
    $taskBindings = @(Get-ChildItem (Join-Path $taskRoot 'bindings') -Filter '*.json')
    Assert-Integration ($taskBindings.Count -eq 1 -and -not (Get-Content $taskBindings[0].FullName -Raw).Contains('synthetic context fixture')) 'persistent thread binding contains metadata but no source'
    Set-Snapshot 'PLC_B'; Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration (-not (Field '_attachPouButton').IsEnabled) 'switching PLC projects blocks source attachment to the old chat'
    JS ("window.__fixturePrompt=null;fetch('/api/session/"+$taskFirstSession+"/message',{method:'POST',headers:{'content-type':'application/json'},body:'{}'}).then(r=>window.__fixturePrompt=r.status)") | Out-Null
    Wait-JS 'window.__fixturePrompt!==null'
    Assert-Integration ((JS 'window.__fixturePrompt') -eq '403') 'native network boundary blocks prompts for a mismatched project'
    Invoke-Chat 'BindCurrentChatAsync'
    Assert-Integration (-not (Field '_attachPouButton').IsEnabled) 'an existing project thread cannot be silently rebound to another PLC'
    Set-Snapshot 'PLC_A'; Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration (Field '_attachPouButton').IsEnabled 'returning to the exact project revalidates its persisted binding'
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
    Wait-JS ("window.__twinCatInstall==='ready' && window.__twinCatChat.state().documentId!=="+$taskPreviousDocument)
    Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((JS 'window.__twinCatChat.state().attachedObject===null') -eq 'true') 'reconnection does not replay source disclosure'
    $global:taskIntegrationSnapshot = $null
    $taskControl.GetType().GetMethod('RenderContext',$taskFlags).Invoke($taskControl,[object[]]@($null,'Synthetic no-editor state'))
    Invoke-Chat 'NewProjectChatAsync'; Invoke-Chat 'PollChatSelectionAsync'
    Assert-Integration ((Field '_chatBinding').IsGeneral) 'a general chat can be started without any active ST editor'
    Assert-Integration ($taskControl.GetType().GetMethod('CanPromptCurrentChat',$taskFlags).Invoke($taskControl,@())) 'general chat accepts normal conversation without a project wizard'
    Assert-Integration (-not (Field '_attachPouButton').IsEnabled) 'general chat does not authorize native source disclosure'
    Set-Snapshot 'PLC_A'
    $taskSelectionJson = $taskSerializer.Serialize($taskFirstSession)
    JS ("window.__fixtureRestore=false;import('/assets/useAppFontEffects-DC1U1Z6i.js').then(m=>{m.z.getState().setCurrentSession("+$taskSelectionJson+");window.__fixtureRestore=true;})") | Out-Null
    Wait-JS 'window.__fixtureRestore'
    Invoke-Chat 'PollChatSelectionAsync'; Invoke-Chat 'AttachActivePouAsync'; Pump 500
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
