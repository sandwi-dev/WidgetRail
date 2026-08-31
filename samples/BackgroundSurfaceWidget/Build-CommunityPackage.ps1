[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'

$sampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $sampleRoot '..\..'))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\community-addons\background-surface-test'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = Join-Path $artifactsRoot 'package-root'
$publishRoot = Join-Path $artifactsRoot 'publish'
$payloadRoot = Join-Path $stagingRoot 'payload'
$manifestPath = Join-Path $sampleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).wrwidget"
$cliProject = Join-Path $repositoryRoot 'tools\WrailCli\WrailCli.csproj'
$widgetProject = Join-Path $sampleRoot 'BackgroundSurfaceWidget.csproj'

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)
    $resolvedParent = [System.IO.Path]::GetFullPath($Parent)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    if (-not $resolvedChild.StartsWith(
        $resolvedParent + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside $resolvedParent`: $resolvedChild"
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Assert-ChildPath -Parent $artifactsRoot -Child $stagingRoot
Assert-ChildPath -Parent $artifactsRoot -Child $publishRoot
foreach ($generatedDirectory in @($stagingRoot, $publishRoot)) {
    if (Test-Path -LiteralPath $generatedDirectory) {
        Remove-Item -LiteralPath $generatedDirectory -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $payloadRoot, (Join-Path $stagingRoot 'styles') | Out-Null

& dotnet publish $widgetProject --configuration $Configuration --no-self-contained --nologo `
    --property:UseSharedCompilation=false --property:BuildInParallel=false --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "BackgroundSurface test publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $publishRoot 'BackgroundSurfaceWidget.dll') `
    -Destination (Join-Path $payloadRoot 'BackgroundSurfaceWidget.dll') -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingRoot 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.wrss') `
    -Destination (Join-Path $stagingRoot 'styles\default.wrss') -Force

$expectedFiles = @('manifest.json', 'payload\BackgroundSurfaceWidget.dll', 'styles\default.wrss')
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | ForEach-Object {
    [System.IO.Path]::GetRelativePath($stagingRoot, $_.FullName)
})
$unexpectedFiles = @($stagedFiles | Where-Object { $_ -notin $expectedFiles })
$missingFiles = @($expectedFiles | Where-Object { $_ -notin $stagedFiles })
if ($unexpectedFiles.Count -ne 0 -or $missingFiles.Count -ne 0) {
    throw "Staged package allowlist mismatch. Unexpected: [$($unexpectedFiles -join ', ')]; missing: [$($missingFiles -join ', ')]."
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- validate $stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'wrail validate rejected the BackgroundSurface test package.' }
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw 'wrail pack did not produce the BackgroundSurface test package.'
}

Write-Host "BackgroundSurface test package: $packagePath"
