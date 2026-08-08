[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',
    [switch]$SkipTests,
    [switch]$SkipPackaging
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameInputVersion = '3.5.262'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$gameInputPackage = Join-Path $nugetRoot "microsoft.gameinput\$gameInputVersion"
$gameInputHeader = Join-Path $gameInputPackage 'native\include\GameInput.h'
if (-not (Test-Path -LiteralPath $gameInputHeader)) {
    & dotnet restore (Join-Path $projectDirectory 'NativeDependencies.csproj') --nologo
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $gameInputHeader)) {
        throw "Microsoft.GameInput $gameInputVersion could not be restored."
    }
}
$vsWhere = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (Test-Path -LiteralPath $vsWhere) {
    $vsRoot = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}

if (-not $vsRoot) {
    $vsCandidates = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    foreach ($version in $vsCandidates) {
        $edition = Get-ChildItem -LiteralPath $version.FullName -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($edition -and (Test-Path -LiteralPath (Join-Path $edition.FullName 'VC\Tools\MSVC'))) {
            $vsRoot = $edition.FullName
            break
        }
    }
}

if (-not $vsRoot) {
    throw 'Visual Studio C++ build tools were not found. Install the Desktop development with C++ workload.'
}

$vcToolsRoot = Join-Path $vsRoot 'VC\Tools\MSVC'
$vcTools = Get-ChildItem -LiteralPath $vcToolsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $vcTools) {
    throw "No MSVC toolset was found below $vcToolsRoot. Install the Desktop development with C++ workload."
}

$cl = Join-Path $vcTools.FullName "bin\Host$Architecture\$Architecture\cl.exe"
$standardHeader = Join-Path $vcTools.FullName 'include\excpt.h'
if (-not (Test-Path -LiteralPath $cl) -or -not (Test-Path -LiteralPath $standardHeader)) {
    throw "The MSVC installation at $($vcTools.FullName) is incomplete. Install or repair the Desktop development with C++ workload."
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\GameInput.h') } |
    Sort-Object Name -Descending |
    Select-Object -First 1
if (-not $sdk) {
    throw "A Windows SDK containing GameInput.h was not found below $sdkRoot."
}

$sdkBin = Join-Path $sdkRoot "bin\$($sdk.Name)\$Architecture"
$compilerBin = Split-Path -Parent $cl
if (-not (Test-Path -LiteralPath (Join-Path $sdkBin 'mt.exe'))) {
    throw "The Windows SDK manifest tool was not found below $sdkBin."
}
$env:PATH = "$sdkBin;$compilerBin;$env:PATH"

$outputDirectory = Join-Path $projectDirectory "out\$Configuration"
$hostObjectDirectory = Join-Path $outputDirectory 'obj\host'
$testObjectDirectory = Join-Path $outputDirectory 'obj\tests'
$imageTestObjectDirectory = Join-Path $outputDirectory 'obj\image-tests'
$layoutTestObjectDirectory = Join-Path $outputDirectory 'obj\layout-tests'
$iconTestObjectDirectory = Join-Path $outputDirectory 'obj\icon-tests'
$styleTestObjectDirectory = Join-Path $outputDirectory 'obj\style-tests'
$motionTestObjectDirectory = Join-Path $outputDirectory 'obj\motion-tests'
$placementTestObjectDirectory = Join-Path $outputDirectory 'obj\placement-tests'
$targetingTestObjectDirectory = Join-Path $outputDirectory 'obj\targeting-tests'
$guideTestObjectDirectory = Join-Path $outputDirectory 'obj\guide-tests'
$navigationTestObjectDirectory = Join-Path $outputDirectory 'obj\navigation-tests'
$sliderTestObjectDirectory = Join-Path $outputDirectory 'obj\slider-tests'
$focusTestObjectDirectory = Join-Path $outputDirectory 'obj\focus-tests'
$surfaceFocusTestObjectDirectory = Join-Path $outputDirectory 'obj\surface-focus-tests'
$lifecycleTestObjectDirectory = Join-Path $outputDirectory 'obj\lifecycle-tests'
$bridgeCatalogTestObjectDirectory = Join-Path $outputDirectory 'obj\bridge-catalog-tests'
$rendererTestObjectDirectory = Join-Path $outputDirectory 'obj\renderer-tests'
New-Item -ItemType Directory -Force -Path $hostObjectDirectory, $testObjectDirectory, $imageTestObjectDirectory, $layoutTestObjectDirectory, $iconTestObjectDirectory, $styleTestObjectDirectory, $motionTestObjectDirectory, $placementTestObjectDirectory, $targetingTestObjectDirectory, $guideTestObjectDirectory, $navigationTestObjectDirectory, $sliderTestObjectDirectory, $focusTestObjectDirectory, $surfaceFocusTestObjectDirectory, $lifecycleTestObjectDirectory, $bridgeCatalogTestObjectDirectory, $rendererTestObjectDirectory | Out-Null

$optimization = if ($Configuration -eq 'Release') { @('/O2', '/DNDEBUG') } else { @('/Od', '/Zi') }
$includeArguments = @(
    "/I$gameInputPackage\native\include",
    "/I$($vcTools.FullName)\include",
    "/I$sdkRoot\Include\$($sdk.Name)\ucrt",
    "/I$sdkRoot\Include\$($sdk.Name)\shared",
    "/I$sdkRoot\Include\$($sdk.Name)\um",
    "/I$sdkRoot\Include\$($sdk.Name)\winrt",
    "/I$sdkRoot\Include\$($sdk.Name)\cppwinrt"
)
$libraryArguments = @(
    "/LIBPATH:$gameInputPackage\native\lib\$Architecture",
    "/LIBPATH:$($vcTools.FullName)\lib\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\ucrt\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\um\$Architecture"
)
$common = @('/nologo', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-', '/DUSING_GAMEINPUT', '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX') +
    $optimization + $includeArguments

$hostArguments = $common + @(
    (Join-Path $projectDirectory 'main.cpp'),
    (Join-Path $projectDirectory 'OverlayState.cpp'),
    (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
    (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
    (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
    (Join-Path $projectDirectory 'NativeIcons.cpp'),
    (Join-Path $projectDirectory 'NativeStyle.cpp'),
    (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
    (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
    (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
    (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
    (Join-Path $projectDirectory 'GuideInputCompatibility.cpp'),
    (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
    (Join-Path $projectDirectory 'SliderInteraction.cpp'),
    (Join-Path $projectDirectory 'FocusNavigation.cpp'),
    (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
    (Join-Path $projectDirectory 'WidgetLifecycle.cpp'),
    "/Fo:$hostObjectDirectory\",
    "/Fe:$outputDirectory\OverlayHost.exe",
    '/link'
) + $libraryArguments + @(
    '/SUBSYSTEM:WINDOWS',
    '/MANIFEST:EMBED',
    "/MANIFESTINPUT:$(Join-Path $projectDirectory 'app.manifest')",
    'user32.lib', 'gdi32.lib', 'd2d1.lib', 'dwrite.lib', 'dwmapi.lib',
    'gameinput.lib', 'shcore.lib', 'xinput9_1_0.lib', 'windowsapp.lib',
    'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
)

& $cl $hostArguments
if ($LASTEXITCODE -ne 0) {
    throw "OverlayHost build failed with exit code $LASTEXITCODE."
}

if (-not $SkipPackaging) {
    function Assert-NoReparsePointInPath {
        param([Parameter(Mandatory = $true)] [string]$Path)

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
                    throw "Build output paths cannot traverse a reparse point: $current"
                }
            }
        }
    }

    function Remove-GeneratedDirectory {
        param([Parameter(Mandatory = $true)] [string]$Path)

        $resolvedOutputRoot = [System.IO.Path]::GetFullPath($outputDirectory)
        $resolvedPath = [System.IO.Path]::GetFullPath($Path)
        if (-not $resolvedPath.StartsWith(
            $resolvedOutputRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Generated output escaped the build layout: $resolvedPath"
        }
        Assert-NoReparsePointInPath -Path $resolvedOutputRoot
        Assert-NoReparsePointInPath -Path $resolvedPath
        if (Test-Path -LiteralPath $resolvedPath) {
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        }
    }

    function Publish-BundledWidgetPackage {
        param(
            [Parameter(Mandatory = $true)] [string]$WidgetProject,
            [Parameter(Mandatory = $true)] [string]$PackageRoot,
            [Parameter(Mandatory = $true)] [string]$AssemblyName,
            [Parameter(Mandatory = $true)] [string]$DisplayName
        )

        $resolvedPackageRoot = [System.IO.Path]::GetFullPath($PackageRoot)
        $resolvedOutputRoot = [System.IO.Path]::GetFullPath($outputDirectory)
        if (-not $resolvedPackageRoot.StartsWith(
            $resolvedOutputRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Bundled package output escaped the build layout: $resolvedPackageRoot"
        }
        if (Test-Path -LiteralPath $resolvedPackageRoot) {
            Remove-Item -LiteralPath $resolvedPackageRoot -Recurse -Force
        }
        $payloadOutput = Join-Path $resolvedPackageRoot 'payload'
        $stylesOutput = Join-Path $resolvedPackageRoot 'styles'
        New-Item -ItemType Directory -Force -Path $payloadOutput, $stylesOutput | Out-Null
        & dotnet publish (Join-Path $WidgetProject "$AssemblyName.csproj") `
            --configuration $Configuration --no-self-contained --nologo --output $payloadOutput
        if ($LASTEXITCODE -ne 0) {
            throw "$DisplayName package publish failed with exit code $LASTEXITCODE."
        }
        # WidgetSdk/WidgetProtocol are host-ABI assemblies selected by the
        # generic loader. Project assets copied by `dotnet publish` are pruned
        # so four bundled packages do not duplicate or shadow host contracts.
        foreach ($hostSharedFile in @(
            'WidgetSdk.dll',
            'WidgetSdk.pdb',
            'WidgetProtocol.dll',
            'WidgetProtocol.pdb',
            "$AssemblyName.pdb",
            'manifest.json'
        )) {
            $candidate = Join-Path $payloadOutput $hostSharedFile
            if (Test-Path -LiteralPath $candidate) {
                Remove-Item -LiteralPath $candidate -Force
            }
        }
        $copiedStyles = Join-Path $payloadOutput 'styles'
        if (Test-Path -LiteralPath $copiedStyles) {
            Remove-Item -LiteralPath $copiedStyles -Recurse -Force
        }
        Copy-Item -LiteralPath (Join-Path $WidgetProject 'manifest.json') `
            -Destination (Join-Path $resolvedPackageRoot 'manifest.json') -Force
        Copy-Item -LiteralPath (Join-Path $WidgetProject 'styles\default.gbss') `
            -Destination (Join-Path $stylesOutput 'default.gbss') -Force
        foreach ($requiredFile in @(
            'manifest.json',
            'styles\default.gbss',
            "payload\$AssemblyName.dll"
        )) {
            if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackageRoot $requiredFile))) {
                throw "$DisplayName bundled package is missing $requiredFile."
            }
        }
    }

    $bridgeOutput = Join-Path $outputDirectory 'runtime\Bridge'
    $workerHostOutput = Join-Path $outputDirectory 'runtime\WidgetWorkerHost'
    $settingsOutput = Join-Path $outputDirectory 'runtime\Settings'
    $audioMixerOutput = Join-Path $outputDirectory 'runtime\AudioMixer'
    $networkControlsOutput = Join-Path $outputDirectory 'runtime\NetworkControls'
    $gamesAppsOutput = Join-Path $outputDirectory 'runtime\GamesApps'
    $mediaSessionsOutput = Join-Path $outputDirectory 'runtime\MediaSessions'
    # YT Music is a community addon now. Remove an incremental build's retired
    # trusted worker so it cannot remain as an accidental fallback.
    Remove-GeneratedDirectory -Path (Join-Path $outputDirectory 'runtime\YtMusic')
    & dotnet publish (Join-Path $projectDirectory '..\WidgetBridge\WidgetBridge.csproj') `
        --configuration $Configuration --no-self-contained --nologo --output $bridgeOutput
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridge publish failed with exit code $LASTEXITCODE."
    }
    & dotnet publish (Join-Path $projectDirectory '..\WidgetWorkerHost\WidgetWorkerHost.csproj') `
        --configuration $Configuration --no-self-contained --nologo --output $workerHostOutput
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath (Join-Path $workerHostOutput 'WidgetWorkerHost.exe'))) {
        throw "Generic widget worker host publish failed with exit code $LASTEXITCODE."
    }
    & dotnet publish (Join-Path $projectDirectory '..\FirstPartyWidgets\SettingsWidget.Worker\SettingsWidget.Worker.csproj') `
        --configuration $Configuration --no-self-contained --nologo --output $settingsOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Settings worker publish failed with exit code $LASTEXITCODE."
    }
    $settingsProject = Join-Path $projectDirectory '..\FirstPartyWidgets\SettingsWidget'
    $settingsStylesOutput = Join-Path $settingsOutput 'styles'
    $settingsPayloadOutput = Join-Path $settingsOutput 'payload'
    New-Item -ItemType Directory -Force -Path $settingsStylesOutput, $settingsPayloadOutput | Out-Null
    Copy-Item -LiteralPath (Join-Path $settingsProject 'manifest.json') `
        -Destination (Join-Path $settingsOutput 'manifest.json') -Force
    Copy-Item -LiteralPath (Join-Path $settingsProject 'styles\default.gbss') `
        -Destination (Join-Path $settingsStylesOutput 'default.gbss') -Force
    Copy-Item -LiteralPath (Join-Path $settingsOutput 'SettingsWidget.dll') `
        -Destination (Join-Path $settingsPayloadOutput 'SettingsWidget.dll') -Force
    foreach ($requiredSettingsFile in @(
        'SettingsWidget.Worker.exe',
        'manifest.json',
        'styles\default.gbss',
        'payload\SettingsWidget.dll'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $settingsOutput $requiredSettingsFile))) {
            throw "Settings deployment is missing $requiredSettingsFile."
        }
    }
    Publish-BundledWidgetPackage `
        (Join-Path $projectDirectory '..\FirstPartyWidgets\AudioMixerWidget') `
        $audioMixerOutput 'AudioMixerWidget' 'Audio Mixer'
    Publish-BundledWidgetPackage `
        (Join-Path $projectDirectory '..\FirstPartyWidgets\NetworkControlsWidget') `
        $networkControlsOutput 'NetworkControlsWidget' 'Network Controls'
    Publish-BundledWidgetPackage `
        (Join-Path $projectDirectory '..\FirstPartyWidgets\GamesAppsWidget') `
        $gamesAppsOutput 'GamesAppsWidget' 'Games & Apps'
    Publish-BundledWidgetPackage `
        (Join-Path $projectDirectory '..\FirstPartyWidgets\MediaSessionsWidget') `
        $mediaSessionsOutput 'MediaSessionsWidget' 'Now Playing'
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'widget-catalog.json') `
        -Destination (Join-Path $outputDirectory 'widget-catalog.json') -Force
}

if (-not $SkipTests) {
    $testArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayStateTests.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        "/Fo:$testObjectDirectory\",
        "/Fe:$outputDirectory\OverlayStateTests.exe",
        '/link'
    ) + $libraryArguments + @('/SUBSYSTEM:CONSOLE')

    & $cl $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests build failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $outputDirectory 'OverlayStateTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests failed with exit code $LASTEXITCODE."
    }

    $imageTestArguments = $common + @(
        (Join-Path $projectDirectory 'RemoteImageCacheTests.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$imageTestObjectDirectory\",
        "/Fe:$outputDirectory\RemoteImageCacheTests.exe",
        '/link'
    ) + $libraryArguments + @(
        '/SUBSYSTEM:CONSOLE', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib', 'd2d1.lib'
    )
    & $cl $imageTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "RemoteImageCacheTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'RemoteImageCacheTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "RemoteImageCacheTests failed with exit code $LASTEXITCODE."
    }

    $layoutTestArguments = $common + @(
        (Join-Path $projectDirectory 'DeclarativeLayoutTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        "/Fo:$layoutTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeLayoutTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $layoutTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeLayoutTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeLayoutTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeLayoutTests failed with exit code $LASTEXITCODE."
    }

    $iconTestArguments = $common + @(
        (Join-Path $projectDirectory 'NativeIconsTests.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        "/Fo:$iconTestObjectDirectory\",
        "/Fe:$outputDirectory\NativeIconsTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('d2d1.lib', 'windowscodecs.lib', 'ole32.lib')
    & $cl $iconTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "NativeIconsTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'NativeIconsTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "NativeIconsTests failed with exit code $LASTEXITCODE."
    }

    $styleTestArguments = $common + @(
        (Join-Path $projectDirectory 'NativeStyleTests.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        "/Fo:$styleTestObjectDirectory\",
        "/Fe:$outputDirectory\NativeStyleTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $styleTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "NativeStyleTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'NativeStyleTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "NativeStyleTests failed with exit code $LASTEXITCODE."
    }

    $motionTestArguments = $common + @(
        (Join-Path $projectDirectory 'DeclarativeMotionTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        "/Fo:$motionTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeMotionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $motionTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeMotionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeMotionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeMotionTests failed with exit code $LASTEXITCODE."
    }

    $bridgeCatalogTestArguments = $common + @(
        '/DGBA_WIDGET_BRIDGE_CLIENT_TESTING',
        (Join-Path $projectDirectory 'WidgetBridgeCatalogTests.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        "/Fo:$bridgeCatalogTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetBridgeCatalogTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('windowsapp.lib', 'user32.lib')
    & $cl $bridgeCatalogTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridgeCatalogTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetBridgeCatalogTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridgeCatalogTests failed with exit code $LASTEXITCODE."
    }

    $placementTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
        (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
        "/Fo:$placementTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlacementTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $placementTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayPlacementTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests failed with exit code $LASTEXITCODE."
    }

    $targetingTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTargetingTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
        "/Fo:$targetingTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayTargetingTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $targetingTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayTargetingTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests failed with exit code $LASTEXITCODE."
    }

    $guideTestArguments = $common + @(
        (Join-Path $projectDirectory 'GuideInputCompatibilityTests.cpp'),
        (Join-Path $projectDirectory 'GuideInputCompatibility.cpp'),
        "/Fo:$guideTestObjectDirectory\",
        "/Fe:$outputDirectory\GuideInputCompatibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib')
    & $cl $guideTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "GuideInputCompatibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'GuideInputCompatibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "GuideInputCompatibilityTests failed with exit code $LASTEXITCODE."
    }

    $navigationTestArguments = $common + @(
        (Join-Path $projectDirectory 'ControllerNavigationTests.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        "/Fo:$navigationTestObjectDirectory\",
        "/Fe:$outputDirectory\ControllerNavigationTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $navigationTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerNavigationTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'ControllerNavigationTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerNavigationTests failed with exit code $LASTEXITCODE."
    }

    $sliderTestArguments = $common + @(
        (Join-Path $projectDirectory 'SliderInteractionTests.cpp'),
        (Join-Path $projectDirectory 'SliderInteraction.cpp'),
        "/Fo:$sliderTestObjectDirectory\",
        "/Fe:$outputDirectory\SliderInteractionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $sliderTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SliderInteractionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'SliderInteractionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "SliderInteractionTests failed with exit code $LASTEXITCODE."
    }

    $focusTestArguments = $common + @(
        (Join-Path $projectDirectory 'FocusNavigationTests.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        "/Fo:$focusTestObjectDirectory\",
        "/Fe:$outputDirectory\FocusNavigationTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $focusTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "FocusNavigationTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'FocusNavigationTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "FocusNavigationTests failed with exit code $LASTEXITCODE."
    }

    $surfaceFocusTestArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetSurfaceFocusTests.cpp'),
        (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
        "/Fo:$surfaceFocusTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetSurfaceFocusTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $surfaceFocusTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSurfaceFocusTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetSurfaceFocusTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSurfaceFocusTests failed with exit code $LASTEXITCODE."
    }

    $lifecycleTestArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetLifecycleTests.cpp'),
        (Join-Path $projectDirectory 'WidgetLifecycle.cpp'),
        "/Fo:$lifecycleTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetLifecycleTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $lifecycleTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetLifecycleTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetLifecycleTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetLifecycleTests failed with exit code $LASTEXITCODE."
    }

    $rendererTestArguments = $common + @(
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$rendererTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeRendererTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $rendererTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeRendererTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Built $outputDirectory\OverlayHost.exe"
