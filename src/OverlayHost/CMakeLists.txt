cmake_minimum_required(VERSION 3.24)

project(GameBarAlternativeOverlayHost LANGUAGES CXX)

set(GAMEINPUT_VERSION "3.5.262")
set(GAMEINPUT_ROOT "$ENV{USERPROFILE}/.nuget/packages/microsoft.gameinput/${GAMEINPUT_VERSION}")
if(NOT EXISTS "${GAMEINPUT_ROOT}/native/include/GameInput.h")
    message(FATAL_ERROR "Restore NativeDependencies.csproj before configuring CMake")
endif()

option(GBA_BUILD_TESTS "Build the dependency-free state-machine tests" ON)

# Widget labels and controller guides intentionally contain non-ASCII source
# text. Apply UTF-8 to every native target so CMake and the canonical build.ps1
# path compile identical literals (including test executables).
add_compile_options(/utf-8)

add_executable(OverlayHost WIN32
    main.cpp
    OverlayState.cpp
    WidgetBridgeClient.cpp
    RemoteImageCache.cpp
    DeclarativeLayout.cpp
    NativeIcons.cpp
    NativeStyle.cpp
    DeclarativeMotion.cpp
    OverlayPlacement.cpp
    OverlayTargeting.cpp
    OverlayTransition.cpp
    DeclarativeRenderer.cpp
    GuideInputCompatibility.cpp
    ControllerNavigation.cpp
    SliderInteraction.cpp
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
target_compile_definitions(OverlayHost PRIVATE USING_GAMEINPUT)
target_include_directories(OverlayHost PRIVATE "${GAMEINPUT_ROOT}/native/include")
target_link_directories(OverlayHost PRIVATE "${GAMEINPUT_ROOT}/native/lib/x64")
target_compile_definitions(OverlayHost PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)
target_compile_options(OverlayHost PRIVATE /W4 /permissive- /EHsc)
target_link_libraries(OverlayHost PRIVATE
    user32
    gdi32
    d2d1
    dwrite
    dwmapi
    gameinput
    shcore
    xinput9_1_0
    windowsapp
    winhttp
    windowscodecs
    ole32
    oleaut32
    uiautomationcore
)

if(GBA_BUILD_TESTS)
    enable_testing()
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

    add_executable(GuideInputCompatibilityTests
        GuideInputCompatibilityTests.cpp GuideInputCompatibility.cpp)
    target_compile_features(GuideInputCompatibilityTests PRIVATE cxx_std_20)
    target_compile_options(GuideInputCompatibilityTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(GuideInputCompatibilityTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(GuideInputCompatibilityTests PRIVATE user32)
    add_test(NAME GuideInputCompatibilityTests COMMAND GuideInputCompatibilityTests)

    add_executable(ControllerInputOwnershipTests ControllerInputOwnershipTests.cpp)
    target_compile_features(ControllerInputOwnershipTests PRIVATE cxx_std_20)
    target_compile_options(ControllerInputOwnershipTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME ControllerInputOwnershipTests COMMAND ControllerInputOwnershipTests)

    add_executable(ControllerNavigationTests
        ControllerNavigationTests.cpp ControllerNavigation.cpp)
    target_compile_features(ControllerNavigationTests PRIVATE cxx_std_20)
    target_compile_options(ControllerNavigationTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME ControllerNavigationTests COMMAND ControllerNavigationTests)

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
        GBA_WIDGET_BRIDGE_CLIENT_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(WidgetBridgeCatalogTests PRIVATE windowsapp user32)
    add_test(NAME WidgetBridgeCatalogTests COMMAND WidgetBridgeCatalogTests)

    add_executable(SliderInteractionTests SliderInteractionTests.cpp SliderInteraction.cpp)
    target_compile_features(SliderInteractionTests PRIVATE cxx_std_20)
    target_compile_options(SliderInteractionTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME SliderInteractionTests COMMAND SliderInteractionTests)

    add_executable(FocusNavigationTests
        FocusNavigationTests.cpp FocusNavigation.cpp)
    target_compile_features(FocusNavigationTests PRIVATE cxx_std_20)
    target_compile_options(FocusNavigationTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(FocusNavigationTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    add_test(NAME FocusNavigationTests COMMAND FocusNavigationTests)

    add_executable(WidgetSurfaceFocusTests
        WidgetSurfaceFocusTests.cpp WidgetSurfaceFocus.cpp)
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

    add_executable(DeclarativeRendererTests
        DeclarativeRendererTests.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp)
    target_compile_features(DeclarativeRendererTests PRIVATE cxx_std_20)
    target_compile_options(DeclarativeRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(DeclarativeRendererTests PRIVATE
        GBA_DECLARATIVE_RENDERER_TESTING UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(DeclarativeRendererTests PRIVATE d2d1 dwrite winhttp windowscodecs ole32)
    add_test(NAME DeclarativeRendererTests COMMAND DeclarativeRendererTests)
endif()
