#pragma once

#include "ControllerIsolationCore.h"

#include <cstdint>
#include <span>
#include <string>

namespace widgetrail::isolation {

[[nodiscard]] bool ControllerIsolationDosDevicePath(
    const std::wstring& absolutePath,
    std::wstring& devicePath,
    std::uint32_t& nativeError) noexcept;

[[nodiscard]] bool ParseHidHideMultiString(
    std::span<const wchar_t> characters,
    std::set<std::wstring>& values) noexcept;

enum class HidHideConfigurationStatus : std::uint8_t {
    Ready,
    DriverUnavailable,
    InvalidPayload,
    CapacityExceeded,
    ReadFailed,
    WriteFailed,
    ReadbackMismatch,
    UnsupportedInversePolicy,
    ConcurrentPolicyDrift,
    PartialMutation,
};

struct HidHideSnapshotTransaction final {
    void* context{};
    bool (*Read)(void*, HidHideSnapshot&, std::uint32_t&) noexcept{};
    bool (*WriteApplications)(
        void*, const std::set<std::wstring>&, std::uint32_t&) noexcept{};
    bool (*WriteDevices)(
        void*, const std::set<std::wstring>&, std::uint32_t&) noexcept{};
    bool (*WriteActive)(void*, bool, std::uint32_t&) noexcept{};

    [[nodiscard]] bool complete() const noexcept {
        return context && Read && WriteApplications && WriteDevices &&
            WriteActive;
    }
};

[[nodiscard]] HidHideConfigurationStatus ApplyHidHideSnapshotTransaction(
    const HidHideSnapshotTransaction& transaction,
    const HidHideSnapshot& expectedBefore,
    const HidHideSnapshot& desired,
    HidHideSnapshot& observedAfter,
    std::uint32_t& nativeError) noexcept;

// Short-lived exclusive owner of HidHide's documented WDM control device.
// Each mutation is a bounded read/plan/write/readback transaction; callers
// persist the exact delta journal before invoking WriteSnapshot.
class HidHideConfigurationAdapter final {
public:
    [[nodiscard]] HidHideConfigurationStatus ReadSnapshot(
        HidHideSnapshot& snapshot,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] HidHideConfigurationStatus ApplySnapshot(
        const HidHideSnapshot& expectedBefore,
        const HidHideSnapshot& desired,
        HidHideSnapshot& observedAfter,
        std::uint32_t& nativeError) noexcept;
};

} // namespace widgetrail::isolation
