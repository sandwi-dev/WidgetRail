[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-gridview'))
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Invoke-Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI command failed: $($Arguments -join ' '): $raw"}
    return ($raw | ConvertFrom-Json)
}
function Record-Check([string]$Name,[bool]$Passed) {
    $results.Add(@{name=$Name;passed=$Passed})
}
function Observe([string]$Name) {
    Invoke-Ui @('send-keys','F6','--via','send-input') | Out-Null
    $value=Invoke-Ui @('get-value','GridCollection.Observation')
    $observation=$value.text | ConvertFrom-Json
    $observation | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$Name.json")
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory "$Name.png")) | Out-Null
    return $observation
}
function Mutate([string]$Key,[int]$Revision) {
    Invoke-Ui @('send-keys',$Key,'--via','send-input') | Out-Null
    Invoke-Ui @('wait-for','GridCollection.Status','--value',"revision: $Revision",'--contains','-t','3000') | Out-Null
    # Allow the native layout/scroll animation to settle; never repair application state.
    Start-Sleep -Milliseconds 600
}
try {
    Invoke-Ui @('wait-for','GridCollection.View','-t','5000') | Out-Null
    Invoke-Ui @('focus','item.0') | Out-Null
    $initial=Observe '01-top'
    Record-Check 'initial item focused and fully visible' ($initial.focusKey -eq 'item.0' -and $initial.fullyVisible)
    Record-Check 'initial control realizes fewer than 100 of 1000 records' ($initial.logical -eq 1000 -and $initial.realized -lt 100)
    Mutate 'F7' 2
    $prepended=Observe '02-prepend'
    Record-Check 'prepend preserves focused key and visibility' ($prepended.focusKey -eq 'item.0' -and $prepended.fullyVisible)
    Record-Check 'prepend preserves focused item vertical position' ([Math]::Abs($prepended.y-$initial.y) -le 1)
    # Set up an independent deep-focus case after recording prepend behavior.
    Invoke-Ui @('send-keys','F5','--via','send-input') | Out-Null
    Start-Sleep -Milliseconds 600
    Invoke-Ui @('focus','item.600') | Out-Null
    $deep=Observe '03-deep'
    Record-Check 'deep item focused and fully visible' ($deep.focusKey -eq 'item.600' -and $deep.fullyVisible)
    Record-Check 'deep control still virtualizes' ($deep.realized -lt 100)
    Mutate 'F8' 3
    $appended=Observe '04-append'
    Record-Check 'append preserves focus and visibility' ($appended.focusKey -eq 'item.600' -and $appended.fullyVisible)
    Mutate 'F10' 4
    $updated=Observe '05-update'
    Record-Check 'same-key update preserves focus and visibility' ($updated.focusKey -eq 'item.600' -and $updated.fullyVisible)
    $title=Invoke-Ui @('wait-for','item.600','-p','Name','--value','Updated item.600','-t','3000')
    Record-Check 'same-key update publishes new title' $title.found
    Mutate 'F9' 5
    $evicted=Observe '06-eviction'
    Record-Check 'eviction before focus preserves key and visibility' ($evicted.focusKey -eq 'item.600' -and $evicted.fullyVisible)
    Record-Check 'eviction preserves focused item vertical position' ([Math]::Abs($evicted.y-$updated.y) -le 1)
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
$failed=@($results | Where-Object { -not $_.passed }).Count
Write-Output "GridView checks: $($results.Count-$failed) passed; $failed failed. Inspect recorded screenshots separately."
if($failed -gt 0){exit 1}
