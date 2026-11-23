param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PublishedPayload,
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Both','Minimum','Expanded')][string]$Surface='Both',
    [string]$ThemeId='widgetrail.builtin.mono-ink',
    [ValidateSet('Standard','High')][string]$Contrast='Standard',
    [ValidateRange(1,2)][double]$TextScale=1,
    [ValidateSet('Reduced','Full','System')][string]$Motion='Reduced',
    [switch]$SolidSurface,
    [switch]$ComposerOnly,
    [switch]$DirectionalEntryOnly,
    [switch]$SectionReturnOnly,
    [switch]$LargeConversations,
    [switch]$PrivateCalls,
    [switch]$MediaMessages,
    [switch]$PublicPortrait,
    [switch]$HistoryContinuityOnly,
    [switch]$HeldScrollOnly,
    [switch]$AdvanceCommitOnly,
    [switch]$ReverseWhileMoving,
    [switch]$FocusHistoryBeforeScrolling,
    [switch]$WheelHistoryScrolling,
    [switch]$HistoryLifecycleOnly,
    [switch]$PinnedReadOnly,
    [switch]$SentRevealOnly,
    [switch]$SuspendDuringPreparation,
    [ValidateRange(0,2000)][int]$HistoryDelayMilliseconds=100,
    [switch]$PointerMediaOpen,
    [switch]$PointerMediaClick
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path ([IO.Path]::GetFullPath($CandidateDirectory)) 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Missing candidate: $exe"}
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated Messages result directory.'}
if($LargeConversations -and $Surface -ne 'Expanded'){throw 'LargeConversations requires Surface Expanded.'}
if($LargeConversations -and $PrivateCalls){throw 'Choose one focused scenario.'}
if($MediaMessages -and ($LargeConversations -or $PrivateCalls -or $ComposerOnly -or $DirectionalEntryOnly -or $SectionReturnOnly)){throw 'Choose one focused scenario.'}
if($PointerMediaOpen -and !$MediaMessages){throw 'PointerMediaOpen requires MediaMessages.'}
if($PointerMediaClick -and !$MediaMessages){throw 'PointerMediaClick requires MediaMessages.'}
if($PublicPortrait -and (!$MediaMessages -or $Surface -ne 'Expanded')){throw 'PublicPortrait requires expanded MediaMessages.'}
if($ReverseWhileMoving -and !$HeldScrollOnly){throw 'ReverseWhileMoving requires HeldScrollOnly.'}
if($FocusHistoryBeforeScrolling -and !$HeldScrollOnly){throw 'FocusHistoryBeforeScrolling requires HeldScrollOnly.'}
if($WheelHistoryScrolling -and !$HeldScrollOnly){throw 'WheelHistoryScrolling requires HeldScrollOnly.'}
if($SuspendDuringPreparation -and !$HistoryLifecycleOnly){throw 'SuspendDuringPreparation requires HistoryLifecycleOnly.'}
foreach($scenario in @(@{Name='minimum';Viewport='468x548';Selection='Minimum';Width=420;Height=360},@{Name='expanded';Viewport='1088x868';Selection='Expanded';Width=1040;Height=680})){
    if($Surface -ne 'Both' -and $Surface -ne $scenario.Selection){continue}
    $scenarioOutput=Join-Path $output $scenario.Name
    & "$PSScriptRoot/Stage.ps1" -PublishedPayload $PublishedPayload -CliPath $CliPath -InstallationRoot $InstallationRoot -OutputDirectory $scenarioOutput -Messages -LargeConversations:$LargeConversations -PrivateCalls:$PrivateCalls -MediaMessages:$MediaMessages -RichConversations:$MediaMessages -PublicPortrait:$PublicPortrait -HeldScroll:($HeldScrollOnly -or $HistoryLifecycleOnly) -HistoryDelayMilliseconds $HistoryDelayMilliseconds -ThemeId $ThemeId -Contrast $Contrast -TextScale $TextScale -Motion $Motion -SolidSurface:$SolidSurface
    @{width=$scenario.Width;height=$scenario.Height}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $scenarioOutput 'expected-surface.json')
    $result=Join-Path $scenarioOutput 'result.json'
    $nativeArguments=@(( '"--shell-config='+$scenarioOutput+'/shell.json"'),'--shell-no-controller','--validation-platform-activation',
        ('--validation-viewport-dips='+$scenario.Viewport),('"--validate-discord-messages='+$result+'"'))
    if($ComposerOnly){$nativeArguments+='--discord-messages-composer-only'}
    if($SentRevealOnly){
        New-Item -ItemType File -Path (Join-Path $scenarioOutput 'generated-send') | Out-Null
        $nativeArguments+='--discord-sent-reveal-only'
    }
    if($PinnedReadOnly){
        New-Item -ItemType File -Path (Join-Path $scenarioOutput 'hold-history') | Out-Null
        $nativeArguments+='--discord-pinned-read-only'
    }
    if($DirectionalEntryOnly){$nativeArguments+='--discord-messages-directional-entry-only'}
    if($SectionReturnOnly){$nativeArguments+='--discord-messages-section-return-only'}
    if($LargeConversations){$nativeArguments+='--discord-large-conversations'}
    if($PrivateCalls){$nativeArguments+='--discord-private-calls'}
    if($MediaMessages){$nativeArguments+='--discord-media-messages'}
    if($PublicPortrait){$nativeArguments+='--discord-public-portrait'}
    if($HistoryContinuityOnly){$nativeArguments+='--discord-history-continuity-only'}
    if($HeldScrollOnly){$nativeArguments+='--discord-history-held-scroll-only'}
    if($AdvanceCommitOnly){$nativeArguments+='--discord-history-advance-commit-only'}
    if($ReverseWhileMoving){$nativeArguments+='--discord-history-held-scroll-reverse'}
    if($FocusHistoryBeforeScrolling){$nativeArguments+='--discord-history-held-scroll-focused-row'}
    if($WheelHistoryScrolling){$nativeArguments+='--discord-history-wheel-scroll'}
    if($HistoryLifecycleOnly){$nativeArguments+='--discord-history-lifecycle-only'}
    if($SuspendDuringPreparation){$nativeArguments+='--discord-history-suspend-preparation'}
    if($PointerMediaOpen){$nativeArguments+='--discord-media-pointer-open'}
    if($PointerMediaClick){$nativeArguments+='--discord-media-pointer-click'}
    if($SolidSurface){$nativeArguments+='--discord-solid-captures'}
    # The fixture raises its own window through the validated activation path.
    $process=Start-Process -FilePath $exe -ArgumentList $nativeArguments -WindowStyle Hidden -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(180)
        while(!(Test-Path -LiteralPath $result)){
            if($process.HasExited){throw "Messages fixture exited with $($process.ExitCode). Evidence: $scenarioOutput"}
            if([DateTime]::UtcNow -gt $deadline){throw "Messages fixture timed out. Evidence: $scenarioOutput"}
            Start-Sleep -Milliseconds 250
        }
        $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
        if(!$data.passed){throw $data.error}
        if($HeldScrollOnly -and (Test-Path -LiteralPath (Join-Path $scenarioOutput 'cursor-exceptions.jsonl'))){
            $cursorFailures=@(Get-Content -LiteralPath (Join-Path $scenarioOutput 'cursor-exceptions.jsonl')|ForEach-Object {$_|ConvertFrom-Json}|
                Where-Object {$_.type -notin @('System.OperationCanceledException','System.Threading.Tasks.TaskCanceledException')})
            if($cursorFailures.Count){throw "Held paging raised an unexpected cursor exception: $($cursorFailures[0].Message)"}
        }
        if($data.checks.Count -lt $(if($SentRevealOnly){8}elseif($PinnedReadOnly){9}elseif($HistoryLifecycleOnly){10}elseif($HeldScrollOnly){7}elseif($MediaMessages){13}elseif($PrivateCalls){16}elseif($DirectionalEntryOnly){15}elseif($HistoryContinuityOnly){15}else{20})){throw 'Messages fixture did not complete the expected checks.'}
        $audit=Get-Content -LiteralPath "$scenarioOutput/messages-provider.json" -Raw|ConvertFrom-Json
        if($audit.mutationAttempts -ne $(if($SentRevealOnly){1}else{0})){throw 'Unexpected number of synthetic message mutations.'}
        if($HeldScrollOnly){
            $olderPages=@($audit.history|Where-Object {$_.before}).Count
            $newerPages=@($audit.history|Where-Object {$_.after}).Count
            if($olderPages -lt 2 -or $newerPages -lt 1){
                throw "Held paging did not exercise bounded eviction and return paging: older=$olderPages newer=$newerPages"
            }
        }
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
