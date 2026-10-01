[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot '../artifacts/winui-indexed/activation'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
& winapp ui wait-for Widget.items.Item.0 -a $AppPid -t 5000 --json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Indexed worker did not publish its initial native row.' }
& winapp ui invoke IndexedWidget.ActivationProbe -a $AppPid --json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not start activation probe.' }
$deadline=[DateTime]::UtcNow.AddSeconds(75)
do {
    $reply=(& winapp ui get-value IndexedWidget.Status -a $AppPid --json) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read activation probe.' }
    $state=$reply.text | ConvertFrom-Json
    if ($state.activationResult -notin @('pending','not-run',$null)) { break }
    Start-Sleep -Milliseconds 100
} while ([DateTime]::UtcNow -lt $deadline)
$state | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'result.json')
if ($state.activationResult -notmatch '^passed:([1-9][0-9]*)$' -or [int]$Matches[1] -lt 31) { throw "Activation probe failed: $($state.activationResult)" }
"Indexed activation checks passed: $($Matches[1])."
