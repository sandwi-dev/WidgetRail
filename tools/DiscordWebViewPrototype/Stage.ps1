param([string]$OutputDirectory,
    [ValidateSet('Auto','H264','VP8','VP9','AV1')][string]$ProbeCodec='Auto',
    [ValidateRange(1,50)][int]$ProbeBitrateMbps=8)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo 'artifacts/discord-webview/assets'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Copy-Item "$PSScriptRoot/capture.html","$PSScriptRoot/capture.js","$PSScriptRoot/capture-timing.js","$PSScriptRoot/capture-policy.js","$PSScriptRoot/native-media.js","$PSScriptRoot/webpack.js","$PSScriptRoot/discord-sender-stats.js","$PSScriptRoot/incoming.html","$PSScriptRoot/incoming.js" $OutputDirectory
$probePolicy=@{codec=$ProbeCodec;maximumBitrate=$ProbeBitrateMbps*1000000}|ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'capture-policy-config.js'),"globalThis.captureProbeConfiguration=$probePolicy;")
# Reuse the same hosted provider as the shipping widget.
$activities=[IO.File]::ReadAllText("$repo/samples/DiscordWebViewWidget/web/provider/activity-provider.js")
$adapterBuild=([IO.File]::ReadAllText("$repo/samples/DiscordWebViewWidget/web/provider/adapter-contract.json")|ConvertFrom-Json).build
if(($adapterBuild -isnot [long] -and $adapterBuild -isnot [int]) -or $adapterBuild -lt 1 -or $adapterBuild -gt [int]::MaxValue){throw 'Invalid Discord adapter build contract.'}
$adapter=[IO.File]::ReadAllText("$repo/samples/DiscordWebViewWidget/web/provider/adapter.js")
$adapter+="`ninitializeWidgetRailDiscordAdapter($adapterBuild);"
$adapter=$activities+"`n"+$adapter
$adapter += "`n" + [IO.File]::ReadAllText("$PSScriptRoot/discord-commands.js")
[IO.File]::WriteAllText("$OutputDirectory/discord-adapter.js",$adapter)
