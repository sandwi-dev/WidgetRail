param(
    [Parameter(Mandatory)][int]$AppPid,
    [string]$OutputDirectory = 'artifacts/winui-shell/production-check',
    [string]$WidgetId = 'widgetrail.samples.clock',
    [string]$WidgetName = 'Clock',
    [switch]$CloseAfter
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
    $null = Assert-Ui @('wait-for', 'Overlay.Shell', '--value', $WidgetName, '-t', '20000')
}
Invoke-Check 'Actual catalog entry is accessible' {
    $null = Assert-Ui @('wait-for', "Overlay.Widget.$WidgetId", '-t', '3000')
}
if ($WidgetId -eq 'widgetrail.samples.clock') {
    Invoke-Check 'Real widget acquired cold entry focus without injected click' {
        $focus = Assert-Ui @('get-property', 'Widget.refresh', '--property', 'HasKeyboardFocus')
        if ($focus.properties.HasKeyboardFocus -ne 'True') { throw 'Clock refresh did not have initial keyboard focus.' }
    }
    Invoke-Check 'Worker action updates an admitted shell publication' {
        $before = (Assert-Ui @('get-property', 'Overlay.Shell', '--property', 'HelpText')).properties.HelpText | ConvertFrom-Json
        $null = Assert-Ui @('invoke', 'Widget.refresh')
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $after = (Assert-Ui @('get-property', 'Overlay.Shell', '--property', 'HelpText')).properties.HelpText | ConvertFrom-Json
            if ($after.publication -gt $before.publication) { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($after.publication -le $before.publication) { throw 'No new admitted publication after worker action.' }
    }
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
    $null = Assert-Ui @('wait-for', 'Overlay.Shell', '--value', $WidgetName, '-t', '10000')
}
Invoke-Check 'Desktop screenshot captured for separate inspection' {
    $windows = Assert-Ui @('list-windows')
    $main = @($windows | Where-Object title -EQ 'WidgetRail — WinUI frontend')
    if ($main.Count -ne 1) { throw 'Could not identify the owned production HWND.' }
    $null = Assert-Ui @('screenshot', '-w', $main[0].hwnd, '--capture-screen', '-o', (Join-Path $OutputDirectory 'shell.png'))
}
if ($CloseAfter) {
    Invoke-Check 'Owned bridge and workers exit with the frontend' {
        $diagnostics = (Assert-Ui @('get-property', 'Overlay.Shell', '--property', 'HelpText')).properties.HelpText | ConvertFrom-Json
        $bridge = [int]$diagnostics.bridgePid
        if ($bridge -le 0) { throw 'No owned bridge PID in diagnostics.' }
        $workers = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$bridge" | Select-Object -ExpandProperty ProcessId)
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $AppPid
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        do {
            $remaining = @(Get-Process -Id (@($AppPid, $bridge) + $workers) -ErrorAction SilentlyContinue)
            if ($remaining.Count -eq 0) { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($remaining.Count -ne 0) { throw 'Frontend shutdown left owned processes running.' }
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
$results | Format-Table
if (@($results | Where-Object status -EQ 'FAIL').Count -gt 0) { exit 1 }
