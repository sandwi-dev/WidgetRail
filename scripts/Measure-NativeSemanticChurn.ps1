[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(3, 10)]
    [int]$SampleCount = 5,

    [ValidateRange(5, 60)]
    [int]$SampleTimeoutSeconds = 15,

    [string]$OutputDirectory,

    [switch]$SkipBuild,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Contract = 'dlv016-native-semantic-churn-v1'
$script:ControllerResponseBudgetMilliseconds = 50.0
$script:ProtocolSnapshotLimitBytes = 1024 * 1024
$script:MaterialIdleCpuPercent = 5.0
$script:MaterialPrivateWorkingSetBytes = 128 * 1024 * 1024

function Get-NearestRankSummary {
    param([Parameter(Mandatory)][double[]]$Values)

    if ($Values.Count -eq 0) { throw 'A metric summary requires at least one value.' }
    $ordered = @($Values | Sort-Object)
    $middle = [Math]::Max(0, [Math]::Ceiling($ordered.Count * 0.5) - 1)
    [ordered]@{
        minimum = [double]$ordered[0]
        median = [double]$ordered[$middle]
        maximum = [double]$ordered[-1]
        range = [double]$ordered[-1] - [double]$ordered[0]
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected' but observed '$Actual'."
    }
}

function Invoke-SelfTest {
    $summary = Get-NearestRankSummary -Values @(3.0, 1.0, 5.0, 2.0, 4.0)
    Assert-Equal 1.0 $summary.minimum 'Minimum is deterministic.'
    Assert-Equal 3.0 $summary.median 'Nearest-rank median is deterministic.'
    Assert-Equal 5.0 $summary.maximum 'Maximum is deterministic.'
    Assert-Equal 4.0 $summary.range 'Range is deterministic.'
    Assert-Equal $false ($summary.maximum -le 4.9) 'Material threshold failure is retained.'
    Write-Host 'Measure-NativeSemanticChurn self-test passed (5 checks).'
}

function Invoke-BoundedNativeSample {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$EvidenceFile,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('--evidence-file')
    $startInfo.ArgumentList.Add($EvidenceFile)
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'The native semantic-churn sample did not start.' }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "The native semantic-churn sample exceeded $TimeoutSeconds seconds."
        }
        $standardOutput = $process.StandardOutput.ReadToEnd().Trim()
        $standardError = $process.StandardError.ReadToEnd().Trim()
        if ($process.ExitCode -ne 0) {
            throw "The native semantic-churn sample failed with exit code $($process.ExitCode): $standardError"
        }
        if ($standardOutput) { Write-Host $standardOutput }
    } finally {
        $process.Dispose()
    }
}

function Write-AtomicUtf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $temporary = "$Path.tmp"
    if ((Test-Path -LiteralPath $Path) -or (Test-Path -LiteralPath $temporary)) {
        throw "Evidence publication requires new paths: $Path"
    }
    [System.IO.File]::WriteAllText(
        $temporary, $Content, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporary, $Path)
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nativeBuildScript = Join-Path $repositoryRoot 'src\OverlayHost\build.ps1'
$nativeExecutable = Join-Path $repositoryRoot "src\OverlayHost\out\$Configuration\SemanticChurnPerformanceTests.exe"
if (-not $SkipBuild) {
    & $nativeBuildScript -Configuration $Configuration -Architecture x64 -SemanticChurnTestsOnly
    if ($LASTEXITCODE -ne 0) {
        throw "The focused semantic-churn build failed with exit code $LASTEXITCODE."
    }
}
if (-not (Test-Path -LiteralPath $nativeExecutable -PathType Leaf)) {
    throw "The focused native executable is missing: $nativeExecutable"
}

if (-not $OutputDirectory) {
    $runId = 'dlv016-native-{0}-{1}' -f `
        ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')), `
        ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\performance\$runId"
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "The evidence directory already exists: $resolvedOutput"
}
$null = New-Item -ItemType Directory -Path $resolvedOutput

$samples = [System.Collections.Generic.List[object]]::new()
for ($index = 1; $index -le $SampleCount; $index++) {
    $samplePath = Join-Path $resolvedOutput ("sample-{0}.json" -f $index)
    Invoke-BoundedNativeSample `
        -Executable $nativeExecutable `
        -EvidenceFile $samplePath `
        -TimeoutSeconds $SampleTimeoutSeconds
    $sample = Get-Content -LiteralPath $samplePath -Raw | ConvertFrom-Json -Depth 20
    if ($sample.contract -ne $script:Contract -or $sample.gate.status -ne 'pass') {
        throw "Sample $index did not publish the accepted DLV-016 contract and pass state."
    }
    if ($sample.configuration -ne $Configuration -or
        [double]$sample.thresholds.controllerResponseP95Milliseconds -ne
            $script:ControllerResponseBudgetMilliseconds -or
        [long]$sample.thresholds.protocolSnapshotBytes -ne
            $script:ProtocolSnapshotLimitBytes -or
        [double]$sample.thresholds.materialIdleCpuPercent -ne
            $script:MaterialIdleCpuPercent -or
        [long]$sample.thresholds.materialPrivateWorkingSetBytes -ne
            $script:MaterialPrivateWorkingSetBytes) {
        throw "Sample $index configuration or native material thresholds drifted from the repeat runner."
    }
    $samples.Add($sample)
}

$first = $samples[0]
foreach ($sample in $samples) {
    foreach ($property in @(
        'snapshotNodeCount',
        'semanticNodeCount',
        'updateCount',
        'totalProjectedSemanticNodes',
        'totalChangedSemanticNodes',
        'canonicalSnapshotBytes'
    )) {
        if ($sample.workload.$property -ne $first.workload.$property) {
            throw "Repeated samples changed the stable workload field '$property'."
        }
    }
}

$projectionP95 = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.latencyMilliseconds.inputToProjectionP95 })
$hiddenCpu = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.hidden.normalizedCpuPercent })
$visibleIdleCpu = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.visibleIdle.normalizedCpuPercent })
$hiddenPrivateWorkingSet = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.hidden.privateWorkingSetAfterBytes })
$visiblePrivateWorkingSet = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.visibleIdle.privateWorkingSetAfterBytes })
$inputPrivateWorkingSet = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.inputUpdate.privateWorkingSetAfterBytes })
$qpcOverhead = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.latencyMilliseconds.qpcPairP50 })
$phaseOverheadCpu = Get-NearestRankSummary -Values @(
    $samples | ForEach-Object { [double]$_.phases.measurementOverhead.cpuMilliseconds })
$privateWorkingSetMaximum = [double](@(
    $hiddenPrivateWorkingSet.maximum,
    $visiblePrivateWorkingSet.maximum,
    $inputPrivateWorkingSet.maximum
) | Measure-Object -Maximum).Maximum

$failures = [System.Collections.Generic.List[string]]::new()
if ($projectionP95.maximum -gt $script:ControllerResponseBudgetMilliseconds) {
    $failures.Add('input-to-projection p95 exceeded the documented 50 ms response budget')
}
if ([double]$first.workload.canonicalSnapshotBytes -gt $script:ProtocolSnapshotLimitBytes) {
    $failures.Add('canonical snapshot bytes exceeded the public 1 MiB frame bound')
}
if ($hiddenCpu.maximum -gt $script:MaterialIdleCpuPercent) {
    $failures.Add('hidden CPU exceeded the 5 percent material-regression ceiling')
}
if ($visibleIdleCpu.maximum -gt $script:MaterialIdleCpuPercent) {
    $failures.Add('visible-idle CPU exceeded the 5 percent material-regression ceiling')
}
if ($privateWorkingSetMaximum -gt $script:MaterialPrivateWorkingSetBytes) {
    $failures.Add('private working set exceeded the 128 MiB material-regression ceiling')
}

$gitHead = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$gitBranch = (& git -C $repositoryRoot branch --show-current).Trim()
$gitChanges = @(& git -C $repositoryRoot status --porcelain)
$processorName = $env:PROCESSOR_IDENTIFIER
try {
    $registryName = (Get-ItemProperty `
        -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' `
        -Name ProcessorNameString -ErrorAction Stop).ProcessorNameString
    if ($registryName) { $processorName = $registryName.Trim() }
} catch {
    # PROCESSOR_IDENTIFIER is a bounded sanitized fallback.
}

$gateStatus = if ($failures.Count -eq 0) { 'pass' } else { 'fail' }
$aggregate = [ordered]@{
    schemaVersion = 1
    contract = 'dlv016-native-semantic-churn-repeat-v1'
    capturedAtUtc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    sampleCount = $SampleCount
    provenance = [ordered]@{
        gitHead = $gitHead
        gitBranch = $gitBranch
        gitDirty = $gitChanges.Count -gt 0
        gitChangedPathCount = $gitChanges.Count
        executableSha256 = (Get-FileHash -LiteralPath $nativeExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        nativeSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'src\OverlayHost\SemanticChurnPerformanceTests.cpp') -Algorithm SHA256).Hash.ToLowerInvariant()
        runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
        operatingSystem = [Environment]::OSVersion.VersionString
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        logicalProcessors = [Environment]::ProcessorCount
        processor = $processorName
        powerShell = $PSVersionTable.PSVersion.ToString()
    }
    workload = $first.workload
    repeatedMetrics = [ordered]@{
        inputToProjectionP95Milliseconds = $projectionP95
        hiddenNormalizedCpuPercent = $hiddenCpu
        visibleIdleNormalizedCpuPercent = $visibleIdleCpu
        hiddenPrivateWorkingSetBytes = $hiddenPrivateWorkingSet
        visibleIdlePrivateWorkingSetBytes = $visiblePrivateWorkingSet
        inputUpdatePrivateWorkingSetBytes = $inputPrivateWorkingSet
        qpcPairP50Milliseconds = $qpcOverhead
        measurementOverheadCpuMilliseconds = $phaseOverheadCpu
    }
    thresholds = [ordered]@{
        controllerResponseP95Milliseconds = $script:ControllerResponseBudgetMilliseconds
        protocolSnapshotBytes = $script:ProtocolSnapshotLimitBytes
        materialIdleCpuPercent = $script:MaterialIdleCpuPercent
        materialPrivateWorkingSetBytes = $script:MaterialPrivateWorkingSetBytes
        maximumChangedSemanticNodesPerUpdate = 4
        hiddenReferenceCpuPercent = 0.1
        hiddenReferenceCpuIsReleaseGate = $false
    }
    gate = [ordered]@{
        status = $gateStatus
        classification = 'material-regression'
        failures = @($failures)
    }
    limitations = @(
        'Null-target native layout excludes GPU, DWM, paint, presentation, and game-frame cost.',
        'Same-process private resident pages exclude the bridge and widget workers.',
        'Idle CPU uses process-time granularity; the 0.1 percent reference target is reported but does not gate this short sample.',
        'The stable synthetic tree is a renderer and UI Automation projection baseline, not a claim about every first-party widget.',
        'ETW, PresentMon, scheduler wakeups, controller hardware, and screenshots are intentionally outside this bounded lane.'
    )
    sanitized = $true
}

$aggregatePath = Join-Path $resolvedOutput 'native-semantic-churn.json'
Write-AtomicUtf8File `
    -Path $aggregatePath `
    -Content ($aggregate | ConvertTo-Json -Depth 20)

function Format-Number([double]$Value, [string]$Format = '0.000') {
    return $Value.ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
}
function Format-Mebibytes([double]$Value) {
    return Format-Number ($Value / 1MB) '0.00'
}

$reportLines = [System.Collections.Generic.List[string]]::new()
$reportLines.Add('# DLV-016 native idle and semantic-churn sample')
$reportLines.Add('')
$reportLines.Add("Status: **$gateStatus** over $SampleCount bounded $Configuration samples.")
$reportLines.Add('')
$reportLines.Add('| Metric | Minimum | Median | Maximum | Material gate |')
$reportLines.Add('| --- | ---: | ---: | ---: | ---: |')
$reportLines.Add("| Input to semantic projection p95 | $(Format-Number $projectionP95.minimum) ms | $(Format-Number $projectionP95.median) ms | $(Format-Number $projectionP95.maximum) ms | 50.000 ms |")
$reportLines.Add("| Hidden normalized CPU | $(Format-Number $hiddenCpu.minimum '0.0000')% | $(Format-Number $hiddenCpu.median '0.0000')% | $(Format-Number $hiddenCpu.maximum '0.0000')% | 5.0000% |")
$reportLines.Add("| Visible-idle normalized CPU | $(Format-Number $visibleIdleCpu.minimum '0.0000')% | $(Format-Number $visibleIdleCpu.median '0.0000')% | $(Format-Number $visibleIdleCpu.maximum '0.0000')% | 5.0000% |")
$reportLines.Add("| Hidden private working set | $(Format-Mebibytes $hiddenPrivateWorkingSet.minimum) MiB | $(Format-Mebibytes $hiddenPrivateWorkingSet.median) MiB | $(Format-Mebibytes $hiddenPrivateWorkingSet.maximum) MiB | 128.00 MiB |")
$reportLines.Add("| Visible-idle private working set | $(Format-Mebibytes $visiblePrivateWorkingSet.minimum) MiB | $(Format-Mebibytes $visiblePrivateWorkingSet.median) MiB | $(Format-Mebibytes $visiblePrivateWorkingSet.maximum) MiB | 128.00 MiB |")
$reportLines.Add("| Input-update private working set | $(Format-Mebibytes $inputPrivateWorkingSet.minimum) MiB | $(Format-Mebibytes $inputPrivateWorkingSet.median) MiB | $(Format-Mebibytes $inputPrivateWorkingSet.maximum) MiB | 128.00 MiB |")
$reportLines.Add('')
$reportLines.Add("The stable workload contains $($first.workload.snapshotNodeCount) snapshot nodes, $($first.workload.semanticNodeCount) projected semantic nodes, $($first.workload.updateCount) input updates, $($first.workload.totalProjectedSemanticNodes) total projected nodes, $($first.workload.totalChangedSemanticNodes) changed semantic nodes, and $($first.workload.canonicalSnapshotBytes) canonical snapshot bytes.")
$reportLines.Add('')
$reportLines.Add("Harness overhead is explicit: QPC-pair p50 ranged from $(Format-Number $qpcOverhead.minimum '0.000000') to $(Format-Number $qpcOverhead.maximum '0.000000') ms and the empty phase consumed at most $(Format-Number $phaseOverheadCpu.maximum '0.000') ms of process CPU.")
$reportLines.Add('')
$reportLines.Add("Provenance: commit $gitHead on $gitBranch; dirty=$($gitChanges.Count -gt 0); executable SHA-256 $($aggregate.provenance.executableSha256); $processorName; $([Environment]::OSVersion.VersionString).")
$reportLines.Add('')
$reportLines.Add('Limitations:')
$reportLines.Add('')
foreach ($limitation in $aggregate.limitations) { $reportLines.Add("- $limitation") }
$reportPath = Join-Path $resolvedOutput 'native-semantic-churn.md'
Write-AtomicUtf8File -Path $reportPath -Content ($reportLines -join [Environment]::NewLine)

Write-Host "DLV-016 repeated native evidence: $resolvedOutput"
Write-Host "Gate: $gateStatus; input-to-projection p95 max $(Format-Number $projectionP95.maximum) ms; visible-idle CPU max $(Format-Number $visibleIdleCpu.maximum '0.0000')%."
if ($failures.Count -gt 0) {
    throw "DLV-016 material-regression gate failed: $($failures -join '; ')"
}
