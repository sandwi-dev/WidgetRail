param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$exe=Join-Path $candidate 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'A validation-enabled candidate is required.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/backend-runs/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory so previous results cannot count as a pass.'}
New-Item -ItemType Directory -Path $output | Out-Null
$result=Join-Path $output 'result.json'
$process=Start-Process -FilePath $exe -ArgumentList ('"--validate-web-session-backend='+$output+'"') -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Fixture exited with code $($process.ExitCode) before writing results."}
        if([DateTime]::UtcNow -gt $deadline){throw "Fixture timed out. Evidence directory: $output"}
        Start-Sleep -Milliseconds 250
    }
    # The fixture flushes this file only after browser teardown completes.
    $data=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if($data.error){throw $data.error}
    if($data.checks.Count -lt 15){throw 'Fixture did not execute the expected backend and isolated-adapter checks.'}
    $data.checks | ForEach-Object { Write-Output "PASS $_" }
    Write-Output "Evidence: $result"
}finally{
    # Only the synthetic process created above; never touch the user's prototype.
    if(!$process.HasExited){Stop-Process -Id $process.Id}
    $process.Dispose()
}
