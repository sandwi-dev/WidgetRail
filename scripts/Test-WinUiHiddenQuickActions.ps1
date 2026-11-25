param(
    [Parameter(Mandatory)][string]$FrontendExecutable,
    [Parameter(Mandatory)][string]$FixtureInstallation,
    [Parameter(Mandatory)][string]$FixtureExecutable,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$frontend=(Resolve-Path -LiteralPath $FrontendExecutable).Path
$installation=(Resolve-Path -LiteralPath $FixtureInstallation).Path
$peerExe=(Resolve-Path -LiteralPath $FixtureExecutable).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path "$installation/runtime/Bridge/replay-ui-fixture.marker") -or
    [IO.Path]::GetFileName($peerExe) -ne 'WindowCaptureFixture.exe' -or (Test-Path $output)) {
    throw 'Use the isolated replay fixture installation, WindowCaptureFixture and a fresh output directory.'
}
$null=New-Item -ItemType Directory -Path $output
function Start-Peer([int]$Width,[int]$Height) {
    $peer=Start-Process $peerExe -WindowStyle Hidden -PassThru -ArgumentList "--audio --width=$Width --height=$Height"
    $peers.Add($peer)
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 80
        $peer.Refresh()
        if ($peer.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Test window did not appear.' }
    } while (!$peer.MainWindowHandle)
    return $peer
}
foreach ($mode in 'delayed','approved') {
    $dir=Join-Path $output $mode
    $profile=Join-Path $dir 'profile'
    $catalog=Join-Path $dir 'catalog'
    $null=New-Item -ItemType Directory -Path $profile,$catalog -Force
    @{ InitialWidgetId='replay-approval'; InstalledCatalogRoot=$catalog; SettingsRoot=$profile; InstallationRoot=$installation } |
        ConvertTo-Json | Set-Content "$dir/shell.json"
    $peers=[Collections.Generic.List[Diagnostics.Process]]::new()
    $app=$null
    try {
        $extra=if ($mode -eq 'delayed') {
            $second=Start-Peer 800 450
            "--hidden-delay-peer=$($second.Id)"
        } else {
            $seed=Get-Content "$output/delayed/profile/platform-settings.json" -Raw | ConvertFrom-Json -AsHashtable
            $seed.Remove('captureConfirmations')
            $bytes=[Text.Encoding]::UTF8.GetBytes($peerExe.ToUpperInvariant()+[char]0)
            $registered='start-'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            $id=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($registered)))
            $seed.replayCapture.ownerPackageId='example.capture-replay-fixture'
            $seed.replayCapture.ownerPublisherId='example.fixture'
            $seed.replayCapture.ownerInstanceId='capture-replay.instance'
            $seed.replayCapture.approvals.applications=@(@{name='WindowCaptureFixture';id=$id})
            $seed.replayCapture.settings.enabled=$false
            $seed.replayCapture.settings.autoStart=$false
            $seed | ConvertTo-Json -Depth 30 | Set-Content "$profile/platform-settings.json"
            '--hidden-fixture-approved'
        }
        $first=Start-Peer 1280 720
        $arguments=@('--show','--shell-no-controller','--validation-platform-activation',
            "--hidden-fixture-pid=$($first.Id)", $extra, ('--shell-config="'+$dir+'/shell.json"'), ('--validate-hidden-slots="'+$dir+'/result.json"'))
        $app=Start-Process $frontend -WorkingDirectory (Split-Path $frontend) -WindowStyle Hidden -PassThru -ArgumentList $arguments
        $deadline=[DateTime]::UtcNow.AddMinutes(3)
        while (!(Test-Path "$dir/result.json") -and !$app.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        $report=Get-Content "$dir/result.json" -Raw | ConvertFrom-Json
        if (!$report.passed) { throw $report.failure }
        "$mode passed $($report.checks.Count) native checks."
    } finally {
        if ($app -and !$app.HasExited) { & "$PSScriptRoot/Close-WinUiTestShell.ps1" -AppPid $app.Id }
        foreach ($peer in $peers) {
            if (!$peer.HasExited) { $null=$peer.CloseMainWindow(); if (!$peer.WaitForExit(3000)) { $peer.Kill() } }
        }
    }
}
