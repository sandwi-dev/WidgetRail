[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-controls'))
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Invoke-Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI command failed: $($Arguments -join ' '): $raw"}
    return ($raw | ConvertFrom-Json)
}
function Assert-Result([string]$Name,[bool]$Passed) {
    $results.Add(@{name=$Name;passed=$Passed})
    if(-not $Passed){throw "Failed: $Name"}
}
function Assert-Focus([string]$Name,[string]$Id) {
    $deadline=[DateTime]::UtcNow.AddSeconds(2)
    do {
        $result=Invoke-Ui @('get-property',$Id,'--property','HasKeyboardFocus')
        if($result.properties.HasKeyboardFocus -eq 'True'){break}
        Start-Sleep -Milliseconds 25
    } while([DateTime]::UtcNow -lt $deadline)
    Assert-Result $Name ($result.properties.HasKeyboardFocus -eq 'True')
    return $result.element.selector
}
function Key([string]$Name) { Invoke-Ui @('send-keys',$Name,'--via','send-input') | Out-Null }
try {
    Invoke-Ui @('wait-for','Widget.second','-t','5000') | Out-Null
    $original=Assert-Focus 'authored second control receives cold entry focus' 'Widget.second'
    Invoke-Ui @('invoke','Widget.second') | Out-Null
    Assert-Result 'one control invocation dispatches once' (Invoke-Ui @('wait-for','Controls.Status','--value','Actions: 1;','--contains','-t','2000')).found
    Key F7
    $inserted=Assert-Focus 'sibling insertion retains focus' 'Widget.second'
    Assert-Result 'insertion retains control identity' ($inserted -eq $original)
    Key F8
    $reparented=Assert-Focus 'reparent retains focus' 'Widget.second'
    Assert-Result 'reparent retains control identity' ($reparented -eq $original)
    Key F5
    Assert-Focus 'new input scope receives its initial focus' 'Widget.third' | Out-Null
    Invoke-Ui @('invoke','Widget.second') | Out-Null
    Assert-Result 'inactive scope cannot dispatch' ((Invoke-Ui @('get-value','Controls.Status')).text -match 'actions: 1$')
    Key F5
    Assert-Focus 'return to parent scope restores its focus' 'Widget.second' | Out-Null
    Invoke-Ui @('focus','Controls.Outside') | Out-Null
    Key F12
    Assert-Focus 'ordinary widget update does not steal external focus' 'Controls.Outside' | Out-Null
    Invoke-Ui @('focus','Widget.second') | Out-Null
    Key F9
    Assert-Focus 'disabled focus falls back to valid authored target' 'Widget.first' | Out-Null
    Key F10
    $replacement=Assert-Focus 'new worker applies initial focus' 'Widget.second'
    Assert-Result 'new worker creates new control identity' ($replacement -ne $original)
    Invoke-Ui @('invoke','Widget.second') | Out-Null
    Assert-Result 'new worker receives current command once' (Invoke-Ui @('wait-for','Controls.Status','--value','Actions: 2; source: second; owner: 2','-t','2000')).found
    Key F11
    Assert-Result 'retired control command cannot invoke new worker' (Invoke-Ui @('wait-for','Controls.Status','--value','Retired command checked; captured: True; actions: 2','-t','2000')).found
    Key F3
    Invoke-Ui @('invoke','Widget.second') | Out-Null
    Invoke-Ui @('invoke','Widget.second') | Out-Null
    Assert-Result 'pending admission does not suppress later deliberate presses' (Invoke-Ui @('wait-for','Controls.Status','--value','Actions: 4; source: second; owner: 2','-t','2000')).found
    Key F2
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'controls.png')) | Out-Null
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'controls-results.json')
}
"Control checks passed: $($results.Count)."
