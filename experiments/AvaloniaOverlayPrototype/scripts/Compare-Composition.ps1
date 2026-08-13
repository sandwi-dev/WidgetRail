[CmdletBinding()]
param(
    [ValidateRange(3, 20)]
    [int]$Samples = 7,
    [ValidateRange(30, 180)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $prototypeRoot 'tests\fixtures\CompositionComparison\CompositionComparison.csproj'
$artifactRoot = Join-Path $prototypeRoot 'artifacts\avp003\composition-comparison'

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$Label,
        [switch]$CaptureOutput
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $CaptureOutput
    $startInfo.RedirectStandardError = $CaptureOutput
    $startInfo.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "$Label could not start." }
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Label exceeded the bounded $TimeoutSeconds second timeout."
        }
        $timer.Stop()
        $stdout = if ($CaptureOutput) { $process.StandardOutput.ReadToEnd() } else { '' }
        $stderr = if ($CaptureOutput) { $process.StandardError.ReadToEnd() } else { '' }
        if ($process.ExitCode -ne 0) { throw "$Label failed with exit code $($process.ExitCode): $stderr" }
        return [pscustomobject]@{ ElapsedMilliseconds = $timer.Elapsed.TotalMilliseconds; Output = $stdout }
    }
    finally { $process.Dispose() }
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$variants = @(
    [pscustomobject]@{ Name = 'manual'; UseDirectDi = 'false'; Executable = 'CompositionComparison.Manual.exe' },
    [pscustomobject]@{ Name = 'direct-di'; UseDirectDi = 'true'; Executable = 'CompositionComparison.DirectDi.exe' }
)
$results = @()
foreach ($variant in $variants) {
    $output = Join-Path $artifactRoot $variant.Name
    Invoke-BoundedProcess -FilePath 'dotnet' -Arguments @(
        'publish', $project, '--configuration', 'Release', '--output', $output,
        '--nologo', "-p:UseDirectDi=$($variant.UseDirectDi)"
    ) -WorkingDirectory $prototypeRoot -Label "$($variant.Name) comparison publish" | Out-Null
    $samplesOutput = @()
    for ($index = 0; $index -lt $Samples; $index++) {
        $sample = Invoke-BoundedProcess -FilePath (Join-Path $output $variant.Executable) -Arguments @() `
            -WorkingDirectory $output -Label "$($variant.Name) sample $($index + 1)" -CaptureOutput
        $reported = $sample.Output | ConvertFrom-Json
        $samplesOutput += [pscustomobject]@{
            processMilliseconds = $sample.ElapsedMilliseconds
            compositionMilliseconds = $reported.compositionMilliseconds
            privateMemoryMiB = $reported.privateMemoryMiB
        }
    }
    $orderedProcess = @($samplesOutput.processMilliseconds | Sort-Object)
    $orderedComposition = @($samplesOutput.compositionMilliseconds | Sort-Object)
    $orderedMemory = @($samplesOutput.privateMemoryMiB | Sort-Object)
    $middle = [int][Math]::Floor($Samples / 2)
    $publishedBytes = (Get-ChildItem -LiteralPath $output -File | Measure-Object Length -Sum).Sum
    $results += [pscustomobject]@{
        strategy = $reported.strategy
        sampleCount = $Samples
        medianProcessMilliseconds = $orderedProcess[$middle]
        medianCompositionMilliseconds = $orderedComposition[$middle]
        medianPrivateMemoryMiB = $orderedMemory[$middle]
        publishedBytes = $publishedBytes
        samples = $samplesOutput
    }
}

$artifact = [pscustomobject]@{
    measuredAtUtc = [DateTimeOffset]::UtcNow
    dotNetRuntime = [Environment]::Version.ToString()
    microsoftDependencyInjectionVersion = '10.0.10'
    scope = 'Bounded framework-dependent console microcomparison; not an Avalonia startup benchmark.'
    results = $results
}
$path = Join-Path $artifactRoot 'comparison.json'
[IO.File]::WriteAllText($path, ($artifact | ConvertTo-Json -Depth 6))
$artifact | ConvertTo-Json -Depth 6
