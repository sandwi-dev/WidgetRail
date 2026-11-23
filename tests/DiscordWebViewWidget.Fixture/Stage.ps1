param(
    [Parameter(Mandatory)][string]$PublishedPayload,
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ThemeId='widgetrail.builtin.mono-ink',
    [ValidateSet('Standard','High')][string]$Contrast='Standard',
    [ValidateRange(1,2)][double]$TextScale=1,
    [ValidateSet('Reduced','Full','System')][string]$Motion='Reduced',
    [switch]$Showcase,
    [switch]$SolidSurface,
    [switch]$Audio,
    [switch]$VoicePin,
    [switch]$VoiceControls,
    [switch]$ParticipantSpeaking,
    [switch]$Messages,
    [switch]$LargeConversations,
    [switch]$RichConversations,
    [switch]$PublicPortrait,
    [switch]$MediaMessages,
    [switch]$HeldScroll,
    [ValidateRange(0,2000)][int]$HistoryDelayMilliseconds=100,
    [switch]$PrivateCalls
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh isolated fixture directory.'}
$payload=[IO.Path]::GetFullPath($PublishedPayload)
if(!(Test-Path -LiteralPath "$payload/DiscordWebViewWidget.Fixture.exe")){throw 'Publish the synthetic fixture first.'}
$package=Join-Path $output 'package'
New-Item -ItemType Directory -Force $package,"$output/profile","$output/catalog","$output/logs"|Out-Null
Copy-Item -LiteralPath $payload -Destination "$package/payload" -Recurse
if($VoicePin){[IO.File]::WriteAllText("$package/payload/fixture-voice-pin.enabled",'Synthetic long channel and voice controls; no media or network.')}
if($VoiceControls){[IO.File]::WriteAllText("$package/payload/fixture-voice-controls.enabled",'Synthetic participant and device controls; no media, devices or network.')}
if($ParticipantSpeaking){
    if(!$VoiceControls){throw 'ParticipantSpeaking requires VoiceControls.'}
    [IO.File]::WriteAllText("$package/payload/fixture-participant-speaking.enabled",'Alternate one synthetic participant speaking state on provider reads.')
}
if($Messages){
    [IO.File]::WriteAllText("$package/payload/fixture-messages.enabled",[IO.Path]::GetFullPath("$output/messages-provider.json"))
}
if($LargeConversations){
    if(!$Messages){throw 'LargeConversations requires the Messages fixture.'}
    [IO.File]::WriteAllText("$package/payload/fixture-large-conversations.enabled",'120 synthetic conversations; no accounts or network.')
}
if($RichConversations){
    if(!$Messages -or $LargeConversations){throw 'RichConversations requires Messages without LargeConversations.'}
    [IO.File]::WriteAllText("$package/payload/fixture-rich-conversations.enabled",'Six synthetic rich rows; no account or network data.')
}
if($MediaMessages){
    if(!$Messages){throw 'MediaMessages requires Messages.'}
    [IO.File]::WriteAllText("$package/payload/fixture-media-messages.enabled",'Synthetic links and attachment metadata; no account data.')
}
if($PublicPortrait){
    if(!$RichConversations){throw 'PublicPortrait requires RichConversations.'}
    [IO.File]::WriteAllText("$package/payload/fixture-public-portrait.enabled",'Fetch the public Discord default avatar; no private account media.')
}
if($HeldScroll){[IO.File]::WriteAllText("$package/payload/fixture-held-scroll.enabled",'Variable-height short messages for sustained bidirectional pagination.')}
if($Messages){[IO.File]::WriteAllText("$package/payload/fixture-history-delay.txt",$HistoryDelayMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture))}
if($PrivateCalls){
    if(!$Messages){throw 'PrivateCalls requires the Messages fixture.'}
    [IO.File]::WriteAllText("$package/payload/fixture-private-calls.enabled",[IO.Path]::GetFullPath("$output/calls-provider.json"))
}
Copy-Item -LiteralPath "$repo/samples/DiscordWebViewWidget/assets","$repo/samples/DiscordWebViewWidget/styles" -Destination $package -Recurse
$manifest=Get-Content "$repo/samples/DiscordWebViewWidget/manifest.json" -Raw|ConvertFrom-Json -AsHashtable
$manifest.id='widgetrail.tests.discord-ui';$manifest.publisher='widgetrail.tests';$manifest.name='Discord UI fixture';$manifest.version='0.0.1'
if($Audio){
    $manifest.id='widgetrail.tests.discord-audio';$manifest.name='Discord audio fixture'
    [IO.File]::WriteAllText("$package/payload/fixture-audio.enabled",'Synthetic audio controls; no media or network.')
}
if($Showcase){$manifest.fullWidgetPinningSupported=$true}
$manifest.entrypoint.executable='payload/DiscordWebViewWidget.Fixture.exe';$manifest.optionalPermissions=@()
$manifest|ConvertTo-Json -Depth 12|Set-Content "$package/manifest.json"
& $CliPath validate $package *> "$output/logs/validate.log"
if($LASTEXITCODE){throw "Validation failed: $output/logs/validate.log"}
& $CliPath pack $package --output "$output/fixture.wrwidget" *> "$output/logs/pack.log"
if($LASTEXITCODE){throw "Packaging failed: $output/logs/pack.log"}
& $CliPath install "$output/fixture.wrwidget" --catalog "$output/catalog" --settings-root "$output/profile" --accept-full-trust *> "$output/logs/install.log"
if($LASTEXITCODE){throw "Install failed: $output/logs/install.log"}
& $CliPath enable $manifest.id --catalog "$output/catalog" --accept-full-trust *> "$output/logs/enable.log"
if($LASTEXITCODE){throw "Enable failed: $output/logs/enable.log"}
@{version=1;step='complete';paused=$false}|ConvertTo-Json|Set-Content "$output/profile/introduction-progress.json"
$appearance=@{themeId=$ThemeId;themeVersion='1.0.0';interfaceScale=1;textScale=$TextScale;backdropOpacity=.64;motion=$Motion;contrast=$Contrast;boldText=$true;transparency='Full'}
if($Showcase -or $SolidSurface){$appearance.widgetSurfaceAppearance='Solid'}
@{schemaVersion=3;appearance=$appearance}|
    ConvertTo-Json -Depth 5|Set-Content "$output/profile/platform-settings.json"
@{InstallationRoot=[IO.Path]::GetFullPath($InstallationRoot);SettingsRoot="$output/profile";InstalledCatalogRoot="$output/catalog";
    InitialWidgetId=$manifest.id;LayoutDiagnosticsPath="$output/layout.json"}|ConvertTo-Json|Set-Content "$output/shell.json"
Write-Output "$output/shell.json"
