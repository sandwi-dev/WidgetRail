[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RequestPath,
    [Parameter(Mandatory)][string]$StartEventName
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $RequestPath -PathType Leaf)) {
    throw 'Verification command request is missing.'
}
$requestFile = Get-Item -LiteralPath $RequestPath
if ($requestFile.Length -gt 262144) {
    throw 'Verification command request exceeds 256 KiB.'
}

$startEvent = [Threading.EventWaitHandle]::OpenExisting($StartEventName)
try {
    if (-not $startEvent.WaitOne(10000)) {
        throw 'Verification command start authorization timed out.'
    }
}
finally { $startEvent.Dispose() }

$request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($request.file) -or $null -eq $request.arguments) {
    throw 'Verification command request is invalid.'
}
$arguments = @($request.arguments | ForEach-Object { [string]$_ })
& ([string]$request.file) @arguments
$commandExitCode = $LASTEXITCODE
if ($null -eq $commandExitCode) { $commandExitCode = if ($?) { 0 } else { 1 } }
exit $commandExitCode
