[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-indexed/widget-ui'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI command failed: $($Arguments -join ' '): $raw"}
    return ($raw | ConvertFrom-Json)
}
function Check([string]$Name,[bool]$Passed) {
    $results.Add(@{name=$Name;passed=$Passed})
    if(-not $Passed){throw "Failed: $Name"}
}
function Key([string]$Value) { Ui @('send-keys',$Value,'--via','send-input') | Out-Null }
function Observe([string]$Name) {
    Key F6
    $value=(Ui @('get-value','IndexedWidget.Status')).text | ConvertFrom-Json
    $value | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$Name.json")
    if($value.failure){throw "Presenter failed: $($value.failure)"}
    return $value
}
function Until([string]$Name,[scriptblock]$Condition,[int]$Seconds=8) {
    $deadline=[DateTime]::UtcNow.AddSeconds($Seconds)
    do { $value=Observe $Name; if(& $Condition $value){return $value}; Start-Sleep -Milliseconds 50 } while([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $Name"
}
try {
    Ui @('wait-for','Widget.items.Item.0','-p','Name','--value','Item 0','-t','5000') | Out-Null
    $initial=Until '00-initial-entry' {param($v) $v.focus -eq 'Widget.items.Item.0'}
    Check 'initial collection focus enters a native item' ($initial.focus -eq 'Widget.items.Item.0')
    Ui @('focus','Widget.items.Item.0') | Out-Null
    $first=Until '01-ready' {param($v) $v.images -gt 0}
    Check 'real worker rows use a constrained virtualized native list' ($first.count -eq 100 -and $first.realized -lt 40 -and $first.height -lt 600 -and $first.height -gt 100)
    Check 'indexed row styles and worker artwork reach native rendering' ([Math]::Abs($first.imageWidth-48) -lt 0.1)
    Key F7
    $action=Until '02-action' {param($v) $v.status -eq 'row:0:0:open'}
    Check 'controller activation invokes the captured row once and preserves focus' ($action.focus -eq 'Widget.items.Item.0' -and $action.calls -eq 'Calls: 1')
    Key F8
    $parent=Until '03-parent' {param($v) $v.status -eq 'parent'}
    Check 'ancestor shortcut preserves the focused row' ($parent.focus -eq $action.focus -and $parent.calls -eq 'Calls: 2')
    Key F5
    Ui @('wait-for','Widget.items.Item.70','-p','Name','--value','Item 70','-t','5000') | Out-Null
    Ui @('focus','Widget.items.Item.70') | Out-Null
    $deep=Observe '04-deep'
    Key F11
    $refreshed=Until '05-refreshed' {param($v) $v.revision -eq 1 -and $v.images -gt 0}
    Check 'content refresh keeps native focus and viewport' ($refreshed.focus -eq $deep.focus -and [Math]::Abs($refreshed.offset-$deep.offset) -le 1)
    Key F10
    $navigation=Until '06-navigation' {param($v) $v.navigation -ne 'pending'} 12
    Check 'controller route crosses realization boundaries without lost movement' ($navigation.navigation -eq 'last:60;stalled:0')
    Key F12
    $reverse=Until '06-reversal' {param($v) $v.navigation -ne 'pending'} 12
    Check 'controller reversal returns through unrealized rows' ($reverse.navigation -eq 'last:50;stalled:0')
    Key F4
    $burst=Until '06-burst' {param($v) $v.navigation -ne 'pending'} 12
    Check 'batched controller frames coalesce without losing logical movement' ($burst.navigation.StartsWith('last:60;') -and $burst.focus -eq 'Widget.items.Item.60')
    Key F3
    $superseded=Observe '06-superseded'
    Check 'leaving the collection cancels pending focus without stealing it back' ($superseded.focus -eq 'Widget.parent')
    Ui @('invoke','Widget.grid') | Out-Null
    $grid=Until '07-grid' {param($v) $v.control -eq 'GridView' -and $v.realized -gt 0}
    Check 'same indexed contract uses native virtualized grid' ($grid.count -eq 100 -and $grid.realized -lt 70 -and $grid.height -lt 600)
    Key F10
    $gridNavigation=Until '08-grid-navigation' {param($v) $v.navigation -ne 'pending'} 12
    Check 'grid controller navigation retains the column across native realization' ($gridNavigation.navigation -eq ('last:'+[Math]::Min(99,12*$grid.columns)+';stalled:0'))
    Key F2
    $entry=Until '09-logical-focus' {param($v) $v.logicalFocus -ne 'pending'} 20
    Check 'logical entry verifies keys and preserves one-shot focus lifecycle' ($entry.logicalFocus -eq 'passed:12')
    Ui @('invoke','Widget.groups') | Out-Null
    $grouped=Until '10-grouped' {param($v) $v.groupCount -eq 4 -and $v.control -eq 'GridView'}
    Ui @('wait-for','Widget.items.Item.0','-p','Name','--value','Item 0','-t','5000') | Out-Null
    Check 'grouped worker rows retain exact flat count and native columns' ($grouped.count -eq 100 -and $grouped.columns -eq 3)
    Key F1
    $groupedNavigation=Until '11-grouped-navigation' {param($v) $v.groupedFocus -ne 'pending'} 12
    Check 'grouped controller navigation handles partial and empty sections with stable header updates' ($groupedNavigation.groupedFocus -eq 'passed:7')
    Ui @('invoke','Widget.grid') | Out-Null
    $groupedList=Until '12-grouped-list' {param($v) $v.groupCount -eq 4 -and $v.control -eq 'ListView' -and $v.realized -gt 0}
    Check 'same grouped contract supports native vertical list' ($groupedList.count -eq 100 -and $groupedList.realized -lt 50)
    Key F10
    $groupedListMoves=Until '13-grouped-list-navigation' {param($v) $v.navigation -ne 'pending'} 12
    Check 'grouped list traverses section headers without lost controller steps' ($groupedListMoves.navigation -eq 'last:60;stalled:0')
    Ui @('invoke','Widget.surfaces') | Out-Null
    Until '14-surfaces-ready' {param($v) $v.status -eq 'surfaces' -and $v.count -eq 100} | Out-Null
    Key F14
    $surfaceResult=Until '15-surfaces' {param($v) $v.surfaces -ne 'pending'} 20
    Check 'native surfaces preserve item artwork identity, reject late images and survive recycling' ($surfaceResult.surfaces -eq 'passed:8')
    Ui @('invoke','IndexedWidget.InputProbe') | Out-Null
    $inputResult=Until '16-input-route' {param($v) $v.inputRoute -ne 'pending'} 12
    Check 'shared input preserves release and indexed ancestor semantics' ($inputResult.inputRoute -eq 'passed:3')
    Ui @('invoke','IndexedWidget.ModalProbe') | Out-Null
    $modalResult=Until '17-indexed-modal' {param($v) $v.modalResult -ne 'pending'} 30
    Check 'real worker indexed parent survives native modal lifecycle' ($modalResult.modalResult -eq 'passed:24')
    Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'indexed-widget.png')) | Out-Null
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
    Ui @('invoke','Shell.Close') | Out-Null
}
"Indexed widget checks passed: $($results.Count)."
