param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$NativeDirectory,
    [string]$OutputDirectory,
    [ValidateSet('Native','Browser')][string]$SourceKind='Native',
    [ValidateSet('480p15','480p30','480p60','720p15','720p30','720p60','1080p15','1080p30','1080p60','1440p15','1440p30','1440p60')][string]$Quality='720p30',
    [ValidateSet('motion','detail')][string]$ContentPreference='detail',
    [ValidateSet('Auto','H264','VP8','VP9','AV1')][string]$Codec='Auto',
    [ValidateRange(1,50)][int]$BitrateMbps=8,
    [int]$SourceWidth=0,[int]$SourceHeight=0,
    [switch]$ApplicationAudio,
    [switch]$BlockStop
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CaptureSource.ps1"
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$native=[IO.Path]::GetFullPath($NativeDirectory)
$targetFps=[int]($Quality -split 'p')[1]
if($targetFps -eq 60 -and $SourceKind -ne 'Browser'){throw '60-FPS pipeline tests require the measured browser source preflight.'}
foreach($file in @((Join-Path $candidate 'OverlayFrontend.WinUI.exe'),(Join-Path $native 'WebRtcCapturePrototype.dll'),(Join-Path $native 'WebRtcCaptureWorker.exe'),(Join-Path $native 'CaptureWorkerHost.dll'))){
    if(!(Test-Path -LiteralPath $file)){throw "Missing capture-worker prerequisite: $file"}
}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/worker-runs/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
Copy-Item -LiteralPath $candidate -Destination "$output/candidate" -Recurse
foreach($file in @('WebRtcCapturePrototype.dll','WebRtcCaptureWorker.exe','CaptureWorkerHost.dll')){Copy-Item -LiteralPath "$native/$file" -Destination "$output/candidate"}
& "$PSScriptRoot/Stage.ps1" -OutputDirectory "$output/assets" -ProbeCodec $Codec -ProbeBitrateMbps $BitrateMbps
$source=$null;$probe=$null
try{
    # Visible generated content is the sole source selected by the self-test.
    $source=Start-SyntheticCaptureSource -CandidateDirectory "$output/candidate" -NativeDirectory $native -OutputDirectory "$output/source" -Kind $SourceKind -Audio ([bool]$ApplicationAudio) -Width $SourceWidth -Height $SourceHeight
    $preflight=if($targetFps -eq 60){Assert-SyntheticSource60Fps $source}else{$null}
    $arguments=@(('"--discord-capture-probe='+$output+'/assets"'),'--capture-probe-selftest','--capture-probe-worker',('--capture-probe-quality='+$Quality),('--capture-probe-content='+$ContentPreference),'--capture-probe-stop-after=14')
    $arguments += $source.Arguments
    if($ApplicationAudio){$arguments+='--capture-probe-audio'}
    if($BlockStop){$arguments+='--capture-probe-worker-block-stop'}
    $probe=Start-Process -FilePath "$output/candidate/OverlayFrontend.WinUI.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    $before=$null;$last=$null;$sourceBefore=$null;$sourceLast=$null
    while(!(Test-Path -LiteralPath "$output/assets/stop-result.json")){
        if($probe.HasExited){throw "Capture worker UI probe exited with $($probe.ExitCode)."}
        if($source.Process.HasExited){throw 'The runner-owned synthetic source exited during capture.'}
        if(Test-Path -LiteralPath "$output/assets/probe-error.json"){
            $failure=Get-Content -LiteralPath "$output/assets/probe-error.json" -Raw|ConvertFrom-Json
            throw "Synthetic pipeline failed before measurement: $($failure.type): $($failure.message)"
        }
        if([DateTime]::UtcNow -gt $deadline){throw "Capture worker UI probe timed out. Evidence: $output"}
        if(Test-Path -LiteralPath "$output/assets/capture-result.json"){
            try{
                $current=Get-Content -LiteralPath "$output/assets/capture-result.json" -Raw|ConvertFrom-Json
                if($null -ne $current.capture -and $current.capture.transport -ne 'worker'){throw 'Candidate used the in-process comparison path.'}
                $videoReady=@($current.receiver.inbound|Where-Object { $_.kind -eq 'video' -and $_.framesDecoded -gt 0 }).Count -eq 1
                $audioReady=!$ApplicationAudio -or @($current.receiver.inbound|Where-Object { $_.kind -eq 'audio' -and $_.totalSamplesReceived -gt 0 }).Count -eq 1
                if($current.capture.running -and $current.receiver.connection -eq 'connected' -and $current.receiver.frames -gt 30 -and $videoReady -and $audioReady){
                    $sample=[pscustomobject]@{at=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds();value=$current}
                    if(!$before){$before=$sample;if($SourceKind -eq 'Browser'){$sourceBefore=Read-SyntheticSourceObservation $source}}
                    $last=$sample;if($SourceKind -eq 'Browser'){$sourceLast=Read-SyntheticSourceObservation $source}
                }
            }catch [System.ArgumentException] { }
        }
        Start-Sleep -Milliseconds 250
    }
    $stopped=Get-Content -LiteralPath "$output/assets/stop-result.json" -Raw|ConvertFrom-Json
    if(!$before -or !$last -or $last.at-$before.at -lt 8000){throw 'Insufficient live worker WebRTC evidence.'}
    $seconds=($last.value.receiver.nativeSampleTimestamp-$before.value.receiver.nativeSampleTimestamp)/1000.0
    if($seconds -lt 8){throw 'Insufficient native-browser measurement interval.'}
    $sent=($last.value.receiver.frames-$before.value.receiver.frames)/$seconds
    $beforeVideo=@($before.value.receiver.inbound|Where-Object kind -eq 'video')[0]
    $lastVideo=@($last.value.receiver.inbound|Where-Object kind -eq 'video')[0]
    $videoSeconds=($lastVideo.timestamp-$beforeVideo.timestamp)/1000.0
    if($videoSeconds -lt 8){throw 'Insufficient receiver RTP statistics interval.'}
    $decoded=($lastVideo.framesDecoded-$beforeVideo.framesDecoded)/$videoSeconds
    $beforeEncoded=@($before.value.receiver.outbound|Where-Object kind -eq 'video')[0]
    $lastEncoded=@($last.value.receiver.outbound|Where-Object kind -eq 'video')[0]
    $encodeSeconds=($lastEncoded.timestamp-$beforeEncoded.timestamp)/1000.0
    $encoded=if($encodeSeconds -ge 8){($lastEncoded.framesEncoded-$beforeEncoded.framesEncoded)/$encodeSeconds}else{$null}
    $codecOkay=$Codec -eq 'Auto' -or ($lastEncoded.codec -eq ('video/'+$Codec) -and $lastVideo.codec -eq ('video/'+$Codec))
    $frameDelta=$lastEncoded.framesEncoded-$beforeEncoded.framesEncoded
    $encoding=[pscustomobject]@{requestedCodec=$Codec;codec=$lastEncoded.codec;codecConfirmed=$codecOkay;maximumBitrateMbps=$BitrateMbps;
        actualBitrateMbps=if($encodeSeconds -ge 8 -and $null -ne $lastEncoded.bytesSent -and $null -ne $beforeEncoded.bytesSent -and $lastEncoded.bytesSent -ge $beforeEncoded.bytesSent){($lastEncoded.bytesSent-$beforeEncoded.bytesSent)*8/$encodeSeconds/1000000}else{$null};
        meanEncodeMilliseconds=if($frameDelta -gt 0 -and $null -ne $lastEncoded.totalEncodeTime -and $null -ne $beforeEncoded.totalEncodeTime -and $lastEncoded.totalEncodeTime -ge $beforeEncoded.totalEncodeTime){1000*($lastEncoded.totalEncodeTime-$beforeEncoded.totalEncodeTime)/$frameDelta}else{$null};
        meanQuantizer=if($frameDelta -gt 0 -and $null -ne $lastEncoded.qpSum -and $null -ne $beforeEncoded.qpSum -and $lastEncoded.qpSum -ge $beforeEncoded.qpSum){($lastEncoded.qpSum-$beforeEncoded.qpSum)/$frameDelta}else{$null};
        implementation=$lastEncoded.encoderImplementation;powerEfficient=$lastEncoded.powerEfficientEncoder;limitation=$lastEncoded.qualityLimitationReason}
    $processGone=!(Get-Process -Id $stopped.capture.processId -ErrorAction SilentlyContinue)
    $audioSamples=$last.value.receiver.samples-$before.value.receiver.samples
    $firstAudio=@($before.value.receiver.inbound|Where-Object kind -eq 'audio')
    $lastAudio=@($last.value.receiver.inbound|Where-Object kind -eq 'audio')
    $decodedAudioSamples=if($firstAudio.Count -eq 1 -and $lastAudio.Count -eq 1){$lastAudio[0].totalSamplesReceived-$firstAudio[0].totalSamplesReceived}else{0}
    $targetHeight=[int]($Quality -split 'p')[0]
    $targetWidth=@{480=1706;720=2560;1080=3840;1440=5120}[$targetHeight]
    $measuredWidth=if($SourceKind -eq 'Browser'){$source.Identity.captureWidth}else{$SourceWidth}
    $measuredHeight=if($SourceKind -eq 'Browser'){$source.Identity.captureHeight}else{$SourceHeight}
    $expectedScale=if($measuredWidth -gt 0){[Math]::Min(1.0,[Math]::Min($targetWidth/$measuredWidth,$targetHeight/$measuredHeight))}else{0}
    $geometryOkay=$last.value.receiver.quality -eq $Quality -and $last.value.receiver.width -gt 0 -and $last.value.receiver.height -gt 0 -and $last.value.receiver.width -le $targetWidth -and $last.value.receiver.height -le $targetHeight
    if($measuredWidth -gt 0){$geometryOkay=$geometryOkay -and $last.value.receiver.width -eq ([int][Math]::Floor($measuredWidth*$expectedScale/2)*2) -and $last.value.receiver.height -eq ([int][Math]::Floor($measuredHeight*$expectedScale/2)*2)}
    $sourceCadence=if($SourceKind -eq 'Browser'){Measure-SyntheticSourceCadence $sourceBefore $sourceLast}else{$null}
    $sourceOkay=$targetFps -ne 60 -or ($sourceCadence.seconds -ge 8 -and $sourceCadence.framesPerSecond -ge 58.5 -and $sourceCadence.framesPerSecond -le 61.5)
    $passed=$codecOkay -and $sourceOkay -and $geometryOkay -and $last.value.receiver.contentPreference -eq $ContentPreference -and $sent -ge $targetFps*.85 -and $decoded -ge $targetFps*.85 -and (!$ApplicationAudio -or ($audioSamples -gt 48000 -and $decodedAudioSamples -gt 48000)) -and $stopped.milliseconds -lt 2500 -and $stopped.capture.workerExited -and
        $stopped.capture.forcedTermination -eq [bool]$BlockStop -and $processGone -and !$stopped.capture.running
    $result=[pscustomobject]@{passed=$passed;quality=$Quality;contentPreference=$ContentPreference;geometryOkay=$geometryOkay;sourceKind=$SourceKind;sourceIdentity=$source.Identity;sourceQualified=$sourceOkay;sourcePreflight=$preflight;sourceCadence=$sourceCadence;sourceFirst=$sourceBefore;sourceLast=$sourceLast;applicationAudio=[bool]$ApplicationAudio;audioSamples=$audioSamples;decodedAudioSamples=$decodedAudioSamples;submittedFps=$sent;encodedFps=$encoded;decodedFps=$decoded;seconds=$seconds;stopMilliseconds=$stopped.milliseconds;forced=$stopped.capture.forcedTermination;workerExited=$stopped.capture.workerExited;processGone=$processGone;nativeCapture=$last.value.capture;firstReceiver=$before.value.receiver;receiver=$last.value.receiver;scope='Synthetic source to job-owned WGC worker to WebView native shared mapping to local WebRTC; no Discord or microphone'}
    $result|Add-Member -NotePropertyName encoding -NotePropertyValue $encoding
    $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$output/result.json"
    $result|ConvertTo-Json -Depth 10|Write-Output
    if(!$passed){throw "Capture worker probe failed; see $output/result.json"}
}finally{
    if($probe){if(!$probe.HasExited){Stop-Process -Id $probe.Id};$probe.Dispose()}
    Stop-SyntheticCaptureSource $source
}
