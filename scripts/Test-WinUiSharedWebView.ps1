[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/shared-webview/native'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$resultPath = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/embedded-media-result.json'
foreach ($mode in @('baseline', 'shared')) {
    $started = [DateTime]::UtcNow
    $arguments = '--validate-embedded-media --validate-shared-webview'
    if ($mode -eq 'baseline') { $arguments += ' --webview-separate-baseline' }
    $raw = & winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
        --no-build --arch x64 -p Platform=x64 --detach --json --args $arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture launch failed: $raw" }
    $launch = ($raw -join "`n") | ConvertFrom-Json
    $ownedPid = [int]$launch.ProcessId
    if ($ownedPid -le 0) { throw 'Fixture process identity missing.' }
    $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$mode-launch.json")
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(150)
        $result = $null
        do {
            $file = Get-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
            if ($file -and $file.LastWriteTimeUtc -ge $started) {
                try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
                if ($result.passed -eq $false -or $result.phase -eq 'complete') { break }
            }
            if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Fixture exited without a completed result.' }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($null -ne $result) { $result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $OutputDirectory "$mode.json") }
        if ($result.passed -ne $true -or $result.phase -ne 'complete') { throw "Shared WebView $mode failed or timed out: $($result.error)" }
        [pscustomobject]@{ Mode = $mode; Passed = @($result.checks).Count; Failed = 0; Processes = @($result.processSnapshot).Count }
    }
    finally {
        $process = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
        if ($process) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(10000)) { throw "Owned fixture $ownedPid failed to shut down cleanly." }
        }
    }
}
