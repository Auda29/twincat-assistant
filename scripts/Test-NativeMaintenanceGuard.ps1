param([string]$HostAssembly = (Join-Path $PSScriptRoot '../native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.dll'))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Core'){throw 'Run with Windows PowerShell 5.1 for .NET Framework AppDomain shadow-copy checks.'}
Add-Type -AssemblyName System.Web.Extensions,System.Security,System.Net.Http
$repo=Split-Path $PSScriptRoot -Parent
$run=Join-Path $repo ('local-data\maintenance-guard-'+[guid]::NewGuid().ToString('N'))
$hostDir=Join-Path $run 'host'
$package=Join-Path $run 'synthetic-package'
$local=[Environment]::GetFolderPath('LocalApplicationData')
$profile=Join-Path $local ('TwinCatAssistant\host-tests\maintenance-check-'+[guid]::NewGuid().ToString('N'))
$config=Join-Path $profile 'native-chat.json'
$marker=Join-Path $hostDir 'assistant-test-maintenance.json'
$selector=Join-Path $hostDir 'assistant-installation.json'
$copy=Join-Path $hostDir 'TwinCatAssistant.IdeHost.dll'
$variable='TWINCAT_ASSISTANT_TEST_CONFIG'
$old=[Environment]::GetEnvironmentVariable($variable,'Process')
$programFiles=if($env:ProgramW6432){$env:ProgramW6432}else{[Environment]::GetFolderPath('ProgramFiles')}
$checks=[Collections.Generic.List[string]]::new()
function Assert([bool]$condition,[string]$name){if(!$condition){throw "FAIL $name"};$checks.Add($name);Write-Output "PASS $name"}
function Reject([scriptblock]$action,[string]$name){$failed=$false;try{& $action|Out-Null}catch{$failed=$true};Assert $failed $name}
function Write-Json([string]$path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 8 -Compress))}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Restore-Marker{Write-Json $marker $valid}
function Reject-Paths([string]$name){foreach($property in @('ConfigurationPath','CredentialDirectory','WebViewUserDataFolder')){Reject {$probe.Read($copy,$property)} ($name+' before '+$property)}}
New-Item -ItemType Directory -Force $hostDir,$package,$profile|Out-Null
Copy-Item -LiteralPath $HostAssembly -Destination $copy
$probeDll=Join-Path $run 'MaintenanceGuardProbe.dll'
Add-Type -Path (Join-Path $PSScriptRoot 'fixtures\MaintenanceGuardProbe.cs') -OutputAssembly $probeDll
[Reflection.Assembly]::LoadFrom($probeDll)|Out-Null
$setup=New-Object AppDomainSetup
$setup.ApplicationBase=$hostDir
$domain=[AppDomain]::CreateDomain('SyntheticMaintenanceHost',$null,$setup)
$probe=$domain.CreateInstanceFromAndUnwrap($probeDll,'MaintenanceGuardProbe')
$shadow=$null
try{
    $names=@('runtime/node/node.exe','runtime/mcp/default-settings.json','runtime/mcp/knowledge/twincat-knowledge.exe','runtime/mcp/engineering/packages/mcp/dist/index.js','src/native-chat.mjs','src/native-mcp.mjs','src/native-knowledge.mjs','src/native-chat-model.mjs','src/package-runtime.json','src/package-integrity.mjs','scripts/application-manifest.mjs','native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe')
    $entries=foreach($name in $names){$file=Join-Path $package $name;New-Item -ItemType Directory -Force (Split-Path $file -Parent)|Out-Null;[IO.File]::WriteAllText($file,'synthetic metadata fixture; never execute');@{path=$name;size=(Get-Item -LiteralPath $file).Length;sha256=(Hash $file)}}
    Write-Json (Join-Path $package 'application-manifest.json') @{schemaVersion=1;version='maintenance-fixture';platform='win32';architecture='ia32';files=@($entries)}
    Write-Json $selector @{schemaVersion=1;targetId='tcxaeshell-x86-tc4024';version='maintenance-fixture';applicationRoot=$package}
    Write-Json $config @{StateRoot=(Join-Path $profile 'state');DotNetRoot=(Join-Path $programFiles 'dotnet')}
    $valid=@{schemaVersion=1;configurationPath=$config;configurationSha256=(Hash $config);operatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;selectorSha256=(Hash $selector)}
    [Environment]::SetEnvironmentVariable($variable,$null,'Process')
    Assert ($probe.Read($copy,'ConfigurationPath') -eq (Join-Path $local 'TwinCatAssistant\native-chat.json')) 'unmarked public configuration default unchanged'
    Assert ($probe.Read($copy,'CredentialDirectory') -eq (Join-Path $local 'TwinCatAssistant\native-chat')) 'unmarked public credential preference binding defaults unchanged'
    Assert ($probe.Read($copy,'WebViewUserDataFolder') -eq (Join-Path $local ('TwinCatAssistant\WebView2\'+$PID))) 'unmarked public WebView default unchanged'
    Restore-Marker
    Reject-Paths 'marked host rejects no override'
    Reject {$probe.LoadState($copy)} 'marked public Load rejects before operator config'
    foreach($name in @('NativeChatCredentials','NativeChatPreferences','ChatBindingStore')){Reject {$probe.ConstructDefault($copy,$name)} ('marked default constructor blocks '+$name)}
    [Environment]::SetEnvironmentVariable($variable,(Join-Path $profile 'wrong.json'),'Process')
    Reject-Paths 'marked host rejects wrong override'
    [Environment]::SetEnvironmentVariable($variable,$config,'Process')
    Assert ($probe.Read($copy,'ConfigurationPath') -eq $config) 'marked exact configuration accepted'
    Assert ($probe.Read($copy,'CredentialDirectory') -eq (Join-Path $profile 'native-chat')) 'marked credentials preferences bindings stay private'
    Assert ($probe.LoadState($copy) -eq (Join-Path $profile 'state')) 'marked public Load selects private state with synthetic installer package'
    $otherProfile=Join-Path $local ('TwinCatAssistant\host-tests\other-maintenance-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $otherProfile|Out-Null
    $otherConfig=Join-Path $otherProfile 'native-chat.json'
    Write-Json $otherConfig @{StateRoot=(Join-Path $otherProfile 'state');DotNetRoot=(Join-Path $programFiles 'dotnet')}
    [Environment]::SetEnvironmentVariable($variable,$otherConfig,'Process')
    Reject-Paths 'another otherwise valid private profile rejected'
    [Environment]::SetEnvironmentVariable($variable,$config,'Process')
    $bad=$valid.Clone();$bad.operatorSid='S-1-0-0';Write-Json $marker $bad
    Reject-Paths 'wrong SID rejected'
    Restore-Marker
    $original=[IO.File]::ReadAllBytes($config);[IO.File]::AppendAllText($config,' ')
    Reject-Paths 'byte-tampered config rejected'
    [IO.File]::WriteAllBytes($config,$original)
    $originalSelector=[IO.File]::ReadAllBytes($selector);[IO.File]::AppendAllText($selector,' ')
    Reject-Paths 'byte-tampered selector rejected'
    [IO.File]::WriteAllBytes($selector,$originalSelector)
    Move-Item -LiteralPath $selector -Destination ($selector+'.saved')
    Reject-Paths 'missing selector rejected'
    Move-Item -LiteralPath ($selector+'.saved') -Destination $selector
    foreach($json in @('{}','null','[]','{"schemaVersion":2}',('x'*65537),'{"schemaVersion":1,','{"schemaVersion":1,"schemaVersion":1,"configurationPath":"x","configurationSha256":"x","operatorSid":"x"}')){
        [IO.File]::WriteAllText($marker,$json);Reject {$probe.Read($copy,'CredentialDirectory')} 'malformed or oversized marker rejected'
    }
    $bad=$valid.Clone();$bad.extra='forbidden';Write-Json $marker $bad
    Reject {$probe.Read($copy,'ConfigurationPath')} 'unknown marker field rejected'
    $bad=$valid.Clone();$bad.configurationSha256=$bad.configurationSha256.ToUpperInvariant();Write-Json $marker $bad
    Reject {$probe.Read($copy,'ConfigurationPath')} 'noncanonical hash rejected'
    Restore-Marker
    $json=[IO.File]::ReadAllText($marker)
    [IO.File]::WriteAllText($marker,$json.Insert(1,'"schemaVersion":1,'))
    Reject {$probe.Read($copy,'ConfigurationPath')} 'duplicate marker key with otherwise valid fields rejected'
    Restore-Marker
    $markerBytes=[IO.File]::ReadAllBytes($marker)
    $hard=Join-Path $run 'marker-source.json';[IO.File]::WriteAllBytes($hard,$markerBytes);Remove-Item -LiteralPath $marker
    New-Item -ItemType HardLink -Path $marker -Target $hard|Out-Null
    Reject-Paths 'hardlinked marker rejected'
    Remove-Item -LiteralPath $marker;Restore-Marker
    Move-Item -LiteralPath $config -Destination ($config+'.saved')
    New-Item -ItemType HardLink -Path $config -Target ($config+'.saved')|Out-Null
    Reject-Paths 'hardlinked config rejected'
    Remove-Item -LiteralPath $config;Move-Item -LiteralPath ($config+'.saved') -Destination $config
    Move-Item -LiteralPath $config -Destination ($config+'.saved')
    Reject-Paths 'missing private config rejected'
    Move-Item -LiteralPath ($config+'.saved') -Destination $config
    Move-Item -LiteralPath $profile -Destination ($profile+'-saved')
    New-Item -ItemType Junction -Path $profile -Target ($profile+'-saved')|Out-Null
    Reject-Paths 'redirected config ancestor rejected'
    [IO.Directory]::Delete($profile);Move-Item -LiteralPath ($profile+'-saved') -Destination $profile
    Move-Item -LiteralPath $selector -Destination ($selector+'.saved')
    New-Item -ItemType HardLink -Path $selector -Target ($selector+'.saved')|Out-Null
    Reject-Paths 'hardlinked selector rejected'
    Remove-Item -LiteralPath $selector;Move-Item -LiteralPath ($selector+'.saved') -Destination $selector
    Remove-Item -LiteralPath $marker
    New-Item -ItemType Junction -Path $marker -Target $package|Out-Null
    Reject-Paths 'redirected marker rejected'
    [IO.Directory]::Delete($marker);Restore-Marker
    $shadowSetup=New-Object AppDomainSetup
    $shadowSetup.ApplicationBase=$hostDir;$shadowSetup.ShadowCopyFiles='true';$shadowSetup.ShadowCopyDirectories=$hostDir;$shadowSetup.CachePath=(Join-Path $run 'shadow-cache');$shadowSetup.ApplicationName='MaintenanceShadowProbe'
    $shadow=[AppDomain]::CreateDomain('SyntheticShadowMaintenanceHost',$null,$shadowSetup)
    $shadowProbe=$shadow.CreateInstanceFromAndUnwrap($probeDll,'MaintenanceGuardProbe')
    $identity=$shadowProbe.Identity($copy)
    Assert (([Uri]$identity[0]).LocalPath -eq $copy) 'shadow copied assembly CodeBase retains original host'
    Assert ($identity[1] -ne $copy) 'shadow copied assembly Location differs from original host'
    Assert ($shadowProbe.Read($copy,'ConfigurationPath') -eq $config) 'shadow copy reads original marker and accepts bound config'
    [Environment]::SetEnvironmentVariable($variable,$null,'Process')
    Reject {$shadowProbe.Read($copy,'CredentialDirectory')} 'shadow copy cannot escape original marker with missing override'
    [Environment]::SetEnvironmentVariable($variable,$config,'Process')
    [IO.File]::WriteAllText((Join-Path (Split-Path $identity[1] -Parent) 'assistant-test-maintenance.json'),'malformed cache marker must be ignored')
    Assert ($shadowProbe.Read($copy,'ConfigurationPath') -eq $config) 'shadow cache marker cannot replace original marker'
    Remove-Item -LiteralPath $marker
    [Environment]::SetEnvironmentVariable($variable,$null,'Process')
    Assert ($probe.Read($copy,'CredentialDirectory') -eq (Join-Path $local 'TwinCatAssistant\native-chat')) 'removing synthetic marker restores unmarked legacy contract'
    @{count=$checks.Count;checks=@($checks);assemblySha256=(Hash $copy);shadowIdentity=$identity;ideStarted=$false;backendStarted=$false;globalMutation=$false}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'result.json') -Encoding UTF8
    Write-Output "Evidence: $run\result.json"
}finally{
    if($shadow){[AppDomain]::Unload($shadow)}
    [AppDomain]::Unload($domain)
    [Environment]::SetEnvironmentVariable($variable,$old,'Process')
}
