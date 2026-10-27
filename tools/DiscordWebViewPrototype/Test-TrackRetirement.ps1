param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path $candidate 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'A validation candidate is required.'}
if(Test-Path -LiteralPath $output){throw 'Choose a fresh isolated evidence directory.'}
New-Item -ItemType Directory -Path "$output/assets" | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/track-retirement.js","$PSScriptRoot/native-media.js" -Destination "$output/assets"
[IO.File]::WriteAllText("$output/assets/capture.html",'<!doctype html><meta charset="utf-8"><title>Synthetic track retirement</title><body>Testing generated media only.<script src="native-media.js"></script><script src="track-retirement.js"></script>')
$probe=$null
try{
    # Deliberately no --capture-probe-selftest: never selects or captures an application.
    $probe=Start-Process -FilePath $exe -ArgumentList ('"--discord-capture-probe='+$output+'/assets"') -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(70)
    $result=$null
    while([DateTime]::UtcNow -lt $deadline){
        if($probe.HasExited){throw "Synthetic track probe exited with $($probe.ExitCode)."}
        if(Test-Path -LiteralPath "$output/assets/capture-result.json"){
            try{$value=Get-Content -LiteralPath "$output/assets/capture-result.json" -Raw|ConvertFrom-Json;if($value.receiver.complete){$result=$value.receiver;break;}}
            catch [System.ArgumentException]{}
        }
        Start-Sleep -Milliseconds 200
    }
    if(!$result){throw 'Synthetic track probe timed out.'}
    $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$output/result.json"
    $result.results | Select-Object kind,method,queued,readers,@{n='states';e={$_.states -join ','}},@{n='events';e={$_.events -join ','}},retireMilliseconds | Format-Table -AutoSize
    if($result.error){throw 'Synthetic track probe reported an error.'}
    if(@($result.results).Count -ne 25){throw 'Synthetic track probe did not finish all comparisons.'}
    foreach($case in @($result.results|Where-Object { $_.method -in @('close','abort') })){
        if($case.retire.state -ne 'fulfilled' -or $case.states -contains 'live' -or ($case.events -join ',') -ne '1,1' -or
            ($case.eventsAfterRepeat -join ',') -ne '1,1' -or $case.written+$case.rejected -ne $case.queued){throw 'Generated source retirement did not settle exactly once.'}
    }
    $runtime=@($result.results|Where-Object kind -eq 'runtime')[0]
    if(($runtime.states -join ',') -ne 'ended,ended,ended,ended' -or ($runtime.events -join ',') -ne '1,1,1,1' -or
        ($runtime.eventsAfterRepeat -join ',') -ne '1,1,1,1'){throw 'Native media runtime suppressed source-ended notification.'}
}finally{
    if($probe){if(!$probe.HasExited){Stop-Process -Id $probe.Id};$probe.Dispose()}
}
