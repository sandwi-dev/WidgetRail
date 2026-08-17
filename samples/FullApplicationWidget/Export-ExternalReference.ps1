[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Wrail,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$wrailPath = [System.IO.Path]::GetFullPath($Wrail)
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (-not [System.IO.File]::Exists($wrailPath)) {
    throw "wrail executable was not found: $wrailPath"
}
if ([System.IO.Directory]::Exists($outputPath) -or [System.IO.File]::Exists($outputPath)) {
    throw "Output already exists: $outputPath"
}

& $wrailPath new widget ExternalFullApplication `
    --output $outputPath `
    --id dev.external.full-application `
    --publisher dev.external `
    --template multipage
if ($LASTEXITCODE -ne 0) {
    throw "wrail new failed with exit code $LASTEXITCODE."
}

$sampleRoot = $PSScriptRoot
$sourceRoot = Join-Path $outputPath 'src'
Remove-Item -LiteralPath (Join-Path $sourceRoot 'ExternalFullApplication.cs')
Copy-Item -LiteralPath (Join-Path $sampleRoot 'FullApplicationReferenceWidget.cs') -Destination $sourceRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot 'ReferenceLibrary.cs') -Destination $sourceRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.wrss') `
    -Destination (Join-Path $outputPath 'styles\default.wrss') -Force

@'
{
  "manifestVersion": 1,
  "id": "dev.external.full-application",
  "publisher": "dev.external",
  "name": "External Full Application",
  "version": "0.1.0",
  "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
  "entrypoint": {
    "runtime": "dotnet-worker",
    "assembly": "payload/ExternalFullApplication.dll",
    "type": "WidgetRail.Samples.FullApplicationWidget.FullApplicationReferenceWidget"
  },
  "presentation": { "icon": "settings" },
  "permissions": [],
  "optionalPermissions": [],
  "residencyPolicy": { "schemaVersion": 1, "mode": "unload-after-idle", "idleSeconds": 300 },
  "resourceRequest": { "updateHz": 4 },
  "architectures": ["x64", "arm64"]
}
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'manifest.json') -Encoding utf8

@'
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.FullApplicationWidget;

public static class ExternalScenarios
{
    public static WidgetScenarioDefinition Ready() => new(
        new FullApplicationReferenceWidget(),
        new WidgetTestHostServicesBuilder().Build());
}
'@ | Set-Content -LiteralPath (Join-Path $sourceRoot 'ExternalScenarios.cs') -Encoding utf8

@'
{
  "version": 1,
  "assembly": "bin/Release/net8.0/ExternalFullApplication.dll",
  "providerType": "WidgetRail.Samples.FullApplicationWidget.ExternalScenarios",
  "scenarios": [
    { "name": "ready", "factory": "Ready", "description": "Bounded application-scale library" }
  ]
}
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'widgetrail.scenarios.json') -Encoding utf8

Write-Host "Exported the self-contained Full Application reference to $outputPath"
