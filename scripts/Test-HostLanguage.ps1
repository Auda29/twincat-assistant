param(
 [string]$HostAssembly=(Join-Path $PSScriptRoot '../native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'),
 [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) ('local-data/host-language-'+[guid]::NewGuid().ToString('N')))
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
$profileInfo=& (Join-Path $PSScriptRoot 'New-PrivateHostAcceptanceProfile.ps1') -ProfileId ('language-'+[guid]::NewGuid().ToString('N')) | ConvertFrom-Json
$env:TWINCAT_ASSISTANT_TEST_CONFIG=$profileInfo.ConfigurationPath
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$assets=Get-Content (Join-Path $root 'native/IdeHost/X86/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoot=@($assets.packageFolders.PSObject.Properties)[0].Name
$global:languageFixtureReferences=@{}
foreach($package in @($assets.targets.PSObject.Properties)[0].Value.PSObject.Properties){
 foreach($reference in $package.Value.compile.PSObject.Properties){if($reference.Name.EndsWith('.dll')){
  $path=Join-Path $packageRoot ($package.Name.ToLowerInvariant()+'/'+$reference.Name)
  $global:languageFixtureReferences[[IO.Path]::GetFileNameWithoutExtension($path)]=$path
 }}
}
foreach($dll in Get-ChildItem -LiteralPath $bin -Filter *.dll){$global:languageFixtureReferences[$dll.BaseName]=$dll.FullName}
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
 $name=(New-Object Reflection.AssemblyName($eventArgs.Name)).Name
 if($global:languageFixtureReferences.ContainsKey($name)){return [Reflection.Assembly]::LoadFrom($global:languageFixtureReferences[$name])};return $null
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
# The shared state transition is synchronous; IDE menu dispatch uses the IDE-owned JTF.
function Select-Language([string]$language){$control.GetType().GetMethod('RememberLanguage',$flags).Invoke($control,@($language))}
function Shot([string]$name,[int]$width){
 # Capture native chrome without pretending that the hidden WebView is ready.
 (Field '_splash').Visibility=[Windows.Visibility]::Collapsed
 (Field '_strip').Visibility=[Windows.Visibility]::Visible
 (Field '_browser').Visibility=[Windows.Visibility]::Collapsed
 $control.Measure((New-Object Windows.Size($width,440)));$control.Arrange((New-Object Windows.Rect(0,0,$width,440)));$control.UpdateLayout()
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap($width,440,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($control);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder;$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[IO.File]::Create((Join-Path $OutputDirectory ($name+'-'+$width+'.png')));try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
try{
 $assembly=[Reflection.Assembly]::LoadFrom($HostAssembly)
 $shell=[Reflection.Assembly]::LoadFrom($global:languageFixtureReferences['Microsoft.VisualStudio.Shell.15.0'])
 $shell.GetType('Microsoft.VisualStudio.Shell.ThreadHelper').GetMethod('SetUIThread',[Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null,@())
 $preferencesType=$assembly.GetType('TwinCatAssistant.Native.NativeChatPreferences')
 $preferences=[Activator]::CreateInstance($preferencesType,@([string]$profileInfo.CredentialDirectory))
 $preferences.AutoAttach=$true;$preferences.Language='de';$preferences.Save()
 Make-Control
 Check ((Field '_moreButton').ToolTip -eq 'Weitere Aktionen') 'native German controls'
 $identity=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.ActiveEditorIdentity'),@([int]1,'C:/Synthetic/Fixture.sln','C:/Synthetic/Fixture.plcproj','C:/Synthetic/FB.TcPOU','POUs^FB'))
 $snapshot=[Activator]::CreateInstance($assembly.GetType('TwinCatAssistant.Native.ActiveEditorSnapshot'))
 $snapshot.Identity=$identity;$snapshot.ObjectName='Gespeichert';$snapshot.ObjectKind='FB'
 $snapshot.Declaration="FUNCTION_BLOCK Deutsch`r`nVAR`r`n`tText : STRING := 'English';`r`nEND_VAR";$snapshot.Implementation='// synthetic source only'
 $snapshot.SourceNotice='Unsaved editor text is not verified. No authoritative dirty-state evidence from the exact running document.'
 $control.GetType().GetMethod('RenderContext',$flags).Invoke($control,[object[]]@($snapshot,$null))
 $source=(Field '_source').Text
 Select-Language 'en'
 Check ((Field '_moreButton').ToolTip -eq 'More actions') 'native English controls update without recreating the control'
 Check ((Field '_objectName').Text -eq 'Gespeichert') 'object name equal to a UI label is unchanged'
 Check ((Field '_source').Text -ceq $source) 'source preview is byte-identical after switching'
 Check ((Field '_sourceNotice').Text -like 'Snapshot from TwinCAT*') 'English source-state notice'
 $saved=Get-Content (Join-Path $profileInfo.CredentialDirectory 'preferences.json') -Raw | ConvertFrom-Json
 Check ($saved.language -eq 'en' -and $saved.autoAttach -eq $true) 'language is saved with auto-attach retained'
 foreach($width in @(430,980)){Shot 'en' $width}
 $control.Dispose();$control=$null
 Make-Control
 Check ((Field '_moreButton').ToolTip -eq 'More actions') 'new control restores English from the private preference file'
 $control.GetType().GetMethod('RenderContext',$flags).Invoke($control,[object[]]@($snapshot,$null))
 Select-Language 'de'
 Check ((Field '_moreButton').ToolTip -eq 'Weitere Aktionen') 'German round trip'
 $legacyPath=Join-Path $OutputDirectory 'legacy-preferences';[IO.Directory]::CreateDirectory($legacyPath)|Out-Null
 $legacyFile=Join-Path $legacyPath 'preferences.json';[IO.File]::WriteAllText($legacyFile,'{"autoAttach":true}')
 $legacy=$preferencesType.GetMethod('Load').Invoke($null,[object[]]@([string]$legacyPath))
 Check ($legacy.AutoAttach -and $null -eq $legacy.Language) 'old preferences remain readable without a migration'
 [IO.File]::WriteAllText($legacyFile,'{"autoAttach":true,"language":"fr"}')
 $before=[IO.File]::ReadAllBytes($legacyFile)
 $unknown=$preferencesType.GetMethod('Load').Invoke($null,[object[]]@([string]$legacyPath))
 Check ($null -eq $unknown.Language -and [Convert]::ToBase64String($before) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacyFile))) 'unsupported saved language falls back without rewriting the file'
 foreach($width in @(430,980)){Shot 'de' $width}
 Check ($null -eq (Field '_reader') -and $null -eq (Field '_chatSession')) 'no IDE reader or backend was created'
 @{checks=@($checks);count=$checks.Count;hostSha256=(Get-FileHash $HostAssembly -Algorithm SHA256).Hash;profile=$profileInfo.ConfigurationPath;ideStarted=$false;backendStarted=$false;operatorStateUsed=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
 Write-Output ('Evidence: '+$OutputDirectory)
}finally{
 if($control){$control.Dispose()}
 [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
 $env:TWINCAT_ASSISTANT_TEST_CONFIG=$oldOverride
}
