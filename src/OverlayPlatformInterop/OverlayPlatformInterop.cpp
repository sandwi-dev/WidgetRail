#include "OverlayPlatformInterop.h"

#include "OverlayPlatformPolicy.h"
#include "../OverlayHost/ControllerInputOwnership.h"
#include "../OverlayHost/GuideInputCompatibility.h"
#include "../OverlayHost/OverlayPlacement.h"
#include "../OverlayHost/OverlayTargeting.h"

#include <Windows.h>
#include <GameInput.h>
#include <Xinput.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <condition_variable>
#include <deque>
#include <memory>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <utility>

using Microsoft::WRL::ComPtr;
using namespace GameInput::v3;

namespace {

enum class RawEventKind {
    GuidePressed,
    LegacyDeviceChanged,
    VisibilityChanged,
    FocusChanged,
};

struct RawEvent final {
    RawEventKind kind{};
    GbaOverlayPlatformGuideSource guideSource{
        GbaOverlayPlatformGuideSource::None};
    gba::input::GuideCompatibilityActivation::DeviceId deviceId{};
    bool value{};
};

constexpr std::uint32_t ToAbiBoolean(const bool value) noexcept {
    return value
        ? GBA_OVERLAY_PLATFORM_TRUE
        : GBA_OVERLAY_PLATFORM_FALSE;
}

[[nodiscard]] GbaOverlayPlatformReadPath ConvertReadPath(
    const gba::input::ControllerReadPath path) noexcept {
    switch (path) {
    case gba::input::ControllerReadPath::GameInputVisibleLease:
        return GbaOverlayPlatformReadPath::GameInputVisibleLease;
    case gba::input::ControllerReadPath::XInputCompatibility:
        return GbaOverlayPlatformReadPath::XInputCompatibility;
    case gba::input::ControllerReadPath::None:
    default:
        return GbaOverlayPlatformReadPath::None;
    }
}

[[nodiscard]] bool ValidOutput(
    const GbaOverlayPlatformEvent* event) noexcept {
    return event && event->structSize >= sizeof(GbaOverlayPlatformEvent) &&
           event->abiVersion == GBA_OVERLAY_PLATFORM_ABI_VERSION;
}

[[nodiscard]] bool ValidOutput(
    const GbaOverlayPlatformControllerFrame* frame) noexcept {
    return frame &&
           frame->structSize >= sizeof(GbaOverlayPlatformControllerFrame) &&
           frame->abiVersion == GBA_OVERLAY_PLATFORM_ABI_VERSION;
}

void PublishEvent(
    GbaOverlayPlatformEvent& event,
    const GbaOverlayPlatformEventKind kind,
    const std::uint64_t nowMilliseconds,
    const std::uint32_t value = 0,
    const GbaOverlayPlatformGuideSource source =
        GbaOverlayPlatformGuideSource::None) noexcept {
    event = {};
    event.kind = kind;
    event.guideSource = source;
    event.timestampMilliseconds = nowMilliseconds;
    event.value = value;
}

} // namespace

struct GbaOverlayPlatformHandle final {
    enum class CallbackGateState {
        Open,
        Closing,
        Closed,
    };

    explicit GbaOverlayPlatformHandle(
        const GbaOverlayPlatformCreateOptions& createOptions) noexcept
        : options(createOptions) {}

    GbaOverlayPlatformCreateOptions options{};
    mutable std::mutex mutex;
    mutable std::mutex callbackMutex;
    std::condition_variable callbackDrain;
    std::uint32_t callbacksInFlight{};
    CallbackGateState callbackGateState{CallbackGateState::Open};
    std::deque<RawEvent> events;
    bool initialized{};
    std::atomic_bool shutDown{};
    bool visible{};
    bool focused{};
    bool compatibilityDeviceTrackingAvailable{};
    ComPtr<IGameInput> gameInput;
    GameInputCallbackToken guideCallback{};
    GameInputCallbackToken compatibilityDeviceCallback{};
    gba::input::XInputGuideCompatibility guideCompatibility;
    gba::input::GuideCompatibilityActivation guideCompatibilityActivation;
    gba::platform::GuideToggleDebouncer guideDebouncer;
    gba::platform::ControllerFrameTracker controllerTracker;
    gba::ForegroundTargetTracker foregroundTarget;
    std::optional<gba::input::ControllerReadPath> lastReadPath;
    std::optional<bool> lastForegroundExclusive;

    void Diagnostic(const std::wstring& message) noexcept {
        if (!options.diagnostic || !TryEnterCallback()) return;
        options.diagnostic(options.callbackContext, message.c_str());
        LeaveCallback();
    }

    void Signal() noexcept {
        if (!options.eventAvailable || !TryEnterCallback()) return;
        options.eventAvailable(options.callbackContext);
        LeaveCallback();
    }

    void Queue(RawEvent event) noexcept {
        bool queued = false;
        {
            std::scoped_lock lock(mutex);
            if (!shutDown) {
                events.push_back(std::move(event));
                queued = true;
            }
        }
        if (queued) Signal();
    }

    [[nodiscard]] bool TryEnterCallback() noexcept {
        std::scoped_lock lock(callbackMutex);
        if (callbackGateState != CallbackGateState::Open) return false;
        ++callbacksInFlight;
        return true;
    }

    void LeaveCallback() noexcept {
        std::scoped_lock lock(callbackMutex);
        if (--callbacksInFlight == 0) {
            callbackDrain.notify_all();
        }
    }

    [[nodiscard]] bool BeginShutdown() noexcept {
        std::unique_lock lock(callbackMutex);
        if (callbackGateState != CallbackGateState::Open) {
            callbackDrain.wait(lock, [this] {
                return callbackGateState == CallbackGateState::Closed;
            });
            return false;
        }
        callbackGateState = CallbackGateState::Closing;
        shutDown.store(true, std::memory_order_release);
        return true;
    }

    void WaitForCallbacks() noexcept {
        std::unique_lock lock(callbackMutex);
        callbackDrain.wait(lock, [this] {
            return callbacksInFlight == 0;
        });
    }

    void FinishShutdown() noexcept {
        std::scoped_lock lock(callbackMutex);
        callbackGateState = CallbackGateState::Closed;
        callbackDrain.notify_all();
    }

    [[nodiscard]] bool RequiresLegacyPolling() const noexcept {
        std::scoped_lock lock(mutex);
        return guideCompatibility.available() &&
            (!compatibilityDeviceTrackingAvailable ||
             guideCompatibilityActivation.active());
    }

    [[nodiscard]] bool TryReadRaw(
        const bool foregroundConfirmed,
        GbaOverlayPlatformRawControllerState& state,
        bool& connected,
        gba::input::ControllerInputOwnershipDecision& decision) noexcept {
        decision = gba::input::DecideControllerInputOwnership(
            visible, visible, foregroundConfirmed, gameInput.Get() != nullptr);
        if (!lastReadPath || *lastReadPath != decision.readPath ||
            !lastForegroundExclusive ||
            *lastForegroundExclusive != decision.foregroundExclusive) {
            lastReadPath = decision.readPath;
            lastForegroundExclusive = decision.foregroundExclusive;
            switch (decision.readPath) {
            case gba::input::ControllerReadPath::GameInputVisibleLease:
                Diagnostic(decision.foregroundExclusive
                    ? L"Controller read path: visible GameInput lease, foreground-exclusive"
                    : L"Controller read path: visible GameInput lease, background-shared");
                break;
            case gba::input::ControllerReadPath::XInputCompatibility:
                Diagnostic(L"Controller read path: XInput compatibility (not exclusive)");
                break;
            case gba::input::ControllerReadPath::None:
                Diagnostic(L"Controller read path dormant: visible lease is inactive");
                break;
            }
        }

        state = {};
        connected = false;
        if (decision.readPath ==
            gba::input::ControllerReadPath::GameInputVisibleLease) {
            ComPtr<IGameInputReading> reading;
            const HRESULT result = gameInput->GetCurrentReading(
                GameInputKindGamepad, nullptr, reading.ReleaseAndGetAddressOf());
            if (FAILED(result) || !reading) return false;
            GameInputGamepadState input{};
            if (!reading->GetGamepadState(&input)) return false;
            const auto has = [buttons = input.buttons](
                                 const GameInputGamepadButtons button) {
                return (static_cast<unsigned>(buttons) &
                        static_cast<unsigned>(button)) != 0;
            };
            if (has(GameInputGamepadDPadUp))
                state.buttons |= XINPUT_GAMEPAD_DPAD_UP;
            if (has(GameInputGamepadDPadDown))
                state.buttons |= XINPUT_GAMEPAD_DPAD_DOWN;
            if (has(GameInputGamepadDPadLeft))
                state.buttons |= XINPUT_GAMEPAD_DPAD_LEFT;
            if (has(GameInputGamepadDPadRight))
                state.buttons |= XINPUT_GAMEPAD_DPAD_RIGHT;
            if (has(GameInputGamepadMenu)) state.buttons |= XINPUT_GAMEPAD_START;
            if (has(GameInputGamepadView)) state.buttons |= XINPUT_GAMEPAD_BACK;
            if (has(GameInputGamepadLeftThumbstick))
                state.buttons |= XINPUT_GAMEPAD_LEFT_THUMB;
            if (has(GameInputGamepadRightThumbstick))
                state.buttons |= XINPUT_GAMEPAD_RIGHT_THUMB;
            if (has(GameInputGamepadLeftShoulder))
                state.buttons |= XINPUT_GAMEPAD_LEFT_SHOULDER;
            if (has(GameInputGamepadRightShoulder))
                state.buttons |= XINPUT_GAMEPAD_RIGHT_SHOULDER;
            if (has(GameInputGamepadA)) state.buttons |= XINPUT_GAMEPAD_A;
            if (has(GameInputGamepadB)) state.buttons |= XINPUT_GAMEPAD_B;
            if (has(GameInputGamepadX)) state.buttons |= XINPUT_GAMEPAD_X;
            if (has(GameInputGamepadY)) state.buttons |= XINPUT_GAMEPAD_Y;
            const auto thumb = [](const float value) {
                const float clamped = std::clamp(value, -1.0F, 1.0F);
                const float scale = clamped < 0.0F ? 32768.0F : 32767.0F;
                return static_cast<std::int16_t>(std::lround(clamped * scale));
            };
            const auto trigger = [](const float value) {
                return static_cast<std::uint8_t>(std::lround(
                    std::clamp(value, 0.0F, 1.0F) * 255.0F));
            };
            state.leftTrigger = trigger(input.leftTrigger);
            state.rightTrigger = trigger(input.rightTrigger);
            state.leftThumbX = thumb(input.leftThumbstickX);
            state.leftThumbY = thumb(input.leftThumbstickY);
            state.rightThumbX = thumb(input.rightThumbstickX);
            state.rightThumbY = thumb(input.rightThumbstickY);
            connected = true;
            return true;
        }

        if (decision.readPath ==
            gba::input::ControllerReadPath::XInputCompatibility) {
            for (DWORD index = 0; index < XUSER_MAX_COUNT; ++index) {
                XINPUT_STATE input{};
                if (XInputGetState(index, &input) != ERROR_SUCCESS) continue;
                state.buttons = input.Gamepad.wButtons;
                state.leftTrigger = input.Gamepad.bLeftTrigger;
                state.rightTrigger = input.Gamepad.bRightTrigger;
                state.leftThumbX = input.Gamepad.sThumbLX;
                state.leftThumbY = input.Gamepad.sThumbLY;
                state.rightThumbX = input.Gamepad.sThumbRX;
                state.rightThumbY = input.Gamepad.sThumbRY;
                connected = true;
                return true;
            }
        }
        return false;
    }
};

namespace {

class CallbackLease final {
public:
    explicit CallbackLease(GbaOverlayPlatformHandle* handle) noexcept
        : handle_(handle && handle->TryEnterCallback() ? handle : nullptr) {}

    ~CallbackLease() {
        if (handle_) handle_->LeaveCallback();
    }

    [[nodiscard]] explicit operator bool() const noexcept {
        return handle_ != nullptr;
    }

private:
    GbaOverlayPlatformHandle* handle_{};
};

void CALLBACK OnSystemButton(
    GameInputCallbackToken,
    void* context,
    IGameInputDevice*,
    std::uint64_t,
    GameInputSystemButtons current,
    GameInputSystemButtons previous) {
    auto* handle = static_cast<GbaOverlayPlatformHandle*>(context);
    CallbackLease callbackLease(handle);
    if (!callbackLease) return;
    const auto pressed = [](const GameInputSystemButtons buttons) {
        return (static_cast<unsigned>(buttons) &
                static_cast<unsigned>(GameInputSystemButtonGuide)) != 0;
    };
    if (pressed(current) && !pressed(previous)) {
        handle->Diagnostic(L"GameInput Guide press callback received");
        handle->Queue(RawEvent{
            RawEventKind::GuidePressed,
            GbaOverlayPlatformGuideSource::GameInput});
    }
}

void CALLBACK OnGameInputDevice(
    GameInputCallbackToken,
    void* context,
    IGameInputDevice* device,
    std::uint64_t,
    GameInputDeviceStatus current,
    GameInputDeviceStatus previous) {
    auto* handle = static_cast<GbaOverlayPlatformHandle*>(context);
    CallbackLease callbackLease(handle);
    if (!callbackLease) return;
    const bool connected =
        (current & GameInputDeviceConnected) != GameInputDeviceNoStatus;
    const bool wasConnected =
        (previous & GameInputDeviceConnected) != GameInputDeviceNoStatus;
    if (connected == wasConnected || !device) return;

    const GameInputDeviceInfo* info{};
    if (FAILED(device->GetDeviceInfo(&info)) || !info ||
        info->deviceFamily != GameInputFamilyXbox360) {
        return;
    }
    RawEvent event;
    event.kind = RawEventKind::LegacyDeviceChanged;
    event.value = connected;
    std::copy(
        std::begin(info->deviceId.value),
        std::end(info->deviceId.value),
        event.deviceId.begin());
    handle->Queue(std::move(event));
}

[[nodiscard]] GbaOverlayPlatformStatus ValidateHandle(
    const GbaOverlayPlatformHandle* handle) noexcept {
    if (!handle) return GbaOverlayPlatformStatus::InvalidArgument;
    if (handle->shutDown) return GbaOverlayPlatformStatus::ShutDown;
    if (!handle->initialized) return GbaOverlayPlatformStatus::NotInitialized;
    return GbaOverlayPlatformStatus::Ok;
}

} // namespace

extern "C" {

std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformGetAbiVersion() noexcept {
    return GBA_OVERLAY_PLATFORM_ABI_VERSION;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL GbaOverlayPlatformCreate(
    const GbaOverlayPlatformCreateOptions* options,
    GbaOverlayPlatformHandle** handle) noexcept {
    if (!options || !handle ||
        options->structSize < sizeof(GbaOverlayPlatformCreateOptions)) {
        return GbaOverlayPlatformStatus::InvalidArgument;
    }
    if (options->abiVersion != GBA_OVERLAY_PLATFORM_ABI_VERSION) {
        return GbaOverlayPlatformStatus::InvalidVersion;
    }
    *handle = nullptr;
    *handle = new (std::nothrow) GbaOverlayPlatformHandle(*options);
    if (!*handle) return GbaOverlayPlatformStatus::AllocationFailed;
    return GbaOverlayPlatformStatus::Ok;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformInitialize(GbaOverlayPlatformHandle* handle) noexcept {
    if (!handle) return GbaOverlayPlatformStatus::InvalidArgument;
    if (handle->shutDown) return GbaOverlayPlatformStatus::ShutDown;
    if (handle->initialized) return GbaOverlayPlatformStatus::Ok;

    const HRESULT createResult = GameInputCreate(
        handle->gameInput.ReleaseAndGetAddressOf());
    if (FAILED(createResult)) {
        handle->Diagnostic(
            L"GameInputCreate failed HRESULT=" +
            std::to_wstring(static_cast<unsigned long>(createResult)));
    } else {
        handle->gameInput->SetFocusPolicy(static_cast<GameInputFocusPolicy>(
            GameInputExclusiveForegroundInput |
            GameInputEnableBackgroundInput |
            GameInputEnableBackgroundGuideButton |
            GameInputExclusiveForegroundGuideButton));
        const HRESULT guideResult =
            handle->gameInput->RegisterSystemButtonCallback(
                nullptr,
                GameInputSystemButtonGuide,
                handle,
                OnSystemButton,
                &handle->guideCallback);
        if (FAILED(guideResult)) {
            handle->guideCallback = 0;
            handle->Diagnostic(
                L"RegisterSystemButtonCallback failed HRESULT=" +
                std::to_wstring(static_cast<unsigned long>(guideResult)));
            handle->Diagnostic(
                L"GameInput remains available for the visible ordinary-input lease; "
                L"Guide requires the compatibility path");
        } else {
            handle->Diagnostic(
                L"GameInput configured for a visible background-read lease plus "
                L"foreground-exclusive Guide and ordinary controls");
            const HRESULT deviceResult =
                handle->gameInput->RegisterDeviceCallback(
                    nullptr,
                    GameInputKindGamepad,
                    GameInputDeviceConnected,
                    GameInputAsyncEnumeration,
                    handle,
                    OnGameInputDevice,
                    &handle->compatibilityDeviceCallback);
            if (FAILED(deviceResult)) {
                handle->compatibilityDeviceCallback = 0;
                handle->Diagnostic(
                    L"GameInput legacy-device tracking failed HRESULT=" +
                    std::to_wstring(static_cast<unsigned long>(deviceResult)));
            } else {
                handle->compatibilityDeviceTrackingAvailable = true;
                handle->Diagnostic(
                    L"GameInput legacy-device tracking registered; hidden Guide polling remains dormant unless required");
            }
        }
        handle->Diagnostic(
            L"Controller exclusivity covers other GameInput clients only; XInput, Raw Input, "
            L"HID, and remapping drivers may still receive the same physical input");
    }

    if (handle->guideCompatibility.Initialize()) {
        handle->Diagnostic(handle->compatibilityDeviceTrackingAvailable
            ? L"XInput Guide compatibility adapter standing by for legacy devices"
            : L"XInput Guide compatibility adapter active because GameInput device tracking is unavailable");
    } else {
        handle->Diagnostic(L"XInput Guide compatibility adapter unavailable");
    }
    handle->initialized = true;
    return GbaOverlayPlatformStatus::Ok;
}

void GBA_OVERLAY_PLATFORM_CALL GbaOverlayPlatformShutdown(
    GbaOverlayPlatformHandle* handle) noexcept {
    if (!handle || !handle->BeginShutdown()) return;
    if (handle->gameInput && handle->guideCallback != 0) {
        handle->gameInput->StopCallback(handle->guideCallback);
        handle->gameInput->UnregisterCallback(handle->guideCallback);
        handle->guideCallback = 0;
    }
    if (handle->gameInput && handle->compatibilityDeviceCallback != 0) {
        handle->gameInput->StopCallback(handle->compatibilityDeviceCallback);
        handle->gameInput->UnregisterCallback(
            handle->compatibilityDeviceCallback);
        handle->compatibilityDeviceCallback = 0;
    }
    handle->WaitForCallbacks();
    handle->guideCompatibility.Shutdown();
    handle->gameInput.Reset();
    handle->controllerTracker.Reset();
    handle->guideDebouncer.Reset();
    {
        std::scoped_lock lock(handle->mutex);
        handle->events.clear();
        handle->guideCompatibilityActivation.Reset();
        handle->compatibilityDeviceTrackingAvailable = false;
    }
    handle->initialized = false;
    handle->FinishShutdown();
}

void GBA_OVERLAY_PLATFORM_CALL GbaOverlayPlatformDestroy(
    GbaOverlayPlatformHandle* handle) noexcept {
    if (!handle) return;
    GbaOverlayPlatformShutdown(handle);
    delete handle;
}

std::uint32_t GBA_OVERLAY_PLATFORM_CALL GbaOverlayPlatformHasGameInput(
    const GbaOverlayPlatformHandle* handle) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown && handle->gameInput);
}

std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRequiresLegacyGuidePolling(
    const GbaOverlayPlatformHandle* handle) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown && handle->RequiresLegacyPolling());
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetWindowState(
    GbaOverlayPlatformHandle* handle,
    const std::uint32_t visibleValue,
    const std::uint32_t focusedValue) noexcept {
    const auto status = ValidateHandle(handle);
    if (status != GbaOverlayPlatformStatus::Ok) return status;
    const bool visible = visibleValue != GBA_OVERLAY_PLATFORM_FALSE;
    const bool focused = focusedValue != GBA_OVERLAY_PLATFORM_FALSE;
    if (handle->visible != visible) {
        handle->visible = visible;
        RawEvent event;
        event.kind = RawEventKind::VisibilityChanged;
        event.value = visible;
        handle->Queue(std::move(event));
    }
    if (handle->focused != focused) {
        handle->focused = focused;
        RawEvent event;
        event.kind = RawEventKind::FocusChanged;
        event.value = focused;
        handle->Queue(std::move(event));
    }
    if (!visible || !focused) handle->controllerTracker.Reset();
    return GbaOverlayPlatformStatus::Ok;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformDrainEvent(
    GbaOverlayPlatformHandle* handle,
    const std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept {
    if (!hasEvent || !ValidOutput(event)) {
        return GbaOverlayPlatformStatus::InvalidArgument;
    }
    *hasEvent = GBA_OVERLAY_PLATFORM_FALSE;
    const auto status = ValidateHandle(handle);
    if (status != GbaOverlayPlatformStatus::Ok) return status;

    for (;;) {
        RawEvent raw;
        {
            std::scoped_lock lock(handle->mutex);
            if (handle->events.empty()) return GbaOverlayPlatformStatus::Ok;
            raw = std::move(handle->events.front());
            handle->events.pop_front();
        }
        switch (raw.kind) {
        case RawEventKind::GuidePressed:
            if (!handle->guideDebouncer.Accept(nowMilliseconds)) continue;
            PublishEvent(
                *event,
                GbaOverlayPlatformEventKind::GuideToggleRequested,
                nowMilliseconds,
                0,
                raw.guideSource);
            *hasEvent = GBA_OVERLAY_PLATFORM_TRUE;
            return GbaOverlayPlatformStatus::Ok;
        case RawEventKind::LegacyDeviceChanged: {
            bool changed = false;
            bool active = false;
            {
                std::scoped_lock lock(handle->mutex);
                changed = handle->guideCompatibilityActivation.Update(
                    raw.deviceId, raw.value);
                active = handle->guideCompatibilityActivation.active();
            }
            if (!changed) continue;
            PublishEvent(
                *event,
                GbaOverlayPlatformEventKind::LegacyGuidePollingChanged,
                nowMilliseconds,
                active ? 1U : 0U);
            *hasEvent = GBA_OVERLAY_PLATFORM_TRUE;
            return GbaOverlayPlatformStatus::Ok;
        }
        case RawEventKind::VisibilityChanged:
            PublishEvent(
                *event,
                GbaOverlayPlatformEventKind::VisibilityChanged,
                nowMilliseconds,
                raw.value ? 1U : 0U);
            *hasEvent = GBA_OVERLAY_PLATFORM_TRUE;
            return GbaOverlayPlatformStatus::Ok;
        case RawEventKind::FocusChanged:
            PublishEvent(
                *event,
                GbaOverlayPlatformEventKind::FocusChanged,
                nowMilliseconds,
                raw.value ? 1U : 0U);
            *hasEvent = GBA_OVERLAY_PLATFORM_TRUE;
            return GbaOverlayPlatformStatus::Ok;
        }
    }
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPollLegacyGuide(
    GbaOverlayPlatformHandle* handle,
    const std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept {
    if (!hasEvent || !ValidOutput(event)) {
        return GbaOverlayPlatformStatus::InvalidArgument;
    }
    *hasEvent = GBA_OVERLAY_PLATFORM_FALSE;
    const auto status = ValidateHandle(handle);
    if (status != GbaOverlayPlatformStatus::Ok) return status;
    if (!handle->RequiresLegacyPolling()) return GbaOverlayPlatformStatus::Ok;
    const auto slots = handle->guideCompatibility.PollRisingEdges();
    if (slots == 0 || !handle->guideDebouncer.Accept(nowMilliseconds)) {
        return GbaOverlayPlatformStatus::Ok;
    }
    PublishEvent(
        *event,
        GbaOverlayPlatformEventKind::GuideToggleRequested,
        nowMilliseconds,
        slots,
        GbaOverlayPlatformGuideSource::LegacyCompatibility);
    *hasEvent = GBA_OVERLAY_PLATFORM_TRUE;
    return GbaOverlayPlatformStatus::Ok;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPrimeController(
    GbaOverlayPlatformHandle* handle,
    const std::uint32_t foregroundConfirmed,
    const std::uint64_t nowMilliseconds) noexcept {
    const auto status = ValidateHandle(handle);
    if (status != GbaOverlayPlatformStatus::Ok) return status;
    GbaOverlayPlatformRawControllerState state;
    bool connected = false;
    gba::input::ControllerInputOwnershipDecision decision;
    (void)handle->TryReadRaw(
        foregroundConfirmed != GBA_OVERLAY_PLATFORM_FALSE,
        state,
        connected,
        decision);
    handle->controllerTracker.Prime(
        connected, state, nowMilliseconds);
    return GbaOverlayPlatformStatus::Ok;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformReadController(
    GbaOverlayPlatformHandle* handle,
    const std::uint32_t foregroundConfirmed,
    const std::uint64_t nowMilliseconds,
    GbaOverlayPlatformControllerFrame* frame) noexcept {
    if (!ValidOutput(frame)) {
        return GbaOverlayPlatformStatus::InvalidArgument;
    }
    const auto status = ValidateHandle(handle);
    if (status != GbaOverlayPlatformStatus::Ok) return status;
    GbaOverlayPlatformRawControllerState state;
    bool connected = false;
    gba::input::ControllerInputOwnershipDecision decision;
    (void)handle->TryReadRaw(
        foregroundConfirmed != GBA_OVERLAY_PLATFORM_FALSE,
        state,
        connected,
        decision);
    const auto structSize = frame->structSize;
    const auto abiVersion = frame->abiVersion;
    *frame = handle->controllerTracker.Update(
        connected, state, nowMilliseconds);
    frame->structSize = structSize;
    frame->abiVersion = abiVersion;
    frame->foregroundExclusive = ToAbiBoolean(
        decision.foregroundExclusive);
    frame->readPath = ConvertReadPath(decision.readPath);
    return GbaOverlayPlatformStatus::Ok;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetOwnedWindows(
    GbaOverlayPlatformHandle* handle,
    const std::uintptr_t overlay,
    const std::uintptr_t backdrop) noexcept {
    if (!handle || handle->shutDown) {
        return handle
            ? GbaOverlayPlatformStatus::ShutDown
            : GbaOverlayPlatformStatus::InvalidArgument;
    }
    handle->foregroundTarget.SetOwnedWindows(overlay, backdrop);
    return GbaOverlayPlatformStatus::Ok;
}

std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformObserveForegroundTarget(
    GbaOverlayPlatformHandle* handle,
    const std::uintptr_t candidate,
    const std::uint32_t candidateIsValid) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown &&
        handle->foregroundTarget.Observe(
            candidate,
            candidateIsValid != GBA_OVERLAY_PLATFORM_FALSE));
}

std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRememberedForegroundTarget(
    const GbaOverlayPlatformHandle* handle) noexcept {
    return handle && !handle->shutDown
        ? handle->foregroundTarget.remembered()
        : 0;
}

std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformResolveForegroundTarget(
    const GbaOverlayPlatformHandle* handle,
    const std::uintptr_t fallback,
    const std::uint32_t rememberedTargetIsValid) noexcept {
    return handle && !handle->shutDown
        ? handle->foregroundTarget.Resolve(
              fallback,
              rememberedTargetIsValid != GBA_OVERLAY_PLATFORM_FALSE)
        : fallback;
}

GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformComputePlacement(
    const GbaOverlayPlatformPlacementInput* input,
    GbaOverlayPlatformPlacement* placement,
    std::uint32_t* hasPlacement) noexcept {
    if (!input || !placement || !hasPlacement ||
        input->structSize < sizeof(GbaOverlayPlatformPlacementInput) ||
        placement->structSize < sizeof(GbaOverlayPlatformPlacement)) {
        return GbaOverlayPlatformStatus::InvalidArgument;
    }
    if (input->abiVersion != GBA_OVERLAY_PLATFORM_ABI_VERSION ||
        placement->abiVersion != GBA_OVERLAY_PLATFORM_ABI_VERSION) {
        return GbaOverlayPlatformStatus::InvalidVersion;
    }
    *hasPlacement = GBA_OVERLAY_PLATFORM_FALSE;
    const auto resolved = gba::ComputeOverlayPlacement(
        {input->workLeft, input->workTop, input->workRight, input->workBottom},
        input->dpi,
        input->desiredWidthDip,
        input->desiredHeightDip,
        {input->sideMarginDip, input->topMarginDip, input->bottomMarginDip});
    if (!resolved) return GbaOverlayPlatformStatus::Ok;
    placement->x = resolved->x;
    placement->y = resolved->y;
    placement->width = resolved->width;
    placement->height = resolved->height;
    *hasPlacement = GBA_OVERLAY_PLATFORM_TRUE;
    return GbaOverlayPlatformStatus::Ok;
}

} // extern "C"
