[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {
    Join-Path $repository 'artifacts/verification/playnite-local-framework'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$published = Join-Path $output 'published'
# Keep the fixture's intermediate graph separate from the distributable plugin.
& dotnet build (Join-Path $repository 'tests/WidgetRail.Playnite.FrameworkFixture/WidgetRail.Playnite.FrameworkFixture.csproj') `
    -c $Configuration -o $published --artifacts-path (Join-Path $output 'build-graph') "-bl:$output/build-{}.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Playnite local Framework fixture build failed.' }
# This executable uses a WPF dispatcher and fake SDK/provider objects. It never
# opens the installed Playnite, reads its credentials, or changes a real game.
& (Join-Path $published 'WidgetRail.Playnite.FrameworkFixture.exe')
if ($LASTEXITCODE -ne 0) { throw 'Playnite local Framework fixture failed.' }
