#pragma once

#include "ControllerIsolationCore.h"
#include "ControllerIsolationProtocol.h"

#include <windows.h>

#include <array>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>

namespace widgetrail::isolation {

class ControllerIsolationLifecycleLease final {
public:
    ControllerIsolationLifecycleLease() noexcept = default;
    ~ControllerIsolationLifecycleLease();
    ControllerIsolationLifecycleLease(
        const ControllerIsolationLifecycleLease&) = delete;
    ControllerIsolationLifecycleLease& operator=(
        const ControllerIsolationLifecycleLease&) = delete;

    [[nodiscard]] bool Acquire(
        DWORD timeoutMilliseconds,
        std::uint32_t& nativeError) noexcept;
    void Release() noexcept;
    [[nodiscard]] bool owned() const noexcept { return owned_; }

private:
    HANDLE mutex_{};
    bool owned_{};
};

enum class ControllerIsolationJournalPhase : std::uint32_t {
    Prepared = 1,
    PolicyPlanned = 2,
    Playing = 3,
    RecoveryRequired = 4,
};

struct ControllerIsolationJournalRecord final {
    static constexpr std::uint32_t Version = 3;
    RoutingAuthority authority{};
    ControllerIsolationNonce nonce{};
    std::uint32_t hostProcessId{};
    std::uint64_t hostCreationTime{};
    std::uint32_t guardianProcessId{};
    std::uint64_t guardianCreationTime{};
    std::uint32_t workerProcessId{};
    std::uint64_t workerCreationTime{};
    std::wstring hostPath;
    std::array<std::uint8_t, 32> hostSha256{};
    std::wstring guardianPath;
    std::array<std::uint8_t, 32> guardianSha256{};
    std::wstring workerPath;
    std::array<std::uint8_t, 32> workerSha256{};
    HidHideJournal policy;
    ControllerIsolationJournalPhase phase{
        ControllerIsolationJournalPhase::Prepared};

    [[nodiscard]] bool valid() const noexcept;
};

[[nodiscard]] bool ControllerIsolationFileSha256(
    const std::filesystem::path& path,
    std::array<std::uint8_t, 32>& digest,
    std::uint32_t& nativeError) noexcept;

class ControllerIsolationJournalStore final {
public:
    explicit ControllerIsolationJournalStore(std::filesystem::path path)
        : path_(std::move(path)) {}

    [[nodiscard]] bool SaveAtomic(
        const ControllerIsolationJournalRecord& record,
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] bool SaveAtomicIfAbsent(
        const ControllerIsolationJournalRecord& record,
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] bool SaveAtomicIfCurrent(
        const ControllerIsolationJournalRecord& expected,
        const ControllerIsolationJournalRecord& replacement,
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] bool Matches(
        const ControllerIsolationJournalRecord& expected,
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] bool RemoveIfCurrent(
        const ControllerIsolationJournalRecord& expected,
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] std::optional<ControllerIsolationJournalRecord> Load(
        std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] bool Remove(std::uint32_t& nativeError) const noexcept;
    [[nodiscard]] const std::filesystem::path& path() const noexcept {
        return path_;
    }

private:
    std::filesystem::path path_;
};

} // namespace widgetrail::isolation
