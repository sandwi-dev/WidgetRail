[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',
    [switch]$NoRestore,
    [switch]$SkipTests,
    [switch]$SkipPackaging,
    [switch]$SemanticChurnTestsOnly,
    [switch]$DeclarativeLayoutTestsOnly,
    [switch]$DeclarativeRendererTestsOnly,
    [switch]$BackgroundSurfaceHostTestsOnly,
    [switch]$AccessibilityTreeTestsOnly,
    [switch]$ControllerGuideTestsOnly,
    [switch]$AccessibilityEventsTestsOnly,
    [switch]$PinnedSurfaceTestsOnly,
    [switch]$PinnedPlacementTestsOnly,
    [switch]$ProcessOwnerTestsOnly,
    [switch]$CompositionTestsOnly,
    [switch]$WidgetSwitchTestsOnly,
    [switch]$PinnedSliderRouteTestsOnly,
    [switch]$WidgetSwitchFallbackAuthorityTestsOnly,
    [string]$WidgetSwitchFallbackAuthorityReplayLog,
    [Int64]$WidgetSwitchFallbackAuthorityReplayMarker,
    [Int64]$WidgetSwitchFallbackAuthorityReplaySequence,
    [switch]$WidgetSwitchGeometryOnly,
    [switch]$TrustedArtworkTestsOnly,
    [switch]$WidgetSessionTestsOnly,
    [switch]$WidgetInteractionTestsOnly,
    [switch]$WidgetBridgeCatalogTestsOnly,
    [switch]$LocalPackageImportTestsOnly,
    [switch]$WidgetSurfaceTestsOnly,
    [switch]$ColdDashboardTestsOnly,
    [switch]$TextEntryHostTestsOnly,
    [switch]$TrayAccessibilityHostTestsOnly,
    [switch]$TrayRefreshHostTestsOnly,
    [switch]$PlatformInteropTestsOnly,
    [switch]$WidgetActionFailureHostTestsOnly,
    [switch]$AudioMixerScrollHostTestsOnly,
    [switch]$RichMediaTestsOnly,
    [switch]$RichMediaContractTestsOnly,
    [switch]$RichMediaPerformanceTestsOnly
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameInputVersion = '3.5.262'
$webView2Version = '1.0.4078.44'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$gameInputPackage = Join-Path $nugetRoot "microsoft.gameinput\$gameInputVersion"
$gameInputHeader = Join-Path $gameInputPackage 'native\include\GameInput.h'
$webView2Package = Join-Path $nugetRoot "microsoft.web.webview2\$webView2Version"
$webView2Header = Join-Path $webView2Package 'build\native\include\WebView2.h'
$webView2Loader = Join-Path $webView2Package "build\native\$Architecture\WebView2LoaderStatic.lib"
$restoredNuGetProjects = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

function Invoke-NuGetAuditedRestore {
    param([Parameter(Mandatory = $true)] [string]$Project)

    $resolvedProject = [System.IO.Path]::GetFullPath($Project)
    if (-not $restoredNuGetProjects.Add($resolvedProject)) {
        return
    }
    & dotnet restore $resolvedProject --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet-audited restore failed for $resolvedProject with exit code $LASTEXITCODE."
    }
}

if (-not $NoRestore) {
    Invoke-NuGetAuditedRestore -Project (Join-Path $projectDirectory 'NativeDependencies.csproj')
}
if (-not (Test-Path -LiteralPath $gameInputHeader)) {
    throw "Microsoft.GameInput $gameInputVersion is unavailable. Run this restore-bearing build with supported network access, or restore it before using -NoRestore."
}
if (-not (Test-Path -LiteralPath $webView2Header) -or
    -not (Test-Path -LiteralPath $webView2Loader)) {
    throw "Microsoft.Web.WebView2 $webView2Version is unavailable. Run this restore-bearing build with supported network access, or restore it before using -NoRestore."
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

function Invoke-SerializedManagedPublish {
    param(
        [Parameter(Mandatory = $true)] [string]$Project,
        [Parameter(Mandatory = $true)] [string]$Configuration,
        [Parameter(Mandatory = $true)] [string]$Output,
        [Parameter(Mandatory = $true)] [ref]$ExitCode
    )

    if (-not $NoRestore) {
        Invoke-NuGetAuditedRestore -Project $Project
    }
    & dotnet publish $Project --no-restore `
        --configuration $Configuration --no-self-contained --nologo --output $Output `
        -m:1 -p:BuildInParallel=false -nr:false -p:UseSharedCompilation=false
    $ExitCode.Value = $LASTEXITCODE
}

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
$previousCargoErrorActionPreference = $ErrorActionPreference
$cargoExitCode = $null
try {
    $env:CARGO_TARGET_DIR = $taffyTargetDirectory
    # Cargo reports ordinary compiler progress through stderr. Keep that stream
    # visible without turning it into a PowerShell exception; Cargo's exact exit
    # code remains the build authority below.
    $ErrorActionPreference = 'Continue'
    & $cargo $taffyArguments
    $cargoExitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $previousCargoErrorActionPreference
    $env:CARGO_TARGET_DIR = $previousCargoTargetDirectory
}
if ($cargoExitCode -ne 0) {
    throw "Pinned Taffy static-library build failed with exit code $cargoExitCode."
}
$taffyLibrary = Join-Path $taffyTargetDirectory "x86_64-pc-windows-msvc\$taffyProfile\wrail_taffy_layout.lib"
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
$artworkDecoderObjectDirectory = Join-Path $outputDirectory 'obj\artwork-decoder'
$artworkDecoderTestObjectDirectory = Join-Path $outputDirectory 'obj\artwork-decoder-tests'
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
$widgetInteractionTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-interaction-tests'
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
$backgroundSurfaceHostTestObjectDirectory = Join-Path $outputDirectory 'obj\background-surface-host-tests'
$semanticChurnTestObjectDirectory = Join-Path $outputDirectory 'obj\semantic-churn-performance-tests'
$pinnedSurfaceTestObjectDirectory = Join-Path $outputDirectory 'obj\pinned-surface-host-tests'
$pinnedPlacementTestObjectDirectory = Join-Path $outputDirectory 'obj\pinned-placement-tests'
$widgetSurfaceTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-surface-coordinator-tests'
$widgetSessionTestObjectDirectory = Join-Path $outputDirectory 'obj\widget-session-coordinator-tests'
$processOwnerTestObjectDirectory = Join-Path $outputDirectory 'obj\process-owner-tests'
$componentGeometryTestObjectDirectory = Join-Path $outputDirectory 'obj\component-geometry-tests'
$trayRefreshHostTestObjectDirectory = Join-Path $outputDirectory 'obj\tray-refresh-host-tests'
$trayRefreshCommunityFixtureOutput = Join-Path $outputDirectory 'obj\tray-refresh-community-fixture'
$richMediaTestObjectDirectory = Join-Path $outputDirectory 'obj\rich-media-tests'
$bundledPackageSealOutput = Join-Path $outputDirectory 'obj\bundled-package-seal'
New-Item -ItemType Directory -Force -Path $hostObjectDirectory, $platformObjectDirectory, $platformTestObjectDirectory, $testObjectDirectory, $imageTestObjectDirectory, $artworkDecoderObjectDirectory, $artworkDecoderTestObjectDirectory, $layoutTestObjectDirectory, $iconTestObjectDirectory, $styleTestObjectDirectory, $textLayoutTestObjectDirectory, $motionTestObjectDirectory, $placementTestObjectDirectory, $targetingTestObjectDirectory, $transitionTestObjectDirectory, $chromeTestObjectDirectory, $guideTestObjectDirectory, $inputOwnershipTestObjectDirectory, $navigationTestObjectDirectory, $pressedTestObjectDirectory, $sliderTestObjectDirectory, $widgetInteractionTestObjectDirectory, $focusTestObjectDirectory, $surfaceFocusTestObjectDirectory, $lifecycleTestObjectDirectory, $actionFeedbackTestObjectDirectory, $accessibilityTreeTestObjectDirectory, $accessibilityProjectionTestObjectDirectory, $accessibilityProviderTestObjectDirectory, $realHostAccessibilityTestObjectDirectory, $actionFailureHostTestObjectDirectory, $actionFailureFixtureOutput, $widgetSwitchHostTestObjectDirectory, $coldDashboardHostTestObjectDirectory, $widgetSwitchFixtureOutput, $audioMixerScrollHostTestObjectDirectory, $audioMixerScrollFixtureOutput, $scrollEvidenceProbeTestObjectDirectory, $trayLayoutTestObjectDirectory, $hostAccessibilityTestObjectDirectory, $accessibilityEventsTestObjectDirectory, $bridgeCatalogTestObjectDirectory, $localPackageImportTestObjectDirectory, $textEntryModalTestObjectDirectory, $rendererTestObjectDirectory, $backgroundSurfaceHostTestObjectDirectory, $semanticChurnTestObjectDirectory, $pinnedSurfaceTestObjectDirectory, $pinnedPlacementTestObjectDirectory, $widgetSurfaceTestObjectDirectory, $widgetSessionTestObjectDirectory, $processOwnerTestObjectDirectory, $componentGeometryTestObjectDirectory, $trayRefreshHostTestObjectDirectory, $trayRefreshCommunityFixtureOutput, $richMediaTestObjectDirectory, $bundledPackageSealOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDirectory '..\..\THIRD_PARTY_NOTICES.md') `
    -Destination (Join-Path $outputDirectory 'THIRD_PARTY_NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory '..\..\third_party\public_suffix_list\public_suffix_list.dat') `
    -Destination (Join-Path $outputDirectory 'public_suffix_list.dat') -Force

$optimization = if ($Configuration -eq 'Release') { @('/O2', '/DNDEBUG') } else { @('/Od', '/Zi') }
$includeArguments = @(
    "/I$gameInputPackage\native\include",
    "/I$webView2Package\build\native\include",
    "/I$($vcTools.FullName)\include",
    "/I$sdkRoot\Include\$($sdk.Name)\ucrt",
    "/I$sdkRoot\Include\$($sdk.Name)\shared",
    "/I$sdkRoot\Include\$($sdk.Name)\um",
    "/I$sdkRoot\Include\$($sdk.Name)\winrt",
    "/I$sdkRoot\Include\$($sdk.Name)\cppwinrt"
)
$libraryArguments = @(
    "/LIBPATH:$gameInputPackage\native\lib\$Architecture",
    "/LIBPATH:$webView2Package\build\native\$Architecture",
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

function Invoke-ArtworkDecoderBuild {
    param([switch]$Testing)
    $name = if ($Testing) { 'ArtworkDecoderTestHost' } else { 'ArtworkDecoderHost' }
    $objectDirectory = if ($Testing) {
        $artworkDecoderTestObjectDirectory
    } else {
        $artworkDecoderObjectDirectory
    }
    $definitions = if ($Testing) { @('/DWRAIL_ARTWORK_DECODER_TESTING') } else { @() }
    $arguments = $common + $definitions + @(
        (Join-Path $projectDirectory 'ArtworkDecoderHost.cpp'),
        "/Fo:$objectDirectory\",
        "/Fe:$outputDirectory\$name.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('windowscodecs.lib', 'ole32.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$name build failed with exit code $LASTEXITCODE."
    }
}

Invoke-ArtworkDecoderBuild
if ($TrustedArtworkTestsOnly) { Invoke-ArtworkDecoderBuild -Testing }

function Invoke-OverlayPlatformInteropBuild {
    $arguments = $common + @(
        '/DWRAIL_OVERLAY_PLATFORM_EXPORTS',
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

function Invoke-RichMediaTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'RichMediaSurfaceCoordinatorTests.cpp'),
        (Join-Path $projectDirectory 'MediaSessionManager.cpp'),
        (Join-Path $projectDirectory 'RichMediaSurfaceCoordinator.cpp'),
        (Join-Path $projectDirectory 'PublicSuffixDomainAuthority.cpp'),
        (Join-Path $projectDirectory 'OverlayCompositionSurface.cpp'),
        "/Fo:$richMediaTestObjectDirectory\",
        "/Fe:$outputDirectory\RichMediaSurfaceCoordinatorTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'WebView2LoaderStatic.lib', 'user32.lib', 'd2d1.lib', 'd3d11.lib',
        'dxgi.lib', 'dcomp.lib', 'windowsapp.lib', 'ole32.lib',
        'uiautomationcore.lib', 'psapi.lib', 'shlwapi.lib', 'version.lib',
        'bcrypt.lib', 'normaliz.lib')
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "RichMediaSurfaceCoordinatorTests build failed with exit code $LASTEXITCODE."
    }
    $testArguments = if ($RichMediaContractTestsOnly) {
        @('--contract-only')
    } elseif ($RichMediaPerformanceTestsOnly) {
        @('--performance')
    } else {
        @()
    }
    & (Join-Path $outputDirectory 'RichMediaSurfaceCoordinatorTests.exe') $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "RichMediaSurfaceCoordinatorTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-OverlayPlatformInteropTests {
    $arguments = $common + @(
        '/DWRAIL_OVERLAY_PLATFORM_IMPORTS',
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
    Invoke-ControllerGuideTests
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

function Invoke-ControllerGuideTests {
    Invoke-OverlayPlatformParityTest `
        -Name 'GuideInputCompatibilityTests' `
        -ObjectDirectory $guideTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'GuideInputCompatibilityTests.cpp'),
            (Join-Path $projectDirectory 'GuideInputCompatibility.cpp'),
            (Join-Path $projectDirectory 'ControllerGuide.cpp')) `
        -Libraries @('user32.lib')
}

function Invoke-AccessibilityEventsTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'AccessibilityEventsTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        "/Fo:$accessibilityEventsTestObjectDirectory\",
        "/Fe:$outputDirectory\AccessibilityEventsTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityEventsTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'AccessibilityEventsTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "AccessibilityEventsTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SemanticChurnPerformanceTests {
    $arguments = $common + @(
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'SemanticChurnPerformanceTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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

function Invoke-BackgroundSurfaceHostTests {
    $arguments = $common + @(
        '/DWRAIL_WIDGET_BRIDGE_CLIENT_TESTING',
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'BackgroundSurfaceHostTests.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        (Join-Path $projectDirectory 'PublicSuffixDomainAuthority.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
        "/Fo:$backgroundSurfaceHostTestObjectDirectory\",
        "/Fe:$outputDirectory\BackgroundSurfaceHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'windowsapp.lib', 'user32.lib', 'bcrypt.lib', 'normaliz.lib',
        'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "BackgroundSurfaceHostTests build failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $outputDirectory 'BackgroundSurfaceHostTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "BackgroundSurfaceHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-PinnedSurfaceHostTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'PinnedSurfaceHostTests.cpp'),
        (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
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
        (Join-Path $projectDirectory 'SliderInteraction.cpp'),
        (Join-Path $projectDirectory 'WidgetInteractionSession.cpp'),
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
        (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
        '/DWRAIL_WIDGET_SURFACE_COORDINATOR_TESTING',
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
        '/DWRAIL_WIDGET_BRIDGE_CLIENT_TESTING',
        (Join-Path $projectDirectory 'WidgetBridgeCatalogTests.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        (Join-Path $projectDirectory 'PublicSuffixDomainAuthority.cpp'),
        "/Fo:$bridgeCatalogTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetBridgeCatalogTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @('windowsapp.lib', 'user32.lib', 'bcrypt.lib', 'normaliz.lib')
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
        (Join-Path $projectDirectory 'FocusNavigation.cpp'),
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

function Invoke-WidgetInteractionTests {
    Invoke-OverlayPlatformParityTest `
        -Name 'WidgetInteractionSessionTests' `
        -ObjectDirectory $widgetInteractionTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'WidgetInteractionSessionTests.cpp'),
            (Join-Path $projectDirectory 'WidgetInteractionSession.cpp'),
            (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
            (Join-Path $projectDirectory 'FocusNavigation.cpp'),
            (Join-Path $projectDirectory 'SliderInteraction.cpp'),
            (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
            (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
            (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
            (Join-Path $projectDirectory 'NativeStyle.cpp'),
            (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
            (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
            (Join-Path $projectDirectory 'NativeIcons.cpp'),
            (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
            (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp')) `
        -Libraries @(
            'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib')
    Invoke-OverlayPlatformParityTest `
        -Name 'ControllerNavigationTests' `
        -ObjectDirectory $navigationTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'ControllerNavigationTests.cpp'),
            (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
            (Join-Path $platformDirectory 'OverlayPlatformPolicy.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'FocusNavigationTests' `
        -ObjectDirectory $focusTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'FocusNavigationTests.cpp'),
            (Join-Path $projectDirectory 'FocusNavigation.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'WidgetSurfaceFocusTests' `
        -ObjectDirectory $surfaceFocusTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'WidgetSurfaceFocusTests.cpp'),
            (Join-Path $projectDirectory 'WidgetSurfaceFocus.cpp'),
            (Join-Path $projectDirectory 'FocusNavigation.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'SliderInteractionTests' `
        -ObjectDirectory $sliderTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'SliderInteractionTests.cpp'),
            (Join-Path $projectDirectory 'SliderInteraction.cpp'))
    Invoke-OverlayPlatformParityTest `
        -Name 'PressedInteractionTests' `
        -ObjectDirectory $pressedTestObjectDirectory `
        -Sources @((Join-Path $projectDirectory 'PressedInteractionTests.cpp'))
    Invoke-TextEntryModalTests
    Invoke-OverlayPlatformParityTest `
        -Name 'AccessibilityProviderTests' `
        -ObjectDirectory $accessibilityProviderTestObjectDirectory `
        -Sources @(
            (Join-Path $projectDirectory 'AccessibilityProviderTests.cpp'),
            (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
            (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
            (Join-Path $projectDirectory 'AccessibilityTree.cpp')) `
        -Libraries @('user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib')
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
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
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
        (Join-Path $projectDirectory 'OverlayCompositionSurface.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$chromeTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayChromeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'd3d11.lib', 'dxgi.lib', 'dcomp.lib',
        'windowscodecs.lib', 'ole32.lib', 'user32.lib')
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
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
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
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
    if ($WidgetSwitchFallbackAuthorityTestsOnly -or
        -not [string]::IsNullOrWhiteSpace($WidgetSwitchFallbackAuthorityReplayLog)) {
        $selectorArguments = @()
        if ($WidgetSwitchFallbackAuthorityTestsOnly) {
            $selectorArguments += '--fallback-authority-selection-only'
        } else {
            if ($WidgetSwitchFallbackAuthorityReplayMarker -le 0 -or
                $WidgetSwitchFallbackAuthorityReplaySequence -le 0) {
                throw 'WidgetSwitch fallback-authority replay requires positive marker and sequence.'
            }
            $selectorArguments += @(
                '--fallback-authority-replay-log', $WidgetSwitchFallbackAuthorityReplayLog,
                '--fallback-authority-marker', $WidgetSwitchFallbackAuthorityReplayMarker,
                '--fallback-authority-sequence', $WidgetSwitchFallbackAuthorityReplaySequence)
        }
        & (Join-Path $outputDirectory 'WidgetSwitchHostTests.exe') `
            $selectorArguments
        if ($LASTEXITCODE -ne 0) {
            throw "WidgetSwitch fallback-authority selector/replay failed with exit code $LASTEXITCODE."
        }
        return
    }
    $managedPublishExitCode = 0
    Invoke-SerializedManagedPublish `
        -Project (Join-Path $projectDirectory '..\..\tests\WidgetSwitchFixture\WidgetSwitchFixture.csproj') `
        -Configuration $Configuration -Output $widgetSwitchFixtureOutput `
        -ExitCode ([ref]$managedPublishExitCode)
    if ($managedPublishExitCode -ne 0) {
        throw "Widget switch fixture publish failed with exit code $managedPublishExitCode."
    }
    $fixture = Join-Path $widgetSwitchFixtureOutput 'WidgetSwitchFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'Widget switch fixture publish omitted WidgetSwitchFixture.exe.'
    }
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectDirectory '..\..'))
    $repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
        throw 'Widget switch provenance could not resolve the repository commit.'
    }
    $hostSha256 = (Get-FileHash -LiteralPath `
        (Join-Path $outputDirectory 'OverlayHost.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    $widgetSwitchArguments = @(
        '--installation', $outputDirectory,
        '--fixture-worker', $fixture,
        '--repository-commit', $repositoryCommit,
        '--host-sha256', $hostSha256)
    if ($WidgetSwitchGeometryOnly) {
        $widgetSwitchArguments += '--geometry-only'
    }
    if ($PinnedSliderRouteTestsOnly) {
        $widgetSwitchArguments += '--pinned-slider-route-only'
    }
    & (Join-Path $outputDirectory 'WidgetSwitchHostTests.exe') $widgetSwitchArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetSwitchHostTests failed with exit code $LASTEXITCODE."
    }
}

function Invoke-PathIsolatedHostTestProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureName
    )

    $resolvedInstallation = [IO.Path]::GetFullPath($outputDirectory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $sanitizedEntries = @($env:PATH.Split([IO.Path]::PathSeparator) | Where-Object {
        if ([string]::IsNullOrWhiteSpace($_)) { return $false }
        try {
            $candidate = [IO.Path]::GetFullPath(
                [Environment]::ExpandEnvironmentVariables($_.Trim('"'))).TrimEnd(
                    [IO.Path]::DirectorySeparatorChar,
                    [IO.Path]::AltDirectorySeparatorChar)
            return -not $candidate.Equals(
                $resolvedInstallation, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            return $true
        }
    })
    $savedPath = $env:PATH
    try {
        $env:PATH = [string]::Join([IO.Path]::PathSeparator, $sanitizedEntries)
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FailureName failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:PATH = $savedPath
    }
}

function Invoke-WidgetActionFailureHostTestProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FixtureWorker
    )

    Invoke-PathIsolatedHostTestProcess `
        -Executable (Join-Path $outputDirectory 'WidgetActionFailureHostTests.exe') `
        -Arguments @(
            '--installation', $outputDirectory,
            '--fixture-worker', $FixtureWorker
        ) `
        -FailureName 'WidgetActionFailureHostTests'
}

function Invoke-WidgetActionFailureHostTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'WidgetActionFailureHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$actionFailureHostTestObjectDirectory\",
        "/Fe:$outputDirectory\WidgetActionFailureHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'ole32.lib', 'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "WidgetActionFailureHostTests build failed with exit code $LASTEXITCODE."
    }
    $managedPublishExitCode = 0
    Invoke-SerializedManagedPublish `
        -Project (Join-Path $projectDirectory '..\..\tests\AdvancedActionFailureFixture\AdvancedActionFailureFixture.csproj') `
        -Configuration $Configuration -Output $actionFailureFixtureOutput `
        -ExitCode ([ref]$managedPublishExitCode)
    if ($managedPublishExitCode -ne 0) {
        throw "Advanced action-failure fixture publish failed with exit code $managedPublishExitCode."
    }
    $fixture = Join-Path $actionFailureFixtureOutput 'AdvancedActionFailureFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'Advanced action-failure fixture publish omitted AdvancedActionFailureFixture.exe.'
    }
    Invoke-WidgetActionFailureHostTestProcess -FixtureWorker $fixture
}

function Invoke-AudioMixerScrollHostTests {
    $arguments = $common + @(
        (Join-Path $projectDirectory 'AudioMixerScrollHostTests.cpp'),
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
        "/Fo:$audioMixerScrollHostTestObjectDirectory\",
        "/Fe:$outputDirectory\AudioMixerScrollHostTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'user32.lib', 'gdi32.lib', 'windowscodecs.lib', 'ole32.lib',
        'oleaut32.lib', 'uiautomationcore.lib'
    )
    & $cl $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AudioMixerScrollHostTests build failed with exit code $LASTEXITCODE."
    }
    if ($SkipPackaging) {
        return
    }

    $managedPublishExitCode = 0
    Invoke-SerializedManagedPublish `
        -Project (Join-Path $projectDirectory '..\..\tests\AudioMixerScrollFixture\AudioMixerScrollFixture.csproj') `
        -Configuration $Configuration -Output $audioMixerScrollFixtureOutput `
        -ExitCode ([ref]$managedPublishExitCode)
    if ($managedPublishExitCode -ne 0) {
        throw "Audio Mixer scroll fixture publish failed with exit code $managedPublishExitCode."
    }
    $fixture = Join-Path $audioMixerScrollFixtureOutput 'AudioMixerScrollFixture.exe'
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw 'Audio Mixer scroll fixture publish omitted AudioMixerScrollFixture.exe.'
    }
    Invoke-PathIsolatedHostTestProcess `
        -Executable (Join-Path $outputDirectory 'AudioMixerScrollHostTests.exe') `
        -Arguments @(
            '--installation', $outputDirectory,
            '--fixture-worker', $fixture
        ) `
        -FailureName 'AudioMixerScrollHostTests'
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
    $managedPublishExitCode = 0
    Invoke-SerializedManagedPublish `
        -Project (Join-Path $projectDirectory '..\..\tests\TrayRefreshCommunityFixture\TrayRefreshCommunityFixture.csproj') `
        -Configuration $Configuration -Output $trayRefreshCommunityFixtureOutput `
        -ExitCode ([ref]$managedPublishExitCode)
    if ($managedPublishExitCode -ne 0) {
        throw "Tray refresh Community fixture publish failed with exit code $managedPublishExitCode."
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
    param([switch]$DirectOnly)
    $arguments = $common + @(
        '/DWRAIL_OVERLAY_PROCESS_OWNER_TESTING',
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
    $processOwnerTestExecutable = Join-Path $outputDirectory 'OverlayProcessOwnerTests.exe'
    $processOwnerTest = Start-Process -FilePath $processOwnerTestExecutable `
        -PassThru -NoNewWindow
    if (-not $processOwnerTest.WaitForExit(20000)) {
        Stop-Process -Id $processOwnerTest.Id -Force -ErrorAction SilentlyContinue
        [void]$processOwnerTest.WaitForExit(5000)
        throw 'OverlayProcessOwnerTests exceeded its 20-second bounded timeout.'
    }
    $processOwnerTest.WaitForExit()
    $processOwnerTest.Refresh()
    [int]$processOwnerTestExitCode = $processOwnerTest.ExitCode
    if ($processOwnerTestExitCode -ne 0) {
        throw "OverlayProcessOwnerTests failed with exit code $processOwnerTestExitCode."
    }

    $hostExecutable = Join-Path $outputDirectory 'OverlayHost.exe'
    if (-not $DirectOnly -and (Test-Path -LiteralPath $hostExecutable)) {
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

if ($BackgroundSurfaceHostTestsOnly) {
    if ($SkipTests) {
        throw 'BackgroundSurfaceHostTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-BackgroundSurfaceHostTests
    return
}

if ($AccessibilityTreeTestsOnly) {
    if ($SkipTests) {
        throw 'AccessibilityTreeTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-AccessibilityTreeTests
    return
}

if ($ControllerGuideTestsOnly) {
    if ($SkipTests) {
        throw 'ControllerGuideTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-ControllerGuideTests
    return
}

if ($AccessibilityEventsTestsOnly) {
    if ($SkipTests) {
        throw 'AccessibilityEventsTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-AccessibilityEventsTests
    return
}

if ($WidgetInteractionTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetInteractionTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetInteractionTests
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
    Invoke-OverlayProcessOwnerTests -DirectOnly
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
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        (Join-Path $projectDirectory 'WidgetAdmissionTrace.cpp'),
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

if ($RichMediaTestsOnly -or $RichMediaContractTestsOnly -or $RichMediaPerformanceTestsOnly) {
    if ($SkipTests) {
        throw 'RichMedia test routes cannot be combined with SkipTests.'
    }
    Invoke-RichMediaTests
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

if ($WidgetSwitchFallbackAuthorityTestsOnly -or
    -not [string]::IsNullOrWhiteSpace($WidgetSwitchFallbackAuthorityReplayLog)) {
    if ($SkipTests) {
        throw 'WidgetSwitch fallback-authority selector/replay cannot be combined with SkipTests.'
    }
    Invoke-WidgetSwitchHostTests
    return
}

Invoke-OverlayPlatformInteropBuild

$hostCompileArguments = $common + @('/Zi')
if ($PinnedSliderRouteTestsOnly) {
    $hostCompileArguments += '/DWRAIL_PINNED_SLIDER_ROUTE_TESTING'
}
$hostArguments = $hostCompileArguments + @(
    '/DWRAIL_OVERLAY_PLATFORM_IMPORTS',
    (Join-Path $projectDirectory 'main.cpp'),
    (Join-Path $projectDirectory 'MediaSessionManager.cpp'),
    (Join-Path $projectDirectory 'OverlayCompositionSurface.cpp'),
    (Join-Path $projectDirectory 'RichMediaSurfaceCoordinator.cpp'),
    (Join-Path $projectDirectory 'PublicSuffixDomainAuthority.cpp'),
    (Join-Path $projectDirectory 'OverlayProcessOwner.cpp'),
    (Join-Path $projectDirectory 'OverlayState.cpp'),
    (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
    (Join-Path $projectDirectory 'LocalWidgetPackageImport.cpp'),
    (Join-Path $projectDirectory 'WidgetSessionCoordinator.cpp'),
    (Join-Path $projectDirectory 'WidgetAdmissionTrace.cpp'),
    (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
    (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
    (Join-Path $projectDirectory 'ControllerNavigation.cpp'),
    (Join-Path $projectDirectory 'ControllerGuide.cpp'),
    (Join-Path $projectDirectory 'SliderInteraction.cpp'),
    (Join-Path $projectDirectory 'WidgetInteractionSession.cpp'),
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
    'WebView2LoaderStatic.lib',
    'gameinput.lib', 'shcore.lib', 'xinput9_1_0.lib', 'windowsapp.lib',
    (Join-Path $outputDirectory 'OverlayPlatformInterop.lib'),
    'winhttp.lib', 'windowscodecs.lib', 'ole32.lib', 'oleaut32.lib',
    'uiautomationcore.lib', 'advapi32.lib', 'uuid.lib', 'bcrypt.lib', 'normaliz.lib'
)

& $cl $hostArguments
if ($LASTEXITCODE -ne 0) {
    throw "OverlayHost build failed with exit code $LASTEXITCODE."
}

$bridgeOutput = Join-Path $outputDirectory 'runtime\Bridge'
$workerHostOutput = Join-Path $outputDirectory 'runtime\WidgetWorkerHost'
$settingsOutput = Join-Path $outputDirectory 'runtime\Settings'
$audioMixerOutput = Join-Path $outputDirectory 'runtime\AudioMixer'
$networkControlsOutput = Join-Path $outputDirectory 'runtime\NetworkControls'
$gamesAppsOutput = Join-Path $outputDirectory 'runtime\GamesApps'
$mediaSessionsOutput = Join-Path $outputDirectory 'runtime\MediaSessions'
$embeddedMediaSampleOutput = Join-Path $outputDirectory 'runtime\EmbeddedMediaSample'

# Host runtime generation is part of every coherent Release build. The
# SkipPackaging switch omits Community/test package publication only; it must
# never leave a new native host beside stale managed workers or contracts.
foreach ($hostRuntimeOutput in @(
    $bridgeOutput,
    $workerHostOutput,
    $settingsOutput,
    $audioMixerOutput,
    $networkControlsOutput,
    $gamesAppsOutput,
    $mediaSessionsOutput,
    $embeddedMediaSampleOutput,
    (Join-Path $outputDirectory 'runtime\SpotifyPlaybackHost'),
    (Join-Path $outputDirectory 'runtime\YtMusic')
)) {
    Remove-GeneratedDirectory -Path $hostRuntimeOutput
}
$managedPublishExitCode = 0
Invoke-SerializedManagedPublish `
    -Project (Join-Path $projectDirectory '..\WidgetBridge\WidgetBridge.csproj') `
    -Configuration $Configuration -Output $bridgeOutput `
    -ExitCode ([ref]$managedPublishExitCode)
if ($managedPublishExitCode -ne 0) {
    throw "WidgetBridge publish failed with exit code $managedPublishExitCode."
}
$managedPublishExitCode = 0
Invoke-SerializedManagedPublish `
    -Project (Join-Path $projectDirectory '..\WidgetWorkerHost\WidgetWorkerHost.csproj') `
    -Configuration $Configuration -Output $workerHostOutput `
    -ExitCode ([ref]$managedPublishExitCode)
if ($managedPublishExitCode -ne 0 -or
    -not (Test-Path -LiteralPath (Join-Path $workerHostOutput 'WidgetWorkerHost.exe'))) {
    throw "Generic widget worker host publish failed with exit code $managedPublishExitCode."
}
$managedPublishExitCode = 0
Remove-GeneratedDirectory -Path $bundledPackageSealOutput
Invoke-SerializedManagedPublish `
    -Project (Join-Path $projectDirectory '..\..\tools\BundledWidgetPackageSeal\BundledWidgetPackageSeal.csproj') `
    -Configuration $Configuration -Output $bundledPackageSealOutput `
    -ExitCode ([ref]$managedPublishExitCode)
$bundledPackageSeal = Join-Path $bundledPackageSealOutput 'BundledWidgetPackageSeal.exe'
if ($managedPublishExitCode -ne 0 -or -not (Test-Path -LiteralPath $bundledPackageSeal)) {
    throw "Bundled widget package seal tool publish failed with exit code $managedPublishExitCode."
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
    $managedPublishExitCode = 0
    Invoke-SerializedManagedPublish `
        -Project (Join-Path $WidgetProject "$AssemblyName.csproj") `
        -Configuration $Configuration -Output $payloadOutput `
        -ExitCode ([ref]$managedPublishExitCode)
    if ($managedPublishExitCode -ne 0) {
        throw "$DisplayName package publish failed with exit code $managedPublishExitCode."
    }
    # WidgetSdk/WidgetProtocol are host-ABI assemblies selected by the generic
    # loader. Project assets copied by `dotnet publish` are pruned so bundled
    # packages do not duplicate or shadow host contracts.
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
    Copy-Item -LiteralPath (Join-Path $WidgetProject 'styles\default.wrss') `
        -Destination (Join-Path $stylesOutput 'default.wrss') -Force
    foreach ($requiredFile in @(
        'manifest.json',
        'styles\default.wrss',
        "payload\$AssemblyName.dll"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackageRoot $requiredFile))) {
            throw "$DisplayName bundled package is missing $requiredFile."
        }
    }
    & $bundledPackageSeal $resolvedOutputRoot $resolvedPackageRoot
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath (Join-Path $resolvedPackageRoot '.wrail-integrity.json'))) {
        throw "$DisplayName bundled package sealing failed with exit code $LASTEXITCODE."
    }
}

$managedPublishExitCode = 0
Invoke-SerializedManagedPublish `
    -Project (Join-Path $projectDirectory '..\FirstPartyWidgets\SettingsWidget.Worker\SettingsWidget.Worker.csproj') `
    -Configuration $Configuration -Output $settingsOutput `
    -ExitCode ([ref]$managedPublishExitCode)
if ($managedPublishExitCode -ne 0) {
    throw "Settings worker publish failed with exit code $managedPublishExitCode."
}
$settingsProject = Join-Path $projectDirectory '..\FirstPartyWidgets\SettingsWidget'
$settingsStylesOutput = Join-Path $settingsOutput 'styles'
$settingsPayloadOutput = Join-Path $settingsOutput 'payload'
New-Item -ItemType Directory -Force -Path $settingsStylesOutput, $settingsPayloadOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $settingsProject 'manifest.json') `
    -Destination (Join-Path $settingsOutput 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $settingsProject 'styles\default.wrss') `
    -Destination (Join-Path $settingsStylesOutput 'default.wrss') -Force
Copy-Item -LiteralPath (Join-Path $settingsOutput 'SettingsWidget.dll') `
    -Destination (Join-Path $settingsPayloadOutput 'SettingsWidget.dll') -Force
foreach ($requiredSettingsFile in @(
    'SettingsWidget.Worker.exe',
    'manifest.json',
    'styles\default.wrss',
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
Publish-BundledWidgetPackage `
    (Join-Path $projectDirectory '..\..\samples\EmbeddedMediaWidget') `
    $embeddedMediaSampleOutput 'EmbeddedMediaWidget' 'Embedded Media Sample'
Copy-Item -LiteralPath (Join-Path $projectDirectory 'widget-catalog.json') `
    -Destination (Join-Path $outputDirectory 'widget-catalog.json') -Force

if ($WidgetSwitchTestsOnly -or $PinnedSliderRouteTestsOnly) {
    if ($SkipTests) {
        throw 'WidgetSwitchTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-WidgetSwitchHostTests
    return
}

if ($WidgetSwitchGeometryOnly) {
    throw 'WidgetSwitchGeometryOnly requires WidgetSwitchTestsOnly.'
}

if ($WidgetActionFailureHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'WidgetActionFailureHostTestsOnly requires tests and packaging.'
    }
    Invoke-WidgetActionFailureHostTests
    return
}

if ($AudioMixerScrollHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'AudioMixerScrollHostTestsOnly requires tests and packaging.'
    }
    Invoke-AudioMixerScrollHostTests
    return
}

if ($ColdDashboardTestsOnly) {
    if ($SkipTests) {
        throw 'ColdDashboardTestsOnly cannot be combined with SkipTests.'
    }
    Invoke-ColdDashboardTests
    return
}

if ($TextEntryHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TextEntryHostTestsOnly requires tests and packaging.'
    }
    Invoke-TextEntryModalTests
    Invoke-AccessibilityTreeTests
    return
}

if ($TrayAccessibilityHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TrayAccessibilityHostTestsOnly requires tests and packaging.'
    }
    Invoke-TrayAccessibilityTests
    return
}

if ($TrayRefreshHostTestsOnly) {
    if ($SkipTests -or $SkipPackaging) {
        throw 'TrayRefreshHostTestsOnly requires tests and packaging.'
    }
    Invoke-TrayRefreshHostTests
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
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        (Join-Path $projectDirectory 'OverlayCompositionSurface.cpp'),
        (Join-Path $projectDirectory 'OverlayState.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        "/Fo:$chromeTestObjectDirectory\",
        "/Fe:$outputDirectory\OverlayChromeTests.exe",
        '/link', '/SUBSYSTEM:CONSOLE'
    ) + $libraryArguments + @(
        'd2d1.lib', 'd3d11.lib', 'dxgi.lib', 'dcomp.lib',
        'windowscodecs.lib', 'ole32.lib', 'user32.lib')
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
        (Join-Path $projectDirectory 'ControllerGuide.cpp'),
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
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
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
        '/DWRAIL_WIDGET_BRIDGE_CLIENT_TESTING',
        (Join-Path $projectDirectory 'RealHostAccessibilityTests.cpp'),
        (Join-Path $projectDirectory 'AccessibilityTree.cpp'),
        (Join-Path $projectDirectory 'HostAccessibility.cpp'),
        (Join-Path $projectDirectory 'AccessibilityProvider.cpp'),
        (Join-Path $projectDirectory 'AccessibilityEvents.cpp'),
        (Join-Path $projectDirectory 'TrayLayout.cpp'),
        (Join-Path $projectDirectory 'WidgetBridgeClient.cpp'),
        (Join-Path $projectDirectory 'PublicSuffixDomainAuthority.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        (Join-Path $projectDirectory 'OverlayHostTestSupport.cpp'),
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
        $managedPublishExitCode = 0
        Invoke-SerializedManagedPublish `
            -Project (Join-Path $projectDirectory '..\..\tests\AdvancedActionFailureFixture\AdvancedActionFailureFixture.csproj') `
            -Configuration $Configuration -Output $actionFailureFixtureOutput `
            -ExitCode ([ref]$managedPublishExitCode)
        if ($managedPublishExitCode -ne 0) {
            throw "Advanced action-failure fixture publish failed with exit code $managedPublishExitCode."
        }
        $actionFailureFixture = Join-Path $actionFailureFixtureOutput 'AdvancedActionFailureFixture.exe'
        if (-not (Test-Path -LiteralPath $actionFailureFixture)) {
            throw "Advanced action-failure fixture publish omitted AdvancedActionFailureFixture.exe."
        }
        Invoke-WidgetActionFailureHostTestProcess -FixtureWorker $actionFailureFixture
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
        $managedPublishExitCode = 0
        Invoke-SerializedManagedPublish `
            -Project (Join-Path $projectDirectory '..\..\tests\WidgetSwitchFixture\WidgetSwitchFixture.csproj') `
            -Configuration $Configuration -Output $widgetSwitchFixtureOutput `
            -ExitCode ([ref]$managedPublishExitCode)
        if ($managedPublishExitCode -ne 0) {
            throw "Widget switch fixture publish failed with exit code $managedPublishExitCode."
        }
        $widgetSwitchFixture = Join-Path $widgetSwitchFixtureOutput 'WidgetSwitchFixture.exe'
        if (-not (Test-Path -LiteralPath $widgetSwitchFixture)) {
            throw "Widget switch fixture publish omitted WidgetSwitchFixture.exe."
        }
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectDirectory '..\..'))
        $repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
            throw 'Widget switch provenance could not resolve the repository commit.'
        }
        $hostSha256 = (Get-FileHash -LiteralPath `
            (Join-Path $outputDirectory 'OverlayHost.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
        & (Join-Path $outputDirectory 'WidgetSwitchHostTests.exe') `
            --installation $outputDirectory `
            --fixture-worker $widgetSwitchFixture `
            --repository-commit $repositoryCommit `
            --host-sha256 $hostSha256
        if ($LASTEXITCODE -ne 0) {
            throw "WidgetSwitchHostTests failed with exit code $LASTEXITCODE."
        }
    }

    Invoke-AudioMixerScrollHostTests

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
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'DeclarativeRendererTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
        '/DWRAIL_DECLARATIVE_RENDERER_TESTING',
        (Join-Path $projectDirectory 'SharedComponentGeometryTests.cpp'),
        (Join-Path $projectDirectory 'DeclarativeRenderer.cpp'),
        (Join-Path $projectDirectory 'DeclarativeLayout.cpp'),
        (Join-Path $projectDirectory 'NativeStyle.cpp'),
        (Join-Path $projectDirectory 'NativeTextLayout.cpp'),
        (Join-Path $projectDirectory 'DeclarativeMotion.cpp'),
        (Join-Path $projectDirectory 'NativeIcons.cpp'),
        (Join-Path $projectDirectory 'RemoteImageCache.cpp'),
        (Join-Path $projectDirectory 'ArtworkDecoderProcessOwner.cpp'),
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
