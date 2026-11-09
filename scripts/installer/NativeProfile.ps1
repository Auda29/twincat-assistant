# Host-test override contract v1, coordinated 2026-11-06. Read-only preflight.
Set-StrictMode -Version Latest
function Initialize-ProfileFileReader {
 if ('AssistantInstaller.ProfileFile' -as [type]) { return }
 Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace AssistantInstaller {
 public static class ProfileFile {
  [StructLayout(LayoutKind.Sequential)] struct Info {
   public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
   public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
  }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFileW(string p,uint a,uint s,IntPtr x,uint d,uint f,IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out Info i);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandleW(SafeFileHandle h,StringBuilder b,uint n,uint f);
  public static byte[] Check(string path,bool read) {
   using(var h=CreateFileW(path,0x80000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero)) {
    Info i;
    if(h.IsInvalid || !GetFileInformationByHandle(h,out i) || (i.Attributes & (0x400u|0x10u))!=0 || i.Links!=1) throw new IOException("Profile file must be regular, single-link and readable.");
    var name=new StringBuilder(32768);uint n=GetFinalPathNameByHandleW(h,name,32768,0);
    string final=name.ToString(); if(final.StartsWith(@"\\?\"))final=final.Substring(4);
    if(n==0 || n>=32768 || !String.Equals(final,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)) throw new IOException("Profile file path is not canonical.");
    if(!read)return new byte[0];
    if(i.SizeHigh!=0 || i.SizeLow>65536)throw new IOException("Profile configuration exceeds its bound.");
    using(var stream=new FileStream(h,FileAccess.Read)) {
     byte[] b=new byte[(int)i.SizeLow];int count=0;
     while(count<b.Length){int got=stream.Read(b,count,b.Length-count);if(got==0)throw new IOException("Incomplete profile configuration.");count+=got;}
     return b;
    }
   }
  }
 }
}
'@
}
function Assert-ProfileDirectory([string]$Path) {
 $candidate=$Path
 while(-not (Test-Path -LiteralPath $candidate)) {
  $parent=Split-Path -Parent $candidate
  if(-not $parent -or $parent -eq $candidate){throw 'Profile path has no existing ancestor.'}
  $candidate=$parent
 }
 Assert-NoLinks $candidate
 if(-not (Get-Item -LiteralPath $candidate -Force).PSIsContainer){throw 'Profile directory conflicts with a file.'}
}
function Read-InstallerProfileJson([string]$Path) {
 Assert-NoLinks $Path
 Initialize-ProfileFileReader
 [byte[]]$bytes=[AssistantInstaller.ProfileFile]::Check($Path,$true)
 $encoding=New-Object Text.UTF8Encoding($false,$true)
 $text=$encoding.GetString($bytes).TrimStart([char]0xFEFF)
 return ($text | ConvertFrom-Json)
}
function Resolve-InstallerProfile([string]$LocalAppData=$env:LOCALAPPDATA,[string]$SystemDotNetRoot) {
 if(-not $SystemDotNetRoot){$programs=if($env:ProgramW6432){$env:ProgramW6432}else{$env:ProgramFiles};$SystemDotNetRoot=Join-Path $programs 'dotnet'}
 if(-not [IO.Path]::IsPathRooted($LocalAppData)){throw 'Local application data path must be absolute.'}
 $overridePresent=[Environment]::GetEnvironmentVariables('Process').Contains('TWINCAT_ASSISTANT_TEST_CONFIG')
 if($overridePresent) {
  if($env:WEBVIEW2_USER_DATA_FOLDER -or $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS){throw 'Inherited WebView2 overrides are incompatible with isolated host tests.'}
  $override=[Environment]::GetEnvironmentVariable('TWINCAT_ASSISTANT_TEST_CONFIG','Process')
  if(-not $override -or -not [IO.Path]::IsPathRooted($override) -or -not [string]::Equals($override,[IO.Path]::GetFullPath($override),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid host-test override path; global fallback forbidden.'}
  $base=[IO.Path]::GetFullPath((Join-Path $LocalAppData 'TwinCatAssistant/host-tests')).TrimEnd('\')
  $prefix=$base+'\'
  if(-not $override.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Host-test override must stay under the fixed host-tests root.'}
  $relative=$override.Substring($prefix.Length)
  if($relative -cnotmatch '^[a-zA-Z0-9_-]{1,64}\\native-chat\.json$'){throw 'Invalid host-test id or configuration filename.'}
  $profile=Split-Path -Parent $override
  Assert-ProfileDirectory $profile
  if(-not (Test-Path -LiteralPath $override -PathType Leaf)){throw 'Host-test configuration is missing; global fallback forbidden.'}
  $config=Read-InstallerProfileJson $override
  if((($config.PSObject.Properties.Name | Sort-Object) -join ',') -cne 'DotNetRoot,StateRoot'){throw 'Host-test configuration permits only StateRoot and DotNetRoot.'}
  $root=Join-Path $profile 'state'
  if(-not [string]::Equals([string]$config.StateRoot,$root,[StringComparison]::OrdinalIgnoreCase) -or -not [string]::Equals([string]$config.DotNetRoot,$SystemDotNetRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Host-test state or system .NET selection does not match its fixed contract.'}
  foreach($directory in @($root,(Join-Path $profile 'native-chat'),(Join-Path $profile 'WebView2'))){Assert-ProfileDirectory $directory}
  $directories=New-Object 'Collections.Generic.Stack[string]'
  $directories.Push($profile)
  while($directories.Count) {
   foreach($entry in @(Get-ChildItem -LiteralPath $directories.Pop() -Force)) {
    if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked host-test profile entry refused.'}
    if($entry.PSIsContainer){$directories.Push($entry.FullName)}else{[void][AssistantInstaller.ProfileFile]::Check($entry.FullName,$false)}
   }
  }
  return @{StateRoot=$root;UserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;TestConfiguration=$override;Contract='host-test-override-v1'}
 }
 $root=Join-Path $LocalAppData 'TwinCatAssistant/operator-state'
 $nativeConfig=Join-Path $LocalAppData 'TwinCatAssistant/native-chat.json'
 if(Test-Path -LiteralPath $nativeConfig) {
  $config=Read-InstallerProfileJson $nativeConfig
  if(-not $config.StateRoot -or -not [IO.Path]::IsPathRooted([string]$config.StateRoot)){throw 'Native StateRoot must be absolute.'}
  $root=[IO.Path]::GetFullPath([string]$config.StateRoot)
 }
 return @{StateRoot=$root;UserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;TestConfiguration=$null;Contract='user-profile'}
}
