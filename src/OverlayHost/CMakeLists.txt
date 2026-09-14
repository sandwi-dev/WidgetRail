cmake_minimum_required(VERSION 3.24)

project(WidgetRailOverlayHost LANGUAGES CXX RC)

set(GAMEINPUT_VERSION "3.5.262")
set(GAMEINPUT_ROOT "$ENV{USERPROFILE}/.nuget/packages/microsoft.gameinput/${GAMEINPUT_VERSION}")
if(NOT EXISTS "${GAMEINPUT_ROOT}/native/include/GameInput.h")
    message(FATAL_ERROR "Restore NativeDependencies.csproj before configuring CMake")
endif()

option(WRAIL_BUILD_TESTS "Build the dependency-free state-machine tests" ON)

set(WRAIL_TAFFY_MANIFEST "${CMAKE_CURRENT_SOURCE_DIR}/taffy_bridge/Cargo.toml")
set(WRAIL_TAFFY_LOCK "${CMAKE_CURRENT_SOURCE_DIR}/taffy_bridge/Cargo.lock")
set(WRAIL_TAFFY_TARGET_DIR "${CMAKE_CURRENT_BINARY_DIR}/cargo")
find_program(WRAIL_CARGO_EXECUTABLE cargo
    HINTS "$ENV{USERPROFILE}/.cargo/bin"
    REQUIRED)

add_custom_target(WidgetRailTaffyRust
    COMMAND "${CMAKE_COMMAND}" -E env
        "CARGO_TARGET_DIR=${WRAIL_TAFFY_TARGET_DIR}"
        "${WRAIL_CARGO_EXECUTABLE}" build --locked
        --manifest-path "${WRAIL_TAFFY_MANIFEST}"
        --target x86_64-pc-windows-msvc
        $<$<NOT:$<CONFIG:Debug>>:--release>
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/taffy_bridge"
    COMMENT "Building pinned Taffy static layout library"
    VERBATIM
    COMMAND_EXPAND_LISTS)

add_library(WidgetRailTaffyLayout STATIC IMPORTED GLOBAL)
set_target_properties(WidgetRailTaffyLayout PROPERTIES
    IMPORTED_LOCATION_DEBUG
        "${WRAIL_TAFFY_TARGET_DIR}/x86_64-pc-windows-msvc/debug/wrail_taffy_layout.lib"
    IMPORTED_LOCATION_RELEASE
        "${WRAIL_TAFFY_TARGET_DIR}/x86_64-pc-windows-msvc/release/wrail_taffy_layout.lib"
    IMPORTED_LOCATION_RELWITHDEBINFO
        "${WRAIL_TAFFY_TARGET_DIR}/x86_64-pc-windows-msvc/release/wrail_taffy_layout.lib"
    IMPORTED_LOCATION_MINSIZEREL
        "${WRAIL_TAFFY_TARGET_DIR}/x86_64-pc-windows-msvc/release/wrail_taffy_layout.lib")

function(widgetrail_link_taffy target_name)
    add_dependencies(${target_name} WidgetRailTaffyRust)
    target_link_libraries(${target_name} PRIVATE WidgetRailTaffyLayout ntdll userenv ws2_32)
endfunction()

# Widget labels and controller guides intentionally contain non-ASCII source
# text. Apply UTF-8 to every native target so CMake and the canonical build.ps1
# path compile identical literals (including test executables).
add_compile_options(/utf-8)

add_executable(ArtworkDecoderHost ArtworkDecoderHost.cpp ApplicationIcon.rc)
target_include_directories(ArtworkDecoderHost PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../assets/branding")
target_compile_features(ArtworkDecoderHost PRIVATE cxx_std_20)
target_compile_options(ArtworkDecoderHost PRIVATE /W4 /permissive- /EHsc)
target_compile_definitions(ArtworkDecoderHost PRIVATE
    UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(ArtworkDecoderHost PRIVATE windowscodecs ole32 d2d1 d3d11 dxgi)

add_executable(OverlayHost WIN32
    ApplicationIcon.rc
    main.cpp
    DeveloperInspector.cpp
    DeveloperInspectorWindow.cpp
    MediaSessionManager.cpp
    OverlayCompositionSurface.cpp
    CompositorBackgroundSurfaceCoordinator.cpp
    OverlayProcessOwner.cpp
    OverlayState.cpp
    WidgetBridgeClient.cpp
    LocalWidgetPackageImport.cpp
    WidgetSessionCoordinator.cpp
    WidgetAdmissionTrace.cpp
    RemoteImageCache.cpp
    ArtworkDecoderProcessOwner.cpp
    ScrollEvidenceProbe.cpp
    DeclarativeLayout.cpp
    NativeIcons.cpp
    NativeTextLayout.cpp
    NativeStyle.cpp
    OverlayChrome.cpp
    DeclarativeMotion.cpp
    OverlayPlacement.cpp
    PinnedSurfacePolicy.cpp
    PinnedSurfacePlacement.cpp
    WidgetSurfaceCoordinator.cpp
    OverlayTargeting.cpp
    OverlayTransition.cpp
    DeclarativeRenderer.cpp
    GuideInputCompatibility.cpp
    ../OverlayPlatformInterop/OverlayPlatformInterop.cpp
    ../OverlayPlatformInterop/OverlayPlatformPolicy.cpp
    ../OverlayPlatformInterop/OverlayPlatformPlacement.cpp
    ../OverlayPlatformInterop/OverlayPlatformTargeting.cpp
    ../OverlayPlatformInterop/ControllerIsolationCore.cpp
    ../OverlayPlatformInterop/ControllerIsolationReader.cpp
    ../OverlayPlatformInterop/ControllerIsolationRoutingSession.cpp
    ../OverlayPlatformInterop/GameInputSelectedControllerReader.cpp
    ../OverlayPlatformInterop/ControllerIsolationHostSession.cpp
    ../OverlayPlatformInterop/LocalControllerPolicy.cpp
    ../OverlayPlatformInterop/HidHideConfigurationAdapter.cpp
    ../OverlayPlatformInterop/ViGEmOutputAdapter.cpp
    ../../third_party/ViGEmClient/src/ViGEmClient.cpp
    ControllerNavigation.cpp
    ControllerGuide.cpp
    SliderInteraction.cpp
    WidgetInteractionSession.cpp
    TextEntryActionAdmission.cpp
    TextEntryModal.cpp
    FocusNavigation.cpp
    WidgetSurfaceFocus.cpp
    WidgetLifecycle.cpp
    WidgetActionFeedback.cpp
    AccessibilityTree.cpp
    AccessibilityProvider.cpp
    TrayLayout.cpp
    HostAccessibility.cpp
    AccessibilityEvents.cpp
)

target_compile_features(OverlayHost PRIVATE cxx_std_20)
target_compile_definitions(OverlayHost PRIVATE USING_GAMEINPUT
    WRAIL_VIGEM_NATIVE_BACKEND WRAIL_GAMEINPUT_ISOLATION_READER)
target_include_directories(OverlayHost PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../../assets/branding"
    "${GAMEINPUT_ROOT}/native/include"
    ../../third_party/ViGEmClient/include)
target_link_directories(OverlayHost PRIVATE "${GAMEINPUT_ROOT}/native/lib/x64")
target_compile_definitions(OverlayHost PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)
target_compile_options(OverlayHost PRIVATE /W4 /permissive- /EHsc)
target_link_libraries(OverlayHost PRIVATE
    comctl32
    user32
    gdi32
    d2d1
    dwrite
    dwmapi
    d3d11
    dxgi
    dcomp
    gameinput
    shcore
    xinput9_1_0
    windowsapp
    bcrypt
    advapi32
    shell32
    ole32
    winhttp
    windowscodecs
    ole32
    oleaut32
    uiautomationcore
    setupapi
    cfgmgr32
    advapi32
)
widgetrail_link_taffy(OverlayHost)

if(WRAIL_BUILD_TESTS)
    enable_testing()
    add_executable(ArtworkDecoderTestHost ArtworkDecoderHost.cpp)
    target_compile_features(ArtworkDecoderTestHost PRIVATE cxx_std_20)
    target_compile_options(ArtworkDecoderTestHost PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ArtworkDecoderTestHost PRIVATE
        WRAIL_ARTWORK_DECODER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(ArtworkDecoderTestHost PRIVATE windowscodecs ole32 d2d1 d3d11 dxgi)
    add_executable(WidgetSessionCoordinatorTests
        WidgetSessionCoordinatorTests.cpp WidgetSessionCoordinator.cpp
        WidgetAdmissionTrace.cpp)
    target_compile_features(WidgetSessionCoordinatorTests PRIVATE cxx_std_20)
    target_compile_options(WidgetSessionCoordinatorTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetSessionCoordinatorTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetSessionCoordinatorTests PRIVATE user32)
    add_test(NAME WidgetSessionCoordinatorTests COMMAND WidgetSessionCoordinatorTests)

    add_executable(OverlayProcessOwnerTests
        OverlayProcessOwnerTests.cpp OverlayProcessOwner.cpp)
    target_compile_features(OverlayProcessOwnerTests PRIVATE cxx_std_20)
    target_compile_options(OverlayProcessOwnerTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(OverlayProcessOwnerTests PRIVATE
        WRAIL_OVERLAY_PROCESS_OWNER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(OverlayProcessOwnerTests PRIVATE user32 advapi32)
    add_test(NAME OverlayProcessOwnerTests COMMAND OverlayProcessOwnerTests)

    add_executable(ScrollEvidenceProbeTests
        ScrollEvidenceProbeTests.cpp ScrollEvidenceProbe.cpp)
    target_compile_features(ScrollEvidenceProbeTests PRIVATE cxx_std_20)
    target_compile_options(ScrollEvidenceProbeTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ScrollEvidenceProbeTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME ScrollEvidenceProbeTests COMMAND ScrollEvidenceProbeTests)

    add_executable(OverlayStateTests
        OverlayStateTests.cpp
        OverlayState.cpp
    )
    target_compile_features(OverlayStateTests PRIVATE cxx_std_20)
    target_compile_options(OverlayStateTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME OverlayStateTests COMMAND OverlayStateTests)

    add_executable(DeclarativeLayoutTests DeclarativeLayoutTests.cpp DeclarativeLayout.cpp)
    target_compile_features(DeclarativeLayoutTests PRIVATE cxx_std_20)
    target_compile_options(DeclarativeLayoutTests PRIVATE /W4 /permissive- /EHsc)
    widgetrail_link_taffy(DeclarativeLayoutTests)
    add_test(NAME DeclarativeLayoutTests COMMAND DeclarativeLayoutTests)

    add_executable(NativeIconsTests NativeIconsTests.cpp NativeIcons.cpp)
    target_compile_features(NativeIconsTests PRIVATE cxx_std_20)
    target_compile_options(NativeIconsTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(NativeIconsTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(NativeIconsTests PRIVATE d2d1 windowscodecs ole32)
    add_test(NAME NativeIconsTests COMMAND NativeIconsTests)

    add_executable(NativeStyleTests NativeStyleTests.cpp NativeStyle.cpp)
    target_compile_features(NativeStyleTests PRIVATE cxx_std_20)
    target_compile_options(NativeStyleTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(NativeStyleTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME NativeStyleTests COMMAND NativeStyleTests)

    add_executable(NativeTextLayoutTests
        NativeTextLayoutTests.cpp NativeTextLayout.cpp NativeStyle.cpp)
    target_compile_features(NativeTextLayoutTests PRIVATE cxx_std_20)
    target_compile_options(NativeTextLayoutTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(NativeTextLayoutTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(NativeTextLayoutTests PRIVATE dwrite ole32)
    add_test(NAME NativeTextLayoutTests COMMAND NativeTextLayoutTests)

    add_executable(DeclarativeMotionTests
        DeclarativeMotionTests.cpp DeclarativeMotion.cpp)
    target_compile_features(DeclarativeMotionTests PRIVATE cxx_std_20)
    target_compile_options(DeclarativeMotionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(DeclarativeMotionTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME DeclarativeMotionTests COMMAND DeclarativeMotionTests)

    add_executable(OverlayPlacementTests OverlayPlacementTests.cpp OverlayPlacement.cpp)
    target_compile_features(OverlayPlacementTests PRIVATE cxx_std_20)
    target_compile_options(OverlayPlacementTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME OverlayPlacementTests COMMAND OverlayPlacementTests)

    add_executable(PinnedSurfaceHostTests
        PinnedSurfaceHostTests.cpp ControllerNavigation.cpp OverlayState.cpp
        PinnedSurfacePolicy.cpp
        AccessibilityProvider.cpp AccessibilityEvents.cpp)
    target_compile_features(PinnedSurfaceHostTests PRIVATE cxx_std_20)
    target_compile_options(PinnedSurfaceHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(PinnedSurfaceHostTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(PinnedSurfaceHostTests PRIVATE
        user32 gdi32 psapi ole32 oleaut32 uiautomationcore)
    add_test(NAME PinnedSurfaceHostTests COMMAND PinnedSurfaceHostTests)

    add_executable(WidgetSurfaceCoordinatorTests
        WidgetSurfaceCoordinatorTests.cpp WidgetSurfaceCoordinator.cpp
        PinnedSurfacePolicy.cpp PinnedSurfacePlacement.cpp AccessibilityProvider.cpp AccessibilityEvents.cpp
        AccessibilityTree.cpp ControllerNavigation.cpp FocusNavigation.cpp WidgetSurfaceFocus.cpp
        DeclarativeRenderer.cpp DeclarativeLayout.cpp NativeStyle.cpp
        NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp
        ArtworkDecoderProcessOwner.cpp)
    target_compile_features(WidgetSurfaceCoordinatorTests PRIVATE cxx_std_20)
    target_compile_options(WidgetSurfaceCoordinatorTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetSurfaceCoordinatorTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX
        WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING)
    target_link_libraries(WidgetSurfaceCoordinatorTests PRIVATE
        user32 gdi32 d2d1 dwrite shcore winhttp windowscodecs psapi
        ole32 oleaut32 uiautomationcore)
    widgetrail_link_taffy(WidgetSurfaceCoordinatorTests)
    add_test(NAME WidgetSurfaceCoordinatorTests COMMAND WidgetSurfaceCoordinatorTests)

    add_executable(PinnedSurfacePlacementTests
        PinnedSurfacePlacementTests.cpp PinnedSurfacePlacement.cpp PinnedSurfacePolicy.cpp)
    target_compile_features(PinnedSurfacePlacementTests PRIVATE cxx_std_20)
    target_compile_options(PinnedSurfacePlacementTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(PinnedSurfacePlacementTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(PinnedSurfacePlacementTests PRIVATE user32)
    add_test(NAME PinnedSurfacePlacementTests COMMAND PinnedSurfacePlacementTests)

    add_executable(OverlayTargetingTests OverlayTargetingTests.cpp OverlayTargeting.cpp)
    target_compile_features(OverlayTargetingTests PRIVATE cxx_std_20)
    target_compile_options(OverlayTargetingTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME OverlayTargetingTests COMMAND OverlayTargetingTests)

    add_executable(OverlayTransitionTests
        OverlayTransitionTests.cpp OverlayTransition.cpp)
    target_compile_features(OverlayTransitionTests PRIVATE cxx_std_20)
    target_compile_options(OverlayTransitionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(OverlayTransitionTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME OverlayTransitionTests COMMAND OverlayTransitionTests)

    add_executable(OverlayChromeTests OverlayChromeTests.cpp OverlayChrome.cpp)
    target_compile_features(OverlayChromeTests PRIVATE cxx_std_20)
    target_compile_options(OverlayChromeTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(OverlayChromeTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(OverlayChromeTests PRIVATE d2d1 windowscodecs ole32)
    add_test(NAME OverlayChromeTests COMMAND OverlayChromeTests)

    add_executable(GuideInputCompatibilityTests
        GuideInputCompatibilityTests.cpp GuideInputCompatibility.cpp
        ControllerGuide.cpp)
    target_compile_features(GuideInputCompatibilityTests PRIVATE cxx_std_20)
    target_compile_options(GuideInputCompatibilityTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(GuideInputCompatibilityTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(GuideInputCompatibilityTests PRIVATE user32)
    add_test(NAME GuideInputCompatibilityTests COMMAND GuideInputCompatibilityTests)

    add_executable(ControllerInputOwnershipTests ControllerInputOwnershipTests.cpp)
    target_compile_features(ControllerInputOwnershipTests PRIVATE cxx_std_20)
    target_compile_options(ControllerInputOwnershipTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME ControllerInputOwnershipTests COMMAND ControllerInputOwnershipTests)

    add_executable(ControllerIsolationCoreTests
        ../../tests/OverlayPlatformInterop.Tests/ControllerIsolationCoreTests.cpp
        ../OverlayPlatformInterop/ControllerIsolationCore.cpp)
    target_compile_features(ControllerIsolationCoreTests PRIVATE cxx_std_20)
    target_compile_options(ControllerIsolationCoreTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ControllerIsolationCoreTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME ControllerIsolationCoreTests COMMAND ControllerIsolationCoreTests)

    add_executable(LocalControllerOwnerTests
        ../../tests/OverlayPlatformInterop.Tests/LocalControllerOwnerTests.cpp
        ../OverlayPlatformInterop/ControllerIsolationHostSession.cpp
        ../OverlayPlatformInterop/LocalControllerPolicy.cpp
        ../OverlayPlatformInterop/ControllerIsolationCore.cpp
        ../OverlayPlatformInterop/ControllerIsolationReader.cpp
        ../OverlayPlatformInterop/ControllerIsolationRoutingSession.cpp
        ../OverlayPlatformInterop/HidHideConfigurationAdapter.cpp
        ../OverlayPlatformInterop/ViGEmOutputAdapter.cpp)
    target_compile_features(LocalControllerOwnerTests PRIVATE cxx_std_20)
    target_compile_options(LocalControllerOwnerTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(LocalControllerOwnerTests PRIVATE WRAIL_LOCAL_CONTROLLER_TESTING
        WRAIL_GAMEINPUT_ISOLATION_READER WRAIL_CONTROLLER_ISOLATION_READER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(LocalControllerOwnerTests PRIVATE shell32 ole32)
    add_test(NAME LocalControllerOwnerTests COMMAND LocalControllerOwnerTests)
    set_tests_properties(LocalControllerOwnerTests PROPERTIES TIMEOUT 15)

    add_executable(ControllerIsolationReaderTests
        ../../tests/OverlayPlatformInterop.Tests/ControllerIsolationReaderTests.cpp
        ../OverlayPlatformInterop/ControllerIsolationReader.cpp)
    target_compile_features(ControllerIsolationReaderTests PRIVATE cxx_std_20)
    target_compile_options(ControllerIsolationReaderTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ControllerIsolationReaderTests PRIVATE
        WRAIL_CONTROLLER_ISOLATION_READER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME ControllerIsolationReaderTests COMMAND ControllerIsolationReaderTests)

    add_executable(ControllerIsolationRoutingSessionTests
        ../../tests/OverlayPlatformInterop.Tests/ControllerIsolationRoutingSessionTests.cpp
        ../OverlayPlatformInterop/ControllerIsolationRoutingSession.cpp
        ../OverlayPlatformInterop/ControllerIsolationReader.cpp
        ../OverlayPlatformInterop/ControllerIsolationCore.cpp)
    target_compile_features(ControllerIsolationRoutingSessionTests PRIVATE cxx_std_20)
    target_compile_options(ControllerIsolationRoutingSessionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ControllerIsolationRoutingSessionTests PRIVATE
        WRAIL_CONTROLLER_ISOLATION_READER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME ControllerIsolationRoutingSessionTests
        COMMAND ControllerIsolationRoutingSessionTests)

    add_executable(ViGEmOutputAdapterTests
        ../../tests/OverlayPlatformInterop.Tests/ViGEmOutputAdapterTests.cpp
        ../OverlayPlatformInterop/ViGEmOutputAdapter.cpp)
    target_compile_features(ViGEmOutputAdapterTests PRIVATE cxx_std_20)
    target_compile_options(ViGEmOutputAdapterTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ViGEmOutputAdapterTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME ViGEmOutputAdapterTests COMMAND ViGEmOutputAdapterTests)

    add_executable(HidHideConfigurationAdapterTests
        ../../tests/OverlayPlatformInterop.Tests/HidHideConfigurationAdapterTests.cpp
        ../OverlayPlatformInterop/HidHideConfigurationAdapter.cpp)
    target_compile_features(HidHideConfigurationAdapterTests PRIVATE cxx_std_20)
    target_compile_options(HidHideConfigurationAdapterTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(HidHideConfigurationAdapterTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME HidHideConfigurationAdapterTests
        COMMAND HidHideConfigurationAdapterTests)

    add_executable(ControllerNavigationTests
        ControllerNavigationTests.cpp ControllerNavigation.cpp)
    target_compile_features(ControllerNavigationTests PRIVATE cxx_std_20)
    target_compile_options(ControllerNavigationTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME ControllerNavigationTests COMMAND ControllerNavigationTests)

    add_executable(TextEntryModalTests
        TextEntryModalTests.cpp TextEntryActionAdmission.cpp TextEntryModal.cpp
        WidgetSurfaceFocus.cpp FocusNavigation.cpp)
    target_compile_features(TextEntryModalTests PRIVATE cxx_std_20)
    target_compile_options(TextEntryModalTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(TextEntryModalTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(TextEntryModalTests PRIVATE user32 gdi32 ole32 oleaut32 uiautomationcore)
    add_test(NAME TextEntryModalTests COMMAND TextEntryModalTests)

    add_executable(PressedInteractionTests PressedInteractionTests.cpp)
    target_compile_features(PressedInteractionTests PRIVATE cxx_std_20)
    target_compile_options(PressedInteractionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(PressedInteractionTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME PressedInteractionTests COMMAND PressedInteractionTests)

    add_executable(WidgetBridgeCatalogTests
        WidgetBridgeCatalogTests.cpp WidgetBridgeClient.cpp)
    target_compile_features(WidgetBridgeCatalogTests PRIVATE cxx_std_20)
    target_compile_options(WidgetBridgeCatalogTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetBridgeCatalogTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetBridgeCatalogTests PRIVATE windowsapp user32)
    add_test(NAME WidgetBridgeCatalogTests COMMAND WidgetBridgeCatalogTests)

    add_executable(BackgroundSurfaceHostTests
        BackgroundSurfaceHostTests.cpp WidgetBridgeClient.cpp
        PublicSuffixDomainAuthority.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        CompositorBackgroundSurfaceCoordinator.cpp OverlayCompositionSurface.cpp
        NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp
        RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    target_compile_features(BackgroundSurfaceHostTests PRIVATE cxx_std_20)
    target_compile_options(BackgroundSurfaceHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(BackgroundSurfaceHostTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING WRAIL_DECLARATIVE_RENDERER_TESTING
        WRAIL_COMPOSITOR_BACKGROUND_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(BackgroundSurfaceHostTests PRIVATE
        windowsapp user32 bcrypt normaliz d2d1 dwrite d3d11 dxgi dcomp
        winhttp windowscodecs ole32)
    widgetrail_link_taffy(BackgroundSurfaceHostTests)
    add_test(NAME BackgroundSurfaceHostTests COMMAND BackgroundSurfaceHostTests)

    add_executable(LocalWidgetPackageImportTests
        LocalWidgetPackageImportTests.cpp LocalWidgetPackageImport.cpp)
    target_compile_features(LocalWidgetPackageImportTests PRIVATE cxx_std_20)
    target_compile_options(LocalWidgetPackageImportTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(LocalWidgetPackageImportTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(LocalWidgetPackageImportTests PRIVATE user32 ole32 uuid comctl32)
    target_link_options(LocalWidgetPackageImportTests PRIVATE /MANIFEST:EMBED "/MANIFESTINPUT:${CMAKE_CURRENT_SOURCE_DIR}/app.manifest")
    add_test(NAME LocalWidgetPackageImportTests COMMAND LocalWidgetPackageImportTests)

    add_executable(SliderInteractionTests SliderInteractionTests.cpp SliderInteraction.cpp)
    target_compile_features(SliderInteractionTests PRIVATE cxx_std_20)
    target_compile_options(SliderInteractionTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME SliderInteractionTests COMMAND SliderInteractionTests)

    add_executable(WidgetInteractionSessionTests
        WidgetInteractionSessionTests.cpp WidgetInteractionSession.cpp
        ControllerNavigation.cpp FocusNavigation.cpp SliderInteraction.cpp
        WidgetSurfaceFocus.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp
        NativeIcons.cpp RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    target_compile_features(WidgetInteractionSessionTests PRIVATE cxx_std_20)
    target_compile_options(WidgetInteractionSessionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetInteractionSessionTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetInteractionSessionTests PRIVATE
        d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(WidgetInteractionSessionTests)
    add_test(NAME WidgetInteractionSessionTests COMMAND WidgetInteractionSessionTests)

    add_executable(FocusNavigationTests
        FocusNavigationTests.cpp FocusNavigation.cpp)
    target_compile_features(FocusNavigationTests PRIVATE cxx_std_20)
    target_compile_options(FocusNavigationTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(FocusNavigationTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME FocusNavigationTests COMMAND FocusNavigationTests)

    add_executable(WidgetSurfaceFocusTests
        WidgetSurfaceFocusTests.cpp WidgetSurfaceFocus.cpp FocusNavigation.cpp)
    target_compile_features(WidgetSurfaceFocusTests PRIVATE cxx_std_20)
    target_compile_options(WidgetSurfaceFocusTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetSurfaceFocusTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME WidgetSurfaceFocusTests COMMAND WidgetSurfaceFocusTests)

    add_executable(WidgetActionFeedbackTests
        WidgetActionFeedbackTests.cpp WidgetActionFeedback.cpp)
    target_compile_features(WidgetActionFeedbackTests PRIVATE cxx_std_20)
    target_compile_options(WidgetActionFeedbackTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetActionFeedbackTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME WidgetActionFeedbackTests COMMAND WidgetActionFeedbackTests)

    add_executable(AccessibilityTreeTests
        AccessibilityTreeTests.cpp AccessibilityTree.cpp)
    target_compile_features(AccessibilityTreeTests PRIVATE cxx_std_20)
    target_compile_options(AccessibilityTreeTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AccessibilityTreeTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME AccessibilityTreeTests COMMAND AccessibilityTreeTests)

    add_executable(AccessibilityProjectionTests AccessibilityProjectionTests.cpp)
    target_compile_features(AccessibilityProjectionTests PRIVATE cxx_std_20)
    target_compile_options(AccessibilityProjectionTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AccessibilityProjectionTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME AccessibilityProjectionTests COMMAND AccessibilityProjectionTests)

    add_executable(AccessibilityProviderTests
        AccessibilityProviderTests.cpp AccessibilityProvider.cpp AccessibilityEvents.cpp)
    target_compile_features(AccessibilityProviderTests PRIVATE cxx_std_20)
    target_compile_options(AccessibilityProviderTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AccessibilityProviderTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(AccessibilityProviderTests PRIVATE user32 ole32 oleaut32 uiautomationcore)
    add_test(NAME AccessibilityProviderTests COMMAND AccessibilityProviderTests)

    add_executable(RealHostAccessibilityTests
        RealHostAccessibilityTests.cpp AccessibilityTree.cpp HostAccessibility.cpp
        AccessibilityProvider.cpp AccessibilityEvents.cpp TrayLayout.cpp
        WidgetBridgeClient.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp
        RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp FocusNavigation.cpp)
    target_compile_features(RealHostAccessibilityTests PRIVATE cxx_std_20)
    target_compile_options(RealHostAccessibilityTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(RealHostAccessibilityTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(RealHostAccessibilityTests PRIVATE
        windowsapp user32 ole32 oleaut32 uiautomationcore d2d1 dwrite winhttp windowscodecs)
    widgetrail_link_taffy(RealHostAccessibilityTests)
    add_test(NAME RealHostAccessibilityTests COMMAND RealHostAccessibilityTests)

    # Cross-process production-host coverage. This target is invoked by
    # build.ps1 with an isolated managed YT Music fixture and packaged host
    # directory; CTest deliberately omits it because those paths are required.
    add_executable(WidgetActionFailureHostTests WidgetActionFailureHostTests.cpp)
    target_compile_features(WidgetActionFailureHostTests PRIVATE cxx_std_20)
    target_compile_options(WidgetActionFailureHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetActionFailureHostTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetActionFailureHostTests PRIVATE
        user32 ole32 oleaut32 uiautomationcore)

    # Product-geometry switch coverage runs against a packaged production host
    # and a deterministic managed worker, so build.ps1 supplies those paths.
    add_executable(WidgetSwitchHostTests
        WidgetSwitchHostTests.cpp OverlayHostTestSupport.cpp TrayLayout.cpp)
    target_compile_features(WidgetSwitchHostTests PRIVATE cxx_std_20)
    target_compile_options(WidgetSwitchHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(WidgetSwitchHostTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetSwitchHostTests PRIVATE
        user32 gdi32 windowscodecs ole32)

    # Cold-start dashboard placement coverage launches a fresh production host
    # and inspects its first committed HWND/UIA geometry.
    add_executable(ColdDashboardHostTests
        ColdDashboardHostTests.cpp OverlayHostTestSupport.cpp)
    target_compile_features(ColdDashboardHostTests PRIVATE cxx_std_20)
    target_compile_options(ColdDashboardHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(ColdDashboardHostTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(ColdDashboardHostTests PRIVATE
        user32 ole32 oleaut32 uiautomationcore)

    # Real-product Audio Mixer focus/scroll coverage runs the production host
    # against a deterministic worker built from the first-party widget.
    add_executable(AudioMixerScrollHostTests
        AudioMixerScrollHostTests.cpp OverlayHostTestSupport.cpp)
    target_compile_features(AudioMixerScrollHostTests PRIVATE cxx_std_20)
    target_compile_options(AudioMixerScrollHostTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AudioMixerScrollHostTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(AudioMixerScrollHostTests PRIVATE
        user32 gdi32 windowscodecs ole32 oleaut32 uiautomationcore)

    add_executable(TrayLayoutTests TrayLayoutTests.cpp TrayLayout.cpp)
    target_compile_features(TrayLayoutTests PRIVATE cxx_std_20)
    target_compile_options(TrayLayoutTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(TrayLayoutTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME TrayLayoutTests COMMAND TrayLayoutTests)

    add_executable(HostAccessibilityTests
        HostAccessibilityTests.cpp HostAccessibility.cpp TrayLayout.cpp)
    target_compile_features(HostAccessibilityTests PRIVATE cxx_std_20)
    target_compile_options(HostAccessibilityTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(HostAccessibilityTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME HostAccessibilityTests COMMAND HostAccessibilityTests)

    add_executable(AccessibilityEventsTests
        AccessibilityEventsTests.cpp AccessibilityEvents.cpp)
    target_compile_features(AccessibilityEventsTests PRIVATE cxx_std_20)
    target_compile_options(AccessibilityEventsTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AccessibilityEventsTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME AccessibilityEventsTests COMMAND AccessibilityEventsTests)

    add_executable(DeveloperInspectorTests DeveloperInspectorTests.cpp DeveloperInspector.cpp DeveloperInspectorWindow.cpp NativeStyle.cpp)
    target_compile_features(DeveloperInspectorTests PRIVATE cxx_std_20)
    target_compile_options(DeveloperInspectorTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(DeveloperInspectorTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(DeveloperInspectorTests PRIVATE user32 gdi32 comctl32)
    add_test(NAME DeveloperInspectorTests COMMAND DeveloperInspectorTests)

    add_executable(DeclarativeRendererTests
        DeclarativeRendererTests.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp
        RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    target_compile_features(DeclarativeRendererTests PRIVATE cxx_std_20)
    target_compile_options(DeclarativeRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(DeclarativeRendererTests PRIVATE
        WRAIL_DECLARATIVE_RENDERER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(DeclarativeRendererTests PRIVATE d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(DeclarativeRendererTests)
    add_test(NAME DeclarativeRendererTests COMMAND DeclarativeRendererTests)

    # Identity-neutral real-widget responsive coverage: a managed YT Music
    # fixture supplies its semantic snapshot and bridge-computed WRSS styles.
    add_executable(YtMusicResponsiveRendererTests
        YtMusicResponsiveRendererTests.cpp WidgetBridgeClient.cpp
        DeclarativeRenderer.cpp DeclarativeLayout.cpp NativeStyle.cpp
        NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp
        ArtworkDecoderProcessOwner.cpp)
    target_compile_features(YtMusicResponsiveRendererTests PRIVATE cxx_std_20)
    target_compile_options(YtMusicResponsiveRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(YtMusicResponsiveRendererTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING WRAIL_DECLARATIVE_RENDERER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(YtMusicResponsiveRendererTests PRIVATE
        windowsapp user32 d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(YtMusicResponsiveRendererTests)

    # Real managed Settings root coverage for the public Preferred-by-Content
    # surface contract. The target consumes a test-generated bridge response.
    add_executable(SettingsContentRendererTests
        SettingsContentRendererTests.cpp WidgetBridgeClient.cpp
        DeclarativeRenderer.cpp DeclarativeLayout.cpp NativeStyle.cpp
        NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp
        ArtworkDecoderProcessOwner.cpp)
    target_compile_features(SettingsContentRendererTests PRIVATE cxx_std_20)
    target_compile_options(SettingsContentRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(SettingsContentRendererTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING WRAIL_DECLARATIVE_RENDERER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(SettingsContentRendererTests PRIVATE
        windowsapp user32 d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(SettingsContentRendererTests)

    # Real managed Audio Mixer first-page coverage for generic stretch/flex
    # width consumption. The target consumes a test-generated bridge response.
    add_executable(AudioMixerWidthRendererTests
        AudioMixerWidthRendererTests.cpp WidgetBridgeClient.cpp
        DeclarativeRenderer.cpp DeclarativeLayout.cpp NativeStyle.cpp
        NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp
        ArtworkDecoderProcessOwner.cpp)
    target_compile_features(AudioMixerWidthRendererTests PRIVATE cxx_std_20)
    target_compile_options(AudioMixerWidthRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(AudioMixerWidthRendererTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING WRAIL_DECLARATIVE_RENDERER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(AudioMixerWidthRendererTests PRIVATE
        windowsapp user32 d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(AudioMixerWidthRendererTests)

    # Real managed Network NotScanned first-page coverage for authored vertical
    # admission and constrained focus reveal.
    add_executable(NetworkFirstPageRendererTests
        NetworkFirstPageRendererTests.cpp WidgetBridgeClient.cpp
        DeclarativeRenderer.cpp DeclarativeLayout.cpp NativeStyle.cpp
        NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp
        ArtworkDecoderProcessOwner.cpp)
    target_compile_features(NetworkFirstPageRendererTests PRIVATE cxx_std_20)
    target_compile_options(NetworkFirstPageRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(NetworkFirstPageRendererTests PRIVATE
        WRAIL_WIDGET_BRIDGE_CLIENT_TESTING WRAIL_DECLARATIVE_RENDERER_TESTING
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(NetworkFirstPageRendererTests PRIVATE
        windowsapp user32 d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(NetworkFirstPageRendererTests)

    add_executable(SemanticChurnPerformanceTests
        SemanticChurnPerformanceTests.cpp AccessibilityTree.cpp DeclarativeRenderer.cpp
        DeclarativeLayout.cpp NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp
        NativeIcons.cpp RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    target_compile_features(SemanticChurnPerformanceTests PRIVATE cxx_std_20)
    target_compile_options(SemanticChurnPerformanceTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(SemanticChurnPerformanceTests PRIVATE
        WRAIL_DECLARATIVE_RENDERER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(SemanticChurnPerformanceTests PRIVATE
        d2d1 dwrite winhttp windowscodecs ole32 psapi)
    widgetrail_link_taffy(SemanticChurnPerformanceTests)
    add_test(NAME SemanticChurnPerformanceTests COMMAND SemanticChurnPerformanceTests)

    add_executable(SharedComponentGeometryTests
        SharedComponentGeometryTests.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp NativeTextLayout.cpp DeclarativeMotion.cpp NativeIcons.cpp
        RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    target_compile_features(SharedComponentGeometryTests PRIVATE cxx_std_20)
    target_compile_options(SharedComponentGeometryTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(SharedComponentGeometryTests PRIVATE
        WRAIL_DECLARATIVE_RENDERER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(SharedComponentGeometryTests PRIVATE
        d2d1 dwrite winhttp windowscodecs ole32)
    widgetrail_link_taffy(SharedComponentGeometryTests)
    add_test(NAME SharedComponentGeometryTests COMMAND SharedComponentGeometryTests)

    add_executable(RemoteImageCacheTests
        RemoteImageCacheTests.cpp RemoteImageCache.cpp ArtworkDecoderProcessOwner.cpp)
    add_dependencies(RemoteImageCacheTests ArtworkDecoderHost ArtworkDecoderTestHost)
    target_compile_features(RemoteImageCacheTests PRIVATE cxx_std_20)
    target_compile_options(RemoteImageCacheTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(RemoteImageCacheTests PRIVATE
        UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(RemoteImageCacheTests PRIVATE
        d2d1 winhttp windowscodecs ole32)
    add_test(NAME RemoteImageCacheTests COMMAND RemoteImageCacheTests)
endif()
