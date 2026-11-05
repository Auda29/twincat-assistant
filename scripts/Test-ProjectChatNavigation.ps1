# Dot-sourced by Test-ChatIntegration.ps1: use its real runtime and private
# profile. Use the actual host-owned navigation view; the sidebar stays inside
# the WPF tool window instead of creating a separate mouse-capturing popup.
function Test-ProjectChatNavigation {
    Set-Snapshot 'PLC_A' 'bRun := TRUE; // navigation fixture'
    Wait-Native { (Field '_chatSelection').IsSelected -and (Field '_attachPouButton').IsEnabled -and (Field '_chatBinding').ProjectFile.EndsWith('PLC_A.plcproj') } 'project navigation fixture returns to PLC_A'
    $taskA=(Field '_chatSelection').sessionId
    $taskControl.GetType().GetMethod('CloseProjectChats',$taskFlags).Invoke($taskControl,@()) | Out-Null
    Invoke-Chat 'OpenHistoryAsync'
    $taskPicker=Field '_projectChatPicker'
    $taskPickerType=$taskPicker.GetType()
    function Open-FixtureHistory {
        $taskPicker.Close()
        Invoke-Chat 'OpenHistoryAsync'
        $taskControl.UpdateLayout(); Pump 100
        Assert-Integration ($taskPicker.IsOpen) 'native clock opens the project chat sidebar'
        Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().isSidebarOpen') -eq 'false') 'clock leaves the generic OpenChamber sidebar closed'
    }
    function Invoke-PickerAction([string]$Name,[object[]]$Arguments) {
        $taskHandler=$taskPickerType.GetProperty($Name).GetValue($taskPicker)
        Await-Task ($taskHandler.DynamicInvoke($Arguments)) | Out-Null
    }
    function Picker-Entries { return @($taskControl.GetType().GetField('_pickerScope',$taskFlags).GetValue($taskControl).Entries) }
    function Save-PanelView([string]$Name) {
        $taskOutput=Join-Path $taskRepo 'local-data/panel-sidebar-views'
        [IO.Directory]::CreateDirectory($taskOutput) | Out-Null
        $taskStream=[IO.File]::Open((Join-Path $taskOutput ($Name+'.png')),[IO.FileMode]::Create)
        try { Await-Task ($taskBrowser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png,$taskStream)) | Out-Null }
        finally { $taskStream.Dispose() }
    }

    $taskNavigationWidth=$taskWindow.Width
    try {
        $taskWindow.Width=1100; Pump 200
        Open-FixtureHistory
        $taskChatOrigin=$taskBrowser.TransformToAncestor($taskControl).Transform([Windows.Point]::new(0,0))
        Assert-Integration ($taskChatOrigin.X -ge 220 -and $taskChatOrigin.X -le 400 -and $taskBrowser.ActualWidth -ge 600) 'wide project sidebar sits beside the actual WebView and preserves readable conversation width'
        $taskBrowser.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.UIElement]::GotFocusEvent))
        Assert-Integration ($taskPicker.IsOpen) 'browser focus leaves the wide project sidebar open'
        Invoke-Chat 'OpenHistoryAsync'
        $taskControl.UpdateLayout(); Pump 100
        Assert-Integration (-not $taskPicker.IsOpen) 'clock closes the wide project sidebar'
        $taskViews=(Field '_chatBindings').ViewState()
        Assert-Integration ($taskViews.SidebarOpen($global:taskIntegrationSnapshot.Identity.ProjectIdentity) -eq $false) 'closing the wide sidebar persists a separate project presentation preference'
        $taskChatOrigin=$taskBrowser.TransformToAncestor($taskControl).Transform([Windows.Point]::new(0,0))
        Assert-Integration ([Math]::Abs($taskChatOrigin.X) -lt 1) 'closing the sidebar returns its column to the actual conversation'
        Open-FixtureHistory
        Assert-Integration ($taskViews.SidebarOpen($global:taskIntegrationSnapshot.Identity.ProjectIdentity) -eq $true) 'opening the wide sidebar durably restores its project preference'
        $taskWindow.Width=390; Pump 200
        $taskChatOrigin=$taskBrowser.TransformToAncestor($taskControl).Transform([Windows.Point]::new(0,0))
        Assert-Integration ($taskPicker.IsOpen -and [Math]::Abs($taskChatOrigin.X) -lt 1 -and $taskBrowser.ActualWidth -ge 370) 'narrow project sidebar overlays the conversation without shrinking the WebView'
        $taskBrowser.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.UIElement]::GotFocusEvent))
        Assert-Integration (-not $taskPicker.IsOpen) 'browser focus dismisses the narrow sidebar overlay'
        Open-FixtureHistory
        $taskPinButton=$taskPickerType.GetField('_pinButton',$taskFlags).GetValue($taskPicker)
        $taskPinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent)); Pump 100
        $taskBrowser.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.UIElement]::GotFocusEvent))
        Assert-Integration ($taskPicker.Pinned -and $taskPicker.IsOpen) 'pinned narrow history survives real browser focus'
        Assert-Integration ($taskViews.SidebarPinned($global:taskIntegrationSnapshot.Identity.ProjectIdentity) -eq $true) 'pinning is stored for this PLC project'
        $taskWindow.Width=650; Pump 200
        $taskChatOrigin=$taskBrowser.TransformToAncestor($taskControl).Transform([Windows.Point]::new(0,0))
        Assert-Integration ($taskPicker.Docked -and $taskChatOrigin.X -ge 270 -and $taskBrowser.ActualWidth -ge 320) 'pinned history reserves its own column below the automatic dock breakpoint'
        $taskPinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent)); Pump 100
        $taskBrowser.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.UIElement]::GotFocusEvent))
        Assert-Integration (-not $taskPicker.Pinned -and -not $taskPicker.IsOpen) 'unpin restores the transient overlay focus behavior'
        $taskWindow.Width=1100; Pump 200
        $taskPicker.Close(); $taskControl.UpdateLayout(); Pump 100
        Wait-JS 'Boolean(document.querySelector("[data-testid=twincat-session-information-toggle]"))'
        Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled') -eq 'false') 'embedded session information defaults to hidden even in a wide window'
        Save-PanelView 'session-hidden-wide'
        JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").click()' | Out-Null
        Wait-JS 'window.__twinCatSource.stores.ui.getState().workStatusPanelVisible'
        Assert-Integration ((JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").getAttribute("aria-pressed")') -eq '"true"') 'session information opens through its own right rail button'
        Pump 300; Save-PanelView 'session-shown-wide'
        JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").click()' | Out-Null
        Wait-JS '!window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled'
        $taskWindow.Width=1250; Pump 200
        Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().workStatusPanelVisible') -eq 'false') 'widening does not reopen explicitly hidden session information'
        $taskWindow.Width=650; Pump 200
        JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").click()' | Out-Null
        Wait-JS 'window.__twinCatSource.stores.ui.getState().workStatusOverlayOpen'
        Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled') -eq 'true') 'narrow session information is accessible as an explicit overlay'
        Pump 300; Save-PanelView 'session-shown-narrow'
        JS 'document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }))' | Out-Null
        Wait-JS '!window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled'
        Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().workStatusOverlayOpen') -eq 'false') 'Escape closes the overlay without leaving automatic wide-window activation enabled'

    } finally {
        $taskPicker.Close()
        $taskWindow.Width=$taskNavigationWidth; Pump 150
    }
    Open-FixtureHistory
    Assert-Integration ((Picker-Entries).Count -eq 1 -and (Picker-Entries)[0].id -eq $taskA) 'picker lists only this PLC project, excluding general and foreign chats'
    # The original composer mailbox populates its real controlled editor and
    # draft store. This checks draft ownership, without synthesizing keystrokes.
    JS 'window.__twinCatSource.stores.input.getState().setPendingInputText("Entwurf Projektchat A","replace")' | Out-Null
    Wait-JS 'document.querySelector("[data-testid=chat-input]")?.textContent.includes("Entwurf Projektchat A")'
    (Field '_attachPouButton').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    Wait-Native { (Field '_chatSelection').attachments.Length -gt 0 -and -not (Field '_chatSelection').manualAttachmentPending } 'manual snapshot is staged before switching conversations'
    $taskReference=(Field '_chatSelection').attachments[0].reference
    Open-FixtureHistory
    Invoke-PickerAction 'NewAsync' @()
    Wait-Native { (Field '_chatSelection').IsSelected -and (Field '_chatSelection').sessionId -ne $taskA -and (Field '_chatBinding').ProjectFile.EndsWith('PLC_A.plcproj') } 'plus creates a second persistent chat in the same PLC project'
    $taskB=(Field '_chatSelection').sessionId
    Assert-Integration ((JS 'document.querySelector("[data-testid=chat-input]")?.textContent.includes("Entwurf Projektchat A")') -eq 'false' -and (Field '_chatSelection').attachments.Length -eq 0) 'new project chat receives neither another chat draft nor its manual snapshot'
    Open-FixtureHistory
    Invoke-PickerAction 'RenameAsync' @($taskA,'Umbenannter älterer Projektchat')
    Assert-Integration (@(Picker-Entries | Where-Object { $_.id -eq $taskA -and $_.title -eq 'Umbenannter älterer Projektchat' }).Count -eq 1) 'rename is confirmed by durable history, not only the picker label'
    try {
        Invoke-PickerAction 'GenerateTitleAsync' @($taskA)
        throw 'An empty conversation unexpectedly generated a title.'
    } catch [System.InvalidOperationException] {
        Assert-Integration ($_.Exception.Message.Contains('abgeschlossenen Austausch')) 'native title action uses original completed-history validation and reports an empty conversation'
    }
    Open-FixtureHistory
    Assert-Integration (@(Picker-Entries | Where-Object { $_.id -eq $taskA -and $_.title -eq 'Umbenannter älterer Projektchat' }).Count -eq 1) 'failed title generation preserves the prior durable title and chat identity'
    $taskStore=Field '_chatBindings'
    $taskIdentity=$global:taskIntegrationSnapshot.Identity.ProjectIdentity
    Assert-Integration ($taskStore.SelectedForProject($taskIdentity) -eq $taskB) 'updating an older chat does not overwrite the explicitly selected chat'
    $taskPicker.Close()
    Set-Snapshot 'PLC_B'; Wait-Native { (Field '_chatBinding').ProjectFile.EndsWith('PLC_B.plcproj') } 'switch to another PLC project'
    Set-Snapshot 'PLC_A'; Wait-Native { (Field '_chatSelection').sessionId -eq $taskB } 'return restores the last selected chat rather than the most recently updated one'
    Open-FixtureHistory
    Invoke-PickerAction 'SelectAsync' @($taskA)
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskA } 'project sidebar selects an older project conversation'
    Wait-JS 'document.querySelector("[data-testid=chat-input]")?.textContent.includes("Entwurf Projektchat A")'
    Wait-Native { @((Field '_chatSelection').attachments | Where-Object { $_.reference -eq $taskReference }).Count -eq 1 } 'returning restores the manual snapshot belonging to that chat'
    Assert-Integration ($taskStore.SelectedForProject($taskIdentity) -eq $taskA) 'manual history selection is remembered durably'
    $taskStoreDirectory=$taskStore.GetType().GetField('_directory',$taskFlags).GetValue($taskStore)
    $taskNewStore=$taskStore.GetType().GetConstructor([type[]]@([string])).Invoke([object[]]@([string]$taskStoreDirectory))
    Assert-Integration ($taskNewStore.SelectedForProject($taskIdentity) -eq $taskA) 'a fresh binding store restores the saved project selection'
    Assert-Integration ($taskNewStore.ViewState().SidebarOpen($taskIdentity) -eq $true) 'a fresh binding store restores the open sidebar independently of conversation selection'
    Set-Field '_chatBindings' $taskNewStore
    Set-Snapshot 'PLC_A' 'bRun := FALSE; // another member' 'FB_Other'
    Wait-Native { (Field '_objectName').Text -eq 'FB_Other' } 'editor moves to another POU in the same PLC'
    Assert-Integration ((Field '_chatSelection').sessionId -eq $taskA) 'POU changes retain the selected conversation'
    Open-FixtureHistory
    Invoke-PickerAction 'ArchiveAsync' @($taskB)
    Assert-Integration (@(Picker-Entries | Where-Object { $_.id -eq $taskB -and $_.archived }).Count -eq 1 -and (Field '_chatSelection').sessionId -eq $taskA) 'marking another chat as done preserves the current conversation and retains its history'
    Invoke-PickerAction 'RestoreAsync' @($taskB)
    Assert-Integration (@(Picker-Entries | Where-Object { $_.id -eq $taskB -and -not $_.archived }).Count -eq 1) 'a done conversation is reopened under its original ID'
    # Marking the selected chat as done uses the same durable archive action.
    Invoke-PickerAction 'ArchiveAsync' @($taskA)
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskB } 'marking the selected chat as done opens another existing project chat'
    Open-FixtureHistory
    Invoke-PickerAction 'RestoreAsync' @($taskA)
    Invoke-PickerAction 'SelectAsync' @($taskA)
    Wait-JS 'document.querySelector("[data-testid=chat-input]")?.textContent.includes("Entwurf Projektchat A")'
    Assert-Integration ((Field '_chatSelection').sessionId -eq $taskA) 'archive and restore retain chat identity and its unsent draft'
    Open-FixtureHistory
    $taskScope=(Field '_pickerScope')
    Set-Snapshot 'PLC_B'
    Assert-Integration (-not [object]::ReferenceEquals((Field '_pickerScope'),$taskScope)) 'changing projects invalidates the previous sidebar scope before asynchronous history can act'
    $taskStale=$taskControl.GetType().GetMethod('SelectPickerChatAsync',$taskFlags).Invoke($taskControl,[object[]]@($taskScope,$taskA))
    try { Await-Task $taskStale | Out-Null; throw 'A stale project action was accepted.' } catch [System.OperationCanceledException] { }
    Wait-Native { (Field '_chatBinding').ProjectFile.EndsWith('PLC_B.plcproj') } 'stale project picker cannot redirect the newly active PLC'
    Await-Task ($taskControl.GetType().GetMethod('NewChatAsync',$taskFlags).Invoke($taskControl,[object[]]@($true))) | Out-Null
    Wait-Native { (Field '_chatBinding').IsGeneral } 'a general conversation can still be created from the host menu'
    $taskGeneral=(Field '_chatSelection').sessionId
    $taskPicker.Close()
    Await-Task ($taskControl.GetType().GetMethod('ShowProjectChatsAsync',$taskFlags).Invoke($taskControl,[object[]]@($true))) | Out-Null
    $taskGeneralEntries=Picker-Entries
    Assert-Integration (@($taskGeneralEntries | Where-Object { $_.id -eq $taskGeneral }).Count -eq 1 -and
        @($taskGeneralEntries | Where-Object { $taskEntryBinding=(Field '_chatBindings').Read((Field '_origin').AbsoluteUri,$_.id); -not $taskEntryBinding.IsGeneral }).Count -eq 0) 'general history excludes project conversations'
    $taskGeneralProject=$global:taskIntegrationSnapshot.Identity.ProjectIdentity
    $taskBeforeGeneralClose=(Field '_chatBindings').ViewState().SidebarOpen($taskGeneralProject)
    Await-Task ($taskControl.GetType().GetMethod('ShowProjectChatsAsync',$taskFlags).Invoke($taskControl,[object[]]@($true))) | Out-Null
    Assert-Integration (-not $taskPicker.IsOpen -and (Field '_chatBindings').ViewState().SidebarOpen($taskGeneralProject) -eq $taskBeforeGeneralClose) 'closing general history leaves the active PLC sidebar preference unchanged'
    $taskPicker.Close()
    Set-Snapshot 'PLC_A'
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskA } 'switching projects leaves the deliberately opened general conversation separate'
    Await-Task ($taskControl.GetType().GetMethod('ShowProjectChatsAsync',$taskFlags).Invoke($taskControl,[object[]]@($true))) | Out-Null
    Invoke-PickerAction 'SelectAsync' @($taskGeneral)
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskGeneral -and (Field '_chatBinding').IsGeneral } 'general history can reopen its existing conversation while a PLC is active'
    Open-FixtureHistory
    Assert-Integration (@(Picker-Entries | Where-Object { $_.id -eq $taskGeneral }).Count -eq 0 -and (Picker-Entries).Count -eq 2) 'the clock still shows only the current PLC when a general chat is selected'
    Invoke-PickerAction 'SelectAsync' @($taskA)
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskA } 'return from a general conversation to its active project'
    Open-FixtureHistory
    Invoke-PickerAction 'RenameAsync' @($taskB,'Neuer bearbeitet, aber nicht ausgewählt')
    $taskPicker.Close()
    JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").click()' | Out-Null
    Wait-JS 'window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled'
    Pump 1800 # Let the original settings auto-save complete before restarting its service.
    Await-Task ($taskSession.StopAsync()) | Out-Null
    $taskRestartDeadline=[DateTime]::UtcNow.AddSeconds(110)
    do {
        Pump 200
        $taskOwnedRuntime=Field '_chatSession'
    } while ((-not $taskOwnedRuntime -or -not $taskOwnedRuntime.IsRunning -or -not (Field '_chatAdapterReady')) -and [DateTime]::UtcNow -lt $taskRestartDeadline)
    Assert-Integration ($taskOwnedRuntime -and $taskOwnedRuntime.IsRunning) 'the isolated runtime reconnects after an actual service restart'
    $script:taskBrowser=Field '_browser'
    Wait-JS "window.__twinCatInstall==='ready' && window.__twinCatChat.state().authenticated" 40
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskA -and -not (Field '_chatActionPending') -and -not (Field '_automating') } 'real runtime restart restores the last selected older conversation'
    Assert-Integration ((Field '_chatSelection').sessionId -eq $taskA -and (Field '_chatBinding').ProjectFile.EndsWith('PLC_A.plcproj')) 'project selection and binding survive runtime and page recreation'
    Wait-JS 'window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled' 20
    Assert-Integration ((JS 'window.__twinCatSource.stores.ui.getState().twinCatSessionInformationEnabled') -eq 'true') 'session information choice survives the service and page restart'
    JS 'document.querySelector("[data-testid=twincat-session-information-toggle]").click()' | Out-Null

}
