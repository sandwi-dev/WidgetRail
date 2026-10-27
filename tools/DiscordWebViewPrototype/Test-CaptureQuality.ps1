param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$NativeDirectory,
    [string]$OutputDirectory,
    [ValidateRange(8,30)][int]$MeasureSeconds=12,
    [ValidateSet('480p15','480p30','480p60','720p15','720p30','720p60','1080p15','1080p30','1080p60','1440p15','1440p30','1440p60')][string[]]$Presets=@('480p15','480p30','720p15','720p30'),
    [int]$SourceWidth=0,[int]$SourceHeight=0
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$native=[IO.Path]::GetFullPath($NativeDirectory)
foreach($file in @((Join-Path $candidate 'OverlayFrontend.WinUI.exe'),(Join-Path $native 'WebRtcCapturePrototype.dll'),(Join-Path $native 'WindowCaptureFixture.exe'))){
    if(!(Test-Path -LiteralPath $file)){throw "Build a matching validation candidate and native capture first: $file"}
}
if(Get-Process -Name WindowCaptureFixture -ErrorAction SilentlyContinue){throw 'Close the other synthetic capture fixture before this isolated test.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/quality-runs/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
Copy-Item -LiteralPath $candidate -Destination "$output/candidate" -Recurse
Copy-Item -LiteralPath "$native/WebRtcCapturePrototype.dll" -Destination "$output/candidate"
$results=[Collections.Generic.List[object]]::new()
$fixture=$null
$probe=$null
function Read-Sample([string]$Path){
    # Statistics are diagnostic files written once a second; tolerate an in-progress write.
    for($attempt=0;$attempt -lt 10;$attempt++){
        try { if(Test-Path -LiteralPath $Path){return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json} }
        catch { }
        Start-Sleep -Milliseconds 100
    }
    return $null
}
try{
    # This deliberately visible source renders generated graphics only. No user window is selected.
    $sourceArgs=@('--large')
    if($SourceWidth -ne 0 -or $SourceHeight -ne 0){
        if($SourceWidth -lt 2 -or $SourceHeight -lt 2 -or $SourceWidth -gt 8192 -or $SourceHeight -gt 8192 -or [long]$SourceWidth*$SourceHeight -gt 33554432){throw 'Invalid synthetic source dimensions.'}
        $sourceArgs=@(('--width='+$SourceWidth),('--height='+$SourceHeight))
    }
    $fixture=Start-Process -FilePath "$native/WindowCaptureFixture.exe" -ArgumentList $sourceArgs -WindowStyle Normal -PassThru
    $sourceIdentity=@(('--capture-probe-source-pid='+$fixture.Id),('--capture-probe-source-created='+$fixture.StartTime.ToUniversalTime().ToFileTimeUtc().ToString('X16')))
    foreach($preset in $Presets){
        $assets=Join-Path $output $preset
        & "$PSScriptRoot/Stage.ps1" -OutputDirectory $assets
        $statistics=Join-Path $assets 'capture-result.json'
        $probe=Start-Process -FilePath "$output/candidate/OverlayFrontend.WinUI.exe" -ArgumentList (@(('"--discord-capture-probe='+$assets+'"'),'--capture-probe-selftest',('--capture-probe-quality='+$preset))+$sourceIdentity) -WindowStyle Hidden -PassThru
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do{
            if($probe.HasExited -or $fixture.HasExited){throw "Synthetic capture exited while starting $preset"}
            $sample=Read-Sample $statistics
            if($sample -and $sample.capture.running -and $sample.receiver.connection -eq 'connected' -and $sample.receiver.frames -ge 30){break}
            if([DateTime]::UtcNow -gt $deadline){throw "Synthetic capture did not become ready: $preset"}
            Start-Sleep -Milliseconds 250
        }while($true)
        $begin=$sample
        $started=[Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Seconds $MeasureSeconds
        $end=Read-Sample $statistics
        $elapsed=$started.Elapsed.TotalSeconds
        if(!$end -or !$end.capture.running -or $end.receiver.connection -ne 'connected'){throw "Synthetic capture stopped during $preset"}
        $target=[int]($preset -split 'p')[1]
        $height=[int]($preset -split 'p')[0]
        $widthLimit=@{480=1706;720=2560;1080=3840;1440=5120}[$height]
        $sent=($end.receiver.frames-$begin.receiver.frames)/$elapsed
        $firstVideo=@($begin.receiver.inbound|Where-Object kind -eq 'video')
        $lastVideo=@($end.receiver.inbound|Where-Object kind -eq 'video')
        if($firstVideo.Count -ne 1 -or $lastVideo.Count -ne 1){throw "Missing decoded video statistics for $preset"}
        $decoded=($lastVideo[0].framesDecoded-$firstVideo[0].framesDecoded)/$elapsed
        $passed=$end.capture.quality -eq $preset -and $end.receiver.quality -eq $preset -and
            $end.capture.targetFrameRate -eq $target -and $end.receiver.targetFrameRate -eq $target -and
            # The shared geometry helper rounds down to even pixels. Floating
            # scale arithmetic plus alignment can reduce the ceiling by two.
            $end.receiver.width -le $widthLimit -and $end.receiver.height -le $height -and $end.receiver.height -ge $height-2 -and
            $sent -ge $target*0.85 -and $sent -le $target*1.1 -and $decoded -ge $target*0.85 -and $decoded -le $target*1.1
        $entry=[pscustomobject]@{preset=$preset;passed=$passed;seconds=$elapsed;submittedFps=$sent;decodedFps=$decoded;width=$end.receiver.width;height=$end.receiver.height;nativeResult=$end.capture.result;reason=$end.capture.reason;measurementDrops=$end.capture.dropped-$begin.capture.dropped;totalDrops=$end.capture.dropped;scope='Local synthetic window -> WGC -> generated track -> local WebRTC receiver; no Discord/account/audio'}
        $results.Add($entry)
        $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$output/results.json"
        $entry | ConvertTo-Json -Compress | Write-Output
        if(!$passed){throw "Quality verification failed for $preset; see $output/results.json"}
        Stop-Process -Id $probe.Id
        $probe.Dispose();$probe=$null
    }
    Write-Output "Evidence: $output/results.json"
}finally{
    if($probe){if(!$probe.HasExited){Stop-Process -Id $probe.Id};$probe.Dispose()}
    if($fixture){if(!$fixture.HasExited){Stop-Process -Id $fixture.Id};$fixture.Dispose()}
}
