param([Parameter(Mandatory)][string]$CandidateDirectory, [Parameter(Mandatory)][string]$NativeDirectory, [string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$native=[IO.Path]::GetFullPath($NativeDirectory)
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/cap-'+[guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh output/profile directory.'}
foreach($file in @("$candidate/OverlayFrontend.WinUI.exe","$native/WindowCaptureFixture.exe")){
    if(!(Test-Path -LiteralPath $file)){throw "Missing prerequisite $file"}
}
New-Item -ItemType Directory -Path $output|Out-Null
# Immutable test deployment: preserve candidate production worker binaries and
# add only the generated source executable. Never select an existing user app.
Copy-Item -LiteralPath $candidate -Destination "$output/candidate" -Recurse
Copy-Item -LiteralPath "$native/WindowCaptureFixture.exe" -Destination "$output/candidate"
$process=Start-Process -FilePath "$output/candidate/OverlayFrontend.WinUI.exe" -ArgumentList ('"--validate-web-session-capture='+$output+'"') -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while(!(Test-Path -LiteralPath "$output/result.json")){
        if($process.HasExited){throw "Capture session fixture exited: $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Capture session fixture timed out. Evidence: $output"}
        Start-Sleep -Milliseconds 250
    }
    $result=Get-Content -LiteralPath "$output/result.json" -Raw|ConvertFrom-Json
    if($result.error){throw $result.error}
    if($result.checks.Count -lt 20){throw 'Capture fixture did not complete its checklist, including runtime heartbeat retirement.'}
    $result.checks|ForEach-Object{Write-Output "PASS $_"}
    $result.samples|ConvertTo-Json|Write-Output
    Write-Output "Evidence: $output/result.json"
}finally{
    if(!$process.HasExited){Stop-Process -Id $process.Id}
    $process.Dispose()
    # The fixture owns its source process and stops it in finally. If its host
    # crashed, clean only an exact source executable within this test directory.
    Get-CimInstance Win32_Process -Filter "Name='WindowCaptureFixture.exe'" | Where-Object ExecutablePath -eq "$output\candidate\WindowCaptureFixture.exe" | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
}
