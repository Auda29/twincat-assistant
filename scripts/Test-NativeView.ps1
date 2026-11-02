param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/native-view'),
    # Built X86 host to render; defaults to the regular Release output.
    [string]$BinDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'native/IdeHost/X86/bin/Release/net472')
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run in Windows PowerShell STA: powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1'
}

# This renders the real built WPF control with synthetic presentation inputs.
# There is no IDE attachment, WebView initialization, source transport or live
# editor evidence. Shell thread initialization below applies only to this process.
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskBin = $BinDirectory
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
function Set-ViewField([string]$Name,$Value) { $taskControl.GetType().GetField($Name,$taskFlags).SetValue($taskControl,$Value) }
function Set-ViewWidth([double]$Width) {
    $taskControl.Width = $Width
    $taskControl.Height = 760
    # The strip collapses the attach label from its SizeChanged handler; the
    # second pass lays out that compact state.
    foreach ($taskPass in 1..2) {
        $taskControl.Measure((New-Object Windows.Size($Width,760)))
        $taskControl.Arrange((New-Object Windows.Rect(0,0,$Width,760)))
        $taskControl.UpdateLayout()
    }
}
# IsVisible needs a window; the fixture renders without one, so walk the
# visual parents instead.
function Test-Shown([Windows.UIElement]$Element) {
    for ($taskNode = $Element; $taskNode -ne $null; $taskNode = [Windows.Media.VisualTreeHelper]::GetParent($taskNode)) {
        if ($taskNode -is [Windows.UIElement] -and $taskNode.Visibility -ne [Windows.Visibility]::Visible) { return $false }
    }
    return $true
}
function Get-ViewBounds([Windows.FrameworkElement]$Element) {
    $Element.TransformToAncestor($taskControl).TransformBounds((New-Object Windows.Rect(0,0,$Element.ActualWidth,$Element.ActualHeight)))
}
function Get-TextWidth([Windows.Controls.TextBlock]$Text) {
    $taskTypeface = New-Object Windows.Media.Typeface($Text.FontFamily,$Text.FontStyle,$Text.FontWeight,$Text.FontStretch)
    $taskFormatted = New-Object Windows.Media.FormattedText($Text.Text,[Globalization.CultureInfo]::CurrentUICulture,
        [Windows.FlowDirection]::LeftToRight,$taskTypeface,$Text.FontSize,[Windows.Media.Brushes]::Black,1.0)
    $taskFormatted.WidthIncludingTrailingWhitespace
}
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
    Assert-View ((Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Collapsed -and
        (Get-ViewField '_splash').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Hidden) 'startup shows only the native wordmark, with the browser hidden but laid out'
    Save-View 'loading-light'
    $taskLoadingRender = $taskControl.GetType().GetMethod('RenderLoading',$taskFlags)
    $taskResetLoading = $taskControl.GetType().GetMethod('ResetLoading',$taskFlags)
    $taskLoadingState = Get-ViewField '_chatConnection'
    $taskLoadingGeneration = $taskLoadingState.Begin()
    $taskLoadingState.Navigating(1)
    $taskLoadingState.Completed(1,$true,200,'')
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Collapsed) 'successful navigation alone does not expose the chat or bar'
    $taskLoadingSelection = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ChatSelection'))
    $taskLoadingSelection.ready=$true
    Set-ViewField '_chatSelection' $taskLoadingSelection
    Set-ViewField '_chatAdapterReady' $true
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Hidden) 'automatic sign-in keeps the chat hidden until authentication completes'
    $taskLoadingSelection.authenticated=$true
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Hidden) 'authentication without restored history keeps the splash visible'
    $taskLoadingSelection.presentationReady=$true
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_splash').Visibility -eq [Windows.Visibility]::Collapsed) 'authenticated presentation readiness reveals the chat and bar together'
    Set-ViewField '_chatAdapterReady' $false
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Visible) 'a transient failed status poll does not flash the loading screen'
    $taskResetLoading.Invoke($taskControl,@())
    Set-ViewField '_chatAdapterReady' $true
    $taskLoadingSelection.authenticated=$false
    Set-ViewField '_signInAttempts' 3
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_browser').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Visible) 'exhausted automatic sign-in permits manual login'
    $taskResetLoading.Invoke($taskControl,@())
    Set-ViewField '_signInAttempts' 0
    Set-ViewField '_chatAdapterReady' $false
    $taskControl.GetType().GetMethod('OnChatReadinessTimeout',$taskFlags).Invoke($taskControl,[object[]]@($null,[EventArgs]::Empty))
    Assert-View ($taskLoadingState.Phase -eq 'failed' -and (Get-ViewField '_bannerAction').Content -eq 'Neu verbinden' -and
        (Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_splash').Visibility -eq [Windows.Visibility]::Collapsed) 'stalled integration exits loading with an accessible reconnect action'
    Save-View 'loading-timeout-light'
    $taskResetLoading.Invoke($taskControl,@())
    $taskLoadingGeneration=$taskLoadingState.Begin()
    Set-ViewField '_chatSelection' $null
    $taskLoadingRender.Invoke($taskControl,@())
    Assert-View ((Get-ViewField '_splash').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Collapsed) 'a new connection returns to loading after failure'
    $taskRestoredPane = [Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.AssistantToolWindow'))
    try {
        $taskRestoredPane.OnToolWindowCreated()
        $taskRestoredControl = $taskRestoredPane.Content
        $taskRestoredButton = $taskRestoredControl.GetType().GetField('_pouButton',$taskFlags).GetValue($taskRestoredControl)
        Assert-View ($taskRestoredButton.ToolTip.Contains('IDE-Editorbindung')) 'restored pane attempts editor binding without the menu command and reports unavailable services'
    } finally { $taskRestoredPane.Dispose() }
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
    $taskRender.Invoke($taskControl,[object[]]@($null,'Resolving the active editor...'))
    Assert-View ((Get-ViewField '_objectName').Text -eq 'FB_Motor' -and (Get-ViewField '_chatActive') -eq $null -and (Get-ViewField '_holdingView')) 'an editor switch keeps the previous POU visible while the logical context is already cleared'
    $taskRender.Invoke($taskControl,[object[]]@($null,'Synthetic unsupported editor'))
    Assert-View ((Get-ViewField '_objectName').Text -ne 'FB_Motor' -and -not (Get-ViewField '_holdingView')) 'a resolved non-POU editor replaces the held view at once'
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))
    $taskSavedControl=$taskControl
    $taskControl=[Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.AssistantControl'))
    try {
        $taskKnowledgeState=[Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.KnowledgeState'))
        $taskKnowledgeState.phase='available';Set-ViewField '_knowledgeState' $taskKnowledgeState
        (Get-ViewField '_knowledgePanel').Visibility=[Windows.Visibility]::Visible
        $taskKnowledgeSource=[Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.KnowledgeSource'))
        $taskKnowledgeSource.path='C:\Synthetic\BKINFOSYS3_TEST_DE-DE.47.mshc';$taskKnowledgeSource.language='de';$taskKnowledgeSource.catalog='VisualStudio15';$taskKnowledgeSource.name='BKINFOSYS3_TEST_DE-DE.47.mshc'
        (Get-ViewField '_knowledgeSources').ItemsSource=[object[]]@($taskKnowledgeSource)
        $taskKnowledgeRender=$taskControl.GetType().GetMethod('RenderKnowledge',$taskFlags)
        $taskKnowledgeRender.Invoke($taskControl,@())
        Assert-View (-not (Get-ViewField '_knowledgeStart').IsEnabled) 'knowledge setup requires a selected source'
        (Get-ViewField '_knowledgeSources').SelectedIndex=0
        Assert-View (Get-ViewField '_knowledgeStart').IsEnabled 'knowledge setup exposes its primary action for a selected source'
        Save-View 'knowledge-available-light'
        Set-ViewField '_knowledgeBusy' $true;$taskKnowledgeState.phase='indexing';$taskKnowledgeRender.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeProgress').Visibility -eq [Windows.Visibility]::Visible -and (Get-ViewField '_knowledgeCancel').IsEnabled) 'background import has progress and cancellation'
        Save-View 'knowledge-indexing-light'
        $taskKnowledgeState.phase='activating';$taskKnowledgeRender.Invoke($taskControl,@())
        Assert-View (-not (Get-ViewField '_knowledgeCancel').IsEnabled) 'atomic knowledge finalization cannot be interrupted by a late cancel'
        Set-ViewField '_knowledgeBusy' $false;$taskKnowledgeState.phase='ready';$taskKnowledgeState.ready=$true;$taskKnowledgeState.language='de';$taskKnowledgeState.topics=49970;$taskKnowledgeRender.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeText').Text.Contains('Dokumentation bereit') -and -not (Get-ViewField '_knowledgeText').Text.Contains('Wissenssuche verbunden')) 'a prepared index does not claim an established MCP connection'
        Save-View 'knowledge-prepared-light'
        $taskSelection=[Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ChatSelection'))
        $taskSelection.version=1;$taskSelection.ready=$true;$taskSelection.authenticated=$true;$taskSelection.sessionId='view-session'
        $taskSelection.documentId=[Guid]::NewGuid().ToString();$taskSelection.directory='C:\Synthetic\workspace'
        $taskMcp=[Activator]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ChatMcpStatus'));$taskMcp.name='knowledge';$taskMcp.status='failed'
        $taskMcpArray=[Array]::CreateInstance($taskAssembly.GetType('TwinCatAssistant.Native.ChatMcpStatus'),1);$taskMcpArray[0]=$taskMcp;$taskSelection.mcp=$taskMcpArray
        Set-ViewField '_chatSelection' $taskSelection;Set-ViewField '_chatAdapterReady' $true;Set-ViewField '_knowledgeConnecting' $true
        $taskRetry=$taskControl.GetType().GetMethod('RetryKnowledge',$taskFlags);$taskDiagnose=$taskControl.GetType().GetMethod('RenderDiagnostics',$taskFlags)
        $taskRetry.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRetries') -eq 0) 'no automatic Knowledge reconnect starts while another reconnect runs'
        Set-ViewField '_knowledgeConnecting' $false;Set-ViewField '_knowledgeChecking' $false
        $taskRetry.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRetries') -eq 1) 'a ready index with a failed Knowledge connection reconnects automatically'
        Set-ViewField '_knowledgeConnecting' $false;$taskRetry.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRetries') -eq 1) 'automatic Knowledge reconnects wait for their growing gap'
        $taskDiagnose.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRow').Value.Text.StartsWith('Verbindet')) 'a failed Knowledge connection with retries left shows as connecting'
        Set-ViewField '_knowledgeRetries' 8;$taskDiagnose.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRow').Value.Text.StartsWith('Nicht')) 'after the last automatic reconnect a failed Knowledge connection is reported'
        $taskMcp.status='connected';$taskRetry.Invoke($taskControl,@())
        Assert-View ((Get-ViewField '_knowledgeRetries') -eq 0) 'a connected Knowledge server resets the automatic reconnects'
    } finally { $taskControl.Dispose();$taskControl=$taskSavedControl }

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
    Assert-View ((Get-ViewField '_splash').Background.ToString() -eq '#FF1C1C1A') 'loading follows the live dark palette'
    Save-View 'loading-dark'

    $taskControl.Bind($null,$null,$null,$null)
    Assert-View ((Get-ViewField '_source').Text -eq '' -and (Get-ViewField '_pouButton').ToolTip.Contains('IDE-Editorbindung')) 'missing IDE binding clears old source instead of preserving stale context'

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
    Assert-View ((Get-ViewField '_strip').Visibility -eq [Windows.Visibility]::Visible -and
        (Get-ViewField '_splash').Visibility -eq [Windows.Visibility]::Collapsed) 'navigation failure exposes controls and removes the loading screen'
    Assert-View ((Get-ViewField '_source').Text -eq '') 'chat error does not attach or restore native source'
    Save-View 'chat-refused-dark'
    $taskRender.Invoke($taskControl,[object[]]@($taskSnapshot,$null))

    # Attach button: every state label keeps the rendered button and its
    # neighbours in place, in the compact icon-only and in the labelled strip.
    # The labels come from the state renderer itself, so a new label that
    # does not fit the reserved width fails here.
    $taskBindingSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'native/IdeHost/ChatIntegration.cs'),[Text.Encoding]::UTF8)
    $taskLabelLine = ($taskBindingSource -split "`n" | Where-Object { $_ -match '_attachLabel\.Text\s*=' } | Select-Object -First 1)
    $taskAttachLabels = @([regex]::Matches($taskLabelLine,'"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    Assert-View ($taskAttachLabels.Count -ge 5) "attach state labels are read from the state renderer ($($taskAttachLabels.Count))"
    $taskAttach = Get-ViewField '_attachPouButton'
    $taskAttachText = Get-ViewField '_attachLabel'
    $taskAttachIcon = Get-ViewField '_attachIcon'
    $taskNeighbours = @{ history = (Get-ViewField '_historyButton'); new = (Get-ViewField '_newChatButton'); more = (Get-ViewField '_moreButton') }
    $taskSavedLabel = $taskAttachText.Text
    $taskSavedIcon = $taskAttachIcon.Data
    $taskIcons = @('attach','check','refresh')
    $taskThemeType = $taskAssembly.GetType('TwinCatAssistant.Native.AssistantTheme')
    $taskWidths = @{}
    try {
        foreach ($taskWidth in @(340,459,460,520,900)) {
            $taskCompact = $taskWidth -lt 460
            $taskReference = $null
            for ($taskIndex = 0; $taskIndex -lt $taskAttachLabels.Count; $taskIndex++) {
                $taskLabel = $taskAttachLabels[$taskIndex]
                $taskAttachText.Text = $taskLabel
                $taskAttachIcon.Data = $taskThemeType.GetMethod('Icon').Invoke($null,[object[]]@($taskIcons[$taskIndex % 3]))
                Set-ViewWidth $taskWidth
                $taskBounds = @{ attach = (Get-ViewBounds $taskAttach) }
                foreach ($taskKey in @($taskNeighbours.Keys)) { $taskBounds[$taskKey] = Get-ViewBounds $taskNeighbours[$taskKey] }
                $taskIconBounds = Get-ViewBounds $taskAttachIcon
                Assert-View ((Test-Shown $taskAttach) -and (Test-Shown $taskAttachIcon) -and $taskBounds.attach.Width -gt 0 -and [Math]::Abs($taskIconBounds.Width - 16) -lt 0.01 -and
                    $taskIconBounds.Left -ge $taskBounds.attach.Left -and $taskIconBounds.Right -le $taskBounds.attach.Right) "attach icon is inside the button at $taskWidth px: $taskLabel"
                if ($taskCompact) {
                    Assert-View (-not (Test-Shown $taskAttachText) -and $taskAttach.ToolTip -ne $null) "compact strip keeps the attach button icon-only with its tooltip at $taskWidth px: $taskLabel"
                } else {
                    $taskTextBounds = Get-ViewBounds $taskAttachText
                    Assert-View ((Test-Shown $taskAttachText) -and $taskAttachText.ActualWidth + 0.01 -ge (Get-TextWidth $taskAttachText) -and
                        $taskTextBounds.Left -ge $taskIconBounds.Right -and $taskTextBounds.Right -le $taskBounds.attach.Right - 1) "attach label renders unclipped inside the button at $taskWidth px: $taskLabel"
                }
                Assert-View ($taskBounds.history.Right -le $taskBounds.new.Left + 0.01 -and $taskBounds.new.Right -le $taskBounds.attach.Left -and
                    $taskBounds.attach.Right -le $taskBounds.more.Left + 0.01 -and $taskBounds.more.Right -le $taskWidth) "strip buttons do not overlap at $taskWidth px: $taskLabel"
                if ($taskReference -eq $null) { $taskReference = $taskBounds } else {
                    foreach ($taskKey in @('history','new','attach','more')) {
                        $taskBefore = $taskReference[$taskKey]; $taskNow = $taskBounds[$taskKey]
                        Assert-View ([Math]::Abs($taskBefore.Left - $taskNow.Left) -lt 0.01 -and [Math]::Abs($taskBefore.Width - $taskNow.Width) -lt 0.01 -and
                            [Math]::Abs($taskBefore.Top - $taskNow.Top) -lt 0.01) "$taskKey keeps its rendered bounds at $taskWidth px: '$($taskAttachLabels[0])' -> '$taskLabel'"
                    }
                }
                if ($taskWidth -eq 340 -or $taskWidth -eq 900) { Save-View ("attach-$taskIndex-dark") }
            }
            $taskWidths[$taskWidth] = $taskReference.attach.Width
        }
        Assert-View ($taskWidths[340] -lt $taskWidths[520] -and [Math]::Abs($taskWidths[340] - $taskWidths[459]) -lt 0.01 -and
            [Math]::Abs($taskWidths[460] - $taskWidths[900]) -lt 0.01) 'the attach button has one width per responsive mode, compact narrower than labelled'
        # A label outside the known set grows the button instead of clipping.
        $taskAttachText.Text = $taskAttachLabels[3] + ' (' + [char]0x00E4 + 'ltere Revision ' + [char]0x2013 + ' Gr' + [char]0x00F6 + [char]0x00DF + 'e)'
        Set-ViewWidth 900
        Assert-View ($taskAttachText.ActualWidth + 0.01 -ge (Get-TextWidth $taskAttachText) -and
            (Get-ViewBounds $taskAttachText).Right -le (Get-ViewBounds $taskAttach).Right - 1 -and
            (Get-ViewBounds $taskAttach).Right -le (Get-ViewBounds $taskNeighbours.more).Left + 0.01) 'an unknown longer attach label grows the button without clipping'
    } finally {
        $taskAttachText.Text = $taskSavedLabel; $taskAttachIcon.Data = $taskSavedIcon
    }
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
