[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-clock'))
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
try {
    Assert-Result 'real worker first snapshot' (Invoke-Ui @('wait-for','Bridge.Status','--value','Ready: Clock;','--contains','-t','15000')).found
    $initial=Invoke-Ui @('get-property','Widget.refresh','--property','HasKeyboardFocus')
    Assert-Result 'initial focus without mouse or keyboard input' ($initial.properties.HasKeyboardFocus -eq 'True')
    $initialSelector=$initial.element.selector
    $text=(Invoke-Ui @('get-value','Bridge.Status')).text
    if($text -notmatch 'snapshot (\d+); publications (\d+); bridge (\d+)'){throw 'Invalid fixture status'}
    $sequence=[long]$Matches[1]; $publications=[int]$Matches[2]; $bridgePid=[int]$Matches[3]
    $workers=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$bridgePid" | Where-Object Name -eq 'WidgetWorkerHost.exe' | Select-Object -ExpandProperty ProcessId)
    Assert-Result 'actual generic worker process' ($workers.Count -eq 1)
    foreach($iteration in 1..3) {
        Invoke-Ui @('invoke','Widget.refresh') | Out-Null
        ++$sequence; ++$publications
        Assert-Result "action $iteration produces one updated snapshot" (Invoke-Ui @('wait-for','Bridge.Status','--value',"snapshot $sequence; publications $publications;",'--contains','-t','5000')).found
        $after=Invoke-Ui @('get-property','Widget.refresh','--property','HasKeyboardFocus')
        Assert-Result "update $iteration retains focused control" ($after.properties.HasKeyboardFocus -eq 'True' -and $after.element.selector -eq $initialSelector)
    }
    # Escape has no action in this fixture. Wake desktop presentation before pixel
    # capture; this input is deliberately after the cold-focus assertion above.
    Invoke-Ui @('send-keys','Escape','--via','send-input') | Out-Null
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'clock-verified.png')) | Out-Null
    Invoke-Ui @('invoke','Shell.Close') | Out-Null
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $remaining=@(Get-Process -Id (@($AppPid,$bridgePid)+$workers) -ErrorAction SilentlyContinue)
        if($remaining.Count -eq 0){break}
        Start-Sleep -Milliseconds 50
    } while([DateTime]::UtcNow -lt $deadline)
    Assert-Result 'frontend bridge and worker exit on close' ($remaining.Count -eq 0)
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Bridge widget checks passed: $($results.Count). Inspect captured pixels separately; physical controller behavior remains unverified."
