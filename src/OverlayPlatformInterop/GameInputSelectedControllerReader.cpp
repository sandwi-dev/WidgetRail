#include "ControllerIsolationReader.h"

#if defined(WRAIL_GAMEINPUT_ISOLATION_READER)

#include <windows.h>

#include <GameInput.h>
#include <bcrypt.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <devpkey.h>
#include <hidsdi.h>
#include <hidpi.h>
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
#include <vector>

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

    bool ReadNodeService(
        const ControllerDeviceNodeToken node,
        ControllerDeviceNodeIdentity& service) noexcept override {
        service = {};
        DEVPROPTYPE type{};
        ULONG bytes = static_cast<ULONG>(service.value.size() * sizeof(wchar_t));
        const auto result = CM_Get_DevNode_PropertyW(
            static_cast<DEVINST>(node), &DEVPKEY_Device_Service, &type,
            reinterpret_cast<PBYTE>(service.value.data()), &bytes, 0);
        if (result == CR_NO_SUCH_VALUE) { service = {}; return true; }
        if (result != CR_SUCCESS || type != DEVPROP_TYPE_STRING ||
            bytes < sizeof(wchar_t) || bytes > service.value.size() * sizeof(wchar_t) ||
            bytes % sizeof(wchar_t) != 0) return false;
        service.length = bytes / sizeof(wchar_t) - 1;
        if (service.value[service.length] != L'\0') return false;
        return service.length == 0 || service.valid();
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
        const std::uint64_t value, const SelectedControllerEnrollment* expected = nullptr) noexcept
        : enrollmentToken(value), requested(expected) {}

    std::uint64_t enrollmentToken{};
    std::mutex mutex;
    SelectedControllerDiscovery discovery{true};
    const SelectedControllerEnrollment* requested{};
    const ControllerDeviceNodeIdentity* ownedOutput{};
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
                *normalized, backend, state.ownedOutput);
            if (ancestry == ControllerDeviceAncestry::KnownVirtualOutput ||
                ancestry == ControllerDeviceAncestry::OwnedVirtualOutput) {
                candidateKind =
                    SelectedControllerCandidateKind::KnownVirtualOutput;
            } else if (ancestry == ControllerDeviceAncestry::SoftwareEnumerated) {
                candidateKind = SelectedControllerCandidateKind::SoftwareEnumerated;
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
    if (state.requested && (candidateKind != SelectedControllerCandidateKind::Physical ||
        !SameStableControllerIdentity(*state.requested, descriptor.enrollment))) return;
    if (candidateKind == SelectedControllerCandidateKind::Physical &&
        descriptor.valid() &&
        (!state.selectedDevice || descriptor.deviceInstanceId.view() <= state.selectedDescriptor.deviceInstanceId.view())) {
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
    ComPtr<IGameInputDevice>* const selectedDevice = nullptr,
    const SelectedControllerEnrollment* const requested = nullptr,
    const ControllerDeviceNodeIdentity* const ownedOutput = nullptr) noexcept {
    descriptor = {};
    if (selectedDevice) selectedDevice->Reset();
    if (enrollmentToken == 0)
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    PhysicalControllerDiscoveryContext context(enrollmentToken, requested);
    context.ownedOutput = ownedOutput;
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
    GameInputSelectedControllerReader() noexcept {
        const auto createResult =
            GameInputCreate(gameInput_.ReleaseAndGetAddressOf());
        if (FAILED(createResult) || !gameInput_) return;
        gameInput_->SetFocusPolicy(GameInputEnableBackgroundInput);
    }

    ~GameInputSelectedControllerReader() override { Stop(); }

    SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& ingress,
        SelectedControllerEnrollment& preparedEnrollment) noexcept override {
        preparedEnrollment = {};
        if (!enrollment.valid())
            return SelectedControllerPrepareStatus::InvalidEnrollment;
        if (!gameInput_ || device_)
            return SelectedControllerPrepareStatus::Unavailable;
        SelectedControllerDescriptor localDescriptor;
        const auto discovery = DiscoverCurrentPhysicalController(
            *gameInput_.Get(), enrollment.enrollmentToken, localDescriptor,
            &device_, &enrollment);
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
            (ancestry != ControllerDeviceAncestry::Physical &&
             ancestry != ControllerDeviceAncestry::KnownVirtualOutput)) {
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
        ingress_ = &ingress;
        accepting_.store(true, std::memory_order_release);
        const auto readingResult = gameInput_->RegisterReadingCallback(
                device_.Get(), GameInputKindGamepad, this, OnReading,
                &readingToken_);
        if (FAILED(readingResult)) {
            Stop();
            return SelectedControllerPrepareStatus::CallbackRegistrationFailed;
        }
        const auto deviceResult = gameInput_->RegisterDeviceCallback(
                device_.Get(), GameInputKindGamepad,
                GameInputDeviceConnected, GameInputNoEnumeration,
                this, OnDevice, &deviceToken_);
        if (FAILED(deviceResult)) {
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
        // Although the SDK marks this pointer optional, GameInputRedist
        // 3.3.221 dereferences null while forwarding a stop report. Supply an
        // explicit all-motors-off report before retiring the selected device.
        const GameInputRumbleParams stoppedRumble{};
        if (device_) device_->SetRumbleState(&stoppedRumble);
        if (gameInput_) {
            for (auto* token : {&readingToken_, &deviceToken_}) {
                if (*token == 0) continue;
                gameInput_->StopCallback(*token);
                gameInput_->UnregisterCallback(*token);
                *token = 0;
            }
        }
        std::unique_lock lock(callbackMutex_);
        callbackDrain_.wait(lock, [this] { return callbacksInFlight_ == 0; });
        ingress_ = nullptr;
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

    std::atomic_bool accepting_{};
    std::mutex callbackMutex_;
    std::condition_variable callbackDrain_;
    std::uint32_t callbacksInFlight_{};
    ControllerIsolationReaderIngress* ingress_{};
    ComPtr<IGameInput> gameInput_;
    ComPtr<IGameInputDevice> device_;
    GameInputCallbackToken readingToken_{};
    GameInputCallbackToken deviceToken_{};
};

} // namespace

std::unique_ptr<SelectedControllerSource>
CreateGameInputSelectedControllerReader() noexcept {
    return std::unique_ptr<SelectedControllerSource>(
        new (std::nothrow) GameInputSelectedControllerReader());
}

SelectedControllerDiscoveryStatus DiscoverCurrentPhysicalController(
    const std::uint64_t enrollmentToken,
    SelectedControllerDescriptor& descriptor,
    const ControllerDeviceNodeIdentity* ownedOutput) noexcept {
    descriptor = {};
    if (enrollmentToken == 0)
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    ComPtr<IGameInput> gameInput;
    if (FAILED(GameInputCreate(gameInput.ReleaseAndGetAddressOf())) ||
        !gameInput) return SelectedControllerDiscoveryStatus::Unavailable;
    return DiscoverCurrentPhysicalController(
        *gameInput.Get(), enrollmentToken, descriptor, nullptr, nullptr, ownedOutput);
}

bool ResolveViGEmOwnedTarget(const std::uint32_t targetIndex,
    ControllerDeviceNodeIdentity& identity) noexcept {
    identity = {};
    if (targetIndex == 0) return false;
    // ViGEm exposes its live target serial as the PDO address. Correlate only
    // beneath a unique present ViGEmBus instance; neither VID/PID nor a name
    // identifies a target. Ambiguous/missing metadata must not assert ownership.
    constexpr ULONG flags = CM_GETIDLIST_FILTER_SERVICE | CM_GETIDLIST_FILTER_PRESENT;
    ULONG characters{};
    if (CM_Get_Device_ID_List_SizeW(&characters, L"ViGEmBus", flags) != CR_SUCCESS ||
        characters < 2 || characters > 16'384) return false;
    std::array<wchar_t, 16'384> buses{};
    if (CM_Get_Device_ID_ListW(L"ViGEmBus", buses.data(), characters, flags) != CR_SUCCESS)
        return false;
    std::size_t firstLength{};
    while (firstLength < characters && buses[firstLength] != L'\0') ++firstLength;
    if (firstLength == 0 || firstLength + 1 >= characters || buses[firstLength + 1] != L'\0')
        return false;
    DEVINST bus{}, child{};
    if (CM_Locate_DevNodeW(&bus, buses.data(), CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS ||
        CM_Get_Child(&child, bus, 0) != CR_SUCCESS) return false;
    CfgMgrDeviceAncestryBackend backend;
    OwnedControllerTargetDiscovery discovery(targetIndex);
    for (unsigned count = 0; count < 256; ++count) {
        ULONG status{}, problem{};
        if (CM_Get_DevNode_Status(&status, &problem, child, 0) != CR_SUCCESS) return false;
        const bool removing = (status & DN_WILL_BE_REMOVED) != 0;
        ULONG address{};
        ULONG bytes = sizeof(address);
        DEVPROPTYPE type{};
        const bool addressKnown = !removing && CM_Get_DevNode_PropertyW(child, &DEVPKEY_Device_Address, &type,
                reinterpret_cast<PBYTE>(&address), &bytes, 0) == CR_SUCCESS &&
            type == DEVPROP_TYPE_UINT32 && bytes == sizeof(address);
        ControllerDeviceNodeIdentity candidateIdentity;
        if (addressKnown && address == targetIndex && !backend.ReadNodeIdentity(child, candidateIdentity)) return false;
        discovery.Observe(removing, addressKnown ? std::optional<std::uint32_t>{address} : std::nullopt, candidateIdentity);
        DEVINST sibling{};
        const auto result = CM_Get_Sibling(&sibling, child, 0);
        if (result == CR_NO_SUCH_DEVNODE) {
            return discovery.Resolve(identity);
        }
        if (result != CR_SUCCESS || sibling == child) return false;
        child = sibling;
    }
    return false;
}

bool DiscoverSelectedControllerHideTargets(
    const ControllerDeviceNodeIdentity& selected,
    std::set<std::wstring>& targets) noexcept {
    targets.clear();
    try {
        CfgMgrDeviceAncestryBackend backend;
        ControllerDeviceNodeToken selectedNode{};
        if (!selected.valid() || !backend.LocateNode(selected, selectedNode)) return false;
        std::set<std::wstring> resolved{std::wstring(selected.view())};
        GUID hid{}; HidD_GetHidGuid(&hid);
        ULONG length{};
        if (CM_Get_Device_Interface_List_SizeW(&length, &hid, nullptr,
                CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || length == 0 || length > 65'536)
            return false;
        std::vector<wchar_t> paths(length);
        if (CM_Get_Device_Interface_ListW(&hid, nullptr, paths.data(), length,
                CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || paths.back() != L'\0')
            return false;
        for (std::size_t offset = 0; offset < paths.size() && paths[offset] != L'\0';) {
            const auto end = std::find(paths.begin() + offset, paths.end(), L'\0');
            if (end == paths.end()) return false;
            const auto count = static_cast<std::size_t>(end - paths.begin()) - offset;
            const auto* path = paths.data() + offset;
            offset += count + 1;
            ControllerDeviceNodeIdentity identity;
            ControllerDeviceNodeToken node{};
            if (!backend.ResolveInterfaceInstanceId({path, count}, identity) || !backend.LocateNode(identity, node))
                return false;
            const auto related = IsSelectedControllerDescendant(node, selectedNode, backend);
            if (!related) return false;
            if (!*related || resolved.contains(std::wstring(identity.view()))) continue;
            // An Xbox composite device can have a separate DirectInput/HID
            // gamepad collection. Hide only gamepad/joystick collections in its
            // exact subtree, never sibling controllers or keyboard/mouse nodes.
            const auto handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
            if (handle == INVALID_HANDLE_VALUE) return false;
            PHIDP_PREPARSED_DATA data{};
            HIDP_CAPS caps{};
            const bool described = HidD_GetPreparsedData(handle, &data) &&
                HidP_GetCaps(data, &caps) == HIDP_STATUS_SUCCESS;
            if (data) HidD_FreePreparsedData(data);
            CloseHandle(handle);
            if (!described) return false;
            if (IsControllerHidUsage(caps.UsagePage, caps.Usage)) {
                ControllerDeviceNodeIdentity current;
                if (!backend.ReadNodeIdentity(node, current) ||
                    _wcsicmp(current.value.data(), identity.value.data()) != 0) return false;
                resolved.insert(std::wstring(identity.view()));
                if (resolved.size() > 16) return false;
            }
        }
        ControllerDeviceNodeIdentity current;
        if (!backend.ReadNodeIdentity(selectedNode, current) ||
            _wcsicmp(current.value.data(), selected.value.data()) != 0) return false;
        targets = std::move(resolved);
        return true;
    } catch (...) { return false; }
}

} // namespace widgetrail::isolation

#endif
