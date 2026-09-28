[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [string]$OutputDirectory = 'artifacts/winui-gamepad-boundary',
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
$resultPath = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/gamepad-boundary-result.json'
$deadline = [DateTime]::UtcNow.AddSeconds(45)
$result = $null
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) {
        try {
            $candidate = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            if ($candidate.pid -eq $AppPid) { $result = $candidate; break }
        } catch [System.ArgumentException] { }
    }
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) { throw 'Native boundary fixture exited without its result.' }
    Start-Sleep -Milliseconds 200
}
if ($null -eq $result) { throw 'Timed out waiting for --validate-gamepad-boundary results for this process.' }
$destination = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath $resultPath -Destination (Join-Path $destination 'result.json')
try {
    & winapp ui screenshot -a $AppPid --capture-screen --output (Join-Path $destination 'native.png') | Out-Null
    if (-not $result.passed) { throw $result.error }
    Write-Output ("Passed {0} native checks. {1}" -f $result.checks.Count, $result.evidence)
} finally {
    if ($CloseAfter) { & winapp ui invoke Shell.Close -a $AppPid | Out-Null }
}
