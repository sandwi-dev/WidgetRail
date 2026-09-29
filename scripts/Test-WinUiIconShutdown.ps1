[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-icon-shutdown'), [switch]$Hidden)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
foreach ($stage in @('render', 'pixels', 'surface')) {
    $launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json `
        --args "--validate-package-icons --validate-icon-shutdown --icon-shutdown-stage=$stage $(if ($Hidden) { '--icon-shutdown-hidden' })" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw "Could not launch $stage shutdown fixture." }
    $appId = [int]$launch.ProcessId
    $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$stage-launch.json")
    $resultPath = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/package-icon-shutdown-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $result = $null
    do {
        if (Test-Path -LiteralPath $resultPath) {
            try { $value = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { $value = $null }
            if ($value.pid -eq $appId) { $result = $value; break }
        }
        if (-not (Get-Process -Id $appId -ErrorAction SilentlyContinue)) { throw "Fixture $appId exited before reporting its drain." }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $result) { throw "Fixture $appId timed out; preserve this process for inspection before another deployment." }
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory "$stage-result.json")
    if (-not $result.passed -or $result.pendingPreparations -ne 0 -or $result.pendingNativeOperations -ne 0) {
        throw "Fixture $appId failed; preserve it for inspection: $($result.error)"
    }
    & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $appId
    Wait-Process -Id $appId -Timeout 15 -ErrorAction SilentlyContinue
    if (Get-Process -Id $appId -ErrorAction SilentlyContinue) { throw "Native shutdown did not exit process $appId." }
    $results.Add(@{ stage=$stage; hidden=$Hidden.IsPresent; checks=@($result.checks).Count; nativeDrain=$true; processExited=$true })
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
$results | Format-Table
