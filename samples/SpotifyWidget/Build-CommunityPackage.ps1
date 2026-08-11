[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$Catalog,
    [string]$Version,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'

$sampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $sampleRoot '..\..'))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\community-addons\spotify'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = Join-Path $artifactsRoot 'package-root'
$payloadRoot = Join-Path $stagingRoot 'payload'
$manifestPath = Join-Path $sampleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $parsedVersion = $null
    if (-not [System.Version]::TryParse($Version, [ref]$parsedVersion) -or
        $parsedVersion.ToString() -cne $Version) {
        throw "Version must use canonical dotted numeric notation: $Version"
    }
    $manifest.version = $Version
}
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).gbarwidget"
$cliProject = Join-Path $repositoryRoot 'tools\GbarCli\GbarCli.csproj'
$widgetProject = Join-Path $sampleRoot 'SpotifyWidget.csproj'
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
    --property:UseSharedCompilation=false `
    --property:BuildInParallel=false `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Spotify addon publish failed with exit code $LASTEXITCODE."
}

# Package only these explicitly selected public artifacts. Configuration values and
# OAuth credentials live in host-owned stores and must never enter the addon archive.
Copy-Item -LiteralPath (Join-Path $publishRoot 'SpotifyWidget.dll') `
    -Destination (Join-Path $payloadRoot 'SpotifyWidget.dll') -Force
if ([string]::IsNullOrWhiteSpace($Version)) {
    Copy-Item -LiteralPath $manifestPath `
        -Destination (Join-Path $stagingRoot 'manifest.json') -Force
} else {
    $generatedManifest = $manifest | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingRoot 'manifest.json'),
        $generatedManifest,
        [System.Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.gbss') `
    -Destination (Join-Path $stagingRoot 'styles\default.gbss') -Force

$expectedFiles = @(
    'manifest.json',
    'payload\SpotifyWidget.dll',
    'styles\default.gbss'
)
$stagingPrefix = $stagingRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | ForEach-Object {
    $fullName = [System.IO.Path]::GetFullPath($_.FullName)
    if (-not $fullName.StartsWith($stagingPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Staged package file escaped the package root: $fullName"
    }
    $fullName.Substring($stagingPrefix.Length)
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
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
    validate $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "gbar validate rejected the staged Spotify addon."
}
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
    --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw "gbar pack did not produce the Spotify addon package."
}

if ($Install) {
    $catalogRoot = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        Join-Path ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) 'GameBarAlternative\widgets'
    } else {
        [System.IO.Path]::GetFullPath($Catalog)
    }
    $catalogArguments = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        @()
    } else {
        @('--catalog', $catalogRoot)
    }
    $installedPackageRoot = Join-Path (Join-Path $catalogRoot 'packages') $manifest.id
    if (Test-Path -LiteralPath $installedPackageRoot -PathType Container) {
        & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
            --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
            disable $manifest.id @catalogArguments
        if ($LASTEXITCODE -ne 0) {
            throw "gbar disable failed for the installed $($manifest.id) update."
        }
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        install $packagePath @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "gbar install failed. Installed versions are immutable; bump manifest.json when replacing an existing version."
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        version select $manifest.id $manifest.version @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "gbar version select failed for $($manifest.id) $($manifest.version)."
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile `
        --property:UseSharedCompilation=false --property:BuildInParallel=false -- `
        enable $manifest.id @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "gbar enable failed for $($manifest.id)."
    }
}

Write-Host "Community addon package: $packagePath"
