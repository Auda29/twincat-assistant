param([Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$ProfileId)
$ErrorActionPreference='Stop'
# Preparation only. This script never starts an IDE, installs a payload, or sets
# a user/machine environment variable. No existing profile is imported or reused.
$local=[Environment]::GetFolderPath('LocalApplicationData')
$profile=Join-Path $local ('TwinCatAssistant\host-tests\'+$ProfileId)
$config=Join-Path $profile 'native-chat.json'
$programFiles=if($env:ProgramW6432){$env:ProgramW6432}else{[Environment]::GetFolderPath('ProgramFiles')}
$dotnet=Join-Path $programFiles 'dotnet'
if(!(Test-Path -LiteralPath (Join-Path $dotnet 'dotnet.exe') -PathType Leaf)){throw 'System dotnet is required.'}
for($current=$profile;$current;$current=Split-Path $current -Parent){
    if((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Redirected private profile path.'}
}
if(Test-Path -LiteralPath $profile){throw 'Use a new profile id. Existing profiles are never overwritten.'}
New-Item -ItemType Directory -Path $profile | Out-Null
[IO.File]::WriteAllText($config,(@{StateRoot=(Join-Path $profile 'state');DotNetRoot=$dotnet}|ConvertTo-Json))
[pscustomobject]@{
    ConfigurationPath=$config;StateRoot=(Join-Path $profile 'state')
    CredentialDirectory=(Join-Path $profile 'native-chat');WebViewRoot=(Join-Path $profile 'WebView2')
    ProcessEnvironment=@{TWINCAT_ASSISTANT_TEST_CONFIG=$config}
    OperatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    IdeStarted=$false;InstallerBindingRequired=$true
} | ConvertTo-Json -Depth 3
