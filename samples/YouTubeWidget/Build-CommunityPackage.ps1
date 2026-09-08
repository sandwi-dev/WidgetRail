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
    Join-Path $repositoryRoot 'artifacts\community-addons\youtube-video'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = Join-Path $artifactsRoot 'package-root'
$publishRoot = Join-Path $artifactsRoot 'publish'
$payloadRoot = Join-Path $stagingRoot 'payload'
$mediaRoot = Join-Path $payloadRoot 'media'
$manifestPath = Join-Path $sampleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).wrwidget"
$cliProject = Join-Path $repositoryRoot 'tools\WrailCli\WrailCli.csproj'
$applicationProject = Join-Path $sampleRoot 'Application\YouTubeApplication.csproj'

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
New-Item -ItemType Directory -Force -Path $mediaRoot, `
    (Join-Path $stagingRoot 'styles'), (Join-Path $stagingRoot 'assets\icons') | Out-Null

& dotnet publish $applicationProject --configuration $Configuration --no-self-contained --nologo `
    --property:UseSharedCompilation=false --property:BuildInParallel=false --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "YouTube application publish failed with exit code $LASTEXITCODE." }

Get-ChildItem -LiteralPath $publishRoot -File | Where-Object {
    $_.Extension -notin @('.pdb', '.xml')
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadRoot $_.Name) -Force
}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingRoot 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.wrss') `
    -Destination (Join-Path $stagingRoot 'styles\default.wrss') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'media\adapter.html') `
    -Destination (Join-Path $mediaRoot 'adapter.html') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\WidgetSdk\EmbeddedMediaAdapterRuntime.js') `
    -Destination (Join-Path $mediaRoot 'adapter-runtime.js') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'assets\icons\youtube-red.svg') `
    -Destination (Join-Path $stagingRoot 'assets\icons\youtube-red.svg') -Force

$requiredFiles = @(
    'manifest.json',
    'assets\icons\youtube-red.svg',
    'payload\YouTubeApplication.exe',
    'payload\YouTubeApplication.dll',
    'payload\YouTubeWidget.dll',
    'payload\WidgetApplicationRuntime.dll',
    'payload\WidgetSdk.dll',
    'payload\WidgetProtocol.dll',
    'payload\media\adapter.html',
    'payload\media\adapter-runtime.js',
    'styles\default.wrss'
)
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | ForEach-Object {
    [System.IO.Path]::GetRelativePath($stagingRoot, $_.FullName)
})
$missingFiles = @($requiredFiles | Where-Object { $_ -notin $stagedFiles })
$forbiddenFiles = @($stagedFiles | Where-Object {
    $_ -match '\.(pdb|xml)$' -or
    $_ -match '(^|\\)(PlatformBroker|WindowsSpotifyProvider|PlatformSettings)\.dll$'
})
if ($forbiddenFiles.Count -ne 0 -or $missingFiles.Count -ne 0) {
    throw "Staged package graph mismatch. Forbidden: [$($forbiddenFiles -join ', ')]; missing: [$($missingFiles -join ', ')]."
}

if (Test-Path -LiteralPath $packagePath) {
    Assert-NoReparsePoint -Path $packagePath
    Remove-Item -LiteralPath $packagePath -Force
}

& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- validate $stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'wrail validate rejected the staged YouTube widget.' }
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw 'wrail pack did not produce the YouTube widget package.'
}

if ($Install) {
    $catalogArguments = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        @()
    } else {
        @('--catalog', [System.IO.Path]::GetFullPath($Catalog))
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        install $packagePath --accept-full-trust @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'wrail install failed. Installed versions are immutable; bump manifest.json before replacing one.'
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        enable $manifest.id @catalogArguments
    if ($LASTEXITCODE -ne 0) { throw "wrail enable failed for $($manifest.id)." }
}

Write-Host "Community addon package: $packagePath"
