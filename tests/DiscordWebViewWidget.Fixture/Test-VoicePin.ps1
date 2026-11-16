param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PublishedPayload,
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Missing candidate: $exe"}
& "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $output -VoicePin
$result=Join-Path $output 'result.json'
$process=Start-Process -FilePath $exe -ArgumentList @(('"--shell-config='+$output+'/shell.json"'),'--shell-no-controller','--validation-platform-activation',
    ('"--validate-discord-voice-pin='+$result+'"')) -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Voice-pin fixture exited with $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Voice-pin fixture timed out. Evidence: $output"}
        Start-Sleep -Milliseconds 250
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    if(!$data.passed){throw $data.error}
    if($data.checks.Count -lt 40){throw 'Voice-pin fixture did not complete both sizes and native interaction checks.'}
    $data.checks|ForEach-Object{Write-Output "PASS $_"}
    Write-Output "Evidence: $result"
} finally {
        if(!$process.HasExited){
            & "$PSScriptRoot/../../scripts/Close-WinUiTestShell.ps1" -AppPid $process.Id
            if(!$process.WaitForExit(30000)){Stop-Process -Id $process.Id;throw 'Voice fixture did not close normally.'}
        }
        if($process.ExitCode -ne 0){throw "Voice fixture exited with $($process.ExitCode)."}
        @{exitCode=$process.ExitCode}|ConvertTo-Json|Set-Content "$output/shutdown.json"
    $process.Dispose()
}
