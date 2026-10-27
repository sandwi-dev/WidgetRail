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
& "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $output -Audio
$result=Join-Path $output 'result.json'
$process=Start-Process -FilePath $exe -ArgumentList @(('"--shell-config='+$output+'/shell.json"'),'--shell-no-controller',
    ('"--validate-discord-audio='+$result+'"')) -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Audio fixture exited with $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Audio fixture timed out. Evidence: $output"}
        Start-Sleep -Milliseconds 250
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    if(!$data.passed){throw $data.error}
    if($data.checks.Count -lt 25){throw 'Audio fixture did not complete its main and pin checks.'}
    $data.checks|ForEach-Object{Write-Output "PASS $_"}
    Write-Output "Evidence: $result"
} finally {
    if(!$process.HasExited){Stop-Process -Id $process.Id}
    $process.Dispose()
}
