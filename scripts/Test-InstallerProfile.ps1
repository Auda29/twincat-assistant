$ErrorActionPreference='Stop'
. "$PSScriptRoot/installer/HostTransaction.ps1"
. "$PSScriptRoot/installer/NativeProfile.ps1"
$base=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) ('local-data/test-installer-profile-'+[guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory $base|Out-Null
$local=Join-Path $base 'local-app-data';$profile=[IO.Path]::GetFullPath((Join-Path $local 'TwinCatAssistant/host-tests/valid_01'))
New-Item -ItemType Directory $profile -Force|Out-Null
$global=Join-Path $local 'TwinCatAssistant/native-chat.json';$globalRoot=Join-Path $base 'global-state'
[IO.File]::WriteAllText($global,(@{StateRoot=$globalRoot;DotNetRoot='old-sdk-hint';NodePath='never-executed'}|ConvertTo-Json))
$original=[IO.File]::ReadAllBytes($global)
$systemDotNet=Join-Path $env:ProgramFiles 'dotnet'
$config=Join-Path $profile 'native-chat.json';$root=Join-Path $profile 'state'
function Write-TestConfig($value){[IO.File]::WriteAllText($config,($value|ConvertTo-Json),[Text.UTF8Encoding]::new($false))}
function Refused([scriptblock]$action){$failed=$false;try{& $action|Out-Null}catch{$failed=$true};if(-not $failed){throw 'Unsafe profile accepted'};if(-not [Linq.Enumerable]::SequenceEqual([byte[]]$original,[byte[]][IO.File]::ReadAllBytes($global))){throw 'Global configuration changed'};if(Test-Path $root){throw 'Preflight created state'}}
$prior=[Environment]::GetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG','Process')
try {
 [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$null,'Process')
 $result=Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet
 if($result.StateRoot -cne $globalRoot){throw 'Normal user selection changed'}
 [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$config,'Process')
 Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}
 Write-TestConfig @{StateRoot=$root;DotNetRoot=$systemDotNet}
 $result=Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet
 if($result.StateRoot -cne $root -or $result.Contract -cne 'host-test-override-v1'){throw 'Valid override not selected'}
 foreach($value in @(@{StateRoot=$globalRoot;DotNetRoot=$systemDotNet},@{StateRoot=$root;DotNetRoot='C:\OtherSDK'},@{StateRoot=$root;DotNetRoot=$systemDotNet;NodePath='C:\foreign.exe'},@{StateRoot=$root;DotNetRoot=$systemDotNet;RepositoryRoot='C:\foreign'},@{StateRoot=$root;DotNetRoot=$systemDotNet;McpSettingsPath='C:\foreign.json'})) {
  Write-TestConfig $value;Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}
 }
 Write-TestConfig @{StateRoot=$root;DotNetRoot=$systemDotNet}
 foreach($path in @((Join-Path $base 'native-chat.json'),(Join-Path $profile '../valid_01/native-chat.json'),(Join-Path $local 'TwinCatAssistant/host-tests/bad.id/native-chat.json'))) {
  [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$path,'Process');Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}
 }
 [Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$config,'Process')
 foreach($name in @('WEBVIEW2_USER_DATA_FOLDER','WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS')) {
  $previous=[Environment]::GetEnvironmentVariable($name,'Process')
  try {[Environment]::SetEnvironmentVariable($name,'unsafe-test-value','Process');Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}}
  finally {[Environment]::SetEnvironmentVariable($name,$previous,'Process')}
 }
 $priorLocal=$env:LOCALAPPDATA;$blockedInstall=Join-Path $base 'must-not-create'
 try {
  $env:LOCALAPPDATA=$local;$blocked=$false
  try {& "$PSScriptRoot/installer/Invoke-Installer.ps1" -Action Install -InstallRoot $blockedInstall|Out-Null}
  catch {if($_.Exception.Message -notmatch 'isolated extension-search target'){throw};$blocked=$true}
  if(-not $blocked -or (Test-Path $blockedInstall)){throw 'Registered-host installation was not blocked before mutation'}
 } finally {$env:LOCALAPPDATA=$priorLocal}
 $link=Join-Path $profile 'config-link.json';New-Item -ItemType HardLink -Path $link -Target $config|Out-Null
 Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}
 Remove-Item -LiteralPath $link
 $outside=Join-Path $base 'outside';New-Item -ItemType Directory $outside|Out-Null
 $junction=Join-Path $profile 'redirected';New-Item -ItemType Junction -Path $junction -Target $outside|Out-Null
 Refused {Resolve-InstallerProfile -LocalAppData $local -SystemDotNetRoot $systemDotNet}
 Write-Output 'Installer profile preflight passed: normal path, valid override, missing/foreign/extra-field/path/hardlink/reparse refusal, unchanged global bytes and no state creation.'
} finally {[Environment]::SetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG',$prior,'Process')}
