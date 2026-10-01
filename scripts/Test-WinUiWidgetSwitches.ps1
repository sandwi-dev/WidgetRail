[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(0,1000)][int]$SoakCycles = 0,
    [ValidateRange(0,1800)][int]$SoakSeconds = 0
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue) { throw 'Close the candidate before isolated switching qualification.' }
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
# The fixture owns its catalog/settings and intentionally replaces one incarnation.
# Never point that mutation at an installed user's catalog.
$configuration = & (Join-Path $PSScriptRoot 'New-WinUiSwitchFixture.ps1') `
    -BridgeInstallation $BridgeInstallation -OutputDirectory $OutputDirectory
$resultPath = Join-Path $OutputDirectory 'result.json'
$launch = & winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json `
    --args "--shell-no-controller --shell-config=`"$configuration`" --validate-widget-switches=`"$resultPath`" --switch-soak-cycles=$SoakCycles --switch-soak-seconds=$SoakSeconds" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Switch fixture did not launch.' }
$ownedProcess = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
$resourceObserver = $null
try {
    if ($SoakSeconds -ge 30) {
        & (Join-Path $PSScriptRoot 'Assert-WinUiCandidatePayload.ps1') -AppPid $ownedProcess `
            -BuildDirectory (Join-Path $root 'src/OverlayFrontend.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64') `
            -OutputPath (Join-Path $OutputDirectory 'payload.json') | Out-Null
        $resourceObserver = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru `
            -ArgumentList @('-NoProfile', '-File', ('"' + (Join-Path $PSScriptRoot 'Measure-WinUiProcessResources.ps1') + '"'),
                '-AppPid', $ownedProcess, '-OutputPath', ('"' + (Join-Path $OutputDirectory 'process-tree.json') + '"'),
                '-SampleSeconds', (90 + [Math]::Max($SoakSeconds, $SoakCycles)), '-IntervalMilliseconds', 5000,
                '-Scenario', 'isolated-synthetic-switch-retirement') `
            -RedirectStandardOutput (Join-Path $OutputDirectory 'resources.stdout.log') `
            -RedirectStandardError (Join-Path $OutputDirectory 'resources.stderr.log')
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(90 + $SoakCycles + $SoakSeconds)
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
    if ($resourceObserver) {
        if (-not $resourceObserver.WaitForExit(15000)) {
            $resourceObserver.Kill(); $resourceObserver.WaitForExit();
            throw 'Resource observer did not finish after its owned frontend exited.'
        }
        if ($resourceObserver.ExitCode -ne 0) { throw 'Resource observation failed; inspect resources.stderr.log.' }
        $resourceObserver.Dispose()
    }
}
