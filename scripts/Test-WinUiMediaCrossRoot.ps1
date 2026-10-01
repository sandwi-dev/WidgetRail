[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-media/cross-root'),
    [switch]$CapturePixels
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force $OutputDirectory
$resultPath = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/embedded-media-cross-root-result.json'
$started = [DateTime]::UtcNow
$raw = & winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json --args '--validate-embedded-media-cross-root'
if ($LASTEXITCODE -ne 0) { throw "Unable to launch the owned cross-root fixture: $raw" }
$launch = ($raw -join "`n") | ConvertFrom-Json
$ownedPid = [int]$launch.ProcessId
if ($ownedPid -le 0) { throw 'The launcher did not identify an owned process.' }
$launch | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'launch.json')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $result = $null
    $captured = $false
    $pixelError = $null
    do {
        $file = Get-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
        if ($file -and $file.LastWriteTimeUtc -ge $started) {
            try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
            if ($CapturePixels -and -not $captured -and $result.phase -eq 'cross-root-peer') {
                try {
                    & (Join-Path $PSScriptRoot 'Capture-WinUiTestWindow.ps1') -AppPid $ownedPid -WindowHandle $result.peerHwnd `
                        -OutputPath (Join-Path $OutputDirectory 'peer.png') -RequireMediaPixels -PassiveTopmost | Out-Null
                } catch { $pixelError = $_.Exception.Message }
                $captured = $true
            }
            if ($result.passed -eq $false -or $result.phase -eq 'complete') { break }
        }
        if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'The cross-root fixture exited before publishing its result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -ne $result) { $result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $OutputDirectory 'result.json') }
    if ($result.passed -ne $true -or $result.phase -ne 'complete' -or @($result.transferObservations).Count -lt 3) {
        throw "Cross-root transfer failed or timed out: $($result.error)"
    }
    if ($CapturePixels -and (!$captured -or $pixelError)) { throw "Passive media pixel check failed: $pixelError" }
    [pscustomobject]@{ Result = 'passed'; Checks = @($result.checks).Count; Observations = @($result.transferObservations).Count; PixelsCaptured = $captured }
}
finally {
    if ($null -ne $result) {
        $result | Add-Member -NotePropertyName pixelError -NotePropertyValue $pixelError -Force
        $result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $OutputDirectory 'result.json')
    }
    if (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
        Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) { throw 'Owned cross-root fixture did not exit; stop before redeploying.' }
    }
}
