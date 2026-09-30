param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$CloseAfter)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$diagnostic = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/pinned-window-result.json'
$started = (Get-Process -Id $AppPid).StartTime.ToUniversalTime()
$fixtureRun = $null
$latestState = $null
function Ui([string[]]$Arguments, [long]$WindowHandle = 0) {
    $target = if ($WindowHandle -ne 0) { @('-w', $WindowHandle) } else { @('-a', $AppPid) }
    $raw = winapp ui @Arguments @target --json
    if ($LASTEXITCODE -ne 0) { throw "Native operation failed: $raw" }
    $value = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $value.found) { throw "Native wait did not find $($Arguments[1])." }
    return $value
}
function Phase([string]$Expected, [long]$After = 0) {
    $until = [DateTime]::UtcNow.AddSeconds(8)
    do {
        if ((Get-Item $diagnostic -ErrorAction SilentlyContinue).LastWriteTimeUtc -ge $started) {
            try { $state = Get-Content $diagnostic -Raw | ConvertFrom-Json } catch { $state = $null }
            if ($null -ne $state -and $state.processId -eq $AppPid) {
                if ($null -eq $script:fixtureRun) { $script:fixtureRun = $state.runId }
                if ($state.runId -ne $script:fixtureRun) { throw 'The owned fixture was replaced during the native test.' }
                $script:latestState = $state
                if ($state.phase -eq 'failed') { throw $state.error }
                if ($state.phase -eq $Expected -and $state.publication -gt $After) { return $state }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $until)
    throw "Native pinned window did not reach $Expected."
}
try {
    $null = Ui @('wait-for','PinWindow.Status','-t','8000')
    $windows = Ui @('list-windows')
    $main = @($windows | Where-Object title -Like 'WidgetRail*WinUI frontend')
    if ($main.Count -ne 1) { throw 'Could not identify the main owned window.' }
    $hwnd = [long]$main[0].hwnd
    $state = Phase 'passive'
    # This is a real mouse click at the underlying element's coordinates. Never
    # use InvokePattern for this gate: it would bypass native hit testing.
    $null = Ui @('click','PinWindow.Under') $hwnd
    $null = Ui @('wait-for','PinWindow.Status','--value','Underlying clicks: 1','-t','3000') $hwnd
    $state = Phase 'passive' $state.publication
    if ($state.underlyingClicks -ne 1 -or $state.pinnedClicks -ne 0) { throw 'The passive pointer click did not reach only the underlying target.' }
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'passive-opacity.png')) $hwnd
    $null = Ui @('invoke','PinWindow.Next') $hwnd
    $state = Phase 'interactive' $state.publication
    $null = Ui @('click','PinWindow.Target') ([long]$state.handle)
    $null = Ui @('wait-for','PinWindow.Status','--value','Pinned clicks: 1','-t','3000') $hwnd
    $state = Phase 'interactive' $state.publication
    if ($state.pinnedClicks -ne 1 -or $state.underlyingClicks -ne 1) { throw 'The interactive pointer click did not reach only the pinned target.' }
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'interactive.png')) ([long]$state.handle)
    $null = Ui @('invoke','PinWindow.Next') $hwnd
    $state = Phase 'passed' $state.publication
    $state | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'result.json')
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'reopened-passive.png')) $hwnd
    "Pinned native window: $($state.checks.Count) checks passed."
} finally {
    try {
        $finalState = Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
        if ($finalState.processId -eq $AppPid -and ($null -eq $fixtureRun -or $finalState.runId -eq $fixtureRun)) { $latestState = $finalState }
    } catch { }
    if ($null -ne $latestState) { $latestState | ConvertTo-Json -Depth 14 | Set-Content (Join-Path $OutputDirectory 'result.json') }
    if ($CloseAfter) { $null = Ui @('invoke','Shell.Close') }
}
