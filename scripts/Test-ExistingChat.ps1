# Dot-sourced by the real WebView fixture. No operator profile or model calls.
function Test-ExistingChat {
    $taskExisting=(Field '_chatSelection').sessionId
    & (Join-Path $taskRepo 'tools/node_modules/bun/bin/bun.exe') (Join-Path $taskRepo 'scripts/seed-chat-fixture.mjs') $taskRoot $taskExisting
    if($LASTEXITCODE -ne 0){throw 'Synthetic completed history could not be seeded'}
    Invoke-Chat 'NewProjectChatAsync'
    Wait-Native { (Field '_chatSelection').sessionId -ne $taskExisting -and (Field '_chatSelection').presentationReady } 'a separate new chat is ready before reopening historical content'
    $taskNew=(Field '_chatSelection').sessionId
    $taskPreviousDocument=JS 'window.__twinCatChat.state().documentId'
    $taskBrowser.CoreWebView2.Reload()
    Wait-JS ('window.__twinCatChat && JSON.stringify(window.__twinCatChat.state().documentId)!=='+($taskPreviousDocument|ConvertTo-Json -Compress)) 40
    Wait-JS 'window.__twinCatInstall==="ready" && window.__twinCatChat.state().presentationReady' 40
    Assert-Integration ((JS 'window.__twinCatChat.state().sessionId') -eq ('"'+$taskNew+'"')) 'reload restores the selected new chat before its historical neighbour is opened'
    Set-Snapshot 'PLC_A' 'bRun := TRUE; // existing-chat PRG' 'PRG_Ablauf' 'PROGRAM PRG_Ablauf'
    if(-not (Field '_chatPreferences').AutoAttach){Invoke-Chat 'ToggleAutoAttachAsync'}
    Wait-Native { (JS 'window.__twinCatChat.state().attachedObject') -eq '"PRG_Ablauf / PLC_A"' } 'automatic attachment works in the new neighbouring chat'
    $taskControl.GetType().GetMethod('CloseProjectChats',$taskFlags).Invoke($taskControl,@()) | Out-Null
    Invoke-Chat 'OpenHistoryAsync'
    $taskPicker=Field '_projectChatPicker'
    $taskPickerType=$taskPicker.GetType()
    Await-Task ($taskPickerType.GetProperty('SelectAsync').GetValue($taskPicker).DynamicInvoke([object[]]@($taskExisting)))|Out-Null
    Wait-Native { (Field '_chatSelection').sessionId -eq $taskExisting -and (Field '_chatSelection').presentationReady } 'selected historical conversation finishes loading'
    $taskPicker.Close()
    try { Wait-JS 'document.body.innerText.includes("Synthetischer vorhandener Chat")' }
    catch {
        Write-Output ('EXISTING_CHAT_UI '+(JS 'JSON.stringify({session:window.__twinCatChat.state().sessionId,body:document.body.innerText.slice(0,1600)})'))
        JS 'window.__existingChatProbe=null;fetch("/api/session/"+window.__twinCatChat.state().sessionId+"/message",{headers:{"x-opencode-directory":encodeURIComponent(window.__twinCatChat.state().directory),"x-opencode-directory-encoding":"uri"}}).then(async r=>{const p=await r.json();window.__existingChatProbe={status:r.status,keys:Object.keys(p),count:Array.isArray(p)?p.length:p.data?.length,error:p.error??p.code}}).catch(e=>window.__existingChatProbe={error:e.name})'|Out-Null
        Wait-JS 'window.__existingChatProbe!==null'
        Write-Output ('EXISTING_CHAT_API '+(JS 'JSON.stringify(window.__existingChatProbe)'))
        throw
    }
    Assert-Integration ((JS 'window.__twinCatChat.state().sessionId') -eq ('"'+$taskExisting+'"')) 'a nonempty historical conversation reloads with its original identity'
    try {Wait-Native { (JS 'window.__twinCatChat.state().attachedObject') -eq '"PRG_Ablauf / PLC_A"' } 'automatic attachment in a conversation with completed history'}
    catch {
        $taskAttempts=Field '_autoAttachment'
        Write-Output ('AUTO_DETAIL '+((@{enabled=(Field '_chatPreferences').AutoAttach;automating=(Field '_automating');pending=(Field '_chatActionPending');holding=(Field '_holdingView');bound=$taskControl.GetType().GetMethod('HasCurrentChatBinding',$taskFlags).Invoke($taskControl,@());failure=(Field '_automaticAttachmentFailure');key=$taskAttempts.GetType().GetField('_key',$taskFlags).GetValue($taskAttempts);attempts=$taskAttempts.GetType().GetField('_attempts',$taskFlags).GetValue($taskAttempts);confirmed=$taskAttempts.GetType().GetField('_confirmed',$taskFlags).GetValue($taskAttempts)}|ConvertTo-Json -Compress)))
        throw
    }
    Wait-JS 'document.body.innerText.includes("Automatisch") && document.body.innerText.includes("PRG_Ablauf / PLC_A")'
    Assert-Integration $true 'automatic PRG attachment survives switching from a new chat into existing completed history'
    $taskTitleState=[pscustomobject]@{Requests=0;Responses=[Collections.Generic.Queue[string]]::new();Status=200;Paths=[Collections.Generic.List[string]]::new()}
    $taskTitleBrowser=$taskBrowser
    $taskTitleState.Responses.Enqueue('Rückzug im PRG prüfen')
    $taskTitleMock=[EventHandler[Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs]]({
        param($sender,$eventArgs)
        $taskPath=([Uri]$eventArgs.Request.Uri).AbsolutePath
        if($taskPath.Contains('small-model') -and $taskTitleState.Paths.Count -lt 8){$taskTitleState.Paths.Add($taskPath)}
        if(-not $taskPath.EndsWith('/small-model/generate',[StringComparison]::Ordinal)){return}
        $taskTitleState.Requests++
        $taskText=if($taskTitleState.Responses.Count){$taskTitleState.Responses.Dequeue()}else{'synthetic title fixture exhausted'}
        $taskBody=[Text.Encoding]::UTF8.GetBytes((@{text=$taskText}|ConvertTo-Json -Compress))
        $eventArgs.Response=$taskTitleBrowser.CoreWebView2.Environment.CreateWebResourceResponse([IO.MemoryStream]::new($taskBody),$taskTitleState.Status,'Synthetic title provider','Content-Type: application/json; charset=utf-8')
    }.GetNewClosure())
    $taskBrowser.CoreWebView2.add_WebResourceRequested($taskTitleMock)
    try {
        Invoke-Chat 'OpenHistoryAsync'
        $taskGenerate=$taskPickerType.GetProperty('GenerateTitleAsync').GetValue($taskPicker)
        try {Await-Task ($taskGenerate.DynamicInvoke([object[]]@($taskExisting)))|Out-Null}
        catch {Write-Output ('TITLE_REQUESTS '+$taskTitleState.Requests+' CODE '+(JS 'window.__twinCatManage?.code')+' PATHS '+($taskTitleState.Paths|ConvertTo-Json -Compress));throw}
        $taskEntries=$taskControl.GetType().GetField('_pickerScope',$taskFlags).GetValue($taskControl).Entries
        Assert-Integration ($taskTitleState.Requests -eq 1 -and @($taskEntries|Where-Object {$_.id -eq $taskExisting -and $_.title -eq 'Rückzug im PRG prüfen'}).Count -eq 1) 'existing completed history generates and durably saves a Unicode title through the original pipeline'
        $taskTitleState.Responses.Enqueue('Alternativer Fix für Anschnitt-Klinkungen in TwinCAT S】【。')
        $taskTitleState.Responses.Enqueue('Größe prüfen – Rückzug 😀')
        Await-Task ($taskGenerate.DynamicInvoke([object[]]@($taskExisting)))|Out-Null
        $taskEntries=$taskControl.GetType().GetField('_pickerScope',$taskFlags).GetValue($taskControl).Entries
        Assert-Integration ($taskTitleState.Requests -eq 3 -and @($taskEntries|Where-Object {$_.id -eq $taskExisting -and $_.title -eq 'Größe prüfen – Rückzug 😀'}).Count -eq 1) 'malformed generated punctuation gets one bounded retry while valid German text and emoji remain intact'
        $taskTitleState.Responses.Enqueue('Fehler S】【。');$taskTitleState.Responses.Enqueue('Fehler S】【。')
        try {Await-Task ($taskGenerate.DynamicInvoke([object[]]@($taskExisting)))|Out-Null;throw 'Malformed generated punctuation was accepted'}
        catch [InvalidOperationException] {Assert-Integration ($_.Exception.Message.Contains('fehlerhaften Titel')) 'repeated malformed model output reports the title-validation failure rather than blaming empty history'}
        Invoke-Chat 'OpenHistoryAsync';Invoke-Chat 'OpenHistoryAsync'
        $taskEntries=$taskControl.GetType().GetField('_pickerScope',$taskFlags).GetValue($taskControl).Entries
        Assert-Integration ($taskTitleState.Requests -eq 5 -and @($taskEntries|Where-Object {$_.id -eq $taskExisting -and $_.title -eq 'Größe prüfen – Rückzug 😀'}).Count -eq 1) 'rejected generated output never replaces the last valid stored title'
        $taskGenerate=$taskPickerType.GetProperty('GenerateTitleAsync').GetValue($taskPicker)
        $taskTitleState.Status=503
        try {Await-Task ($taskGenerate.DynamicInvoke([object[]]@($taskExisting)))|Out-Null;throw 'A failed model response was accepted'}
        catch [InvalidOperationException] {Assert-Integration ($_.Exception.Message.Contains('Modellanbieter konnte')) 'provider failures have a distinct German error and preserve the title'}
    } finally {$taskBrowser.CoreWebView2.remove_WebResourceRequested($taskTitleMock);$taskPicker.Close()}
}
