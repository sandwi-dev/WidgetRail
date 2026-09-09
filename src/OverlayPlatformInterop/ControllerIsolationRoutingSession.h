#pragma once

#include "ControllerIsolationCore.h"
#include "ControllerIsolationReader.h"

#include <cstdint>
#include <optional>

namespace widgetrail::isolation {

class ControllerIsolationOutput : public VirtualOutputEffects {
public:
    [[nodiscard]] virtual bool OpenOwnedTarget() noexcept = 0;
};

class ControllerIsolationGuideSink {
public:
    virtual ~ControllerIsolationGuideSink() = default;
    [[nodiscard]] virtual bool PublishGuide(
        bool pressed,
        std::uint64_t sourceTimestampMicroseconds,
        std::uint64_t ingressOrdinal) noexcept = 0;
};

enum class ControllerIsolationRoutingState : std::uint8_t {
    Disabled,
    PreparedNeutral,
    AwaitingPlaying,
    Playing,
    OverlayInteraction,
    Fault,
};

enum class ControllerIsolationRoutingResult : std::uint8_t {
    Applied,
    Waiting,
    RejectedAuthority,
    RejectedState,
    ReaderUnavailable,
    OutputUnavailable,
    Faulted,
};

// Thin serialized execution owner. One worker routing thread calls every
// method; callbacks only publish fixed events to readerIngress(). Mode,
// neutral and barrier behavior remains owned by ControllerIsolationCore.
class ControllerIsolationRoutingSession final {
public:
    ControllerIsolationRoutingSession(
        SelectedControllerSource& source,
        ControllerIsolationOutput& output,
        ControllerIsolationGuideSink& guideSink,
        RoutingBudgets budgets = {}) noexcept;
    ~ControllerIsolationRoutingSession();

    [[nodiscard]] ControllerIsolationReaderIngress& readerIngress() noexcept {
        return ingress_;
    }
    [[nodiscard]] ControllerIsolationRoutingResult PrepareSession(
        const RoutingAuthority& authority,
        const SelectedControllerEnrollment& enrollment,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult CommitPlaying(
        const RoutingAuthority& authority,
        std::uint64_t measuredP99ReadingIntervalMilliseconds,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult EnterOverlay(
        const RoutingAuthority& authority,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult CloseOverlay(
        const RoutingAuthority& authority,
        std::uint64_t measuredP99ReadingIntervalMilliseconds,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult Heartbeat(
        const RoutingAuthority& authority,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult Pump(
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ControllerIsolationRoutingResult Stop(
        const RoutingAuthority& authority) noexcept;

    [[nodiscard]] ControllerIsolationRoutingState state() const noexcept {
        return state_;
    }
    [[nodiscard]] RoutingFault fault() const noexcept { return core_.fault(); }

private:
    [[nodiscard]] std::optional<DeviceReading> MapCurrent(
        const SelectedControllerCurrent& current) noexcept;
    [[nodiscard]] std::optional<DeviceReading> MapEvent(
        const ControllerReaderEvent& event) noexcept;
    [[nodiscard]] bool DrainIngress() noexcept;
    [[nodiscard]] bool DrainCore(std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] bool ExactAuthority(
        const RoutingAuthority& authority) const noexcept;
    [[nodiscard]] SelectedDeviceIdentity CoreDeviceIdentity(
        const SelectedControllerEnrollment& enrollment) const;
    void Fail() noexcept;

    SelectedControllerSource& source_;
    ControllerIsolationOutput& output_;
    ControllerIsolationGuideSink& guideSink_;
    ControllerIsolationReaderIngress ingress_;
    ControllerIsolationCore core_;
    RoutingAuthority authority_{};
    SelectedControllerEnrollment enrollment_{};
    ControllerIsolationRoutingState state_{
        ControllerIsolationRoutingState::Disabled};
    std::optional<std::uint64_t> lastSourceTimestamp_;
    std::optional<GamepadState> lastSourceState_;
    bool lastSourceConnected_{};
    std::uint64_t lastReadingOrdinal_{};
    bool outputOwned_{};
};

} // namespace widgetrail::isolation
