[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-indexed'))
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
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
    $data=(Ui @('get-value','Indexed.Status')).text | ConvertFrom-Json
    $data | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$Name.json")
    return $data
}
try {
    Ui @('wait-for','indexed.0','-p','Name','--value','Item 0','-t','5000') | Out-Null
    Ui @('focus','indexed.0') | Out-Null
    $top=Observe '01-top'
    Check 'native range callbacks load actual rows' ($top.callbacks -gt 0 -and $top.loads -gt 0 -and $top.loaded)
    Check 'million logical entries avoid full enumeration and realization' ($top.count -eq 1000000 -and $top.enumerations -eq 0 -and $top.resident -le 128 -and $top.realized -lt 100)
    Key F8
    Key F5
    Ui @('wait-for','indexed.600000','-p','Name','--value','Item 600000','-t','5000') | Out-Null
    Ui @('focus','indexed.600000') | Out-Null
    Key down
    Ui @('wait-for','indexed.600001','-p','HasKeyboardFocus','--value','True','-t','2000') | Out-Null
    Key up
    Ui @('wait-for','indexed.600000','-p','HasKeyboardFocus','--value','True','-t','2000') | Out-Null
    Start-Sleep -Milliseconds 600
    $pending=Observe '02-pending-buffer'
    Check 'direction reversal works while adjacent data is pending' ($pending.focus -eq 600000 -and $pending.loaded -and $pending.y -ge -1 -and $pending.y+$pending.height -le $pending.viewport+1)
    Key F9
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do {
        $admitted=Observe '03-buffer-arrived'
        if($admitted.loads -gt $pending.loads){break}
        Start-Sleep -Milliseconds 50
    } while([DateTime]::UtcNow -lt $deadline)
    Check 'buffer page really arrived' ($admitted.loads -gt $pending.loads)
    Check 'buffer arrival preserves current focus and viewport' ($admitted.focus -eq $pending.focus -and [Math]::Abs($admitted.y-$pending.y) -le 1 -and [Math]::Abs($admitted.offset-$pending.offset) -le 1)
    Key F7
    Ui @('wait-for','indexed.0','-p','Name','--value','Item 0','-t','3000') | Out-Null
    Ui @('focus','indexed.0') | Out-Null
    Start-Sleep -Milliseconds 300
    $returned=Observe '04-returned'
    Check 'return releases distant payload without shrinking logical count' ($returned.count -eq $top.count -and $returned.resident -lt $admitted.resident -and $returned.focus -eq 0)
    Key F5
    Ui @('wait-for','indexed.600000','-p','Name','--value','Item 600000','-t','5000') | Out-Null
    Ui @('focus','indexed.600000') | Out-Null
    Start-Sleep -Milliseconds 400
    $reloaded=Observe '05-reloaded'
    Check 'evicted data reloads with stable logical position' ($reloaded.loads -gt $returned.loads -and $reloaded.focus -eq 600000 -and $reloaded.loaded -and $reloaded.y -ge -1 -and $reloaded.y+$reloaded.height -le $reloaded.viewport+1)
    Check 'deep traversal retains bounded data and containers' ($reloaded.enumerations -eq 0 -and $reloaded.peak -le 160 -and $reloaded.realized -lt 100 -and $reloaded.failures -eq 0)
    Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'indexed.png')) | Out-Null
    Ui @('invoke','Shell.Close') | Out-Null
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Indexed collection checks passed: $($results.Count)."
