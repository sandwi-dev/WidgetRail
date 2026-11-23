# Pure measurement reduction shared by the capture runner and its fixture tests.
# A missing/reset counter is unknown, never a successful zero-drop observation.
function New-CaptureFormatObservation {
    [pscustomobject]@{observations=0;invalid=0;changes=0;first=$null;last=$null;lastPair=$null;lastSignature=$null}
}
function Add-CaptureFormatObservation($State,$Sample) {
    $sent=Get-CaptureVideoReport $Sample.receiver.outbound
    $received=Get-CaptureVideoReport $Sample.receiver.inbound
    try {
        foreach($value in @($Sample.capture.processId,$Sample.receiver.width,$Sample.receiver.height,
            $sent.width,$sent.height,$received.width,$received.height,$sent.timestamp,$received.timestamp)) {
            if($null -eq $value -or $value -is [string] -or ![double]::IsFinite([double]$value) -or $value -le 0){throw 'Missing format observation'}
        }
        if(!$sent.codec -or !$received.codec){throw 'Missing codec observation'}
        foreach($report in @($sent,$received)) {
            if($report.codec -eq 'video/H264' -and
                ($report.h264.profileLevelId -notmatch '^[0-9a-fA-F]{6}$' -or
                 $null -eq $report.h264.packetizationMode -or $report.h264.packetizationMode -notin @(0,1))) {
                throw 'Missing H264 format observation'
            }
        }
        $format=[pscustomobject]@{worker=$Sample.capture.processId;displayWidth=$Sample.receiver.width;displayHeight=$Sample.receiver.height;
            sender=[pscustomobject]@{width=$sent.width;height=$sent.height;codec=$sent.codec;h264Profile=$sent.h264.profileLevelId;packetizationMode=$sent.h264.packetizationMode};
            receiver=[pscustomobject]@{width=$received.width;height=$received.height;codec=$received.codec;h264Profile=$received.h264.profileLevelId;packetizationMode=$received.h264.packetizationMode}}
        $signature=$format|ConvertTo-Json -Depth 4 -Compress
        $pair=@($sent.timestamp,$received.timestamp)|ConvertTo-Json -Compress
        if($pair -eq $State.lastPair -and $signature -eq $State.lastSignature){return}
        if($State.observations -eq 0){$State.first=$format}
        elseif($signature -ne $State.lastSignature){$State.changes++}
        $State.observations++;$State.last=$format;$State.lastSignature=$signature;$State.lastPair=$pair
    }catch {$State.invalid++}
}
function Complete-CaptureFormatObservation($State) {
    [pscustomobject]@{observations=$State.observations;invalid=$State.invalid;changes=$State.changes;
        stable=($State.observations -ge 2 -and $State.invalid -eq 0 -and $State.changes -eq 0);
        first=$State.first;last=$State.last;
        scope='Periodic ready RTP/display samples across the measurement window, not frame-by-frame proof; missing or changed observations cannot establish stable format.'}
}
function Get-CaptureCounterDelta($First, $Last, [string]$Name) {
    $a=$First.$Name;$b=$Last.$Name
    if($null -eq $a -or $null -eq $b -or ![double]::IsFinite([double]$a) -or
        ![double]::IsFinite([double]$b) -or $a -lt 0 -or $b -lt $a){return $null}
    return [double]$b-[double]$a
}
function Get-CaptureVideoReport($Reports) {
    $video=@($Reports|Where-Object kind -eq 'video')
    if($video.Count -eq 1){return $video[0]}
    return $null
}
function Measure-CaptureStage($First, $Last) {
    $count=Get-CaptureCounterDelta $First $Last count
    $elapsed=Get-CaptureCounterDelta $First $Last totalMs
    return [pscustomobject]@{
        count=$count
        meanMilliseconds=if($count -gt 0 -and $null -ne $elapsed){$elapsed/$count}else{$null}
        lateIntervals=Get-CaptureCounterDelta $First $Last lateIntervals
        # Maxima cannot be subtracted; keep the lifetime label explicit.
        lifetimeMaximumMilliseconds=$Last.maxMs
    }
}
function Measure-CapturePipelineWindow($First, $Last) {
    $duration=Get-CaptureCounterDelta $First.receiver $Last.receiver nativeSampleTimestamp
    $sameWorker=$First.capture.processId -gt 0 -and $First.capture.processId -eq $Last.capture.processId
    if(!$sameWorker -or $null -eq $duration -or $duration -le 0){return $null}
    $sentBefore=Get-CaptureVideoReport $First.receiver.outbound
    $sentAfter=Get-CaptureVideoReport $Last.receiver.outbound
    $receivedBefore=Get-CaptureVideoReport $First.receiver.inbound
    $receivedAfter=Get-CaptureVideoReport $Last.receiver.inbound
    $sourceBefore=Get-CaptureVideoReport $First.receiver.mediaSources
    $sourceAfter=Get-CaptureVideoReport $Last.receiver.mediaSources
    $decoded=Get-CaptureCounterDelta $receivedBefore $receivedAfter framesDecoded
    $decodeTime=Get-CaptureCounterDelta $receivedBefore $receivedAfter totalDecodeTime
    $jitterCount=Get-CaptureCounterDelta $receivedBefore $receivedAfter jitterBufferEmittedCount
    $jitterDelay=Get-CaptureCounterDelta $receivedBefore $receivedAfter jitterBufferDelay
    $stages=[ordered]@{}
    foreach($name in @('construction','writerWrite','receivedToAckSent','nativeTimestampIntervals','browserDeliveryIntervals')){
        $stages[$name]=Measure-CaptureStage $First.receiver.stageTimings.$name $Last.receiver.stageTimings.$name
    }
    return [pscustomobject]@{
        seconds=$duration/1000
        nativeSubmittedPackets=Get-CaptureCounterDelta $First.capture $Last.capture submitted
        nativeVideoCreditDrops=Get-CaptureCounterDelta $First.capture $Last.capture dropped
        deliveredVideoFrames=Get-CaptureCounterDelta $First.receiver $Last.receiver frames
        browserSourceFrames=Get-CaptureCounterDelta $sourceBefore $sourceAfter frames
        encodedFrames=Get-CaptureCounterDelta $sentBefore $sentAfter framesEncoded
        sentFrames=Get-CaptureCounterDelta $sentBefore $sentAfter framesSent
        decodedFrames=$decoded
        receiverDroppedFrames=Get-CaptureCounterDelta $receivedBefore $receivedAfter framesDropped
        receiverFreezes=Get-CaptureCounterDelta $receivedBefore $receivedAfter freezeCount
        receiverFreezeSeconds=Get-CaptureCounterDelta $receivedBefore $receivedAfter totalFreezesDuration
        receiverLostPackets=Get-CaptureCounterDelta $receivedBefore $receivedAfter packetsLost
        retransmittedPackets=Get-CaptureCounterDelta $sentBefore $sentAfter retransmittedPacketsSent
        receiverMeanDecodeMilliseconds=if($decoded -gt 0 -and $null -ne $decodeTime){1000*$decodeTime/$decoded}else{$null}
        receiverMeanJitterBufferMilliseconds=if($jitterCount -gt 0 -and $null -ne $jitterDelay){1000*$jitterDelay/$jitterCount}else{$null}
        stageTimings=[pscustomobject]$stages
        scope='First ready sample to last ready sample; native packets include audio; counters describe separate asynchronous stages and must not be subtracted to infer exact frame loss. Credit drops exclude WGC frame-pool replacement; receiver metrics describe this local test peer, not remote Discord.'
    }
}
