param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/native-view'))
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run in Windows PowerShell STA: powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1'
}

# This renders the real built WPF control with synthetic presentation inputs.
# There is no IDE attachment, WebView initialization, source transport or live
# editor evidence. Shell thread initialization below applies only to this process.
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskBin = Join-Path $taskRoot 'native/IdeHost/X86/bin/Release/net472'
$taskAssets = Get-Content (Join-Path $taskRoot 'native/IdeHost/X86/obj/project.assets.json') -Raw | ConvertFrom-Json
$taskPackageRoot = @($taskAssets.packageFolders.PSObject.Properties)[0].Name
$global:taskNativeViewReferences = @{}
$taskPackages = @($taskAssets.targets.PSObject.Properties)[0].Value
foreach ($taskPackage in $taskPackages.PSObject.Properties) {
    foreach ($taskReference in $taskPackage.Value.compile.PSObject.Properties) {
        if ($taskReference.Name.EndsWith('.dll')) {
            $taskPath = Join-Path $taskPackageRoot ($taskPackage.Name.ToLowerInvariant() + '/' + $taskReference.Name)
            $global:taskNativeViewReferences[[IO.Path]::GetFileNameWithoutExtension($taskPath)] = $taskPath
        }
    }
}
foreach ($taskDll in Get-ChildItem $taskBin -Filter '*.dll') { $global:taskNativeViewReferences[$taskDll.BaseName] = $taskDll.FullName }
$taskResolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $taskName = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name
    if ($global:taskNativeViewReferences.ContainsKey($taskName)) {
        return [Reflection.Assembly]::LoadFrom($global:taskNativeViewReferences[$taskName])
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($taskResolver)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$taskControl = $null
$taskFlags = [Reflection.BindingFlags]'Instance,NonPublic'
function Assert-View([bool]$Result, [string]$Name) {
    if (-not $Result) { throw "Native view check failed: $Name" }
    Write-Output "PASS $Name"
}
function Get-ViewField([string]$Name) { $taskControl.GetType().GetField($Name, $taskFlags).GetValue($taskControl) }
function Save-View([string]$State) {
    foreach ($taskWidth in @(340,520,900)) {
        $taskControl.Width = $taskWidth
        $taskControl.Height = 760
        $taskControl.Measure((New-Object Windows.Size($taskWidth,760)))
        $taskControl.Arrange((New-Object Windows.Rect(0,0,$taskWidth,760)))
        $taskControl.UpdateLayout()
        $taskImage = New-Object Windows.Media.Imaging.RenderTargetBitmap($taskWidth,760,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $taskImage.Render($taskControl)
        $taskEncoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskImage))
        $taskFile = [IO.File]::Create((Join-Path $OutputDirectory "$State-$taskWidth.png"))
        try { $taskEncoder.Save($taskFile) } finally { $taskFile.Dispose() }
    }
}
try {
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $taskAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'TwinCatAssistant.IdeHost.dll'))
    $taskShell = [Reflection.Assembly]::LoadFrom($global:taskNativeViewReferences['Microsoft.VisualStudio.Shell.15.0'])
    $taskThreadHelper = $taskShell.GetType('Microsoft.VisualStudio.Shell.ThreadHelper')
    $taskThreadHelper.GetMethod('SetUIThread', [Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null,@())
    $taskControl = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.AssistantControl'))
    $taskControl.Background = [Windows.Media.Brushes]::White
    $taskControl.Foreground = [Windows.Media.Brushes]::Black
    $taskRender = $taskControl.GetType().GetMethod('RenderContext', $taskFlags)
    Assert-View ((Get-ViewField '_source').Text -eq '') 'unbound control starts without source'
    Assert-View ((Get-ViewField '_reader') -eq $null) 'constructing the view does not scan the catalog'
    Save-View 'empty-light'

    $taskIdentity = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorIdentity'),
        [object[]]@(42,'C:\Synthetic\ContextChecks.sln','C:\Synthetic\PLC_A\PLC_A.plcproj',
        'C:\Synthetic\PLC_A\FB_Motor.TcPOU','TIPC^PLC_A^PLC_A Project^POUs^FB_Motor'))
    $taskSnapshot = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ActiveEditorSnapshot'))
    $taskSnapshot.Identity = $taskIdentity
    $taskSnapshot.ObjectName = 'FB_Motor'
    $taskSnapshot.Declaration = "FUNCTION_BLOCK FB_Motor`r`nVAR`r`n    bRun : BOOL;`r`nEND_VAR"
    $taskSnapshot.Implementation = 'bRun := FALSE;'
    $taskSnapshot.SourceKind = 'Automation Interface snapshot'
    $taskSnapshot.SourceNotice = 'Unsaved editor text is not verified. No authoritative dirty-state evidence from the exact running document.'
    $taskDirty = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.DirtyAssessment'))
    $taskDirty.State = 'unknown'
    $taskSnapshot.Dirty = $taskDirty
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))
    Assert-View ((Get-ViewField '_objectName').Text -eq 'FB_Motor') 'active object is visible'
    Assert-View ((Get-ViewField '_evidence').Text.Contains('unbekannt')) 'unknown dirty state remains explicit'
    Assert-View ((Get-ViewField '_sourceNotice').Text.Contains('not verified')) 'editor buffer uncertainty remains visible'
    Assert-View ((Get-ViewField '_source').Text.Contains('bRun := FALSE;')) 'local preview receives the active source'
    Save-View 'active-light'

    (Get-ViewField '_diagnosticSource').Text = 'unrelated manual diagnostic source'
    (Get-ViewField '_chatStatus').Text = 'Chat is not connected.'
    Assert-View ((Get-ViewField '_objectName').Text -eq 'FB_Motor' -and
        -not (Get-ViewField '_source').Text.Contains('unrelated')) 'manual diagnostics and chat status do not replace active context'
    $taskDirty.State = 'known-dirty'
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))
    Assert-View ((Get-ViewField '_evidence').Text.Contains('Ungespeicherte')) 'dirty state is visible'
    $taskControl.Background = [Windows.Media.Brushes]::Black
    $taskControl.Foreground = [Windows.Media.Brushes]::White
    Save-View 'dirty-dark'
    $taskExpander = @((Get-ViewField '_toolbar').Children | Where-Object { $_ -is [Windows.Controls.Expander] })[0]
    Assert-View ($taskExpander.Foreground.ToString() -eq '#FFFFFFFF') 'diagnostic header follows the dark host foreground'

    $taskRender.Invoke($taskControl,[object[]]@($null,'Unsupported editor'))
    Assert-View ((Get-ViewField '_source').Text -eq '' -and
        (Get-ViewField '_source').Visibility -eq [Windows.Visibility]::Collapsed) 'unbinding clears and hides old source'
    Assert-View ((Get-ViewField '_objectName').ToolTip -eq $null -and
        (Get-ViewField '_evidence').ToolTip -eq $null) 'unbinding clears old identity tooltips'
    Save-View 'unsupported-dark'
    $taskChatState = Get-ViewField '_chatConnection'
    $taskChatGeneration = $taskChatState.Begin()
    $taskChatState.Loading($taskChatGeneration)
    Assert-View (-not $taskChatState.PageLoaded) 'starting a service does not claim a loaded chat page'
    $taskChatState.Navigating(42)
    $taskChatState.Completed(42,$false,0,'ConnectionRefused')
    $taskShowChat = $taskControl.GetType().GetMethod('ShowChatStatus', $taskFlags)
    $taskShowChat.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_chatStatus').Text.Contains('ConnectionRefused')) 'navigation failure is available in the real native diagnostics'
    Assert-View ((Get-ViewField '_bindingStatus').Text.Contains('Chat nicht verbunden')) 'connection failure stays visible above the chat'
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Collapsed) 'failed browser error page is hidden'
    Assert-View ((Get-ViewField '_source').Text -eq '') 'chat error does not attach or restore native source'
    Save-View 'chat-refused-dark'
    $taskControl.Background = [Windows.Media.Brushes]::White
    $taskControl.Foreground = [Windows.Media.Brushes]::Black
    (Get-ViewField '_chatSetup').Visibility = [Windows.Visibility]::Visible
    $taskChatState.Fail($taskChatState.Generation,'Set up your local chat password below, then press Start local chat.')
    $taskShowChat.Invoke($taskControl,@())
    Save-View 'chat-password-setup-light'
    $taskControl.GetType().GetField('_origin',$taskFlags).SetValue($taskControl,[Uri]'http://127.0.0.1:3123/')
    $taskAllowResource = $taskControl.GetType().GetMethod('AllowedResource',$taskFlags)
    $taskAllowNavigation = $taskControl.GetType().GetMethod('Allowed',$taskFlags)
    Assert-View ($taskAllowResource.Invoke($taskControl,[object[]]@('ws://127.0.0.1:3123/socket'))) 'chat websocket can use its exact loopback origin'
    Assert-View (-not $taskAllowNavigation.Invoke($taskControl,[object[]]@('ws://127.0.0.1:3123/socket'))) 'websocket resource permission does not grant navigation'
    foreach ($taskDenied in @('ws://127.0.0.1:3124/socket','ws://example.com:3123/socket','ws://user@127.0.0.1:3123/socket')) {
        Assert-View (-not $taskAllowResource.Invoke($taskControl,[object[]]@($taskDenied))) "cross-origin/credential resource rejected: $taskDenied"
    }
    Write-Output "Native view checks passed. Synthetic WPF renders: $OutputDirectory"
} finally {
    if ($taskControl) { $taskControl.Dispose() }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($taskResolver)
    Remove-Variable -Scope Global -Name taskNativeViewReferences
}
