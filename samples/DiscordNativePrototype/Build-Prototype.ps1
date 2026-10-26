[CmdletBinding()]
param([string]$Dotnet = 'C:/Program Files/dotnet/dotnet.exe', [string]$Cli)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/discord-prototype/build-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $run 'package'
New-Item -ItemType Directory -Force $stage | Out-Null
if (!$Cli) {
    $install = (Get-ItemProperty 'HKCU:/Software/WidgetRail/Installation').ApplicationRoot
    $Cli = Join-Path $install 'tools/wrail/wrail.exe'
}
& $Dotnet publish (Join-Path $PSScriptRoot 'DiscordNativePrototype.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $stage 'payload') "-bl:$run/publish-{}.binlog"
if ($LASTEXITCODE) { throw 'Prototype publish failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'web') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'src/WidgetSdk/WebSessionRuntime.js') -Destination (Join-Path $stage 'web/web-session.js')
& $Cli validate $stage
if ($LASTEXITCODE) { throw 'Prototype package validation failed' }
$package = Join-Path $run 'discord-native-prototype.wrwidget'
& $Cli pack $stage --output $package
if ($LASTEXITCODE) { throw 'Prototype pack failed' }
Write-Output $package
