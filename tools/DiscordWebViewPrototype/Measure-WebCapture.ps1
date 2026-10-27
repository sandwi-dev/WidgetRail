param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$NativeDirectory,
    [ValidateRange(20,600)][int]$Seconds = 100,
    [switch]$Static,
    [switch]$ApplicationAudio,
    [switch]$IncludeGpu,
    [ValidateSet('Native','Browser')][string]$SourceKind = 'Native',
    [ValidateSet('480p15','480p30','480p60','720p15','720p30','720p60','1080p15','1080p30','1080p60','1440p15','1440p30','1440p60')][string]$Quality='720p30',
    [int]$SourceWidth=0,[int]$SourceHeight=0,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/CaptureSource.ps1"
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$native = [IO.Path]::GetFullPath($NativeDirectory)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts/cap-measure-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
foreach ($file in @("$candidate/OverlayFrontend.WinUI.exe", "$candidate/WebRtcCaptureWorker.exe", "$candidate/CaptureWorkerHost.dll", "$candidate/WebRtcCapturePrototype.dll") + $(if($SourceKind -eq 'Native'){@("$native/WindowCaptureFixture.exe")}else{@()})) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing prerequisite: $file" }
}
New-Item -ItemType Directory -Path $output | Out-Null
Copy-Item -LiteralPath $candidate -Destination "$output/candidate" -Recurse
if($SourceKind -eq 'Native'){Copy-Item -LiteralPath "$native/WindowCaptureFixture.exe" -Destination "$output/candidate"}
& "$PSScriptRoot/Stage.ps1" -OutputDirectory "$output/assets"
$source = $null; $probe = $null
$samples = [Collections.Generic.List[object]]::new()
$failure = $null
$gpuError = $null
$previousGpu = @{}
try {
    # A visible generated window is required by Windows Graphics Capture.
    $source = Start-SyntheticCaptureSource -CandidateDirectory "$output/candidate" -NativeDirectory "$output/candidate" -OutputDirectory "$output/source" -Kind $SourceKind -Audio ([bool]$ApplicationAudio) -Static ([bool]$Static) -Width $SourceWidth -Height $SourceHeight
    $probeArgs = @(('"--discord-capture-probe=' + $output + '/assets"'), '--capture-probe-selftest', '--capture-probe-worker', ('--capture-probe-quality='+$Quality), ('--capture-probe-stop-after=' + ($Seconds + 5)))
    $probeArgs += $source.Arguments
    if ($ApplicationAudio) { $probeArgs += '--capture-probe-audio' }
    $probe = Start-Process -FilePath "$output/candidate/OverlayFrontend.WinUI.exe" -ArgumentList $probeArgs -WindowStyle Hidden -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $deadline = $Seconds + 40
    while (!(Test-Path -LiteralPath "$output/assets/stop-result.json")) {
        if ($probe.HasExited) { throw "Probe exited with $($probe.ExitCode)." }
        if ($source.Process.HasExited) { throw 'The runner-owned synthetic source exited during capture.' }
        if ($clock.Elapsed.TotalSeconds -gt $deadline) { throw 'Probe timed out.' }
        $state = $null
        if (Test-Path -LiteralPath "$output/assets/capture-result.json") {
            try { $state = Get-Content -LiteralPath "$output/assets/capture-result.json" -Raw | ConvertFrom-Json }
            catch { } # Snapshot replacement can race the read; next sample retries.
        }
        if ($state) {
            if ($state.capture.transport -ne 'worker') { throw 'Expected production worker transport.' }
            # Include only the probe and its descendants, excluding the generated source.
            # No command lines, window names, URLs or user content enter the evidence.
            $processes = @(Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId)
            $owned = [Collections.Generic.HashSet[int]]::new()
            [void]$owned.Add($probe.Id)
            do {
                $added = $false
                foreach ($item in $processes) {
                    if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $added = $true }
                }
            } while ($added)
            $resources = @(foreach ($processId in $owned) {
                $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($process) {
                    try { [pscustomobject]@{ id=$processId; started=$process.StartTime.ToUniversalTime().Ticks; name=$process.ProcessName; cpuSeconds=$process.TotalProcessorTime.TotalSeconds; privateBytes=$process.PrivateMemorySize64; workingSetBytes=$process.WorkingSet64 } }
                    finally { $process.Dispose() }
                }
            })
            $gpu = @()
            if ($IncludeGpu -and !$gpuError) {
                try {
                    # PERF_100NSEC_TIMER: delta busy ticks / delta wall-clock ticks.
                    # The formatted WMI class rounds small workloads to zero.
                    $gpu = @(Get-CimInstance Win32_PerfRawData_GPUPerformanceCounters_GPUEngine | ForEach-Object {
                        if ($_.Name -match '^pid_(\d+)_(.+)$' -and $owned.Contains([int]$Matches[1])) {
                            $gpuProcessId=[int]$Matches[1]; $engine=$Matches[2]
                            $incarnation=($resources | Where-Object id -eq $gpuProcessId | Select-Object -First 1).started
                            $key=$_.Name+':'+$incarnation
                            $prior=$previousGpu[$key]; $percent=$null
                            if($prior -and $_.Timestamp_Sys100NS -gt $prior.at -and $_.UtilizationPercentage -ge $prior.busy) {
                                $percent=100.0*($_.UtilizationPercentage-$prior.busy)/($_.Timestamp_Sys100NS-$prior.at)
                            }
                            $previousGpu[$key]=@{ at=$_.Timestamp_Sys100NS; busy=$_.UtilizationPercentage }
                            [pscustomobject]@{ id=$gpuProcessId; engine=$engine; percent=$percent;
                                busy100ns=$_.UtilizationPercentage; at100ns=$_.Timestamp_Sys100NS }
                        }
                    })
                } catch { $gpuError = $_.Exception.GetType().Name }
            }
            $sourceState=$null
            if($SourceKind -eq 'Browser' -and (Test-Path -LiteralPath "$output/source/source-status.json")){
                try{$sourceState=Get-Content -LiteralPath "$output/source/source-status.json" -Raw|ConvertFrom-Json}catch{}
            }
            $samples.Add([pscustomobject]@{ seconds=$clock.Elapsed.TotalSeconds; media=$state; source=$sourceState; processes=$resources; gpu=$gpu })
            $samples | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$output/samples.json"
            if ($state.capture.reason) { throw "Capture ended or failed: $($state.capture.reason) (result $($state.capture.result))." }
        }
        Start-Sleep -Seconds 2
    }
    $stopped = Get-Content -LiteralPath "$output/assets/stop-result.json" -Raw | ConvertFrom-Json
    if (!$stopped.capture.workerExited -or $stopped.capture.running) { throw 'Native worker exit was not confirmed.' }
    if (Get-Process -Id $stopped.capture.processId -ErrorAction SilentlyContinue) { throw 'The exact capture worker still exists.' }
} catch { $failure = $_.Exception.Message }
finally {
    if ($probe) { if (!$probe.HasExited) { Stop-Process -Id $probe.Id }; $probe.Dispose() }
    Stop-SyntheticCaptureSource $source
}
$steady = @($samples | Where-Object { $_.seconds -ge 10 -and $_.media.capture.running -and $_.media.receiver.connection -eq 'connected' })
$performance = $null
if ($steady.Count -ge 2) {
    $first=$steady[0]; $last=$steady[-1]; $elapsed=$last.seconds-$first.seconds
    $firstVideo=@($first.media.receiver.inbound | Where-Object kind -eq video)
    $lastVideo=@($last.media.receiver.inbound | Where-Object kind -eq video)
    $cpu=0.0
    # Compare the same process incarnations; separately expose excluded processes.
    $matched=0
    foreach ($process in $last.processes) {
        $prior=$first.processes | Where-Object { $_.id -eq $process.id -and $_.started -eq $process.started }
        if ($prior) { $cpu += $process.cpuSeconds-$prior.cpuSeconds; $matched++ }
    }
    $gpuPeak=$null
    if ($IncludeGpu -and !$gpuError) {
        $engineTotals=@(foreach($sample in $steady) {
            foreach($engine in ($sample.gpu | Where-Object { $null -ne $_.percent } | Group-Object engine)) { ($engine.Group | Measure-Object percent -Sum).Sum }
        })
        if ($engineTotals.Count) { $gpuPeak=($engineTotals | Measure-Object -Maximum).Maximum }
    }
    $decoded=$null
    if($firstVideo.Count -eq 1 -and $lastVideo.Count -eq 1) { $decoded=($lastVideo[0].framesDecoded-$firstVideo[0].framesDecoded)/$elapsed }
    $firstAudio=@($first.media.receiver.inbound | Where-Object kind -eq audio)
    $lastAudio=@($last.media.receiver.inbound | Where-Object kind -eq audio)
    $decodedAudio=$null
    if($firstAudio.Count -eq 1 -and $lastAudio.Count -eq 1){$decodedAudio=$lastAudio[0].totalSamplesReceived-$firstAudio[0].totalSamplesReceived}
    $performance=[pscustomobject]@{
        seconds=$elapsed; decodedFps=$decoded; submittedVideoFps=($last.media.receiver.frames-$first.media.receiver.frames)/$elapsed
        videoDrops=$last.media.capture.dropped-$first.media.capture.dropped; audioSamples=$last.media.receiver.samples-$first.media.receiver.samples
        decodedAudioSamples=$decodedAudio
        machineCpuPercent=100*$cpu/$elapsed/[Environment]::ProcessorCount; logicalProcessors=[Environment]::ProcessorCount
        cpuMatchedProcesses=$matched; cpuUnmatchedFinalProcesses=$last.processes.Count-$matched
        privateMiBStart=($first.processes | Measure-Object privateBytes -Sum).Sum/1MB
        privateMiBEnd=($last.processes | Measure-Object privateBytes -Sum).Sum/1MB
        privateMiBPeak=(@($steady | ForEach-Object { ($_.processes | Measure-Object privateBytes -Sum).Sum/1MB }) | Measure-Object -Maximum).Maximum
        gpuBusiestEnginePeakPercent=$gpuPeak; gpuCounterError=$gpuError
    }
}
$result = [pscustomobject]@{
    error=$failure; sampleCount=$samples.Count; static=[bool]$Static; applicationAudio=[bool]$ApplicationAudio
    sourceKind=$SourceKind;sourceIdentity=$source.Identity;quality=$Quality
    performance=$performance; gpuRequested=[bool]$IncludeGpu
    scope='Generated source to production native worker to local WebRTC; includes probe encoder/decoder and descendants, excludes generated source. CPU compares matching process incarnations. Optional GPU values are raw Windows per-engine busy-time deltas; DWM/system-attributed work is excluded, so these are not total GPU cost. No Discord, microphone or remote reception.'
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/result.json"
$result | ConvertTo-Json -Depth 5 | Write-Output
Write-Output "Evidence: $output"
if ($failure) { throw $failure }
