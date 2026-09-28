param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$WidgetId = 'media-sessions'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments, [long]$WindowHandle = 0) {
    $target = if ($WindowHandle) { @('-w', $WindowHandle) } else { @('-a', $AppPid) }
    $raw = winapp ui @Arguments @target --json
    if ($LASTEXITCODE -ne 0) { throw "UI operation failed: $raw" }
    $value = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $value.found) { throw "UI wait timed out: $($Arguments[1])" }
    return $value
}
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $checks.Add(@{name=$Name; passed=$true})
}
function State([long]$WindowHandle = 0) {
    $id = if ($WindowHandle) { 'Overlay.PinnedContent' } else { 'Overlay.Status' }
    return ((Ui @('get-property',$id,'-p','HelpText') $WindowHandle).properties.HelpText | ConvertFrom-Json)
}
function AwaitState([scriptblock]$Predicate, [long]$WindowHandle = 0) {
    $until = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $value = State $WindowHandle
        if (& $Predicate $value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $until)
    throw "Unexpected shell state: $($value | ConvertTo-Json -Compress -Depth 8)"
}
function Command([string]$Id) {
    $null = Ui @('click',"Overlay.Widget.$WidgetId",'--right') $mainHandle
    $null = Ui @('wait-for',"Overlay.TrayCommand.$Id",'-t','5000')
    $null = Ui @('invoke',"Overlay.TrayCommand.$Id")
}
try {
    $null = Ui @('wait-for',"Overlay.Widget.$WidgetId",'-t','20000')
    $main = @((Ui @('list-windows')) | Where-Object title -Like 'WidgetRail*WinUI frontend')
    if ($main.Count -ne 1) { throw 'Expected one main frontend window.' }
    $mainHandle = [long]$main[0].hwnd
    if ((State).pinnedWidget) {
        Command 'Pin.Remove'
        $null = AwaitState { param($s) $null -eq $s.pinnedWidget }
    }
    Check 'Tray full-widget command creates a current passive pin' {
        Command 'Pin.Full'
        $null = AwaitState { param($s) $s.pinnedWidget -eq $WidgetId -and $s.pinnedSelectionCurrent -and -not $s.pinnedInput }
        $pin = @((Ui @('list-windows')) | Where-Object title -Like 'WidgetRail pinned*')
        if ($pin.Count -ne 1) { throw 'Expected one pinned native window.' }
        $script:pinHandle = [long]$pin[0].hwnd
        $null = Ui @('wait-for','Overlay.PinnedContent','-t','5000') $pinHandle
    }
    Check 'Explicit interaction transfers native focus to the pin' {
        Command 'Pin.Interact'
        $null = AwaitState { param($s) $s.pinnedInput -and $s.pinnedSelectionCurrent } $pinHandle
        $focus = Ui @('get-focused') $pinHandle
        if (-not $focus.element) { throw 'Pinned window has no native focused element.' }
        $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'interactive-pin.png')) $pinHandle
    }
    Check 'Returning to main makes the pin passive without dropping selection' {
        $null = Ui @('click',"Overlay.Widget.$WidgetId") $mainHandle
        $null = AwaitState { param($s) -not $s.pinnedInput -and $s.pinnedSelectionCurrent }
    }
    Check 'Unpin removes the peer and clears shell selection' {
        Command 'Pin.Remove'
        $null = AwaitState { param($s) $null -eq $s.pinnedWidget }
        $pin = @((Ui @('list-windows')) | Where-Object title -Like 'WidgetRail pinned*')
        if ($pin.Count -ne 0) { throw 'Unpin left a peer native window.' }
    }
    Check 'Hiding the main overlay preserves a passive current pin' {
        Command 'Pin.Full'
        $null = AwaitState { param($s) $s.pinnedSelectionCurrent }
        $pin = @((Ui @('list-windows')) | Where-Object title -Like 'WidgetRail pinned*')
        $script:pinHandle = [long]$pin[0].hwnd
        $null = Ui @('focus',"Overlay.Widget.$WidgetId") $mainHandle
        $null = Ui @('send-keys','escape','--via','send-input') $mainHandle
        $null = AwaitState { param($s) -not $s.visible -and $s.pinnedSelectionCurrent -and -not $s.pinnedInput } $pinHandle
        $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'hidden-main-passive-pin.png')) $pinHandle
    }
} catch {
    $checks.Add(@{name='Production pin coordinator'; passed=$false; error=$_.ToString()})
    throw
} finally {
    $checks | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Production pin: $($checks.Count) checks passed. No media playback action invoked."
