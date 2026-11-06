param([Parameter(Mandatory=$true)][string]$MainCheckout,[int]$ViewWidth=980,[string]$CaseId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$taskBin=Join-Path $MainCheckout 'artifacts/delivery-20261105/application/host/x86'
[Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'Microsoft.Web.WebView2.Core.dll'))|Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'Microsoft.Web.WebView2.Wpf.dll'))|Out-Null
$env:PATH=(Join-Path $taskBin 'runtimes/win-x86/native')+';'+$env:PATH
$taskRoot=Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/prompt-renderer'
$taskOut=Join-Path (Split-Path $PSScriptRoot -Parent) ('tests/prompt-evaluation/evidence/renderer/'+$ViewWidth)
[IO.Directory]::CreateDirectory($taskOut)|Out-Null
function Pump([int]$ms){$f=New-Object Windows.Threading.DispatcherFrame;$t=New-Object Windows.Threading.DispatcherTimer;$t.Interval=[TimeSpan]::FromMilliseconds($ms);$t.add_Tick({$f.Continue=$false}.GetNewClosure());$t.Start();try{[Windows.Threading.Dispatcher]::PushFrame($f)}finally{$t.Stop()}}
function Await($task){$deadline=[DateTime]::UtcNow.AddSeconds(15);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $deadline){throw 'WebView fixture timeout'};Pump 50};$task.GetAwaiter().GetResult()}
function JS([string]$text){Await ($taskView.CoreWebView2.ExecuteScriptAsync($text))}
$taskWindow=New-Object Windows.Window;$taskWindow.Width=$ViewWidth;$taskWindow.Height=900;$taskWindow.Title='Synthetic prompt renderer acceptance'
$taskView=New-Object Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
$taskWindow.Content=$taskView
try{
 $taskWindow.Show(); [Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new([Windows.Threading.Dispatcher]::CurrentDispatcher)); Pump 100
 $taskEnv=Await ([Microsoft.Web.WebView2.Core.CoreWebView2Environment]::CreateAsync($null,(Join-Path $env:TEMP ('tc-prompt-webview-'+[Guid]::NewGuid().ToString('N'))),$null))
 Await ($taskView.EnsureCoreWebView2Async($taskEnv))|Out-Null
 $taskView.CoreWebView2.SetVirtualHostNameToFolderMapping('prompt-fixture.test',$taskRoot,[Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind]::Allow)
 $taskView.CoreWebView2.Navigate('https://prompt-fixture.test/index.html')
 $deadline=[DateTime]::UtcNow.AddSeconds(15)
 do {Pump 150;$ready=JS '!!window.fixtureAnswers && document.querySelectorAll("pre code").length>0'}while($ready -ne 'true' -and [DateTime]::UtcNow -lt $deadline)
 if($ready -ne 'true'){throw ('Renderer did not mount: '+(JS 'JSON.stringify({url:location.href,ready:document.readyState,html:document.documentElement.outerHTML.slice(0,1500),errors:window.fixtureErrors,body:document.body.innerText.slice(0,700)})'))}
 $taskIds=(JS 'Object.keys(fixtureAnswers)')|ConvertFrom-Json
 $taskResults=@(); if($CaseId){$taskIds=@($taskIds|Where-Object {$_ -eq $CaseId}); $taskExisting=Join-Path $taskOut 'webview.json';if(Test-Path -LiteralPath $taskExisting){$taskResults=@((Get-Content -LiteralPath $taskExisting -Raw|ConvertFrom-Json).results|Where-Object {$_.id -ne $CaseId})}}
 foreach($taskId in $taskIds){
   JS ('showCase('+($taskId|ConvertTo-Json -Compress)+')')|Out-Null;Pump 1400
   $taskResult=JS 'JSON.stringify({id:document.getElementById("case").textContent,errors:fixtureErrors,blocks:[...document.querySelectorAll("pre code")].map(e=>({text:e.textContent,language:e.className,whiteSpace:getComputedStyle(e).whiteSpace,tabSize:getComputedStyle(e).tabSize,header:e.closest("[data-component=markdown-code]").firstElementChild.textContent,color:getComputedStyle(e).color})),buttons:[...document.querySelectorAll("button")].map(e=>({label:e.getAttribute("aria-label"),title:e.title,action:e.getAttribute("data-md-action")}))})'
   $taskResult=($taskResult|ConvertFrom-Json)|ConvertFrom-Json
   if($taskResult.errors.Count){throw ('Renderer exception '+($taskResult.errors -join '; '))}
   # Actual production handler input; never touch the user's OS clipboard.
   JS 'fixtureCopies.length=0;document.querySelectorAll("[data-md-action=copy-code]").forEach(e=>e.click())'|Out-Null;Pump 100
   $taskCopies=(JS 'JSON.stringify(fixtureCopies)')|ConvertFrom-Json|ConvertFrom-Json
   $taskResults+=@{id=$taskId;render=$taskResult;copyHandlerTexts=@($taskCopies)}
   if($taskResult.blocks.Count -gt 0){
     $taskStream=[IO.File]::Create((Join-Path $taskOut ($taskId+'.png')))
     try{Await ($taskView.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png,$taskStream))|Out-Null}finally{$taskStream.Dispose()}
   }
   Write-Output ('RENDER '+$taskId+' blocks='+$taskResult.blocks.Count+' copy='+$taskCopies.Count)
 }
 [IO.File]::WriteAllText((Join-Path $taskOut 'webview.json'),(@{date=[DateTime]::UtcNow.ToString('o');width=$ViewWidth;theme='pinned OpenChamber default light';control=$taskView.GetType().FullName;browserVersion=$taskView.CoreWebView2.Environment.BrowserVersionString;results=$taskResults}|ConvertTo-Json -Depth 35),[Text.UTF8Encoding]::new($false))
}finally{if($taskView){$taskView.Dispose()};$taskWindow.Close()}
