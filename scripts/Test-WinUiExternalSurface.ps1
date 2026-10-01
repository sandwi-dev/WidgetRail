[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-media'))
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
    $ready=Invoke-Ui @('wait-for','Media.Status','--value','External surface ready','-t','10000')
    Assert-Result 'external content navigation completed' $ready.found
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'surface.png')) | Out-Null
    Invoke-Ui @('click','Media.OpenDialog') | Out-Null
    $dialog=Invoke-Ui @('wait-for','Media.Dialog','-t','3000')
    Assert-Result 'pointer command over external content opens WinUI dialog' $dialog.found
    Invoke-Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'dialog.png')) | Out-Null
    Invoke-Ui @('send-keys','Escape','--via','send-input') | Out-Null
    $until=[DateTime]::UtcNow.AddSeconds(3)
    do {
        $focus=Invoke-Ui @('get-focused')
        if($focus.element.automationId -eq 'Media.OpenDialog'){break}
        Start-Sleep -Milliseconds 50
    } while([DateTime]::UtcNow -lt $until)
    Assert-Result 'dismissal restores the invoking control focus' ($focus.element.automationId -eq 'Media.OpenDialog')
    Invoke-Ui @('click','Media.OpenDialog') | Out-Null
    Assert-Result 'dialog can reopen over the live surface' (Invoke-Ui @('wait-for','Media.Dialog','-t','3000')).found
    Invoke-Ui @('send-keys','Escape','--via','send-input') | Out-Null
} finally {
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
Write-Output "External surface checks passed: $($results.Count). Inspect captured pixels separately."
