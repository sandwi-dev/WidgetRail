# Run only against the generated WindowCaptureFixture executable and a validation host.
param([Parameter(Mandatory)][string]$OutputDirectory,
 [Parameter(Mandatory)][string]$CandidateDirectory,
 [Parameter(Mandatory)][string]$FixtureDirectory,
 [Parameter(Mandatory)][string]$FixtureExecutable)
$ErrorActionPreference='Stop'
$run=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $run){throw 'Use a fresh replay UI run directory.'}
$bridge=Join-Path $run 'installation/runtime/Bridge'
$profile=Join-Path $run 'profile'
$catalog=Join-Path $run 'catalog'
New-Item -ItemType Directory -Path $run,$bridge,$profile,$catalog|Out-Null
$source=[IO.Path]::GetFullPath($FixtureDirectory)
foreach($entry in Get-ChildItem -LiteralPath $source){Copy-Item -LiteralPath $entry.FullName -Destination $bridge -Recurse -Force}
Copy-Item -LiteralPath (Join-Path $bridge 'WidgetBridge.Tests.exe') -Destination (Join-Path $bridge 'WidgetBridge.exe') -Force
[IO.File]::WriteAllText((Join-Path $bridge 'replay-ui-fixture.marker'),'isolated')
[IO.File]::WriteAllText((Join-Path $run 'installation/widget-catalog.json'),'{"version":1,"widgets":[]}')
[IO.File]::WriteAllText((Join-Path $profile 'automatic-app-updates.txt'),'disabled')
$config=Join-Path $run 'shell.json'
$shellOptions=@{InstallationRoot=(Join-Path $run 'installation');SettingsRoot=$profile;InstalledCatalogRoot=$catalog;InitialWidgetId='replay-approval'}
$shellOptions|ConvertTo-Json|Set-Content -LiteralPath $config
@{Version=1;Step='complete';Paused=$false}|ConvertTo-Json|Set-Content "$profile/introduction-progress.json"
$peerExe=(Resolve-Path -LiteralPath $FixtureExecutable).Path
$peers = [Collections.Generic.List[Diagnostics.Process]]::new()
function StartPeer([string]$path) {
 $owned=Start-Process -FilePath $path -WindowStyle Hidden -PassThru -ArgumentList @('--audio','--width=1280','--height=720')
 $peers.Add($owned)
 $deadline=[datetime]::UtcNow.AddSeconds(8)
 do {
     $owned.Refresh()
     if ($owned.HasExited -or [datetime]::UtcNow -gt $deadline) { throw 'Generated window did not open.' }
     Start-Sleep -Milliseconds 30
 } while ($owned.MainWindowHandle -eq [IntPtr]::Zero)
 return $owned
}
function FixtureIdentity([string]$path) {
 $bytes = [Text.Encoding]::UTF8.GetBytes($path.ToUpperInvariant() + [char]0)
 $registered = 'start-' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
 return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($registered)))
}
$frontend=$null
try {
 $peer=StartPeer $peerExe
 $target=Join-Path $run 'target.json'
 @{Handle=$peer.MainWindowHandle.ToString('X');ProcessId=$peer.Id;ProcessCreated=$peer.StartTime.ToUniversalTime().ToFileTimeUtc().ToString('X16');ClassName='WidgetRailCaptureFixture'}|ConvertTo-Json|Set-Content -LiteralPath $target
 $exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
 foreach($mode in 'rejection','management','manual','automatic','ambiguous','focused') {
     $foregroundArgument=@()
     if($mode -notin 'manual','management','rejection'){
       $settingsPath = Join-Path $profile 'platform-settings.json'
       $settings=Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable
       if($settings.replayCapture.approvals.applications[0].id -ne (FixtureIdentity $peerExe)){throw 'Manual approval does not match the fixture executable.'}
       if($mode -eq 'ambiguous') {
         $alternate=Join-Path $run 'ReplaySecondApp.exe'
         $unapproved=Join-Path $run 'ReplayUnapproved.exe'
         foreach($copy in $alternate,$unapproved){Copy-Item -LiteralPath $peerExe -Destination $copy}
         $null = StartPeer $alternate
         $thirdPeer=StartPeer $unapproved
         $settings.replayCapture.approvals.applications += @{id=(FixtureIdentity $alternate);name='ReplaySecondApp'}
       }
       if($mode -in 'ambiguous','focused') {
         $focusPeer=if($mode -eq 'focused'){$peer}else{$thirdPeer}
         $foregroundArgument=@("--verify-replay-foreground=$($focusPeer.Id)", "--verify-replay-window=$($focusPeer.MainWindowHandle.ToInt64())")
       }
       $settings.replayCapture.settings.enabled=$true
       $settings.replayCapture.settings.autoStart=$true
       $settings | ConvertTo-Json -Depth 64 | Set-Content $settingsPath
     }
     $result=Join-Path $run "$mode.json"
     $launch = @{
         FilePath = $exe
         WorkingDirectory = Split-Path $exe
         WindowStyle = 'Normal'
         PassThru = $true
         ArgumentList = $foregroundArgument + @(
             "--verify-replay-start-notice=$mode", '--show', '--shell-no-controller', '--validation-platform-activation',
             ('--shell-config="' + $config + '"'), ('--validate-replay-ui="' + $result + '"'), ('--replay-fixture-target="' + $target + '"')
         )
         RedirectStandardOutput = Join-Path $run "$mode-frontend.log"
         RedirectStandardError = Join-Path $run "$mode-frontend.err"
     }
     $frontend = Start-Process @launch
     $deadline=[DateTime]::UtcNow.AddSeconds(140)
     while(!(Test-Path -LiteralPath $result)) {
      $frontend.Refresh();if($frontend.HasExited){throw "Replay UI fixture exited $($frontend.ExitCode)."}
      if([DateTime]::UtcNow -gt $deadline){throw 'Replay UI fixture timed out.'}
      Start-Sleep -Milliseconds 200
     }
     $receipt=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
     if(!$receipt.passed){throw "$mode failed at $($receipt.step): $($receipt.failure)"}
     $receipt.checks | Select-Object -Last 3
     & (Join-Path $PSScriptRoot "Close-WinUiTestShell.ps1") -AppPid $frontend.Id
     if(!$frontend.WaitForExit(30000)){throw 'Replay validation host did not exit'}
 }
} finally {
 if($frontend -and !$frontend.HasExited){
  & (Join-Path $PSScriptRoot "Close-WinUiTestShell.ps1") -AppPid $frontend.Id
  if(!$frontend.WaitForExit(30000)){throw 'Owned replay UI fixture did not close normally.'}
 }
 foreach ($owned in $peers) {
     $owned.Refresh()
     if ($owned.HasExited) { continue }
     [void]$owned.CloseMainWindow()
     if (!$owned.WaitForExit(3000)) { $owned.Kill(); $owned.WaitForExit() }
 }
 [IO.File]::WriteAllText((Join-Path $run 'shutdown.json'),(@{frontendClosed=(!$frontend -or $frontend.HasExited);peersClosed=@($peers | Where-Object { !$_.HasExited }).Count -eq 0}|ConvertTo-Json))
}
