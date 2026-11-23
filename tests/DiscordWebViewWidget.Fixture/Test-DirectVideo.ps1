param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$CliDll,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$CompactLargeText
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated fixture directory.'}
$exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Missing validation frontend: $exe"}
New-Item -ItemType Directory "$output/profile","$output/catalog","$output/logs"|Out-Null
& dotnet $CliDll install $Package --catalog "$output/catalog" --settings-root "$output/profile" --accept-full-trust *> "$output/logs/install.log"
if($LASTEXITCODE){throw "Fixture installation failed: $output/logs/install.log"}
& dotnet $CliDll enable widgetrail.samples.discord-controls --catalog "$output/catalog" --accept-full-trust *> "$output/logs/enable.log"
if($LASTEXITCODE){throw "Fixture enable failed: $output/logs/enable.log"}
[IO.File]::WriteAllText("$output/profile/automatic-app-updates.txt",'disabled')
@{version=1;step='complete';paused=$false}|ConvertTo-Json|Set-Content "$output/profile/introduction-progress.json"
@{InstallationRoot=[IO.Path]::GetFullPath($InstallationRoot);SettingsRoot="$output/profile";InstalledCatalogRoot="$output/catalog";
    InitialWidgetId='widgetrail.samples.discord-controls'}|ConvertTo-Json|Set-Content "$output/shell.json"
$result=Join-Path $output 'result.json'
$arguments=@(
    ('--shell-config="'+$output+'/shell.json"'),'--shell-no-controller','--validation-platform-activation',
    ('--validate-discord-direct-video="'+$result+'"'))
if($CompactLargeText){
    @{schemaVersion=3;appearance=@{themeId='widgetrail.builtin.mono-ink';themeVersion='1.0.0';interfaceScale=1;textScale=1.5;backdropOpacity=.64;motion='Reduced';boldText=$true}}|
        ConvertTo-Json -Depth 4|Set-Content "$output/profile/platform-settings.json"
    $arguments+=@('--validation-viewport-dips=468x548','--discord-compact-stream-viewer')
}
$process=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList $arguments
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(180)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Direct-video fixture exited with $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Direct-video fixture timed out. Evidence: $output"}
        Start-Sleep -Milliseconds 250
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    if(!$data.passed){throw $data.error}
    if($data.hiddenViewerSubscription -ne $false){throw 'The unpinned viewer kept its Discord subscription while hidden.'}
    if($data.thirdReplacements -ne 3 -or $data.thirdLast-$data.thirdFirst -lt 60 -or $data.pinChoiceChecks -lt 11){throw 'Three-stream playback/replacement/pin availability checks did not complete.'}
    if (!$data.volumeConfirmed -or !$data.endedFocusRestored -or $data.survivingFrameDeltas.Count -ne 2 -or
        $data.survivingFrameDeltas[0] -lt 1 -or $data.survivingFrameDeltas[1] -lt 1) {
        throw 'Viewer volume confirmation, controller return or sibling playback was not observed.'
    }
    $data|ConvertTo-Json
    Write-Output "Evidence: $result"
} finally {
    if(!$process.HasExited){
        & "$PSScriptRoot/../../scripts/Close-WinUiTestShell.ps1" -AppPid $process.Id
        if(!$process.WaitForExit(30000)){Stop-Process -Id $process.Id;throw 'Direct-video fixture did not close normally.'}
    }
    @{exitCode=$process.ExitCode}|ConvertTo-Json|Set-Content "$output/shutdown.json"
    if($process.ExitCode -ne 0){throw "Direct-video fixture exited with $($process.ExitCode)."}
    $process.Dispose()
}
