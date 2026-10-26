param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo 'artifacts/discord-webview/assets'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Copy-Item "$PSScriptRoot/capture.html","$PSScriptRoot/capture.js","$PSScriptRoot/native-media.js","$PSScriptRoot/webpack.js","$PSScriptRoot/incoming.html","$PSScriptRoot/incoming.js" $OutputDirectory
# Reuse the tested domain adapter; only its module-discovery implementation changes.
$adapter=[IO.File]::ReadAllText("$repo/samples/DiscordNativePrototype/adapter.js").Replace('globalThis.Vencord?.Webpack','globalThis.WidgetRailWebWebpack')
$adapter += "`n" + [IO.File]::ReadAllText("$PSScriptRoot/discord-commands.js")
[IO.File]::WriteAllText("$OutputDirectory/discord-adapter.js",$adapter)
