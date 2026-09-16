#include "../../src/OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "../../src/OverlayPlatformInterop/OverlayPlatformPolicy.h"
#include "../../src/OverlayPlatformInterop/ControllerActivitySelection.h"

#include <vector>
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

void ControllerActivitySelectionKeepsHoldsAndReleases() {
    using namespace widgetrail::platform;
    using Path = widgetrail::input::ControllerReadPath;
    ControllerActivitySelection selection;
    std::vector<ControllerActivitySample> devices{
        {{Path::GameInputVisibleLease, 101}, {}, true},
        {{Path::XInputCompatibility, 0}, {}, true},
        {{Path::XInputCompatibility, 2}, {}, true},
        {{Path::DualSenseHid, 1}, {}, true},
        {{Path::GameInputVisibleLease, 102}, {}, true}};
    std::vector<ControllerSource> reads;
    std::uint64_t latestGameInput = 101;
    const auto read = [&](ControllerSource requested) {
        reads.push_back(requested);
        if (requested.path == Path::GameInputVisibleLease && requested.device == 0)
            requested.device = latestGameInput;
        if (requested.path == Path::DualSenseHid && requested.device == 0)
            requested.device = devices[3].source.device;
        for (const auto& device : devices) if (device.source == requested) return device;
        return ControllerActivitySample{requested};
    };
    ControllerFrameTracker tracker;
    tracker.Prime(true, {}, 0);
    std::uint64_t now{};
    const auto poll = [&] {
        reads.clear();
        const auto sample = selection.Poll(read);
        now += 15;
        return tracker.Update(sample.connected, sample.state, now);
    };
    devices[3].state.buttons = XINPUT_GAMEPAD_A;
    auto frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_A && reads.front().path == Path::GameInputVisibleLease &&
        reads.back().path == Path::DualSenseHid, "idle connected GameInput and XInput do not block DualSense activity");
    devices[0].state.buttons = XINPUT_GAMEPAD_B;
    frame = poll();
    Check(reads.size() == 1 && reads.front() == devices[3].source && frame.pressedButtons == 0 &&
        frame.state.buttons == XINPUT_GAMEPAD_A, "higher priority activity cannot steal a held DualSense button");
    devices[3].state = {};
    frame = poll();
    Check(frame.releasedButtons == XINPUT_GAMEPAD_A && frame.pressedButtons == 0,
        "old owner releases before a different backend supplies input");
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_B && reads.size() == 1,
        "backend handoff keeps the very first new button press");
    latestGameInput = 102;
    devices[4].state.buttons = XINPUT_GAMEPAD_X;
    frame = poll();
    Check(reads.size() == 1 && reads.front().device == 101 && frame.state.buttons == XINPUT_GAMEPAD_B,
        "GameInput hold reads its exact device even when another device supplies the latest reading");
    devices[0].connected = false;
    frame = poll();
    Check(!frame.connected && frame.releasedButtons == XINPUT_GAMEPAD_B,
        "disconnect produces a neutral release without mixing in another controller");
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_X, "another GameInput device can act after the disconnect release");
    devices[4].state = {};
    (void)poll();
    devices[2].state.buttons = XINPUT_GAMEPAD_Y;
    devices[3].state.buttons = XINPUT_GAMEPAD_A;
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_Y && reads.back() == devices[2].source,
        "XInput skips idle lower slots and wins over simultaneous HID activity");
    devices[1].state.buttons = XINPUT_GAMEPAD_B;
    frame = poll();
    Check(reads.size() == 1 && reads.front().device == 2 && frame.pressedButtons == 0,
        "a held XInput slot is not replaced by a lower slot");
    devices[2].state = {};
    (void)poll();
    devices[1].state = {};
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_A, "HID is considered after XInput becomes neutral");
    ++devices[3].source.device;
    frame = poll();
    Check(!frame.connected && frame.releasedButtons == XINPUT_GAMEPAD_A,
        "HID reconnection generation cannot silently inherit a previous hold");
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_A, "reconnected HID can supply a fresh action after release");
    selection.Reset();
    tracker.Reset();
    devices[3].state = {};
    devices[4].state = {};
    frame = poll();
    Check(frame.primed && frame.pressedButtons == 0, "hide/reset retires source and frame ownership");

    devices[4].state.buttons = XINPUT_GAMEPAD_A;
    devices[1].state.buttons = XINPUT_GAMEPAD_A; // Same physical pad exposed by both APIs.
    frame = poll();
    Check(frame.pressedButtons == XINPUT_GAMEPAD_A && reads.size() == 1,
        "GameInput wins over a duplicate XInput report");
    frame = poll();
    Check(frame.pressedButtons == 0, "mirrored backend cannot replay a held press");
    devices[4].state = {};
    devices[1].state = {};
    (void)poll();
    frame = poll();
    Check(frame.pressedButtons == 0, "mirrored release does not become a second action");
}

void ControllerActivityThresholdsAndRepeats() {
    using namespace widgetrail::platform;
    using Path = widgetrail::input::ControllerReadPath;
    ControllerActivitySelection selection;
    WidgetRailOverlayPlatformRawControllerState state{};
    state.leftThumbX = 7'849; state.rightThumbY = -8'000; state.leftTrigger = 29;
    Check(!selection.HasActivity(state), "stick drift and subthreshold triggers do not claim input");
    state.rightThumbY = -8'001;
    Check(selection.HasActivity(state), "right stick scrolling can claim input without buttons");
    selection.SetOptions({12'000, 4'000, 30});
    state = {}; state.leftThumbX = 11'000;
    Check(!selection.HasActivity(state), "left deadzone is independently configurable");
    state.rightThumbX = 4'001;
    Check(selection.HasActivity(state), "right deadzone is independently configurable");
    state = {}; state.leftThumbY = -32'768;
    Check(selection.HasActivity(state), "negative full-scale axes do not overflow");
    state = {}; state.rightTrigger = 30;
    Check(selection.HasActivity(state), "trigger threshold matches trigger action detection");

    ControllerFrameTracker tracker;
    tracker.Prime(true, {}, 0);
    state = {}; state.leftThumbX = 20'000;
    const auto read = [&](ControllerSource source) {
        if (source.path == Path::GameInputVisibleLease)
            return ControllerActivitySample{{source.path, 100}, state, true};
        return ControllerActivitySample{source};
    };
    auto sample = selection.Poll(read);
    auto frame = tracker.Update(sample.connected, sample.state, 15);
    Check(frame.stickNavigation.direction == WidgetRailOverlayPlatformNavigationDirection::Right,
        "selected stick starts navigation immediately");
    sample = selection.Poll(read);
    frame = tracker.Update(sample.connected, sample.state, 400);
    Check(frame.stickNavigation.phase == WidgetRailOverlayPlatformNavigationPhase::Repeated &&
        frame.stickNavigation.direction == WidgetRailOverlayPlatformNavigationDirection::Right,
        "owner selection preserves stick repeat cadence");
    state = {};
    sample = selection.Poll(read);
    frame = tracker.Update(sample.connected, sample.state, 800);
    Check(frame.stickNavigation.direction == WidgetRailOverlayPlatformNavigationDirection::None,
        "neutral release retires stick repeats");
}

void QueuedNavigationCannotRepeatAfterRenderingStall() {
    using Direction = WidgetRailOverlayPlatformNavigationDirection;
    using Phase = WidgetRailOverlayPlatformNavigationPhase;
    widgetrail::platform::ControllerFrameTracker tracker;
    tracker.Prime(true, {}, 0);
    WidgetRailOverlayPlatformRawControllerState tilted;
    tilted.leftThumbX = 20'000;
    auto frame = tracker.Update(true, tilted, 10, false);
    Check(frame.stickNavigation.direction == Direction::Right &&
              frame.stickNavigation.phase == Phase::Pressed,
          "queued flick keeps its initial direction edge");
    tilted.leftThumbX = 21'000;
    frame = tracker.Update(true, tilted, 507, false);
    Check(frame.stickNavigation.direction == Direction::None,
          "497 ms rendering delay cannot turn queued tilt into a hold repeat");
    frame = tracker.Update(true, {}, 507, false);
    Check(frame.stickNavigation.direction == Direction::None,
          "queued neutral retires the flick without another move");
    frame = tracker.Update(true, {}, 508);
    Check(frame.stickNavigation.direction == Direction::None,
          "current neutral remains quiet after backlog drain");
    frame = tracker.Update(true, tilted, 520, false);
    Check(frame.stickNavigation.phase == Phase::Pressed &&
              frame.stickNavigation.direction == Direction::Right,
          "a fresh flick after release still navigates");
    frame = tracker.Update(true, tilted, 1'000, false);
    Check(frame.stickNavigation.direction == Direction::None,
          "backlog drain does not advance the held-repeat deadline");
    frame = tracker.Update(true, tilted, 1'000);
    Check(frame.stickNavigation.direction == Direction::Right &&
              frame.stickNavigation.phase == Phase::Repeated,
          "current physically held stick can repeat after history drains");
    Check(tracker.Update(true, tilted, 1'124).stickNavigation.direction == Direction::None &&
              tracker.Update(true, tilted, 1'125).stickNavigation.phase == Phase::Repeated,
          "held stick retains its ordinary 125 ms repeat cadence");
    tracker.Reset();
    tracker.Prime(true, {}, 0);
    WidgetRailOverlayPlatformRawControllerState button;
    button.buttons = XINPUT_GAMEPAD_DPAD_RIGHT | XINPUT_GAMEPAD_A;
    frame = tracker.Update(true, button, 10, false);
    Check(frame.pressedButtons == button.buttons && frame.dpadNavigation.direction == Direction::Right,
          "queued button and D-pad presses are preserved");
    frame = tracker.Update(true, button, 507, false);
    Check(frame.pressedButtons == 0 && frame.dpadNavigation.direction == Direction::None,
          "queued held D-pad cannot synthesize a repeat");
    frame = tracker.Update(true, {}, 507, false);
    Check(frame.releasedButtons == button.buttons,
          "queued short button and D-pad releases are preserved");
    tracker.Prime(true, tilted, 1'500);
    frame = tracker.Update(true, tilted, 1'501);
    Check(frame.stickNavigation.direction == Direction::None,
          "reopening with a held stick still requires its primed repeat delay");
}

} // namespace

int main() {
    ControllerActivitySelectionKeepsHoldsAndReleases();
    ControllerActivityThresholdsAndRepeats();
    QueuedNavigationCannotRepeatAfterRenderingStall();
    Check(WidgetRailOverlayPlatformGetAbiVersion() ==
              WRAIL_OVERLAY_PLATFORM_ABI_VERSION,
          "the exported ABI reports the version compiled into the caller");
    const HMODULE importedModule =
        GetModuleHandleW(L"OverlayPlatformInterop.dll");
    Check(importedModule != nullptr &&
              GetProcAddress(importedModule,
                  "WidgetRailOverlayPlatformGetAbiVersion") != nullptr,
          "the focused executable imports the built public DLL ABI");
    Check(GetProcAddress(importedModule, "WidgetRailOverlayPlatformControllerPrerequisites") != nullptr &&
          GetProcAddress(importedModule, "WidgetRailOverlayPlatformControllerControlState") != nullptr &&
          GetProcAddress(importedModule, "WidgetRailOverlayPlatformSetExclusiveControl") != nullptr,
          "controller settings operations are exported by the production DLL");
    Check(WidgetRailOverlayPlatformControllerControlState(nullptr) == 0 &&
          WidgetRailOverlayPlatformSetExclusiveControl(nullptr, 1) == WidgetRailOverlayPlatformStatus::InvalidArgument,
          "controller settings reject missing owner without driver mutation");
    std::uint16_t nativeButtons = 0xFFFF;
    Check(GetProcAddress(importedModule, "WidgetRailOverlayPlatformNativeShortcutButtons") != nullptr &&
          WidgetRailOverlayPlatformNativeShortcutButtons(nullptr, &nativeButtons) == WRAIL_OVERLAY_PLATFORM_FALSE &&
          nativeButtons == 0 && WidgetRailOverlayPlatformNativeShortcutButtons(nullptr, nullptr) == WRAIL_OVERLAY_PLATFORM_FALSE,
          "native shortcuts are exported and invalid reads clear output safely");
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
              overlay.surface() == widgetrail::Surface::Widget && overlay.activeWidget() == L"widget",
          "the first Guide edge presents the selected widget");
    Check(!guide.Accept(1'149) &&
              overlay.surface() == widgetrail::Surface::Widget && overlay.activeWidget() == L"widget",
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
