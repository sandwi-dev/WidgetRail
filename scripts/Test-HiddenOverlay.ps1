[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$overlayPath = Resolve-Path (Join-Path $repositoryRoot "src\OverlayHost\out\$Configuration\OverlayHost.exe")
$startupError = Join-Path $env:LOCALAPPDATA 'GameBarAlternative\startup-error.log'
$startupErrorTimestamp = if (Test-Path -LiteralPath $startupError) {
    (Get-Item -LiteralPath $startupError).LastWriteTimeUtc
} else { $null }
$overlayProcess = Start-Process -FilePath $overlayPath -ArgumentList '--hidden' -WindowStyle Hidden -PassThru
try {
    $overlayProcess.WaitForExit(1000) | Out-Null
    $overlayProcess.Refresh()
    if ($overlayProcess.HasExited) {
        throw "OverlayHost exited during initialization with code $($overlayProcess.ExitCode)."
    }
    $currentStartupErrorTimestamp = if (Test-Path -LiteralPath $startupError) {
        (Get-Item -LiteralPath $startupError).LastWriteTimeUtc
    } else { $null }
    if ($null -ne $currentStartupErrorTimestamp -and
        ($null -eq $startupErrorTimestamp -or $currentStartupErrorTimestamp -ne $startupErrorTimestamp)) {
        throw 'OverlayHost reported a startup failure. Inspect the local startup-error log.'
    }
    Write-Output 'OverlayHost initialized and remained resident while hidden.'
}
finally {
    if (-not $overlayProcess.HasExited) {
        try { $overlayProcess.Kill($true) } catch [InvalidOperationException] { }
        if (-not $overlayProcess.WaitForExit(5000)) {
            throw 'OverlayHost did not terminate within five seconds after the smoke test.'
        }
    }
    $overlayProcess.Dispose()
}
