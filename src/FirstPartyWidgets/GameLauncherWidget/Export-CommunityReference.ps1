[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Gbar,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$gbarPath = [System.IO.Path]::GetFullPath($Gbar)
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (-not [System.IO.File]::Exists($gbarPath)) {
    throw "gbar executable was not found: $gbarPath"
}
if ([System.IO.Directory]::Exists($outputPath) -or [System.IO.File]::Exists($outputPath)) {
    throw "Output already exists: $outputPath"
}

& $gbarPath new widget GameLauncherCommunity `
    --output $outputPath `
    --id org.gbar.community.reference.game-launcher `
    --publisher org.gbar.community.reference `
    --template multipage
if ($LASTEXITCODE -ne 0) {
    throw "gbar new failed with exit code $LASTEXITCODE."
}

$sourceRoot = Join-Path $outputPath 'src'
Remove-Item -LiteralPath (Join-Path $sourceRoot 'GameLauncherCommunity.cs')
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' |
    Where-Object { $_.Name -ne 'AssemblyInfo.cs' } |
    Copy-Item -Destination $sourceRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'styles\default.gbss') `
    -Destination (Join-Path $outputPath 'styles\default.gbss') -Force

@'
{
  "manifestVersion": 1,
  "id": "org.gbar.community.reference.game-launcher",
  "publisher": "org.gbar.community.reference",
  "name": "Game Launcher Community Reference",
  "version": "0.1.0",
  "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
  "entrypoint": {
    "runtime": "dotnet-worker",
    "assembly": "payload/GameLauncherCommunity.dll",
    "type": "GameBarAlternative.FirstPartyWidgets.GameLauncher.GameLauncherWidget"
  },
  "presentation": { "icon": "play" },
  "permissions": [ "system.apps.library.read.v1" ],
  "optionalPermissions": [
    "system.apps.library.launch.v1",
    "system.apps.running.read.v1"
  ],
  "residencyPolicy": {
    "schemaVersion": 1,
    "mode": "unload-after-idle",
    "idleSeconds": 120
  },
  "resourceRequest": { "memoryMb": 48, "updateHz": 4 },
  "architectures": [ "x64" ]
}
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'manifest.json') -Encoding utf8

@'
# Game Launcher Community Reference

This self-contained reference consumes only the packaged public Widget SDK. It
keeps durable SavedIds and sanitized display state; launch always re-resolves an
exact current opaque AppId through the declared app-library capabilities.
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'README.md') -Encoding utf8

Write-Host "Exported the self-contained Game Launcher Community reference to $outputPath"
