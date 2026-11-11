$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/installer/HostTransaction.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) ('tc-registration-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
try {
 $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $src = Join-Path $root 'interop.cs'; $interop = Join-Path $root 'TCatSysManagerLib.dll'
 '[assembly:System.Reflection.AssemblyVersion("1.0.0.0")] public class InteropType {}' | Set-Content $src
 & $csc /nologo /target:library "/out:$interop" $src
 if ($LASTEXITCODE) { throw 'Interop fixture compile failed.' }
 $src2=Join-Path $root 'host.cs'; $hostDll=Join-Path $root 'TwinCatAssistant.IdeHost.dll'
 'public class HostType { public InteropType Value; }' | Set-Content $src2
 & $csc /nologo /target:library "/reference:$interop" "/out:$hostDll" $src2
 if ($LASTEXITCODE) { throw 'Host fixture compile failed.' }
 $hostRoot=Join-Path $root 'fake-host'; $ext=Join-Path $hostRoot 'Common7/IDE/Extensions'
 New-Item -ItemType Directory $ext -Force | Out-Null
 $marker=Join-Path $ext 'extensions.configurationchanged'; Set-Content $marker 'keep'
 $target=@((Get-Content "$PSScriptRoot/../native/host-targets.json" -Raw | ConvertFrom-Json).targets | Where-Object { $_.id -eq 'tcxaeshell-x86-tc4024' })[0]
 function New-Package($version) {
  $p=Join-Path $root "package-$version"; $h=Join-Path $p 'host/x86'; New-Item -ItemType Directory $h -Force | Out-Null
  Copy-Item $hostDll $h
  '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="TwinCatAssistant.Native.X86" Version="1.0.0" /></Metadata><Installation><InstallationTarget Id="Microsoft.VisualStudio.IntegratedShell"><ProductArchitecture>x86</ProductArchitecture></InstallationTarget></Installation></PackageManifest>' | Set-Content (Join-Path $h 'extension.vsixmanifest')
  Set-Content (Join-Path $h 'payload.txt') $version
  $files=@(@('TwinCatAssistant.IdeHost.dll','extension.vsixmanifest','payload.txt') | ForEach-Object { Get-Item (Join-Path $h $_) } | ForEach-Object { @{path=$_.FullName.Substring($p.Length+1).Replace('\','/');size=$_.Length;sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()} })
  @{schemaVersion=1;version=$version;platform='win32';architecture='x64';files=$files} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $p 'application-manifest.json')
  return $p
 }
 $p1=New-Package '1.0.0'; $p2=New-Package '1.1.0'; $p3=New-Package '1.2.0'
 $install=Join-Path $root 'owned'; $args=@{InstallRoot=$install;HostRoot=$hostRoot;Interop=$interop;Target=$target}
 $state=Invoke-HostTransaction @args -Action Install -Package $p1
 $payload=Join-Path $ext 'TwinCatAssistant.Native.X86'
 if ((Get-Content (Join-Path $payload 'payload.txt')).Trim() -ne '1.0.0') { throw 'Install failed.' }
 New-Item -ItemType Directory (Join-Path $install 'operator-data') | Out-Null
 Set-Content (Join-Path $install 'operator-data/chat') 'preserved'
 Add-Type -AssemblyName System.Security
 $login=[Text.Encoding]::UTF8.GetBytes('synthetic-login-sentinel-not-provider-credentials')
 $protected=[Security.Cryptography.ProtectedData]::Protect($login,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 [IO.File]::WriteAllBytes((Join-Path $install 'operator-data/login.bin'),$protected)
 [IO.File]::WriteAllText((Join-Path $install 'operator-data/binding.json'),'{"session":"synthetic","project":"PLC_A"}')
 $dataHashes=@{}
 foreach($file in Get-ChildItem (Join-Path $install 'operator-data') -File){$dataHashes[$file.Name]=(Get-FileHash $file.FullName).Hash}
 $state=Invoke-HostTransaction @args -Action Install -Package $p2
 if ($state.previous -ne '1.0.0') { throw 'Update failed.' }
 $failed=$false; try { Invoke-HostTransaction @args -Action Install -Package $p3 -InjectFailure after-stage | Out-Null } catch { $failed=$true }
 if (-not $failed -or (Get-Content (Join-Path $payload 'payload.txt')).Trim() -ne '1.1.0' -or (Test-Path (Join-Path $install 'versions/1.2.0'))) { throw 'Failed staging reserved a version or changed the installed payload.' }
 if (@(Get-ChildItem -LiteralPath $hostRoot -Directory -Filter '.assistant-stage-*').Count) { throw 'Failed staging left a partial payload.' }
 $failed=$false; try { Invoke-HostTransaction @args -Action Install -Package $p3 -InjectFailure after-swap | Out-Null } catch { $failed=$true }
 if (-not $failed -or (Get-Content (Join-Path $payload 'payload.txt')).Trim() -ne '1.1.0') { throw 'Failed update was not restored.' }
 if (Test-Path (Join-Path $install 'versions/1.2.0')) { throw 'Failed update left its version reserved.' }
 $lock=[IO.File]::Open((Join-Path $install '.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try { $failed=$false; try { Invoke-HostTransaction @args -Action Rollback | Out-Null } catch { $failed=$true }; if (-not $failed) { throw 'Concurrent update accepted.' } } finally { $lock.Dispose() }
 $hostLock=[IO.File]::Open((Join-Path $hostRoot '.twincat-assistant-deployment.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try { $failed=$false; try { Invoke-HostTransaction @args -Action Rollback | Out-Null } catch { $failed=$true }; if (-not $failed) { throw 'Concurrent host update accepted.' } } finally { $hostLock.Dispose() }
 $state=Invoke-HostTransaction @args -Action Rollback
 if ($state.current -ne '1.0.0' -or (Get-Content (Join-Path $payload 'payload.txt')).Trim() -ne '1.0.0') { throw 'Rollback failed.' }
 if ((Get-Content (Join-Path $install 'operator-data/chat')).Trim() -ne 'preserved' -or (Get-Content $marker).Trim() -ne 'keep') { throw 'Operator data or marker content changed.' }
 foreach($name in $dataHashes.Keys){if((Get-FileHash (Join-Path $install ('operator-data/'+$name))).Hash -cne $dataHashes[$name]){throw 'Rollback changed chat/login/binding bytes'}}
 $restored=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $install 'operator-data/login.bin')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 if([Convert]::ToBase64String($restored) -cne [Convert]::ToBase64String($login)){throw 'Rollback credentials no longer decrypt for the original user'}
 Set-Content (Join-Path $payload 'payload.txt') 'changed'
 $failed=$false; try { Invoke-HostTransaction @args -Action Rollback | Out-Null } catch { $failed=$true }; if (-not $failed) { throw 'Changed extension accepted.' }
 $badInterop=Join-Path $root 'wrong.dll'; '[assembly:System.Reflection.AssemblyVersion("2.0.0.0")] public class Wrong {}' | Set-Content $src
 & $csc /nologo /target:library "/out:$badInterop" $src
 $failed=$false; try { Assert-Interop $hostDll $badInterop } catch { $failed=$true }; if (-not $failed) { throw 'Wrong interop accepted.' }
 Copy-Item $interop (Join-Path $p1 'host/x86/TCatSysManagerLib.dll')
 $failed=$false; try { Assert-HostPayload $p1 | Out-Null } catch { $failed=$true }; if (-not $failed) { throw 'Bundled interop accepted.' }
 Remove-Item (Join-Path $p1 'host/x86/TCatSysManagerLib.dll')
 $out=Join-Path $root 'out'; & "$PSScriptRoot/Build-WindowsInstaller.ps1" -PackageDirectory $p1 -OutputDirectory $out | Out-Null
 if (-not (Test-Path (Join-Path $out 'TwinCAT-Assistant-1.0.0-x64-setup.exe'))) { throw 'Executable not built.' }
 Write-Host 'Synthetic registration, update, rollback, recovery, interop, preservation and executable build passed.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
