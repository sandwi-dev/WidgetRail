param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$WidgetId = 'widgetrail.samples.playnite-library',
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw = winapp ui @Arguments -a $AppPid --json
    if ($LASTEXITCODE -ne 0) { throw "UI operation failed: $raw" }
    $result = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $result.found) { throw "UI wait timed out: $($Arguments[1])" }
    return $result
}
function Order {
    $tree = Ui @('inspect','Overlay.Tray','--depth','2','--interactive')
    return @($tree.windows.elements | Where-Object { $_.automationId -like 'Overlay.Widget.*' } |
        ForEach-Object automationId)
}
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add(@{name=$Name; passed=$true})
}
try {
    $target = "Overlay.Widget.$WidgetId"
    $null = Ui @('wait-for',$target,'-t','20000')
    $null = Ui @('focus',$target)
    Start-Sleep -Milliseconds 500
    $original = Order
    $position = [Array]::IndexOf($original, $target)
    if ($position -lt 0 -or $original.Count -lt 2) { throw 'The tray fixture needs two widgets and the requested item.' }
    $move = if ($position -lt $original.Count - 1) { 'right' } else { 'left' }
    $reverse = if ($move -eq 'right') { 'left' } else { 'right' }
    Check 'Native tray menu exposes reorder and restart without dispatching a widget action' {
        $null = Ui @('click',$target,'--right')
        $null = Ui @('wait-for','Overlay.TrayCommand.Reorder','-t','5000')
        $null = Ui @('wait-for','Overlay.TrayCommand.Restart','-t','3000')
        $windows = Ui @('list-windows')
        $hwnd = ($windows | Where-Object title -Like 'WidgetRail*WinUI frontend' | Select-Object -First 1).hwnd
        winapp ui screenshot -w $hwnd --capture-screen -o (Join-Path $OutputDirectory 'tray-menu.png') --json | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Screenshot failed.' }
    }
    Check 'Reorder command enters host mode and keeps the exact widget focused' {
        $null = Ui @('invoke','Overlay.TrayCommand.Reorder')
        $null = Ui @('wait-for','Overlay.Tray','-p','HelpText','--value','Move widget','--contains','-t','3000')
        $null = Ui @('wait-for',$target,'-p','HasKeyboardFocus','--value','True','-t','3000')
    }
    Check 'Native keyboard movement changes order without changing selected widget identity' {
        $null = Ui @('send-keys',$move,'--via','send-input')
        $null = Ui @('wait-for',$target,'-p','HasKeyboardFocus','--value','True','-t','3000')
        $changed = Order
        $expected = if ($move -eq 'right') { $position + 1 } else { $position - 1 }
        if ([Array]::IndexOf($changed, $target) -ne $expected) { throw 'Reorder did not move the selected identity exactly one position.' }
    }
    Check 'Finishing reorder retains the restored order and tray ownership' {
        $null = Ui @('send-keys',$reverse,'--via','send-input')
        $null = Ui @('send-keys','escape','--via','send-input')
        $null = Ui @('wait-for','Overlay.Tray','-p','HelpText','--value','Open widget','--contains','-t','3000')
        if (((Order) -join '|') -ne ($original -join '|')) { throw 'Restoring order changed other widget identities.' }
        $state = (Ui @('get-property','Overlay.Status','-p','HelpText')).properties.HelpText | ConvertFrom-Json
        if ($state.interactive) { throw 'Reorder transferred focus into the widget.' }
    }
    Check 'Dismissal returns focus without leaving a stale command menu' {
        $null = Ui @('click',$target,'--right')
        $null = Ui @('wait-for','Overlay.TrayCommand.Reorder','-t','3000')
        $null = Ui @('send-keys','escape','--via','send-input')
        $null = Ui @('wait-for',$target,'-p','HasKeyboardFocus','--value','True','-t','3000')
    }
} catch {
    $results.Add(@{name='Native tray commands'; passed=$false; error=$_.ToString()})
    throw
} finally {
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json')
    if ($CloseAfter) { $null = Ui @('invoke','Shell.Close') }
}
"Tray commands: $($results.Count) checks passed."
