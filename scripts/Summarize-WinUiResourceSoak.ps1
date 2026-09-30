[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [ValidateRange(0,1800)][int]$MinimumDurationSeconds = 0
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($InputDirectory)
$output = Join-Path $directory 'resource-summary.json'
if (Test-Path -LiteralPath $output) { throw 'Resource summaries are immutable; choose a fresh evidence directory.' }
$native = Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw | ConvertFrom-Json
$resources = Get-Content -LiteralPath (Join-Path $directory 'process-tree.json') -Raw | ConvertFrom-Json
$soak = @($native.result.observations | Where-Object { $_.PSObject.Properties.Name -contains 'scenario' -and $_.scenario -eq 'synthetic-switch-soak' })
if ($native.result.passed -ne $true -or $resources.schemaVersion -ne 1 -or $native.pid -ne $resources.rootPid -or $soak.Count -ne 1) {
    throw 'Expected successful native soak and matching owned process-tree evidence.'
}
if ($soak[0].elapsedSeconds -lt $MinimumDurationSeconds) { throw 'Native workload did not reach the required duration.' }
if ($resources.durationSeconds -lt $MinimumDurationSeconds) { throw 'Resource observation did not reach the required duration.' }
$samples = @($resources.samples)
if ($samples.Count -lt 2) { throw 'Insufficient process-tree samples.' }
function Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}
function MiB($Value) { if ($null -eq $Value) { return $null }; return [Math]::Round([double]$Value / 1MB, 2) }
$previous = -1.0
foreach ($sample in $samples) {
    if ($sample.elapsedSeconds -le $previous) { throw 'Resource sample timestamps are not increasing.' }
    $previous = $sample.elapsedSeconds
}
$windows = @($samples | Group-Object { [int][Math]::Floor($_.elapsedSeconds / 300) } | ForEach-Object {
    $group = @($_.Group)
    [ordered]@{
        minuteStart = 5 * [int]$_.Name
        sampleCount = $group.Count
        privateMedianMiB = MiB (Median $group.privateBytes)
        privateMinimumMiB = MiB (($group.privateBytes | Measure-Object -Minimum).Minimum)
        privateMaximumMiB = MiB (($group.privateBytes | Measure-Object -Maximum).Maximum)
        cpuMedianPercentOfOneCore = Median @($group.cpuPercentOfOneCore | Where-Object { $null -ne $_ })
        minimumProcesses = ($group.processCount | Measure-Object -Minimum).Minimum
        maximumProcesses = ($group.processCount | Measure-Object -Maximum).Maximum
    }
})
$firstWarm = @($samples | Where-Object { $_.elapsedSeconds -ge 300 -and $_.elapsedSeconds -lt 600 })
$lastWarm = @($samples | Where-Object { $_.elapsedSeconds -ge [Math]::Max(300, $previous - 300) })
$firstMedian = if ($firstWarm.Count) { Median $firstWarm.privateBytes } else { $null }
$lastMedian = if ($lastWarm.Count) { Median $lastWarm.privateBytes } else { $null }
$actors = @($samples.processes.name | Sort-Object -Unique | ForEach-Object {
    $name = $_
    $actorSamples = @($samples | ForEach-Object {
        $actorsAtSample = @($_.processes | Where-Object name -eq $name)
        [pscustomobject]@{
            elapsed = $_.elapsedSeconds
            privateBytes = $(if ($actorsAtSample.Count) { ($actorsAtSample | Measure-Object privateBytes -Sum).Sum } else { 0 })
            handles = $(if ($actorsAtSample.Count) { ($actorsAtSample | Measure-Object handles -Sum).Sum } else { 0 })
            count = $actorsAtSample.Count
        }
    })
    [ordered]@{
        name = $name
        peakInstances = ($actorSamples | Measure-Object count -Maximum).Maximum
        firstPrivateMiB = MiB $actorSamples[0].privateBytes
        lastPrivateMiB = MiB $actorSamples[-1].privateBytes
        peakPrivateMiB = MiB (($actorSamples.privateBytes | Measure-Object -Maximum).Maximum)
        firstHandles = $actorSamples[0].handles
        lastHandles = $actorSamples[-1].handles
    }
})
$nativeSamples = @($soak[0].samples)
if (-not $nativeSamples.Count) { throw 'Native retirement/heap samples are missing.' }
$report = [ordered]@{
    rootPid = $resources.rootPid
    workloadSeconds = $soak[0].elapsedSeconds
    completedSwitches = $soak[0].completedCycles
    hideReopenCycles = [int][Math]::Floor($soak[0].completedCycles / 4)
    nativeChecks = $native.result.checks.Count
    resourceSamples = $samples.Count
    sampledSeconds = $resources.durationSeconds
    ending = $resources.ending
    sampledPeakPrivateMiB = MiB $resources.peakSampledPrivateBytes
    firstWarmMedianMiB = MiB $firstMedian
    finalWarmMedianMiB = MiB $lastMedian
    warmMedianChangeMiB = $(if ($null -eq $firstMedian -or $null -eq $lastMedian) { $null } else { MiB ($lastMedian - $firstMedian) })
    maximumObservationMilliseconds = ($samples.observationMilliseconds | Measure-Object -Maximum).Maximum
    fiveMinuteWindows = $windows
    actors = $actors
    nativeMemory = [ordered]@{
        sampleCount = $nativeSamples.Count
        minimumManagedMiB = MiB (($nativeSamples.managedBytes | Measure-Object -Minimum).Minimum)
        maximumManagedMiB = MiB (($nativeSamples.managedBytes | Measure-Object -Maximum).Maximum)
        finalManagedMiB = MiB $nativeSamples[-1].managedBytes
        finalPrivateMiB = MiB $nativeSamples[-1].PrivateMemorySize64
        finalHandles = $nativeSamples[-1].HandleCount
        maximumNativeSurfaces = ($nativeSamples.nativeSurfaces | Measure-Object -Maximum).Maximum
        maximumRetainedSurfaces = ($nativeSamples.retained | Measure-Object -Maximum).Maximum
    }
    interpretation = 'Observed synthetic workload only. Native checks establish bounded surface ownership, not a memory budget or leak-free verdict. Compare warmed windows and managed heap samples; GPU memory, media/grids and physical frame pacing are outside scope.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output
[pscustomobject]$report | Select-Object workloadSeconds, completedSwitches, sampledPeakPrivateMiB, firstWarmMedianMiB, finalWarmMedianMiB, warmMedianChangeMiB
