[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$Catalog,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'

$sampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $sampleRoot '..\..'))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\community-addons\sdk-gallery'
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
$widgetProject = Join-Path $sampleRoot 'SdkGalleryWidget.csproj'

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

function Assert-NoReparsePoint {
    param([string]$Path)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($resolved)
    if ([string]::IsNullOrWhiteSpace($root)) { throw "Path has no filesystem root: $resolved" }
    $current = $root
    foreach ($segment in $resolved.Substring($root.Length).Split(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $attributes = [System.IO.File]::GetAttributes($current)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Generated package paths cannot traverse a reparse point: $current"
            }
        }
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Assert-NoReparsePoint -Path $artifactsRoot
Assert-ChildPath -Parent $artifactsRoot -Child $stagingRoot
Assert-ChildPath -Parent $artifactsRoot -Child $publishRoot
foreach ($generatedDirectory in @($stagingRoot, $publishRoot)) {
    Assert-NoReparsePoint -Path $generatedDirectory
    if (Test-Path -LiteralPath $generatedDirectory) {
        Remove-Item -LiteralPath $generatedDirectory -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $payloadRoot, `
    (Join-Path $stagingRoot 'styles'), (Join-Path $stagingRoot 'assets\icons') | Out-Null
Assert-NoReparsePoint -Path $stagingRoot
Assert-NoReparsePoint -Path $publishRoot

& dotnet publish $widgetProject --configuration $Configuration --no-self-contained --nologo `
    --property:UseSharedCompilation=false --property:BuildInParallel=false --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "SDK Gallery publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $publishRoot 'SdkGalleryWidget.dll') `
    -Destination (Join-Path $payloadRoot 'SdkGalleryWidget.dll') -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingRoot 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.wrss') `
    -Destination (Join-Path $stagingRoot 'styles\default.wrss') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'assets\icons\gallery-mark.svg') `
    -Destination (Join-Path $stagingRoot 'assets\icons\gallery-mark.svg') -Force

$expectedFiles = @(
    'manifest.json',
    'assets\icons\gallery-mark.svg',
    'payload\SdkGalleryWidget.dll',
    'styles\default.wrss'
)
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | ForEach-Object {
    [System.IO.Path]::GetRelativePath($stagingRoot, $_.FullName)
})
$unexpectedFiles = @($stagedFiles | Where-Object { $_ -notin $expectedFiles })
$missingFiles = @($expectedFiles | Where-Object { $_ -notin $stagedFiles })
if ($unexpectedFiles.Count -ne 0 -or $missingFiles.Count -ne 0) {
    throw "Staged package allowlist mismatch. Unexpected: [$($unexpectedFiles -join ', ')]; missing: [$($missingFiles -join ', ')]."
}

if (Test-Path -LiteralPath $packagePath) {
    Assert-NoReparsePoint -Path $packagePath
    Remove-Item -LiteralPath $packagePath -Force
}

& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- validate $stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'wrail validate rejected the staged SDK Gallery addon.' }
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw 'wrail pack did not produce the SDK Gallery addon package.'
}

if ($Install) {
    $catalogArguments = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        @()
    } else {
        @('--catalog', [System.IO.Path]::GetFullPath($Catalog))
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        install $packagePath @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'wrail install failed. Installed versions are immutable; bump manifest.json before replacing one.'
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        enable $manifest.id @catalogArguments
    if ($LASTEXITCODE -ne 0) { throw "wrail enable failed for $($manifest.id)." }
}

Write-Host "Community addon package: $packagePath"
