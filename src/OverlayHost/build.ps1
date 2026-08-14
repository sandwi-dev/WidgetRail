[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',
    [switch]$SkipTests,
    [switch]$SkipPackaging,
    [switch]$SemanticChurnTestsOnly,
    [switch]$DeclarativeLayoutTestsOnly,
    [switch]$DeclarativeRendererTestsOnly,
    [switch]$PinnedSurfaceTestsOnly,
    [switch]$PinnedPlacementTestsOnly,
    [switch]$ProcessOwnerTestsOnly,
    [switch]$CompositionTestsOnly,
    [switch]$WidgetSwitchTestsOnly,
    [switch]$TrustedArtworkTestsOnly,
    [switch]$WidgetSessionTestsOnly,
    [switch]$WidgetBridgeCatalogTestsOnly,
    [switch]$LocalPackageImportTestsOnly,
    [switch]$WidgetSurfaceTestsOnly,
    [switch]$ColdDashboardTestsOnly,
    [switch]$LauncherExperienceTestsOnly,
    [switch]$LauncherExperienceHostTestsOnly,
    [switch]$AdvancedPresentationHostTestsOnly,
    [switch]$TextEntryHostTestsOnly,
    [switch]$TrayAccessibilityHostTestsOnly,
    [switch]$TrayRefreshHostTestsOnly,
    [switch]$LauncherExperienceLifecycleTestsOnly,
    [switch]$PlatformInteropTestsOnly
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
$cargo = Get-Command cargo -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $cargo) {
    $cargoCandidate = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
    if (Test-Path -LiteralPath $cargoCandidate) { $cargo = $cargoCandidate }
}
if (-not $cargo) {
    throw 'Rust Cargo was not found. Install the pinned toolchain declared by taffy_bridge\rust-toolchain.toml.'
}
$taffyManifest = Join-Path $projectDirectory 'taffy_bridge\Cargo.toml'
$taffyTargetDirectory = Join-Path $outputDirectory 'cargo'
$taffyProfile = if ($Configuration -eq 'Release') { 'release' } else { 'debug' }
$taffyArguments = @(
    'build', '--locked', '--manifest-path', $taffyManifest,
    '--target', 'x86_64-pc-windows-msvc')
if ($Configuration -eq 'Release') { $taffyArguments += '--release' }
$previousCargoTargetDirectory = $env:CARGO_TARGET_DIR
try {
    $env:CARGO_TARGET_DIR = $taffyTargetDirectory
    & $cargo $taffyArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Taffy static-library build failed with exit code $LASTEXITCODE."
    }
} finally {
    $env:CARGO_TARGET_DIR = $previousCargoTargetDirectory
}
$taffyLibrary = Join-Path $taffyTargetDirectory "x86_64-pc-windows-msvc\$taffyProfile\gba_taffy_layout.lib"
if (-not (Test-Path -LiteralPath $taffyLibrary)) {
    throw "Pinned Taffy static library was not produced at '$taffyLibrary'."
}
$platformDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $projectDirectory '..\OverlayPlatformInterop'))
$platformTestDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $projectDirectory '..\..\tests\OverlayPlatformInterop.Tests'))
$hostObjectDirectory = Join-Path $outputDirectory 'obj\host'
$platformObjectDirectory = Join-Path $outputDirectory 'obj\platform-interop'
$platformTestObjectDirectory = Join-Path $outputDirectory 'obj\platform-interop-tests'
$testObjectDirectory = Join-Path $outputDirectory 'obj\tests'
$imageTestObjectDirectory = Join-Path $outputDirectory 'obj\image-tests'
$layoutTestObjectDirectory = Join-Path $outputDirectory 'obj\layout-tests'
$iconTestObjectDirectory = Join-Path $outputDirectory 'obj\icon-tests'
$styleTestObjectDirectory = Join-Path $outputDirectory 'obj\style-tests'
$textLayoutTestObjectDirectory = Join-Path $outputDirectory 'obj\text-layout-tests'
$motionTestObjectDirectory = Join-Path $outputDirectory 'obj\motion-tests'
$placementTestObjectDirectory = Join-Path $outputDirectory 'obj\placement-tests'
$targetingTestObjectDirectory = Join-Path $outputDirectory 'obj\targeting-tests'
$transitionTestObjectDirectory = Join-Path $outputDirectory 'obj\transition-tests'
$chromeTestObjectDirectory = Join-Path $outputDirectory 'obj\chrome-tests'
$guideTestObjectDirectory = Join-Path $outputDirectory 'obj\guide-tests'
$inputOwnershipTestObjectDirectory = Join-Path $outputDirectory 'obj\input-ownership-tests'
$navigationTestObjectDirectory = Join-Path $outputDirectory 'obj\navigation-tests'
$pressedTestObjectDirectory = Join-Path $outputDirectory 'obj\pressed-tests'
$sliderTestObjectDirectory = Join-Path $outputDirectory 'obj\slider-tests'
$focusTestObjectDirectory = Join-Path $outputDirectory 'obj\focus-tests'
$surfaceFocusTestObjectDirectory = Join-Path $outputDirectory 'obj\surface-focus-tests'
$lifecycleTestObjectDirectory = Join-Path $outputDirectory 'obj\lifecycle-tests'
$actionFeedbackTestObjectDirectory = Join-Path $outputDirectory 'obj\action-feedback-tests'
$accessibilityTreeTestObjectDirectory = Join-Path $outputDirectory 'obj\accessibility-tree-tests'
$accessibilityProjectionTestObjectDirectory = Join-Path $outputDirectory 'obj\accessibility-projection-tests'
$accessibilityProviderTestObjectDirectory = Join-Path $outputDirectory 'obj\accessibility-provider-tests'
$realHostAccessibilityTestObjectDirectory = Join-Path $outputDirectory 'obj\real-host-accessibility-tests'
$actionFailureHostTestObjectDirectory = Join-Path $outputDirectory 'obj\action-failure-host-tests'
$actionFailureFixtureOutput = Join-Path $outputDirectory 'obj\action-failure-fixture'
$widgetSwitchHostTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-switch-host-tests'
$coldDashboardHostTestObjectDirectory = Join-Path $outputDirectory 'obj\cold-dashboard-host-tests'
$widgetSwitchFixtureOutput = Join-Path $outputDirectory 'obj\widget-switch-fixture'
$audioMixerScrollHostTestObjectDirectory = Join-Path $outputDirectory 'obj\audio-mixer-scroll-host-tests'
$audioMixerScrollFixtureOutput = Join-Path $outputDirectory 'obj\audio-mixer-scroll-fixture'
$scrollEvidenceProbeTestObjectDirectory = Join-Path $outputDirectory 'obj\scroll-evidence-probe-tests'
$trayLayoutTestObjectDirectory = Join-Path $outputDirectory 'obj\tray-layout-tests'
$hostAccessibilityTestObjectDirectory = Join-Path $outputDirectory 'obj\host-accessibility-tests'
$accessibilityEventsTestObjectDirectory = Join-Path $outputDirectory 'obj\accessibility-events-tests'
$bridgeCatalogTestObjectDirectory = Join-Path $outputDirectory 'obj\bridge-catalog-tests'
$localPackageImportTestObjectDirectory = Join-Path $outputDirectory 'obj\local-package-import-tests'
$textEntryModalTestObjectDirectory = Join-Path $outputDirectory 'obj\text-entry-modal-tests'
$rendererTestObjectDirectory = Join-Path $outputDirectory 'obj\renderer-tests'
$semanticChurnTestObjectDirectory = Join-Path $outputDirectory 'obj\semantic-churn-performance-tests'
$pinnedSurfaceTestObjectDirectory = Join-Path $outputDirectory 'obj\pinned-surface-host-tests'
$pinnedPlacementTestObjectDirectory = Join-Path $outputDirectory 'obj\pinned-placement-tests'
$widgetSurfaceTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-surface-coordinator-tests'
$widgetSessionTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-session-coordinator-tests'
$processOwnerTestObjectDirectory = Join-Path $outputDirectory 'obj\process-owner-tests'
$componentGeometryTestObjectDirectory = Join-Path $outputDirectory 'obj\component-geometry-tests'
$launcherExperienceTestObjectDirectory = Join-Path $outputDirectory 'obj\launcher-experience-tests'
$launcherExperienceHostTestObjectDirectory = Join-Path $outputDirectory 'obj\launcher-experience-host-tests'
$advancedPresentationHostTestObjectDirectory = Join-Path $outputDirectory 'obj\advanced-presentation-host-tests'
$advancedPresentationCommunityFixtureOutput = Join-Path $outputDirectory 'obj\advanced-presentation-community-fixture'
$trayRefreshHostTestObjectDirectory = Join-Path $outputDirectory 'obj\tray-refresh-host-tests'
$trayRefreshCommunityFixtureOutput = Join-Path $outputDirectory 'obj\tray-refresh-community-fixture'
$launcherExperienceBridgeFixtureOutput = Join-Path $outputDirectory 'obj\launcher-experience-bridge-fixture'
New-Item -ItemType Directory -Force -Path $hostObjectDirectory, $platformObjectDirectory, $platformTestObjectDirectory, $testObjectDirectory, $imageTestObjectDirectory, $layoutTestObjectDirectory, $iconTestObjectDirectory, $styleTestObjectDirectory, $textLayoutTestObjectDirectory, $motionTestObjectDirectory, $placementTestObjectDirectory, $targetingTestObjectDirectory, $transitionTestObjectDirectory, $chromeTestObjectDirectory, $guideTestObjectDirectory, $inputOwnershipTestObjectDirectory, $navigationTestObjectDirectory, $pressedTestObjectDirectory, $sliderTestObjectDirectory, $focusTestObjectDirectory, $surfaceFocusTestObjectDirectory, $lifecycleTestObjectDirectory, $actionFeedbackTestObjectDirectory, $accessibilityTreeTestObjectDirectory, $accessibilityProjectionTestObjectDirectory, $accessibilityProviderTestObjectDirectory, $realHostAccessibilityTestObjectDirectory, $actionFailureHostTestObjectDirectory, $actionFailureFixtureOutput, $widgetSwitchHostTestObjectDirectory, $coldDashboardHostTestObjectDirectory, $widgetSwitchFixtureOutput, $audioMixerScrollHostTestObjectDirectory, $audioMixerScrollFixtureOutput, $scrollEvidenceProbeTestObjectDirectory, $trayLayoutTestObjectDirectory, $hostAccessibilityTestObjectDirectory, $accessibilityEventsTestObjectDirectory, $bridgeCatalogTestObjectDirectory, $localPackageImportTestObjectDirectory, $textEntryModalTestObjectDirectory, $rendererTestObjectDirectory, $semanticChurnTestObjectDirectory, $pinnedSurfaceTestObjectDirectory, $pinnedPlacementTestObjectDirectory, $widgetSurfaceTestObjectDirectory, $widgetSessionTestObjectDirectory, $processOwnerTestObjectDirectory, $componentGeometryTestObjectDirectory, $launcherExperienceTestObjectDirectory, $launcherExperienceHostTestObjectDirectory, $advancedPresentationHostTestObjectDirectory, $advancedPresentationCommunityFixtureOutput, $trayRefreshHostTestObjectDirectory, $trayRefreshCommunityFixtureOutput, $launcherExperienceBridgeFixtureOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDirectory '..\..\THIRD_PARTY_NOTICES.md') `
    -Destination (Join-Path $outputDirectory 'THIRD_PARTY_NOTICES.md') -Force

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
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\um\$Architecture",
    $taffyLibrary,
    'ntdll.lib',
    'userenv.lib',
    'ws2_32.lib'
)
$common = @('/nologo', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-', '/DUSING_GAMEINPUT', '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX') +
    $optimization + $includeArguments

function Invoke-OverlayPlatformInteropBuild {
    $arguments = $common + @(
        '/DGBA_OVERLAY_PLATFORM_EXPORTS',
        '/LD',
        (Join-Path $platformDirectory 'OverlayPlatformInterop.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformTargeting.cpp'),
        (Join-Path $projectDirectory 'GuideInputCompatibility.cpp'),
        "/Fo:$platformObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlatformInterop.dll",
        '/link',
        "/IMPLIB:$outputDirectory\OverlayPlatformInterop.lib"
    ) + $libraryArguments + @(
        '/SUBSYSTEM:WINDOWS', 'gameinput.lib', 'user32.lib',
        'xinput9_1_0.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlatformInterop build failed with exit code $LASTEXITCODE."
    }
}

function Invoke-OverlayPlatformInteropTests {
    $arguments = $common + @(
        '/DGBA_OVERLAY_PLATFORM_IMPORTS',
        (Join-Path $platformTestDirectory 'OverlayPlatformInteropTests.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        "/Fo:$platformTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlatformInteropTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        (Join-Path $outputDirectory 'OverlayPlatformInterop.lib'),
        'user32.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlatformInteropTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayPlatformInteropTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlatformInteropTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-OverlayPlatformParityTest {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$ObjectDirectory,
        [Parameter(Mandatory = $true)] [string[]]$Sources,
        [string[]]$Libraries = @()
    )

    $arguments = $common + $Sources + @(
        "/Fo:$ObjectDirectory\",
        "/Fe:$outputDirectory\$Name.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + $Libraries
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory "$Name.exe")
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-OverlayPlatformParityTests {
    Invoke-OverlayPlatformParityTest `
        -Name 'OverlayStateTests' `
        -ObjectDirectory $testObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'OverlayStateTests.cpp'),
            (Join-Path $projectDirectory 'OverlayState.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'OverlayPlacementTests' `
        -ObjectDirectory $placementTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
            (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
            (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'OverlayTargetingTests' `
        -ObjectDirectory $targetingTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'OverlayTargetingTests.cpp'),
            (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
            (Join-Path $platformDirectory 'OverlayPlatformTargeting.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'GuideInputCompatibilityTests' `
        -ObjectDirectory $guideTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'GuideInputCompatibilityTests.cpp'),
            (Join-Path $projectDirectory 'GuideInputCompatibility.cpp')) `
        -Libraries @('user32.lib')
    Invoke-OverlayPlatformParityTest `
        -Name 'ControllerInputOwnershipTests' `
        -ObjectDirectory $inputOwnershipTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'ControllerInputOwnershipTests.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'ControllerNavigationTests' `
        -ObjectDirectory $navigationTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'ControllerNavigationTests.cpp'),
            (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
            (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'))
}

function Invoke-SemanticChurnPerformanceTests {
    $arguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'SemanticChurnPerformanceTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$semanticChurnTestObjectDirectory\",
        "/Fe:$outputDirectory\SemanticChurnPerformanceTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib',
        'psapi.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SemanticChurnPerformanceTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'SemanticChurnPerformanceTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "SemanticChurnPerformanceTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-DeclarativeLayoutTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'DeclarativeLayoutTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        "/Fo:$layoutTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeLayoutTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeLayoutTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeLayoutTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeLayoutTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-DeclarativeRendererTests {
    $arguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$rendererTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeRendererTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeRendererTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-PinnedSurfaceHostTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'PinnedSurfaceHostTests.cpp'),
        (Join-Path $projectDirectory 'PinnedSurfacePolicy.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$pinnedSurfaceTestObjectDirectory\",
        "/Fe:$outputDirectory\PinnedSurfaceHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'psapi.lib', 'ole32.lib', 'oleaut32.lib',
        'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "PinnedSurfaceHostTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'PinnedSurfaceHostTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "PinnedSurfaceHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-PinnedSurfacePlacementTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'PinnedSurfacePlacementTests.cpp'),
        (Join-Path $projectDirectory 'PinnedSurfacePlacement.cpp'),
        (Join-Path $projectDirectory 'PinnedSurfacePolicy.cpp'),
        "/Fo:$pinnedPlacementTestObjectDirectory\",
        "/Fe:$outputDirectory\PinnedSurfacePlacementTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "PinnedSurfacePlacementTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'PinnedSurfacePlacementTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "PinnedSurfacePlacementTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-WidgetSurfaceCoordinatorTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'WidgetSurfaceCoordinatorTests.cpp'),
        (Join-Path $projectDirectory 'WidgetSurfaceCoordinator.cpp'),
        (Join-Path $projectDirectory 'PinnedSurfacePolicy.cpp'),
        (Join-Path $projectDirectory 'PinnedSurfacePlacement.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        '/DGBA_WIDGET_SURFACE_COORDINATOR_TESTING',
        "/Fo:$widgetSurfaceTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetSurfaceCoordinatorTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'd2d1.lib', 'dwrite.lib', 'shcore.lib',
        'winhttp.lib', 'windowscodecs.lib', 'psapi.lib', 'ole32.lib',
        'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSurfaceCoordinatorTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetSurfaceCoordinatorTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSurfaceCoordinatorTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-WidgetBridgeCatalogTests {
    $arguments = $common + @(
        '/DGBA_WIDGET_BRIDGE_CLIENT_TESTING',
        (Join-Path $projectDirectory 'WidgetBridgeCatalogTests.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        "/Fo:$bridgeCatalogTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetBridgeCatalogTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('windowsapp.lib', 'user32.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridgeCatalogTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetBridgeCatalogTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridgeCatalogTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TextEntryModalTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'TextEntryModalTests.cpp'),
        (Join-Path $projectDirectory 'TextEntryActionAdmission.cpp'),
        (Join-Path $projectDirectory 'TextEntryModal.cpp'),
        (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$textEntryModalTestObjectDirectory\",
        "/Fe:$outputDirectory\TextEntryModalTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "TextEntryModalTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'TextEntryModalTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "TextEntryModalTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AccessibilityTreeTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityTreeTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        "/Fo:$accessibilityTreeTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityTreeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityTreeTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityTreeTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityTreeTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TrayAccessibilityTests {
    $providerArguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityProviderTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$accessibilityProviderTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityProviderTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $providerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityProviderTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests failed with exit code $LASTEXITCODE."
    }

    $hostArguments = $common + @(
        (Join-Path $projectDirectory 'HostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$hostAccessibilityTestObjectDirectory\",
        "/Fe:$outputDirectory\HostAccessibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $hostArguments
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'HostAccessibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests failed with exit code $LASTEXITCODE."
    }

    $stateArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayStateTests.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        "/Fo:$testObjectDirectory\",
        "/Fe:$outputDirectory\OverlayStateTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $stateArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayStateTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-LocalPackageImportTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'LocalWidgetPackageImportTests.cpp'),
        (Join-Path $projectDirectory 'LocalWidgetPackageImport.cpp'),
        "/Fo:$localPackageImportTestObjectDirectory\",
        "/Fe:$outputDirectory\LocalWidgetPackageImportTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib', 'ole32.lib', 'uuid.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "LocalWidgetPackageImportTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'LocalWidgetPackageImportTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "LocalWidgetPackageImportTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-CompositionTests {
    $placementArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
        (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'),
        "/Fo:$placementTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlacementTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $placementArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayPlacementTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests failed with exit code $LASTEXITCODE."
    }

    $targetingArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTargetingTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformTargeting.cpp'),
        "/Fo:$targetingTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayTargetingTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $targetingArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayTargetingTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests failed with exit code $LASTEXITCODE."
    }

    $transitionArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTransitionTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTransition.cpp'),
        "/Fo:$transitionTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayTransitionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $transitionArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTransitionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayTransitionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTransitionTests failed with exit code $LASTEXITCODE."
    }

    $chromeArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayChromeTests.cpp'),
        (Join-Path $projectDirectory 'OverlayChrome.cpp'),
        "/Fo:$chromeTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayChromeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('d2d1.lib', 'windowscodecs.lib', 'ole32.lib')
    & $cl $chromeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayChromeTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayChromeTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayChromeTests failed with exit code $LASTEXITCODE."
    }

    $providerArguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityProviderTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$accessibilityProviderTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityProviderTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $providerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityProviderTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests failed with exit code $LASTEXITCODE."
    }

    $focusArguments = $common + @(
        (Join-Path $projectDirectory 'FocusNavigationTests.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        "/Fo:$focusTestObjectDirectory\",
        "/Fe:$outputDirectory\FocusNavigationTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $focusArguments
    if ($LASTEXITCODE -ne 0) {
        throw "FocusNavigationTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'FocusNavigationTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "FocusNavigationTests failed with exit code $LASTEXITCODE."
    }

    $hostAccessibilityArguments = $common + @(
        (Join-Path $projectDirectory 'HostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$hostAccessibilityTestObjectDirectory\",
        "/Fe:$outputDirectory\HostAccessibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $hostAccessibilityArguments
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'HostAccessibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests failed with exit code $LASTEXITCODE."
    }

    $rendererArguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$rendererTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeRendererTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $rendererArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeRendererTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-WidgetSwitchHostTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'WidgetSwitchHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$widgetSwitchHostTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetSwitchHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSwitchHostTests build failed with exit code $LASTEXITCODE."
    }
    & dotnet publish `
        (Join-Path $projectDirectory '..\..\tests\WidgetSwitchFixture\WidgetSwitchFixture.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $widgetSwitchFixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Widget switch fixture publish failed with exit code $LASTEXITCODE."
    }
    $fixture = Join-Path $widgetSwitchFixtureOutput 'WidgetSwitchFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'Widget switch fixture publish omitted WidgetSwitchFixture.exe.'
    }
    & (Join-Path $outputDirectory 'WidgetSwitchHostTests.exe') `
        --installation $outputDirectory `
        --fixture-worker $fixture
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSwitchHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TrayRefreshHostTests {
    Invoke-WidgetBridgeCatalogTests

    $navigationArguments = $common + @(
        (Join-Path $projectDirectory 'ControllerNavigationTests.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'),
        "/Fo:$navigationTestObjectDirectory\",
        "/Fe:$outputDirectory\ControllerNavigationTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $navigationArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerNavigationTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'ControllerNavigationTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerNavigationTests failed with exit code $LASTEXITCODE."
    }

    $placementArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
        (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'),
        "/Fo:$placementTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlacementTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $placementArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayPlacementTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests failed with exit code $LASTEXITCODE."
    }

    $hostArguments = $common + @(
        (Join-Path $projectDirectory 'TrayRefreshHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$trayRefreshHostTestObjectDirectory\",
        "/Fe:$outputDirectory\TrayRefreshHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib', 'ole32.lib')
    & $cl $hostArguments
    if ($LASTEXITCODE -ne 0) {
        throw "TrayRefreshHostTests build failed with exit code $LASTEXITCODE."
    }
    & dotnet publish `
        (Join-Path $projectDirectory '..\..\tests\TrayRefreshCommunityFixture\TrayRefreshCommunityFixture.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $trayRefreshCommunityFixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Tray refresh Community fixture publish failed with exit code $LASTEXITCODE."
    }
    $fixture = Join-Path $trayRefreshCommunityFixtureOutput 'TrayRefreshCommunityFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'Tray refresh Community fixture publish omitted its executable.'
    }
    & (Join-Path $outputDirectory 'TrayRefreshHostTests.exe') `
        --installation $outputDirectory `
        --community-fixture $fixture
    if ($LASTEXITCODE -ne 0) {
        throw "TrayRefreshHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ColdDashboardTests {
    $placementArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
        (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'),
        "/Fo:$placementTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayPlacementTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $placementArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayPlacementTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayPlacementTests failed with exit code $LASTEXITCODE."
    }

    $targetingArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTargetingTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformTargeting.cpp'),
        "/Fo:$targetingTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayTargetingTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $targetingArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayTargetingTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTargetingTests failed with exit code $LASTEXITCODE."
    }

    $trayLayoutArguments = $common + @(
        (Join-Path $projectDirectory 'TrayLayoutTests.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$trayLayoutTestObjectDirectory\",
        "/Fe:$outputDirectory\TrayLayoutTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $trayLayoutArguments
    if ($LASTEXITCODE -ne 0) {
        throw "TrayLayoutTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'TrayLayoutTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "TrayLayoutTests failed with exit code $LASTEXITCODE."
    }

    $hostAccessibilityArguments = $common + @(
        (Join-Path $projectDirectory 'HostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$hostAccessibilityTestObjectDirectory\",
        "/Fe:$outputDirectory\HostAccessibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $hostAccessibilityArguments
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'HostAccessibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests failed with exit code $LASTEXITCODE."
    }

    $arguments = $common + @(
        (Join-Path $projectDirectory 'ColdDashboardHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$coldDashboardHostTestObjectDirectory\",
        "/Fe:$outputDirectory\ColdDashboardHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ColdDashboardHostTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'ColdDashboardHostTests.exe') `
        --installation $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "ColdDashboardHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-OverlayProcessOwnerTests {
    $arguments = $common + @(
        '/DGBA_OVERLAY_PROCESS_OWNER_TESTING',
        (Join-Path $projectDirectory 'OverlayProcessOwnerTests.cpp'),
        (Join-Path $projectDirectory 'OverlayProcessOwner.cpp'),
        "/Fo:$processOwnerTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayProcessOwnerTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib', 'advapi32.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayProcessOwnerTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayProcessOwnerTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayProcessOwnerTests failed with exit code $LASTEXITCODE."
    }

    $hostExecutable = Join-Path $outputDirectory 'OverlayHost.exe'
    if (Test-Path -LiteralPath $hostExecutable) {
        $profile = 'dlv070-exact-' + [Guid]::NewGuid().ToString('N')
        $owner = Start-Process -FilePath $hostExecutable -ArgumentList @(
            '--hidden', '--process-owner-probe', '--process-profile', $profile
        ) -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds 250
        $client = Start-Process -FilePath $hostExecutable -ArgumentList @(
            '--show', '--process-profile', $profile
        ) -PassThru -WindowStyle Hidden
        $clientDone = $client.WaitForExit(5000)
        $ownerDone = $owner.WaitForExit(12000)
        if (-not $clientDone -or -not $ownerDone -or
            $client.ExitCode -ne 0 -or $owner.ExitCode -ne 0) {
            throw 'Exact OverlayHost no-HWND owner/client fixture failed.'
        }
        Write-Host "OverlayHost exact owner/client fixture passed (owner PID $($owner.Id), client PID $($client.Id))."
    }
}

if ($SemanticChurnTestsOnly) {
    if ($SkipTests) {
        throw 'SemanticChurnTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-SemanticChurnPerformanceTests
    return
}

if ($DeclarativeLayoutTestsOnly) {
    if ($SkipTests) {
        throw 'DeclarativeLayoutTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-DeclarativeLayoutTests
    return
}

if ($DeclarativeRendererTestsOnly) {
    if ($SkipTests) {
        throw 'DeclarativeRendererTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-DeclarativeRendererTests
    return
}

if ($PinnedSurfaceTestsOnly) {
    if ($SkipTests) {
        throw 'PinnedSurfaceTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-PinnedSurfaceHostTests
    return
}

if ($PinnedPlacementTestsOnly) {
    if ($SkipTests) {
        throw 'PinnedPlacementTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-PinnedSurfacePlacementTests
    return
}

if ($WidgetSurfaceTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetSurfaceTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetSurfaceCoordinatorTests
    return
}

if ($WidgetBridgeCatalogTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetBridgeCatalogTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetBridgeCatalogTests
    return
}

if ($ProcessOwnerTestsOnly) {
    if ($SkipTests) {
        throw 'ProcessOwnerTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-OverlayProcessOwnerTests
    return
}

if ($LocalPackageImportTestsOnly) {
    if ($SkipTests) {
        throw 'LocalPackageImportTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-LocalPackageImportTests
    return
}

function Invoke-TrustedArtworkTests {
    $imageArguments = $common + @(
        (Join-Path $projectDirectory 'RemoteImageCacheTests.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$imageTestObjectDirectory\",
        "/Fe:$outputDirectory\RemoteImageCacheTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'winhttp.lib', 'windowscodecs.lib', 'ole32.lib', 'd2d1.lib'
    )
    & $cl $imageArguments
    if ($LASTEXITCODE -ne 0) {
        throw "RemoteImageCacheTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'RemoteImageCacheTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "RemoteImageCacheTests failed with exit code $LASTEXITCODE."
    }

    $rendererArguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$rendererTestObjectDirectory\",
        "/Fe:$outputDirectory\DeclarativeRendererTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $rendererArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'DeclarativeRendererTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "DeclarativeRendererTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-WidgetSessionTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'WidgetSessionCoordinatorTests.cpp'),
        (Join-Path $projectDirectory 'WidgetSessionCoordinator.cpp'),
        "/Fo:$widgetSessionTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetSessionCoordinatorTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('user32.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSessionCoordinatorTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetSessionCoordinatorTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSessionCoordinatorTests failed with exit code $LASTEXITCODE."
    }

    $stateArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayStateTests.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        "/Fo:$testObjectDirectory\",
        "/Fe:$outputDirectory\OverlayStateTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $stateArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayStateTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayStateTests failed with exit code $LASTEXITCODE."
    }

    $lifecycleArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetLifecycleTests.cpp'),
        (Join-Path $projectDirectory 'WidgetLifecycle.cpp'),
        "/Fo:$lifecycleTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetLifecycleTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $lifecycleArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetLifecycleTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetLifecycleTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetLifecycleTests failed with exit code $LASTEXITCODE."
    }

    $feedbackArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetActionFeedbackTests.cpp'),
        (Join-Path $projectDirectory 'WidgetActionFeedback.cpp'),
        "/Fo:$actionFeedbackTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetActionFeedbackTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $feedbackArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFeedbackTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetActionFeedbackTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFeedbackTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-LauncherExperienceTests {
    $arguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'LauncherExperienceTests.cpp'),
        (Join-Path $projectDirectory 'LauncherExperienceLayout.cpp'),
        (Join-Path $projectDirectory 'LauncherExperienceAdapter.cpp'),
        (Join-Path $projectDirectory 'LauncherExperienceProjection.cpp'),
        (Join-Path $projectDirectory 'LauncherExperiencePresentation.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$launcherExperienceTestObjectDirectory\",
        "/Fe:$outputDirectory\LauncherExperienceTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'LauncherExperienceTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-LauncherExperienceHostTests {
    param(
        [switch]$TextEntryOnly,
        [switch]$TrayInvokeOnly,
        [switch]$LifecycleOnly
    )
    & dotnet publish (Join-Path $projectDirectory '..\..\tests\LauncherExperienceBridgeFixture\LauncherExperienceBridgeFixture.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $launcherExperienceBridgeFixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceBridgeFixture publish failed with exit code $LASTEXITCODE."
    }
    $fixtureBridge = Join-Path $launcherExperienceBridgeFixtureOutput 'LauncherExperienceBridgeFixture.exe'
    if (-not (Test-Path -LiteralPath $fixtureBridge)) {
        throw 'LauncherExperienceBridgeFixture publish omitted its executable.'
    }
    $arguments = $common + @(
        (Join-Path $projectDirectory 'LauncherExperienceHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$launcherExperienceHostTestObjectDirectory\",
        "/Fe:$outputDirectory\LauncherExperienceHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceHostTests build failed with exit code $LASTEXITCODE."
    }
    $testArguments = @(
        '--installation', $outputDirectory,
        '--fixture-bridge', $fixtureBridge
    )
    if ($TextEntryOnly) {
        $testArguments += '--text-entry-only'
    } elseif ($TrayInvokeOnly) {
        $testArguments += '--tray-invoke-only'
    } elseif ($LifecycleOnly) {
        $previousInstallation = $env:GBA_LAUNCHER_LIFECYCLE_INSTALLATION
        $previousHostTest = $env:GBA_LAUNCHER_LIFECYCLE_HOST_TEST
        $previousFixtureBridge = $env:GBA_LAUNCHER_LIFECYCLE_FIXTURE_BRIDGE
        try {
            $env:GBA_LAUNCHER_LIFECYCLE_INSTALLATION = $outputDirectory
            $env:GBA_LAUNCHER_LIFECYCLE_HOST_TEST = Join-Path $outputDirectory 'LauncherExperienceHostTests.exe'
            $env:GBA_LAUNCHER_LIFECYCLE_FIXTURE_BRIDGE = $fixtureBridge
            & dotnet run `
                --project (Join-Path $projectDirectory '..\..\tests\GbarCli.Tests\GbarCli.Tests.csproj') `
                --configuration $Configuration -- `
                --test 'Launcher Experience author-to-production lifecycle is exact'
            if ($LASTEXITCODE -ne 0) {
                throw "Launcher Experience lifecycle coordinator failed with exit code $LASTEXITCODE."
            }
        } finally {
            $env:GBA_LAUNCHER_LIFECYCLE_INSTALLATION = $previousInstallation
            $env:GBA_LAUNCHER_LIFECYCLE_HOST_TEST = $previousHostTest
            $env:GBA_LAUNCHER_LIFECYCLE_FIXTURE_BRIDGE = $previousFixtureBridge
        }
        return
    }
    & (Join-Path $outputDirectory 'LauncherExperienceHostTests.exe') $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AdvancedPresentationHostTests {
    & dotnet publish `
        (Join-Path $projectDirectory '..\..\tests\AdvancedPresentationCommunityFixture\AdvancedPresentationCommunityFixture.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $advancedPresentationCommunityFixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw "AdvancedPresentationCommunityFixture publish failed with exit code $LASTEXITCODE."
    }
    $fixture = Join-Path $advancedPresentationCommunityFixtureOutput 'AdvancedPresentationCommunityFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'AdvancedPresentationCommunityFixture publish omitted its executable.'
    }
    $gbarOutput = Join-Path $advancedPresentationCommunityFixtureOutput 'gbar'
    & dotnet publish `
        (Join-Path $projectDirectory '..\..\tools\GbarCli\GbarCli.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $gbarOutput
    if ($LASTEXITCODE -ne 0) {
        throw "gbar publish for the DLV-212 export failed with exit code $LASTEXITCODE."
    }
    $gbar = Join-Path $gbarOutput 'gbar.exe'
    & dotnet publish `
        (Join-Path $projectDirectory '..\..\tests\LauncherExperienceBridgeFixture\LauncherExperienceBridgeFixture.csproj') `
        --configuration $Configuration --no-self-contained --nologo `
        --output $launcherExperienceBridgeFixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw "LauncherExperienceBridgeFixture publish failed with exit code $LASTEXITCODE."
    }
    $fixtureBridge = Join-Path $launcherExperienceBridgeFixtureOutput 'LauncherExperienceBridgeFixture.exe'
    if (-not (Test-Path -LiteralPath $gbar) -or
        -not (Test-Path -LiteralPath $fixtureBridge)) {
        throw 'The exported-candidate host fixture omitted gbar or its seeded bridge.'
    }
    $arguments = $common + @(
        (Join-Path $projectDirectory 'AdvancedPresentationHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$advancedPresentationHostTestObjectDirectory\",
        "/Fe:$outputDirectory\AdvancedPresentationHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AdvancedPresentationHostTests build failed with exit code $LASTEXITCODE."
    }
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $runRoot = Join-Path $temporaryRoot ("gba-dlv213-export-" + [Guid]::NewGuid().ToString('N'))
    $candidateSource = Join-Path $runRoot 'GameLauncherCommunity'
    $candidatePackage = Join-Path $runRoot 'org.gbar.community.reference.game-launcher-0.1.0.gbarwidget'
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    try {
        & (Join-Path $projectDirectory '..\FirstPartyWidgets\GameLauncherWidget\Export-CommunityReference.ps1') `
            -Gbar $gbar -Output $candidateSource
        if ($LASTEXITCODE -ne 0) {
            throw "Game Launcher Community export failed with exit code $LASTEXITCODE."
        }
        & $gbar pack $candidateSource --configuration $Configuration --output $candidatePackage
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $candidatePackage)) {
            throw "Game Launcher Community pack failed with exit code $LASTEXITCODE."
        }
        & (Join-Path $outputDirectory 'AdvancedPresentationHostTests.exe') `
            --installation $outputDirectory `
            --community-fixture $fixture `
            --candidate-package $candidatePackage `
            --fixture-bridge $fixtureBridge
        if ($LASTEXITCODE -ne 0) {
            throw "AdvancedPresentationHostTests failed with exit code $LASTEXITCODE."
        }
    } finally {
        $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
        if (-not $resolvedRunRoot.StartsWith(
                $temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an exported-candidate path outside the temporary root.'
        }
        if (Test-Path -LiteralPath $resolvedRunRoot) {
            Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        }
    }
}

if ($LauncherExperienceTestsOnly) {
    if ($SkipTests) {
        throw 'LauncherExperienceTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-LauncherExperienceTests
    return
}

if ($CompositionTestsOnly) {
    if ($SkipTests) {
        throw 'CompositionTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-CompositionTests
    return
}

if ($TrustedArtworkTestsOnly) {
    if ($SkipTests) {
        throw 'TrustedArtworkTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-TrustedArtworkTests
    return
}

if ($WidgetSessionTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetSessionTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetSessionTests
    return
}

if ($PlatformInteropTestsOnly) {
    if ($SkipTests) {
        throw 'PlatformInteropTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-OverlayPlatformInteropBuild
    Invoke-OverlayPlatformInteropTests
    Invoke-OverlayPlatformParityTests
    return
}

Invoke-OverlayPlatformInteropBuild

$hostArguments = $common + @(
    '/DGBA_OVERLAY_PLATFORM_IMPORTS',
    (Join-Path $projectDirectory 'main.cpp'),
    (Join-Path $projectDirectory 'OverlayCompositionSurface.cpp'),
    (Join-Path $projectDirectory 'OverlayProcessOwner.cpp'),
    (Join-Path $projectDirectory 'OverlayState.cpp'),
    (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
    (Join-Path $projectDirectory 'LocalWidgetPackageImport.cpp'),
    (Join-Path $projectDirectory 'WidgetSessionCoordinator.cpp'),
    (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
    (Join-Path $projectDirectory 'ScrollEvidenceProbe.cpp'),
    (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
    (Join-Path $projectDirectory 'NativeIcons.cpp'),
    (Join-Path $projectDirectory 'NativeStyle.cpp'),
    (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
    (Join-Path $projectDirectory 'OverlayChrome.cpp'),
    (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
    (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
    (Join-Path $projectDirectory 'PinnedSurfacePolicy.cpp'),
    (Join-Path $projectDirectory 'PinnedSurfacePlacement.cpp'),
    (Join-Path $projectDirectory 'WidgetSurfaceCoordinator.cpp'),
    (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
    (Join-Path $projectDirectory 'OverlayTransition.cpp'),
    (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
    (Join-Path $projectDirectory 'LauncherExperienceLayout.cpp'),
    (Join-Path $projectDirectory 'LauncherExperienceAdapter.cpp'),
    (Join-Path $projectDirectory 'LauncherExperienceProjection.cpp'),
    (Join-Path $projectDirectory 'LauncherExperiencePresentation.cpp'),
    (Join-Path $projectDirectory 'LauncherExperienceHostProof.cpp'),
    (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
    (Join-Path $projectDirectory 'SliderInteraction.cpp'),
    (Join-Path $projectDirectory 'TextEntryActionAdmission.cpp'),
    (Join-Path $projectDirectory 'TextEntryModal.cpp'),
    (Join-Path $projectDirectory 'FocusNavigation.cpp'),
    (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
    (Join-Path $projectDirectory 'WidgetLifecycle.cpp'),
    (Join-Path $projectDirectory 'WidgetActionFeedback.cpp'),
    (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
    (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
    (Join-Path $projectDirectory 'TrayLayout.cpp'),
    (Join-Path $projectDirectory 'HostAccessibility.cpp'),
    (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
    "/Fo:$hostObjectDirectory\",
    "/Fe:$outputDirectory\OverlayHost.exe",
    '/link'
) + $libraryArguments + @(
    '/SUBSYSTEM:WINDOWS',
    '/MANIFEST:EMBED',
    "/MANIFESTINPUT:$(Join-Path $projectDirectory 'app.manifest')",
    'user32.lib', 'gdi32.lib', 'd2d1.lib', 'dwrite.lib', 'dwmapi.lib',
    'd3d11.lib', 'dxgi.lib', 'dcomp.lib',
    'gameinput.lib', 'shcore.lib', 'xinput9_1_0.lib', 'windowsapp.lib',
    (Join-Path $outputDirectory 'OverlayPlatformInterop.lib'),
    'winhttp.lib', 'windowscodecs.lib', 'ole32.lib', 'oleaut32.lib',
    'uiautomationcore.lib', 'advapi32.lib', 'uuid.lib'
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
    $spotifyPlaybackHostOutput = Join-Path $outputDirectory 'runtime\SpotifyPlaybackHost'
    $workerHostOutput = Join-Path $outputDirectory 'runtime\WidgetWorkerHost'
    $settingsOutput = Join-Path $outputDirectory 'runtime\Settings'
    $audioMixerOutput = Join-Path $outputDirectory 'runtime\AudioMixer'
    $networkControlsOutput = Join-Path $outputDirectory 'runtime\NetworkControls'
    $gamesAppsOutput = Join-Path $outputDirectory 'runtime\GamesApps'
    $gameLauncherOutput = Join-Path $outputDirectory 'runtime\GameLauncher'
    $mediaSessionsOutput = Join-Path $outputDirectory 'runtime\MediaSessions'
    # YT Music is a community addon now. Remove an incremental build's retired
    # trusted worker so it cannot remain as an accidental fallback.
    Remove-GeneratedDirectory -Path (Join-Path $outputDirectory 'runtime\YtMusic')
    & dotnet publish (Join-Path $projectDirectory '..\WidgetBridge\WidgetBridge.csproj') `
        --configuration $Configuration --no-self-contained --nologo --output $bridgeOutput
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetBridge publish failed with exit code $LASTEXITCODE."
    }
    & dotnet publish (Join-Path $projectDirectory '..\SpotifyPlaybackHost\SpotifyPlaybackHost.csproj') `
        --configuration $Configuration --no-self-contained --nologo --output $spotifyPlaybackHostOutput
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath (Join-Path $spotifyPlaybackHostOutput 'SpotifyPlaybackHost.exe')) -or
        -not (Test-Path -LiteralPath (Join-Path $spotifyPlaybackHostOutput 'WebView2Loader.dll'))) {
        throw "Spotify playback host publish failed with exit code $LASTEXITCODE."
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
        (Join-Path $projectDirectory '..\FirstPartyWidgets\GameLauncherWidget') `
        $gameLauncherOutput 'GameLauncherWidget' 'Game Launcher'
    Publish-BundledWidgetPackage `
        (Join-Path $projectDirectory '..\FirstPartyWidgets\MediaSessionsWidget') `
        $mediaSessionsOutput 'MediaSessionsWidget' 'Now Playing'
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'widget-catalog.json') `
        -Destination (Join-Path $outputDirectory 'widget-catalog.json') -Force
}

if ($WidgetSwitchTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetSwitchTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetSwitchHostTests
    return
}

if ($ColdDashboardTestsOnly) {
    if ($SkipTests) {
        throw 'ColdDashboardTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-ColdDashboardTests
    return
}

if ($LauncherExperienceHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'LauncherExperienceHostTestsOnly requires tests and packaging.'
    }
    Invoke-LauncherExperienceHostTests
    return
}

if ($AdvancedPresentationHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'AdvancedPresentationHostTestsOnly requires tests and packaging.'
    }
    Invoke-AdvancedPresentationHostTests
    return
}

if ($TextEntryHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TextEntryHostTestsOnly requires tests and packaging.'
    }
    Invoke-TextEntryModalTests
    Invoke-AccessibilityTreeTests
    Invoke-LauncherExperienceHostTests -TextEntryOnly
    return
}

if ($TrayAccessibilityHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TrayAccessibilityHostTestsOnly requires tests and packaging.'
    }
    Invoke-TrayAccessibilityTests
    Invoke-LauncherExperienceHostTests -TrayInvokeOnly
    return
}

if ($TrayRefreshHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TrayRefreshHostTestsOnly requires tests and packaging.'
    }
    Invoke-TrayRefreshHostTests
    return
}

if ($LauncherExperienceLifecycleTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'LauncherExperienceLifecycleTestsOnly requires tests and packaging.'
    }
    Invoke-LauncherExperienceTests
    Invoke-LauncherExperienceHostTests -LifecycleOnly
    return
}

if (-not $SkipTests) {
    $scrollEvidenceProbeTestArguments = $common + @(
        (Join-Path $projectDirectory 'ScrollEvidenceProbeTests.cpp'),
        (Join-Path $projectDirectory 'ScrollEvidenceProbe.cpp'),
        "/Fo:$scrollEvidenceProbeTestObjectDirectory\",
        "/Fe:$outputDirectory\ScrollEvidenceProbeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $scrollEvidenceProbeTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ScrollEvidenceProbeTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'ScrollEvidenceProbeTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "ScrollEvidenceProbeTests failed with exit code $LASTEXITCODE."
    }

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

    $textLayoutTestArguments = $common + @(
        (Join-Path $projectDirectory 'NativeTextLayoutTests.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        "/Fo:$textLayoutTestObjectDirectory\",
        "/Fe:$outputDirectory\NativeTextLayoutTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('dwrite.lib', 'ole32.lib')
    & $cl $textLayoutTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "NativeTextLayoutTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'NativeTextLayoutTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "NativeTextLayoutTests failed with exit code $LASTEXITCODE."
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

    Invoke-WidgetBridgeCatalogTests
    Invoke-TextEntryModalTests

    $placementTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayPlacementTests.cpp'),
        (Join-Path $projectDirectory 'OverlayPlacement.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPlacement.cpp'),
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

    Invoke-PinnedSurfaceHostTests
    Invoke-PinnedSurfacePlacementTests
    Invoke-WidgetSurfaceCoordinatorTests
    Invoke-OverlayProcessOwnerTests

    $targetingTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTargetingTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTargeting.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformTargeting.cpp'),
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

    $transitionTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayTransitionTests.cpp'),
        (Join-Path $projectDirectory 'OverlayTransition.cpp'),
        "/Fo:$transitionTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayTransitionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $transitionTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTransitionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayTransitionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayTransitionTests failed with exit code $LASTEXITCODE."
    }

    $chromeTestArguments = $common + @(
        (Join-Path $projectDirectory 'OverlayChromeTests.cpp'),
        (Join-Path $projectDirectory 'OverlayChrome.cpp'),
        "/Fo:$chromeTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayChromeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('d2d1.lib', 'windowscodecs.lib', 'ole32.lib')
    & $cl $chromeTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayChromeTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'OverlayChromeTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "OverlayChromeTests failed with exit code $LASTEXITCODE."
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

    $inputOwnershipTestArguments = $common + @(
        (Join-Path $projectDirectory 'ControllerInputOwnershipTests.cpp'),
        "/Fo:$inputOwnershipTestObjectDirectory\",
        "/Fe:$outputDirectory\ControllerInputOwnershipTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $inputOwnershipTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerInputOwnershipTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'ControllerInputOwnershipTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "ControllerInputOwnershipTests failed with exit code $LASTEXITCODE."
    }

    $navigationTestArguments = $common + @(
        (Join-Path $projectDirectory 'ControllerNavigationTests.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'),
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

    $pressedTestArguments = $common + @(
        (Join-Path $projectDirectory 'PressedInteractionTests.cpp'),
        "/Fo:$pressedTestObjectDirectory\",
        "/Fe:$outputDirectory\PressedInteractionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $pressedTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "PressedInteractionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'PressedInteractionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "PressedInteractionTests failed with exit code $LASTEXITCODE."
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

    $actionFeedbackTestArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetActionFeedbackTests.cpp'),
        (Join-Path $projectDirectory 'WidgetActionFeedback.cpp'),
        "/Fo:$actionFeedbackTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetActionFeedbackTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $actionFeedbackTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFeedbackTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'WidgetActionFeedbackTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFeedbackTests failed with exit code $LASTEXITCODE."
    }

    Invoke-AccessibilityTreeTests

    $accessibilityProjectionTestArguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityProjectionTests.cpp'),
        "/Fo:$accessibilityProjectionTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityProjectionTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $accessibilityProjectionTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProjectionTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityProjectionTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProjectionTests failed with exit code $LASTEXITCODE."
    }

    $accessibilityProviderTestArguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityProviderTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$accessibilityProviderTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityProviderTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $accessibilityProviderTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityProviderTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityProviderTests failed with exit code $LASTEXITCODE."
    }

    $realHostAccessibilityTestArguments = $common + @(
        '/DGBA_WIDGET_BRIDGE_CLIENT_TESTING',
        (Join-Path $projectDirectory 'RealHostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        "/Fo:$realHostAccessibilityTestObjectDirectory\",
        "/Fe:$outputDirectory\RealHostAccessibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'windowsapp.lib', 'user32.lib', 'ole32.lib', 'oleaut32.lib',
        'uiautomationcore.lib', 'd2d1.lib', 'dwrite.lib', 'winhttp.lib',
        'windowscodecs.lib'
    )
    & $cl $realHostAccessibilityTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "RealHostAccessibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'RealHostAccessibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "RealHostAccessibilityTests failed with exit code $LASTEXITCODE."
    }

    $actionFailureHostTestArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetActionFailureHostTests.cpp'),
        "/Fo:$actionFailureHostTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetActionFailureHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $actionFailureHostTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFailureHostTests build failed with exit code $LASTEXITCODE."
    }
    if (-not $SkipPackaging) {
        & dotnet publish `
            (Join-Path $projectDirectory '..\..\tests\AdvancedActionFailureFixture\AdvancedActionFailureFixture.csproj') `
            --configuration $Configuration --no-self-contained --nologo `
            --output $actionFailureFixtureOutput
        if ($LASTEXITCODE -ne 0) {
            throw "Advanced action-failure fixture publish failed with exit code $LASTEXITCODE."
        }
        $actionFailureFixture = Join-Path $actionFailureFixtureOutput 'AdvancedActionFailureFixture.exe'
        if (-not (Test-Path -LiteralPath $actionFailureFixture)) {
            throw "Advanced action-failure fixture publish omitted AdvancedActionFailureFixture.exe."
        }
        & (Join-Path $outputDirectory 'WidgetActionFailureHostTests.exe') `
            --installation $outputDirectory `
            --fixture-worker $actionFailureFixture
        if ($LASTEXITCODE -ne 0) {
            throw "WidgetActionFailureHostTests failed with exit code $LASTEXITCODE."
        }
    }

    $coldDashboardHostTestArguments = $common + @(
        (Join-Path $projectDirectory 'ColdDashboardHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$coldDashboardHostTestObjectDirectory\",
        "/Fe:$outputDirectory\ColdDashboardHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $coldDashboardHostTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ColdDashboardHostTests build failed with exit code $LASTEXITCODE."
    }
    if (-not $SkipPackaging) {
        & (Join-Path $outputDirectory 'ColdDashboardHostTests.exe') `
            --installation $outputDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "ColdDashboardHostTests failed with exit code $LASTEXITCODE."
        }
    }

    $widgetSwitchHostTestArguments = $common + @(
        (Join-Path $projectDirectory 'WidgetSwitchHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$widgetSwitchHostTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetSwitchHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $widgetSwitchHostTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSwitchHostTests build failed with exit code $LASTEXITCODE."
    }
    if (-not $SkipPackaging) {
        & dotnet publish `
            (Join-Path $projectDirectory '..\..\tests\WidgetSwitchFixture\WidgetSwitchFixture.csproj') `
            --configuration $Configuration --no-self-contained --nologo `
            --output $widgetSwitchFixtureOutput
        if ($LASTEXITCODE -ne 0) {
            throw "Widget switch fixture publish failed with exit code $LASTEXITCODE."
        }
        $widgetSwitchFixture = Join-Path $widgetSwitchFixtureOutput 'WidgetSwitchFixture.exe'
        if (-not (Test-Path -LiteralPath $widgetSwitchFixture)) {
            throw "Widget switch fixture publish omitted WidgetSwitchFixture.exe."
        }
        & (Join-Path $outputDirectory 'WidgetSwitchHostTests.exe') `
            --installation $outputDirectory `
            --fixture-worker $widgetSwitchFixture
        if ($LASTEXITCODE -ne 0) {
            throw "WidgetSwitchHostTests failed with exit code $LASTEXITCODE."
        }
    }

    $audioMixerScrollHostTestArguments = $common + @(
        (Join-Path $projectDirectory 'AudioMixerScrollHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$audioMixerScrollHostTestObjectDirectory\",
        "/Fe:$outputDirectory\AudioMixerScrollHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'windowscodecs.lib', 'ole32.lib',
        'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $audioMixerScrollHostTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AudioMixerScrollHostTests build failed with exit code $LASTEXITCODE."
    }
    if (-not $SkipPackaging) {
        & dotnet publish `
            (Join-Path $projectDirectory '..\..\tests\AudioMixerScrollFixture\AudioMixerScrollFixture.csproj') `
            --configuration $Configuration --no-self-contained --nologo `
            --output $audioMixerScrollFixtureOutput
        if ($LASTEXITCODE -ne 0) {
            throw "Audio Mixer scroll fixture publish failed with exit code $LASTEXITCODE."
        }
        $audioMixerScrollFixture = Join-Path $audioMixerScrollFixtureOutput 'AudioMixerScrollFixture.exe'
        if (-not (Test-Path -LiteralPath $audioMixerScrollFixture)) {
            throw "Audio Mixer scroll fixture publish omitted AudioMixerScrollFixture.exe."
        }
        & (Join-Path $outputDirectory 'AudioMixerScrollHostTests.exe') `
            --installation $outputDirectory `
            --fixture-worker $audioMixerScrollFixture
        if ($LASTEXITCODE -ne 0) {
            throw "AudioMixerScrollHostTests failed with exit code $LASTEXITCODE."
        }
    }

    $trayLayoutTestArguments = $common + @(
        (Join-Path $projectDirectory 'TrayLayoutTests.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$trayLayoutTestObjectDirectory\",
        "/Fe:$outputDirectory\TrayLayoutTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $trayLayoutTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "TrayLayoutTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'TrayLayoutTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "TrayLayoutTests failed with exit code $LASTEXITCODE."
    }

    $hostAccessibilityTestArguments = $common + @(
        (Join-Path $projectDirectory 'HostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$hostAccessibilityTestObjectDirectory\",
        "/Fe:$outputDirectory\HostAccessibilityTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $hostAccessibilityTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'HostAccessibilityTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "HostAccessibilityTests failed with exit code $LASTEXITCODE."
    }

    $accessibilityEventsTestArguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityEventsTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$accessibilityEventsTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityEventsTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $accessibilityEventsTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityEventsTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityEventsTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityEventsTests failed with exit code $LASTEXITCODE."
    }

    $rendererTestArguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
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

    Invoke-SemanticChurnPerformanceTests

    $componentGeometryTestArguments = $common + @(
        '/DGBA_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'SharedComponentGeometryTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        "/Fo:$componentGeometryTestObjectDirectory\",
        "/Fe:$outputDirectory\SharedComponentGeometryTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $componentGeometryTestArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SharedComponentGeometryTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'SharedComponentGeometryTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "SharedComponentGeometryTests failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Built $outputDirectory\OverlayHost.exe"
