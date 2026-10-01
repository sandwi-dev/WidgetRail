[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-theme-refresh'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force $OutputDirectory
$settings = Join-Path $OutputDirectory ('settings-' + [guid]::NewGuid().ToString('N'))
$pipe = 'winui-style-' + [guid]::NewGuid().ToString('N')
$bridge = Join-Path $root 'tests/WidgetBridge.Tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64/WidgetBridge.Tests.exe'
if (-not (Test-Path -LiteralPath $bridge)) { throw 'Build WidgetBridge.Tests before this native test.' }
$server = Start-Process -FilePath $bridge -ArgumentList @('--serve-themed-indexed-validation', $pipe, ('"' + $settings + '"')) `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'bridge.txt') `
    -RedirectStandardError (Join-Path $OutputDirectory 'bridge-errors.txt')
$ownedPid = 0
try {
    $launchArgs = '--indexed-validation-pipe=' + $pipe + ' --style-validation-settings="' + $settings + '"'
    $launch = (& winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args $launchArgs | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Native theme fixture did not launch.' }
    $ownedPid = [int]$launch.ProcessId
    $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
    $resultPath = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/indexed-theme-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(50)
    $result = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            try {
                $candidate = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                if ($candidate.pid -eq $ownedPid) { $result = $candidate; break }
            } catch { }
        }
        if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Native theme fixture exited before its result.' }
        Start-Sleep -Milliseconds 200
    }
    if ($null -eq $result) { throw 'Native theme fixture timed out.' }
    $result | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory 'result.json')
    & winapp ui screenshot -a $ownedPid --capture-screen -o (Join-Path $OutputDirectory 'native.png') --json | Out-Null
    if (-not $result.passed) { throw $result.error }
    Write-Output ("Passed {0} native retained-theme checks." -f $result.checks.Count)
} finally {
    if ($ownedPid -gt 0 -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        & winapp ui invoke Shell.Close -a $ownedPid --json | Out-Null
        Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
    }
    if (-not $server.HasExited) { $server.WaitForExit(10000) | Out-Null }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}
