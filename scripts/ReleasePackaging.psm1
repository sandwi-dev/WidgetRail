Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ReleasePath {
    param([Parameter(Mandatory)][string]$Path, [string]$Within)
    $full = [IO.Path]::GetFullPath($Path)
    if ($Within) {
        $parent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Within))
        if (!$full.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release path escapes its root: $full"
        }
    }
    $current = $full
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Release paths cannot use reparse points: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $full
}

function Get-ReleaseFiles {
    param([Parameter(Mandatory)][string]$Root)
    $resolved = Assert-ReleasePath $Root
    if (!(Test-Path -LiteralPath $resolved -PathType Container)) { throw "Missing release input directory: $resolved" }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($resolved)
    $result = [Collections.Generic.List[string]]::new()
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release input contains a reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else { $result.Add([IO.Path]::GetRelativePath($resolved, $item.FullName).Replace('\', '/')) }
        }
    }
    $paths = $result.ToArray()
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    return $paths
}

function Write-ReleaseJson {
    param([string]$Path, $Value)
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 60) + "`n", [Text.UTF8Encoding]::new($false))
}

function Copy-ReleaseFile {
    param([string]$Source, [string]$Destination, [string]$Root)
    $sourcePath = Assert-ReleasePath $Source
    $destinationPath = Assert-ReleasePath $Destination -Within $Root
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Missing release input: $Source" }
    if (Test-Path -LiteralPath $destinationPath) { throw "Duplicate release destination: $Destination" }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destinationPath)) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    if ((Get-FileHash -LiteralPath $sourcePath).Hash -cne (Get-FileHash -LiteralPath $destinationPath).Hash) {
        throw "Release input changed while being copied: $sourcePath"
    }
}

function Copy-ReleaseTree {
    param([string]$Source, [string]$Destination, [string]$Root, [switch]$SealedPackage)
    foreach ($relative in Get-ReleaseFiles $Source) {
        $extension = [IO.Path]::GetExtension($relative)
        if ($extension -in @('.pdb', '.xml')) {
            if ($SealedPackage) { throw "Sealed package contains development output: $relative" }
            continue
        }
        if ($relative -match '(^|/)(bin|obj|logs|TestResults|\.git)(/|$)' -or
            $extension -in @('.log', '.binlog', '.obj', '.ilk', '.exp', '.lib', '.tmp', '.env') -or
            $relative -match '(^|/)[^/]*Tests?\.(exe|dll)$') {
            throw "Unexpected development/runtime-state file in release input: $relative"
        }
        Copy-ReleaseFile (Join-Path $Source $relative) (Join-Path $Destination $relative) $Root
    }
}

function Get-ReleaseInventory {
    param([string]$Root)
    return @(foreach ($relative in Get-ReleaseFiles $Root) {
        if ($relative -eq 'release.json') { continue }
        $file = Join-Path $Root $relative
        [ordered]@{ path = $relative; bytes = (Get-Item -LiteralPath $file).Length; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}

function Test-ReleaseInventory {
    param([Parameter(Mandatory)][string]$Root)
    $manifestPath = Assert-ReleasePath (Join-Path $Root 'release.json') -Within $Root
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $actual = @(Get-ReleaseInventory $Root)
    if ($manifest.files.Count -ne $actual.Count) { throw 'Release inventory file count differs.' }
    for ($i = 0; $i -lt $actual.Count; $i++) {
        if ($manifest.files[$i].path -cne $actual[$i].path -or
            $manifest.files[$i].bytes -ne $actual[$i].bytes -or
            $manifest.files[$i].sha256 -cne $actual[$i].sha256) {
            throw "Release inventory differs at $($actual[$i].path)."
        }
    }
    foreach ($desktop in @($manifest.requirements | Where-Object name -CEQ '.NET Desktop')) {
        foreach ($file in @('System.Windows.Forms.dll', 'PresentationFramework.dll', 'Microsoft.WindowsDesktop.App.deps.json')) {
            $path = Assert-ReleasePath (Join-Path $Root "dotnet/shared/Microsoft.WindowsDesktop.App/$($desktop.version)/$file") -Within $Root
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release is missing the declared Desktop runtime: $file" }
        }
    }
    $catalog = Get-Content -LiteralPath (Join-Path $Root 'widget-catalog.json') -Raw | ConvertFrom-Json
    foreach ($path in @($catalog.genericWorkerExecutable) + @($catalog.widgets | ForEach-Object workerExecutable)) {
        if ([IO.Path]::IsPathRooted($path)) { throw 'Catalog runtime path must be relative.' }
        $target = Assert-ReleasePath (Join-Path $Root $path) -Within $Root
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "Catalog runtime missing: $path" }
    }
    foreach ($widget in $catalog.bundledWidgets) {
        $package = Assert-ReleasePath (Join-Path $Root $widget.packageRoot) -Within $Root
        foreach ($required in @('manifest.json', 'styles/default.wrss', '.wrail-integrity.json')) {
            if (!(Test-Path -LiteralPath (Join-Path $package $required) -PathType Leaf)) { throw "Bundled package missing $required" }
        }
        $packageManifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
        $entry = Assert-ReleasePath (Join-Path $package $packageManifest.entrypoint.assembly) -Within $package
        if ($packageManifest.id -cne $widget.packageId -or !(Test-Path -LiteralPath $entry -PathType Leaf)) {
            throw "Bundled package identity or entrypoint differs: $($widget.id)"
        }
    }
}

function New-WidgetRailReleaseFolders {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$BuildRoot,
        [Parameter(Mandatory)][string]$DeveloperRoot,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$OutputRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$SourceCommit,
        [Parameter(Mandatory)][string]$SdkVersion,
        [Parameter(Mandatory)]$Content,
        [scriptblock]$VerifyCatalog,
        [scriptblock]$BeforePublish,
        [string]$PackagingCommit
    )
    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $SourceCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Release version or source commit is invalid.'
    }
    if ($Content.schemaVersion -ne 1 -or $Content.architecture -ne 'x64') { throw 'Unsupported release content schema.' }
    $build = Assert-ReleasePath $BuildRoot
    $developer = Assert-ReleasePath $DeveloperRoot
    $output = Assert-ReleasePath $OutputRoot
    $final = Assert-ReleasePath (Join-Path $output $Version) -Within $output
    if (Test-Path -LiteralPath $final) { throw "Release version already exists: $final" }
    $stage = Assert-ReleasePath (Join-Path $output ('.staging-' + [guid]::NewGuid().ToString('N'))) -Within $output
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $baseCatalog = Get-Content -LiteralPath (Join-Path $build 'widget-catalog.json') -Raw | ConvertFrom-Json
    $extraCatalog = Get-Content -LiteralPath (Join-Path $developer 'developer-widgets.json') -Raw | ConvertFrom-Json
    if (@($baseCatalog.widgets).Count -ne 1 -or $baseCatalog.widgets[0].id -ne 'settings') {
        throw 'Release requires exactly the trusted Settings widget.'
    }
    $all = @($baseCatalog.bundledWidgets)
    $requested = @($Content.productionWidgets) + @($Content.developerExistingWidgets)
    foreach ($id in $requested) {
        if (@($all | Where-Object id -CEQ $id).Count -ne 1) { throw "Missing or duplicate configured widget: $id" }
    }
    if (@($extraCatalog).Count -ne @($Content.developerWidgets).Count) { throw 'Developer widget count differs.' }
    foreach ($spec in $Content.developerWidgets) {
        if (@($extraCatalog | Where-Object id -CEQ $spec.id).Count -ne 1) { throw "Missing developer widget: $($spec.id)" }
    }
    foreach ($edition in @('production', 'developer')) {
        $folderName = if ($edition -eq 'production') { "WidgetRail-$Version-win-x64" } else { "WidgetRail-Developer-$Version-win-x64" }
        $root = Join-Path $stage $folderName
        New-Item -ItemType Directory -Path $root | Out-Null
        foreach ($file in $Content.rootFiles) {
            $source = Assert-ReleasePath (Join-Path $build $file) -Within $build
            Copy-ReleaseFile $source (Join-Path $root $file) $root
        }
        Copy-ReleaseFile (Join-Path $RepositoryRoot 'LICENSE') (Join-Path $root 'LICENSE') $root
        Copy-ReleaseFile (Join-Path $RepositoryRoot 'THIRD_PARTY_NOTICES.md') (Join-Path $root 'THIRD_PARTY_NOTICES.md') $root
        foreach ($license in @('third_party/ViGEmClient/LICENSE', 'third_party/public_suffix_list/LICENSE')) {
            Copy-ReleaseFile (Join-Path $RepositoryRoot $license) (Join-Path $root $license) $root
        }
        Copy-ReleaseTree (Join-Path $developer 'licenses') (Join-Path $root 'licenses') $root
        foreach ($name in $Content.sharedRuntimeDirectories) {
            Copy-ReleaseTree (Join-Path $developer $name) (Join-Path $root $name) $root
        }
        foreach ($name in $Content.hostRuntimeDirectories) {
            Copy-ReleaseTree (Join-Path $build "runtime/$name") (Join-Path $root "runtime/$name") $root
        }
        $selected = @($Content.productionWidgets | ForEach-Object {
            $id = $_; $all | Where-Object id -CEQ $id
        })
        if ($edition -eq 'developer') {
            $selected += @($Content.developerExistingWidgets | ForEach-Object { $id = $_; $all | Where-Object id -CEQ $id })
        }
        foreach ($widget in $selected) {
            $source = Assert-ReleasePath (Join-Path $build $widget.packageRoot) -Within $build
            Copy-ReleaseTree $source (Join-Path $root $widget.packageRoot) $root -SealedPackage
        }
        if ($edition -eq 'developer') {
            foreach ($widget in $extraCatalog) {
                $source = Assert-ReleasePath (Join-Path $developer $widget.packageRoot) -Within $developer
                Copy-ReleaseTree $source (Join-Path $root $widget.packageRoot) $root -SealedPackage
            }
            $selected += @($extraCatalog)
        }
        Copy-ReleaseTree (Join-Path $developer 'tools/wrail') (Join-Path $root 'tools/wrail') $root
        [IO.File]::WriteAllText((Join-Path $root 'wrail.cmd'), '@echo off' + "`r`n" + '"%~dp0dotnet\dotnet.exe" "%~dp0tools\wrail\wrail.dll" %*' + "`r`n")
        $catalog = [ordered]@{
            catalogVersion = $baseCatalog.catalogVersion
            genericWorkerExecutable = $baseCatalog.genericWorkerExecutable
            widgets = @($baseCatalog.widgets)
            bundledWidgets = @($selected)
        }
        Write-ReleaseJson (Join-Path $root 'widget-catalog.json') $catalog
        $packages = @(foreach ($widget in @($catalog.widgets) + @($catalog.bundledWidgets)) {
            $packageRoot = if ($widget.id -eq 'settings') { 'runtime/Settings' } else { $widget.packageRoot }
            $metadata = Get-Content -LiteralPath (Join-Path $root "$packageRoot/manifest.json") -Raw | ConvertFrom-Json
            [ordered]@{ id = $metadata.id; version = $metadata.version; path = $packageRoot }
        })
        $readme = @(
            "WidgetRail $Version - $edition edition"
            ''
            'Run OverlayHost.exe. Keep this complete folder together.'
            'View + Menu toggles the overlay by default; F1 is the keyboard fallback.'
            'Review widget permissions in Settings before using Windows controls.'
            ''
            'This Windows x64 folder includes a private .NET runtime.'
            'Use setup to install missing GameInput and WebView2 dependencies.'
            'Embedded web media requires the Microsoft Edge WebView2 runtime.'
            'Settings and user data remain under %LOCALAPPDATA%\WidgetRail.'
            'Startup registration, driver installation and automatic updates are not performed.'
        )
        $readme += @('', 'Command-line tools: wrail.cmd help',
            'Create an independent widget with wrail new widget <Name> --output <new-directory>.',
            'Building widgets requires a compatible .NET SDK; the application folder does not include an SDK.')
        [IO.File]::WriteAllText((Join-Path $root 'START-HERE.txt'), ($readme -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
        Write-ReleaseJson (Join-Path $root 'release.json') ([ordered]@{
            schemaVersion = 1; product = 'WidgetRail'; version = $Version; edition = $edition
            architecture = 'x64'; sourceCommit = $SourceCommit; sdkVersion = $SdkVersion
            packaging = 'private-runtime-folder'; signed = $false
            packagingCommit = $(if ($PackagingCommit) { $PackagingCommit } else { $SourceCommit })
            requirements = @($Content.runtimeRequirements); widgets = $packages
            files = @(Get-ReleaseInventory $root)
        })
        Test-ReleaseInventory $root
        if ($VerifyCatalog) { & $VerifyCatalog $root | Out-Host }
    }
    $checksums = @(foreach ($folder in Get-ChildItem -LiteralPath $stage -Directory | Sort-Object Name) {
        ((Get-FileHash -LiteralPath (Join-Path $folder.FullName 'release.json')).Hash.ToLowerInvariant()) + '  ' + $folder.Name + '/release.json'
    })
    [IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    # The parent rename exposes both validated editions together, never a partial release.
    $null = Assert-ReleasePath $stage -Within $output
    $null = Assert-ReleasePath $final -Within $output
    if ($BeforePublish) { & $BeforePublish | Out-Host }
    [IO.Directory]::Move($stage, $final)
    return $final
}

Export-ModuleMember -Function Assert-ReleasePath, Get-ReleaseFiles, Copy-ReleaseTree, Write-ReleaseJson, New-WidgetRailReleaseFolders, Test-ReleaseInventory
