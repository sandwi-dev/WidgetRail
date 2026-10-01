[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [Parameter(Mandatory)][ValidateSet('flat','grouped','adapted')][string]$Mode,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-grouped'))
$ErrorActionPreference='Stop'
$OutputDirectory=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $Mode
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI command failed: $($Arguments -join ' '): $raw"}
    return ($raw | ConvertFrom-Json)
}
function Key([string]$Value){Ui @('send-keys',$Value,'--via','send-input') | Out-Null}
function Check([string]$Name,[bool]$Passed){
    $results.Add(@{name=$Name;passed=$Passed})
    if(-not $Passed){throw "Failed: $Name"}
}
function Observe([string]$Name){
    Key F6
    $value=(Ui @('get-value','Grouped.Status')).text | ConvertFrom-Json
    $value | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory "$Name.json")
    return $value
}
function Until([string]$Name,[scriptblock]$Condition){
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    do {$value=Observe $Name; if(& $Condition $value){return $value}; Start-Sleep -Milliseconds 50}while([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $Name"
}
try {
    Ui @('wait-for','Grouped.Item.0','-t','5000') | Out-Null
    Ui @('focus','Grouped.Item.0') | Out-Null
    $start=Until '01-start' {param($v) $Mode -eq 'grouped' -or $v.groups[0].CompletedLoads -gt 0}
    Check 'expected native data source mode' ($start.mode -eq $Mode)
    if($Mode -eq 'adapted'){Check 'grouped range adapter passes its native contract checks' ($start.contractChecks -eq 5)}
    Check 'native grid uses one scroll surface and bounded controls' ($start.scrollers -eq 1 -and $start.realized -gt 0 -and $start.realized -lt 100)
    Check 'logical count remains independent of realization' ($start.count -eq $(if($Mode -eq 'flat'){10000}else{30000}))
    Check 'source range capability matches expected integration' ($start.directSourceSupportsRanges -eq ($Mode -ne 'grouped'))
    Check 'native grouping avoids full enumeration' ($start.groupEnumerationCalls -eq 0 -and ($start.groups | Measure-Object EnumerationCalls -Sum).Sum -eq 0)
    Key F5
    $deep=Until '02-deep' {param($v) $v.offset -gt 100000}
    Key F8
    $target=if($Mode -eq 'flat'){6000}else{16000}
    $focused=Until '03-focused' {param($v) $v.focus -eq $target}
    Check 'deep native container accepts logical item focus' ($focused.focusResult -eq 'True')
    if($Mode -eq 'grouped'){
        Check 'unadapted native grouping does not forward inner range demand' (($focused.groups | Measure-Object RangeNotifications -Sum).Sum -eq 0 -and ($focused.groups | Measure-Object CompletedLoads -Sum).Sum -eq 0)
    }else{
        $group=0
        $loaded=Until '04-loaded' {param($v) $v.groups[$group].CompletedLoads -gt 0}
        $previousLoads=$loaded.groups[$group].CompletedLoads
        Key F9
        $refreshed=Until '05-refreshed' {param($v) $v.groups[$group].CompletedLoads -gt $previousLoads}
        Check 'content refresh preserves native focus and scroll offset' ($refreshed.focus -eq $target -and [Math]::Abs($refreshed.offset-$loaded.offset) -lt 1)
        Key F7
        Ui @('wait-for','Grouped.Item.0','-t','5000') | Out-Null
        Ui @('focus','Grouped.Item.0') | Out-Null
        $returned=Until '06-returned' {param($v) $v.offset -lt 100 -and $v.focus -eq 0 -and $v.groups[0].ResidentSlots -le 128}
        Check 'reversal keeps data retention bounded and logical positions unchanged' ($returned.count -eq $start.count -and ($returned.groups | Measure-Object ResidentSlots -Sum).Sum -le 128)
        Check 'deep traversal and refresh never enumerate inner collections' ($returned.groupEnumerationCalls -eq 0 -and ($returned.groups | Measure-Object EnumerationCalls -Sum).Sum -eq 0)
    }
    Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'native-grouped.png')) | Out-Null
}finally{
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
    Ui @('invoke','Shell.Close') | Out-Null
}
"Grouped collection checks passed: $($results.Count) ($Mode)."
