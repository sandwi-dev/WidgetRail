param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PublishedPayload,
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Both','Minimum','Expanded')][string]$Surface='Both',
    [switch]$ComposerOnly,
    [switch]$DirectionalEntryOnly
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Missing candidate: $exe"}
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated Messages result directory.'}
foreach($scenario in @(@{Name='minimum';Viewport='468x548';Selection='Minimum';Width=420;Height=360},@{Name='expanded';Viewport='1088x868';Selection='Expanded';Width=1040;Height=680})){
    if($Surface -ne 'Both' -and $Surface -ne $scenario.Selection){continue}
    $scenarioOutput=Join-Path $output $scenario.Name
    & "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $scenarioOutput -Messages
    @{width=$scenario.Width;height=$scenario.Height}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $scenarioOutput 'expected-surface.json')
    $result=Join-Path $scenarioOutput 'result.json'
    $nativeArguments=@(( '"--shell-config='+$scenarioOutput+'/shell.json"'),'--shell-no-controller','--validation-platform-activation',
        ('--validation-viewport-dips='+$scenario.Viewport),('"--validate-discord-messages='+$result+'"'))
    if($ComposerOnly){$nativeArguments+='--discord-messages-composer-only'}
    if($DirectionalEntryOnly){$nativeArguments+='--discord-messages-directional-entry-only'}
    # This is an explicitly interactive native-input fixture. SW_HIDE can leave
    # its composition tree invisible even after WinUI reports loaded controls.
    $process=Start-Process -FilePath $exe -ArgumentList $nativeArguments -WindowStyle Normal -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(180)
        while(!(Test-Path -LiteralPath $result)){
            if($process.HasExited){throw "Messages fixture exited with $($process.ExitCode). Evidence: $scenarioOutput"}
            if([DateTime]::UtcNow -gt $deadline){throw "Messages fixture timed out. Evidence: $scenarioOutput"}
            Start-Sleep -Milliseconds 250
        }
        $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
        if(!$data.passed){throw $data.error}
        if($data.checks.Count -lt $(if($DirectionalEntryOnly){15}else{20})){throw 'Messages fixture did not complete the expected checks.'}
        $audit=Get-Content -LiteralPath "$scenarioOutput/messages-provider.json" -Raw|ConvertFrom-Json
        if($audit.mutationAttempts -ne 0){throw 'Messages fixture attempted an unsolicited mutation.'}
        $data.checks|ForEach-Object{Write-Output "PASS [$($scenario.Name)] $_"}
        Write-Output "Evidence: $result"
    } finally {
        if(!$process.HasExited){
            & "$PSScriptRoot/../../scripts/Close-WinUiTestShell.ps1" -AppPid $process.Id
            if(!$process.WaitForExit(30000)){Stop-Process -Id $process.Id;throw 'Messages fixture did not close normally.'}
        }
        @{exitCode=$process.ExitCode}|ConvertTo-Json|Set-Content "$scenarioOutput/shutdown.json"
        if($process.ExitCode -ne 0){throw "Messages fixture exited with $($process.ExitCode)."}
        $process.Dispose()
    }
}
