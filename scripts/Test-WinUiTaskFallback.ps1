[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('timeout','denied','late-success','reopen','keyboard-busy','input-rejected','stale-target')][string]$Case
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$peers = @()
$ownedPid = 0
try {
    foreach ($name in @('A-return','B-external')) {
        $ready = Join-Path $output "$name.json"
        $stop = Join-Path $output "$name.stop"
        $process = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot/Start-WinUiForegroundTestPeer.ps1`"",
            '-ReadyPath', "`"$ready`"", '-StopPath', "`"$stop`"", '-PeerName', $name)
        $peers += @{Process=$process;Stop=$stop}
        $until = [DateTime]::UtcNow.AddSeconds(15)
        while (!(Test-Path -LiteralPath $ready)) {
            if ($process.HasExited -or [DateTime]::UtcNow -ge $until) { throw 'Owned foreground peer did not become ready.' }
            Start-Sleep -Milliseconds 100
        }
    }
    $result = Join-Path $output 'result.json'
    $first = Join-Path $output 'A-return.json'
    $second = Join-Path $output 'B-external.json'
    $arguments = "--shell-config=`"$([IO.Path]::GetFullPath($ShellConfig))`" --shell-no-controller --validation-platform-activation --validate-task-fallback=`"$result`" --activation-task-peer=`"$first`" --fallback-other-peer=`"$second`""
    if ($Case) { $arguments += " --task-fallback-case=$Case" }
    $launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json --args $arguments | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Task View fallback fixture launch failed.' }
    $ownedPid = [int]$launch.ProcessId
    $launch | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
    $until = [DateTime]::UtcNow.AddSeconds(100)
    while (!(Test-Path -LiteralPath $result)) {
        if (!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Fixture exited before producing evidence.' }
        if ([DateTime]::UtcNow -ge $until) { throw 'Timed out waiting for foreground return evidence.' }
        Start-Sleep -Milliseconds 100
    }
    $evidence = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if (!$evidence.passed) { throw "Task View fallback failed: $($evidence.error)" }
    "Task View fallback: $($evidence.checks.Count) native checks passed."
} finally {
    if ($ownedPid -gt 0 -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
        Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
    }
    foreach ($peer in $peers) {
        Set-Content -LiteralPath $peer.Stop -Value 'stop'
        [void]$peer.Process.WaitForExit(5000)
    }
}
