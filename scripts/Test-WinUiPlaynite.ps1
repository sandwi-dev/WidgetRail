param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Home', 'Library')][string]$Page = 'Home',
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments -a $AppPid --json 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return ($output -join "`n" | ConvertFrom-Json)
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name; status='PASS'}) }
    catch { $results.Add(@{name=$Name; status='FAIL'; detail=$_.Exception.Message}) }
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
