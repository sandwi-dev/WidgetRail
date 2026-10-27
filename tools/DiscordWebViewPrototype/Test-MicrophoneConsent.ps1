param([Parameter(Mandatory)][string]$CandidateDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$exe=Join-Path $candidate 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build a validation-enabled frontend candidate first.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/microphone-runs/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'A fresh isolated output/profile directory is required.'}
New-Item -ItemType Directory -Path $output|Out-Null
$result=Join-Path $output 'result.json'
$process=Start-Process -FilePath $exe -ArgumentList ('"--validate-web-session-microphone='+$output+'"') -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Fixture exited with $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Microphone fixture timed out. Stage history: $output/microphone-progress.json"}
        Start-Sleep -Milliseconds 250
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    if($data.error){throw $data.error}
    if($data.checks.Count -lt 40){throw 'Microphone, capture chooser and document lifetime fixture did not execute its full checklist.'}
    $data.checks|ForEach-Object{Write-Output "PASS $_"}
    Write-Output "Evidence: $result"
}finally{
    if(!$process.HasExited){Stop-Process -Id $process.Id}
    $process.Dispose()
}
