#include "ControllerIsolationReader.h"

#if defined(WRAIL_GAMEINPUT_ISOLATION_READER)

#include <windows.h>

#include <GameInput.h>
#include <bcrypt.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <devpkey.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cmath>
#include <cstring>
#include <cwctype>
#include <limits>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <string_view>

namespace widgetrail::isolation {
namespace {

using Microsoft::WRL::ComPtr;
using namespace GameInput::v3;

static_assert(sizeof(APP_LOCAL_DEVICE_ID) == 32);

[[nodiscard]] GamepadState ConvertState(
    const GameInputGamepadState& input) noexcept {
    const auto has = [buttons = input.buttons](
                         const GameInputGamepadButtons button) noexcept {
        return (static_cast<unsigned>(buttons) &
                static_cast<unsigned>(button)) != 0;
    };
    GamepadState state{};
    if (has(GameInputGamepadDPadUp)) state.buttons |= 0x0001;
    if (has(GameInputGamepadDPadDown)) state.buttons |= 0x0002;
    if (has(GameInputGamepadDPadLeft)) state.buttons |= 0x0004;
    if (has(GameInputGamepadDPadRight)) state.buttons |= 0x0008;
    if (has(GameInputGamepadMenu)) state.buttons |= 0x0010;
    if (has(GameInputGamepadView)) state.buttons |= 0x0020;
    if (has(GameInputGamepadLeftThumbstick)) state.buttons |= 0x0040;
    if (has(GameInputGamepadRightThumbstick)) state.buttons |= 0x0080;
    if (has(GameInputGamepadLeftShoulder)) state.buttons |= 0x0100;
    if (has(GameInputGamepadRightShoulder)) state.buttons |= 0x0200;
    if (has(GameInputGamepadA)) state.buttons |= 0x1000;
    if (has(GameInputGamepadB)) state.buttons |= 0x2000;
    if (has(GameInputGamepadX)) state.buttons |= 0x4000;
    if (has(GameInputGamepadY)) state.buttons |= 0x8000;
    const auto thumb = [](const float value) noexcept {
        const auto clamped = std::clamp(value, -1.0F, 1.0F);
        const auto scale = clamped < 0.0F ? 32768.0F : 32767.0F;
        return static_cast<std::int16_t>(std::lround(clamped * scale));
    };
    const auto trigger = [](const float value) noexcept {
        return static_cast<std::uint8_t>(std::lround(
            std::clamp(value, 0.0F, 1.0F) * 255.0F));
    };
    state.leftTrigger = trigger(input.leftTrigger);
    state.rightTrigger = trigger(input.rightTrigger);
    state.leftThumbX = thumb(input.leftThumbstickX);
    state.leftThumbY = thumb(input.leftThumbstickY);
    state.rightThumbX = thumb(input.rightThumbstickX);
    state.rightThumbY = thumb(input.rightThumbstickY);
    return state;
}

[[nodiscard]] std::optional<std::wstring> NormalizePnpPath(
    const char* path) {
    if (!path || !*path) return std::nullopt;
    const auto length = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, nullptr, 0);
    if (length <= 1) return std::nullopt;
    std::wstring normalized(static_cast<std::size_t>(length), L'\0');
    if (MultiByteToWideChar(
            CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, normalized.data(),
            length) == 0) {
        return std::nullopt;
    }
    normalized.pop_back();
    std::ranges::transform(normalized, normalized.begin(), [](wchar_t value) {
        return static_cast<wchar_t>(std::towupper(value));
    });
    return normalized;
}

[[nodiscard]] bool HashPnpPath(
    const std::wstring& path,
    std::array<std::uint8_t, 32>& digest) noexcept {
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    if (BCryptOpenAlgorithmProvider(
            &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0) {
        return false;
    }
    const auto bytes = reinterpret_cast<PUCHAR>(
        const_cast<wchar_t*>(path.data()));
    if (path.size() >
        std::numeric_limits<ULONG>::max() / sizeof(wchar_t)) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return false;
    }
    const auto byteCount = static_cast<ULONG>(path.size() * sizeof(wchar_t));
    const bool succeeded =
        BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0 &&
        BCryptHashData(hash, bytes, byteCount, 0) == 0 &&
        BCryptFinishHash(
            hash, digest.data(), static_cast<ULONG>(digest.size()), 0) == 0;
    if (hash) BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return succeeded;
}

class CfgMgrDeviceAncestryBackend final
    : public ControllerDeviceAncestryBackend {
public:
    bool ResolveInterfaceInstanceId(
        const std::wstring_view normalizedInterfacePath,
        ControllerDeviceNodeIdentity& identity) noexcept override {
        identity = {};
        if (normalizedInterfacePath.empty() ||
            normalizedInterfacePath.size() >=
                ControllerDeviceIdentityCharacterCapacity) {
            return false;
        }
        std::array<wchar_t, ControllerDeviceIdentityCharacterCapacity>
            interfacePath{};
        std::copy(
            normalizedInterfacePath.begin(), normalizedInterfacePath.end(),
            interfacePath.begin());
        DEVPROPTYPE propertyType{};
        ULONG propertyBytes = static_cast<ULONG>(
            identity.value.size() * sizeof(wchar_t));
        const auto result = CM_Get_Device_Interface_PropertyW(
            interfacePath.data(), &DEVPKEY_Device_InstanceId, &propertyType,
            reinterpret_cast<PBYTE>(identity.value.data()), &propertyBytes, 0);
        if (result != CR_SUCCESS || propertyType != DEVPROP_TYPE_STRING ||
            propertyBytes < 2 * sizeof(wchar_t) ||
            propertyBytes > identity.value.size() * sizeof(wchar_t) ||
            propertyBytes % sizeof(wchar_t) != 0) {
            identity = {};
            return false;
        }
        const auto characters = propertyBytes / sizeof(wchar_t);
        if (identity.value[characters - 1] != L'\0') {
            identity = {};
            return false;
        }
        identity.length = characters - 1;
        return identity.valid();
    }

    bool LocateNode(
        const ControllerDeviceNodeIdentity& identity,
        ControllerDeviceNodeToken& node) noexcept override {
        if (!identity.valid()) return false;
        DEVINST value{};
        if (CM_Locate_DevNodeW(
                &value, const_cast<wchar_t*>(identity.value.data()),
                CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS) {
            return false;
        }
        node = value;
        return true;
    }

    bool LocateRoot(ControllerDeviceNodeToken& root) noexcept override {
        DEVINST value{};
        if (CM_Locate_DevNodeW(&value, nullptr, CM_LOCATE_DEVNODE_NORMAL) !=
            CR_SUCCESS) {
            return false;
        }
        root = value;
        return true;
    }

    bool ReadNodeIdentity(
        const ControllerDeviceNodeToken node,
        ControllerDeviceNodeIdentity& identity) noexcept override {
        identity = {};
        if (CM_Get_Device_IDW(
                static_cast<DEVINST>(node), identity.value.data(),
                static_cast<ULONG>(identity.value.size()), 0) != CR_SUCCESS) {
            return false;
        }
        while (identity.length < identity.value.size() &&
               identity.value[identity.length] != L'\0') {
            identity.value[identity.length] = static_cast<wchar_t>(
                std::towupper(identity.value[identity.length]));
            ++identity.length;
        }
        return identity.valid();
    }

    bool Parent(
        const ControllerDeviceNodeToken node,
        ControllerDeviceNodeToken& parent) noexcept override {
        DEVINST value{};
        if (CM_Get_Parent(&value, static_cast<DEVINST>(node), 0) != CR_SUCCESS)
            return false;
        parent = value;
        return true;
    }
};

[[nodiscard]] SelectedControllerEnrollment IdentityFrom(
    const GameInputDeviceInfo& info,
    const std::uint64_t enrollmentToken,
    const std::array<std::uint8_t, 32>& pnpDigest,
    const bool virtualOutput,
    const bool connected) noexcept {
    SelectedControllerEnrollment identity{};
    identity.enrollmentToken = enrollmentToken;
    std::memcpy(identity.deviceId.data(), &info.deviceId, identity.deviceId.size());
    std::memcpy(
        identity.deviceRootId.data(), &info.deviceRootId,
        identity.deviceRootId.size());
    std::memcpy(
        identity.containerId.data(), &info.containerId,
        identity.containerId.size());
    identity.normalizedPnpPathDigest = pnpDigest;
    identity.vendorId = info.vendorId;
    identity.productId = info.productId;
    identity.deviceFamily = static_cast<std::int8_t>(info.deviceFamily);
    identity.connected = connected;
    identity.gamepadSupported =
        (static_cast<unsigned>(info.supportedInput) &
            static_cast<unsigned>(GameInputKindGamepad)) != 0;
    identity.knownVirtualOutput = virtualOutput;
    return identity;
}

struct PhysicalControllerDiscoveryContext final {
    explicit PhysicalControllerDiscoveryContext(
        const std::uint64_t value) noexcept : enrollmentToken(value) {}

    std::uint64_t enrollmentToken{};
    std::mutex mutex;
    SelectedControllerDiscovery discovery;
    SelectedControllerDescriptor selectedDescriptor;
    ComPtr<IGameInputDevice> selectedDevice;
};

void CALLBACK OnPhysicalControllerDiscovery(
    GameInputCallbackToken,
    void* context,
    IGameInputDevice* device,
    std::uint64_t,
    const GameInputDeviceStatus currentStatus,
    GameInputDeviceStatus) noexcept {
    if (!context || !device ||
        (currentStatus & GameInputDeviceConnected) == 0) return;
    auto& state = *static_cast<PhysicalControllerDiscoveryContext*>(context);
    SelectedControllerCandidateKind candidateKind =
        SelectedControllerCandidateKind::Unknown;
    SelectedControllerDescriptor descriptor;
    const GameInputDeviceInfo* info{};
    if (SUCCEEDED(device->GetDeviceInfo(&info)) && info &&
        (static_cast<unsigned>(info->supportedInput) &
            static_cast<unsigned>(GameInputKindGamepad)) != 0) {
        const auto normalized = NormalizePnpPath(info->pnpPath);
        if (normalized) {
            CfgMgrDeviceAncestryBackend backend;
            const auto ancestry = ClassifyControllerDeviceAncestry(
                *normalized, backend);
            if (ancestry == ControllerDeviceAncestry::KnownVirtualOutput) {
                candidateKind =
                    SelectedControllerCandidateKind::KnownVirtualOutput;
            } else if (ancestry == ControllerDeviceAncestry::Physical) {
                std::array<std::uint8_t, 32> digest{};
                if (backend.ResolveInterfaceInstanceId(
                        *normalized, descriptor.deviceInstanceId) &&
                    descriptor.deviceInstanceId.valid() &&
                    HashPnpPath(*normalized, digest)) {
                    descriptor.enrollment = IdentityFrom(
                        *info, state.enrollmentToken, digest, false, true);
                    candidateKind = SelectedControllerCandidateKind::Physical;
                }
            }
        }
    }
    const std::scoped_lock lock(state.mutex);
    if (candidateKind == SelectedControllerCandidateKind::Physical &&
        descriptor.valid() &&
        (!state.selectedDevice || state.selectedDescriptor == descriptor)) {
        state.selectedDescriptor = descriptor;
        state.selectedDevice = device;
    }
    state.discovery.Observe(candidateKind, descriptor);
}

[[nodiscard]] SelectedControllerDiscoveryStatus
DiscoverCurrentPhysicalController(
    IGameInput& gameInput,
    const std::uint64_t enrollmentToken,
    SelectedControllerDescriptor& descriptor,
    ComPtr<IGameInputDevice>* const selectedDevice = nullptr) noexcept {
    descriptor = {};
    if (selectedDevice) selectedDevice->Reset();
    if (enrollmentToken == 0)
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    PhysicalControllerDiscoveryContext context(enrollmentToken);
    GameInputCallbackToken callback{};
    const auto registered = gameInput.RegisterDeviceCallback(
        nullptr, GameInputKindGamepad, GameInputDeviceConnected,
        GameInputBlockingEnumeration, &context,
        OnPhysicalControllerDiscovery, &callback);
    if (FAILED(registered) || callback == 0) {
        if (callback != 0) {
            gameInput.StopCallback(callback);
            (void)gameInput.UnregisterCallback(callback);
        }
        return SelectedControllerDiscoveryStatus::Unavailable;
    }
    gameInput.StopCallback(callback);
    if (!gameInput.UnregisterCallback(callback))
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    const std::scoped_lock lock(context.mutex);
    const auto status = context.discovery.Resolve(descriptor);
    if (status == SelectedControllerDiscoveryStatus::Ready && selectedDevice) {
        if (!context.selectedDevice || context.selectedDescriptor != descriptor)
            return SelectedControllerDiscoveryStatus::UnknownIdentity;
        *selectedDevice = context.selectedDevice;
    }
    return status;
}

class GameInputSelectedControllerReader final : public SelectedControllerSource {
public:
    ~GameInputSelectedControllerReader() override { Stop(); }

    SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& ingress,
        SelectedControllerEnrollment& preparedEnrollment) noexcept override {
        preparedEnrollment = {};
        if (!enrollment.valid())
            return SelectedControllerPrepareStatus::InvalidEnrollment;
        if (gameInput_ || device_)
            return SelectedControllerPrepareStatus::Unavailable;
        if (FAILED(GameInputCreate(gameInput_.ReleaseAndGetAddressOf())) ||
            !gameInput_) {
            Stop();
            return SelectedControllerPrepareStatus::Unavailable;
        }
        SelectedControllerDescriptor localDescriptor;
        const auto discovery = DiscoverCurrentPhysicalController(
            *gameInput_.Get(), enrollment.enrollmentToken, localDescriptor,
            &device_);
        const auto resolution = ResolveLocalController(
            enrollment, discovery, localDescriptor, preparedEnrollment);
        if (resolution != LocalControllerResolutionStatus::Ready || !device_) {
            Stop();
            return resolution == LocalControllerResolutionStatus::Unavailable
                ? SelectedControllerPrepareStatus::Unavailable
                : SelectedControllerPrepareStatus::IdentityMismatch;
        }
        const GameInputDeviceInfo* info{};
        if (FAILED(device_->GetDeviceInfo(&info)) || !info) {
            Stop();
            return SelectedControllerPrepareStatus::IdentityMismatch;
        }
        const auto pnpPath = NormalizePnpPath(info->pnpPath);
        std::array<std::uint8_t, 32> pnpDigest{};
        CfgMgrDeviceAncestryBackend ancestryBackend;
        const auto ancestry = pnpPath
            ? ClassifyControllerDeviceAncestry(*pnpPath, ancestryBackend)
            : ControllerDeviceAncestry::Unknown;
        if (!pnpPath || !HashPnpPath(*pnpPath, pnpDigest) ||
            ancestry == ControllerDeviceAncestry::Unknown) {
            Stop();
            return SelectedControllerPrepareStatus::IdentityMismatch;
        }
        const bool connected =
            (device_->GetDeviceStatus() & GameInputDeviceConnected) != 0;
        const auto actual = IdentityFrom(
            *info, enrollment.enrollmentToken, pnpDigest,
            ancestry == ControllerDeviceAncestry::KnownVirtualOutput,
            connected);
        if (actual.knownVirtualOutput) {
            Stop();
            return SelectedControllerPrepareStatus::VirtualOutputRejected;
        }
        if (actual != preparedEnrollment) {
            Stop();
            return SelectedControllerPrepareStatus::IdentityMismatch;
        }
        selectedDeviceId_ = preparedEnrollment.deviceId;
        ingress_ = &ingress;
        accepting_.store(true, std::memory_order_release);
        gameInput_->SetFocusPolicy(static_cast<GameInputFocusPolicy>(
            GameInputEnableBackgroundInput |
            GameInputEnableBackgroundGuideButton));
        if (FAILED(gameInput_->RegisterReadingCallback(
                device_.Get(), GameInputKindGamepad, this, OnReading,
                &readingToken_)) ||
            FAILED(gameInput_->RegisterDeviceCallback(
                device_.Get(), GameInputKindGamepad,
                GameInputDeviceConnected, GameInputNoEnumeration,
                this, OnDevice, &deviceToken_)) ||
            FAILED(gameInput_->RegisterSystemButtonCallback(
                nullptr, GameInputSystemButtonGuide, this, OnGuide,
                &guideToken_))) {
            Stop();
            return SelectedControllerPrepareStatus::CallbackRegistrationFailed;
        }
        return SelectedControllerPrepareStatus::Ready;
    }

    bool SampleCurrent(SelectedControllerCurrent& current) noexcept override {
        current = {};
        if (!gameInput_ || !device_) return false;
        ComPtr<IGameInputReading> reading;
        if (FAILED(gameInput_->GetCurrentReading(
                GameInputKindGamepad, device_.Get(),
                reading.ReleaseAndGetAddressOf())) ||
            !reading) {
            return false;
        }
        GameInputGamepadState state{};
        if (!reading->GetGamepadState(&state)) return false;
        current = {
            reading->GetTimestamp(), GetTickCount64(), ConvertState(state),
            (device_->GetDeviceStatus() & GameInputDeviceConnected) != 0};
        return current.connected;
    }

    bool ApplyRumble(const ControllerRumbleState& state) noexcept override {
        if (!device_ ||
            !std::isfinite(state.lowFrequency) ||
            !std::isfinite(state.highFrequency) ||
            !std::isfinite(state.leftTrigger) ||
            !std::isfinite(state.rightTrigger)) return false;
        const GameInputRumbleParams parameters{
            std::clamp(state.lowFrequency, 0.0F, 1.0F),
            std::clamp(state.highFrequency, 0.0F, 1.0F),
            std::clamp(state.leftTrigger, 0.0F, 1.0F),
            std::clamp(state.rightTrigger, 0.0F, 1.0F)};
        device_->SetRumbleState(&parameters);
        return (device_->GetDeviceStatus() & GameInputDeviceConnected) != 0;
    }

    void Stop() noexcept override {
        accepting_.store(false, std::memory_order_release);
        if (device_) device_->SetRumbleState(nullptr);
        if (gameInput_) {
            for (auto* token : {&readingToken_, &deviceToken_, &guideToken_}) {
                if (*token == 0) continue;
                gameInput_->StopCallback(*token);
                gameInput_->UnregisterCallback(*token);
                *token = 0;
            }
        }
        std::unique_lock lock(callbackMutex_);
        callbackDrain_.wait(lock, [this] { return callbacksInFlight_ == 0; });
        ingress_ = nullptr;
        selectedDeviceId_ = {};
        device_.Reset();
        gameInput_.Reset();
    }

private:
    class CallbackLease final {
    public:
        explicit CallbackLease(GameInputSelectedControllerReader& owner) noexcept
            : owner_(owner), entered_(owner_.EnterCallback()) {}
        ~CallbackLease() { if (entered_) owner_.LeaveCallback(); }
        [[nodiscard]] explicit operator bool() const noexcept { return entered_; }
    private:
        GameInputSelectedControllerReader& owner_;
        bool entered_{};
    };

    bool EnterCallback() noexcept {
        std::scoped_lock lock(callbackMutex_);
        if (!accepting_.load(std::memory_order_acquire)) return false;
        ++callbacksInFlight_;
        return true;
    }

    void LeaveCallback() noexcept {
        std::scoped_lock lock(callbackMutex_);
        if (--callbacksInFlight_ == 0) callbackDrain_.notify_all();
    }

    static void CALLBACK OnReading(
        GameInputCallbackToken,
        void* context,
        IGameInputReading* reading) noexcept {
        auto& self = *static_cast<GameInputSelectedControllerReader*>(context);
        CallbackLease lease(self);
        if (!lease || !reading || !self.ingress_) return;
        GameInputGamepadState state{};
        if (!reading->GetGamepadState(&state)) return;
        (void)self.ingress_->Publish(
            ControllerReaderEventKind::Reading, reading->GetTimestamp(),
            GetTickCount64(), ConvertState(state), true);
    }

    static void CALLBACK OnDevice(
        GameInputCallbackToken,
        void* context,
        IGameInputDevice*,
        std::uint64_t timestamp,
        GameInputDeviceStatus currentStatus,
        GameInputDeviceStatus) noexcept {
        auto& self = *static_cast<GameInputSelectedControllerReader*>(context);
        CallbackLease lease(self);
        if (!lease || !self.ingress_ ||
            (currentStatus & GameInputDeviceConnected) != 0) return;
        (void)self.ingress_->Publish(
            ControllerReaderEventKind::Disconnected, timestamp,
            GetTickCount64(), {}, false);
    }

    static void CALLBACK OnGuide(
        GameInputCallbackToken,
        void* context,
        IGameInputDevice* device,
        std::uint64_t timestamp,
        GameInputSystemButtons current,
        GameInputSystemButtons previous) noexcept {
        auto& self = *static_cast<GameInputSelectedControllerReader*>(context);
        CallbackLease lease(self);
        if (!lease || !self.ingress_ || !device) return;
        const GameInputDeviceInfo* info{};
        if (FAILED(device->GetDeviceInfo(&info)) || !info ||
            std::memcmp(
                &info->deviceId, self.selectedDeviceId_.data(),
                self.selectedDeviceId_.size()) != 0) return;
        const bool currentGuide =
            (current & GameInputSystemButtonGuide) != 0;
        const bool previousGuide =
            (previous & GameInputSystemButtonGuide) != 0;
        if (currentGuide == previousGuide) return;
        (void)self.ingress_->Publish(
            currentGuide
                ? ControllerReaderEventKind::GuidePressed
                : ControllerReaderEventKind::GuideReleased,
            timestamp, GetTickCount64());
    }

    std::atomic_bool accepting_{};
    std::mutex callbackMutex_;
    std::condition_variable callbackDrain_;
    std::uint32_t callbacksInFlight_{};
    ControllerIsolationReaderIngress* ingress_{};
    std::array<std::uint8_t, 32> selectedDeviceId_{};
    ComPtr<IGameInput> gameInput_;
    ComPtr<IGameInputDevice> device_;
    GameInputCallbackToken readingToken_{};
    GameInputCallbackToken deviceToken_{};
    GameInputCallbackToken guideToken_{};
};

} // namespace

std::unique_ptr<SelectedControllerSource>
CreateGameInputSelectedControllerReader() noexcept {
    return std::unique_ptr<SelectedControllerSource>(
        new (std::nothrow) GameInputSelectedControllerReader());
}

SelectedControllerDiscoveryStatus DiscoverCurrentPhysicalController(
    const std::uint64_t enrollmentToken,
    SelectedControllerDescriptor& descriptor) noexcept {
    descriptor = {};
    if (enrollmentToken == 0)
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    ComPtr<IGameInput> gameInput;
    if (FAILED(GameInputCreate(gameInput.ReleaseAndGetAddressOf())) ||
        !gameInput) return SelectedControllerDiscoveryStatus::Unavailable;
    return DiscoverCurrentPhysicalController(
        *gameInput.Get(), enrollmentToken, descriptor);
}

} // namespace widgetrail::isolation

#endif
