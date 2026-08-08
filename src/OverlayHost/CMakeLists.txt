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
    DeclarativeRenderer.cpp
    GuideInputCompatibility.cpp
    ControllerNavigation.cpp
    SliderInteraction.cpp
    FocusNavigation.cpp
    WidgetSurfaceFocus.cpp
    WidgetLifecycle.cpp
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

    add_executable(GuideInputCompatibilityTests
        GuideInputCompatibilityTests.cpp GuideInputCompatibility.cpp)
    target_compile_features(GuideInputCompatibilityTests PRIVATE cxx_std_20)
    target_compile_options(GuideInputCompatibilityTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(GuideInputCompatibilityTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(GuideInputCompatibilityTests PRIVATE user32)
    add_test(NAME GuideInputCompatibilityTests COMMAND GuideInputCompatibilityTests)

    add_executable(ControllerNavigationTests
        ControllerNavigationTests.cpp ControllerNavigation.cpp)
    target_compile_features(ControllerNavigationTests PRIVATE cxx_std_20)
    target_compile_options(ControllerNavigationTests PRIVATE /W4 /permissive- /EHsc)
    add_test(NAME ControllerNavigationTests COMMAND ControllerNavigationTests)

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

    add_executable(DeclarativeRendererTests
        DeclarativeRendererTests.cpp DeclarativeRenderer.cpp DeclarativeLayout.cpp
        NativeStyle.cpp DeclarativeMotion.cpp NativeIcons.cpp RemoteImageCache.cpp)
    target_compile_features(DeclarativeRendererTests PRIVATE cxx_std_20)
    target_compile_options(DeclarativeRendererTests PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(DeclarativeRendererTests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(DeclarativeRendererTests PRIVATE d2d1 dwrite winhttp windowscodecs ole32)
    add_test(NAME DeclarativeRendererTests COMMAND DeclarativeRendererTests)
endif()
