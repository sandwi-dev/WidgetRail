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
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated navigation results directory.'}
$scenarios=@(
    @{Name='minimum';Width=420;Height=360;Viewport='468x548';Selection='Minimum'},
    @{Name='expanded';Width=1040;Height=680;Viewport='1088x868';Selection='Expanded'}
)
foreach($scenario in $scenarios){
    if($Surface -ne 'Both' -and $scenario.Selection -ne $Surface){continue}
    $scenarioOutput=Join-Path $output $scenario.Name
    & "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $scenarioOutput
    @{width=$scenario.Width;height=$scenario.Height}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $scenarioOutput 'expected-surface.json')
    $result=Join-Path $scenarioOutput 'result.json'
    $process=Start-Process -FilePath $exe -ArgumentList @(('"--shell-config='+$scenarioOutput+'/shell.json"'),'--shell-no-controller',
        ('--validation-viewport-dips='+$scenario.Viewport),('"--validate-discord-navigation='+$result+'"')) -WindowStyle Hidden -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(180)
        while(!(Test-Path -LiteralPath $result)){
            if($process.HasExited){throw "Navigation fixture exited with $($process.ExitCode). Evidence: $scenarioOutput"}
            if([DateTime]::UtcNow -gt $deadline){throw "Navigation fixture timed out. Evidence: $scenarioOutput"}
            Start-Sleep -Milliseconds 250
        }
        $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
        if(!$data.passed){throw $data.error}
        if($data.checks.Count -lt 35){throw 'Navigation fixture did not complete all expected checks.'}
        $data.checks|ForEach-Object{Write-Output "PASS [$($scenario.Name)] $_"}
        Write-Output "Evidence: $result"
    } finally {
        if(!$process.HasExited){Stop-Process -Id $process.Id}
        $process.Dispose()
    }
}
