[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(2,3600)][int]$SampleSeconds = 30,
    [ValidateRange(500,10000)][int]$IntervalMilliseconds = 1000,
    [string]$ExpectedProcessName = 'OverlayFrontend.WinUI',
    [string]$Scenario = 'unspecified'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFile) { throw 'Choose a new output path; resource evidence is never overwritten.' }
$null = New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($outputFile))
$root = Get-Process -Id $AppPid -ErrorAction Stop
if ($root.ProcessName -ne $ExpectedProcessName) { throw 'The supplied process is not the expected application.' }
$rootStarted = $root.StartTime.ToUniversalTime()
$executable = $root.Path
$executableHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
$known = @{}
$previousCpu = @{}
$samples = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
$previousElapsed = 0.0
$ending = 'duration-complete'
function Identity($Entry) { return "$($Entry.ProcessId):$($Entry.CreationDate.ToUniversalTime().Ticks)" }
function SameStart([DateTime]$First, [DateTime]$Second) {
    # CIM creation timestamps lose sub-microsecond precision.
    return [Math]::Abs(($First.ToUniversalTime()-$Second.ToUniversalTime()).Ticks) -lt 10
}
while ($clock.Elapsed.TotalSeconds -lt $SampleSeconds) {
    $sampleStart = $clock.Elapsed.TotalSeconds
    # Read only identity/parentage; never collect command lines, titles or tokens.
    $inventory = @(Get-CimInstance -Query 'SELECT ProcessId,ParentProcessId,CreationDate,Name FROM Win32_Process' -OperationTimeoutSec 3)
    $rootEntry = $inventory | Where-Object { $_.ProcessId -eq $AppPid -and (SameStart $_.CreationDate $rootStarted) } | Select-Object -First 1
    if ($null -eq $rootEntry) { $ending = 'root-exited-or-replaced'; break }
    $current = @{}
    foreach ($entry in $inventory) {
        $key = Identity $entry
        if ($known.ContainsKey($key)) { $current[$key] = $entry }
    }
    $current[(Identity $rootEntry)] = $rootEntry
    do {
        $added = $false
        foreach ($entry in $inventory) {
            $key = Identity $entry
            if ($current.ContainsKey($key)) { continue }
            $parent = @($current.Values | Where-Object { $_.ProcessId -eq $entry.ParentProcessId -and $_.CreationDate -le $entry.CreationDate })
            if ($parent.Count -gt 0) { $current[$key] = $entry; $added = $true }
        }
    } while ($added)
    $rows = [Collections.Generic.List[object]]::new()
    $cpuDelta = 0.0
    foreach ($key in @($current.Keys)) {
        $entry = $current[$key]
        $known[$key] = $entry
        $process = $null
        try {
            $process = Get-Process -Id $entry.ProcessId -ErrorAction Stop
            if (-not (SameStart $process.StartTime $entry.CreationDate)) { continue }
            $cpu = $process.TotalProcessorTime.TotalSeconds
            if ($previousCpu.ContainsKey($key)) { $cpuDelta += [Math]::Max(0.0, $cpu - $previousCpu[$key]) }
            elseif ($samples.Count -gt 0) { $cpuDelta += $cpu }
            $previousCpu[$key] = $cpu
            $rows.Add([pscustomobject][ordered]@{ identity=$key; pid=$process.Id; name=$process.ProcessName;
                privateBytes=$process.PrivateMemorySize64; workingSetBytes=$process.WorkingSet64;
                handles=$process.HandleCount; cpuSeconds=$cpu })
        } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
        catch [InvalidOperationException] { } # A sampled child can exit between identity and counters.
        finally { if ($null -ne $process) { $process.Dispose(); $process = $null } }
    }
    $elapsed = $clock.Elapsed.TotalSeconds
    $interval = $elapsed - $previousElapsed
    $samples.Add([ordered]@{ elapsedSeconds=$elapsed; observationMilliseconds=($elapsed-$sampleStart)*1000;
        processCount=$rows.Count; privateBytes=($rows | Measure-Object privateBytes -Sum).Sum;
        workingSetBytes=($rows | Measure-Object workingSetBytes -Sum).Sum;
        cpuPercentOfOneCore=$(if ($samples.Count -eq 0) { $null } else { 100*$cpuDelta/$interval }); processes=$rows.ToArray() })
    $previousElapsed = $elapsed
    $wait = [Math]::Min($SampleSeconds-$elapsed, [Math]::Max(0.0, $IntervalMilliseconds/1000-($elapsed-$sampleStart)))
    if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]($wait*1000)) }
}
$ordered = @($samples | ForEach-Object { [double]$_.privateBytes } | Sort-Object)
$report = [ordered]@{ schemaVersion=1; generatedUtc=[DateTime]::UtcNow.ToString('O'); rootPid=$AppPid;
    rootStartedUtc=$rootStarted.ToString('O'); executable=$executable; executableSha256=$executableHash;
    scenario=$Scenario; ending=$ending; sampleCount=$samples.Count;
    durationSeconds=$clock.Elapsed.TotalSeconds;
    peakSampledPrivateBytes=$(if ($ordered.Count) { $ordered[-1] } else { $null });
    medianSampledPrivateBytes=$(if ($ordered.Count) { $ordered[[int][Math]::Floor(($ordered.Count-1)/2)] } else { $null });
    limits='Sampled process-tree CPU/private commit/working set only. Short-lived children and between-sample peaks may be missed. GPU memory and frame presentation are not measured. First CPU interval is omitted.';
    samples=$samples.ToArray() }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFile
$root.Dispose()
if ($samples.Count -lt 2) { throw "Insufficient live samples: $($samples.Count); evidence retained at $outputFile" }
"Recorded $($samples.Count) samples to $outputFile"
