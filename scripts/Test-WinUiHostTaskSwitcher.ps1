[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$StartMinimized,
    [switch]$OwnedPopup,
    [switch]$CloseOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$ready = Join-Path $output 'peer.json'
$stop = Join-Path $output 'peer.stop'
$peerScript = Join-Path $PSScriptRoot 'Start-WinUiForegroundTestPeer.ps1'
$peerArguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$peerScript`"",
    '-ReadyPath', "`"$ready`"", '-StopPath', "`"$stop`"", '-PeerName', 'host-task-switcher')
if ($StartMinimized) { $peerArguments += '-StartMinimized' }
if ($OwnedPopup) { $peerArguments += '-OwnedPopup' }
$peer = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList $peerArguments
$ownedPid = 0
$results = [Collections.Generic.List[object]]::new()
try {
    $until = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $ready)) {
        if ($peer.HasExited -or [DateTime]::UtcNow -ge $until) { throw 'Owned foreground peer did not become ready.' }
        Start-Sleep -Milliseconds 100
    }
    $cases = if ($CloseOnly) { @('close') } elseif ($StartMinimized) { @('minimized') } elseif ($OwnedPopup) { @('owned-popup') } else { @('full', 'reduced', 'reopen', 'expiry') }
    foreach ($case in $cases) {
        $result = Join-Path $output "$case.json"
        $arguments = "--shell-config=`"$([IO.Path]::GetFullPath($ShellConfig))`" --shell-no-controller --validation-platform-activation --validate-task-activation=`"$result`" --activation-task-peer=`"$ready`" --handoff-prime-input"
        if ($case -eq 'reduced') { $arguments += ' --handoff-reduced' }
        if ($case -eq 'close') { $arguments += ' --task-close-only' }
        if ($case -in @('reopen', 'expiry')) { $arguments += " --handoff-cancel=$case" }
        $launch = winapp run (Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json --args $arguments | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Host activation fixture launch failed.' }
        $ownedPid = [int]$launch.ProcessId
        $until = [DateTime]::UtcNow.AddSeconds(40)
        while (!(Test-Path -LiteralPath $result)) {
            if (!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw "Fixture exited before $case evidence." }
            if ([DateTime]::UtcNow -ge $until) { throw "Timed out waiting for $case." }
            Start-Sleep -Milliseconds 100
        }
        $evidence = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
        if (!$evidence.passed) { throw "$case failed: $($evidence.error)" }
        $results.Add(@{ case = $case; checks = $evidence.checks.Count; passed = $true })
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
        $resident = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
        if ($resident -and !$resident.WaitForExit(15000)) { throw 'Owned fixture did not close cleanly.' }
        $ownedPid = 0
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
    "Host task switching: $($results.Count) native cases passed."
} finally {
    # Preserve a failed fixture and its target together for diagnosis.
    if ($ownedPid -eq 0 -or !(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        Set-Content -LiteralPath $stop -Value 'stop'
        [void]$peer.WaitForExit(5000)
    }
}
