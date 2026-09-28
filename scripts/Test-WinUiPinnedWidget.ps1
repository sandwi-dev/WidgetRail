[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/pinned-presenter/native'))
$ErrorActionPreference='Stop'
$null=New-Item -ItemType Directory -Force $OutputDirectory
$deadline=[DateTime]::UtcNow.AddSeconds(65)
$result=$null
try {
    $ready=& winapp ui wait-for PinnedWidget.Status -a $AppPid -t 8000 --json | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or -not $ready.found){throw 'Pinned fixture did not become ready.'}
    do {
        $raw=& winapp ui get-value PinnedWidget.Status -a $AppPid --json
        if($LASTEXITCODE -ne 0){throw 'Could not read pinned fixture result.'}
        $value=($raw -join "`n") | ConvertFrom-Json
        if($value.text -eq 'Connecting'){ Start-Sleep -Milliseconds 150; continue }
        $result=$value.text | ConvertFrom-Json
        if($result.completed -ne 0 -or $result.failure){break}
        Start-Sleep -Milliseconds 150
    } while([DateTime]::UtcNow -lt $deadline)
    $result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'result.json')
    & winapp ui screenshot -a $AppPid --capture-screen -o (Join-Path $OutputDirectory 'pinned.png') --json | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not capture the pinned native fixture.'}
    if($result.completed -ne 1 -or $result.failure -or $result.checks -lt 18 -or $result.ordinaryActions -ne 0){
        throw "Pinned fixture failed: $($result | ConvertTo-Json -Compress)"
    }
    Write-Output "PASS pinned native fixture: $($result.checks) checks"
} catch {
    if($result){$result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'result.json')}
    throw
}
