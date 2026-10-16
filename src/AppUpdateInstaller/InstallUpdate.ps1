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
    Invoke-UpdateHandoff $PSScriptRoot $plan $readInstallation {
        # Acknowledge only after verification. The host then closes gracefully.
        [Console]::Out.WriteLine('ready')
        [Console]::Out.Flush()
        $script:ready = $true
    } { Wait-UpdateHostClosed } {
        param($path)
        $process = Start-Process -FilePath $path -PassThru
        try { $process.WaitForExit(); return $process.ExitCode } finally { $process.Dispose() }
    } {
        param($root)
        $updateLock.ReleaseMutex(); $script:ownsLock = $false
        Start-Process -FilePath (Join-Path $root 'OverlayFrontend.WinUI.exe') -ArgumentList '--show' | Out-Null
    }
} catch {
    if ($script:ready) {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'WidgetRail update', 'OK', 'Error') | Out-Null
    }
    exit 1
} finally {
    if ($null -ne $updateLock) { if ($script:ownsLock) { $updateLock.ReleaseMutex() }; $updateLock.Dispose() }
}
