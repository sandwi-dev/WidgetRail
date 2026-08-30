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

$widgetRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $widgetRoot '..\..'))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\community-addons\playnite-library'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = Join-Path $artifactsRoot 'package-root'
$payloadRoot = Join-Path $stagingRoot 'payload'
$publishRoot = Join-Path $artifactsRoot 'application-publish'
$buildGraphRoot = Join-Path $artifactsRoot 'build-graph'
$manifestPath = Join-Path $widgetRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).wrwidget"
$applicationProject = Join-Path $widgetRoot 'Application\PlayniteLibraryApplication.csproj'
$cliProject = Join-Path $repositoryRoot 'tools\WrailCli\WrailCli.csproj'
$deterministicPathMap = "$buildGraphRoot=/_/build%2C$repositoryRoot=/_/"

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $childPath = [System.IO.Path]::GetFullPath($Child)
    if (-not $childPath.StartsWith(
        $parentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside $Parent`: $childPath"
    }
}

function Assert-NoReparsePoint {
    param([string]$Path)
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($resolved)
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
Assert-NoReparsePoint $artifactsRoot
foreach ($generated in @($stagingRoot, $publishRoot, $buildGraphRoot)) {
    Assert-ChildPath $artifactsRoot $generated
    Assert-NoReparsePoint $generated
    if (Test-Path -LiteralPath $generated) {
        Remove-Item -LiteralPath $generated -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $payloadRoot,
    (Join-Path $stagingRoot 'styles') | Out-Null

& dotnet publish $applicationProject `
    --configuration $Configuration `
    --no-self-contained `
    --nologo `
    --property:ContinuousIntegrationBuild=true `
    --property:PathMap=$deterministicPathMap `
    --property:UseSharedCompilation=false `
    --property:BuildInParallel=false `
    --artifacts-path $buildGraphRoot `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Playnite Library application publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Where-Object {
    $_.Extension -notin @('.pdb', '.xml')
} | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName)
    $destination = Join-Path $payloadRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) |
        Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
Copy-Item -LiteralPath $manifestPath `
    -Destination (Join-Path $stagingRoot 'manifest.json')
Copy-Item -LiteralPath (Join-Path $widgetRoot 'styles\default.wrss') `
    -Destination (Join-Path $stagingRoot 'styles\default.wrss')

$stagingPrefix = $stagingRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse |
    ForEach-Object { $_.FullName.Substring($stagingPrefix.Length) })
$required = @(
    'manifest.json',
    'payload\PlayniteLibraryApplication.exe',
    'payload\PlayniteLibraryApplication.dll',
    'payload\PlayniteLibraryWidget.dll',
    'payload\WidgetApplicationRuntime.dll',
    'payload\WidgetSdk.dll',
    'payload\WidgetProtocol.dll',
    'payload\Microsoft.Windows.SDK.NET.dll',
    'styles\default.wrss'
)
$missing = @($required | Where-Object { $_ -notin $stagedFiles })
if ($missing.Count -ne 0) {
    throw "Staged Playnite Library application is incomplete: [$($missing -join ', ')]."
}
$forbidden = @($stagedFiles | Where-Object {
    $_ -match '(^|\\)(PlatformBroker|WindowsAppLibraryProvider|PlatformSettings)\.dll$' -or
    $_ -match '\.(pdb|xml)$'
})
if ($forbidden.Count -ne 0) {
    throw "Staged Playnite Library contains product-owned/debug files: [$($forbidden -join ', ')]."
}

if (Test-Path -LiteralPath $packagePath) {
    Assert-NoReparsePoint $packagePath
    Remove-Item -LiteralPath $packagePath -Force
}
& dotnet run --project $cliProject --configuration $Configuration `
    --no-launch-profile --property:UseSharedCompilation=false `
    --property:BuildInParallel=false -- validate $stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'wrail validate rejected Playnite Library.' }
& dotnet run --project $cliProject --configuration $Configuration `
    --no-launch-profile --property:UseSharedCompilation=false `
    --property:BuildInParallel=false -- pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw 'wrail pack did not produce Playnite Library.'
}

if ($Install) {
    $catalogArguments = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        @()
    } else {
        @('--catalog', [System.IO.Path]::GetFullPath($Catalog))
    }
    & dotnet run --project $cliProject --configuration $Configuration `
        --no-launch-profile -- install $packagePath --accept-full-trust @catalogArguments
    if ($LASTEXITCODE -ne 0) { throw 'Playnite Library install failed.' }
    & dotnet run --project $cliProject --configuration $Configuration `
        --no-launch-profile -- version select $manifest.id $manifest.version @catalogArguments
    if ($LASTEXITCODE -ne 0) { throw 'Playnite Library version selection failed.' }
    & dotnet run --project $cliProject --configuration $Configuration `
        --no-launch-profile -- enable $manifest.id --accept-full-trust @catalogArguments
    if ($LASTEXITCODE -ne 0) { throw 'Playnite Library enable failed.' }
}

Write-Host "Community Playnite Library package: $packagePath"
