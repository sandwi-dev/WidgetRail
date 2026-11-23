$ErrorActionPreference='Stop'
. "$PSScriptRoot/CaptureMeasurements.ps1"
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Sample([int]$Offset){
    [pscustomobject]@{
        capture=[pscustomobject]@{processId=123;submitted=100+$Offset*2;dropped=39}
        receiver=[pscustomobject]@{
            nativeSampleTimestamp=1000+$Offset*1000;frames=50+$Offset
            mediaSources=@([pscustomobject]@{kind='video';frames=40+$Offset})
            outbound=@([pscustomobject]@{kind='video';framesEncoded=30+$Offset;framesSent=29+$Offset;retransmittedPacketsSent=2+$Offset})
            inbound=@([pscustomobject]@{kind='video';framesDecoded=20+$Offset;framesDropped=4+$Offset;freezeCount=1+$Offset;totalFreezesDuration=.1+$Offset*.2;packetsLost=2+$Offset;totalDecodeTime=.2+$Offset*.01;jitterBufferDelay=.5+$Offset*.02;jitterBufferEmittedCount=10+$Offset})
            stageTimings=[pscustomobject]@{construction=[pscustomobject]@{count=50+$Offset;totalMs=100+$Offset*3;maxMs=8}}
        }
    }
}
$first=Sample 0;$last=Sample 10;$result=Measure-CapturePipelineWindow $first $last
Check ($result.seconds -eq 10 -and $result.nativeSubmittedPackets -eq 20 -and $result.deliveredVideoFrames -eq 10) 'Native packets must remain distinct from delivered video frames.'
Check ($result.nativeVideoCreditDrops -eq 0) 'Startup credit drops must not become steady-state drops.'
Check ($result.receiverDroppedFrames -eq 10 -and $result.receiverFreezes -eq 10 -and $result.retransmittedPackets -eq 10) 'Independent receiver/network deltas were lost.'
Check ([Math]::Abs($result.receiverMeanDecodeMilliseconds-10) -lt .001 -and [Math]::Abs($result.receiverMeanJitterBufferMilliseconds-20) -lt .001) 'Receiver timing means are not interval-derived.'
Check ($result.stageTimings.construction.count -eq 10 -and $result.stageTimings.construction.meanMilliseconds -eq 3 -and $result.stageTimings.construction.lifetimeMaximumMilliseconds -eq 8) 'Stage means or lifetime maximum labeling changed.'
Check ($null -eq $result.stageTimings.writerWrite.meanMilliseconds -and $null -eq $result.stageTimings.construction.lateIntervals) 'Missing metrics must remain unknown.'
$last.capture.dropped=2;$last.receiver.inbound[0].framesDropped=1
$result=Measure-CapturePipelineWindow $first $last
Check ($null -eq $result.nativeVideoCreditDrops -and $null -eq $result.receiverDroppedFrames) 'Reset counters must not become negative or zero drops.'
$last.capture.processId=124
Check ($null -eq (Measure-CapturePipelineWindow $first $last)) 'A replacement worker cannot be compared to its predecessor.'
$last=Sample 0
Check ($null -eq (Measure-CapturePipelineWindow $first $last)) 'Zero duration must not produce a measurement window.'
$last=Sample 10;$last.receiver.inbound=@()
$result=Measure-CapturePipelineWindow $first $last
Check ($null -eq $result.decodedFrames -and $null -eq $result.receiverMeanDecodeMilliseconds) 'Absent receiver reports must not claim success.'
$last.receiver.inbound=@($first.receiver.inbound[0],$first.receiver.inbound[0])
Check ($null -eq (Measure-CapturePipelineWindow $first $last).decodedFrames) 'Ambiguous receiver reports must remain unknown.'
Write-Output 'PASS steady-state capture counters, independent stage timing, startup exclusion, missing/reset metrics and worker replacement'

function FormatSample([int]$Offset) {
    $sample=Sample $Offset
    $sample.receiver|Add-Member width 2560
    $sample.receiver|Add-Member height 1440
    foreach($report in @($sample.receiver.outbound[0],$sample.receiver.inbound[0])) {
        foreach($entry in @{width=2560;height=1440;timestamp=(1000+$Offset*1000);codec='video/H264';h264=[pscustomobject]@{profileLevelId='42001f';packetizationMode=1}}.GetEnumerator()){
            $report|Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
    }
    return $sample
}
$formats=New-CaptureFormatObservation
$a=FormatSample 0;$b=FormatSample 1
Add-CaptureFormatObservation $formats $a
Add-CaptureFormatObservation $formats $a
Check (!(Complete-CaptureFormatObservation $formats).stable -and $formats.observations -eq 1) 'Repeated stale RTP samples cannot establish an interval.'
Add-CaptureFormatObservation $formats $b
Check ((Complete-CaptureFormatObservation $formats).stable -and $formats.observations -eq 2) 'Two matching observed formats should be recognized.'
foreach($change in @('resolution','display','codec','profile','mode','worker','missing','missingProfile','missingMode')) {
    $formats=New-CaptureFormatObservation
    Add-CaptureFormatObservation $formats (FormatSample 0)
    $middle=FormatSample 1
    switch($change) {
        resolution {$middle.receiver.outbound[0].width=1280;$middle.receiver.outbound[0].height=720}
        display {$middle.receiver.width=1280}
        codec {$middle.receiver.outbound[0].codec='video/VP8'}
        profile {$middle.receiver.inbound[0].h264.profileLevelId='42e01f'}
        mode {$middle.receiver.inbound[0].h264.packetizationMode=0}
        worker {$middle.capture.processId=124}
        missing {$middle.receiver.inbound[0].width=$null}
        missingProfile {$middle.receiver.inbound[0].h264.profileLevelId=$null}
        missingMode {$middle.receiver.inbound[0].h264.packetizationMode=$null}
    }
    Add-CaptureFormatObservation $formats $middle
    Add-CaptureFormatObservation $formats (FormatSample 2)
    $result=Complete-CaptureFormatObservation $formats
    Check (!$result.stable) "A transient $change cannot disappear merely because the final sample recovers."
    Check ($result.first.sender.width -eq 2560 -and $result.last.sender.width -eq 2560) 'Format observations must retain independent bounded snapshots.'
}
Write-Output 'PASS bounded interval format observations reject transient adaptation, missing data, codec/profile changes and replacement workers'
