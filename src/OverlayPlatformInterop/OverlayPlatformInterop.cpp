#include "OverlayPlatformInterop.h"

#include "OverlayPlatformPolicy.h"
#include "ControllerIsolationHostSession.h"
#include "LocalControllerPolicy.h"
#include "ViGEmOutputAdapter.h"
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

std::atomic_bool controllerIsolationRequested{};

enum class RawEventKind {
    GuidePressed,
    LegacyDeviceChanged,
    VisibilityChanged,
    FocusChanged,
};

struct RawEvent final {
    RawEventKind kind{};
    WidgetRailOverlayPlatformGuideSource guideSource{
        WidgetRailOverlayPlatformGuideSource::None};
    widgetrail::input::GuideCompatibilityActivation::DeviceId deviceId{};
    bool value{};
};

constexpr std::uint32_t ToAbiBoolean(const bool value) noexcept {
    return value
        ? WRAIL_OVERLAY_PLATFORM_TRUE
        : WRAIL_OVERLAY_PLATFORM_FALSE;
}

[[nodiscard]] WidgetRailOverlayPlatformReadPath ConvertReadPath(
    const widgetrail::input::ControllerReadPath path) noexcept {
    switch (path) {
    case widgetrail::input::ControllerReadPath::GameInputVisibleLease:
        return WidgetRailOverlayPlatformReadPath::GameInputVisibleLease;
    case widgetrail::input::ControllerReadPath::XInputCompatibility:
        return WidgetRailOverlayPlatformReadPath::XInputCompatibility;
    case widgetrail::input::ControllerReadPath::None:
    default:
        return WidgetRailOverlayPlatformReadPath::None;
    }
}

[[nodiscard]] bool ValidOutput(
    const WidgetRailOverlayPlatformEvent* event) noexcept {
    return event && event->structSize >= sizeof(WidgetRailOverlayPlatformEvent) &&
           event->abiVersion == WRAIL_OVERLAY_PLATFORM_ABI_VERSION;
}

[[nodiscard]] bool ValidOutput(
    const WidgetRailOverlayPlatformControllerFrame* frame) noexcept {
    return frame &&
           frame->structSize >= sizeof(WidgetRailOverlayPlatformControllerFrame) &&
           frame->abiVersion == WRAIL_OVERLAY_PLATFORM_ABI_VERSION;
}

void PublishEvent(
    WidgetRailOverlayPlatformEvent& event,
    const WidgetRailOverlayPlatformEventKind kind,
    const std::uint64_t nowMilliseconds,
    const std::uint32_t value = 0,
    const WidgetRailOverlayPlatformGuideSource source =
        WidgetRailOverlayPlatformGuideSource::None) noexcept {
    event = {};
    event.kind = kind;
    event.guideSource = source;
    event.timestampMilliseconds = nowMilliseconds;
    event.value = value;
}

} // namespace

struct WidgetRailOverlayPlatformHandle final {
    enum class CallbackGateState {
        Open,
        Closing,
        Closed,
    };

    explicit WidgetRailOverlayPlatformHandle(
        const WidgetRailOverlayPlatformCreateOptions& createOptions) noexcept
        : options(createOptions) {}

    WidgetRailOverlayPlatformCreateOptions options{};
    mutable std::mutex mutex;
    mutable std::mutex callbackMutex;
    std::condition_variable callbackDrain;
    std::uint32_t callbacksInFlight{};
    CallbackGateState callbackGateState{CallbackGateState::Open};
    std::deque<RawEvent> events;
    bool initialized{};
    bool controllerRecoveryRequired{};
    bool controllerSetupFailed{};
    std::atomic_bool shutDown{};
    bool visible{};
    bool focused{};
    bool compatibilityDeviceTrackingAvailable{};
    ComPtr<IGameInput> gameInput;
    GameInputCallbackToken guideCallback{};
    GameInputCallbackToken compatibilityDeviceCallback{};
    widgetrail::input::XInputGuideCompatibility guideCompatibility;
    widgetrail::input::GuideCompatibilityActivation guideCompatibilityActivation;
    widgetrail::platform::GuideToggleDebouncer guideDebouncer;
    widgetrail::platform::ControllerFrameTracker controllerTracker;
    widgetrail::isolation::ControllerIsolationHostSession controllerIsolation;
    widgetrail::ForegroundTargetTracker foregroundTarget;
    std::optional<widgetrail::input::ControllerReadPath> lastReadPath;
    std::optional<bool> lastForegroundExclusive;
    std::optional<HRESULT> lastGamepadReadResult;
    std::uintptr_t lastGamepadReadDevice{};
    GameInputDeviceStatus lastGamepadReadStatus{};
    std::uint64_t lastGamepadReadDiagnosticAt{};

    void ObserveGamepadRead(const HRESULT result, IGameInputDevice* device) noexcept {
        const auto identity = reinterpret_cast<std::uintptr_t>(device);
        const auto status = device ? device->GetDeviceStatus() : GameInputDeviceNoStatus;
        if (lastGamepadReadResult == result && lastGamepadReadDevice == identity &&
            lastGamepadReadStatus == status) return;
        const auto now = GetTickCount64();
        // Alternating active controllers or read failures must not flood the UI log.
        if (lastGamepadReadResult && now - lastGamepadReadDiagnosticAt < 1000) return;
        lastGamepadReadDiagnosticAt = now;
        lastGamepadReadResult = result;
        lastGamepadReadDevice = identity;
        lastGamepadReadStatus = status;
        Diagnostic(L"Controller navigation reader result=" +
            std::to_wstring(static_cast<unsigned long>(result)) +
            L" device=" + std::to_wstring(identity) +
            L" status=" + std::to_wstring(static_cast<unsigned>(status)));
    }

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

    void RetireLocalControllerOwners() noexcept {
        if (gameInput && guideCallback != 0) {
            gameInput->StopCallback(guideCallback);
            gameInput->UnregisterCallback(guideCallback);
            guideCallback = 0;
        }
        if (gameInput && compatibilityDeviceCallback != 0) {
            gameInput->StopCallback(compatibilityDeviceCallback);
            gameInput->UnregisterCallback(compatibilityDeviceCallback);
            compatibilityDeviceCallback = 0;
        }
        WaitForCallbacks();
        guideCompatibility.Shutdown();
        gameInput.Reset();
        compatibilityDeviceTrackingAvailable = false;
        guideCompatibilityActivation.Reset();
    }

    [[nodiscard]] bool RequiresLegacyPolling() const noexcept {
        std::scoped_lock lock(mutex);
        return !controllerIsolation.active() &&
            guideCompatibility.available() &&
            (!compatibilityDeviceTrackingAvailable ||
             guideCompatibilityActivation.active());
    }

    [[nodiscard]] bool TryReadRaw(
        const bool foregroundConfirmed,
        WidgetRailOverlayPlatformRawControllerState& state,
        bool& connected,
        widgetrail::input::ControllerInputOwnershipDecision& decision) noexcept {
        decision = widgetrail::input::DecideControllerInputOwnership(
            visible, visible, foregroundConfirmed, gameInput.Get() != nullptr);
        if (!lastReadPath || *lastReadPath != decision.readPath ||
            !lastForegroundExclusive ||
            *lastForegroundExclusive != decision.foregroundExclusive) {
            lastReadPath = decision.readPath;
            lastForegroundExclusive = decision.foregroundExclusive;
            switch (decision.readPath) {
            case widgetrail::input::ControllerReadPath::GameInputVisibleLease:
                Diagnostic(decision.foregroundExclusive
                    ? L"Controller read path: visible GameInput lease, foreground-exclusive"
                    : L"Controller read path: visible GameInput lease, background-shared");
                break;
            case widgetrail::input::ControllerReadPath::XInputCompatibility:
                Diagnostic(L"Controller read path: XInput compatibility (not exclusive)");
                break;
            case widgetrail::input::ControllerReadPath::None:
                Diagnostic(L"Controller read path dormant: visible lease is inactive");
                break;
            }
        }

        state = {};
        connected = false;
        if (decision.readPath ==
            widgetrail::input::ControllerReadPath::GameInputVisibleLease) {
            ComPtr<IGameInputReading> reading;
            const HRESULT result = gameInput->GetCurrentReading(
                GameInputKindGamepad, nullptr, reading.ReleaseAndGetAddressOf());
            if (FAILED(result) || !reading) {
                ObserveGamepadRead(FAILED(result) ? result : E_UNEXPECTED, nullptr);
                return false;
            }
            ComPtr<IGameInputDevice> device;
            reading->GetDevice(device.GetAddressOf());
            GameInputGamepadState input{};
            if (!reading->GetGamepadState(&input)) {
                ObserveGamepadRead(E_FAIL, device.Get());
                return false;
            }
            ObserveGamepadRead(S_OK, device.Get());
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
            widgetrail::input::ControllerReadPath::XInputCompatibility) {
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
    explicit CallbackLease(WidgetRailOverlayPlatformHandle* handle) noexcept
        : handle_(handle && handle->TryEnterCallback() ? handle : nullptr) {}

    ~CallbackLease() {
        if (handle_) handle_->LeaveCallback();
    }

    [[nodiscard]] explicit operator bool() const noexcept {
        return handle_ != nullptr;
    }

private:
    WidgetRailOverlayPlatformHandle* handle_{};
};

void CALLBACK OnSystemButton(
    GameInputCallbackToken,
    void* context,
    IGameInputDevice*,
    std::uint64_t,
    GameInputSystemButtons current,
    GameInputSystemButtons previous) {
    auto* handle = static_cast<WidgetRailOverlayPlatformHandle*>(context);
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
            WidgetRailOverlayPlatformGuideSource::GameInput});
    }
}

void CALLBACK OnGameInputDevice(
    GameInputCallbackToken,
    void* context,
    IGameInputDevice* device,
    std::uint64_t,
    GameInputDeviceStatus current,
    GameInputDeviceStatus previous) {
    auto* handle = static_cast<WidgetRailOverlayPlatformHandle*>(context);
    CallbackLease callbackLease(handle);
    if (!callbackLease) return;
    const bool connected =
        (current & GameInputDeviceConnected) != GameInputDeviceNoStatus;
    const bool wasConnected =
        (previous & GameInputDeviceConnected) != GameInputDeviceNoStatus;
    if (connected == wasConnected || !device) return;

    handle->Diagnostic(L"Controller device connection changed device=" +
        std::to_wstring(reinterpret_cast<std::uintptr_t>(device)) +
        L" connected=" + std::to_wstring(connected ? 1 : 0) +
        L" status=" + std::to_wstring(static_cast<unsigned>(current)));

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

[[nodiscard]] WidgetRailOverlayPlatformStatus ValidateHandle(
    const WidgetRailOverlayPlatformHandle* handle) noexcept {
    if (!handle) return WidgetRailOverlayPlatformStatus::InvalidArgument;
    if (handle->shutDown) return WidgetRailOverlayPlatformStatus::ShutDown;
    if (!handle->initialized) return WidgetRailOverlayPlatformStatus::NotInitialized;
    return WidgetRailOverlayPlatformStatus::Ok;
}

} // namespace

extern "C" {

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformGetAbiVersion() noexcept {
    return WRAIL_OVERLAY_PLATFORM_ABI_VERSION;
}

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformControllerPrerequisites() noexcept {
    std::uint32_t flags{};
    try {
        widgetrail::isolation::HidHideConfigurationAdapter hiding;
        widgetrail::isolation::HidHideSnapshot snapshot;
        std::uint32_t error{};
        if (hiding.ReadSnapshot(snapshot, error) == widgetrail::isolation::HidHideConfigurationStatus::Ready && !snapshot.applicationListInverted)
            flags |= 1U;
        const auto& api = widgetrail::isolation::OfficialViGEmApi();
        if (auto client = api.AllocateClient()) {
            if (api.Connect(client) == api.successCode) { flags |= 2U; api.Disconnect(client); }
            api.FreeClient(client);
        }
        ComPtr<IGameInput> input;
        widgetrail::input::XInputGuideCompatibility guide;
        if (SUCCEEDED(GameInputCreate(input.ReleaseAndGetAddressOf())) && input && guide.Initialize())
            flags |= 4U;
    } catch (...) { return flags; }
    return flags;
}

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformControllerControlState(const WidgetRailOverlayPlatformHandle* handle) noexcept {
    if (!handle || handle->shutDown) return 0;
    if (handle->controllerRecoveryRequired) return 5;
    if (handle->controllerSetupFailed) return 6;
    using Progress = widgetrail::isolation::LocalControllerProgress;
    switch (handle->controllerIsolation.progress()) {
    case Progress::Disabled: return 1;
    case Progress::Preparing: case Progress::AwaitingNeutral: return 2;
    case Progress::Playing: case Progress::Contained: return 3;
    case Progress::WaitingForController: return 4;
    case Progress::Fault: return 5;
    }
    return 0;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetExclusiveControl(WidgetRailOverlayPlatformHandle* handle, const std::uint32_t enabled) noexcept {
    if (!handle || handle->shutDown) return WidgetRailOverlayPlatformStatus::InvalidArgument;
    const bool requested = enabled != WRAIL_OVERLAY_PLATFORM_FALSE;
    if (requested && WidgetRailOverlayPlatformControllerPrerequisites() != 7U) {
        if (!handle->controllerIsolation.active()) handle->controllerSetupFailed = true;
        return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
    }
    if (requested && requested == controllerIsolationRequested.load(std::memory_order_acquire) &&
        !handle->controllerRecoveryRequired && handle->initialized &&
        handle->controllerIsolation.progress() != widgetrail::isolation::LocalControllerProgress::Fault)
        return WidgetRailOverlayPlatformStatus::Ok;
    handle->controllerIsolation.Stop();
    std::wstring diagnostic;
    try {
        widgetrail::isolation::NativeLocalPolicyEffects effects;
        if (!widgetrail::isolation::RecoverLocalControllerPolicy(
                widgetrail::isolation::LocalControllerJournalPath(), effects, diagnostic)) {
            handle->controllerRecoveryRequired = true;
            handle->Diagnostic(diagnostic);
            return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
        }
    } catch (...) {
        handle->controllerRecoveryRequired = true;
        return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
    }
    handle->controllerRecoveryRequired = false;
    handle->controllerSetupFailed = false;
    handle->controllerIsolation.Reset();
    handle->RetireLocalControllerOwners();
    { std::scoped_lock lock(handle->mutex); handle->events.clear(); }
    handle->controllerTracker.Reset();
    handle->guideDebouncer.Reset();
    handle->initialized = false;
    controllerIsolationRequested.store(requested, std::memory_order_release);
    const auto result = WidgetRailOverlayPlatformInitialize(handle);
    if (result != WidgetRailOverlayPlatformStatus::Ok) return result;
    if (requested && handle->controllerSetupFailed) return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
    if (requested && handle->visible && !handle->controllerIsolation.PrepareOverlay(diagnostic)) {
        handle->Diagnostic(diagnostic);
        return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
    }
    return WidgetRailOverlayPlatformStatus::Ok;
}

void WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformConfigureControllerIsolation(const std::uint32_t enabled) noexcept {
    controllerIsolationRequested.store(enabled != WRAIL_OVERLAY_PLATFORM_FALSE,
                                       std::memory_order_release);
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRecoverControllerIsolation(wchar_t* message, const std::uint32_t capacity) noexcept {
    if (!message || capacity == 0) return WidgetRailOverlayPlatformStatus::InvalidArgument;
    std::wstring diagnostic;
    bool recovered{};
    try {
        widgetrail::isolation::NativeLocalPolicyEffects effects;
        recovered = widgetrail::isolation::RecoverLocalControllerPolicy(
            widgetrail::isolation::LocalControllerJournalPath(), effects, diagnostic);
    } catch (...) {
        diagnostic = L"Controller isolation recovery exception; journal retained.";
    }
    wcsncpy_s(message, capacity, diagnostic.c_str(), _TRUNCATE);
    return recovered ? WidgetRailOverlayPlatformStatus::Ok
                     : WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL WidgetRailOverlayPlatformCreate(
    const WidgetRailOverlayPlatformCreateOptions* options,
    WidgetRailOverlayPlatformHandle** handle) noexcept {
    if (!options || !handle ||
        options->structSize < sizeof(WidgetRailOverlayPlatformCreateOptions)) {
        return WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    if (options->abiVersion != WRAIL_OVERLAY_PLATFORM_ABI_VERSION) {
        return WidgetRailOverlayPlatformStatus::InvalidVersion;
    }
    *handle = nullptr;
    *handle = new (std::nothrow) WidgetRailOverlayPlatformHandle(*options);
    if (!*handle) return WidgetRailOverlayPlatformStatus::AllocationFailed;
    return WidgetRailOverlayPlatformStatus::Ok;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformInitialize(WidgetRailOverlayPlatformHandle* handle) noexcept {
    if (!handle) return WidgetRailOverlayPlatformStatus::InvalidArgument;
    if (handle->shutDown) return WidgetRailOverlayPlatformStatus::ShutDown;
    if (handle->initialized) return WidgetRailOverlayPlatformStatus::Ok;

    std::wstring isolationDiagnostic;
    if (handle->controllerIsolation.Start(
            controllerIsolationRequested.load(std::memory_order_acquire),
            isolationDiagnostic,
            [](void* context) noexcept { static_cast<WidgetRailOverlayPlatformHandle*>(context)->Signal(); },
            handle, handle->visible)) {
        handle->Diagnostic(
            L"Controller exclusive-control owner started");
        handle->initialized = true;
        return WidgetRailOverlayPlatformStatus::Ok;
    }
    if (controllerIsolationRequested.load(std::memory_order_acquire)) {
        if (isolationDiagnostic.empty())
            isolationDiagnostic = L"Controller isolation startup failed without a diagnostic.";
        handle->Diagnostic(isolationDiagnostic);
        // Startup can signal failure before its scoped hide-policy cleanup has
        // finished. Drain the owner and verify recovery before restoring reads.
        handle->controllerIsolation.Stop();
        try {
            widgetrail::isolation::NativeLocalPolicyEffects effects;
            if (!widgetrail::isolation::RecoverLocalControllerPolicy(
                    widgetrail::isolation::LocalControllerJournalPath(), effects, isolationDiagnostic)) {
                handle->controllerRecoveryRequired = true;
                handle->Diagnostic(isolationDiagnostic);
                return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
            }
        } catch (...) {
            handle->controllerRecoveryRequired = true;
            return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
        }
        handle->controllerIsolation.Reset();
        controllerIsolationRequested.store(false, std::memory_order_release);
        handle->controllerSetupFailed = true;
        handle->Diagnostic(L"Exclusive control setup failed; owned hiding restored and ordinary input resumed.");
    }

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
    return WidgetRailOverlayPlatformStatus::Ok;
}

void WRAIL_OVERLAY_PLATFORM_CALL WidgetRailOverlayPlatformShutdown(
    WidgetRailOverlayPlatformHandle* handle) noexcept {
    if (!handle || !handle->BeginShutdown()) return;
    handle->controllerIsolation.Stop();
    handle->RetireLocalControllerOwners();
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

void WRAIL_OVERLAY_PLATFORM_CALL WidgetRailOverlayPlatformDestroy(
    WidgetRailOverlayPlatformHandle* handle) noexcept {
    if (!handle) return;
    WidgetRailOverlayPlatformShutdown(handle);
    delete handle;
}

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL WidgetRailOverlayPlatformHasGameInput(
    const WidgetRailOverlayPlatformHandle* handle) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown &&
        (handle->controllerIsolation.active() || handle->gameInput));
}

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRequiresLegacyGuidePolling(
    const WidgetRailOverlayPlatformHandle* handle) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown && handle->RequiresLegacyPolling());
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetWindowState(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uint32_t visibleValue,
    const std::uint32_t focusedValue) noexcept {
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;
    const bool visible = visibleValue != WRAIL_OVERLAY_PLATFORM_FALSE;
    const bool focused = focusedValue != WRAIL_OVERLAY_PLATFORM_FALSE;
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
    if (!visible && handle->controllerIsolation.active())
        handle->controllerIsolation.CloseOverlay();
    return WidgetRailOverlayPlatformStatus::Ok;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPrepareVisible(
    WidgetRailOverlayPlatformHandle* handle) noexcept {
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;
    if (!handle->controllerIsolation.active())
        return WidgetRailOverlayPlatformStatus::Ok;
    std::wstring diagnostic;
    if (handle->controllerIsolation.PrepareOverlay(diagnostic))
        return WidgetRailOverlayPlatformStatus::Ok;
    handle->Diagnostic(diagnostic);
    return WidgetRailOverlayPlatformStatus::ControllerIsolationUnavailable;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformDrainEvent(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept {
    if (!hasEvent || !ValidOutput(event)) {
        return WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    *hasEvent = WRAIL_OVERLAY_PLATFORM_FALSE;
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;

    if (handle->controllerIsolation.active()) {
        std::uint64_t guideEvent{};
        std::wstring diagnostic;
        const bool guideAvailable =
            handle->controllerIsolation.PollGuide(guideEvent, diagnostic);
        if (!diagnostic.empty()) handle->Diagnostic(diagnostic);
        if (guideAvailable && guideEvent != 0 &&
                   (guideEvent & (1ULL << 63)) == 0) {
            handle->Queue({
                RawEventKind::GuidePressed,
                WidgetRailOverlayPlatformGuideSource::GameInput});
        }
    }

    for (;;) {
        RawEvent raw;
        {
            std::scoped_lock lock(handle->mutex);
            if (handle->events.empty()) return WidgetRailOverlayPlatformStatus::Ok;
            raw = std::move(handle->events.front());
            handle->events.pop_front();
        }
        switch (raw.kind) {
        case RawEventKind::GuidePressed:
            if (!handle->guideDebouncer.Accept(nowMilliseconds)) continue;
            PublishEvent(
                *event,
                WidgetRailOverlayPlatformEventKind::GuideToggleRequested,
                nowMilliseconds,
                0,
                raw.guideSource);
            *hasEvent = WRAIL_OVERLAY_PLATFORM_TRUE;
            return WidgetRailOverlayPlatformStatus::Ok;
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
                WidgetRailOverlayPlatformEventKind::LegacyGuidePollingChanged,
                nowMilliseconds,
                active ? 1U : 0U);
            *hasEvent = WRAIL_OVERLAY_PLATFORM_TRUE;
            return WidgetRailOverlayPlatformStatus::Ok;
        }
        case RawEventKind::VisibilityChanged:
            PublishEvent(
                *event,
                WidgetRailOverlayPlatformEventKind::VisibilityChanged,
                nowMilliseconds,
                raw.value ? 1U : 0U);
            *hasEvent = WRAIL_OVERLAY_PLATFORM_TRUE;
            return WidgetRailOverlayPlatformStatus::Ok;
        case RawEventKind::FocusChanged:
            PublishEvent(
                *event,
                WidgetRailOverlayPlatformEventKind::FocusChanged,
                nowMilliseconds,
                raw.value ? 1U : 0U);
            *hasEvent = WRAIL_OVERLAY_PLATFORM_TRUE;
            return WidgetRailOverlayPlatformStatus::Ok;
        }
    }
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPollLegacyGuide(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept {
    if (!hasEvent || !ValidOutput(event)) {
        return WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    *hasEvent = WRAIL_OVERLAY_PLATFORM_FALSE;
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;
    if (!handle->RequiresLegacyPolling()) return WidgetRailOverlayPlatformStatus::Ok;
    const auto slots = handle->guideCompatibility.PollRisingEdges();
    if (slots == 0 || !handle->guideDebouncer.Accept(nowMilliseconds)) {
        return WidgetRailOverlayPlatformStatus::Ok;
    }
    PublishEvent(
        *event,
        WidgetRailOverlayPlatformEventKind::GuideToggleRequested,
        nowMilliseconds,
        slots,
        WidgetRailOverlayPlatformGuideSource::LegacyCompatibility);
    *hasEvent = WRAIL_OVERLAY_PLATFORM_TRUE;
    return WidgetRailOverlayPlatformStatus::Ok;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPrimeController(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uint32_t foregroundConfirmed,
    const std::uint64_t nowMilliseconds) noexcept {
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;
    WidgetRailOverlayPlatformRawControllerState state;
    bool connected = false;
    if (handle->controllerIsolation.active()) {
        widgetrail::isolation::ControllerIsolationHostReading reading;
        std::wstring diagnostic;
        const bool polled = handle->controllerIsolation.Poll(reading, diagnostic);
        if (!diagnostic.empty()) handle->Diagnostic(diagnostic);
        if (polled) {
            state = {
                reading.state.buttons, reading.state.leftTrigger,
                reading.state.rightTrigger, reading.state.leftThumbX,
                reading.state.leftThumbY, reading.state.rightThumbX,
                reading.state.rightThumbY};
            connected = reading.progress ==
                widgetrail::isolation::LocalControllerProgress::Contained;
            if (reading.guideEvent != 0 &&
                (reading.guideEvent & (1ULL << 63)) == 0) {
                handle->Queue({
                    RawEventKind::GuidePressed,
                    WidgetRailOverlayPlatformGuideSource::GameInput});
            }
        }
        handle->controllerTracker.Prime(connected, state, nowMilliseconds);
        return WidgetRailOverlayPlatformStatus::Ok;
    }
    widgetrail::input::ControllerInputOwnershipDecision decision;
    (void)handle->TryReadRaw(
        foregroundConfirmed != WRAIL_OVERLAY_PLATFORM_FALSE,
        state,
        connected,
        decision);
    handle->controllerTracker.Prime(
        connected, state, nowMilliseconds);
    return WidgetRailOverlayPlatformStatus::Ok;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformReadController(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uint32_t foregroundConfirmed,
    const std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformControllerFrame* frame) noexcept {
    if (!ValidOutput(frame)) {
        return WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    const auto status = ValidateHandle(handle);
    if (status != WidgetRailOverlayPlatformStatus::Ok) return status;
    WidgetRailOverlayPlatformRawControllerState state;
    bool connected = false;
    if (handle->controllerIsolation.active()) {
        widgetrail::isolation::ControllerIsolationHostReading reading;
        std::wstring diagnostic;
        const bool polled = handle->controllerIsolation.Poll(reading, diagnostic);
        if (!diagnostic.empty()) handle->Diagnostic(diagnostic);
        if (polled) {
            state = {
                reading.state.buttons, reading.state.leftTrigger,
                reading.state.rightTrigger, reading.state.leftThumbX,
                reading.state.leftThumbY, reading.state.rightThumbX,
                reading.state.rightThumbY};
            connected = handle->visible && reading.progress ==
                widgetrail::isolation::LocalControllerProgress::Contained;
            if (reading.guideEvent != 0 &&
                (reading.guideEvent & (1ULL << 63)) == 0) {
                handle->Queue({
                    RawEventKind::GuidePressed,
                    WidgetRailOverlayPlatformGuideSource::GameInput});
            }
        }
        const auto structSize = frame->structSize;
        const auto abiVersion = frame->abiVersion;
        *frame = handle->controllerTracker.Update(
            connected, state, nowMilliseconds, !reading.queuedInput);
        frame->structSize = structSize;
        frame->abiVersion = abiVersion;
        frame->foregroundExclusive = ToAbiBoolean(connected);
        frame->readPath = WidgetRailOverlayPlatformReadPath::ControllerIsolation;
        // History preserves transitions, but cannot prove a control is still
        // held after a UI stall. Finish with a current-state read for repeats.
        frame->remainingFrames = reading.remainingInputStates +
            (reading.queuedInput ? 1U : 0U);
        return WidgetRailOverlayPlatformStatus::Ok;
    }
    widgetrail::input::ControllerInputOwnershipDecision decision;
    (void)handle->TryReadRaw(
        foregroundConfirmed != WRAIL_OVERLAY_PLATFORM_FALSE,
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
    return WidgetRailOverlayPlatformStatus::Ok;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetOwnedWindows(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uintptr_t overlay,
    const std::uintptr_t backdrop) noexcept {
    if (!handle || handle->shutDown) {
        return handle
            ? WidgetRailOverlayPlatformStatus::ShutDown
            : WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    handle->foregroundTarget.SetOwnedWindows(overlay, backdrop);
    return WidgetRailOverlayPlatformStatus::Ok;
}

std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformObserveForegroundTarget(
    WidgetRailOverlayPlatformHandle* handle,
    const std::uintptr_t candidate,
    const std::uint32_t candidateIsValid) noexcept {
    return ToAbiBoolean(
        handle && !handle->shutDown &&
        handle->foregroundTarget.Observe(
            candidate,
            candidateIsValid != WRAIL_OVERLAY_PLATFORM_FALSE));
}

std::uintptr_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRememberedForegroundTarget(
    const WidgetRailOverlayPlatformHandle* handle) noexcept {
    return handle && !handle->shutDown
        ? handle->foregroundTarget.remembered()
        : 0;
}

std::uintptr_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformResolveForegroundTarget(
    const WidgetRailOverlayPlatformHandle* handle,
    const std::uintptr_t fallback,
    const std::uint32_t rememberedTargetIsValid) noexcept {
    return handle && !handle->shutDown
        ? handle->foregroundTarget.Resolve(
              fallback,
              rememberedTargetIsValid != WRAIL_OVERLAY_PLATFORM_FALSE)
        : fallback;
}

WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformComputePlacement(
    const WidgetRailOverlayPlatformPlacementInput* input,
    WidgetRailOverlayPlatformPlacement* placement,
    std::uint32_t* hasPlacement) noexcept {
    if (!input || !placement || !hasPlacement ||
        input->structSize < sizeof(WidgetRailOverlayPlatformPlacementInput) ||
        placement->structSize < sizeof(WidgetRailOverlayPlatformPlacement)) {
        return WidgetRailOverlayPlatformStatus::InvalidArgument;
    }
    if (input->abiVersion != WRAIL_OVERLAY_PLATFORM_ABI_VERSION ||
        placement->abiVersion != WRAIL_OVERLAY_PLATFORM_ABI_VERSION) {
        return WidgetRailOverlayPlatformStatus::InvalidVersion;
    }
    *hasPlacement = WRAIL_OVERLAY_PLATFORM_FALSE;
    const auto resolved = widgetrail::ComputeOverlayPlacement(
        {input->workLeft, input->workTop, input->workRight, input->workBottom},
        input->dpi,
        input->desiredWidthDip,
        input->desiredHeightDip,
        {input->sideMarginDip, input->topMarginDip, input->bottomMarginDip});
    if (!resolved) return WidgetRailOverlayPlatformStatus::Ok;
    placement->x = resolved->x;
    placement->y = resolved->y;
    placement->width = resolved->width;
    placement->height = resolved->height;
    *hasPlacement = WRAIL_OVERLAY_PLATFORM_TRUE;
    return WidgetRailOverlayPlatformStatus::Ok;
}

} // extern "C"
