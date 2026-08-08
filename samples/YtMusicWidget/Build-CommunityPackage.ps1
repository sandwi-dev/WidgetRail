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

$sampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $sampleRoot '..\..'))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\community-addons\ytmusic'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = Join-Path $artifactsRoot 'package-root'
$payloadRoot = Join-Path $stagingRoot 'payload'
$manifestPath = Join-Path $sampleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).gbarwidget"
$cliProject = Join-Path $repositoryRoot 'tools\GbarCli\GbarCli.csproj'
$widgetProject = Join-Path $sampleRoot 'YtMusicWidget.csproj'
$publishRoot = Join-Path $artifactsRoot 'publish'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)] [string]$Parent,
        [Parameter(Mandatory = $true)] [string]$Child
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    if (-not $resolvedChild.StartsWith(
        $resolvedParent + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside $resolvedParent`: $resolvedChild"
    }
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory = $true)] [string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($resolved)
    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Path has no filesystem root: $resolved"
    }
    $current = $root
    $relative = $resolved.Substring($root.Length)
    foreach ($segment in $relative.Split(
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
New-Item -ItemType Directory -Force -Path $payloadRoot, (Join-Path $stagingRoot 'styles') | Out-Null
Assert-NoReparsePoint -Path $stagingRoot
Assert-NoReparsePoint -Path $publishRoot

& dotnet publish $widgetProject `
    --configuration $Configuration `
    --no-self-contained `
    --nologo `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "YT Music addon publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $publishRoot 'YtMusicWidget.dll') `
    -Destination (Join-Path $payloadRoot 'YtMusicWidget.dll') -Force
Copy-Item -LiteralPath $manifestPath `
    -Destination (Join-Path $stagingRoot 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.gbss') `
    -Destination (Join-Path $stagingRoot 'styles\default.gbss') -Force

if (Test-Path -LiteralPath $packagePath) {
    Assert-NoReparsePoint -Path $packagePath
    Remove-Item -LiteralPath $packagePath -Force
}

& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
    validate $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "gbar validate rejected the staged YT Music addon."
}
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw "gbar pack did not produce the YT Music addon package."
}

if ($Install) {
    $catalogArguments = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        @()
    } else {
        @('--catalog', [System.IO.Path]::GetFullPath($Catalog))
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
        install $packagePath @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "gbar install failed. Installed versions are immutable; bump manifest.json when replacing an existing version."
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
        enable $manifest.id @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "gbar enable failed for $($manifest.id)."
    }
}

Write-Host "Community addon package: $packagePath"
