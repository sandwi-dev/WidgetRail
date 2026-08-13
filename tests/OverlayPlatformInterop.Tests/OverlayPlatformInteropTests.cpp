#include "../../src/OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "../../src/OverlayPlatformInterop/OverlayPlatformPolicy.h"
#include "../../src/OverlayHost/OverlayState.h"

#include <Windows.h>
#include <Xinput.h>

#include <cstdlib>
#include <iostream>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

} // namespace

int main() {
    Check(GbaOverlayPlatformGetAbiVersion() ==
              GBA_OVERLAY_PLATFORM_ABI_VERSION,
          "the exported ABI reports the version compiled into the caller");

    GbaOverlayPlatformCreateOptions invalidOptions;
    invalidOptions.abiVersion = GBA_OVERLAY_PLATFORM_ABI_VERSION + 1;
    GbaOverlayPlatformHandle* invalidHandle{};
    Check(GbaOverlayPlatformCreate(&invalidOptions, &invalidHandle) ==
              GbaOverlayPlatformStatus::InvalidVersion &&
              invalidHandle == nullptr,
          "a mismatched caller version is rejected before native ownership starts");

    gba::platform::GuideToggleDebouncer guide;
    gba::OverlayState overlay({}, {L"widget"});
    Check(guide.Accept(1'000) &&
              overlay.Dispatch(gba::Command::ToggleOverlay) &&
              overlay.surface() == gba::Surface::Dashboard,
          "the first Guide edge shows the hidden overlay");
    Check(!guide.Accept(1'149) &&
              overlay.surface() == gba::Surface::Dashboard,
          "a duplicate Guide source inside the debounce window cannot double toggle");
    Check(guide.Accept(1'150) &&
              overlay.Dispatch(gba::Command::ToggleOverlay) &&
              overlay.surface() == gba::Surface::Hidden,
          "a later Guide edge hides through the same host command");

    gba::platform::ControllerFrameTracker tracker;
    GbaOverlayPlatformRawControllerState state;
    auto frame = tracker.Update(true, state, 0);
    Check(frame.primed && frame.pressedButtons == 0,
          "the first connected sample establishes a neutral baseline");

    state.buttons = XINPUT_GAMEPAD_DPAD_RIGHT;
    frame = tracker.Update(true, state, 10);
    Check(frame.pressedButtons == XINPUT_GAMEPAD_DPAD_RIGHT &&
              frame.dpadNavigation.direction ==
                  GbaOverlayPlatformNavigationDirection::Right &&
              frame.dpadNavigation.phase ==
                  GbaOverlayPlatformNavigationPhase::Pressed,
          "a D-pad edge produces one pressed navigation event");
    frame = tracker.Update(true, state, 369);
    Check(frame.dpadNavigation.direction ==
              GbaOverlayPlatformNavigationDirection::None,
          "held navigation waits for the bounded initial repeat");
    frame = tracker.Update(true, state, 370);
    Check(frame.dpadNavigation.direction ==
              GbaOverlayPlatformNavigationDirection::Right &&
              frame.dpadNavigation.phase ==
                  GbaOverlayPlatformNavigationPhase::Repeated,
          "held navigation repeats on the production cadence");

    frame = tracker.Update(false, {}, 400);
    Check(!frame.connected &&
              frame.releasedButtons == XINPUT_GAMEPAD_DPAD_RIGHT &&
              frame.dpadNavigation.direction ==
                  GbaOverlayPlatformNavigationDirection::None,
          "device loss releases held buttons and clears repeat direction");

    tracker.Reset();
    state = {};
    state.buttons = XINPUT_GAMEPAD_A;
    frame = tracker.Update(true, state, 500);
    Check(frame.primed && frame.pressedButtons == 0,
          "visibility or focus reset primes a held reconnect sample without activation");
    state.buttons = 0;
    frame = tracker.Update(true, state, 510);
    Check(frame.releasedButtons == XINPUT_GAMEPAD_A,
          "the primed held button must become neutral before another press");
    state.buttons = XINPUT_GAMEPAD_A;
    frame = tracker.Update(true, state, 520);
    Check(frame.pressedButtons == XINPUT_GAMEPAD_A,
          "a post-neutral press is admitted after reconnect");

    state = {};
    state.leftTrigger = 31;
    frame = tracker.Update(true, state, 530);
    Check(frame.leftTriggerPressed,
          "trigger threshold crossing is emitted once by the native boundary");
    frame = tracker.Update(true, state, 540);
    Check(!frame.leftTriggerPressed,
          "a held trigger does not repeat as another press");
    state.leftTrigger = 0;
    frame = tracker.Update(true, state, 550);
    Check(frame.leftTriggerReleased,
          "trigger release is retained for pressed-interaction cleanup");

    tracker.Reset();
    state = {};
    tracker.Prime(true, state, 600);
    state.buttons = XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
    frame = tracker.Update(true, state, 610);
    Check(frame.recoveryChordPressed,
          "the recovery chord has one rising-edge owner");
    frame = tracker.Update(true, state, 620);
    Check(!frame.recoveryChordPressed,
          "the held recovery chord cannot retrigger");

    GbaOverlayPlatformPlacementInput placementInput;
    placementInput.workLeft = -1'920;
    placementInput.workTop = 0;
    placementInput.workRight = 0;
    placementInput.workBottom = 1'080;
    placementInput.dpi = 144;
    placementInput.desiredWidthDip = 1'180.0F;
    placementInput.desiredHeightDip = 700.0F;
    GbaOverlayPlatformPlacement placement;
    bool hasPlacement = false;
    Check(GbaOverlayPlatformComputePlacement(
              &placementInput, &placement, &hasPlacement) ==
              GbaOverlayPlatformStatus::Ok &&
              hasPlacement,
          "the versioned placement entrypoint resolves a valid PMv2 work area");
    Check(placement.x >= placementInput.workLeft &&
              placement.y >= placementInput.workTop &&
              placement.x + placement.width <= placementInput.workRight &&
              placement.y + placement.height <= placementInput.workBottom,
          "resolved placement is contained by a negative-origin monitor work area");

    GbaOverlayPlatformCreateOptions options;
    GbaOverlayPlatformHandle* handle{};
    Check(GbaOverlayPlatformCreate(&options, &handle) ==
              GbaOverlayPlatformStatus::Ok &&
              handle != nullptr,
          "a version-matched caller creates one opaque platform owner");
    Check(GbaOverlayPlatformSetOwnedWindows(handle, 10, 20) ==
              GbaOverlayPlatformStatus::Ok &&
              !GbaOverlayPlatformObserveForegroundTarget(handle, 10, true) &&
              GbaOverlayPlatformObserveForegroundTarget(handle, 30, true) &&
              GbaOverlayPlatformRememberedForegroundTarget(handle) == 30,
          "foreground targeting rejects owned windows and remembers one external target");
    Check(GbaOverlayPlatformResolveForegroundTarget(handle, 10, true) == 30 &&
              GbaOverlayPlatformResolveForegroundTarget(handle, 10, false) == 10,
          "target resolution uses valid remembered authority and bounded fallback");

    Check(GbaOverlayPlatformInitialize(handle) ==
              GbaOverlayPlatformStatus::Ok,
          "native initialization remains usable with GameInput or its existing fallback");
    GbaOverlayPlatformShutdown(handle);
    GbaOverlayPlatformShutdown(handle);
    Check(GbaOverlayPlatformInitialize(handle) ==
              GbaOverlayPlatformStatus::ShutDown,
          "clean shutdown is idempotent and cannot resurrect callback ownership");
    GbaOverlayPlatformDestroy(handle);

    std::cout << "OverlayPlatformInteropTests passed (" << checks
              << " checks)\n";
    return EXIT_SUCCESS;
}
