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
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $parsedVersion = $null
    if (-not [System.Version]::TryParse($Version, [ref]$parsedVersion) -or
        $parsedVersion.ToString() -cne $Version) {
        throw "Version must use canonical dotted numeric notation: $Version"
    }
    $manifest.version = $Version
}
$packagePath = Join-Path $artifactsRoot "$($manifest.id)-$($manifest.version).wrwidget"
$cliProject = Join-Path $repositoryRoot 'tools\WrailCli\WrailCli.csproj'
$widgetProject = Join-Path $sampleRoot 'YtMusicCompanionFixture.csproj'
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
New-Item -ItemType Directory -Force -Path $payloadRoot, `
    (Join-Path $stagingRoot 'styles'), (Join-Path $stagingRoot 'assets\icons') | Out-Null
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
Copy-Item -LiteralPath (Join-Path $sampleRoot 'styles\default.wrss') `
    -Destination (Join-Path $stagingRoot 'styles\default.wrss') -Force
Copy-Item -LiteralPath (Join-Path $sampleRoot 'assets\icons\yt-music.svg') `
    -Destination (Join-Path $stagingRoot 'assets\icons\yt-music.svg') -Force

# Keep the public archive closed over the reviewed runtime payload. Host-owned
# companion state and secrets must never become package inputs.
$expectedFiles = @(
    'manifest.json',
    'assets\icons\yt-music.svg',
    'payload\YtMusicWidget.dll',
    'styles\default.wrss'
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

& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
    validate $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "wrail validate rejected the staged YT Music addon."
}
& dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
    pack $stagingRoot --output $packagePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
    throw "wrail pack did not produce the YT Music addon package."
}

if ($Install) {
    $catalogRoot = if ([string]::IsNullOrWhiteSpace($Catalog)) {
        Join-Path ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) 'WidgetRail\widgets'
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
        & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
            disable $manifest.id @catalogArguments
        if ($LASTEXITCODE -ne 0) {
            throw "wrail disable failed for the installed $($manifest.id) update."
        }
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
        install $packagePath @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "wrail install failed. Installed versions are immutable; bump manifest.json when replacing an existing version."
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
        version select $manifest.id $manifest.version @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "wrail version select failed for $($manifest.id) $($manifest.version)."
    }
    & dotnet run --project $cliProject --configuration $Configuration --no-launch-profile -- `
        enable $manifest.id @catalogArguments
    if ($LASTEXITCODE -ne 0) {
        throw "wrail enable failed for $($manifest.id)."
    }
}

Write-Host "Community addon package: $packagePath"
