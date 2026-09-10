#include "ControllerIsolationRoutingSession.h"

#include <algorithm>
#include <limits>
#include <string>

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
    RoutingBudgets budgets,
    ControllerIsolationHostInputSink* hostInputSink) noexcept
    : source_(source), output_(output), guideSink_(guideSink),
      hostInputSink_(hostInputSink), budgets_(budgets), core_(output, budgets) {}

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
    if (!ingress_.Open(authority, enrollment.enrollmentToken)) {
        Fail();
        return ControllerIsolationRoutingResult::ReaderUnavailable;
    }
    SelectedControllerEnrollment preparedEnrollment;
    const auto prepared = source_.Prepare(
        enrollment, ingress_, preparedEnrollment);
    if (prepared != SelectedControllerPrepareStatus::Ready ||
        !SameStableControllerIdentity(enrollment, preparedEnrollment)) {
        Fail();
        return ControllerIsolationRoutingResult::ReaderUnavailable;
    }
    enrollment_ = preparedEnrollment;
    const auto initial = SampleCurrentFence();
    if (!initial) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    if (!ControllerIsolationNeutralEntry(initial->state)) {
        Fail();
        return ControllerIsolationRoutingResult::RejectedState;
    }
    if (!output_.OpenOwnedTarget()) {
        Fail();
        return ControllerIsolationRoutingResult::OutputUnavailable;
    }
    outputOwned_ = true;
    CommandResult begun{CommandResult::Faulted};
    try {
        const auto device = CoreDeviceIdentity(enrollment_);
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
    return BeginTransition(
        PendingTransitionKind::CommitPlaying,
        measuredP99ReadingIntervalMilliseconds, nowMilliseconds);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::EnterOverlay(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ != ControllerIsolationRoutingState::Playing)
        return ControllerIsolationRoutingResult::RejectedState;
    return BeginTransition(
        PendingTransitionKind::EnterOverlay, 0, nowMilliseconds);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::CloseOverlay(
    const RoutingAuthority& authority,
    const std::uint64_t measuredP99ReadingIntervalMilliseconds,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    if (state_ != ControllerIsolationRoutingState::OverlayInteraction)
        return ControllerIsolationRoutingResult::RejectedState;
    return BeginTransition(
        PendingTransitionKind::CloseOverlay,
        measuredP99ReadingIntervalMilliseconds, nowMilliseconds);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::HoldContained(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!ExactAuthority(authority))
        return ControllerIsolationRoutingResult::RejectedAuthority;
    pendingTransition_.reset();
    ordinaryDrainBarrierOrdinal_.reset();
    const auto result = state_ == ControllerIsolationRoutingState::Playing
        ? core_.EnterOverlay(authority, nowMilliseconds)
        : core_.HoldOverlay(authority, nowMilliseconds);
    if (result == CommandResult::Applied)
        state_ = ControllerIsolationRoutingState::OverlayInteraction;
    else if (result == CommandResult::Faulted)
        Fail();
    return Convert(result);
}

ControllerIsolationRoutingResult ControllerIsolationRoutingSession::Pump(
    const std::uint64_t nowMilliseconds) noexcept {
    if (state_ == ControllerIsolationRoutingState::Disabled)
        return ControllerIsolationRoutingResult::RejectedState;
    if (state_ == ControllerIsolationRoutingState::Fault)
        return ControllerIsolationRoutingResult::Faulted;
    if (!DrainFeedback()) return ControllerIsolationRoutingResult::Faulted;
    if (pendingTransition_) {
        const auto transition = ContinueTransition(nowMilliseconds);
        if (transition == ControllerIsolationRoutingResult::Faulted)
            return transition;
        if (pendingTransition_)
            return ControllerIsolationRoutingResult::Waiting;
        if (state_ == ControllerIsolationRoutingState::OverlayInteraction)
            return ControllerIsolationRoutingResult::Applied;
    }

    if (!ordinaryDrainBarrierOrdinal_) {
        ordinaryDrainBarrierOrdinal_ = ingress_.reservedThroughOrdinal();
    }
    const auto ingress = DrainIngressThrough(
        *ordinaryDrainBarrierOrdinal_, nowMilliseconds);
    if (ingress == DrainIngressResult::Faulted)
        return ControllerIsolationRoutingResult::Faulted;
    if (ingress == DrainIngressResult::Waiting) {
        // Never let a neutral prefix advance AwaitingNeutral while a producer
        // still owns an unpublished suffix inside this barrier.
        return ControllerIsolationRoutingResult::Waiting;
    }
    if (!DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;
    ordinaryDrainBarrierOrdinal_.reset();
    if (state_ == ControllerIsolationRoutingState::AwaitingPlaying &&
        core_.mode() == RoutingMode::Playing) {
        state_ = ControllerIsolationRoutingState::Playing;
        return ControllerIsolationRoutingResult::Applied;
    }

    std::optional<DeviceReading> current;
    if (state_ == ControllerIsolationRoutingState::AwaitingPlaying) {
        current = SampleCurrentFence();
        if (!current) {
            Fail();
            return ControllerIsolationRoutingResult::Faulted;
        }
    }
    const auto result = core_.Tick(nowMilliseconds, current);
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
    sampledFenceTimestamp_.reset();
    sampledFenceState_.reset();
    producerPendingSinceMilliseconds_.reset();
    ordinaryDrainBarrierOrdinal_.reset();
    pendingTransition_.reset();
    lastSourceConnected_ = false;
    lastReadingOrdinal_ = 0;
    lastConsumedIngressOrdinal_ = 0;
    outputOwned_ = false;
    state_ = ControllerIsolationRoutingState::Disabled;
    return Convert(result);
}

std::optional<DeviceReading>
ControllerIsolationRoutingSession::SampleCurrentFence() noexcept {
    if (state_ == ControllerIsolationRoutingState::Playing)
        return std::nullopt;
    SelectedControllerCurrent current;
    if (!source_.SampleCurrent(current)) return std::nullopt;
    const auto mapped = MapCurrent(current);
    if (!mapped) return std::nullopt;
    sampledFenceTimestamp_ = current.sourceTimestampMicroseconds;
    sampledFenceState_ = current.state;
    return mapped;
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

ControllerIsolationRoutingSession::DrainIngressResult
ControllerIsolationRoutingSession::DrainIngressThrough(
    const std::uint64_t barrierIngressOrdinal,
    const std::uint64_t nowMilliseconds) noexcept {
    if (ingress_.fault() != ControllerReaderFault::None) {
        Fail();
        return DrainIngressResult::Faulted;
    }
    if (barrierIngressOrdinal < lastConsumedIngressOrdinal_ ||
        barrierIngressOrdinal > ingress_.reservedThroughOrdinal()) {
        Fail();
        return DrainIngressResult::Faulted;
    }
    while (lastConsumedIngressOrdinal_ < barrierIngressOrdinal) {
        ControllerReaderEvent event;
        auto popped = ingress_.TryPop(event);
        if (popped == ControllerReaderPopResult::Empty) {
            Fail();
            return DrainIngressResult::Faulted;
        }
        if (popped == ControllerReaderPopResult::ProducerPending) {
            if (!producerPendingSinceMilliseconds_) {
                producerPendingSinceMilliseconds_ = nowMilliseconds;
                return DrainIngressResult::Waiting;
            }
            if (nowMilliseconds < *producerPendingSinceMilliseconds_) {
                Fail();
                return DrainIngressResult::Faulted;
            }
            if (nowMilliseconds - *producerPendingSinceMilliseconds_ <=
                budgets_.maximumQueuedReadingAgeMilliseconds) {
                return DrainIngressResult::Waiting;
            }
            // One final acquire check prevents a producer that published at
            // the deadline from being mistaken for a stuck reservation.
            popped = ingress_.TryPop(event);
            if (popped != ControllerReaderPopResult::Event) {
                Fail();
                return DrainIngressResult::Faulted;
            }
        }
        producerPendingSinceMilliseconds_.reset();
        if (popped == ControllerReaderPopResult::Faulted) {
            Fail();
            return DrainIngressResult::Faulted;
        }
        if (event.ingressOrdinal != lastConsumedIngressOrdinal_ + 1 ||
            !ProcessIngressEvent(event)) {
            Fail();
            return DrainIngressResult::Faulted;
        }
        lastConsumedIngressOrdinal_ = event.ingressOrdinal;
    }
    producerPendingSinceMilliseconds_.reset();
    return DrainIngressResult::Complete;
}

bool ControllerIsolationRoutingSession::ProcessIngressEvent(
    const ControllerReaderEvent& event) noexcept {
    if (event.authority != authority_ ||
        event.deviceEnrollmentToken != enrollment_.enrollmentToken ||
        event.ingressOrdinal == 0 ||
        event.sourceTimestampMicroseconds == 0) {
        return false;
    }
    if (event.kind == ControllerReaderEventKind::GuidePressed ||
        event.kind == ControllerReaderEventKind::GuideReleased) {
        return guideSink_.PublishGuide(
            event.kind == ControllerReaderEventKind::GuidePressed,
            event.sourceTimestampMicroseconds, event.ingressOrdinal);
    }
    if (event.kind == ControllerReaderEventKind::Reading &&
        sampledFenceTimestamp_) {
        if (event.sourceTimestampMicroseconds < *sampledFenceTimestamp_)
            return true;
        if (event.sourceTimestampMicroseconds == *sampledFenceTimestamp_) {
            return event.connected && sampledFenceState_ &&
                event.state == *sampledFenceState_;
        }
    }
    const auto reading = MapEvent(event);
    if (!reading) return false;
    const auto admission = core_.EnqueueReading(*reading);
    if (admission != ReadingAdmission::Accepted &&
        admission != ReadingAdmission::Duplicate)
        return false;
    return !hostInputSink_ ||
        state_ != ControllerIsolationRoutingState::OverlayInteraction ||
        admission == ReadingAdmission::Duplicate ||
        hostInputSink_->PublishInput(event.state, event.ingressOrdinal);
}

ControllerIsolationRoutingResult
ControllerIsolationRoutingSession::BeginTransition(
    const PendingTransitionKind kind,
    const std::uint64_t measuredP99ReadingIntervalMilliseconds,
    const std::uint64_t nowMilliseconds) noexcept {
    if (pendingTransition_)
        return ControllerIsolationRoutingResult::RejectedState;
    ordinaryDrainBarrierOrdinal_.reset();
    pendingTransition_ = PendingTransition{
        kind, ingress_.reservedThroughOrdinal(),
        measuredP99ReadingIntervalMilliseconds};
    return ContinueTransition(nowMilliseconds);
}

ControllerIsolationRoutingResult
ControllerIsolationRoutingSession::ContinueTransition(
    const std::uint64_t nowMilliseconds) noexcept {
    if (!pendingTransition_)
        return ControllerIsolationRoutingResult::RejectedState;
    const auto pending = *pendingTransition_;
    const auto ingress = DrainIngressThrough(
        pending.barrierIngressOrdinal, nowMilliseconds);
    if (ingress == DrainIngressResult::Faulted)
        return ControllerIsolationRoutingResult::Faulted;
    if (ingress == DrainIngressResult::Waiting) {
        return ControllerIsolationRoutingResult::Waiting;
    }
    if (!DrainCore(nowMilliseconds))
        return ControllerIsolationRoutingResult::Faulted;

    pendingTransition_.reset();
    if (pending.kind == PendingTransitionKind::EnterOverlay) {
        const auto result = core_.EnterOverlay(authority_, nowMilliseconds);
        if (result == CommandResult::Applied) {
            state_ = ControllerIsolationRoutingState::OverlayInteraction;
        } else {
            Fail();
        }
        return Convert(result);
    }

    const auto current = SampleCurrentFence();
    if (!current) {
        Fail();
        return ControllerIsolationRoutingResult::Faulted;
    }
    const auto result = core_.CloseOverlay(
        authority_, *current,
        pending.measuredP99ReadingIntervalMilliseconds, nowMilliseconds);
    if (result == CommandResult::Waiting) {
        state_ = ControllerIsolationRoutingState::AwaitingPlaying;
    } else {
        Fail();
    }
    return Convert(result);
}

bool ControllerIsolationRoutingSession::DrainCore(
    const std::uint64_t nowMilliseconds) noexcept {
    if (core_.Drain(nowMilliseconds) != CommandResult::Faulted) return true;
    Fail();
    return false;
}

bool ControllerIsolationRoutingSession::DrainFeedback() noexcept {
    ControllerRumbleState feedback;
    if (!output_.TakeLatestFeedback(feedback)) return true;
    if (source_.ApplyRumble(feedback)) return true;
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
