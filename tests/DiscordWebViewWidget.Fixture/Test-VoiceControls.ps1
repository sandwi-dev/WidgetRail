param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PublishedPayload,
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Both','Minimum','Expanded')][string]$Surface='Both'
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Missing candidate: $exe"}
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated voice-controls results directory.'}
foreach($scenario in @(@{Name='minimum';Viewport='468x548';Selection='Minimum'},@{Name='expanded';Viewport='1088x868';Selection='Expanded'})){
    if($Surface -ne 'Both' -and $scenario.Selection -ne $Surface){continue}
    $scenarioOutput=Join-Path $output $scenario.Name
    & "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $scenarioOutput -VoiceControls
    $result=Join-Path $scenarioOutput 'result.json'
    $process=Start-Process -FilePath $exe -ArgumentList @(('"--shell-config='+$scenarioOutput+'/shell.json"'),'--shell-no-controller',
        ('--validation-viewport-dips='+$scenario.Viewport),('"--validate-discord-voice-controls='+$result+'"')) -WindowStyle Hidden -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(150)
        while(!(Test-Path -LiteralPath $result)){
            if($process.HasExited){throw "Voice controls fixture exited with $($process.ExitCode). Evidence: $scenarioOutput"}
            if([DateTime]::UtcNow -gt $deadline){throw "Voice controls fixture timed out. Evidence: $scenarioOutput"}
            Start-Sleep -Milliseconds 250
        }
        $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
        if(!$data.passed){throw $data.error}
        if($data.checks.Count -lt 30 -or $data.checks -notcontains 'Picker B restores the original main focus after saving and joining a favorite'){
            throw 'Voice controls fixture did not complete its dialog and favorite continuity checks.'
        }
        $data.checks|ForEach-Object{Write-Output "PASS [$($scenario.Name)] $_"}
        Write-Output "Evidence: $result"
    } finally {
        if(!$process.HasExited){Stop-Process -Id $process.Id}
        $process.Dispose()
    }
}
