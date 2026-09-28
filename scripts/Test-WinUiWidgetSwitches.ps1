[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
# The fixture owns its catalog/settings and intentionally replaces one incarnation.
# Never point that mutation at an installed user's catalog.
$configuration = & (Join-Path $PSScriptRoot 'New-WinUiSwitchFixture.ps1') `
    -BridgeInstallation $BridgeInstallation -OutputDirectory $OutputDirectory
$resultPath = Join-Path $OutputDirectory 'result.json'
$launch = & winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json `
    --args "--shell-no-controller --shell-config=`"$configuration`" --validate-widget-switches=`"$resultPath`"" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Switch fixture did not launch.' }
$ownedProcess = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $result = $null
    do {
        if (Test-Path -LiteralPath $resultPath) {
            # Publication is tiny but may overlap this observer's read.
            try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { $result = $null }
            if ($result -and $result.pid -eq $ownedProcess) { break }
        }
        if (-not (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue)) { throw 'Switch fixture exited before publishing a result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $result -or $result.pid -ne $ownedProcess) { throw 'Switch fixture did not publish its owned result before the deadline.' }
    if ($result.result.passed -ne $true -or @($result.result.checks).Count -eq 0) {
        throw "Switch fixture failed: $($result.result.error)"
    }
    "Widget switch native checks passed: $(@($result.result.checks).Count)."
} finally {
    if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedProcess
        Wait-Process -Id $ownedProcess -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) { throw 'Switch fixture did not finish normal shutdown.' }
    }
}
