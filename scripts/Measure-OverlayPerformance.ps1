[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Hidden', 'Visible')]
    [string[]]$Scenario = @('Hidden'),

    [string]$OverlayPath,

    [ValidateRange(0, 300)]
    [int]$WarmupSeconds = 3,

    [ValidateRange(2, 3600)]
    [int]$SampleSeconds = 15,

    [ValidateRange(500, 10000)]
    [int]$SampleIntervalMilliseconds = 1000,

    [ValidateRange(1, 60)]
    [int]$StartupTimeoutSeconds = 10,

    [string]$OutputDirectory,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:SchemaVersion = 1
$script:HarnessVersion = '1.0.0'

function Get-NearestRankPercentile {
    param(
        [Parameter(Mandatory)]
        [double[]]$Values,
        [Parameter(Mandatory)]
        [ValidateRange(0.01, 1.0)]
        [double]$Percentile
    )

    if ($Values.Count -eq 0) { return $null }
    $ordered = @($Values | Sort-Object)
    $rank = [Math]::Max(1, [Math]::Ceiling($Percentile * $ordered.Count))
    return [double]$ordered[$rank - 1]
}

function Get-ProcessRole {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [Parameter(Mandatory)][int]$RootProcessId,
        [Parameter(Mandatory)][string]$Name
    )

    if ($ProcessId -eq $RootProcessId) { return 'host' }
    if ($Name -ieq 'WidgetBridge' -or $Name -ieq 'WidgetBridge.exe') { return 'bridge' }
    if ($Name -imatch '(WidgetWorkerHost|Widget.*Worker|\.Worker)(\.exe)?$') { return 'worker' }
    return 'other-child'
}

function Test-ProcessIdentity {
    param(
        [Parameter(Mandatory)][datetime]$ExpectedStartUtc,
        [Parameter(Mandatory)][datetime]$ActualStartUtc
    )

    return $ExpectedStartUtc.Ticks -eq $ActualStartUtc.Ticks
}

function Get-DescendantRows {
    param(
        [Parameter(Mandatory)][int]$RootProcessId,
        [Parameter(Mandatory)][object[]]$Rows
    )

    $childrenByParent = @{}
    foreach ($row in $Rows) {
        $parent = [int]$row.ParentProcessId
        if (-not $childrenByParent.ContainsKey($parent)) {
            $childrenByParent[$parent] = [System.Collections.Generic.List[object]]::new()
        }
        $childrenByParent[$parent].Add($row)
    }

    $result = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $queue = [System.Collections.Generic.Queue[int]]::new()
    $queue.Enqueue($RootProcessId)
    $null = $seen.Add($RootProcessId)

    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        if (-not $childrenByParent.ContainsKey($parent)) { continue }
        foreach ($child in $childrenByParent[$parent]) {
            $childId = [int]$child.ProcessId
            if ($seen.Add($childId)) {
                $result.Add($child)
                $queue.Enqueue($childId)
            }
        }
    }
    return @($result)
}

function Get-ProcessTreeSnapshot {
    param([Parameter(Mandatory)][int]$RootProcessId)

    $rows = @(Get-CimInstance Win32_Process |
        Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate)
    $root = $rows | Where-Object { [int]$_.ProcessId -eq $RootProcessId } | Select-Object -First 1
    if ($null -eq $root) { return @() }
    return @($root) + @(Get-DescendantRows -RootProcessId $RootProcessId -Rows $rows)
}

function Test-VisibleWindowForProcess {
    param([Parameter(Mandatory)][int[]]$ProcessIds)

    if (-not ('OverlayPerformance.NativeWindows' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace OverlayPerformance
{
    public static class NativeWindows
    {
        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public static bool HasVisibleTopLevelWindow(int[] processIds)
        {
            var wanted = new System.Collections.Generic.HashSet<int>(processIds);
            var found = false;
            EnumWindows((window, parameter) =>
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (wanted.Contains((int)processId) && IsWindowVisible(window))
                {
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
'@
    }

    return [OverlayPerformance.NativeWindows]::HasVisibleTopLevelWindow($ProcessIds)
}

function Get-FileState {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        lastWriteUtc = $item.LastWriteTimeUtc.ToString('o')
        lengthBytes = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Test-FileStateChanged {
    param($Before, $After)
    if ($null -eq $After) { return $false }
    if ($null -eq $Before) { return $true }
    return $Before.sha256 -ne $After.sha256 -or
        $Before.lengthBytes -ne $After.lengthBytes -or
        $Before.lastWriteUtc -ne $After.lastWriteUtc
}

function Get-MachineMetadata {
    $operatingSystem = Get-CimInstance Win32_OperatingSystem
    $processor = Get-CimInstance Win32_Processor | Select-Object -First 1
    return [ordered]@{
        operatingSystem = [ordered]@{
            caption = [string]$operatingSystem.Caption
            version = [string]$operatingSystem.Version
            buildNumber = [string]$operatingSystem.BuildNumber
            architecture = [string]$operatingSystem.OSArchitecture
        }
        processor = [ordered]@{
            name = ([string]$processor.Name).Trim()
            physicalCoreCount = [int]$processor.NumberOfCores
            logicalProcessorCount = [int]$processor.NumberOfLogicalProcessors
        }
        totalVisibleMemoryBytes = [long]$operatingSystem.TotalVisibleMemorySize * 1024L
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
}

function Get-BuildMetadata {
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $item = Get-Item -LiteralPath $ExecutablePath
    $packageRoot = $item.Directory.FullName
    $packageBytes = [long](Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Measure-Object -Property Length -Sum).Sum
    $commit = (& git -C $RepositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = $null }
    $dirtyLines = @(& git -C $RepositoryRoot status --short 2>$null)
    if ($LASTEXITCODE -ne 0) { $dirtyLines = @() }

    return [ordered]@{
        configuration = $Configuration
        executablePath = $item.FullName
        executableBytes = [long]$item.Length
        executableLastWriteUtc = $item.LastWriteTimeUtc.ToString('o')
        executableSha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        packageRoot = $packageRoot
        packageBytes = $packageBytes
        gitCommit = if ($commit) { ([string]$commit).Trim() } else { $null }
        gitWorktreeDirty = $dirtyLines.Count -gt 0
    }
}

function Add-TrackedProcesses {
    param(
        [Parameter(Mandatory)][hashtable]$Tracked,
        [Parameter(Mandatory)][object[]]$Rows,
        [Parameter(Mandatory)][datetime]$EarliestStartUtc
    )

    foreach ($row in $Rows) {
        $processId = [int]$row.ProcessId
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            $startUtc = $process.StartTime.ToUniversalTime()
            $process.Dispose()
            if ($startUtc -lt $EarliestStartUtc.AddSeconds(-1)) { continue }
            $key = "$processId|$($startUtc.Ticks)"
            if (-not $Tracked.ContainsKey($key)) {
                $Tracked[$key] = [ordered]@{
                    processId = $processId
                    startTimeUtc = $startUtc
                    executablePath = [string]$row.ExecutablePath
                    name = [string]$row.Name
                }
            }
        }
        catch {
            # Process exit races are expected while sampling.
        }
    }
}

function Get-Sample {
    param(
        [Parameter(Mandatory)][int]$RootProcessId,
        [Parameter(Mandatory)][datetime]$LaunchUtc,
        [Parameter(Mandatory)][double]$ElapsedMilliseconds,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][hashtable]$PreviousCpu,
        [Parameter(Mandatory)][hashtable]$Tracked,
        [Parameter(Mandatory)][int]$LogicalProcessorCount
    )

    $tree = @(Get-ProcessTreeSnapshot -RootProcessId $RootProcessId)
    if ($tree.Count -eq 0) { throw 'OverlayHost exited while it was being measured.' }
    Add-TrackedProcesses -Tracked $Tracked -Rows $tree -EarliestStartUtc $LaunchUtc
    $perfRows = @(Get-CimInstance Win32_PerfRawData_PerfProc_Process |
        Select-Object IDProcess, WorkingSetPrivate, PrivateBytes, WorkingSet, ThreadCount, HandleCount)
    $perfById = @{}
    foreach ($row in $perfRows) { $perfById[[int]$row.IDProcess] = $row }

    $processMetrics = [System.Collections.Generic.List[object]]::new()
    $hostCounterObserved = $false
    $totalPrivateWorkingSet = 0L
    $totalPrivateBytes = 0L
    $totalWorkingSet = 0L
    $totalHandles = 0L
    $totalThreads = 0L
    $totalCpuDelta = 0.0
    $hasCpuDelta = $false

    foreach ($row in $tree) {
        $processId = [int]$row.ProcessId
        if (-not $perfById.ContainsKey($processId)) { continue }
        $perf = $perfById[$processId]
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            $startUtc = $process.StartTime.ToUniversalTime()
            $cpuTotal = $process.TotalProcessorTime.TotalMilliseconds
            $process.Dispose()
        }
        catch { continue }

        $identity = "$processId|$($startUtc.Ticks)"
        $cpuPercent = $null
        if ($PreviousCpu.ContainsKey($identity)) {
            $previous = $PreviousCpu[$identity]
            $elapsed = $ElapsedMilliseconds - [double]$previous.elapsedMilliseconds
            $delta = [Math]::Max(0.0, $cpuTotal - [double]$previous.cpuTotalMilliseconds)
            if ($elapsed -gt 0) {
                $cpuPercent = 100.0 * $delta / $elapsed / [Math]::Max(1, $LogicalProcessorCount)
                $totalCpuDelta += $delta
                $hasCpuDelta = $true
            }
        }
        $PreviousCpu[$identity] = [ordered]@{
            elapsedMilliseconds = $ElapsedMilliseconds
            cpuTotalMilliseconds = $cpuTotal
        }

        $privateWorkingSet = [long]$perf.WorkingSetPrivate
        $privateBytes = [long]$perf.PrivateBytes
        $workingSet = [long]$perf.WorkingSet
        $handles = [long]$perf.HandleCount
        $threads = [long]$perf.ThreadCount
        $totalPrivateWorkingSet += $privateWorkingSet
        $totalPrivateBytes += $privateBytes
        $totalWorkingSet += $workingSet
        $totalHandles += $handles
        $totalThreads += $threads

        $processMetrics.Add([ordered]@{
            processId = $processId
            role = Get-ProcessRole -ProcessId $processId -RootProcessId $RootProcessId -Name ([string]$row.Name)
            name = [IO.Path]::GetFileNameWithoutExtension([string]$row.Name)
            privateWorkingSetBytes = $privateWorkingSet
            privateBytes = $privateBytes
            workingSetBytes = $workingSet
            handleCount = $handles
            threadCount = $threads
            cpuTotalMilliseconds = [Math]::Round($cpuTotal, 3)
            normalizedCpuPercent = if ($null -eq $cpuPercent) { $null } else { [Math]::Round($cpuPercent, 5) }
        })
        if ($processId -eq $RootProcessId) { $hostCounterObserved = $true }
    }

    if (-not $hostCounterObserved) {
        throw 'Windows performance counters did not contain the spawned OverlayHost process.'
    }

    $sampleCpu = $null
    if ($hasCpuDelta -and $PreviousCpu.ContainsKey('__sample')) {
        $sampleElapsed = $ElapsedMilliseconds - [double]$PreviousCpu['__sample']
        if ($sampleElapsed -gt 0) {
            $sampleCpu = 100.0 * $totalCpuDelta / $sampleElapsed / [Math]::Max(1, $LogicalProcessorCount)
        }
    }
    $PreviousCpu['__sample'] = $ElapsedMilliseconds

    return [ordered]@{
        elapsedMilliseconds = [Math]::Round($ElapsedMilliseconds, 3)
        phase = $Phase
        processCount = $processMetrics.Count
        discoveredProcessCount = $tree.Count
        totals = [ordered]@{
            privateWorkingSetBytes = $totalPrivateWorkingSet
            privateBytes = $totalPrivateBytes
            workingSetBytes = $totalWorkingSet
            handleCount = $totalHandles
            threadCount = $totalThreads
            normalizedCpuPercent = if ($null -eq $sampleCpu) { $null } else { [Math]::Round($sampleCpu, 5) }
        }
        processes = @($processMetrics)
    }
}

function Get-MetricSummary {
    param([Parameter(Mandatory)][object[]]$Samples)

    $privateWorkingSet = @($Samples | ForEach-Object { [double]$_.totals.privateWorkingSetBytes })
    $privateBytes = @($Samples | ForEach-Object { [double]$_.totals.privateBytes })
    $workingSet = @($Samples | ForEach-Object { [double]$_.totals.workingSetBytes })
    $handles = @($Samples | ForEach-Object { [double]$_.totals.handleCount })
    $threads = @($Samples | ForEach-Object { [double]$_.totals.threadCount })
    $cpu = @($Samples | ForEach-Object { $_.totals.normalizedCpuPercent } | Where-Object { $null -ne $_ } |
        ForEach-Object { [double]$_ })

    function Describe([double[]]$values, [int]$roundDigits = 3) {
        if ($values.Count -eq 0) { return $null }
        return [ordered]@{
            count = $values.Count
            median = [Math]::Round((Get-NearestRankPercentile -Values $values -Percentile 0.50), $roundDigits)
            p95 = [Math]::Round((Get-NearestRankPercentile -Values $values -Percentile 0.95), $roundDigits)
            maximum = [Math]::Round(($values | Measure-Object -Maximum).Maximum, $roundDigits)
        }
    }

    $roles = [ordered]@{}
    foreach ($role in @('host', 'bridge', 'worker', 'other-child')) {
        $values = @()
        $counts = @()
        foreach ($sample in $Samples) {
            $sum = 0L
            $matching = @($sample.processes | Where-Object { $_.role -eq $role })
            foreach ($processMetric in $matching) {
                $sum += [long]$processMetric.privateWorkingSetBytes
            }
            $values += [double]$sum
            $counts += [double]$matching.Count
        }
        $roles[$role] = [ordered]@{
            processCount = Describe $counts 0
            privateWorkingSetBytes = Describe $values 0
        }
    }

    return [ordered]@{
        sampleCount = $Samples.Count
        total = [ordered]@{
            privateWorkingSetBytes = Describe $privateWorkingSet 0
            privateBytes = Describe $privateBytes 0
            workingSetBytes = Describe $workingSet 0
            normalizedCpuPercent = Describe $cpu 5
            handleCount = Describe $handles 0
            threadCount = Describe $threads 0
        }
        byRole = $roles
    }
}

function Get-TargetComparisons {
    param(
        [Parameter(Mandatory)][string]$ScenarioName,
        [Parameter(Mandatory)]$Summary
    )

    $comparisons = [System.Collections.Generic.List[object]]::new()
    if ($ScenarioName -eq 'Hidden') {
        $cpuSummary = $Summary.total.normalizedCpuPercent
        $cpuP95 = $cpuSummary.p95
        $cpuSampleCount = $cpuSummary.count
        $hostP95 = $Summary.byRole.host.privateWorkingSetBytes.p95
        $comparisons.Add([ordered]@{
            metric = 'hidden.normalizedCpuPercent.p95'
            observed = $cpuP95
            target = 0.1
            unit = 'percent of total machine CPU capacity'
            status = if ($null -eq $cpuP95 -or $cpuSampleCount -lt 20) { 'insufficient-samples' } elseif ($cpuP95 -le 0.1) { 'within-target' } else { 'above-target' }
            kind = 'diagnostic-observation'
            releaseGate = $false
            note = "Nearest-rank p95 comparison requires at least 20 CPU observations; observed $cpuSampleCount."
        })
        $comparisons.Add([ordered]@{
            metric = 'hidden.hostPrivateWorkingSetBytes.p95'
            observed = $hostP95
            target = 50MB
            unit = 'bytes'
            status = if ($null -eq $hostP95) { 'insufficient-samples' } elseif ($hostP95 -le 50MB) { 'within-target' } else { 'above-target' }
            kind = 'diagnostic-observation'
            releaseGate = $false
        })
    }
    if ($ScenarioName -eq 'Visible') {
        $workerP95 = $Summary.byRole.worker.privateWorkingSetBytes.p95
        $workerMaximumCount = $Summary.byRole.worker.processCount.maximum
        $comparisons.Add([ordered]@{
            metric = 'visible.aggregateWorkerPrivateWorkingSetBytes.p95'
            observed = $workerP95
            target = 64MB
            unit = 'bytes'
            status = if ($workerMaximumCount -eq 0) { 'not-observed' } elseif ($workerMaximumCount -ne 1) { 'not-comparable' } elseif ($workerP95 -le 64MB) { 'within-target' } else { 'above-target' }
            kind = 'diagnostic-observation'
            releaseGate = $false
            note = "The 64 MiB target is meaningful only for exactly one worker; maximum observed worker count was $workerMaximumCount."
        })
    }
    return @($comparisons)
}

function Stop-TrackedProcesses {
    param(
        [Parameter(Mandatory)][int]$RootProcessId,
        [Parameter(Mandatory)][datetime]$RootStartUtc,
        [Parameter(Mandatory)][hashtable]$Tracked
    )

    $root = Get-Process -Id $RootProcessId -ErrorAction SilentlyContinue
    if ($null -ne $root) {
        $sameRoot = Test-ProcessIdentity -ExpectedStartUtc $RootStartUtc `
            -ActualStartUtc $root.StartTime.ToUniversalTime()
        if ($sameRoot) {
            try { Stop-Process -Id $RootProcessId -ErrorAction Stop } catch { Write-Warning $_.Exception.Message }
            try { $root.WaitForExit(2000) | Out-Null } catch { }
        }
        $root.Dispose()
    }

    # The host pipe normally makes the bridge and worker processes exit. Only
    # touch descendants whose PID and creation time were observed while rooted
    # beneath the process launched by this invocation.
    Start-Sleep -Milliseconds 500
    foreach ($entry in @($Tracked.Values | Sort-Object { $_.startTimeUtc } -Descending)) {
        if ([int]$entry.processId -eq $RootProcessId) { continue }
        try {
            $process = Get-Process -Id ([int]$entry.processId) -ErrorAction Stop
            $sameInstance = Test-ProcessIdentity -ExpectedStartUtc ([datetime]$entry.startTimeUtc) `
                -ActualStartUtc $process.StartTime.ToUniversalTime()
            if ($sameInstance) {
                Stop-Process -Id ([int]$entry.processId) -ErrorAction Stop
                $process.WaitForExit(2000) | Out-Null
            }
            $process.Dispose()
        }
        catch {
            # Already-exited descendants are the expected cleanup path.
        }
    }
}

function Invoke-ScenarioMeasurement {
    param(
        [Parameter(Mandatory)][string]$ScenarioName,
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][int]$LogicalProcessorCount,
        [Parameter(Mandatory)][string]$StartupErrorPath
    )

    $arguments = if ($ScenarioName -eq 'Visible') { @('--show') } else { @('--hidden') }
    $beforeError = Get-FileState -Path $StartupErrorPath
    $launchUtc = [datetime]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $ExecutablePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $rootStartUtc = $process.StartTime.ToUniversalTime()
    $launchReturnedMilliseconds = $clock.Elapsed.TotalMilliseconds
    $tracked = @{}
    $readyObserved = $null
    $bridgeObserved = $null
    $visibleObserved = $null

    try {
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while ($deadline.Elapsed.TotalSeconds -lt $StartupTimeoutSeconds) {
            $process.Refresh()
            if ($process.HasExited) {
                throw "OverlayHost exited during startup with code $($process.ExitCode)."
            }
            $tree = @(Get-ProcessTreeSnapshot -RootProcessId $process.Id)
            Add-TrackedProcesses -Tracked $tracked -Rows $tree -EarliestStartUtc $launchUtc
            if ($null -eq $bridgeObserved -and ($tree | Where-Object { $_.Name -ieq 'WidgetBridge.exe' })) {
                $bridgeObserved = $clock.Elapsed.TotalMilliseconds
            }
            if ($ScenarioName -eq 'Visible' -and $null -eq $visibleObserved -and $tree.Count -gt 0) {
                $ids = @($tree | ForEach-Object { [int]$_.ProcessId })
                if (Test-VisibleWindowForProcess -ProcessIds $ids) {
                    $visibleObserved = $clock.Elapsed.TotalMilliseconds
                }
            }
            $isReady = $null -ne $bridgeObserved -and
                ($ScenarioName -ne 'Visible' -or $null -ne $visibleObserved)
            if ($isReady) {
                $readyObserved = $clock.Elapsed.TotalMilliseconds
                break
            }
            Start-Sleep -Milliseconds 50
        }

        if ($null -eq $readyObserved) {
            $missing = if ($null -eq $bridgeObserved) { 'WidgetBridge child' } else { 'visible top-level window' }
            throw "Startup observation timed out after $StartupTimeoutSeconds seconds waiting for $missing."
        }

        $afterError = Get-FileState -Path $StartupErrorPath
        if (Test-FileStateChanged -Before $beforeError -After $afterError) {
            $message = Get-Content -LiteralPath $StartupErrorPath -Raw
            throw "OverlayHost wrote a startup error: $message"
        }

        $samples = [System.Collections.Generic.List[object]]::new()
        $previousCpu = @{}
        $measurementStartsAt = $readyObserved + ($WarmupSeconds * 1000.0)
        $endsAt = $measurementStartsAt + ($SampleSeconds * 1000.0)
        $nextSampleAt = $readyObserved
        while ($clock.Elapsed.TotalMilliseconds -lt $endsAt) {
            $remaining = $nextSampleAt - $clock.Elapsed.TotalMilliseconds
            if ($remaining -gt 1) { Start-Sleep -Milliseconds ([int][Math]::Min($remaining, 250)) }
            if ($clock.Elapsed.TotalMilliseconds + 0.5 -lt $nextSampleAt) { continue }
            $elapsed = $clock.Elapsed.TotalMilliseconds
            $phase = if ($elapsed -lt $measurementStartsAt) { 'warmup' } else { 'measurement' }
            $samples.Add((Get-Sample -RootProcessId $process.Id -LaunchUtc $launchUtc `
                -ElapsedMilliseconds $elapsed -Phase $phase -PreviousCpu $previousCpu `
                -Tracked $tracked -LogicalProcessorCount $LogicalProcessorCount))
            $nextSampleAt += $SampleIntervalMilliseconds
            if ($nextSampleAt -lt $clock.Elapsed.TotalMilliseconds) {
                # Never busy-loop to catch up when CIM collection is slower than
                # the requested cadence; preserve a bounded external load.
                $nextSampleAt = $clock.Elapsed.TotalMilliseconds + $SampleIntervalMilliseconds
            }
        }

        $measurementSamples = @($samples | Where-Object { $_.phase -eq 'measurement' })
        if ($measurementSamples.Count -lt 2) {
            throw 'The sampling window produced fewer than two measurement samples.'
        }
        $summary = Get-MetricSummary -Samples $measurementSamples
        $observedNames = @($samples | ForEach-Object { $_.processes } |
            ForEach-Object { $_.name } | Sort-Object -Unique)

        return [ordered]@{
            name = $ScenarioName.ToLowerInvariant()
            arguments = $arguments
            startedUtc = $launchUtc.ToString('o')
            startupObservations = [ordered]@{
                startProcessReturnedMilliseconds = [Math]::Round($launchReturnedMilliseconds, 3)
                bridgeChildObservedMilliseconds = [Math]::Round($bridgeObserved, 3)
                visibleWindowObservedMilliseconds = if ($null -eq $visibleObserved) { $null } else { [Math]::Round($visibleObserved, 3) }
                readyProxyObservedMilliseconds = [Math]::Round($readyObserved, 3)
                readyProxyDefinition = if ($ScenarioName -eq 'Visible') {
                    'Host resident, WidgetBridge child observed, and a visible top-level process window observed.'
                } else {
                    'Host resident and WidgetBridge child observed.'
                }
            }
            sampling = [ordered]@{
                warmupSeconds = $WarmupSeconds
                requestedMeasurementSeconds = $SampleSeconds
                intervalMilliseconds = $SampleIntervalMilliseconds
                observedProcessNames = $observedNames
            }
            summary = $summary
            targetComparisons = @(Get-TargetComparisons -ScenarioName $ScenarioName -Summary $summary)
            samples = @($samples)
        }
    }
    finally {
        try {
            $finalTree = @(Get-ProcessTreeSnapshot -RootProcessId $process.Id)
            Add-TrackedProcesses -Tracked $tracked -Rows $finalTree -EarliestStartUtc $launchUtc
        }
        catch { }
        Stop-TrackedProcesses -RootProcessId $process.Id -RootStartUtc $rootStartUtc -Tracked $tracked
        $process.Dispose()
    }
}

function ConvertTo-PerformanceMarkdown {
    param([Parameter(Mandatory)]$Report)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Overlay performance observation')
    $lines.Add('')
    $lines.Add("Generated: $($Report.generatedUtc)")
    $lines.Add("")
    $lines.Add('> These are bounded local observations, not release guarantees. The sampler does not measure GPU activity, wakeups, frame presentation, Guide latency, controller-to-visual latency, or gameplay impact.')
    $lines.Add('')
    $lines.Add("Build: ``$($Report.build.configuration)`` ``$($Report.build.executableSha256.Substring(0, 12))``; package $([Math]::Round($Report.build.packageBytes / 1MB, 1)) MiB")
    $lines.Add("Machine: $($Report.machine.processor.name), $($Report.machine.processor.logicalProcessorCount) logical processors, $([Math]::Round($Report.machine.totalVisibleMemoryBytes / 1GB, 1)) GiB, $($Report.machine.operatingSystem.caption) build $($Report.machine.operatingSystem.buildNumber)")
    $lines.Add('')
    $lines.Add('| Scenario | Ready proxy | Total private WS p95 | Host private WS p95 | CPU p95 | Peak handles | Processes |')
    $lines.Add('| --- | ---: | ---: | ---: | ---: | ---: | --- |')
    foreach ($scenarioResult in $Report.scenarios) {
        $summary = $scenarioResult.summary
        $processes = [string]::Join(', ', $scenarioResult.sampling.observedProcessNames)
        $lines.Add("| $($scenarioResult.name) | $([Math]::Round($scenarioResult.startupObservations.readyProxyObservedMilliseconds, 1)) ms | $([Math]::Round($summary.total.privateWorkingSetBytes.p95 / 1MB, 1)) MiB | $([Math]::Round($summary.byRole.host.privateWorkingSetBytes.p95 / 1MB, 1)) MiB | $([Math]::Round($summary.total.normalizedCpuPercent.p95, 4))% | $($summary.total.handleCount.maximum) | $processes |")
    }
    $lines.Add('')
    $lines.Add('## Diagnostic target comparisons')
    $lines.Add('')
    foreach ($scenarioResult in $Report.scenarios) {
        $lines.Add("### $($scenarioResult.name)")
        $lines.Add('')
        foreach ($comparison in $scenarioResult.targetComparisons) {
            $lines.Add("- **$($comparison.status)** - ``$($comparison.metric)`` observed ``$($comparison.observed)``; target ``$($comparison.target) $($comparison.unit)``. Observation only; not a release gate.")
        }
        $lines.Add('')
    }
    $lines.Add('## Metric definitions and limits')
    $lines.Add('')
    $lines.Add('- Private working set, private bytes, working set, handles, and threads come from Windows per-process performance counters for the spawned host and descendants observed through parent-process relationships.')
    $lines.Add('- CPU is the target process-tree CPU-time delta divided by wall time and logical processor count. Sampling starts after the readiness proxy and configured warmup.')
    $lines.Add('- The visible readiness proxy confirms a visible top-level window, not a Direct2D-presented first frame or controller interactivity.')
    $lines.Add('- WMI/CIM collection creates external measurement load and can perturb a machine. Use ETW/PresentMon and repeated controlled runs for release evidence.')
    $lines.Add('- Only the spawned host and descendants whose PID plus creation time were observed are stopped during cleanup.')
    return $lines -join [Environment]::NewLine
}

function Write-AtomicText {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )
    $directory = Split-Path -Parent $Path
    $null = New-Item -ItemType Directory -Path $directory -Force
    $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Invoke-SelfTest {
    function Assert-Equal($Expected, $Actual, [string]$Message) {
        if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
    }

    Assert-Equal 2 (Get-NearestRankPercentile -Values @(1, 2, 3, 4) -Percentile 0.50) 'Nearest-rank median failed.'
    Assert-Equal 4 (Get-NearestRankPercentile -Values @(1, 2, 3, 4) -Percentile 0.95) 'Nearest-rank p95 failed.'
    Assert-Equal 'host' (Get-ProcessRole -ProcessId 4 -RootProcessId 4 -Name 'anything') 'Host role failed.'
    Assert-Equal 'bridge' (Get-ProcessRole -ProcessId 5 -RootProcessId 4 -Name 'WidgetBridge.exe') 'Bridge role failed.'
    Assert-Equal 'worker' (Get-ProcessRole -ProcessId 6 -RootProcessId 4 -Name 'WidgetWorkerHost.exe') 'Worker role failed.'
    $identityStart = [datetime]::SpecifyKind([datetime]'2026-01-02T03:04:05.1234567', [DateTimeKind]::Utc)
    Assert-Equal $true (Test-ProcessIdentity -ExpectedStartUtc $identityStart -ActualStartUtc $identityStart) 'Exact process identity failed.'
    Assert-Equal $false (Test-ProcessIdentity -ExpectedStartUtc $identityStart -ActualStartUtc $identityStart.AddTicks(1)) 'PID reuse identity guard failed.'
    $rows = @(
        [pscustomobject]@{ ProcessId = 11; ParentProcessId = 10 },
        [pscustomobject]@{ ProcessId = 12; ParentProcessId = 11 },
        [pscustomobject]@{ ProcessId = 20; ParentProcessId = 99 }
    )
    $descendants = @(Get-DescendantRows -RootProcessId 10 -Rows $rows)
    Assert-Equal 2 $descendants.Count 'Descendant traversal included an unrelated process or missed a nested child.'
    Assert-Equal 12 ([int]$descendants[1].ProcessId) 'Nested descendant order failed.'

    $syntheticSamples = @(
        [pscustomobject]@{ totals = [pscustomobject]@{ privateWorkingSetBytes=10; privateBytes=20; workingSetBytes=30; handleCount=2; threadCount=1; normalizedCpuPercent=$null }; processes=@([pscustomobject]@{ role='host'; privateWorkingSetBytes=10 }) },
        [pscustomobject]@{ totals = [pscustomobject]@{ privateWorkingSetBytes=30; privateBytes=40; workingSetBytes=50; handleCount=4; threadCount=2; normalizedCpuPercent=0.05 }; processes=@([pscustomobject]@{ role='host'; privateWorkingSetBytes=30 }) }
    )
    $summary = Get-MetricSummary -Samples $syntheticSamples
    Assert-Equal 30 $summary.total.privateWorkingSetBytes.p95 'Metric summary p95 failed.'
    Assert-Equal 30 $summary.byRole.host.privateWorkingSetBytes.p95 'Role summary failed.'
    $comparison = @(Get-TargetComparisons -ScenarioName Hidden -Summary $summary)[0]
    Assert-Equal $false $comparison.releaseGate 'Diagnostic thresholds must never become release gates.'
    Assert-Equal 'insufficient-samples' $comparison.status 'Short p95 samples must not produce a target verdict.'
    Write-Host 'Measure-OverlayPerformance self-tests passed (11 assertions).'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OverlayPath)) {
    $OverlayPath = Join-Path $repositoryRoot "src\OverlayHost\out\$Configuration\OverlayHost.exe"
}
$OverlayPath = [IO.Path]::GetFullPath($OverlayPath)
if (-not (Test-Path -LiteralPath $OverlayPath -PathType Leaf)) {
    throw "Packaged overlay was not found at '$OverlayPath'. Build it with .\src\OverlayHost\build.ps1 -Configuration $Configuration."
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\performance'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

# Warm the CIM providers before launch so their one-time startup is not charged
# to the target process. Sampling overhead remains documented in every report.
$machine = Get-MachineMetadata
$null = Get-CimInstance Win32_Process | Select-Object -First 1
$null = Get-CimInstance Win32_PerfRawData_PerfProc_Process | Select-Object -First 1
$build = Get-BuildMetadata -ExecutablePath $OverlayPath -RepositoryRoot $repositoryRoot
$startupError = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GameBarAlternative\startup-error.log'
$results = [System.Collections.Generic.List[object]]::new()

foreach ($scenarioName in @($Scenario | Select-Object -Unique)) {
    Write-Host "Measuring $scenarioName overlay state for $SampleSeconds seconds after a $WarmupSeconds-second warmup..."
    $results.Add((Invoke-ScenarioMeasurement -ScenarioName $scenarioName -ExecutablePath $OverlayPath `
        -LogicalProcessorCount $machine.processor.logicalProcessorCount -StartupErrorPath $startupError))
}

$report = [ordered]@{
    schemaVersion = $script:SchemaVersion
    harnessVersion = $script:HarnessVersion
    generatedUtc = [datetime]::UtcNow.ToString('o')
    evidenceClass = 'bounded-local-observation'
    releaseGate = $false
    percentileMethod = 'nearest-rank'
    build = $build
    machine = $machine
    scenarios = @($results)
    unmeasured = @(
        'GPU utilization and presentation activity',
        'CPU wakeups and context-switch attribution',
        'Guide-to-first-frame and Guide-to-interactive latency',
        'controller-to-visual response latency',
        'frame pacing and game-frame impact',
        'warm activation without synthetic input',
        'long-run memory and handle trends'
    )
}

$stamp = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
$baseName = "overlay-performance-$stamp"
$jsonPath = Join-Path $OutputDirectory "$baseName.json"
$markdownPath = Join-Path $OutputDirectory "$baseName.md"
Write-AtomicText -Path $jsonPath -Content ($report | ConvertTo-Json -Depth 12)
Write-AtomicText -Path $markdownPath -Content (ConvertTo-PerformanceMarkdown -Report $report)
Write-Host "Performance observation written to:"
Write-Host "  $jsonPath"
Write-Host "  $markdownPath"
