param([ValidateSet('Install','Recover')][string]$Mode = 'Recover', [switch]$WaitForAppExit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Windows.Forms
try {
    . (Join-Path $PSScriptRoot 'RecoveryEngine.ps1')
    . (Join-Path $PSScriptRoot 'RecoveryProgress.ps1')
    # In-app preparation must not steal controller focus before the user quits.
    if ($WaitForAppExit -and !(Wait-RecoveryHostClosed)) { exit 0 }
    Assert-PlainPath $PSScriptRoot
    $planPath = Join-Path $PSScriptRoot 'plan.json'
    if ((Get-Item -LiteralPath $planPath).Length -gt 8192) { throw 'Invalid recovery plan.' }
    $plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($plan.schema -ne 1) { throw 'Unsupported recovery plan.' }
    $profile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WidgetRail'
    if ($Mode -eq 'Recover' -and !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'state.json'))) { $Mode = 'Install' }
    $verb = if ($Mode -eq 'Install') { 'Install the prepared update' } else { 'Restore the previous version and its pre-update profile' }
    $choice = [Windows.Forms.MessageBox]::Show(($verb + '? Quit WidgetRail first, then choose OK. Setup stays interactive. Recovery keeps the newer profile separately. Saved credentials outside the profile are not rolled back.'), 'WidgetRail update and recovery', 'OKCancel', 'Information')
    if ($choice -ne 'OK') { exit 0 }
    # No forced shutdown and no waiting indefinitely. Recheck immediately before
    # copying and hold the overlay singleton for the full transaction.
    $created = $false
    $appLock = [Threading.Mutex]::new($true, 'Local\WidgetRail.OverlayHost.Running', [ref]$created)
    if (!$created) { $appLock.Dispose(); throw 'WidgetRail is still running. Quit it and run this recovery shortcut again.' }
    try {
        $setupCreated = $false
        $recoveryLock = [Threading.Mutex]::new($true, 'Local\WidgetRail.Recovery', [ref]$setupCreated)
        if (!$setupCreated) { $recoveryLock.Dispose(); throw 'Another recovery operation is running.' }
        try {
            if (@(Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'WidgetRail is still closing. Wait for it to exit, then retry.' }
            $installed = Get-ItemProperty -LiteralPath 'HKCU:\Software\WidgetRail\Installation'
            Assert-PlainPath $installed.ApplicationRoot
            $releaseFile = Join-Path $installed.ApplicationRoot 'release.json'
            if ((Get-Item -LiteralPath $releaseFile).Length -gt 8MB) { throw 'Invalid installed release metadata.' }
            $release = Get-Content -LiteralPath $releaseFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($installed.Edition -ne $plan.edition) { throw 'The installed edition changed since this update was prepared.' }
            if ($Mode -eq 'Install' -and ($release.version -ne $plan.previousVersion -or $installed.ApplicationRoot -ne $plan.previousRoot)) { throw 'The installation changed since this update was prepared. Check updates again.' }
            if ($Mode -eq 'Recover' -and $release.version -notin @($plan.previousVersion, $plan.updateVersion)) { throw 'Another app version is installed. This recovery set will not replace an unrelated version.' }
            New-RecoveryProgress
            # Inno checks the overlay mutex, so release it only around setup.
            # Both operations refuse any real overlay before their file work.
            $runner = {
                param($path)
                $appLock.ReleaseMutex(); $appLock.Dispose(); $script:appLock = $null
                try {
                    $process = Start-Process -FilePath $path -PassThru
                    $process.WaitForExit()
                    $code = $process.ExitCode
                } finally {
                    $new = $false
                    $script:appLock = [Threading.Mutex]::new($true, 'Local\WidgetRail.OverlayHost.Running', [ref]$new)
                    if (!$new) { $script:appLock.Dispose(); $script:appLock = $null; throw 'WidgetRail opened during setup. Quit it before continuing recovery.' }
                }
                return $code
            }
            if ($Mode -eq 'Install') { Invoke-RecoveryInstall $PSScriptRoot $profile $plan $runner }
            else { Invoke-RecoveryRestore $PSScriptRoot $profile $plan $runner }
        } finally { if (Get-Variable RecoveryProgressForm -Scope Script -ErrorAction SilentlyContinue) { Close-RecoveryProgress }; $recoveryLock.ReleaseMutex(); $recoveryLock.Dispose() }
    } finally { if ($null -ne $appLock) { $appLock.ReleaseMutex(); $appLock.Dispose() } }
    [Windows.Forms.MessageBox]::Show('Completed. You can open WidgetRail from Start. Keep this recovery folder until you have tested the app.', 'WidgetRail recovery', 'OK', 'Information') | Out-Null
} catch {
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'WidgetRail recovery', 'OK', 'Error') | Out-Null
    exit 1
}
