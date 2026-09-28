param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$CloseAfter)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$diagnostic = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/pinned-window-result.json'
$started = (Get-Process -Id $AppPid).StartTime.ToUniversalTime()
function Ui([string[]]$Arguments, [long]$WindowHandle = 0) {
    $target = if ($WindowHandle -ne 0) { @('-w', $WindowHandle) } else { @('-a', $AppPid) }
    $raw = winapp ui @Arguments @target --json
    if ($LASTEXITCODE -ne 0) { throw "Native operation failed: $raw" }
    return ($raw -join "`n") | ConvertFrom-Json
}
function Phase([string]$Expected) {
    $until = [DateTime]::UtcNow.AddSeconds(8)
    do {
        if ((Get-Item $diagnostic -ErrorAction SilentlyContinue).LastWriteTimeUtc -ge $started) {
            $state = Get-Content $diagnostic -Raw | ConvertFrom-Json
            if ($state.phase -eq 'failed') { throw $state.error }
            if ($state.phase -eq $Expected) { return $state }
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
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'passive-opacity.png')) $hwnd
    $null = Ui @('invoke','PinWindow.Next') $hwnd
    $state = Phase 'interactive'
    $null = Ui @('click','PinWindow.Target') ([long]$state.handle)
    $null = Ui @('wait-for','PinWindow.Status','--value','Pinned clicks: 1','-t','3000') $hwnd
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'interactive.png')) ([long]$state.handle)
    $null = Ui @('invoke','PinWindow.Next') $hwnd
    $state = Phase 'passed'
    $state | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'result.json')
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'reopened-passive.png')) $hwnd
    "Pinned native window: $($state.checks.Count) checks passed."
} finally {
    if ($CloseAfter) { $null = Ui @('invoke','Shell.Close') }
}
