#pragma once

#include <windows.h>

#include <cstdint>
#include <filesystem>

#include "ControllerIsolationJournal.h"

namespace widgetrail::isolation {

enum class GuardianLaunchStatus : std::uint8_t {
    Started,
    InvalidInput,
    ParentJobForbidsBreakaway,
    CreateFailed,
};

enum class GuardianLifetimeStatus : std::uint8_t {
    Running,
    Exited,
    IdentityMismatch,
    Unavailable,
};

[[nodiscard]] constexpr GuardianLifetimeStatus ClassifyGuardianLifetime(
    const bool persistedIdentity,
    const bool processOpened,
    const std::uint32_t openError,
    const bool exactIdentity,
    const std::uint32_t waitResult) noexcept {
    if (!persistedIdentity) return GuardianLifetimeStatus::Unavailable;
    if (!processOpened) return openError == ERROR_INVALID_PARAMETER
        ? GuardianLifetimeStatus::Exited
        : GuardianLifetimeStatus::Unavailable;
    if (!exactIdentity) return GuardianLifetimeStatus::IdentityMismatch;
    if (waitResult == WAIT_OBJECT_0) return GuardianLifetimeStatus::Exited;
    if (waitResult == WAIT_TIMEOUT) return GuardianLifetimeStatus::Running;
    return GuardianLifetimeStatus::Unavailable;
}

[[nodiscard]] GuardianLaunchStatus LaunchIndependentIsolationGuardian(
    const std::filesystem::path& executable,
    const std::filesystem::path& journal,
    std::uint32_t& processId,
    std::uint32_t& nativeError) noexcept;

[[nodiscard]] GuardianLifetimeStatus ObserveExactIsolationGuardian(
    const ControllerIsolationJournalRecord& record,
    std::uint32_t& nativeError) noexcept;

} // namespace widgetrail::isolation
