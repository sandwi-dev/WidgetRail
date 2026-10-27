# Shared validation-runner helper. Returns only a process this invocation created.
function Start-SyntheticCaptureSource {
    param([string]$CandidateDirectory,[string]$NativeDirectory,[string]$OutputDirectory,
        [ValidateSet('Native','Browser')][string]$Kind='Native',[bool]$Audio=$false,[bool]$Static=$false,
        [int]$Width=0,[int]$Height=0)
    if(($Width -ne 0 -or $Height -ne 0) -and ($Width -lt 2 -or $Height -lt 2 -or $Width -gt 8192 -or $Height -gt 8192 -or [long]$Width*$Height -gt 33554432)){
        throw 'Explicit synthetic dimensions require a bounded 2..8192 pixel source.'
    }
    $sourceRoot=[IO.Path]::GetFullPath($OutputDirectory)
    if(Test-Path -LiteralPath $sourceRoot){throw 'Synthetic sources require a fresh output/profile directory.'}
    New-Item -ItemType Directory -Path $sourceRoot|Out-Null
    $source=$null
    try {
        if($Kind -eq 'Browser') {
            $stagedCandidate=[IO.Path]::GetFullPath($CandidateDirectory)
            $expectedCandidate=Join-Path ([IO.Directory]::GetParent($sourceRoot).FullName) 'candidate'
            if(![string]::Equals($stagedCandidate.TrimEnd('\','/'),$expectedCandidate.TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase)){
                throw 'The browser source must use the runner-owned candidate copy beside its fresh source directory.'
            }
            # The production task catalogue excludes OverlayFrontend.WinUI.exe.
            # Rename only this isolated staged apphost, keeping its embedded DLL
            # binding intact; do not relax production window admission policy.
            $browserSourceExe=Join-Path $CandidateDirectory 'WidgetRailSyntheticBrowser.exe'
            Copy-Item -LiteralPath (Join-Path $CandidateDirectory 'OverlayFrontend.WinUI.exe') -Destination $browserSourceExe -Force
            $arguments=@(('"--capture-browser-source='+$sourceRoot+'"'),
                ('"--settings-root='+$sourceRoot+'/settings"'),('"--installed-catalog-root='+$sourceRoot+'/catalog"'))
            if($Width -gt 0){$arguments+=('--capture-source-width='+$Width);$arguments+=('--capture-source-height='+$Height)}
            if($Audio){$arguments+='--capture-source-audio'}
            if($Static){$arguments+='--capture-source-static'}
            $source=Start-Process -FilePath $browserSourceExe -ArgumentList $arguments -WindowStyle Normal -PassThru
        } else {
            $arguments=@('--large')
            if($Width -gt 0){$arguments=@(('--width='+$Width),('--height='+$Height))}
            if($Audio){$arguments+='--audio'}
            if($Static){$arguments+='--static'}
            $source=Start-Process -FilePath (Join-Path $NativeDirectory 'WindowCaptureFixture.exe') -ArgumentList $arguments -WindowStyle Normal -PassThru
        }
        $created=$source.StartTime.ToUniversalTime().ToFileTimeUtc().ToString('X16')
        $identity=[pscustomobject]@{processId=$source.Id;processCreated=$created;sourceKind=$Kind;audio=$Audio;static=$Static;width=$Width;height=$Height}
        $identity|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $sourceRoot 'runner-identity.json')
        if($Kind -eq 'Browser') {
            $deadline=[DateTime]::UtcNow.AddSeconds(30)
            $ready=$null
            while($null -eq $ready) {
                if($source.HasExited){throw "Synthetic browser source exited: $($source.ExitCode)."}
                $readyPath=Join-Path $sourceRoot 'source-ready.json'
                if(Test-Path -LiteralPath $readyPath){try{$ready=Get-Content -LiteralPath $readyPath -Raw|ConvertFrom-Json}catch{}}
                if($null -ne $ready){break}
                if([DateTime]::UtcNow -gt $deadline){throw 'Synthetic browser source did not become ready.'}
                Start-Sleep -Milliseconds 100
            }
            if(!$ready.ready){throw "Synthetic browser source failed: $($ready.error)"}
            if($ready.processId -ne $source.Id -or $ready.processCreated -ne $created -or $ready.audio -ne $Audio){throw 'Synthetic browser source identity or audio admission changed.'}
            if($ready.geometry.Width -lt 2 -or $ready.geometry.Height -lt 2){throw 'Synthetic source did not report independent capturable bounds.'}
            $identity|Add-Member -NotePropertyName captureWidth -NotePropertyValue $ready.geometry.Width
            $identity|Add-Member -NotePropertyName captureHeight -NotePropertyValue $ready.geometry.Height
            $identity|Add-Member -NotePropertyName geometryMeasurement -NotePropertyValue $ready.geometry.Measurement
            $identity|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $sourceRoot 'runner-identity.json')
        }
        return [pscustomobject]@{Process=$source;Identity=$identity;Directory=$sourceRoot;
            Arguments=@(('--capture-probe-source-pid='+$source.Id),('--capture-probe-source-created='+$created))}
    } catch {
        if($source){if(!$source.HasExited){Stop-Process -Id $source.Id};$source.Dispose()}
        throw
    }
}

function Read-SyntheticSourceObservation {
    param($Source)
    $path=Join-Path $Source.Directory 'source-status.json'
    if(!(Test-Path -LiteralPath $path)){return $null}
    try {$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}catch [System.IO.IOException]{return $null}catch [System.ArgumentException]{return $null}
    if($value.processId -ne $Source.Identity.processId -or $value.processCreated -ne $Source.Identity.processCreated -or
        $value.stationary -or $value.source.visibility -ne 'visible' -or $value.source.sampleTime -le 0 -or $value.source.draws -lt 0){
        throw 'Synthetic source observation is not current visible animated content.'
    }
    if($value.geometry.Width -ne $Source.Identity.captureWidth -or $value.geometry.Height -ne $Source.Identity.captureHeight){
        throw 'Synthetic capturable geometry changed during measurement.'
    }
    return $value
}

function Measure-SyntheticSourceCadence {
    param($First,$Last)
    if(!$First -or !$Last){throw 'Missing independent source cadence observations.'}
    $seconds=($Last.source.sampleTime-$First.source.sampleTime)/1000.0
    if($seconds -le 0 -or $Last.source.draws -lt $First.source.draws){throw 'Invalid source cadence interval.'}
    return [pscustomobject]@{seconds=$seconds;draws=$Last.source.draws-$First.source.draws;framesPerSecond=($Last.source.draws-$First.source.draws)/$seconds;
        scope='Source renderer requestAnimationFrame canvas draws; not an independent compositor presentation measurement'}
}

function Assert-SyntheticSource60Fps {
    param($Source)
    if($Source.Identity.sourceKind -ne 'Browser' -or $Source.Identity.static){throw '60-FPS qualification requires the instrumented moving browser source.'}
    $first=$null;$last=$null;$samples=[Collections.Generic.List[object]]::new()
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    while([DateTime]::UtcNow -lt $deadline){
        if($Source.Process.HasExited){throw 'Synthetic source ended during its FPS preflight.'}
        $observed=Read-SyntheticSourceObservation $Source
        if($observed -and (!$last -or $observed.source.sampleTime -gt $last.source.sampleTime)){
            if(!$first){$first=$observed};$last=$observed;$samples.Add($observed)
            if($last.source.sampleTime-$first.source.sampleTime -ge 5000){break}
        }
        Start-Sleep -Milliseconds 100
    }
    $cadence=Measure-SyntheticSourceCadence $first $last
    # A 59.94-Hz display is a 60-FPS source. Permit small scheduling jitter, not
    # a lower-rate source which would invalidate capture/encoding comparisons.
    $passed=$cadence.seconds -ge 5 -and $cadence.framesPerSecond -ge 58.5 -and $cadence.framesPerSecond -le 61.5
    $result=[pscustomobject]@{passed=$passed;targetFps=60;minimumFps=58.5;maximumFps=61.5;cadence=$cadence;samples=$samples.ToArray()}
    $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Source.Directory 'fps-preflight.json')
    if(!$passed){throw "Source failed 60-FPS preflight ($($cadence.framesPerSecond) FPS); pipeline was not started."}
    return $result
}

function Stop-SyntheticCaptureSource {
    param($Source)
    if(!$Source){return}
    $process=$Source.Process
    try {
        if(!$process.HasExited){[void]$process.CloseMainWindow();if(!$process.WaitForExit(4000)){Stop-Process -Id $process.Id}}
    } catch [InvalidOperationException] { } finally {$process.Dispose()}
}
