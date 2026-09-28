param(
    [Parameter(Mandatory)][int]$AppPid,
    [string]$OutputDirectory = 'artifacts/winui-shell/production-check',
    [string]$WidgetId = 'widgetrail.samples.clock',
    [string]$WidgetName = 'Clock'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function Invoke-Check([string]$Name, [scriptblock]$Check) {
    try { & $Check; $results.Add(@{name=$Name; status='PASS'}) }
    catch { $results.Add(@{name=$Name; status='FAIL'; detail=$_.Exception.Message}) }
}
function Assert-Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments -a $AppPid --json 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return ($output -join "`n" | ConvertFrom-Json)
}
Invoke-Check 'Real catalog selected the requested widget' {
    $null = Assert-Ui @('wait-for', 'Overlay.Status', '--value', $WidgetName, '-t', '20000')
}
Invoke-Check 'Native widget content is present' {
    $null = Assert-Ui @('wait-for', 'Overlay.Widget', '-t', '3000')
}
Invoke-Check 'Actual catalog entry is accessible' {
    $null = Assert-Ui @('wait-for', "Overlay.Widget.$WidgetId", '-t', '3000')
}
Invoke-Check 'Tray can reacquire native keyboard focus' {
    $null = Assert-Ui @('focus', "Overlay.Widget.$WidgetId")
    $focused = Assert-Ui @('get-focused')
    if (($focused | ConvertTo-Json -Depth 20 -Compress) -notmatch [regex]::Escape("Overlay.Widget.$WidgetId")) {
        throw 'Tray entry did not own focus.'
    }
}
Invoke-Check 'Native catalog entry reopens its real widget' {
    $null = Assert-Ui @('invoke', "Overlay.Widget.$WidgetId")
    $null = Assert-Ui @('wait-for', 'Overlay.Status', '--value', $WidgetName, '-t', '10000')
}
Invoke-Check 'Desktop screenshot captured for separate inspection' {
    $null = Assert-Ui @('screenshot', '--capture-screen', '-o', (Join-Path $OutputDirectory 'shell.png'))
}
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
$results | Format-Table
if (@($results | Where-Object status -EQ 'FAIL').Count -gt 0) { exit 1 }
