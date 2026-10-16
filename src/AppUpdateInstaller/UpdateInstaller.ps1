# Minimal installer handoff; never reads, copies or restores profile data.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-UpdatePlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Update files cannot use linked paths.'
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Open-UpdateInstaller([string]$Root, [object]$Plan) {
    Assert-UpdatePlainPath $Root
    if ($Plan.schema -ne 1 -or $Plan.edition -notin @('production','developer') -or
        $Plan.updateSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        $Plan.previousVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$' -or
        $Plan.updateVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$') { throw 'Invalid update plan.' }
    $path = Join-Path $Root 'update.exe'
    Assert-UpdatePlainPath $path
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'Read')
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose() }
        if ($actual -ne $Plan.updateSha256) { throw 'Installer verification failed. Nothing was installed.' }
        return $stream
    } catch { $stream.Dispose(); throw }
}

function Wait-UpdateHostClosed([string]$MutexName = 'Local\WidgetRail.OverlayHost.Running', [double]$TimeoutSeconds = 120) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        [Threading.Mutex]$running = $null
        if (![Threading.Mutex]::TryOpenExisting($MutexName, [ref]$running)) { return }
        $running.Dispose()
        if ([DateTime]::UtcNow -ge $deadline) { throw 'WidgetRail could not close. Close it and try the update again.' }
        Start-Sleep -Milliseconds 100
    } while ($true)
}

function Assert-UpdateInstallation([object]$Installed, [object]$Release, [object]$Plan) {
    if ($Installed.Edition -ne $Plan.edition -or $Installed.ApplicationRoot -ne $Plan.previousRoot -or
        $Release.version -ne $Plan.previousVersion) { throw 'The installation changed. Open WidgetRail and check for updates again.' }
}

function Invoke-UpdateHandoff([string]$Root, [object]$Plan, [scriptblock]$ReadInstallation,
    [scriptblock]$Ready, [scriptblock]$WaitClosed, [scriptblock]$RunInstaller, [scriptblock]$Relaunch) {
    $lease = Open-UpdateInstaller $Root $Plan
    try {
        $installed = & $ReadInstallation
        Assert-UpdateInstallation $installed $installed $Plan
        & $Ready
        & $WaitClosed
        $installed = & $ReadInstallation
        Assert-UpdateInstallation $installed $installed $Plan
        $code = & $RunInstaller (Join-Path $Root 'update.exe')
        if ($code -eq 0) {
            $installed = & $ReadInstallation
            if ($installed.version -ne $Plan.updateVersion -or $installed.Edition -ne $Plan.edition) {
                throw 'Setup did not register the expected update. Open WidgetRail from Start.'
            }
            & $Relaunch $installed.ApplicationRoot
        } elseif ($code -notin @(2, 3010)) {
            throw "Setup did not finish (exit code $code). Run the installer again or open WidgetRail from Start."
        }
    } finally { $lease.Dispose() }
}
