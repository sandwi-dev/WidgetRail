#include "../../src/OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "../../src/OverlayPlatformInterop/OverlayPlatformPolicy.h"
#include "../../src/OverlayHost/OverlayState.h"

#include <Windows.h>
#include <Xinput.h>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <thread>

namespace {

int checks{};

struct CallbackProbe final {
    std::atomic_uint32_t eventSignals{};
    std::atomic_uint32_t diagnosticSignals{};
    std::atomic_uint32_t postShutdownSignals{};
    std::atomic_bool shutdownReturned{};
    std::mutex holdMutex;
    std::condition_variable holdChanged;
    bool holdNextEvent{};
    bool heldEventEntered{};
    bool releaseHeldEvent{};
};

void WRAIL_OVERLAY_PLATFORM_CALL OnEventAvailable(void* context) noexcept {
    auto& probe = *static_cast<CallbackProbe*>(context);
    {
        std::unique_lock lock(probe.holdMutex);
        if (probe.holdNextEvent) {
            probe.heldEventEntered = true;
            probe.holdChanged.notify_all();
            probe.holdChanged.wait(lock, [&probe] {
                return probe.releaseHeldEvent;
            });
            probe.holdNextEvent = false;
        }
    }
    if (probe.shutdownReturned.load(std::memory_order_acquire)) {
        probe.postShutdownSignals.fetch_add(1, std::memory_order_relaxed);
    }
    probe.eventSignals.fetch_add(1, std::memory_order_relaxed);
}

void WRAIL_OVERLAY_PLATFORM_CALL OnDiagnostic(
    void* context,
    const wchar_t*) noexcept {
    auto& probe = *static_cast<CallbackProbe*>(context);
    if (probe.shutdownReturned.load(std::memory_order_acquire)) {
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
    Check(WidgetRailOverlayPlatformGetAbiVersion() ==
              WRAIL_OVERLAY_PLATFORM_ABI_VERSION,
          "the exported ABI reports the version compiled into the caller");
    const HMODULE importedModule =
        GetModuleHandleW(L"OverlayPlatformInterop.dll");
    Check(importedModule != nullptr &&
              GetProcAddress(importedModule,
                  "WidgetRailOverlayPlatformGetAbiVersion") != nullptr,
          "the focused executable imports the built public DLL ABI");
    Check(sizeof(WidgetRailOverlayPlatformControllerFrame) == 80 &&
              offsetof(WidgetRailOverlayPlatformControllerFrame, state) == 20 &&
              offsetof(
                  WidgetRailOverlayPlatformControllerFrame,
                  stickNavigation) == 60 &&
              offsetof(
                  WidgetRailOverlayPlatformControllerFrame,
                  remainingFrames) == 76 &&
              sizeof(WidgetRailOverlayPlatformCreateOptions) == 32,
          "the managed-facing version-2 layouts match the fixed-width contract");

    WidgetRailOverlayPlatformCreateOptions invalidOptions;
    invalidOptions.abiVersion = WRAIL_OVERLAY_PLATFORM_ABI_VERSION + 1;
    WidgetRailOverlayPlatformHandle* invalidHandle{};
    Check(WidgetRailOverlayPlatformCreate(&invalidOptions, &invalidHandle) ==
              WidgetRailOverlayPlatformStatus::InvalidVersion &&
              invalidHandle == nullptr,
          "a mismatched caller version is rejected before native ownership starts");

    widgetrail::platform::GuideToggleDebouncer guide;
    widgetrail::OverlayState overlay({}, {L"widget"});
    Check(guide.Accept(1'000) &&
              overlay.Dispatch(widgetrail::Command::ToggleOverlay) &&
              overlay.surface() == widgetrail::Surface::Dashboard,
          "the first Guide edge shows the hidden overlay");
    Check(!guide.Accept(1'149) &&
              overlay.surface() == widgetrail::Surface::Dashboard,
          "a duplicate Guide source inside the debounce window cannot double toggle");
    Check(guide.Accept(1'150) &&
              overlay.Dispatch(widgetrail::Command::ToggleOverlay) &&
              overlay.surface() == widgetrail::Surface::Hidden,
          "a later Guide edge hides through the same host command");

    widgetrail::platform::ControllerFrameTracker tracker;
    WidgetRailOverlayPlatformRawControllerState state;
    auto frame = tracker.Update(true, state, 0);
    Check(frame.primed && frame.pressedButtons == 0,
          "the first connected sample establishes a neutral baseline");

    state.buttons = XINPUT_GAMEPAD_DPAD_RIGHT;
    frame = tracker.Update(true, state, 10);
    Check(frame.pressedButtons == XINPUT_GAMEPAD_DPAD_RIGHT &&
              frame.dpadNavigation.direction ==
                  WidgetRailOverlayPlatformNavigationDirection::Right &&
              frame.dpadNavigation.phase ==
                  WidgetRailOverlayPlatformNavigationPhase::Pressed,
          "a D-pad edge produces one pressed navigation event");
    frame = tracker.Update(true, state, 369);
    Check(frame.dpadNavigation.direction ==
              WidgetRailOverlayPlatformNavigationDirection::None,
          "held navigation waits for the bounded initial repeat");
    frame = tracker.Update(true, state, 370);
    Check(frame.dpadNavigation.direction ==
              WidgetRailOverlayPlatformNavigationDirection::Right &&
              frame.dpadNavigation.phase ==
                  WidgetRailOverlayPlatformNavigationPhase::Repeated,
          "held navigation repeats on the production cadence");

    frame = tracker.Update(false, {}, 400);
    Check(!frame.connected &&
              frame.releasedButtons == XINPUT_GAMEPAD_DPAD_RIGHT &&
              frame.dpadNavigation.direction ==
                  WidgetRailOverlayPlatformNavigationDirection::None,
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

    WidgetRailOverlayPlatformPlacementInput placementInput;
    placementInput.workLeft = -1'920;
    placementInput.workTop = 0;
    placementInput.workRight = 0;
    placementInput.workBottom = 1'080;
    placementInput.dpi = 144;
    placementInput.desiredWidthDip = 1'180.0F;
    placementInput.desiredHeightDip = 700.0F;
    WidgetRailOverlayPlatformPlacement placement;
    std::uint32_t hasPlacement = WRAIL_OVERLAY_PLATFORM_FALSE;
    Check(WidgetRailOverlayPlatformComputePlacement(
              &placementInput, &placement, &hasPlacement) ==
              WidgetRailOverlayPlatformStatus::Ok &&
              hasPlacement != WRAIL_OVERLAY_PLATFORM_FALSE,
          "the versioned placement entrypoint resolves a valid PMv2 work area");
    Check(placement.x >= placementInput.workLeft &&
              placement.y >= placementInput.workTop &&
              placement.x + placement.width <= placementInput.workRight &&
              placement.y + placement.height <= placementInput.workBottom,
          "resolved placement is contained by a negative-origin monitor work area");

    CallbackProbe callbackProbe;
    WidgetRailOverlayPlatformCreateOptions options;
    options.callbackContext = &callbackProbe;
    options.eventAvailable = OnEventAvailable;
    options.diagnostic = OnDiagnostic;
    WidgetRailOverlayPlatformHandle* handle{};
    Check(WidgetRailOverlayPlatformCreate(&options, &handle) ==
              WidgetRailOverlayPlatformStatus::Ok &&
              handle != nullptr,
          "a version-matched caller creates one opaque platform owner");
    Check(WidgetRailOverlayPlatformSetOwnedWindows(handle, 10, 20) ==
              WidgetRailOverlayPlatformStatus::Ok &&
              WidgetRailOverlayPlatformObserveForegroundTarget(
                  handle, 10, WRAIL_OVERLAY_PLATFORM_TRUE) ==
                  WRAIL_OVERLAY_PLATFORM_FALSE &&
              WidgetRailOverlayPlatformObserveForegroundTarget(
                  handle, 30, WRAIL_OVERLAY_PLATFORM_TRUE) ==
                  WRAIL_OVERLAY_PLATFORM_TRUE &&
              WidgetRailOverlayPlatformRememberedForegroundTarget(handle) == 30,
          "foreground targeting rejects owned windows and remembers one external target");
    Check(WidgetRailOverlayPlatformResolveForegroundTarget(
              handle, 10, WRAIL_OVERLAY_PLATFORM_TRUE) == 30 &&
              WidgetRailOverlayPlatformResolveForegroundTarget(
                  handle, 10, WRAIL_OVERLAY_PLATFORM_FALSE) == 10,
          "target resolution uses valid remembered authority and bounded fallback");

    Check(WidgetRailOverlayPlatformInitialize(handle) ==
              WidgetRailOverlayPlatformStatus::Ok,
          "native initialization remains usable with GameInput or its existing fallback");
    Check(callbackProbe.diagnosticSignals.load(std::memory_order_acquire) > 0,
          "the imported DLL invokes the registered diagnostic callback");

    Check(WidgetRailOverlayPlatformSetWindowState(
              handle,
              WRAIL_OVERLAY_PLATFORM_TRUE,
              WRAIL_OVERLAY_PLATFORM_TRUE) == WidgetRailOverlayPlatformStatus::Ok &&
              callbackProbe.eventSignals.load(std::memory_order_acquire) >= 2,
          "visibility and focus changes signal the registered event callback");
    WidgetRailOverlayPlatformControllerFrame visibleFrame;
    Check(WidgetRailOverlayPlatformReadController(
              handle,
              WRAIL_OVERLAY_PLATFORM_TRUE,
              1'000,
              &visibleFrame) == WidgetRailOverlayPlatformStatus::Ok &&
              visibleFrame.readPath != WidgetRailOverlayPlatformReadPath::None,
          "a visible platform lease owns a controller read path");

    Check(WidgetRailOverlayPlatformSetWindowState(
              handle,
              WRAIL_OVERLAY_PLATFORM_FALSE,
              WRAIL_OVERLAY_PLATFORM_FALSE) == WidgetRailOverlayPlatformStatus::Ok,
          "close-animation begin retires the visible platform lease immediately");
    WidgetRailOverlayPlatformControllerFrame closingFrame;
    Check(WidgetRailOverlayPlatformReadController(
              handle,
              WRAIL_OVERLAY_PLATFORM_FALSE,
              1'010,
              &closingFrame) == WidgetRailOverlayPlatformStatus::Ok &&
              closingFrame.readPath == WidgetRailOverlayPlatformReadPath::None,
          "controller ownership is dormant during the closing interval");

    Check(WidgetRailOverlayPlatformSetWindowState(
              handle,
              WRAIL_OVERLAY_PLATFORM_TRUE,
              WRAIL_OVERLAY_PLATFORM_TRUE) == WidgetRailOverlayPlatformStatus::Ok,
          "rapid reopen restores the platform lease while the HWND is still visible");
    WidgetRailOverlayPlatformControllerFrame reopenedFrame;
    Check(WidgetRailOverlayPlatformReadController(
              handle,
              WRAIL_OVERLAY_PLATFORM_TRUE,
              1'011,
              &reopenedFrame) == WidgetRailOverlayPlatformStatus::Ok &&
              reopenedFrame.readPath != WidgetRailOverlayPlatformReadPath::None,
          "rapid reopen returns controller read ownership on the next deterministic sample");

    WidgetRailOverlayPlatformEvent event;
    std::uint32_t hasEvent = WRAIL_OVERLAY_PLATFORM_FALSE;
    Check(WidgetRailOverlayPlatformDrainEvent(handle, 1'020, &event, &hasEvent) ==
              WidgetRailOverlayPlatformStatus::Ok &&
              hasEvent == WRAIL_OVERLAY_PLATFORM_TRUE,
          "the callback signal corresponds to a drainable public-ABI event");

    Check(WidgetRailOverlayPlatformSetWindowState(
              handle,
              WRAIL_OVERLAY_PLATFORM_FALSE,
              WRAIL_OVERLAY_PLATFORM_FALSE) == WidgetRailOverlayPlatformStatus::Ok,
          "the callback-race fixture starts from a closed visible lease");

    {
        std::scoped_lock lock(callbackProbe.holdMutex);
        callbackProbe.holdNextEvent = true;
        callbackProbe.heldEventEntered = false;
        callbackProbe.releaseHeldEvent = false;
    }
    std::atomic_uint32_t producerStatus{
        static_cast<std::uint32_t>(WidgetRailOverlayPlatformStatus::InvalidArgument)};
    std::thread producer([&] {
        producerStatus.store(
            static_cast<std::uint32_t>(WidgetRailOverlayPlatformSetWindowState(
                handle,
                WRAIL_OVERLAY_PLATFORM_TRUE,
                WRAIL_OVERLAY_PLATFORM_TRUE)),
            std::memory_order_release);
    });
    {
        std::unique_lock lock(callbackProbe.holdMutex);
        Check(callbackProbe.holdChanged.wait_for(
                  lock,
                  std::chrono::seconds(2),
                  [&callbackProbe] {
                      return callbackProbe.heldEventEntered;
                  }),
              "the built DLL enters and holds a registered callback");
    }

    std::thread shutdown([&] {
        WidgetRailOverlayPlatformShutdown(handle);
        callbackProbe.shutdownReturned.store(true, std::memory_order_release);
    });
    bool callbackAdmissionClosed = false;
    const auto closeDeadline =
        std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (std::chrono::steady_clock::now() < closeDeadline) {
        if (WidgetRailOverlayPlatformInitialize(handle) ==
            WidgetRailOverlayPlatformStatus::ShutDown) {
            callbackAdmissionClosed = true;
            break;
        }
        std::this_thread::yield();
    }
    Check(callbackAdmissionClosed &&
              !callbackProbe.shutdownReturned.load(std::memory_order_acquire),
          "shutdown atomically closes admission but waits for the held callback");

    std::atomic_bool secondShutdownReturned{};
    std::thread secondShutdown([&] {
        WidgetRailOverlayPlatformShutdown(handle);
        secondShutdownReturned.store(true, std::memory_order_release);
    });
    const auto signalsWhileHeld =
        callbackProbe.eventSignals.load(std::memory_order_acquire);
    Check(WidgetRailOverlayPlatformSetWindowState(
              handle,
              WRAIL_OVERLAY_PLATFORM_TRUE,
              WRAIL_OVERLAY_PLATFORM_TRUE) == WidgetRailOverlayPlatformStatus::ShutDown &&
              callbackProbe.eventSignals.load(std::memory_order_acquire) ==
                  signalsWhileHeld &&
              !secondShutdownReturned.load(std::memory_order_acquire),
          "closed admission rejects callbacks and concurrent shutdown waits for completion");

    {
        std::scoped_lock lock(callbackProbe.holdMutex);
        callbackProbe.releaseHeldEvent = true;
    }
    callbackProbe.holdChanged.notify_all();
    producer.join();
    shutdown.join();
    secondShutdown.join();
    Check(producerStatus.load(std::memory_order_acquire) ==
              static_cast<std::uint32_t>(WidgetRailOverlayPlatformStatus::Ok) &&
              callbackProbe.shutdownReturned.load(std::memory_order_acquire) &&
              secondShutdownReturned.load(std::memory_order_acquire),
          "the accounted callback leaves before both shutdown callers return");

    const auto eventSignalsAfterShutdown =
        callbackProbe.eventSignals.load(std::memory_order_acquire);
    const auto diagnosticSignalsAfterShutdown =
        callbackProbe.diagnosticSignals.load(std::memory_order_acquire);
    WidgetRailOverlayPlatformShutdown(handle);
    Check(WidgetRailOverlayPlatformInitialize(handle) ==
              WidgetRailOverlayPlatformStatus::ShutDown &&
              WidgetRailOverlayPlatformSetWindowState(
                  handle,
                  WRAIL_OVERLAY_PLATFORM_TRUE,
                  WRAIL_OVERLAY_PLATFORM_TRUE) ==
                  WidgetRailOverlayPlatformStatus::ShutDown &&
              WidgetRailOverlayPlatformDrainEvent(
                  handle, 1'030, &event, &hasEvent) ==
                  WidgetRailOverlayPlatformStatus::ShutDown &&
              callbackProbe.eventSignals.load(std::memory_order_acquire) ==
                  eventSignalsAfterShutdown &&
              callbackProbe.diagnosticSignals.load(std::memory_order_acquire) ==
                  diagnosticSignalsAfterShutdown &&
              callbackProbe.postShutdownSignals.load(
                  std::memory_order_acquire) == 0,
          "no callback is admitted or invoked after shutdown returns");
    WidgetRailOverlayPlatformDestroy(handle);
    Check(callbackProbe.eventSignals.load(std::memory_order_acquire) ==
              eventSignalsAfterShutdown &&
              callbackProbe.diagnosticSignals.load(std::memory_order_acquire) ==
                  diagnosticSignalsAfterShutdown &&
              callbackProbe.postShutdownSignals.load(
                  std::memory_order_acquire) == 0,
          "destroy after idempotent shutdown produces no callback or freed-context use");

    std::cout << "OverlayPlatformInteropTests passed (" << checks
              << " checks)\n";
    return EXIT_SUCCESS;
}
