[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Hidden', 'Visible', 'Interactive')]
    [string[]]$Scenario = @('Hidden', 'Visible', 'Interactive'),

    [ValidatePattern('^[A-Za-z0-9._-]{1,128}$')]
    [string]$WidgetId = 'settings',

    [string]$OverlayPath,

    [ValidateRange(0, 300)]
    [int]$WarmupSeconds = 3,

    [ValidateRange(2, 3600)]
    [int]$SampleSeconds = 30,

    [ValidateRange(500, 10000)]
    [int]$SampleIntervalMilliseconds = 1000,

    [ValidateRange(1, 60)]
    [int]$StartupTimeoutSeconds = 10,

    [ValidateRange(1, 10)]
    [int]$CimOperationTimeoutSeconds = 3,

    [ValidateRange(1, 15)]
    [int]$GracefulShutdownTimeoutSeconds = 5,

    [string]$OutputDirectory,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:SchemaVersion = 2
$script:HarnessVersion = '2.0.0'
$script:OverlayWindowClass = 'GameBarAlternative.OverlayHost'
$script:PerformanceResetMessage = 0x8008

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

    $rows = @(Get-CimInstance Win32_Process `
        -OperationTimeoutSec $CimOperationTimeoutSeconds |
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, System.Text.StringBuilder value, int maximum);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

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

        public static IntPtr FindTopLevelWindow(int processId, string className, bool requireVisible)
        {
            var found = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if ((int)owner != processId || (requireVisible && !IsWindowVisible(window)))
                    return true;
                var value = new System.Text.StringBuilder(256);
                if (GetClassName(window, value, value.Capacity) > 0 &&
                    string.Equals(value.ToString(), className, StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static bool PostToTopLevelWindow(
            int processId, string className, uint message, UIntPtr wParam)
        {
            var window = FindTopLevelWindow(processId, className, false);
            return window != IntPtr.Zero && PostMessage(window, message, wParam, IntPtr.Zero);
        }
    }
}
'@
    }

    return [OverlayPerformance.NativeWindows]::HasVisibleTopLevelWindow($ProcessIds)
}

function Test-OverlayWindowVisible {
    param([Parameter(Mandatory)][int]$ProcessId)
    if (-not ('OverlayPerformance.NativeWindows' -as [type])) {
        $null = Test-VisibleWindowForProcess -ProcessIds @($ProcessId)
    }
    return [OverlayPerformance.NativeWindows]::FindTopLevelWindow(
        $ProcessId, $script:OverlayWindowClass, $true) -ne [IntPtr]::Zero
}

function Send-OverlayWindowMessage {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [Parameter(Mandatory)][uint32]$Message
    )
    if (-not ('OverlayPerformance.NativeWindows' -as [type])) {
        $null = Test-VisibleWindowForProcess -ProcessIds @($ProcessId)
    }
    return [OverlayPerformance.NativeWindows]::PostToTopLevelWindow(
        $ProcessId, $script:OverlayWindowClass, $Message, [UIntPtr]::Zero)
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

function ConvertFrom-PerformanceRuntimeRecord {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedNonce,
        [Parameter(Mandatory)][string]$ExpectedState,
        [Parameter(Mandatory)][string]$ExpectedWidgetId
    )

    $raw = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
    $lines = @($raw -split "`r?`n")
    if ($lines.Count -eq 13 -and $lines[-1] -eq '') {
        $lines = @($lines[0..11])
    }
    if ($lines.Count -ne 12 -or $lines[0] -ne 'gbar-performance-runtime-v2') {
        throw 'OverlayHost returned an invalid performance runtime record shape.'
    }
    if ($lines[1] -cne $ExpectedNonce -or
        $lines[2] -cne $ExpectedState.ToLowerInvariant() -or
        $lines[3] -cne $ExpectedWidgetId) {
        throw 'OverlayHost returned a mismatched performance runtime identity.'
    }

    $numbers = [System.Collections.Generic.List[uint64]]::new()
    foreach ($line in $lines[4..11]) {
        [uint64]$value = 0
        if (-not [uint64]::TryParse(
                $line,
                [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$value)) {
            throw 'OverlayHost returned a non-numeric performance runtime counter.'
        }
        $numbers.Add($value)
    }
    if ($numbers[1] -lt $numbers[0] -or $numbers[2] -eq 0) {
        throw 'OverlayHost returned an invalid performance counter interval.'
    }
    $durationSeconds = [double]($numbers[1] - $numbers[0]) / [double]$numbers[2]
    return [ordered]@{
        schemaVersion = 2
        state = $lines[2]
        widgetId = $lines[3]
        durationSeconds = [Math]::Round($durationSeconds, 6)
        timerMessageCount = $numbers[3]
        controllerTimerMessageCount = $numbers[4]
        guideCompatibilityTimerMessageCount = $numbers[5]
        paintMessageCount = $numbers[6]
        successfulDirect2DFrameCount = $numbers[7]
        timerMessagesPerSecond = if ($durationSeconds -gt 0) {
            [Math]::Round([double]$numbers[3] / $durationSeconds, 5)
        } else { $null }
        controllerTimerMessagesPerSecond = if ($durationSeconds -gt 0) {
            [Math]::Round([double]$numbers[4] / $durationSeconds, 5)
        } else { $null }
        guideCompatibilityTimerMessagesPerSecond = if ($durationSeconds -gt 0) {
            [Math]::Round([double]$numbers[5] / $durationSeconds, 5)
        } else { $null }
        successfulDirect2DFramesPerSecond = if ($durationSeconds -gt 0) {
            [Math]::Round([double]$numbers[7] / $durationSeconds, 5)
        } else { $null }
        artifact = [ordered]@{
            fileName = [IO.Path]::GetFileName($Path)
            bytes = [long](Get-Item -LiteralPath $Path).Length
            sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function Get-OptionalToolMetadata {
    param([Parameter(Mandatory)][string]$Name)
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        return [ordered]@{ available = $false; path = $null; version = $null }
    }
    $version = if ($command.Version) { $command.Version.ToString() } else { $null }
    return [ordered]@{
        available = $true
        path = [string]$command.Source
        version = $version
    }
}

function Get-MachineMetadata {
    $operatingSystem = Get-CimInstance Win32_OperatingSystem `
        -OperationTimeoutSec $CimOperationTimeoutSeconds
    $processor = Get-CimInstance Win32_Processor `
        -OperationTimeoutSec $CimOperationTimeoutSeconds | Select-Object -First 1
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
    $processMetrics = [System.Collections.Generic.List[object]]::new()
    $totalPrivateBytes = 0L
    $totalWorkingSet = 0L
    $totalHandles = 0L
    $totalThreads = 0L
    $totalCpuDelta = 0.0
    $hasCpuDelta = $false

    foreach ($row in $tree) {
        $processId = [int]$row.ProcessId
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            $startUtc = $process.StartTime.ToUniversalTime()
            $cpuTotal = $process.TotalProcessorTime.TotalMilliseconds
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

            $privateBytes = [long]$process.PrivateMemorySize64
            $workingSet = [long]$process.WorkingSet64
            $handles = [long]$process.HandleCount
            $threads = [long]$process.Threads.Count
            $process.Dispose()
            $totalPrivateBytes += $privateBytes
            $totalWorkingSet += $workingSet
            $totalHandles += $handles
            $totalThreads += $threads

            $processMetrics.Add([ordered]@{
                processId = $processId
                role = Get-ProcessRole -ProcessId $processId -RootProcessId $RootProcessId -Name ([string]$row.Name)
                name = [IO.Path]::GetFileNameWithoutExtension([string]$row.Name)
                privateWorkingSetBytes = $null
                privateBytes = $privateBytes
                workingSetBytes = $workingSet
                handleCount = $handles
                threadCount = $threads
                cpuTotalMilliseconds = [Math]::Round($cpuTotal, 3)
                normalizedCpuPercent = if ($null -eq $cpuPercent) { $null } else { [Math]::Round($cpuPercent, 5) }
            })
        }
        catch {
            # Process exit races are expected while sampling.
        }
    }

    if (-not ($processMetrics | Where-Object { $_.processId -eq $RootProcessId })) {
        throw 'The spawned OverlayHost process could not be sampled.'
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
            privateWorkingSetBytes = $null
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

    $privateWorkingSet = @($Samples | ForEach-Object { $_.totals.privateWorkingSetBytes } |
        Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    $privateBytes = @($Samples | ForEach-Object { [double]$_.totals.privateBytes })
    $workingSet = @($Samples | ForEach-Object { [double]$_.totals.workingSetBytes })
    $handles = @($Samples | ForEach-Object { [double]$_.totals.handleCount })
    $threads = @($Samples | ForEach-Object { [double]$_.totals.threadCount })
    $processCounts = @($Samples | ForEach-Object { [double]$_.processCount })
    $discoveredProcessCounts = @($Samples | ForEach-Object { [double]$_.discoveredProcessCount })
    $cpu = @($Samples | ForEach-Object { $_.totals.normalizedCpuPercent } | Where-Object { $null -ne $_ } |
        ForEach-Object { [double]$_ })
    $quiescentCpuIntervals = @($cpu | Where-Object { $_ -le 0.00001 }).Count

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
        $privateWorkingSetValues = @()
        $privateByteValues = @()
        $counts = @()
        foreach ($sample in $Samples) {
            $matching = @($sample.processes | Where-Object { $_.role -eq $role })
            $privateWorkingSetRows = @($matching | Where-Object {
                $null -ne $_.privateWorkingSetBytes
            })
            if ($matching.Count -gt 0 -and
                $privateWorkingSetRows.Count -eq $matching.Count) {
                $privateWorkingSetSum = 0L
                foreach ($row in $privateWorkingSetRows) {
                    $privateWorkingSetSum += [long]$row.privateWorkingSetBytes
                }
                $privateWorkingSetValues += [double]$privateWorkingSetSum
            }
            $privateByteSum = 0L
            foreach ($row in $matching) {
                $privateByteSum += [long]$row.privateBytes
            }
            $privateByteValues += [double]$privateByteSum
            $counts += [double]$matching.Count
        }
        $roles[$role] = [ordered]@{
            processCount = Describe $counts 0
            privateWorkingSetBytes = Describe $privateWorkingSetValues 0
            privateBytes = Describe $privateByteValues 0
        }
    }

    return [ordered]@{
        sampleCount = $Samples.Count
        total = [ordered]@{
            privateWorkingSetBytes = Describe $privateWorkingSet 0
            privateBytes = Describe $privateBytes 0
            workingSetBytes = Describe $workingSet 0
            normalizedCpuPercent = Describe $cpu 5
            processCount = Describe $processCounts 0
            discoveredProcessCount = Describe $discoveredProcessCounts 0
            handleCount = Describe $handles 0
            threadCount = Describe $threads 0
            cpuQuiescenceProxy = [ordered]@{
                definition = 'Sample intervals where the process-tree CPU-time delta rounded to no more than 0.00001 percent of total machine capacity.'
                observedIntervalCount = $cpu.Count
                quiescentIntervalCount = $quiescentCpuIntervals
                quiescentRatio = if ($cpu.Count -gt 0) {
                    [Math]::Round([double]$quiescentCpuIntervals / $cpu.Count, 5)
                } else { $null }
                schedulerWakeEvidence = $false
            }
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
        $hostP95 = if ($null -eq $Summary.byRole.host.privateWorkingSetBytes) {
            $null
        } else { $Summary.byRole.host.privateWorkingSetBytes.p95 }
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
            status = if ($null -eq $hostP95) { 'metric-unavailable' } elseif ($hostP95 -le 50MB) { 'within-target' } else { 'above-target' }
            kind = 'diagnostic-observation'
            releaseGate = $false
        })
    }
    if ($ScenarioName -eq 'Visible') {
        $workerP95 = if ($null -eq $Summary.byRole.worker.privateWorkingSetBytes) {
            $null
        } else { $Summary.byRole.worker.privateWorkingSetBytes.p95 }
        $workerMaximumCount = $Summary.byRole.worker.processCount.maximum
        $comparisons.Add([ordered]@{
            metric = 'visible.aggregateWorkerPrivateWorkingSetBytes.p95'
            observed = $workerP95
            target = 64MB
            unit = 'bytes'
            status = if ($workerMaximumCount -eq 0) { 'not-observed' } elseif ($workerMaximumCount -ne 1) { 'not-comparable' } elseif ($null -eq $workerP95) { 'metric-unavailable' } elseif ($workerP95 -le 64MB) { 'within-target' } else { 'above-target' }
            kind = 'diagnostic-observation'
            releaseGate = $false
            note = "The 64 MiB private-working-set target needs a private-working-set provider and exactly one worker; maximum observed worker count was $workerMaximumCount."
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

function Complete-PerformanceRuntimeDiagnostics {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Nonce,
        [Parameter(Mandatory)][string]$ScenarioName,
        [Parameter(Mandatory)][string]$ExpectedWidgetId
    )

    if (-not (Send-OverlayWindowMessage -ProcessId $Process.Id -Message 0x0010)) {
        throw 'Could not request graceful OverlayHost performance shutdown.'
    }
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $recordObserved = $false
    while ($deadline.Elapsed.TotalSeconds -lt $GracefulShutdownTimeoutSeconds) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) { $recordObserved = $true }
        $Process.Refresh()
        if ($recordObserved -and $Process.HasExited) { break }
        Start-Sleep -Milliseconds 50
    }
    $Process.Refresh()
    if (-not $recordObserved) {
        throw "OverlayHost did not publish runtime counters within $GracefulShutdownTimeoutSeconds seconds."
    }
    if (-not $Process.HasExited) {
        throw "OverlayHost did not exit gracefully within $GracefulShutdownTimeoutSeconds seconds."
    }
    return ConvertFrom-PerformanceRuntimeRecord -Path $Path -ExpectedNonce $Nonce `
        -ExpectedState $ScenarioName -ExpectedWidgetId $ExpectedWidgetId
}

function Invoke-ScenarioMeasurement {
    param(
        [Parameter(Mandatory)][string]$ScenarioName,
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][int]$LogicalProcessorCount,
        [Parameter(Mandatory)][string]$StartupErrorPath,
        [Parameter(Mandatory)][string]$RuntimeDiagnosticsPath,
        [Parameter(Mandatory)][string]$RuntimeDiagnosticsNonce
    )

    # Start-Process joins ArgumentList entries into one command line. Windows
    # paths cannot contain a quote, so explicit quoting is sufficient for an
    # output directory containing spaces.
    $quotedRuntimeDiagnosticsPath = '"' + $RuntimeDiagnosticsPath + '"'
    $arguments = @(
        '--performance-state', $ScenarioName.ToLowerInvariant(),
        '--performance-widget-id', $WidgetId,
        '--performance-diagnostics-path', $quotedRuntimeDiagnosticsPath,
        '--performance-diagnostics-nonce', $RuntimeDiagnosticsNonce
    )
    $beforeError = Get-FileState -Path $StartupErrorPath
    $launchUtc = [datetime]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = $null
    $rootStartUtc = $null
    $launchReturnedMilliseconds = $null
    $tracked = @{}
    $readyObserved = $null
    $bridgeObserved = $null
    $visibleObserved = $null

    try {
        $process = Start-Process -FilePath $ExecutablePath -ArgumentList $arguments `
            -PassThru -WindowStyle Hidden
        $rootStartUtc = $process.StartTime.ToUniversalTime()
        $launchReturnedMilliseconds = $clock.Elapsed.TotalMilliseconds
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
            if ($ScenarioName -ne 'Hidden' -and $null -eq $visibleObserved -and $tree.Count -gt 0) {
                $ids = @($tree | ForEach-Object { [int]$_.ProcessId })
                if ((Test-VisibleWindowForProcess -ProcessIds $ids) -and
                    (Test-OverlayWindowVisible -ProcessId $process.Id)) {
                    $visibleObserved = $clock.Elapsed.TotalMilliseconds
                }
            }
            $isReady = $null -ne $bridgeObserved -and
                ($ScenarioName -eq 'Hidden' -or $null -ne $visibleObserved)
            if ($isReady) {
                $readyObserved = $clock.Elapsed.TotalMilliseconds
                break
            }
            Start-Sleep -Milliseconds 50
        }

        if ($null -eq $readyObserved) {
            $missing = if ($null -eq $bridgeObserved) { 'WidgetBridge child' } else { 'visible overlay panel' }
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
        $runtimeCounterResetObserved = $null
        while ($clock.Elapsed.TotalMilliseconds -lt $endsAt) {
            $remaining = $nextSampleAt - $clock.Elapsed.TotalMilliseconds
            if ($remaining -gt 1) { Start-Sleep -Milliseconds ([int][Math]::Min($remaining, 250)) }
            if ($clock.Elapsed.TotalMilliseconds + 0.5 -lt $nextSampleAt) { continue }
            $elapsed = $clock.Elapsed.TotalMilliseconds
            # Sampling wakeups may arrive a fraction of a millisecond before
            # their requested deadline. Apply the same tolerance used by the
            # cadence check so the first boundary sample cannot shorten native
            # counter coverage by one whole interval.
            $phase = if ($elapsed + 0.5 -lt $measurementStartsAt) { 'warmup' } else { 'measurement' }
            if ($phase -eq 'measurement' -and $null -eq $runtimeCounterResetObserved) {
                if (-not (Send-OverlayWindowMessage -ProcessId $process.Id `
                        -Message $script:PerformanceResetMessage)) {
                    throw 'Could not reset OverlayHost runtime performance counters.'
                }
                $runtimeCounterResetObserved = $elapsed
            }
            if ($ScenarioName -ne 'Hidden' -and
                -not (Test-OverlayWindowVisible -ProcessId $process.Id)) {
                throw "The $ScenarioName overlay panel became hidden during measurement."
            }
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

        $runtime = Complete-PerformanceRuntimeDiagnostics -Process $process `
            -Path $RuntimeDiagnosticsPath -Nonce $RuntimeDiagnosticsNonce `
            -ScenarioName $ScenarioName -ExpectedWidgetId $WidgetId

        return [ordered]@{
            name = $ScenarioName.ToLowerInvariant()
            arguments = $arguments
            startedUtc = $launchUtc.ToString('o')
            startupObservations = [ordered]@{
                startProcessReturnedMilliseconds = [Math]::Round($launchReturnedMilliseconds, 3)
                bridgeChildObservedMilliseconds = [Math]::Round($bridgeObserved, 3)
                visibleWindowObservedMilliseconds = if ($null -eq $visibleObserved) { $null } else { [Math]::Round($visibleObserved, 3) }
                readyProxyObservedMilliseconds = [Math]::Round($readyObserved, 3)
                readyProxyDefinition = if ($ScenarioName -ne 'Hidden') {
                    "Host established the explicit $ScenarioName lifecycle for '$WidgetId', WidgetBridge was observed, and the exact overlay panel class was visible."
                } else {
                    "Host established the explicit Hidden lifecycle for '$WidgetId' and WidgetBridge was observed."
                }
            }
            sampling = [ordered]@{
                warmupSeconds = $WarmupSeconds
                requestedMeasurementSeconds = $SampleSeconds
                intervalMilliseconds = $SampleIntervalMilliseconds
                runtimeCounterResetMilliseconds = [Math]::Round($runtimeCounterResetObserved, 3)
                observedProcessNames = $observedNames
            }
            runtimeCounters = $runtime
            summary = $summary
            targetComparisons = @(Get-TargetComparisons -ScenarioName $ScenarioName -Summary $summary)
            samples = @($samples)
        }
    }
    finally {
        if ($null -ne $process) {
            try {
                $finalTree = @(Get-ProcessTreeSnapshot -RootProcessId $process.Id)
                Add-TrackedProcesses -Tracked $tracked -Rows $finalTree -EarliestStartUtc $launchUtc
            }
            catch { }
            if ($null -eq $rootStartUtc) {
                try { $rootStartUtc = $process.StartTime.ToUniversalTime() } catch { }
            }
            if ($null -ne $rootStartUtc) {
                Stop-TrackedProcesses -RootProcessId $process.Id `
                    -RootStartUtc $rootStartUtc -Tracked $tracked
            } else {
                # The process object was returned by this exact invocation but
                # its start time could not be read. It cannot yet be mistaken
                # for a reused PID; stop only this live object as the fallback.
                try { $process.Kill($true) } catch { }
            }
            $process.Dispose()
        }
    }
}

function ConvertTo-PerformanceMarkdown {
    param([Parameter(Mandatory)]$Report)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Overlay performance observation')
    $lines.Add('')
    $lines.Add("Generated: $($Report.generatedUtc)")
    $lines.Add("")
    $lines.Add('> These are bounded local observations, not release guarantees. Native counters report host timer messages, paint messages, and successful Direct2D EndDraw calls; they are not OS scheduler wakeups or DWM/game presentation events.')
    $lines.Add('')
    $lines.Add("Build: ``$($Report.build.configuration)`` ``$($Report.build.executableSha256.Substring(0, 12))``; package $([Math]::Round($Report.build.packageBytes / 1MB, 1)) MiB")
    $lines.Add("Machine: $($Report.machine.processor.name), $($Report.machine.processor.logicalProcessorCount) logical processors, $([Math]::Round($Report.machine.totalVisibleMemoryBytes / 1GB, 1)) GiB, $($Report.machine.operatingSystem.caption) build $($Report.machine.operatingSystem.buildNumber)")
    $lines.Add('')
    $lines.Add('| Scenario | Ready proxy | Working set p95 | Private bytes p95 | CPU p95 | Processes p95 | Timer msgs/s | Guide fallback msgs/s | D2D frames |')
    $lines.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($scenarioResult in $Report.scenarios) {
        $summary = $scenarioResult.summary
        $lines.Add("| $($scenarioResult.name) | $([Math]::Round($scenarioResult.startupObservations.readyProxyObservedMilliseconds, 1)) ms | $([Math]::Round($summary.total.workingSetBytes.p95 / 1MB, 1)) MiB | $([Math]::Round($summary.total.privateBytes.p95 / 1MB, 1)) MiB | $([Math]::Round($summary.total.normalizedCpuPercent.p95, 4))% | $($summary.total.processCount.p95) | $($scenarioResult.runtimeCounters.timerMessagesPerSecond) | $($scenarioResult.runtimeCounters.guideCompatibilityTimerMessagesPerSecond) | $($scenarioResult.runtimeCounters.successfulDirect2DFrameCount) |")
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
    $lines.Add('- Working set, private bytes, CPU time, handles, and threads come from bounded .NET process queries for the spawned host and descendants observed through parent-process relationships. Private working set is deliberately unavailable in this baseline because its CIM provider does not honor reliable bounded collection on this machine.')
    $lines.Add('- CPU is the target process-tree CPU-time delta divided by wall time and logical processor count. Sampling starts after the readiness proxy and configured warmup.')
    $lines.Add('- The host receives an explicit ephemeral Hidden, Visible, or Interactive startup state for the selected installed widget. This mode never writes user tray order, last-widget, or reopen-preference state.')
    $lines.Add('- Runtime counters begin after warmup and are bound to the invocation by a per-scenario nonce. This is provenance/error detection, not a security boundary against the same local user. A successful Direct2D EndDraw is renderer work, not proof that DWM presented a frame or that a game met its frame budget.')
    $lines.Add('- Timer-message counts cover the host HWND only. They do not count every message-loop wake, kernel scheduler wake, context switch, bridge/worker wake, or GPU event.')
    $lines.Add('- WMI/CIM collection creates external measurement load and can perturb a machine. Use ETW/PresentMon and repeated controlled runs for release evidence.')
    $lines.Add('- Only the spawned host and descendants whose PID plus creation time were observed are stopped during cleanup.')
    $lines.Add('')
    $lines.Add('## Optional tracing availability')
    $lines.Add('')
    $lines.Add("- WPR/ETW tool present: ``$($Report.optionalTracing.wpr.available)``. Collected: ``false``; an elevated system-wide trace is deliberately outside this auth-free, non-invasive baseline.")
    $lines.Add("- PresentMon present: ``$($Report.optionalTracing.presentMon.available)``. Collected: ``false``; no external tool is downloaded by this workflow.")
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
    function Assert-Throws([scriptblock]$Action, [string]$Message) {
        try {
            & $Action
        }
        catch {
            return
        }
        throw "$Message Expected an exception."
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
        [pscustomobject]@{ processCount=1; discoveredProcessCount=1; totals = [pscustomobject]@{ privateWorkingSetBytes=10; privateBytes=20; workingSetBytes=30; handleCount=2; threadCount=1; normalizedCpuPercent=$null }; processes=@([pscustomobject]@{ role='host'; privateWorkingSetBytes=10; privateBytes=20 }) },
        [pscustomobject]@{ processCount=1; discoveredProcessCount=1; totals = [pscustomobject]@{ privateWorkingSetBytes=30; privateBytes=40; workingSetBytes=50; handleCount=4; threadCount=2; normalizedCpuPercent=0.05 }; processes=@([pscustomobject]@{ role='host'; privateWorkingSetBytes=30; privateBytes=40 }) }
    )
    $summary = Get-MetricSummary -Samples $syntheticSamples
    Assert-Equal 30 $summary.total.privateWorkingSetBytes.p95 'Metric summary p95 failed.'
    Assert-Equal 30 $summary.byRole.host.privateWorkingSetBytes.p95 'Role summary failed.'
    Assert-Equal 1 $summary.total.processCount.p95 'Process-count summary failed.'
    Assert-Equal 0 $summary.total.cpuQuiescenceProxy.quiescentIntervalCount 'CPU quiescence proxy failed.'
    $comparison = @(Get-TargetComparisons -ScenarioName Hidden -Summary $summary)[0]
    Assert-Equal $false $comparison.releaseGate 'Diagnostic thresholds must never become release gates.'
    Assert-Equal 'insufficient-samples' $comparison.status 'Short p95 samples must not produce a target verdict.'
    $runtimePath = Join-Path ([IO.Path]::GetTempPath()) `
        ("gbar-performance-selftest-{0}.txt" -f [Guid]::NewGuid().ToString('N'))
    $nonce = 'a' * 64
    try {
        [IO.File]::WriteAllText(
            $runtimePath,
            "gbar-performance-runtime-v2`n$nonce`ninteractive`nsettings`n100`n1100`n1000`n100`n60`n40`n2`n1`n",
            [Text.UTF8Encoding]::new($false))
        $runtime = ConvertFrom-PerformanceRuntimeRecord -Path $runtimePath `
            -ExpectedNonce $nonce -ExpectedState Interactive -ExpectedWidgetId settings
        Assert-Equal 1 $runtime.durationSeconds 'Runtime duration parsing failed.'
        Assert-Equal 60 $runtime.controllerTimerMessageCount 'Controller timer parsing failed.'
        Assert-Equal 40 $runtime.guideCompatibilityTimerMessageCount 'Guide compatibility timer parsing failed.'
        Assert-Equal 1 $runtime.successfulDirect2DFrameCount 'Frame counter parsing failed.'
        Assert-Throws {
            ConvertFrom-PerformanceRuntimeRecord -Path $runtimePath `
                -ExpectedNonce ('b' * 64) -ExpectedState Interactive `
                -ExpectedWidgetId settings | Out-Null
        } 'Runtime nonce mismatch failed open.'
        Assert-Throws {
            ConvertFrom-PerformanceRuntimeRecord -Path $runtimePath `
                -ExpectedNonce $nonce -ExpectedState Visible `
                -ExpectedWidgetId settings | Out-Null
        } 'Runtime lifecycle mismatch failed open.'
    }
    finally {
        Remove-Item -LiteralPath $runtimePath -Force -ErrorAction SilentlyContinue
    }
    Write-Host 'Measure-OverlayPerformance self-tests passed (19 assertions).'
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
$concurrentOverlay = @(Get-CimInstance Win32_Process `
    -OperationTimeoutSec $CimOperationTimeoutSeconds | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
        [IO.Path]::GetFullPath([string]$_.ExecutablePath) -ieq $OverlayPath
    })
if ($concurrentOverlay.Count -gt 0) {
    $identities = [string]::Join(', ', @($concurrentOverlay | ForEach-Object {
        "PID $($_.ProcessId)"
    }))
    throw "Refusing a contaminated baseline because the target overlay is already running ($identities). Close it and retry."
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\performance'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$runId = "{0}-{1}" -f [datetime]::UtcNow.ToString('yyyyMMdd-HHmmssfff'),
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $OutputDirectory "overlay-performance-$runId"
$null = New-Item -ItemType Directory -Path $runDirectory -Force

# Warm the CIM providers before launch so their one-time startup is not charged
# to the target process. Sampling overhead remains documented in every report.
$machine = Get-MachineMetadata
$null = Get-CimInstance Win32_Process `
    -OperationTimeoutSec $CimOperationTimeoutSeconds | Select-Object -First 1
$build = Get-BuildMetadata -ExecutablePath $OverlayPath -RepositoryRoot $repositoryRoot
$startupError = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GameBarAlternative\startup-error.log'
$results = [System.Collections.Generic.List[object]]::new()

foreach ($scenarioName in @($Scenario | Select-Object -Unique)) {
    Write-Host "Measuring $scenarioName overlay state for $SampleSeconds seconds after a $WarmupSeconds-second warmup..."
    $runtimePath = Join-Path $runDirectory `
        ("runtime-{0}.txt" -f $scenarioName.ToLowerInvariant())
    $runtimeNonce = [Convert]::ToHexString(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $results.Add((Invoke-ScenarioMeasurement -ScenarioName $scenarioName -ExecutablePath $OverlayPath `
        -LogicalProcessorCount $machine.processor.logicalProcessorCount `
        -StartupErrorPath $startupError -RuntimeDiagnosticsPath $runtimePath `
        -RuntimeDiagnosticsNonce $runtimeNonce))
}

$report = [ordered]@{
    schemaVersion = $script:SchemaVersion
    harnessVersion = $script:HarnessVersion
    runId = $runId
    generatedUtc = [datetime]::UtcNow.ToString('o')
    evidenceClass = 'bounded-local-observation'
    releaseGate = $false
    percentileMethod = 'nearest-rank'
    harness = [ordered]@{
        sourcePath = $PSCommandPath
        sourceSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
        powerShellExecutable = (Get-Process -Id $PID).Path
        invocation = [ordered]@{
            configuration = $Configuration
            scenarios = @($Scenario | Select-Object -Unique)
            widgetId = $WidgetId
            warmupSeconds = $WarmupSeconds
            sampleSeconds = $SampleSeconds
            sampleIntervalMilliseconds = $SampleIntervalMilliseconds
            startupTimeoutSeconds = $StartupTimeoutSeconds
            cimOperationTimeoutSeconds = $CimOperationTimeoutSeconds
            gracefulShutdownTimeoutSeconds = $GracefulShutdownTimeoutSeconds
        }
        cleanupPolicy = 'Graceful WM_CLOSE first; exact observed PID plus creation-time descendants only; forced stop is a bounded fallback.'
    }
    build = $build
    machine = $machine
    optionalTracing = [ordered]@{
        wpr = Get-OptionalToolMetadata -Name 'wpr.exe'
        presentMon = Get-OptionalToolMetadata -Name 'PresentMon.exe'
        collected = $false
        reason = 'This baseline never elevates, starts a system-wide ETW session, or downloads an external presentation tool.'
    }
    scenarios = @($results)
    unmeasured = @(
        'GPU utilization and presentation activity',
        'OS scheduler wakeups and context-switch attribution',
        'Guide-to-first-frame and Guide-to-interactive latency',
        'controller-to-visual response latency',
        'frame pacing and game-frame impact',
        'DWM/game presentation (successful Direct2D EndDraw is counted separately)',
        'long-run memory and handle trends'
    )
}

$jsonPath = Join-Path $runDirectory 'report.json'
$markdownPath = Join-Path $runDirectory 'report.md'
Write-AtomicText -Path $jsonPath -Content ($report | ConvertTo-Json -Depth 12)
Write-AtomicText -Path $markdownPath -Content (ConvertTo-PerformanceMarkdown -Report $report)
Write-Host "Performance observation written to:"
Write-Host "  $jsonPath"
Write-Host "  $markdownPath"
