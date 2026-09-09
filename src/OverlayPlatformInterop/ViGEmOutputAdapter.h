#pragma once

#include "ControllerIsolationCore.h"

#include <cstdint>

namespace widgetrail::isolation {

using ViGEmClientHandle = void*;
using ViGEmTargetHandle = void*;

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
    std::uint32_t successCode{};

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
};

// Serialized exact-target owner. The adapter never enumerates the bus or other
// clients' targets. Its process must remain the sole owner of this object.
class ViGEmOutputAdapter final : public VirtualOutputEffects {
public:
    explicit ViGEmOutputAdapter(const ViGEmApi& api) noexcept;
    ~ViGEmOutputAdapter() override;

    ViGEmOutputAdapter(const ViGEmOutputAdapter&) = delete;
    ViGEmOutputAdapter& operator=(const ViGEmOutputAdapter&) = delete;

    [[nodiscard]] bool Open() noexcept;
    [[nodiscard]] bool Submit(const GamepadState& state) noexcept override;
    void RemoveOwnedTarget() noexcept override;

    [[nodiscard]] ViGEmAdapterStatus status() const noexcept { return status_; }
    [[nodiscard]] std::uint32_t rawError() const noexcept { return rawError_; }
    [[nodiscard]] bool targetOwned() const noexcept {
        return target_ != nullptr && targetAdded_;
    }

private:
    [[nodiscard]] bool Succeeded(std::uint32_t result) const noexcept;
    void CloseClient() noexcept;

    const ViGEmApi& api_;
    ViGEmClientHandle client_{};
    ViGEmTargetHandle target_{};
    bool connected_{};
    bool targetAdded_{};
    ViGEmAdapterStatus status_{ViGEmAdapterStatus::Closed};
    std::uint32_t rawError_{};
};

#if defined(WRAIL_VIGEM_NATIVE_BACKEND)
[[nodiscard]] const ViGEmApi& OfficialViGEmApi() noexcept;
#endif

} // namespace widgetrail::isolation
