[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Receipt,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$WidgetId='media-sessions'
)
$ErrorActionPreference='Stop'
$probe=Get-Content $Receipt -Raw | ConvertFrom-Json
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh evidence/profile directory'}
if($probe.packageName -notmatch '^WidgetRail\.WinUI\.External[A-Za-z0-9.-]*Probe$'){throw 'Only isolated external-content probe identities are admitted'}
if(@(Get-AppxPackage -Name $probe.packageName).Count){throw 'An existing registration must not be replaced by this probe'}
if((Get-FileHash (Join-Path $probe.externalLocation 'OverlayFrontend.WinUI.dll')).Hash -ne $probe.assemblySha256){throw 'The staged frontend assembly changed'}
New-Item -ItemType Directory -Path $output | Out-Null
if(-not ('ExternalRuntimeProbe' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ExternalRuntimeProbe {
 [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 private interface IActivation { [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.LPWStr)] string args,uint flags,out uint pid); }
 public static uint Launch(string id,string args){var manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")));try{var result=((IActivation)manager).ActivateApplication(id,args,0,out var pid);Marshal.ThrowExceptionForHR(result);return pid;}finally{Marshal.FinalReleaseComObject(manager);}}
 [DllImport("kernel32.dll",SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("advapi32.dll",SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int size,out int length);
 [DllImport("kernel32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
 public static bool IsAppContainer(uint pid){var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();IntPtr token=IntPtr.Zero;try{if(!OpenProcessToken(process,8,out token)||!GetTokenInformation(token,29,out var value,4,out var length))throw new System.ComponentModel.Win32Exception();return value!=0;}finally{if(token!=IntPtr.Zero)CloseHandle(token);CloseHandle(process);}}
 public static string Package(uint pid){var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();try{uint length=1024;var name=new StringBuilder(1024);var result=GetPackageFullName(process,ref length,name);if(result!=0)throw new System.ComponentModel.Win32Exception(result);return name.ToString();}finally{CloseHandle(process);}}
}
'@
}
$appPid=0; $resident=$null; $ownedChildren=@(); $failure=$null; $registered=$false
$result=@{passed=$false;receipt=[IO.Path]::GetFullPath($Receipt);widget=$WidgetId;registered=$false;processExited=$false;registrationsRemoved=$false}
try {
 # Official CLI development registration: no certificate or trust-store changes.
 # Its debug metadata remains in this isolated evidence directory.
 Push-Location $output
 try {winapp create-debug-identity (Join-Path $probe.externalLocation 'OverlayFrontend.WinUI.exe') --manifest $probe.manifest --keep-identity --verbose *> register-cli.log; $registerCode=$LASTEXITCODE}
 finally {Pop-Location}
 $package=Get-AppxPackage -Name $probe.packageName
 $registered=$null -ne $package
 if($registerCode -ne 0 -or -not $package -or -not $package.IsDevelopmentMode){throw 'Development registration failed; inspect register-cli.log'}
 $result.registered=$true
 $profile=Join-Path $output 'profile'
 $arguments="--settings-root=`"$profile`" --installed-catalog-root=`"$(Join-Path $profile 'widgets')`" --widget=$WidgetId --shell-no-controller"
 # Activate the registered identity, never the packaged executable directly.
 $appPid=[ExternalRuntimeProbe]::Launch(($package.PackageFamilyName+'!App'),$arguments)
 $resident=Get-Process -Id $appPid
 @{pid=$appPid;package=$package.PackageFullName;externalLocation=$probe.externalLocation;assemblySha256=$probe.assemblySha256} | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
$deadline=[DateTime]::UtcNow.AddSeconds(30)
do {
 $stateJson=winapp ui get-property Overlay.Shell -a $appPid -p HelpText --json 2>$null
 if($LASTEXITCODE -eq 0){$state=($stateJson | ConvertFrom-Json).properties.HelpText | ConvertFrom-Json;if($state.activeWidget -eq $WidgetId -and -not $state.switching){break}}
 Start-Sleep -Milliseconds 250
} while([DateTime]::UtcNow -lt $deadline)
if(-not $state -or $state.activeWidget -ne $WidgetId -or $state.switching){throw 'External widget shell did not become ready'}
$bridge=Get-CimInstance Win32_Process -Filter "ParentProcessId=$appPid" | Where-Object Name -eq 'WidgetBridge.exe'
if(@($bridge).Count -ne 1){throw 'Expected one owned Bridge'}
$worker=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($bridge.ProcessId)" | Where-Object Name -eq 'WidgetWorkerHost.exe'
if(@($worker).Count -ne 1){throw 'Expected one sandboxed widget worker'}
$ownedChildren=@(Get-Process -Id $bridge.ProcessId,$worker.ProcessId)
$sandboxed=[ExternalRuntimeProbe]::IsAppContainer($worker.ProcessId)
$identity=[ExternalRuntimeProbe]::Package($appPid)
$result.runtime=@{pid=$appPid;package=$identity;activeWidget=$state.activeWidget;bridgePath=$bridge.ExecutablePath;workerPath=$worker.ExecutablePath;workerAppContainer=$sandboxed;defaultInstallationRoot=$probe.externalLocation}
$result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'runtime-observation.json')
$prefix=[IO.Path]::TrimEndingDirectorySeparator($probe.externalLocation)+[IO.Path]::DirectorySeparatorChar
if(-not $sandboxed -or $identity -ne $package.PackageFullName -or -not $bridge.ExecutablePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or -not $worker.ExecutablePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'External ownership/sandbox proof failed'}
if((Get-FileHash (Join-Path (Split-Path $resident.Path) 'OverlayFrontend.WinUI.dll')).Hash -ne $probe.assemblySha256){throw 'The running assembly differs from the staged publish'}
} catch {$failure=$_}
finally {
 try {
  if($resident -and -not $resident.HasExited){
   & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $appPid
   if(-not $resident.WaitForExit(15000)){throw 'Frontend did not finish shutdown; preserve registration for inspection'}
  }
  foreach($child in $ownedChildren){if(-not $child.WaitForExit(5000)){throw 'Owned service outlived frontend'}}
  $result.processExited=$true
  if($registered){
   $current=Get-AppxPackage -Name $probe.packageName
   if($current -and $current.IsDevelopmentMode -and $current.PackageFullName -eq $package.PackageFullName){Remove-AppxPackage -Package $current.PackageFullName}
   if(@(Get-AppxPackage -Name $probe.packageName).Count){throw 'Probe registration cleanup incomplete'}
  }
  $result.registrationsRemoved=$true
 } catch {$result.cleanupError=$_.Exception.Message;if(-not $failure){$failure=$_}}
 if($resident){$resident.Dispose()};foreach($child in $ownedChildren){$child.Dispose()}
 $result.passed=$null -eq $failure
 if($failure){$result.error=$failure.Exception.Message}
 $result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'result.json')
}
if($failure){throw $failure}
'External-content runtime, AppContainer and cleanup checks passed.'
