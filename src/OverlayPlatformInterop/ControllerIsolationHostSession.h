#pragma once

#include "ControllerIsolationJournal.h"
#include "ControllerIsolationInputTransport.h"
#include "ControllerIsolationReconnect.h"

#include <windows.h>

#include <cstdint>
#include <array>
#include <filesystem>
#include <optional>
#include <string>

namespace widgetrail::isolation {

enum class ControllerIsolationCommand : std::uint32_t {
    Enable = 1,
    Status = 2,
    Disable = 3,
    Recover = 4,
};

enum class ControllerIsolationCommandStatus : std::uint32_t {
    Disabled = 0,
    Prepared = 1,
    AwaitingNeutral = 2,
    Playing = 3,
    Contained = 4,
    RecoveryRequired = 5,
    Failed = 6,
};

struct ControllerIsolationHostReading final {
    GamepadState state{};
    ControlProgress progress{ControlProgress::None};
    std::uint64_t guideEvent{};
    std::uint32_t remainingInputStates{};
};

class ControllerIsolationHostSession final {
public:
    ControllerIsolationHostSession() noexcept = default;
    ~ControllerIsolationHostSession();
    ControllerIsolationHostSession(const ControllerIsolationHostSession&) = delete;
    ControllerIsolationHostSession& operator=(
        const ControllerIsolationHostSession&) = delete;

    [[nodiscard]] bool AttachIfEnabled(std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool PrepareOverlay(std::wstring& diagnostic) noexcept;
    void CloseOverlay() noexcept;
    [[nodiscard]] bool Poll(
        ControllerIsolationHostReading& reading,
        std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool PollGuide(
        std::uint64_t& guideEvent,
        std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool active() const noexcept { return attached_; }
    [[nodiscard]] ControlProgress progress() const noexcept { return progress_; }
    void Detach() noexcept;

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    void BeginResponsePathForTest(bool interactionPipe = true) noexcept;
    [[nodiscard]] bool ApplySuccessfulResponseForTest(
        const ControlFrame& response,
        std::wstring& diagnostic) noexcept;
    [[nodiscard]] ControllerIsolationHostReading
    TakeBufferedResponseForTest() noexcept;
    [[nodiscard]] std::uint64_t interactionGenerationForTest() const noexcept;
#endif

    [[nodiscard]] static ControllerIsolationCommandStatus ExecuteCommand(
        ControllerIsolationCommand command,
        std::wstring& diagnostic) noexcept;

private:
    [[nodiscard]] bool Connect(
        const ControllerIsolationJournalRecord& record,
        std::wstring& diagnostic,
        bool controlPipe = false) noexcept;
    [[nodiscard]] bool Exchange(
        ControlMessageKind kind,
        ControlFrame& response,
        std::wstring& diagnostic,
        std::uint64_t value = 0) noexcept;
    [[nodiscard]] bool Pump(std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool IngestResponse(
        const ControlFrame& response,
        std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool ApplySuccessfulResponse(
        const ControlFrame& response,
        std::wstring& diagnostic) noexcept;
    [[nodiscard]] std::uint64_t TakeGuide() noexcept;

    ControllerIsolationPipeClient client_;
    ControllerIsolationJournalRecord record_{};
    std::uint64_t nextSequence_{1};
    ControlProgress progress_{ControlProgress::None};
    bool attached_{};
    bool interactionPipe_{};
    GamepadState latestState_{};
    ControllerIsolationHostStateQueue pendingStates_;
    std::array<std::uint64_t, 32> pendingGuideEvents_{};
    std::size_t pendingGuideHead_{};
    std::size_t pendingGuideCount_{};
};

[[nodiscard]] std::filesystem::path ControllerIsolationJournalPath() noexcept;

} // namespace widgetrail::isolation
