[CmdletBinding()]
param(
    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $prototypeRoot '..\..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $prototypeRoot 'artifacts\avp004-phase-profile'))
$runtimeRoot = Join-Path $artifactRoot 'runtime-win-x64'
$measurementPath = Join-Path $artifactRoot 'phase-profile.json'
$tracePath = Join-Path $artifactRoot 'native-gpu-composition.etl'
$project = Join-Path $prototypeRoot 'src\AvaloniaOverlayPrototype.csproj'
$productOutput = Join-Path $repositoryRoot 'src\OverlayHost\out\Release'
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$profileIdentity = if ((& git -C $repositoryRoot status --porcelain).Count -eq 0) {
    $head
} else {
    "working-tree-$head"
}

if (-not $runtimeRoot.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Phase-profile runtime escaped its bounded artifact root.'
}
if (-not (Test-Path -LiteralPath (Join-Path $productOutput 'widget-catalog.json'))) {
    throw 'Build src/OverlayHost Release before the phase probe.'
}
if (Test-Path -LiteralPath $runtimeRoot) {
    Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null

& dotnet publish $project --configuration Release --runtime win-x64 --self-contained false `
    --output $runtimeRoot --nologo "-p:SourceRevisionId=$profileIdentity"
if ($LASTEXITCODE -ne 0) { throw 'Phase-profile publish failed.' }
Copy-Item -LiteralPath (Join-Path $productOutput 'OverlayPlatformInterop.dll') -Destination $runtimeRoot -Force
Copy-Item -LiteralPath (Join-Path $productOutput 'widget-catalog.json') -Destination $runtimeRoot -Force
Copy-Item -LiteralPath (Join-Path $productOutput 'runtime') -Destination $runtimeRoot -Recurse -Force

$traceStarted = $false
try {
    & wpr -start VirtualAllocation -start GPU -start DesktopComposition -filemode
    if ($LASTEXITCODE -eq 0) { $traceStarted = $true }
    else { Write-Warning 'WPR native/GPU/composition trace was unavailable; in-process attribution remains retained.' }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Join-Path $runtimeRoot 'AvaloniaOverlayPrototype.exe'
    $startInfo.WorkingDirectory = $runtimeRoot
    $startInfo.UseShellExecute = $false
    foreach ($argument in @(
        '--installation', $runtimeRoot,
        '--evidence', $measurementPath,
        '--source-commit', $profileIdentity,
        '--focused-verification-commit', $profileIdentity)) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw 'Phase-profile candidate could not start.' }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Phase-profile candidate exceeded $TimeoutSeconds seconds."
        }
        if (-not (Test-Path -LiteralPath $measurementPath)) {
            throw "Phase-profile candidate exited $($process.ExitCode) without evidence."
        }
        Write-Host "Phase-profile candidate exit code: $($process.ExitCode)"
    }
    finally { $process.Dispose() }
}
finally {
    if ($traceStarted) {
        & wpr -stop $tracePath
        if ($LASTEXITCODE -ne 0) { Write-Warning 'WPR trace could not be finalized.' }
    }
}

Write-Host "Retained phase profile: $measurementPath"
if (Test-Path -LiteralPath $tracePath) { Write-Host "Retained WPR trace: $tracePath" }
