$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:ready = $false
$updateLock = $null
$script:ownsLock = $false
try {
    . (Join-Path $PSScriptRoot 'UpdateInstaller.ps1')
    Assert-UpdatePlainPath $PSScriptRoot
    $planPath = Join-Path $PSScriptRoot 'plan.json'
    Assert-UpdatePlainPath $planPath
    if ((Get-Item -LiteralPath $planPath).Length -gt 8192) { throw 'Invalid update plan.' }
    $plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $updateLock = [Threading.Mutex]::new($true, 'Local\WidgetRail.UpdateInstaller', [ref]$script:ownsLock)
    if (!$script:ownsLock) { throw 'Another update is already starting.' }
    $readInstallation = {
        $installed = Get-ItemProperty -LiteralPath 'HKCU:\Software\WidgetRail\Installation'
        Assert-UpdatePlainPath $installed.ApplicationRoot
        $releasePath = Join-Path $installed.ApplicationRoot 'release.json'
        Assert-UpdatePlainPath $releasePath
        if ((Get-Item -LiteralPath $releasePath).Length -gt 8MB) { throw 'Invalid installed release metadata.' }
        $release = Get-Content -LiteralPath $releasePath -Raw -Encoding UTF8 | ConvertFrom-Json
        [pscustomobject]@{ ApplicationRoot = $installed.ApplicationRoot; Edition = $installed.Edition; version = $release.version }
    }
    $interactive = $plan.PSObject.Properties['interactive'] -and $plan.interactive -eq $true
    $result = Invoke-UpdateHandoff $PSScriptRoot $plan $readInstallation {
        # Acknowledge only after verification. The host then closes gracefully.
        [Console]::Out.WriteLine('ready')
        [Console]::Out.Flush()
        $script:ready = $true
    } { Wait-UpdateHostClosed } {
        param($path)
        $arguments = Get-UpdateSetupArguments $PSScriptRoot $interactive $plan.previousRoot
        $window = if ($interactive) { 'Normal' } else { 'Hidden' }
        $process = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle $window -PassThru
        try { $process.WaitForExit(); return $process.ExitCode } finally { $process.Dispose() }
    } {
        param($root)
        $updateLock.ReleaseMutex(); $script:ownsLock = $false
        Start-Process -FilePath (Join-Path $root 'OverlayFrontend.WinUI.exe') -ArgumentList '--show' -WindowStyle Hidden | Out-Null
    } -Preflight {
        if ($interactive) { return 'ready' }
        $installer = Join-Path $PSScriptRoot 'update.exe'
        # Do not execute an older installer that could ignore the new switch.
        if (([string][Diagnostics.FileVersionInfo]::GetVersionInfo($installer).FileDescription).Trim() -ne 'WidgetRail Setup (unattended v1)') { return 'interactive-required' }
        $status = Join-Path $PSScriptRoot ('preflight-' + [guid]::NewGuid().ToString('N') + '.txt')
        $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', ('/WRAILPREFLIGHT="' + $status + '"'))
        $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Setup preflight timed out.' }
            if (!(Test-Path -LiteralPath $status)) { return 'interactive-required' }
            Assert-UpdatePlainPath $status
            if ((Get-Item -LiteralPath $status).Length -gt 32) { return 'interactive-required' }
            return (Get-Content -LiteralPath $status -Raw).Trim()
        } finally { $process.Dispose() }
    } -RecordOutcome {
        param($outcome)
        $warning = Join-Path $PSScriptRoot 'warning.txt'
        if ($outcome -eq 'success' -and (Test-Path -LiteralPath $warning)) { $outcome = 'startup-warning' }
        $resultPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'result.json'
        Assert-UpdatePlainPath $resultPath
        [IO.File]::WriteAllText($resultPath, (@{ outcome=$outcome; version=$plan.updateVersion } | ConvertTo-Json -Compress))
    }
    if ($result -eq 'interactive-required') { [Console]::Out.WriteLine('interactive-required'); [Console]::Out.Flush() }
} catch {
    if ($script:ready) {
        # Do not strand controller users behind a helper-owned message box.
        # If no verified app could be reopened, leave persistent diagnostics.
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'handoff-error.txt'), $_.Exception.Message)
    }
    exit 1
} finally {
    if ($null -ne $updateLock) { if ($script:ownsLock) { $updateLock.ReleaseMutex() }; $updateLock.Dispose() }
}
