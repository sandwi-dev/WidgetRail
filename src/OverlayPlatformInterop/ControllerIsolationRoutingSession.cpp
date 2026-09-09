#include "ControllerIsolationRoutingSession.h"

#include <algorithm>
#include <limits>
#include <string>

#include <windows.h>

namespace widgetrail::isolation {
namespace {

template <std::size_t Size>
[[nodiscard]] std::wstring Hex(
    const std::array<std::uint8_t, Size>& value) {
    constexpr wchar_t digits[] = L"0123456789abcdef";
    std::wstring result;
    result.reserve(value.size() * 2);
    for (const auto byte : value) {
        result.push_back(digits[byte >> 4]);
        result.push_back(digits[byte & 0x0F]);
    }
    return result;
}

[[nodiscard]] ControllerIsolationRoutingResult Convert(
    const CommandResult result) noexcept {
    switch (result) {
    case CommandResult::Applied:
    case CommandResult::Resumed:
        return ControllerIsolationRoutingResult::Applied;
    case CommandResult::Waiting:
        return ControllerIsolationRoutingResult::Waiting;
    case CommandResult::RejectedAuthority:
        return ControllerIsolationRoutingResult::RejectedAuthority;
    case CommandResult::RejectedState:
        return ControllerIsolationRoutingResult::RejectedState;
    case CommandResult::Faulted:
        return ControllerIsolationRoutingResult::Faulted;
    }
    return ControllerIsolationRoutingResult::Faulted;
}

} // namespace

ControllerIsolationRoutingSession::ControllerIsolationRoutingSession(
    SelectedControllerSource& source,
    ControllerIsolationOutput& output,
    ControllerIsolationGuideSink& guideSink,
    RoutingBudgets budgets) noexcept
    : source_(source), output_(output), guideSink_(guideSink),
      core_(output, budgets) {}

ControllerIsolationRoutingSession::~ControllerIsolationRoutingSession() {
    if (authority_.valid()) (void)Stop(authority_);
}

ControllerIsolationRoutingResult
ControllerIsolationRoutingSession::PrepareSession(
    const RoutingAuthority& authority,
    const SelectedControllerEnrollment& enrollment,
    const std::uint64_t nowMilliseconds) noexcept {
    if (state_ != ControllerIsolationRoutingState::Disabled)
        return ControllerIsolationRoutingResult::RejectedState;
    if (!authority.valid() || !enrollment.valid())
        return ControllerIsolationRoutingResult::RejectedAuthority;
    authority_ = authority;
    enrollment_ = enrollment;
    if (!ingress_.Open(authority, enrollment.enrollmentToken)) {
        Fail();
        return ControllerIsolationRoutingResult::ReaderUnavailable;
    }
    const auto prepared = source_.Prepare(enrollment, ingress_);
    if (prepared != SelectedControllerPrepareStatus::Ready) {
        Fail();
        return ControllerIsolationRoutingResult::ReaderUnavailable;
    }
    SelectedControllerCurrent current;
    if (!source_.SampleCurrent(current)) {
        Fail();
        return ControllerIsolationRoutingResult::ReaderUnavailable;
    }
    const auto initial = MapCurrent(current);
    if (!initial) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    if (!output_.OpenOwnedTarget()) {
        Fail();
        return ControllerIsolationRoutingResult::OutputUnavailable;
    }
    outputOwned_ = true;
    CommandResult begun{CommandResult::Faulted};
    try {
        const auto device = CoreDeviceIdentity(enrollment);
        begun = core_.BeginSession(authority, device, *initial, nowMilliseconds);
    } catch (...) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    if (begun != CommandResult::Applied ||
        core_.EnterOverlay(authority, nowMilliseconds) != CommandResult::Applied) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    state_ = ControllerIsolationRoutingState::PreparedNeutral;
    return ControllerIsolationRoutingResult::Applied;
}

ControllerIsolationRoutingResult
ControllerIsolationRoutingSession::CommitPlaying(
    const RoutingAuthority& authority,
    const std::uint64_t measuredP99ReadingIntervalMilliseconds,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ != ControllerIsolationRoutingState::PreparedNeutral)
        return ControllerIsolationRoutingResult::RejectedState;
    if (!DrainIngress() || !DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;
    SelectedControllerCurrent current;
    const auto mapped = source_.SampleCurrent(current)
        ? MapCurrent(current)
        : std::nullopt;
    if (!mapped) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    const auto result = core_.CloseOverlay(
        authority, *mapped, measuredP99ReadingIntervalMilliseconds,
        nowMilliseconds);
    if (result != CommandResult::Waiting) {
        Fail();
        return Convert(result);
    }
    state_ = ControllerIsolationRoutingState::AwaitingPlaying;
    return ControllerIsolationRoutingResult::Waiting;
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::EnterOverlay(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ != ControllerIsolationRoutingState::Playing)
        return ControllerIsolationRoutingResult::RejectedState;
    if (!DrainIngress() || !DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;
    const auto result = core_.EnterOverlay(authority, nowMilliseconds);
    if (result == CommandResult::Applied)
        state_ = ControllerIsolationRoutingState::OverlayInteraction;
    return Convert(result);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::CloseOverlay(
    const RoutingAuthority& authority,
    const std::uint64_t measuredP99ReadingIntervalMilliseconds,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ != ControllerIsolationRoutingState::OverlayInteraction)
        return ControllerIsolationRoutingResult::RejectedState;
    if (!DrainIngress() || !DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;
    SelectedControllerCurrent current;
    const auto mapped = source_.SampleCurrent(current)
        ? MapCurrent(current)
        : std::nullopt;
    if (!mapped) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    const auto result = core_.CloseOverlay(
        authority, *mapped, measuredP99ReadingIntervalMilliseconds,
        nowMilliseconds);
    if (result == CommandResult::Waiting)
        state_ = ControllerIsolationRoutingState::AwaitingPlaying;
    return Convert(result);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::Heartbeat(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ == ControllerIsolationRoutingState::Disabled ||
        state_ == ControllerIsolationRoutingState::Fault) {
        return ControllerIsolationRoutingResult::RejectedState;
    }
    if (state_ == ControllerIsolationRoutingState::Playing)
        return ControllerIsolationRoutingResult::Applied;
    const auto result = core_.RenewHostLease(authority, nowMilliseconds);
    if (result == CommandResult::Faulted) Fail();
    return Convert(result);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::Pump(
    const std::uint64_t nowMilliseconds) noexcept {
    if (state_ == ControllerIsolationRoutingState::Disabled)
        return ControllerIsolationRoutingResult::RejectedState;
    if (state_ == ControllerIsolationRoutingState::Fault)
        return ControllerIsolationRoutingResult::Faulted;
    if (!DrainIngress())
        return ControllerIsolationRoutingResult::Faulted;
    if (!DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;
    if (state_ == ControllerIsolationRoutingState::AwaitingPlaying &&
        core_.mode() == RoutingMode::Playing) {
        state_ = ControllerIsolationRoutingState::Playing;
        return ControllerIsolationRoutingResult::Applied;
    }
    std::optional<DeviceReading> mapped;
    if (state_ == ControllerIsolationRoutingState::AwaitingPlaying) {
        SelectedControllerCurrent current;
        mapped = source_.SampleCurrent(current)
            ? MapCurrent(current)
            : std::nullopt;
        if (!mapped) {
            Fail();
            return ControllerIsolationRoutingResult::Faulted;
        }
    }
    const auto result = core_.Tick(nowMilliseconds, mapped);
    if (result == CommandResult::Resumed) {
        state_ = ControllerIsolationRoutingState::Playing;
        return ControllerIsolationRoutingResult::Applied;
    }
    if (result == CommandResult::Faulted) Fail();
    return Convert(result);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::Stop(
    const RoutingAuthority& authority) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    const auto result = core_.StopSession(authority);
    source_.Stop();
    ingress_.Close();
    authority_ = {};
    enrollment_ = {};
    lastSourceTimestamp_.reset();
    lastSourceState_.reset();
    lastSourceConnected_ = false;
    lastReadingOrdinal_ = 0;
    outputOwned_ = false;
    state_ = ControllerIsolationRoutingState::Disabled;
    return Convert(result);
}

std::optional<DeviceReading> ControllerIsolationRoutingSession::MapCurrent(
    const SelectedControllerCurrent& current) noexcept {
    if (current.sourceTimestampMicroseconds == 0 || !current.connected)
        return std::nullopt;
    if (lastSourceTimestamp_) {
        if (current.sourceTimestampMicroseconds < *lastSourceTimestamp_)
            return std::nullopt;
        if (current.sourceTimestampMicroseconds == *lastSourceTimestamp_) {
            if (!lastSourceState_ || current.state != *lastSourceState_ ||
                !lastSourceConnected_) {
                return std::nullopt;
            }
            return DeviceReading{
                authority_, enrollment_.enrollmentToken, lastReadingOrdinal_,
                current.sourceTimestampMicroseconds,
                current.observedAtMilliseconds, current.state, true};
        }
    }
    if (lastReadingOrdinal_ == std::numeric_limits<std::uint64_t>::max())
        return std::nullopt;
    ++lastReadingOrdinal_;
    lastSourceTimestamp_ = current.sourceTimestampMicroseconds;
    lastSourceState_ = current.state;
    lastSourceConnected_ = true;
    return DeviceReading{
        authority_, enrollment_.enrollmentToken, lastReadingOrdinal_,
        current.sourceTimestampMicroseconds, current.observedAtMilliseconds,
        current.state, true};
}

std::optional<DeviceReading> ControllerIsolationRoutingSession::MapEvent(
    const ControllerReaderEvent& event) noexcept {
    if (event.authority != authority_ ||
        event.deviceEnrollmentToken != enrollment_.enrollmentToken ||
        event.ingressOrdinal == 0 ||
        event.sourceTimestampMicroseconds == 0) {
        return std::nullopt;
    }
    if (event.kind == ControllerReaderEventKind::Disconnected) {
        if (lastReadingOrdinal_ == std::numeric_limits<std::uint64_t>::max())
            return std::nullopt;
        ++lastReadingOrdinal_;
        return DeviceReading{
            authority_, enrollment_.enrollmentToken, lastReadingOrdinal_,
            event.sourceTimestampMicroseconds, event.observedAtMilliseconds,
            {}, false};
    }
    if (event.kind != ControllerReaderEventKind::Reading)
        return std::nullopt;
    return MapCurrent({
        event.sourceTimestampMicroseconds, event.observedAtMilliseconds,
        event.state, event.connected});
}

bool ControllerIsolationRoutingSession::DrainIngress() noexcept {
    if (ingress_.fault() != ControllerReaderFault::None) {
        Fail();
        return false;
    }
    std::size_t pendingAttempts{};
    for (;;) {
        ControllerReaderEvent event;
        const auto popped = ingress_.TryPop(event);
        if (popped == ControllerReaderPopResult::Empty) {
            return true;
        }
        if (popped == ControllerReaderPopResult::ProducerPending) {
            if (++pendingAttempts > 64) {
                Fail();
                return false;
            }
            YieldProcessor();
            continue;
        }
        pendingAttempts = 0;
        if (popped == ControllerReaderPopResult::Faulted) {
            Fail();
            return false;
        }
        if (event.kind == ControllerReaderEventKind::GuidePressed ||
            event.kind == ControllerReaderEventKind::GuideReleased) {
            if (!guideSink_.PublishGuide(
                    event.kind == ControllerReaderEventKind::GuidePressed,
                    event.sourceTimestampMicroseconds,
                    event.ingressOrdinal)) {
                Fail();
                return false;
            }
            continue;
        }
        const auto reading = MapEvent(event);
        if (!reading) {
            Fail();
            return false;
        }
        const auto admission = core_.EnqueueReading(*reading);
        if (admission == ReadingAdmission::Faulted ||
            admission == ReadingAdmission::RejectedAuthority ||
            admission == ReadingAdmission::RejectedDevice ||
            admission == ReadingAdmission::RejectedOrder) {
            Fail();
            return false;
        }
    }
}

bool ControllerIsolationRoutingSession::DrainCore(
    const std::uint64_t nowMilliseconds) noexcept {
    if (core_.Drain(nowMilliseconds) != CommandResult::Faulted) return true;
    Fail();
    return false;
}

bool ControllerIsolationRoutingSession::ExactAuthority(
    const RoutingAuthority& authority) const noexcept {
    return authority_.valid() && authority == authority_;
}

SelectedDeviceIdentity ControllerIsolationRoutingSession::CoreDeviceIdentity(
    const SelectedControllerEnrollment& enrollment) const {
    SelectedDeviceIdentity device;
    device.enrollmentToken = enrollment.enrollmentToken;
    device.applicationLocalId = enrollment.deviceId;
    device.applicationLocalRootId = enrollment.deviceRootId;
    device.containerId = Hex(enrollment.containerId);
    device.pnpPath = Hex(enrollment.normalizedPnpPathDigest);
    device.vendorId = enrollment.vendorId;
    device.productId = enrollment.productId;
    device.knownVirtualOutput = enrollment.knownVirtualOutput;
    return device;
}

void ControllerIsolationRoutingSession::Fail() noexcept {
    if (authority_.valid() && core_.mode() != RoutingMode::Disabled) {
        (void)core_.StopSession(authority_);
    } else if (outputOwned_) {
        output_.RemoveOwnedTarget();
    }
    outputOwned_ = false;
    source_.Stop();
    ingress_.Close();
    state_ = ControllerIsolationRoutingState::Fault;
}

} // namespace widgetrail::isolation
