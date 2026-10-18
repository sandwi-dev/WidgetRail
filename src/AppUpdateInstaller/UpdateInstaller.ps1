# Minimal installer handoff; never reads, copies or restores profile data.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-UpdatePlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        # Inventory checks visit every file and ancestor. Avoid PowerShell provider
        # discovery on each visit, while still checking every existing component.
        try { $attributes = [IO.File]::GetAttributes($cursor) }
        catch [IO.FileNotFoundException] { $attributes = 0 }
        catch [IO.DirectoryNotFoundException] { $attributes = 0 }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update files cannot use linked paths.' }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Open-UpdateInstaller([string]$Root, [object]$Plan) {
    Assert-UpdatePlainPath $Root
    if ($Plan.schema -ne 1 -or $Plan.edition -notin @('production','bare') -or
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

function Get-UpdateSetupArguments([string]$Root, [bool]$Interactive, [string]$PreviousRoot = '') {
    # Start-Process joins arguments as a command line. Reject quotes rather than
    # relying on PowerShell array quoting for paths supplied to native setup.
    if ($Root.Contains('"')) { throw 'Invalid update directory.' }
    $arguments = @('/NORESTART', '/RESTARTEXITCODE=3010', ('/LOG="' + (Join-Path $Root 'setup.log') + '"'),
        ('/WRAILWARNING="' + (Join-Path $Root 'warning.txt') + '"'))
    if (!$Interactive) { $arguments += @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-') }
    if ($PreviousRoot) {
        $versions = Split-Path $PreviousRoot -Parent
        if ((Split-Path $versions -Leaf) -ne 'versions' -or $versions.Contains('"')) { throw 'Invalid registered installation directory.' }
        $arguments += '/DIR="' + (Split-Path $versions -Parent) + '"'
    }
    return $arguments
}

function Test-UpdateInstalledPayload([object]$Installed, [object]$Plan) {
    # A failed/cancelled installer must never cause us to launch a partial payload.
    # Inno uses immutable per-inventory directories; check every recorded file.
    try {
        if ($Installed.Edition -ne $Plan.edition -or $Installed.version -notin @($Plan.previousVersion, $Plan.updateVersion)) { return $false }
        $root = [IO.Path]::GetFullPath($Installed.ApplicationRoot)
        if ([IO.Path]::GetDirectoryName($root) -ne [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Plan.previousRoot))) { return $false }
        Assert-UpdatePlainPath $root
        $manifestPath = Join-Path $root 'release.json'
        Assert-UpdatePlainPath $manifestPath
        if ((Get-Item -LiteralPath $manifestPath).Length -gt 8MB) { return $false }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifest.version -ne $Installed.version -or $manifest.edition -ne $Installed.Edition) { return $false }
        $files = @($manifest.files)
        if (!$files.Count -or $files.Count -gt 50000 -or !($files.path -contains 'OverlayFrontend.WinUI.exe')) { return $false }
        foreach ($file in $files) {
            if ([IO.Path]::IsPathRooted($file.path) -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$') { return $false }
            $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
            if (!$path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $false }
            Assert-UpdatePlainPath $path
            if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { return $false }
        }
        return $true
    } catch { return $false }
}

function Invoke-UpdateHandoff([string]$Root, [object]$Plan, [scriptblock]$ReadInstallation,
    [scriptblock]$Ready, [scriptblock]$WaitClosed, [scriptblock]$RunInstaller, [scriptblock]$Relaunch,
    [scriptblock]$Preflight = { 'ready' }, [scriptblock]$VerifyInstalled = { param($installed, $plan) Test-UpdateInstalledPayload $installed $plan },
    [scriptblock]$RecordOutcome = {}) {
    $lease = Open-UpdateInstaller $Root $Plan
    try {
        $installed = & $ReadInstallation
        Assert-UpdateInstallation $installed $installed $Plan
        $previous = [pscustomobject]@{ ApplicationRoot=$Plan.previousRoot; Edition=$Plan.edition; version=$Plan.previousVersion }
        if ((& $Preflight) -ne 'ready') { return 'interactive-required' }
        & $Ready
        & $WaitClosed
        $installed = & $ReadInstallation
        Assert-UpdateInstallation $installed $installed $Plan
        $code = -1
        try { $code = & $RunInstaller (Join-Path $Root 'update.exe') } catch { $code = -1 }
        $outcome = switch ($code) { 0 { 'success' }; 2 { 'cancelled' }; 3010 { 'restart-required' }; default { 'failed' } }
        try {
            $installed = & $ReadInstallation
            $usable = & $VerifyInstalled $installed $Plan
        } catch { $usable = $false }
        if ($code -eq 0 -and (!$usable -or $installed.version -ne $Plan.updateVersion)) { $outcome = 'failed' }
        if (!$usable -and (& $VerifyInstalled $previous $Plan)) {
            $installed = $previous
            $usable = $true
            # This is a verified retained executable, not a rollback of registry,
            # preferences or files. A later interactive setup repairs registration.
            if ($outcome -eq 'success') { $outcome = 'failed' }
        }
        & $RecordOutcome $outcome
        if ($usable) {
            & $Relaunch $installed.ApplicationRoot
        } else {
            throw 'Setup did not leave a verified usable installation. Run WidgetRail setup again from its download. Details are recorded in the update log.'
        }
    } finally { $lease.Dispose() }
}
