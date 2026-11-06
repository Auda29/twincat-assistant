param([ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$ProfileId='local-ui-20261106-7f003913')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$logs=Join-Path $repo 'local-data'
[void][IO.Directory]::CreateDirectory($logs)
Write-Host 'Wartungs-Vorbereitung gestartet. Kein Austausch der Installation.'
Start-Transcript -LiteralPath (Join-Path $logs 'local-ui-maintenance-prepare.log') -Append | Out-Null
try {
 Write-Host '1/3: Pruefe laufende Prozesse ...'
 $blocked=@(Get-Process -Name TcXaeShell,TcXaeShell64,devenv,node,opencode,WindowsHost,twincat-knowledge,dotnet -ErrorAction SilentlyContinue)
 if($blocked.Count){$blocked | Select-Object Id,ProcessName | Format-Table;throw 'Close these applications normally. No process was stopped.'}
 $config=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('TwinCatAssistant\host-tests\'+$ProfileId+'\native-chat.json')
 if(-not (Test-Path -LiteralPath $config)){& (Join-Path $repo 'scripts\New-PrivateHostAcceptanceProfile.ps1') -ProfileId $ProfileId | Out-Host}
 Write-Host '2/3: Host- und Dateipruefung, danach Sicherung/Paketkopie. Fortschritt folgt ...'
 Import-Module (Join-Path $PSScriptRoot 'Maintenance.psm1') -Force
 $plan=New-AssistantMaintenancePlan -RunId $ProfileId -ConfigurationPath $config -PackageRoot (Join-Path $repo 'artifacts\alpha-maintenance-final-20261106\application') -Acknowledgement PREPARE_ONLY_NO_HOST_WRITE
 $result=Join-Path $logs 'local-ui-maintenance-plan-result.json'
 $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $result -Encoding UTF8
 Write-Host ('3/3: Preparation only. Plan result saved to '+$result)
 $plan | Format-List
} finally {Stop-Transcript | Out-Null}
