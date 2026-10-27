# Requires a newly published validation candidate with ExerciseRendererStallsAsync promoted and its opt-in route wired.
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path $candidate 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $candidate 'WindowCaptureFixture.exe'))){throw 'Stage a validation candidate and the generated source fixture first.'}
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
$app=$null
try{
    $app=Start-Process -FilePath $exe -ArgumentList @(('"--validate-web-session-capture='+$output+'"'),'--capture-renderer-stall-matrix') -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(100)
    while(!(Test-Path -LiteralPath "$output/result.json")){
        if($app.HasExited){throw "Stall fixture exited with code $($app.ExitCode)."}
        if([DateTime]::UtcNow -gt $deadline){throw 'Stall fixture timed out; inspect the isolated evidence directory.'}
        Start-Sleep -Milliseconds 250
    }
    $result=Get-Content -LiteralPath "$output/result.json" -Raw|ConvertFrom-Json
    if($result.error){throw $result.error}
    $cases=@($result.samples|Where-Object phase -eq 'renderer_stall')
    if($cases.Count -ne 4){throw 'Candidate does not contain the opt-in renderer-stall matrix.'}
    $cases|Format-Table audio,requestedStallMs,observedStallMs,maximumUiGapMs,hostResponsive,outcome,recoveryVideoFps,recoveredAudioFrames,terminalReason,nativeResult,workerExited -AutoSize
    Write-Output "Recorded four policy observations in $output/result.json"
}finally{
    if($app){if(!$app.HasExited){Stop-Process -Id $app.Id};$app.Dispose()}
    # A host crash/timeout may bypass its managed finally. Clean only the exact
    # generated source incarnation recorded by this fresh fixture run.
    if(Test-Path -LiteralPath "$output/source-process.json"){
        $identity=Get-Content -LiteralPath "$output/source-process.json" -Raw|ConvertFrom-Json
        $child=Get-Process -Id $identity.pid -ErrorAction SilentlyContinue
        if($child){
            try{
                $null=$child.Handle
                if($child.StartTime.ToUniversalTime().Ticks -eq [long]$identity.createdUtcTicks -and
                    [string]::Equals($child.Path,(Join-Path $candidate 'WindowCaptureFixture.exe'),[StringComparison]::OrdinalIgnoreCase)){
                    $child.Kill();$child.WaitForExit(2000)|Out-Null
                }
            }finally{$child.Dispose()}
        }
    }
}
