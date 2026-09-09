#include "ViGEmOutputAdapter.h"

namespace widgetrail::isolation {
namespace {

constexpr GamepadState kNeutralState{};

} // namespace

bool ViGEmApi::complete() const noexcept {
    return AllocateClient && FreeClient && Connect && Disconnect &&
        AllocateX360Target && FreeTarget && AddTarget && RemoveTarget &&
        UpdateX360;
}

ViGEmOutputAdapter::ViGEmOutputAdapter(const ViGEmApi& api) noexcept
    : api_(api) {}

ViGEmOutputAdapter::~ViGEmOutputAdapter() {
    RemoveOwnedTarget();
}

bool ViGEmOutputAdapter::Open() noexcept {
    if (status_ != ViGEmAdapterStatus::Closed || client_ || target_ ||
        !api_.complete()) {
        status_ = ViGEmAdapterStatus::InvalidApi;
        return false;
    }
    client_ = api_.AllocateClient();
    if (!client_) {
        status_ = ViGEmAdapterStatus::ClientAllocationFailed;
        return false;
    }
    rawError_ = api_.Connect(client_);
    if (!Succeeded(rawError_)) {
        status_ = ViGEmAdapterStatus::ConnectFailed;
        CloseClient();
        return false;
    }
    connected_ = true;
    target_ = api_.AllocateX360Target();
    if (!target_) {
        status_ = ViGEmAdapterStatus::TargetAllocationFailed;
        CloseClient();
        return false;
    }
    rawError_ = api_.AddTarget(client_, target_);
    if (!Succeeded(rawError_)) {
        status_ = ViGEmAdapterStatus::AddTargetFailed;
        api_.FreeTarget(target_);
        target_ = nullptr;
        CloseClient();
        return false;
    }
    targetAdded_ = true;
    rawError_ = api_.UpdateX360(client_, target_, kNeutralState);
    if (!Succeeded(rawError_)) {
        status_ = ViGEmAdapterStatus::InitialNeutralFailed;
        RemoveOwnedTarget();
        return false;
    }
    status_ = ViGEmAdapterStatus::Ready;
    return true;
}

bool ViGEmOutputAdapter::Submit(const GamepadState& state) noexcept {
    if (status_ != ViGEmAdapterStatus::Ready || !client_ || !target_ ||
        !targetAdded_) {
        return false;
    }
    rawError_ = api_.UpdateX360(client_, target_, state);
    if (Succeeded(rawError_)) return true;
    status_ = ViGEmAdapterStatus::UpdateFailed;
    return false;
}

void ViGEmOutputAdapter::RemoveOwnedTarget() noexcept {
    const auto priorStatus = status_;
    ViGEmAdapterStatus removalStatus = status_;
    if (target_) {
        if (targetAdded_ && client_) {
            rawError_ = api_.RemoveTarget(client_, target_);
            if (!Succeeded(rawError_))
                removalStatus = ViGEmAdapterStatus::RemoveTargetFailed;
        }
        targetAdded_ = false;
        api_.FreeTarget(target_);
        target_ = nullptr;
    }
    CloseClient();
    status_ = removalStatus == ViGEmAdapterStatus::RemoveTargetFailed
        ? removalStatus
        : (priorStatus == ViGEmAdapterStatus::Ready
              ? ViGEmAdapterStatus::Closed
              : priorStatus);
}

bool ViGEmOutputAdapter::Succeeded(const std::uint32_t result) const noexcept {
    return result == api_.successCode;
}

void ViGEmOutputAdapter::CloseClient() noexcept {
    if (!client_) return;
    if (connected_) api_.Disconnect(client_);
    connected_ = false;
    api_.FreeClient(client_);
    client_ = nullptr;
}

} // namespace widgetrail::isolation

#if defined(WRAIL_VIGEM_NATIVE_BACKEND)

#include <windows.h>

#include "../../third_party/ViGEmClient/include/ViGEmClient.h"

namespace widgetrail::isolation {
namespace {

ViGEmClientHandle NativeAllocateClient() noexcept { return vigem_alloc(); }
void NativeFreeClient(ViGEmClientHandle client) noexcept {
    vigem_free(static_cast<PVIGEM_CLIENT>(client));
}
std::uint32_t NativeConnect(ViGEmClientHandle client) noexcept {
    return vigem_connect(static_cast<PVIGEM_CLIENT>(client));
}
void NativeDisconnect(ViGEmClientHandle client) noexcept {
    vigem_disconnect(static_cast<PVIGEM_CLIENT>(client));
}
ViGEmTargetHandle NativeAllocateTarget() noexcept {
    return vigem_target_x360_alloc();
}
void NativeFreeTarget(ViGEmTargetHandle target) noexcept {
    vigem_target_free(static_cast<PVIGEM_TARGET>(target));
}
std::uint32_t NativeAddTarget(
    ViGEmClientHandle client,
    ViGEmTargetHandle target) noexcept {
    return vigem_target_add(
        static_cast<PVIGEM_CLIENT>(client),
        static_cast<PVIGEM_TARGET>(target));
}
std::uint32_t NativeRemoveTarget(
    ViGEmClientHandle client,
    ViGEmTargetHandle target) noexcept {
    return vigem_target_remove(
        static_cast<PVIGEM_CLIENT>(client),
        static_cast<PVIGEM_TARGET>(target));
}
std::uint32_t NativeUpdateX360(
    ViGEmClientHandle client,
    ViGEmTargetHandle target,
    const GamepadState& state) noexcept {
    XUSB_REPORT report{};
    report.wButtons = state.buttons;
    report.bLeftTrigger = state.leftTrigger;
    report.bRightTrigger = state.rightTrigger;
    report.sThumbLX = state.leftThumbX;
    report.sThumbLY = state.leftThumbY;
    report.sThumbRX = state.rightThumbX;
    report.sThumbRY = state.rightThumbY;
    return vigem_target_x360_update(
        static_cast<PVIGEM_CLIENT>(client),
        static_cast<PVIGEM_TARGET>(target), report);
}

} // namespace

const ViGEmApi& OfficialViGEmApi() noexcept {
    static const ViGEmApi api{
        NativeAllocateClient,
        NativeFreeClient,
        NativeConnect,
        NativeDisconnect,
        NativeAllocateTarget,
        NativeFreeTarget,
        NativeAddTarget,
        NativeRemoveTarget,
        NativeUpdateX360,
        VIGEM_ERROR_NONE,
    };
    return api;
}

} // namespace widgetrail::isolation

#endif
