param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Home', 'Library')][string]$Page = 'Home',
    [switch]$TrayReentry,
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $target = @('-a', $AppPid)
    if ($Arguments[0] -eq 'screenshot') {
        $windows = winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
        $main = @($windows | Where-Object title -Like 'WidgetRail*WinUI frontend')
        if ($main.Count -ne 1) { throw 'Could not identify the owned shell window.' }
        $target = @('-w', $main[0].hwnd)
    }
    $output = & winapp ui @Arguments @target --json 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    $result = $output -join "`n" | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $result.found) { throw "UI wait timed out: $($Arguments[1])" }
    return $result
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name; status='PASS'}) }
    catch {
        $results.Add(@{name=$Name; status='FAIL'; detail=$_.Exception.Message})
        try {
            $null = Ui @('screenshot', '--capture-screen', '-o', (Join-Path $OutputDirectory 'failure.png'))
            Ui @('get-focused') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'failure-focus.json')
            Ui @('get-property', 'Overlay.Status') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'failure-status.json')
        } catch { } # Keep the original assertion failure when the process exited.
    }
}
# Only navigate or open details. Never invoke Play/Install or any metadata action.
# Run against a newly started production shell, not a fixture or retry-recovered view.
try {
    Check 'Cold startup reached the real Playnite widget without Retry' {
        $null = Ui @('wait-for', 'Overlay.Status', '--value', 'Playnite Library', '-t', '20000')
    }
    if ($Page -eq 'Library') {
        Check 'Navigate to Library' {
            $null = Ui @('invoke', 'Widget.playnite-library.destinations.compact-b718f1354f7247312eca086d')
        }
    }
    $collection = if ($Page -eq 'Home') { 'Widget.playnite-library.library.grid' } else { 'Widget.playnite-library.library.scroll' }
    Check 'Actual provider row replaced the disabled saved display or loading placeholder' {
        $null = Ui @('wait-for', "$collection.Item.0", '-t', '10000')
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $row = (Ui @('get-property', "$collection.Item.0")).element
            if ($row.isEnabled -and $row.name -and $row.name -notlike 'Loading item *') { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if (-not $row.isEnabled -or -not $row.name -or $row.name -like 'Loading item *') { throw 'Provider row did not become ready.' }
    }
    Check 'Capture the real page for visual review' {
        $null = Ui @('screenshot', '--capture-screen', '-o', (Join-Path $OutputDirectory "$Page.png"))
    }
    Check 'Poster activation opens details and focuses Play or Install without activating it' {
        if ($TrayReentry) {
            $null = Ui @('focus', 'Overlay.Widget.widgetrail.samples.playnite-library')
            $ownership = Ui @('get-property', 'Overlay.Status', '-p', 'HelpText')
            $ownership | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'tray-ownership.json')
            if (($ownership.properties.HelpText | ConvertFrom-Json).interactive -ne $false) {
                throw 'Tray did not own interaction before the single poster invocation.'
            }
        }
        Ui @('get-focused') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'before-activation-focus.json')
        Ui @('get-property', 'Overlay.Status', '-p', 'HelpText') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'before-activation-state.json')
        $null = Ui @('invoke', "$collection.Item.0")
        $null = Ui @('wait-for', 'Widget.playnite-library.details.play', '-t', '10000')
        $null = Ui @('wait-for', 'Widget.playnite-library.details.play', '-p', 'HasKeyboardFocus', '--value', 'True', '-t', '5000')
        # Allow the configured entrance animation to settle before capture.
        Start-Sleep -Milliseconds 1500
        $null = Ui @('screenshot', '--capture-screen', '-o', (Join-Path $OutputDirectory "$Page-details.png"))
    }
} finally {
    if ($CloseAfter) { Check 'Close the owned test shell' { $null = Ui @('invoke', 'Shell.Close') } }
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
$results | Format-Table
if (@($results | Where-Object status -EQ 'FAIL').Count -gt 0) { exit 1 }
