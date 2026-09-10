#pragma once

#include "ControllerIsolationCore.h"
#include "ControllerIsolationReader.h"

#include <atomic>
#include <cstdint>

namespace widgetrail::isolation {

using ViGEmClientHandle = void*;
using ViGEmTargetHandle = void*;
using ViGEmFeedbackCallback = void (*)(
    void* context, std::uint8_t largeMotor,
    std::uint8_t smallMotor) noexcept;

struct ViGEmApi final {
    ViGEmClientHandle (*AllocateClient)() noexcept{};
    void (*FreeClient)(ViGEmClientHandle) noexcept{};
    std::uint32_t (*Connect)(ViGEmClientHandle) noexcept{};
    void (*Disconnect)(ViGEmClientHandle) noexcept{};
    ViGEmTargetHandle (*AllocateX360Target)() noexcept{};
    void (*FreeTarget)(ViGEmTargetHandle) noexcept{};
    std::uint32_t (*AddTarget)(
        ViGEmClientHandle, ViGEmTargetHandle) noexcept{};
    std::uint32_t (*RemoveTarget)(
        ViGEmClientHandle, ViGEmTargetHandle) noexcept{};
    std::uint32_t (*UpdateX360)(
        ViGEmClientHandle,
        ViGEmTargetHandle,
        const GamepadState&) noexcept{};
    std::uint32_t (*RegisterFeedback)(
        ViGEmClientHandle, ViGEmTargetHandle,
        ViGEmFeedbackCallback, void*) noexcept{};
    void (*UnregisterFeedback)(ViGEmTargetHandle) noexcept{};
    std::uint32_t successCode{};
    bool (*IdentifyTarget)(ViGEmTargetHandle, ControllerDeviceNodeIdentity&) noexcept{};

    [[nodiscard]] bool complete() const noexcept;
};

enum class ViGEmAdapterStatus {
    Closed,
    Ready,
    InvalidApi,
    ClientAllocationFailed,
    ConnectFailed,
    TargetAllocationFailed,
    AddTargetFailed,
    InitialNeutralFailed,
    UpdateFailed,
    RemoveTargetFailed,
    TargetIdentityUnavailable,
};

// Serialized exact-target owner. Read-only PnP correlation identifies its own
// target; all mutations still use only its acquired handle, never peer targets.
class ViGEmOutputAdapter final : public VirtualOutputEffects {
public:
    explicit ViGEmOutputAdapter(const ViGEmApi& api) noexcept;
    ~ViGEmOutputAdapter() override;

    ViGEmOutputAdapter(const ViGEmOutputAdapter&) = delete;
    ViGEmOutputAdapter& operator=(const ViGEmOutputAdapter&) = delete;

    [[nodiscard]] bool Open() noexcept;
    [[nodiscard]] bool Submit(const GamepadState& state) noexcept override;
    [[nodiscard]] bool TakeLatestFeedback(
        ControllerRumbleState& state) noexcept;
    void RemoveOwnedTarget() noexcept override;

    [[nodiscard]] ViGEmAdapterStatus status() const noexcept { return status_; }
    [[nodiscard]] std::uint32_t rawError() const noexcept { return rawError_; }
    [[nodiscard]] bool targetOwned() const noexcept {
        return target_ != nullptr && targetAdded_;
    }
    [[nodiscard]] const ControllerDeviceNodeIdentity& ownedDeviceInstance() const noexcept {
        return ownedDeviceInstance_;
    }

private:
    static constexpr std::uint32_t FeedbackClosed = 1U << 31;
    static constexpr std::uint32_t FeedbackCountMask = FeedbackClosed - 1;
    [[nodiscard]] bool Succeeded(std::uint32_t result) const noexcept;
    void CloseClient() noexcept;
    static void Feedback(
        void* context, std::uint8_t largeMotor,
        std::uint8_t smallMotor) noexcept;

    const ViGEmApi& api_;
    ViGEmClientHandle client_{};
    ViGEmTargetHandle target_{};
    bool connected_{};
    bool targetAdded_{};
    ControllerDeviceNodeIdentity ownedDeviceInstance_{};
    bool feedbackRegistered_{};
    std::atomic<std::uint64_t> feedbackSequence_{};
    std::atomic<std::uint16_t> feedbackMotors_{};
    std::atomic<std::uint32_t> feedbackGate_{FeedbackClosed};
    std::uint64_t consumedFeedbackSequence_{};
    ViGEmAdapterStatus status_{ViGEmAdapterStatus::Closed};
    std::uint32_t rawError_{};
};

#if defined(WRAIL_VIGEM_NATIVE_BACKEND)
[[nodiscard]] const ViGEmApi& OfficialViGEmApi() noexcept;
#endif

} // namespace widgetrail::isolation
