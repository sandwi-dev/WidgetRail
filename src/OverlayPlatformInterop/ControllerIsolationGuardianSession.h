#pragma once

#include "ControllerIsolationJournal.h"

#include <windows.h>

#include <filesystem>

namespace widgetrail::isolation {

enum class GuardianStartupDisposition : std::uint8_t {
    Serve,
    RecoveryRequired,
};

enum class AdmittedHostLifetime : std::uint8_t {
    Running,
    Exited,
    Unavailable,
};

[[nodiscard]] inline AdmittedHostLifetime ObserveAdmittedHostLifetime(
    const HANDLE process) noexcept {
    if (!process) return AdmittedHostLifetime::Unavailable;
    const auto wait = WaitForSingleObject(process, 0);
    if (wait == WAIT_OBJECT_0) return AdmittedHostLifetime::Exited;
    if (wait == WAIT_TIMEOUT) return AdmittedHostLifetime::Running;
    return AdmittedHostLifetime::Unavailable;
}

[[nodiscard]] constexpr GuardianStartupDisposition
DecideGuardianStartupDisposition(
    const ControllerIsolationJournalPhase loadedPhase,
    const bool startSucceeded,
    const ControllerIsolationJournalPhase resultingPhase) noexcept {
    return loadedPhase == ControllerIsolationJournalPhase::Prepared &&
            startSucceeded &&
            resultingPhase == ControllerIsolationJournalPhase::Playing
        ? GuardianStartupDisposition::Serve
        : GuardianStartupDisposition::RecoveryRequired;
}

[[nodiscard]] int RunControllerIsolationGuardianSession(
    const std::filesystem::path& journalPath) noexcept;

} // namespace widgetrail::isolation
