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
    Assert-View ((Get-ViewField '_kindText').Text -eq 'FB' -and (Get-ViewField '_scope').Text -eq 'PLC_A') 'POU kind and PLC project are shown in the context bar'
    Assert-View ((Get-ViewField '_statusText').Text.Contains('unbekannt')) 'unknown dirty state remains explicit'
    Assert-View ((Get-ViewField '_sourceNotice').Text.Contains('nicht verifiziert')) 'editor buffer uncertainty remains visible'
    Assert-View ((Get-ViewField '_source').Text.Contains('bRun := FALSE;')) 'local preview receives the active source'
    Assert-View ((Get-ViewField '_banner').Visibility -eq [Windows.Visibility]::Collapsed) 'no banner is shown while nothing needs attention'
    Assert-View ((Get-ViewField '_diagnostics').Visibility -eq [Windows.Visibility]::Collapsed) 'diagnostics stay hidden by default'
    Assert-View (-not (Get-ViewField '_attachPouButton').IsEnabled) 'attach stays disabled before the chat is ready'
    Save-View 'active-light'

    (Get-ViewField '_diagnosticSource').Text = 'unrelated manual diagnostic source'
    (Get-ViewField '_chatStatus').Text = 'Chat is not connected.'
    Assert-View ((Get-ViewField '_objectName').Text -eq 'FB_Motor' -and
        -not (Get-ViewField '_source').Text.Contains('unrelated')) 'manual diagnostics and chat status do not replace active context'
    $taskDirty.State = 'known-dirty'
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))
    Assert-View ((Get-ViewField '_statusText').Text -eq 'Ungespeichert') 'dirty state is visible'
    $taskTheme = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ChatTheme'))
    $taskTheme.dark = $true; $taskTheme.background = '#1c1c1a'; $taskTheme.foreground = '#e6e4df'; $taskTheme.muted = '#a3a097'
    $taskTheme.border = '#383733'; $taskTheme.surface = '#252522'; $taskTheme.primary = '#d2733a'; $taskTheme.primaryForeground = '#ffffff'
    $taskTheme.error = '#e0796f'; $taskTheme.warning = '#d6b25e'; $taskTheme.success = '#8fbf6a'
    $taskControl.GetType().GetMethod('ApplyTheme', $taskFlags).Invoke($taskControl,[object[]]@($taskTheme))
    Assert-View ((Get-ViewField '_strip').Background.ToString() -eq '#FF1C1C1A') 'context bar follows the live OpenChamber palette'
    $taskTheme.background = 'red'
    $taskControl.GetType().GetMethod('ApplyTheme', $taskFlags).Invoke($taskControl,[object[]]@($taskTheme))
    Assert-View ((Get-ViewField '_strip').Background.ToString() -eq '#FF1C1C1A') 'invalid theme colors are ignored'
    Save-View 'dirty-dark'

    $taskRender.Invoke($taskControl,[object[]]@($null,'Unsupported editor'))
    Assert-View ((Get-ViewField '_source').Text -eq '' -and
        (Get-ViewField '_source').Visibility -eq [Windows.Visibility]::Collapsed) 'unbinding clears and hides old source'
    Assert-View ((Get-ViewField '_pouButton').ToolTip -eq 'Unsupported editor' -and
        (Get-ViewField '_kindBadge').Visibility -eq [Windows.Visibility]::Collapsed) 'unbinding clears old identity details'
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
    Assert-View ((Get-ViewField '_banner').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_bannerText').Text.Contains('Verbindung')) 'connection failure stays visible above the chat'
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Collapsed) 'failed browser error page is hidden'
    Assert-View ((Get-ViewField '_source').Text -eq '') 'chat error does not attach or restore native source'
    Save-View 'chat-refused-dark'
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))
    (Get-ViewField '_diagnostics').Visibility = [Windows.Visibility]::Visible
    Save-View 'diagnostics-dark'
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
