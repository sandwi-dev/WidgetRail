[CmdletBinding()]
param(
    [ValidateRange(60, 600)]
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $prototypeRoot '..\..'))
$artifactRoot = Join-Path $prototypeRoot 'artifacts\avp004'
$runtimeRoot = Join-Path $artifactRoot 'runtime-win-x64'
$measurementPath = Join-Path $artifactRoot 'measurement.json'
$focusedProofPath = Join-Path $artifactRoot 'focused-verification.json'
$project = Join-Path $prototypeRoot 'src\AvaloniaOverlayPrototype.csproj'
$productOutput = Join-Path $repositoryRoot 'src\OverlayHost\out\Release'
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$Label
    )
    Write-Host "[$Label] $FilePath $($Arguments -join ' ')"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
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

if ((& git -C $repositoryRoot status --porcelain).Count -ne 0) {
    throw 'Measurement requires a clean exact commit.'
}
$focusedProof = Get-Content -Raw -LiteralPath $focusedProofPath | ConvertFrom-Json
if (-not $focusedProof.focusedSuitePassed -or $focusedProof.sourceCommit -ne $sourceCommit) {
    throw 'Focused generic mapping proof is missing or stale for the exact measurement commit.'
}

$resolvedArtifact = [System.IO.Path]::GetFullPath($artifactRoot)
$resolvedRuntime = [System.IO.Path]::GetFullPath($runtimeRoot)
if (-not $resolvedRuntime.StartsWith($resolvedArtifact + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Runtime output escaped the AVP-004 artifact root.'
}
if (Test-Path -LiteralPath $resolvedRuntime) {
    Remove-Item -LiteralPath $resolvedRuntime -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedRuntime | Out-Null

Invoke-BoundedProcess -FilePath 'powershell' -Arguments @(
    '-NoProfile', '-File', (Join-Path $repositoryRoot 'src\OverlayHost\build.ps1'),
    '-Configuration', 'Release', '-SkipTests') -WorkingDirectory $repositoryRoot `
    -Label 'retained product runtime package'

Invoke-BoundedProcess -FilePath 'dotnet' -Arguments @(
    'publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
    '--self-contained', 'false', '--output', $resolvedRuntime, '--nologo',
    "-p:SourceRevisionId=$sourceCommit") -WorkingDirectory $prototypeRoot `
    -Label 'AVP-004 Release publish'

Copy-Item -LiteralPath (Join-Path $productOutput 'OverlayPlatformInterop.dll') -Destination $resolvedRuntime -Force
Copy-Item -LiteralPath (Join-Path $productOutput 'widget-catalog.json') -Destination $resolvedRuntime -Force
Copy-Item -LiteralPath (Join-Path $productOutput 'runtime') -Destination $resolvedRuntime -Recurse -Force

$executable = Join-Path $resolvedRuntime 'AvaloniaOverlayPrototype.exe'
Invoke-BoundedProcess -FilePath $executable -Arguments @(
    '--installation', $resolvedRuntime,
    '--evidence', $measurementPath,
    '--source-commit', $sourceCommit,
    '--focused-verification-commit', $focusedProof.sourceCommit) -WorkingDirectory $resolvedRuntime `
    -Label 'AVP-004 ordinary catalog/runtime lifecycle'

if (-not (Test-Path -LiteralPath $measurementPath)) {
    throw 'Candidate exited without retaining AVP-004 measurement evidence.'
}
$measurement = Get-Content -Raw -LiteralPath $measurementPath | ConvertFrom-Json
if ($measurement.assignment -ne 'AVP-004-INTEGRATION') { throw 'Retained assignment is not AVP-004-INTEGRATION.' }
if ($measurement.sourceCommit -ne $sourceCommit) { throw 'Measurement source commit is stale.' }
if ($measurement.executableProductVersion -notmatch [Regex]::Escape($sourceCommit)) {
    throw 'Executable ProductVersion does not carry the exact source commit.'
}
if (-not $measurement.allInstalledWidgetsPassed) { throw 'One or more installed widgets failed the generic adapter lifecycle.' }
if (-not $measurement.responsiveEvidencePassed) { throw 'Responsive containment/ScrollViewer evidence failed.' }
if (-not $measurement.transitionSurfaceDiagnosticsPassed) { throw 'Transition start/mid/end surface evidence failed.' }
if (-not $measurement.nodeKindCoverage.retainedFinalVerificationPassed) { throw 'Combined ordinary lifecycle and focused generic node-kind verification failed.' }
if (-not $measurement.resourceOwnership.supersededResourcesReleased) { throw 'Superseded render or artwork resources remained owned at the visible sample.' }
if (-not $measurement.candidatePrivateMemoryUnder500MiB) { throw 'Visible Avalonia candidate exceeded 500 MiB.' }
if ($measurement.pageTransition -ne 'CrossFade') { throw 'The candidate did not retain Avalonia CrossFade.' }

Write-Host "Retained AVP-004 measurement: $measurementPath"
Write-Host "Copied visible launch: & '$executable' --installation '$resolvedRuntime'"
