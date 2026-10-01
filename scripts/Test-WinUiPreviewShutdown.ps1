[CmdletBinding()]
param([Parameter(Mandatory)][string]$BridgeInstallation,
      [Parameter(Mandatory)][string]$InstalledCatalog,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh evidence directory.'}
if(Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue){throw 'Close the existing frontend before this exclusive shutdown check.'}
New-Item -ItemType Directory -Path $output | Out-Null
$ready=Join-Path $output 'peer.json'
$stop=Join-Path $output 'peer.stop'
$peerScript=Join-Path $PSScriptRoot 'Start-WinUiForegroundTestPeer.ps1'
$peer=Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$peerScript`"",'-ReadyPath',"`"$ready`"",'-StopPath',"`"$stop`"",'-PeerName','capture-shutdown')
$results=[Collections.Generic.List[object]]::new()
$ownedPid=0
try {
 $until=[DateTime]::UtcNow.AddSeconds(15)
 while(!(Test-Path -LiteralPath $ready)){
  if($peer.HasExited -or [DateTime]::UtcNow -ge $until){throw 'Owned source did not become ready.'}
  Start-Sleep -Milliseconds 100
 }
 foreach($state in @('visible','hidden')){
  $case=Join-Path $output $state
  New-Item -ItemType Directory -Path $case | Out-Null
  $config=Join-Path $case 'options.json'
  @{InstallationRoot=(Resolve-Path -LiteralPath $BridgeInstallation).Path; InstalledCatalogRoot=(Resolve-Path -LiteralPath $InstalledCatalog).Path;
    SettingsRoot=(Join-Path $case 'profile');SwitchDiagnosticsPath=(Join-Path $case 'switches.log')} | ConvertTo-Json | Set-Content -LiteralPath $config
  $fixture=Join-Path $case 'fixture.json'
  $consent=Join-Path $case 'profile/consent'
  New-Item -ItemType Directory -Force -Path $consent | Out-Null
  # This isolated profile admits read/preview only. Switch/close and system
  # mutation capabilities are never granted or invoked by this regression.
  @{schemaVersion=1;revision=1;entries=@('system.apps.windows.read.v1','system.apps.windows.preview.v1' | ForEach-Object {
    @{packageId='widgetrail.firstparty.task-switcher';publisherId='widgetrail.firstparty';capabilityId=$_;decision='grant'}
  })} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $consent 'consent-v1.json')
  $args="--shell-config=`"$config`" --shell-no-controller --validate-preview-shutdown=`"$fixture`" --preview-shutdown-peer=`"$ready`""
  if($state -eq 'hidden'){$args+=' --preview-shutdown-hidden'}
  $launch=winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args $args | ConvertFrom-Json
  if($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0){throw 'Shutdown fixture launch failed.'}
  $ownedPid=[int]$launch.ProcessId
  $launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $case 'launch.json')
  & (Join-Path $PSScriptRoot 'Assert-WinUiCandidatePayload.ps1') -AppPid $ownedPid -BuildDirectory (Join-Path $root 'src/OverlayFrontend.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64') -OutputPath (Join-Path $case 'payload.json')
  $until=[DateTime]::UtcNow.AddSeconds(40)
  while(!(Test-Path -LiteralPath $fixture)){
   if(!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)){throw 'Fixture exited before capture evidence.'}
   if([DateTime]::UtcNow -ge $until){throw 'Timed out awaiting live Task Switcher capture.'}
   Start-Sleep -Milliseconds 100
  }
  $evidence=Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json
  if(!$evidence.prepared){throw $evidence.error}
  $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ownedPid" | Where-Object Name -eq 'WidgetBridge.exe' | Select-Object -ExpandProperty ProcessId)
  $watch=[Diagnostics.Stopwatch]::StartNew()
  & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
  $resident=Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
  if($resident -and !$resident.WaitForExit(15000)){throw 'Orderly capture shutdown exceeded 15s; process preserved for diagnosis.'}
  $elapsed=$watch.ElapsedMilliseconds
  foreach($childId in $children){
   $child=Get-Process -Id $childId -ErrorAction SilentlyContinue
   if($child -and !$child.WaitForExit(5000)){throw 'Owned Bridge remains after frontend shutdown.'}
  }
  $results.Add(@{state=$state;passed=$true;pid=$ownedPid;closeMilliseconds=$elapsed;checks=$evidence.checks;capture=$evidence.capture})
  $ownedPid=0
 }
} catch {
 $results.Add(@{passed=$false;pid=$ownedPid;error=$_.ToString()})
 throw
} finally {
 $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json')
 $logs=Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics'
 foreach($name in @('frontend-errors.log','frontend-errors.log.previous')){
  $source=Join-Path $logs $name
  if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $output $name)}
 }
 # Preserve a hung frontend and its source together for inspection, never kill it
 # or abandon capture handles just to obtain a passing shutdown result.
 if($ownedPid -eq 0 -or !(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)){
  Set-Content -LiteralPath $stop -Value 'stop'
  [void]$peer.WaitForExit(5000)
 }
}
"Task Switcher shutdown: $($results.Count) visible/hidden cases passed."
