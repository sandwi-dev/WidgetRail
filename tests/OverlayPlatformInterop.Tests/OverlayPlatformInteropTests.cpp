#include "../../src/OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "../../src/OverlayPlatformInterop/OverlayPlatformPolicy.h"
#include "../../src/OverlayHost/OverlayState.h"

#include <Windows.h>
#include <Xinput.h>

#include <atomic>
#include <cstddef>
#include <cstdlib>
#include <iostream>

namespace {

int checks{};

struct CallbackProbe final {
    std::atomic_uint32_t eventSignals{};
    std::atomic_uint32_t diagnosticSignals{};
    std::atomic_uint32_t postShutdownSignals{};
    std::atomic_bool shutdownStarted{};
};

void GBA_OVERLAY_PLATFORM_CALL OnEventAvailable(void* context) noexcept {
    auto& probe = *static_cast<CallbackProbe*>(context);
    if (probe.shutdownStarted.load(std::memory_order_acquire)) {
        probe.postShutdownSignals.fetch_add(1, std::memory_order_relaxed);
    }
    probe.eventSignals.fetch_add(1, std::memory_order_relaxed);
}

void GBA_OVERLAY_PLATFORM_CALL OnDiagnostic(
    void* context,
    const wchar_t*) noexcept {
    auto& probe = *static_cast<CallbackProbe*>(context);
    if (probe.shutdownStarted.load(std::memory_order_acquire)) {
        probe.postShutdownSignals.fetch_add(1, std::memory_order_relaxed);
    }
    probe.diagnosticSignals.fetch_add(1, std::memory_order_relaxed);
}

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
    const HMODULE importedModule =
        GetModuleHandleW(L"OverlayPlatformInterop.dll");
    Check(importedModule != nullptr &&
              GetProcAddress(importedModule,
                  "GbaOverlayPlatformGetAbiVersion") != nullptr,
          "the focused executable imports the built public DLL ABI");
    Check(sizeof(GbaOverlayPlatformControllerFrame) == 76 &&
              offsetof(GbaOverlayPlatformControllerFrame, state) == 20 &&
              offsetof(
                  GbaOverlayPlatformControllerFrame,
                  stickNavigation) == 60 &&
              sizeof(GbaOverlayPlatformCreateOptions) == 32,
          "the managed-facing version-1 layouts match the fixed-width contract");

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
    std::uint32_t hasPlacement = GBA_OVERLAY_PLATFORM_FALSE;
    Check(GbaOverlayPlatformComputePlacement(
              &placementInput, &placement, &hasPlacement) ==
              GbaOverlayPlatformStatus::Ok &&
              hasPlacement != GBA_OVERLAY_PLATFORM_FALSE,
          "the versioned placement entrypoint resolves a valid PMv2 work area");
    Check(placement.x >= placementInput.workLeft &&
              placement.y >= placementInput.workTop &&
              placement.x + placement.width <= placementInput.workRight &&
              placement.y + placement.height <= placementInput.workBottom,
          "resolved placement is contained by a negative-origin monitor work area");

    CallbackProbe callbackProbe;
    GbaOverlayPlatformCreateOptions options;
    options.callbackContext = &callbackProbe;
    options.eventAvailable = OnEventAvailable;
    options.diagnostic = OnDiagnostic;
    GbaOverlayPlatformHandle* handle{};
    Check(GbaOverlayPlatformCreate(&options, &handle) ==
              GbaOverlayPlatformStatus::Ok &&
              handle != nullptr,
          "a version-matched caller creates one opaque platform owner");
    Check(GbaOverlayPlatformSetOwnedWindows(handle, 10, 20) ==
              GbaOverlayPlatformStatus::Ok &&
              GbaOverlayPlatformObserveForegroundTarget(
                  handle, 10, GBA_OVERLAY_PLATFORM_TRUE) ==
                  GBA_OVERLAY_PLATFORM_FALSE &&
              GbaOverlayPlatformObserveForegroundTarget(
                  handle, 30, GBA_OVERLAY_PLATFORM_TRUE) ==
                  GBA_OVERLAY_PLATFORM_TRUE &&
              GbaOverlayPlatformRememberedForegroundTarget(handle) == 30,
          "foreground targeting rejects owned windows and remembers one external target");
    Check(GbaOverlayPlatformResolveForegroundTarget(
              handle, 10, GBA_OVERLAY_PLATFORM_TRUE) == 30 &&
              GbaOverlayPlatformResolveForegroundTarget(
                  handle, 10, GBA_OVERLAY_PLATFORM_FALSE) == 10,
          "target resolution uses valid remembered authority and bounded fallback");

    Check(GbaOverlayPlatformInitialize(handle) ==
              GbaOverlayPlatformStatus::Ok,
          "native initialization remains usable with GameInput or its existing fallback");
    Check(callbackProbe.diagnosticSignals.load(std::memory_order_acquire) > 0,
          "the imported DLL invokes the registered diagnostic callback");

    Check(GbaOverlayPlatformSetWindowState(
              handle,
              GBA_OVERLAY_PLATFORM_TRUE,
              GBA_OVERLAY_PLATFORM_TRUE) == GbaOverlayPlatformStatus::Ok &&
              callbackProbe.eventSignals.load(std::memory_order_acquire) >= 2,
          "visibility and focus changes signal the registered event callback");
    GbaOverlayPlatformControllerFrame visibleFrame;
    Check(GbaOverlayPlatformReadController(
              handle,
              GBA_OVERLAY_PLATFORM_TRUE,
              1'000,
              &visibleFrame) == GbaOverlayPlatformStatus::Ok &&
              visibleFrame.readPath != GbaOverlayPlatformReadPath::None,
          "a visible platform lease owns a controller read path");

    Check(GbaOverlayPlatformSetWindowState(
              handle,
              GBA_OVERLAY_PLATFORM_FALSE,
              GBA_OVERLAY_PLATFORM_FALSE) == GbaOverlayPlatformStatus::Ok,
          "close-animation begin retires the visible platform lease immediately");
    GbaOverlayPlatformControllerFrame closingFrame;
    Check(GbaOverlayPlatformReadController(
              handle,
              GBA_OVERLAY_PLATFORM_FALSE,
              1'010,
              &closingFrame) == GbaOverlayPlatformStatus::Ok &&
              closingFrame.readPath == GbaOverlayPlatformReadPath::None,
          "controller ownership is dormant during the closing interval");

    Check(GbaOverlayPlatformSetWindowState(
              handle,
              GBA_OVERLAY_PLATFORM_TRUE,
              GBA_OVERLAY_PLATFORM_TRUE) == GbaOverlayPlatformStatus::Ok,
          "rapid reopen restores the platform lease while the HWND is still visible");
    GbaOverlayPlatformControllerFrame reopenedFrame;
    Check(GbaOverlayPlatformReadController(
              handle,
              GBA_OVERLAY_PLATFORM_TRUE,
              1'011,
              &reopenedFrame) == GbaOverlayPlatformStatus::Ok &&
              reopenedFrame.readPath != GbaOverlayPlatformReadPath::None,
          "rapid reopen returns controller read ownership on the next deterministic sample");

    GbaOverlayPlatformEvent event;
    std::uint32_t hasEvent = GBA_OVERLAY_PLATFORM_FALSE;
    Check(GbaOverlayPlatformDrainEvent(handle, 1'020, &event, &hasEvent) ==
              GbaOverlayPlatformStatus::Ok &&
              hasEvent == GBA_OVERLAY_PLATFORM_TRUE,
          "the callback signal corresponds to a drainable public-ABI event");

    callbackProbe.shutdownStarted.store(true, std::memory_order_release);
    const auto eventSignalsBeforeShutdown =
        callbackProbe.eventSignals.load(std::memory_order_acquire);
    const auto diagnosticSignalsBeforeShutdown =
        callbackProbe.diagnosticSignals.load(std::memory_order_acquire);
    GbaOverlayPlatformShutdown(handle);
    GbaOverlayPlatformShutdown(handle);
    Check(GbaOverlayPlatformInitialize(handle) ==
              GbaOverlayPlatformStatus::ShutDown,
          "clean shutdown is idempotent and cannot resurrect callback ownership");
    Check(GbaOverlayPlatformSetWindowState(
              handle,
              GBA_OVERLAY_PLATFORM_TRUE,
              GBA_OVERLAY_PLATFORM_TRUE) == GbaOverlayPlatformStatus::ShutDown &&
              GbaOverlayPlatformDrainEvent(
                  handle, 1'030, &event, &hasEvent) ==
                  GbaOverlayPlatformStatus::ShutDown &&
              callbackProbe.eventSignals.load(std::memory_order_acquire) ==
                  eventSignalsBeforeShutdown &&
              callbackProbe.diagnosticSignals.load(std::memory_order_acquire) ==
                  diagnosticSignalsBeforeShutdown &&
              callbackProbe.postShutdownSignals.load(
                  std::memory_order_acquire) == 0,
          "shutdown drains callback ownership and rejects post-shutdown signaling");
    GbaOverlayPlatformDestroy(handle);
    Check(callbackProbe.eventSignals.load(std::memory_order_acquire) ==
              eventSignalsBeforeShutdown &&
              callbackProbe.diagnosticSignals.load(std::memory_order_acquire) ==
                  diagnosticSignalsBeforeShutdown &&
              callbackProbe.postShutdownSignals.load(
                  std::memory_order_acquire) == 0,
          "destroy after idempotent shutdown produces no callback or freed-context use");

    std::cout << "OverlayPlatformInteropTests passed (" << checks
              << " checks)\n";
    return EXIT_SUCCESS;
}
