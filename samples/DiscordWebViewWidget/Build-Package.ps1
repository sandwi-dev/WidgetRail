param([string]$OutputDirectory,[string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview-widget/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh output directory.'}
$package=Join-Path $output 'package'
New-Item -ItemType Directory -Force "$output/logs","$package/styles" | Out-Null
& dotnet publish "$PSScriptRoot/DiscordWebViewWidget.csproj" -c $Configuration -r win-x64 --self-contained false -m:1 -nr:false -p:UseSharedCompilation=false -p:CopyOutputSymbolsToPublishDirectory=false -o "$package/payload" "-bl:$output/logs/publish-{}.binlog"
if($LASTEXITCODE){throw 'Discord widget publication failed.'}
# Compose the widget-owned provider with the hosted discovery and session entry point.
Push-Location "$repo/tools/DiscordWebViewPrototype"
try {
    & npm ci --ignore-scripts --no-audit --no-fund
    if($LASTEXITCODE){throw 'Pinned backend build-tool restore failed.'}
} finally { Pop-Location }
& node "$repo/tools/DiscordWebViewPrototype/Build-Backend.mjs" "$package/payload/web/backend.js"
if($LASTEXITCODE){throw 'Sealed backend composition failed.'}
Copy-Item -LiteralPath "$PSScriptRoot/manifest.json" -Destination $package
Copy-Item -LiteralPath "$PSScriptRoot/styles/default.wrss" -Destination "$package/styles"
Copy-Item -LiteralPath "$PSScriptRoot/assets" -Destination "$package/assets" -Recurse
Write-Output "Package directory ready for wrail validate/pack: $package"
