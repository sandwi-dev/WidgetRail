#include "ControllerIsolationCore.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace widgetrail::isolation {
namespace {

constexpr GamepadState kNeutralState{};

[[nodiscard]] bool HasIdentityBytes(
    const std::array<std::uint8_t, 32>& value) noexcept {
    return std::ranges::any_of(value, [](const std::uint8_t part) {
        return part != 0;
    });
}

[[nodiscard]] bool NeutralEntryState(const GamepadState& state) noexcept {
    return state.buttons == 0 && state.leftTrigger <= 24 &&
        state.rightTrigger <= 24 &&
        std::abs(static_cast<int>(state.leftThumbX)) <= 6'000 &&
        std::abs(static_cast<int>(state.leftThumbY)) <= 6'000 &&
        std::abs(static_cast<int>(state.rightThumbX)) <= 6'500 &&
        std::abs(static_cast<int>(state.rightThumbY)) <= 6'500;
}

[[nodiscard]] bool OutsideNeutralExit(const GamepadState& state) noexcept {
    return state.buttons != 0 || state.leftTrigger >= 30 ||
        state.rightTrigger >= 30 ||
        std::abs(static_cast<int>(state.leftThumbX)) > 7'849 ||
        std::abs(static_cast<int>(state.leftThumbY)) > 7'849 ||
        std::abs(static_cast<int>(state.rightThumbX)) > 8'689 ||
        std::abs(static_cast<int>(state.rightThumbY)) > 8'689;
}

template <typename T>
[[nodiscard]] bool Includes(const std::set<T>& values,
                            const std::set<T>& required) {
    return std::includes(
        values.begin(), values.end(), required.begin(), required.end());
}

template <typename T>
[[nodiscard]] bool HasExternalAdditions(
    const std::set<T>& current,
    const std::set<T>& before,
    const std::set<T>& owned) {
    for (const auto& value : current) {
        if (!before.contains(value) && !owned.contains(value)) return true;
    }
    return false;
}

} // namespace

bool SelectedDeviceIdentity::valid() const noexcept {
    return HasIdentityBytes(applicationLocalId) &&
        HasIdentityBytes(applicationLocalRootId) && !containerId.empty() &&
        !pnpPath.empty() && vendorId != 0 && productId != 0 &&
        !knownVirtualOutput;
}

ControllerIsolationCore::ControllerIsolationCore(
    VirtualOutputEffects& output,
    RoutingBudgets budgets) noexcept
    : output_(output), budgets_(budgets) {}

CommandResult ControllerIsolationCore::BeginSession(
    const RoutingAuthority& authority,
    const SelectedDeviceIdentity& device,
    const DeviceReading& current,
    const std::uint64_t nowMilliseconds) noexcept {
    if (mode_ != RoutingMode::Disabled || !authority.valid() || !device.valid() ||
        current.authority != authority || current.device != device ||
        current.ordinal == 0 || !current.connected) {
        fault_ = RoutingFault::InvalidInitialAuthority;
        return CommandResult::RejectedAuthority;
    }
    if (!NeutralEntryState(current.state)) {
        fault_ = RoutingFault::InitialStateNotNeutral;
        return CommandResult::RejectedState;
    }
    authority_ = authority;
    device_ = device;
    current_ = current;
    lastQueuedOrdinal_ = current.ordinal;
    lastObservedOrdinal_ = current.ordinal;
    resumeAfterOrdinal_ = current.ordinal;
    lastLeaseRenewedAtMilliseconds_ = nowMilliseconds;
    fault_ = RoutingFault::None;
    if (!SubmitNeutral()) {
        EnterFault(RoutingFault::OutputSubmissionFailed);
        return CommandResult::Faulted;
    }
    mode_ = RoutingMode::Playing;
    return CommandResult::Applied;
}

ReadingAdmission ControllerIsolationCore::EnqueueReading(
    const DeviceReading& reading) noexcept {
    if (mode_ == RoutingMode::Disabled || mode_ == RoutingMode::Fault ||
        reading.authority != authority_) {
        return ReadingAdmission::RejectedAuthority;
    }
    if (reading.device != device_) return ReadingAdmission::RejectedDevice;
    if (!reading.connected) {
        EnterFault(RoutingFault::DeviceDisconnected);
        return ReadingAdmission::Faulted;
    }
    if (reading.ordinal == lastQueuedOrdinal_) return ReadingAdmission::Duplicate;
    if (reading.ordinal < lastQueuedOrdinal_) {
        EnterFault(RoutingFault::ReadingOrderViolation);
        return ReadingAdmission::RejectedOrder;
    }
    if (readings_.size() >= budgets_.maximumQueuedReadings) {
        EnterFault(RoutingFault::ReadingQueueOverflow);
        return ReadingAdmission::Faulted;
    }
    readings_.push_back(reading);
    lastQueuedOrdinal_ = reading.ordinal;
    return ReadingAdmission::Accepted;
}

CommandResult ControllerIsolationCore::Drain(
    const std::uint64_t nowMilliseconds) noexcept {
    if (mode_ == RoutingMode::Disabled) return CommandResult::RejectedState;
    if (mode_ == RoutingMode::Fault) return CommandResult::Faulted;
    while (!readings_.empty()) {
        DeviceReading reading = std::move(readings_.front());
        readings_.pop_front();
        if (reading.observedAtMilliseconds > nowMilliseconds ||
            nowMilliseconds - reading.observedAtMilliseconds >
                budgets_.maximumQueuedReadingAgeMilliseconds) {
            readings_.clear();
            EnterFault(RoutingFault::StaleQueuedReading);
            return CommandResult::Faulted;
        }
        current_ = reading;
        lastObservedOrdinal_ = reading.ordinal;
        if (mode_ == RoutingMode::Playing) {
            if (reading.ordinal <= resumeAfterOrdinal_) continue;
            if (!output_.Submit(reading.state)) {
                readings_.clear();
                EnterFault(RoutingFault::OutputSubmissionFailed);
                return CommandResult::Faulted;
            }
        } else if (mode_ == RoutingMode::AwaitingNeutral) {
            (void)ApplyAwaitingNeutral(reading, nowMilliseconds);
        }
        // OverlayInteraction deliberately observes current state without
        // forwarding it to the game-facing target.
    }
    return CommandResult::Applied;
}

CommandResult ControllerIsolationCore::EnterOverlay(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (authority != authority_) return CommandResult::RejectedAuthority;
    if (mode_ != RoutingMode::Playing) return CommandResult::RejectedState;
    if (!SubmitNeutral()) {
        EnterFault(RoutingFault::OutputSubmissionFailed);
        return CommandResult::Faulted;
    }
    mode_ = RoutingMode::OverlayInteraction;
    lastLeaseRenewedAtMilliseconds_ = nowMilliseconds;
    neutralSinceMilliseconds_.reset();
    return CommandResult::Applied;
}

CommandResult ControllerIsolationCore::CloseOverlay(
    const RoutingAuthority& authority,
    const DeviceReading& current,
    const std::uint64_t measuredP99ReadingIntervalMilliseconds,
    const std::uint64_t nowMilliseconds) noexcept {
    if (authority != authority_) return CommandResult::RejectedAuthority;
    if (mode_ != RoutingMode::OverlayInteraction)
        return CommandResult::RejectedState;
    if (!ExactCurrent(current)) return CommandResult::RejectedAuthority;
    mode_ = RoutingMode::AwaitingNeutral;
    lastLeaseRenewedAtMilliseconds_ = nowMilliseconds;
    current_ = current;
    lastObservedOrdinal_ = std::max(lastObservedOrdinal_, current.ordinal);
    lastQueuedOrdinal_ = std::max(lastQueuedOrdinal_, current.ordinal);
    neutralDwellMilliseconds_ = std::clamp(
        measuredP99ReadingIntervalMilliseconds * 2,
        budgets_.minimumNeutralDwellMilliseconds,
        budgets_.maximumNeutralDwellMilliseconds);
    neutralSinceMilliseconds_ = NeutralEntryState(current.state)
        ? std::optional<std::uint64_t>{nowMilliseconds}
        : std::nullopt;
    return CommandResult::Waiting;
}

CommandResult ControllerIsolationCore::ObserveCurrent(
    const RoutingAuthority& authority,
    const DeviceReading& current,
    const std::uint64_t nowMilliseconds) noexcept {
    if (authority != authority_) return CommandResult::RejectedAuthority;
    if (mode_ != RoutingMode::AwaitingNeutral)
        return CommandResult::RejectedState;
    if (!ExactCurrent(current)) return CommandResult::RejectedAuthority;
    if (current.ordinal < lastObservedOrdinal_) {
        EnterFault(RoutingFault::ReadingOrderViolation);
        return CommandResult::Faulted;
    }
    current_ = current;
    lastObservedOrdinal_ = current.ordinal;
    lastQueuedOrdinal_ = std::max(lastQueuedOrdinal_, current.ordinal);
    return ApplyAwaitingNeutral(current, nowMilliseconds);
}

CommandResult ControllerIsolationCore::RenewHostLease(
    const RoutingAuthority& authority,
    const std::uint64_t nowMilliseconds) noexcept {
    if (authority != authority_) return CommandResult::RejectedAuthority;
    if (mode_ != RoutingMode::OverlayInteraction &&
        mode_ != RoutingMode::AwaitingNeutral) {
        return CommandResult::RejectedState;
    }
    lastLeaseRenewedAtMilliseconds_ = nowMilliseconds;
    return CommandResult::Applied;
}

CommandResult ControllerIsolationCore::Tick(
    const std::uint64_t nowMilliseconds,
    const std::optional<DeviceReading>& current) noexcept {
    if (mode_ == RoutingMode::Disabled) return CommandResult::RejectedState;
    if (mode_ == RoutingMode::Fault) return CommandResult::Faulted;
    if ((mode_ == RoutingMode::OverlayInteraction ||
         mode_ == RoutingMode::AwaitingNeutral) &&
        nowMilliseconds > lastLeaseRenewedAtMilliseconds_ &&
        nowMilliseconds - lastLeaseRenewedAtMilliseconds_ >
            budgets_.hostLeaseMilliseconds) {
        EnterFault(RoutingFault::HostLeaseExpired);
        return CommandResult::Faulted;
    }
    if (current) return ObserveCurrent(authority_, *current, nowMilliseconds);
    return mode_ == RoutingMode::AwaitingNeutral
        ? CommandResult::Waiting
        : CommandResult::Applied;
}

CommandResult ControllerIsolationCore::StopSession(
    const RoutingAuthority& authority) noexcept {
    if (authority != authority_) return CommandResult::RejectedAuthority;
    if (mode_ == RoutingMode::Disabled) return CommandResult::RejectedState;
    (void)SubmitNeutral();
    output_.RemoveOwnedTarget();
    readings_.clear();
    current_.reset();
    neutralSinceMilliseconds_.reset();
    authority_ = {};
    device_ = {};
    lastQueuedOrdinal_ = 0;
    lastObservedOrdinal_ = 0;
    resumeAfterOrdinal_ = 0;
    mode_ = RoutingMode::Disabled;
    return CommandResult::Applied;
}

bool ControllerIsolationCore::Matches(
    const RoutingAuthority& authority,
    const SelectedDeviceIdentity& device) const noexcept {
    return authority == authority_ && device == device_;
}

bool ControllerIsolationCore::ExactCurrent(
    const DeviceReading& current) const noexcept {
    if (!current.connected || current.ordinal == 0 ||
        !Matches(current.authority, current.device) ||
        current.ordinal < lastObservedOrdinal_ ||
        current.ordinal < lastQueuedOrdinal_) {
        return false;
    }
    return !current_ || current.ordinal != current_->ordinal ||
        (current.inputTimestampMicroseconds ==
             current_->inputTimestampMicroseconds &&
         current.state == current_->state);
}

CommandResult ControllerIsolationCore::ApplyAwaitingNeutral(
    const DeviceReading& current,
    const std::uint64_t nowMilliseconds) noexcept {
    if (OutsideNeutralExit(current.state)) {
        neutralSinceMilliseconds_.reset();
        return CommandResult::Waiting;
    }
    if (!neutralSinceMilliseconds_) {
        if (!NeutralEntryState(current.state)) return CommandResult::Waiting;
        neutralSinceMilliseconds_ = nowMilliseconds;
        return CommandResult::Waiting;
    }
    if (nowMilliseconds < *neutralSinceMilliseconds_ ||
        nowMilliseconds - *neutralSinceMilliseconds_ < neutralDwellMilliseconds_) {
        return CommandResult::Waiting;
    }
    mode_ = RoutingMode::Playing;
    resumeAfterOrdinal_ = current.ordinal;
    neutralSinceMilliseconds_.reset();
    return CommandResult::Resumed;
}

bool ControllerIsolationCore::SubmitNeutral() noexcept {
    return output_.Submit(kNeutralState);
}

void ControllerIsolationCore::EnterFault(const RoutingFault fault) noexcept {
    if (mode_ == RoutingMode::Fault) return;
    fault_ = fault;
    readings_.clear();
    if (fault != RoutingFault::OutputSubmissionFailed) {
        if (!SubmitNeutral()) output_.RemoveOwnedTarget();
    } else {
        output_.RemoveOwnedTarget();
    }
    mode_ = RoutingMode::Fault;
}

void LatencyAcceptanceEvidence::ObservePhysicalToSubmission(
    const std::uint64_t elapsedMicroseconds,
    const LatencyAcceptanceTargets& targets) noexcept {
    ++observations_;
    if (elapsedMicroseconds > targets.physicalToSubmissionWorstMicroseconds)
        ++outliers_;
}

void LatencyAcceptanceEvidence::ObserveEnterOverlayToNeutral(
    const std::uint64_t elapsedMicroseconds,
    const LatencyAcceptanceTargets& targets) noexcept {
    ++observations_;
    if (elapsedMicroseconds > targets.enterOverlayToNeutralP99Microseconds)
        ++outliers_;
}

GuardianAction DecideGuardianAction(
    const GuardianObservation& observation,
    const RoutingBudgets& budgets) noexcept {
    if (!observation.expected.valid())
        return GuardianAction::RefuseMismatchedWorker;
    if (!observation.observed) return GuardianAction::ExpectOwnedTargetRetired;
    if (*observation.observed != observation.expected)
        return GuardianAction::RefuseMismatchedWorker;
    if (observation.nowMilliseconds < observation.lastHeartbeatAtMilliseconds ||
        observation.nowMilliseconds - observation.lastHeartbeatAtMilliseconds <=
            budgets.workerHeartbeatTimeoutMilliseconds) {
        return GuardianAction::None;
    }
    return observation.finalRecheck
        ? GuardianAction::TerminateExactWorker
        : GuardianAction::RecheckExactWorker;
}

std::optional<HidHideApplyPlan> PlanHidHideApply(
    const HidHideSnapshot& before,
    const std::wstring& workerApplicationPath,
    const std::set<std::wstring>& selectedDeviceInstanceIds) {
    if (workerApplicationPath.empty() || selectedDeviceInstanceIds.empty() ||
        std::ranges::any_of(selectedDeviceInstanceIds, [](const auto& value) {
            return value.empty();
        })) {
        return std::nullopt;
    }
    HidHideApplyPlan plan;
    plan.journal.before = before;
    plan.desired = before;
    if (!before.applicationPaths.contains(workerApplicationPath)) {
        plan.journal.ownedApplicationPaths.insert(workerApplicationPath);
        plan.desired.applicationPaths.insert(workerApplicationPath);
    }
    for (const auto& device : selectedDeviceInstanceIds) {
        if (!before.deviceInstanceIds.contains(device)) {
            plan.journal.ownedDeviceInstanceIds.insert(device);
            plan.desired.deviceInstanceIds.insert(device);
        }
    }
    plan.journal.activatedBySession = !before.active;
    plan.desired.active = true;
    return plan;
}

HidHideRestorePlan PlanHidHideRestore(
    const HidHideJournal& journal,
    const HidHideSnapshot& current) {
    if (!Includes(current.applicationPaths, journal.before.applicationPaths) ||
        !Includes(current.deviceInstanceIds, journal.before.deviceInstanceIds) ||
        !Includes(current.applicationPaths, journal.ownedApplicationPaths) ||
        !Includes(current.deviceInstanceIds, journal.ownedDeviceInstanceIds)) {
        return {HidHideRestoreStatus::Conflict, std::nullopt};
    }
    HidHideSnapshot desired = current;
    for (const auto& value : journal.ownedApplicationPaths)
        desired.applicationPaths.erase(value);
    for (const auto& value : journal.ownedDeviceInstanceIds)
        desired.deviceInstanceIds.erase(value);

    const bool externalAdditions = HasExternalAdditions(
        current.applicationPaths, journal.before.applicationPaths,
        journal.ownedApplicationPaths) || HasExternalAdditions(
        current.deviceInstanceIds, journal.before.deviceInstanceIds,
        journal.ownedDeviceInstanceIds);
    if (journal.activatedBySession && current.active && !externalAdditions)
        desired.active = false;
    // If another owner added configuration while our session was active, keep
    // global hiding active rather than disabling their policy.
    return {
        externalAdditions
            ? HidHideRestoreStatus::ExternalAdditionsPreserved
            : HidHideRestoreStatus::Restored,
        std::move(desired),
    };
}

} // namespace widgetrail::isolation
