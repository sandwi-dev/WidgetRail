[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-focus-policy'))
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Invoke-Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI command failed: $($Arguments -join ' '): $raw"}
    return ($raw | ConvertFrom-Json)
}
function Assert-Focus([string]$Name,[string]$Id) {
    $deadline=[DateTime]::UtcNow.AddSeconds(2)
    do {
        $result=Invoke-Ui @('get-property',$Id,'--property','HasKeyboardFocus')
        if($result.properties.HasKeyboardFocus -eq 'True'){break}
        Start-Sleep -Milliseconds 25
    } while([DateTime]::UtcNow -lt $deadline)
    $passed=$result.properties.HasKeyboardFocus -eq 'True'
    $results.Add(@{name=$Name;passed=$passed})
    if(-not $passed){throw "Failed: $Name"}
}
function Key([string]$Name) { Invoke-Ui @('send-keys',$Name,'--via','send-input') | Out-Null }
function Focus([string]$Id) { Invoke-Ui @('focus',$Id) | Out-Null }
try {
    Invoke-Ui @('wait-for','Widget.header','-t','5000') | Out-Null
    Assert-Focus 'initial header focus without pointer input' 'Widget.header'
    Key F2
    Assert-Focus 'deferred empty-group entry keeps existing focus' 'Widget.header'
    Key F3
    Assert-Focus 'ready content completes deferred entry at authored child' 'Widget.second'
    Focus 'Widget.header'
    Key F12
    Assert-Focus 'already consumed request does not steal focus on update' 'Widget.header'
    Key F4
    Assert-Focus 'native directional entry restores group child' 'Widget.second'
    Focus 'Widget.third'
    Focus 'Widget.header'
    Key F2
    Assert-Focus 'fresh request restores remembered child' 'Widget.third'
    Focus 'Widget.header'
    Key F7
    Key F2
    Assert-Focus 'disabled remembered child falls back within group' 'Widget.second'
    Key F7
    Focus 'Widget.first'
    Key F5
    Assert-Focus 'native explicit neighbor overrides geometric next' 'Widget.third'
    Focus 'Widget.first'
    Key F6
    Key F5
    Assert-Focus 'removed neighbor restores native geometric navigation' 'Widget.second'
    Key F10
    Focus 'Widget.header'
    Key F5
    Assert-Focus 'explicit group neighbor uses its remembered child' 'Widget.second'
    Focus 'Widget.third'
    Key F4
    Assert-Focus 'directional navigation stays inside active input scope' 'Widget.third'
    Focus 'Widget.first'
    Focus 'Widget.header'
    Key F8
    Assert-Focus 'replacement worker resets entry to its initial target' 'Widget.header'
    Key F4
    Assert-Focus 'replacement worker cannot inherit old group child' 'Widget.second'
    Focus 'Widget.header'
    Key F3
    Key F2
    Key F9
    Key F3
    Assert-Focus 'withdrawn deferred request cannot move focus after content arrives' 'Widget.header'
    Key F8
    Key F3
    Key F2
    Key F4
    Key F3
    Assert-Focus 'user navigation cancels deferred entry even at an empty edge' 'Widget.header'
    Key F1
    Key F8
    Assert-Focus 'passive preview cannot steal host tray focus on worker replacement' 'FocusPolicy.Outside'
    Key F2
    Key F12
    Assert-Focus 'passive preview defers authored entry while continuing publications' 'FocusPolicy.Outside'
    Key F1
    Assert-Focus 'explicit host entry completes the current authored request' 'Widget.second'
    Key F1
    Key F8
    Key F1
    Assert-Focus 'host entry uses the replacement owner initial target' 'Widget.header'
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'focus-policy.png')) | Out-Null
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Focus policy checks passed: $($results.Count)."
