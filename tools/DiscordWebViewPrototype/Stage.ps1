param([string]$OutputDirectory,
    [ValidateSet('Auto','H264','VP8','VP9','AV1')][string]$ProbeCodec='Auto',
    [ValidateRange(1,50)][int]$ProbeBitrateMbps=8,
    [ValidateSet('Auto','42001f','42e01f')][string]$ProbeH264Profile='Auto')
$ErrorActionPreference='Stop'
if($ProbeH264Profile -ne 'Auto' -and $ProbeCodec -ne 'H264'){throw 'An explicit H264 profile requires the H264 codec.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo 'artifacts/discord-webview/assets'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Copy-Item "$PSScriptRoot/capture.html","$PSScriptRoot/capture.js","$PSScriptRoot/capture-timing.js","$PSScriptRoot/capture-policy.js","$PSScriptRoot/native-media.js","$PSScriptRoot/webpack.js","$PSScriptRoot/discord-sender-stats.js","$PSScriptRoot/incoming.html","$PSScriptRoot/incoming.js" $OutputDirectory
$probePolicy=@{codec=$ProbeCodec;maximumBitrate=$ProbeBitrateMbps*1000000;h264Profile=$ProbeH264Profile}|ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'capture-policy-config.js'),"globalThis.captureProbeConfiguration=$probePolicy;")
# Reuse the tested domain adapter; only its module-discovery implementation changes.
$activities=[IO.File]::ReadAllText("$repo/samples/DiscordNativePrototype/activity-provider.js")
$adapterBuild=([IO.File]::ReadAllText("$repo/samples/DiscordNativePrototype/adapter-contract.json")|ConvertFrom-Json).build
if(($adapterBuild -isnot [long] -and $adapterBuild -isnot [int]) -or $adapterBuild -lt 1 -or $adapterBuild -gt [int]::MaxValue){throw 'Invalid Discord adapter build contract.'}
$adapter=[IO.File]::ReadAllText("$repo/samples/DiscordNativePrototype/adapter.js").Replace('globalThis.Vencord?.Webpack','globalThis.WidgetRailWebWebpack')
$adapter+="`ninitializeWidgetRailDiscordAdapter($adapterBuild);"
$adapter=$activities+"`n"+$adapter
$adapter += "`n" + [IO.File]::ReadAllText("$PSScriptRoot/discord-commands.js")
[IO.File]::WriteAllText("$OutputDirectory/discord-adapter.js",$adapter)
