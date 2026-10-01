[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-discovered/native'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$bridgePath = Join-Path $root 'tests/WidgetBridge.Tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64/WidgetBridge.Tests.exe'
$project = Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'
$diagnostic = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/discovered-result.json'
$pipe = 'discovered-validation-' + [guid]::NewGuid().ToString('N')
$bridge = $null
$ownedPid = 0
$mainWindow = $null
$started = [DateTime]::UtcNow
# Requires a built fixture worker and analyzer-built frontend. The caller owns
# the shared development package deployment slot. No controller owner or provider
# credentials are created; the fixture serves synthetic bounded pages only.
try {
    $bridge = Start-Process -FilePath $bridgePath -ArgumentList @('--serve-discovered-validation', $pipe) `
        -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $OutputDirectory 'bridge.stdout.log') `
        -RedirectStandardError (Join-Path $OutputDirectory 'bridge.stderr.log')
    $raw = & winapp run $project --no-build --arch x64 -p Platform=x64 --detach --json --args "--indexed-validation-pipe=$pipe"
    if ($LASTEXITCODE -ne 0) { throw "Native fixture launch failed: $raw" }
    $launch = ($raw -join "`n") | ConvertFrom-Json
    $ownedPid = [int]$launch.ProcessId
    if ($ownedPid -le 0) { throw 'No owned native process returned.' }
    $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
    $ready = winapp ui wait-for Shell.Close -a $ownedPid -t 10000 --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $ready.found) { throw 'Fixture shell did not appear.' }
    $windows = winapp ui list-windows -a $ownedPid --json | ConvertFrom-Json
    $mainWindow = $windows | Where-Object title -Like 'WidgetRail*WinUI frontend' | Select-Object -First 1
    if (-not $mainWindow) { throw 'Owned native window not found.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $result = $null
    do {
        $file = Get-Item -LiteralPath $diagnostic -ErrorAction SilentlyContinue
        if ($file -and $file.LastWriteTimeUtc -ge $started) {
            try { $result = Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json } catch { }
            if ($null -ne $result) { break }
        }
        if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Native fixture exited without a result.' }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $result) { throw 'Native discovered collection fixture timed out.' }
    $result | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $OutputDirectory 'result.json')
    # Let the final asserted layout reach the desktop compositor before capturing.
    Start-Sleep -Milliseconds 250
    winapp ui screenshot -w $mainWindow.hwnd --capture-screen -o (Join-Path $OutputDirectory 'native.png') --json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot capture native collection evidence.' }
    if (-not $result.passed -or @($result.checks).Count -eq 0 -or @($result.failures).Count -ne 0) {
        throw 'Native collection validation failed; see result.json.'
    }
    "Discovered collections: $(@($result.checks).Count) native assertions passed."
} finally {
    if ($ownedPid -gt 0 -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        winapp ui invoke Shell.Close -a $ownedPid --json | Out-File (Join-Path $OutputDirectory 'close.json')
    }
    if ($bridge) {
        if (-not $bridge.WaitForExit(3000)) { $bridge.Kill($true); $bridge.WaitForExit() }
        $bridge.Dispose()
    }
}
