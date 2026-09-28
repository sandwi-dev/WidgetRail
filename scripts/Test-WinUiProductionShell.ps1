[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/product-shell/native'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$null = New-Item -ItemType Directory -Force $OutputDirectory
$started = [DateTime]::UtcNow
$launch = & winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args '--validate-production-shell' | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Production layout fixture did not launch.' }
$ownedProcess = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
try {
    $path = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/production-shell-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $result = $null
    do {
        $file = Get-Item $path -ErrorAction SilentlyContinue
        if ($file -and $file.LastWriteTimeUtc -ge $started) {
            $result = Get-Content $path -Raw | ConvertFrom-Json
            if ($result.result -in @('passed','failed')) { break }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'result.json')
    & winapp ui screenshot -a $ownedProcess --capture-screen -o (Join-Path $OutputDirectory 'shell.png') --json | Out-Null
    if ($result.result -ne 'passed' -or @($result.checks).Count -eq 0) { throw "Production shell fixture failed: $($result.error)" }
    "Production shell native checks passed: $(@($result.checks).Count)."
} finally {
    if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedProcess
        Wait-Process -Id $ownedProcess -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) { throw 'Fixture did not finish normal shutdown.' }
    }
}
