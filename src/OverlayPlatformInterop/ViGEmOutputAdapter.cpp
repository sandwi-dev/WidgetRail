#include "ViGEmOutputAdapter.h"

#include <chrono>
#include <cstdlib>
#include <thread>

namespace widgetrail::isolation {
namespace {

constexpr GamepadState kNeutralState{};

} // namespace

bool ViGEmApi::complete() const noexcept {
    return AllocateClient && FreeClient && Connect && Disconnect &&
        AllocateX360Target && FreeTarget && AddTarget && RemoveTarget &&
        UpdateX360 && RegisterFeedback && UnregisterFeedback;
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
    feedbackGate_.store(0, std::memory_order_release);
    rawError_ = api_.RegisterFeedback(
        client_, target_, Feedback, this);
    if (!Succeeded(rawError_)) {
        feedbackGate_.store(FeedbackClosed, std::memory_order_release);
        status_ = ViGEmAdapterStatus::AddTargetFailed;
        RemoveOwnedTarget();
        return false;
    }
    feedbackRegistered_ = true;
    rawError_ = api_.UpdateX360(client_, target_, kNeutralState);
    if (!Succeeded(rawError_)) {
        status_ = ViGEmAdapterStatus::InitialNeutralFailed;
        RemoveOwnedTarget();
        return false;
    }
    status_ = ViGEmAdapterStatus::Ready;
    return true;
}

bool ViGEmOutputAdapter::TakeLatestFeedback(
    ControllerRumbleState& state) noexcept {
    const auto sequence = feedbackSequence_.load(std::memory_order_acquire);
    if (sequence == consumedFeedbackSequence_) return false;
    const auto motors = feedbackMotors_.load(std::memory_order_acquire);
    consumedFeedbackSequence_ = sequence;
    state = {
        static_cast<float>(motors & 0xFFU) / 255.0F,
        static_cast<float>((motors >> 8) & 0xFFU) / 255.0F,
        0.0F, 0.0F};
    return true;
}

void ViGEmOutputAdapter::Feedback(
    void* context, const std::uint8_t largeMotor,
    const std::uint8_t smallMotor) noexcept {
    auto* self = static_cast<ViGEmOutputAdapter*>(context);
    if (!self) return;
    auto gate = self->feedbackGate_.load(std::memory_order_acquire);
    for (;;) {
        if ((gate & FeedbackClosed) != 0 ||
            (gate & FeedbackCountMask) == FeedbackCountMask) return;
        if (self->feedbackGate_.compare_exchange_weak(
                gate, gate + 1, std::memory_order_acq_rel,
                std::memory_order_acquire)) break;
    }
    self->feedbackMotors_.store(
        static_cast<std::uint16_t>(largeMotor) |
            (static_cast<std::uint16_t>(smallMotor) << 8),
        std::memory_order_relaxed);
    self->feedbackSequence_.fetch_add(1, std::memory_order_release);
    self->feedbackGate_.fetch_sub(1, std::memory_order_release);
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
        feedbackGate_.fetch_or(FeedbackClosed, std::memory_order_acq_rel);
        if (feedbackRegistered_) api_.UnregisterFeedback(target_);
        feedbackRegistered_ = false;
        const auto callbackDeadline =
            std::chrono::steady_clock::now() + std::chrono::seconds(1);
        while ((feedbackGate_.load(std::memory_order_acquire) &
                FeedbackCountMask) != 0) {
            if (std::chrono::steady_clock::now() >= callbackDeadline)
                std::abort();
            std::this_thread::yield();
        }
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

std::atomic<ViGEmFeedbackCallback> gFeedbackCallback{};
std::atomic<void*> gFeedbackContext{};
std::atomic<ViGEmTargetHandle> gFeedbackTarget{};

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

void CALLBACK NativeFeedback(
    PVIGEM_CLIENT,
    PVIGEM_TARGET target,
    UCHAR largeMotor,
    UCHAR smallMotor,
    UCHAR) noexcept {
    if (gFeedbackTarget.load(std::memory_order_acquire) != target) return;
    const auto callback = gFeedbackCallback.load(std::memory_order_acquire);
    if (callback) callback(
        gFeedbackContext.load(std::memory_order_acquire),
        largeMotor, smallMotor);
}

std::uint32_t NativeRegisterFeedback(
    ViGEmClientHandle client,
    ViGEmTargetHandle target,
    ViGEmFeedbackCallback callback,
    void* context) noexcept {
    ViGEmTargetHandle expected{};
    if (!callback || !gFeedbackTarget.compare_exchange_strong(
            expected, target, std::memory_order_acq_rel,
            std::memory_order_acquire))
        return static_cast<std::uint32_t>(VIGEM_ERROR_BUS_ACCESS_FAILED);
    gFeedbackContext.store(context, std::memory_order_release);
    gFeedbackCallback.store(callback, std::memory_order_release);
    const auto result = vigem_target_x360_register_notification(
        static_cast<PVIGEM_CLIENT>(client),
        static_cast<PVIGEM_TARGET>(target), NativeFeedback);
    if (result != VIGEM_ERROR_NONE) {
        gFeedbackCallback.store(nullptr, std::memory_order_release);
        gFeedbackContext.store(nullptr, std::memory_order_release);
        gFeedbackTarget.store(nullptr, std::memory_order_release);
    }
    return result;
}

void NativeUnregisterFeedback(ViGEmTargetHandle target) noexcept {
    if (gFeedbackTarget.load(std::memory_order_acquire) != target) return;
    vigem_target_x360_unregister_notification(
        static_cast<PVIGEM_TARGET>(target));
    gFeedbackCallback.store(nullptr, std::memory_order_release);
    gFeedbackContext.store(nullptr, std::memory_order_release);
    gFeedbackTarget.store(nullptr, std::memory_order_release);
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
        NativeRegisterFeedback,
        NativeUnregisterFeedback,
        VIGEM_ERROR_NONE,
    };
    return api;
}

} // namespace widgetrail::isolation

#endif
