[CmdletBinding()]
param(
    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $prototypeRoot 'artifacts\avp002'
$runtimeRoot = Join-Path $artifactRoot 'runtime-win-x64'
$measurementPath = Join-Path $artifactRoot 'measurement.json'
$project = Join-Path $prototypeRoot 'src\AvaloniaOverlayPrototype.csproj'
$sourceCommit = (& git -C $prototypeRoot rev-parse HEAD).Trim()

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$Label
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "$Label could not start." }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Label exceeded the bounded $TimeoutSeconds second timeout."
        }
        if ($process.ExitCode -ne 0) { throw "$Label failed with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}

if ((& git -C $prototypeRoot status --porcelain).Count -ne 0) {
    throw 'Measurement requires a clean exact commit so source/runtime provenance is truthful.'
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Invoke-BoundedProcess -FilePath 'dotnet' -Arguments @(
    'publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
    '--self-contained', 'false', '--output', $runtimeRoot, '--nologo'
) -WorkingDirectory $prototypeRoot -Label 'AVP-002 Release publish'

$executable = Join-Path $runtimeRoot 'AvaloniaOverlayPrototype.exe'
Invoke-BoundedProcess -FilePath $executable -Arguments @(
    '--evidence', $measurementPath, '--source-commit', $sourceCommit
) -WorkingDirectory $runtimeRoot -Label 'AVP-002 prototype evidence lifecycle'

if (-not (Test-Path -LiteralPath $measurementPath)) { throw 'Prototype exited without retaining its measurement artifact.' }
$measurement = Get-Content -Raw -LiteralPath $measurementPath | ConvertFrom-Json
if ($measurement.sourceCommit -ne $sourceCommit) { throw 'Retained measurement provenance does not match the exact source commit.' }
if (-not $measurement.completeFrameDiagnosticsPassed) { throw 'One or more complete-frame diagnostics failed.' }
if (-not $measurement.transitionSurfaceDiagnosticsPassed) { throw 'One or more Avalonia transition-surface diagnostics failed.' }

Write-Host "Retained AVP-002 measurement: $measurementPath"
Write-Host "Copied visible launch: & '$executable'"
