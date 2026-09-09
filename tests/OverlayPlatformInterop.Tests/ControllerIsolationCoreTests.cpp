#include "../../src/OverlayPlatformInterop/ControllerIsolationCore.h"

#include <cstdlib>
#include <iostream>
#include <limits>
#include <type_traits>
#include <utility>

namespace {

using namespace widgetrail::isolation;

static_assert(std::is_trivially_copyable_v<DeviceReading>);

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

struct FakeOutput final : VirtualOutputEffects {
    std::vector<GamepadState> submitted;
    bool failNext{};
    int removals{};

    [[nodiscard]] bool Submit(const GamepadState& state) noexcept override {
        if (std::exchange(failNext, false)) return false;
        submitted.push_back(state);
        return true;
    }

    void RemoveOwnedTarget() noexcept override { ++removals; }
};

RoutingAuthority Authority(const std::uint64_t generation = 1) {
    return {generation, generation + 10, generation + 20, generation + 30};
}

SelectedDeviceIdentity Device(const std::uint8_t seed = 1) {
    SelectedDeviceIdentity result;
    result.enrollmentToken = seed;
    result.applicationLocalId[0] = seed;
    result.applicationLocalRootId[0] = static_cast<std::uint8_t>(seed + 1);
    result.containerId = L"container-" + std::to_wstring(seed);
    result.pnpPath = L"device-" + std::to_wstring(seed);
    result.vendorId = 0x045E;
    result.productId = static_cast<std::uint16_t>(0x0200 + seed);
    return result;
}

DeviceReading Reading(
    const RoutingAuthority& authority,
    const SelectedDeviceIdentity& device,
    const std::uint64_t ordinal,
    const std::uint64_t observedAt,
    const GamepadState state = {}) {
    return {
        authority,
        device.enrollmentToken,
        ordinal,
        ordinal * 1'000,
        observedAt,
        state,
        true,
    };
}

ControllerIsolationCore Started(
    FakeOutput& output,
    const RoutingAuthority& authority,
    const SelectedDeviceIdentity& device,
    const RoutingBudgets budgets = {}) {
    ControllerIsolationCore core{output, budgets};
    Check(core.BeginSession(
              authority, device, Reading(authority, device, 1, 0), 0) ==
              CommandResult::Applied,
          "a valid neutral selected device starts one dormant routing session");
    return core;
}

void ExactAuthorityAndOrderedGameplay() {
    FakeOutput output;
    const auto authority = Authority();
    const auto device = Device();
    auto core = Started(output, authority, device);
    Check(output.submitted == std::vector<GamepadState>{GamepadState{}},
          "session preparation publishes one neutral report");

    GamepadState down;
    down.buttons = 0x1000;
    Check(core.EnqueueReading(Reading(authority, device, 2, 10, down)) ==
              ReadingAdmission::Accepted &&
              core.EnqueueReading(Reading(authority, device, 3, 11)) ==
                  ReadingAdmission::Accepted &&
              core.Drain(12) == CommandResult::Applied,
          "a short press and release drain in exact callback order");
    Check(output.submitted.size() == 3 && output.submitted[1] == down &&
              output.submitted[2] == GamepadState{},
          "no short gameplay transition is coalesced or stretched");

    Check(core.EnqueueReading(Reading(authority, device, 3, 13)) ==
              ReadingAdmission::Duplicate &&
              core.mode() == RoutingMode::Playing,
          "an unchanged current reading is healthy rather than stale");
    auto alteredDuplicate = Reading(authority, device, 3, 14);
    alteredDuplicate.state.buttons = 0x2000;
    Check(core.EnqueueReading(alteredDuplicate) ==
              ReadingAdmission::RejectedOrder &&
              core.mode() == RoutingMode::Fault &&
              output.submitted.back() == GamepadState{},
          "a duplicate ordinal carrying altered report authority fails closed");

    FakeOutput rejectionOutput;
    auto rejection = Started(rejectionOutput, authority, device);
    Check(rejection.EnqueueReading(Reading(Authority(2), device, 4, 14)) ==
              ReadingAdmission::RejectedAuthority &&
              rejection.EnqueueReading(
                  Reading(authority, Device(2), 4, 14)) ==
                  ReadingAdmission::RejectedDevice,
          "wrong session and selected-device identities fail closed");
}

void InvalidBudgetsFailBeforeTargetOwnership() {
    const auto authority = Authority();
    const auto device = Device();
    RoutingBudgets zeroQueue;
    zeroQueue.maximumQueuedReadings = 0;
    FakeOutput zeroQueueOutput;
    ControllerIsolationCore zeroQueueCore{zeroQueueOutput, zeroQueue};
    Check(zeroQueueCore.BeginSession(
              authority, device, Reading(authority, device, 1, 0), 0) ==
              CommandResult::RejectedState &&
              zeroQueueCore.fault() == RoutingFault::InvalidBudgets &&
              zeroQueueOutput.submitted.empty() &&
              zeroQueueOutput.removals == 0,
          "zero queue capacity is rejected before target ownership");

    RoutingBudgets oversizedQueue;
    oversizedQueue.maximumQueuedReadings =
        ControllerIsolationCore::MaximumReadingCapacity + 1;
    FakeOutput oversizedOutput;
    ControllerIsolationCore oversizedCore{oversizedOutput, oversizedQueue};
    Check(oversizedCore.BeginSession(
              authority, device, Reading(authority, device, 1, 0), 0) ==
              CommandResult::RejectedState &&
              oversizedCore.fault() == RoutingFault::InvalidBudgets,
          "configured queue capacity cannot exceed fixed storage");

    RoutingBudgets invertedDwell;
    invertedDwell.minimumNeutralDwellMilliseconds = 51;
    invertedDwell.maximumNeutralDwellMilliseconds = 50;
    FakeOutput invertedOutput;
    ControllerIsolationCore invertedCore{invertedOutput, invertedDwell};
    Check(invertedCore.BeginSession(
              authority, device, Reading(authority, device, 1, 0), 0) ==
              CommandResult::RejectedState &&
              invertedCore.fault() == RoutingFault::InvalidBudgets,
          "inverted neutral dwell bounds are rejected before std::clamp");

    FakeOutput overflowDwellOutput;
    auto overflowDwell = Started(overflowDwellOutput, authority, device);
    Check(overflowDwell.EnterOverlay(authority, 0) == CommandResult::Applied &&
              overflowDwell.CloseOverlay(
                  authority,
                  Reading(authority, device, 1, 0),
                  std::numeric_limits<std::uint64_t>::max(),
                  0) == CommandResult::Waiting &&
              overflowDwell.ObserveCurrent(
                  authority, Reading(authority, device, 1, 0), 49) ==
                  CommandResult::Waiting &&
              overflowDwell.ObserveCurrent(
                  authority, Reading(authority, device, 1, 0), 50) ==
                  CommandResult::Resumed,
          "doubling a measured interval saturates at the configured dwell cap");
}

void QueueBoundsNeverReplayStaleInput() {
    RoutingBudgets budgets;
    budgets.maximumQueuedReadings = 2;
    FakeOutput overflowOutput;
    const auto authority = Authority();
    const auto device = Device();
    auto overflow = Started(overflowOutput, authority, device, budgets);
    GamepadState down;
    down.buttons = 0x1000;
    Check(overflow.EnqueueReading(Reading(authority, device, 2, 1, down)) ==
              ReadingAdmission::Accepted &&
              overflow.EnqueueReading(Reading(authority, device, 3, 2)) ==
                  ReadingAdmission::Accepted &&
              overflow.EnqueueReading(Reading(authority, device, 4, 3, down)) ==
                  ReadingAdmission::Faulted,
          "a full transition queue faults instead of overwriting an edge");
    Check(overflow.mode() == RoutingMode::Fault &&
              overflow.fault() == RoutingFault::ReadingQueueOverflow &&
              overflow.queuedReadings() == 0 &&
              overflowOutput.submitted.size() == 2 &&
              overflowOutput.submitted.back() == GamepadState{},
          "overflow clears stale work and leaves output neutral");

    FakeOutput staleOutput;
    auto stale = Started(staleOutput, authority, device);
    Check(stale.EnqueueReading(Reading(authority, device, 2, 1, down)) ==
              ReadingAdmission::Accepted &&
              stale.Drain(102) == CommandResult::Faulted &&
              stale.fault() == RoutingFault::StaleQueuedReading &&
              staleOutput.submitted.size() == 2 &&
              staleOutput.submitted.back() == GamepadState{},
          "an aged gameplay report is neutralized rather than replayed");
}

void OverlayNeutralAckAndHealthyCachedClose() {
    FakeOutput output;
    const auto authority = Authority();
    const auto device = Device();
    auto core = Started(output, authority, device);
    Check(core.EnterOverlay(authority, 10) == CommandResult::Applied &&
              core.mode() == RoutingMode::OverlayInteraction &&
              output.submitted.size() == 2 &&
              output.submitted.back() == GamepadState{},
          "overlay interaction begins only after backend neutral acceptance");

    GamepadState containedPress;
    containedPress.buttons = 0x2000;
    Check(core.EnqueueReading(
              Reading(authority, device, 2, 11, containedPress)) ==
              ReadingAdmission::Accepted &&
              core.EnqueueReading(Reading(authority, device, 3, 12)) ==
                  ReadingAdmission::Accepted &&
              core.Drain(13) == CommandResult::Applied &&
              output.submitted.size() == 2,
          "physical reports update contained state without reaching gameplay");

    const auto cachedNeutral = Reading(authority, device, 3, 13);
    Check(core.CloseOverlay(authority, cachedNeutral, 4, 20) ==
              CommandResult::Waiting &&
              core.ObserveCurrent(authority, cachedNeutral, 39) ==
                  CommandResult::Waiting &&
              core.ObserveCurrent(authority, cachedNeutral, 40) ==
                  CommandResult::Resumed,
          "one exact cached neutral state can remain authoritative across the dwell");
    Check(core.resumeAfterOrdinal() == 3 && output.submitted.size() == 2,
          "resumption does not replay the cached neutral evidence");

    GamepadState fresh;
    fresh.buttons = 0x4000;
    Check(core.EnqueueReading(Reading(authority, device, 4, 41, fresh)) ==
              ReadingAdmission::Accepted &&
              core.Drain(41) == CommandResult::Applied &&
              output.submitted.back() == fresh,
          "only a distinct post-resume actuation reaches gameplay");
}

void CloseRequiresReleaseAndUsesHysteresis() {
    FakeOutput output;
    const auto authority = Authority();
    const auto device = Device();
    auto core = Started(output, authority, device);
    Check(core.EnterOverlay(authority, 1) == CommandResult::Applied,
          "test setup enters contained interaction");
    GamepadState held;
    held.buttons = 0x1000;
    Check(core.EnqueueReading(Reading(authority, device, 2, 2, held)) ==
              ReadingAdmission::Accepted &&
              core.Drain(2) == CommandResult::Applied &&
              core.CloseOverlay(
                  authority, Reading(authority, device, 1, 3), 10, 3) ==
                  CommandResult::RejectedAuthority &&
              core.mode() == RoutingMode::OverlayInteraction,
          "a pre-barrier neutral reading cannot release a newer held state");
    Check(core.CloseOverlay(
              authority, Reading(authority, device, 2, 2, held), 10, 3) ==
              CommandResult::Waiting,
          "a held close-barrier state cannot start neutral dwell");
    Check(core.ObserveCurrent(
              authority, Reading(authority, device, 3, 5), 5) ==
              CommandResult::Waiting,
          "a distinct neutral reading starts the bounded dwell");
    GamepadState gray;
    gray.leftThumbX = 7'000;
    Check(core.ObserveCurrent(
              authority, Reading(authority, device, 4, 10, gray), 10) ==
              CommandResult::Waiting,
          "deadzone hysteresis tolerates bounded neutral drift");
    GamepadState outside;
    outside.leftThumbX = 8'000;
    Check(core.ObserveCurrent(
              authority, Reading(authority, device, 5, 15, outside), 15) ==
              CommandResult::Waiting &&
              core.ObserveCurrent(
                  authority, Reading(authority, device, 6, 20), 20) ==
                  CommandResult::Waiting &&
              core.ObserveCurrent(
                  authority, Reading(authority, device, 6, 39), 39) ==
                  CommandResult::Waiting &&
              core.ObserveCurrent(
                  authority, Reading(authority, device, 6, 40), 40) ==
                  CommandResult::Resumed,
          "non-neutral input cancels dwell and a later neutral sample restarts it");
}

void PendingContainedReportsCannotEscapeCloseBarrier() {
    FakeOutput output;
    const auto authority = Authority();
    const auto device = Device();
    auto core = Started(output, authority, device);
    Check(core.EnterOverlay(authority, 1) == CommandResult::Applied,
          "pending-barrier setup enters contained interaction");
    GamepadState contained;
    contained.buttons = 0x1000;
    const auto pending = Reading(authority, device, 2, 2, contained);
    Check(core.EnqueueReading(pending) == ReadingAdmission::Accepted &&
              core.CloseOverlay(authority, pending, 10, 2) ==
                  CommandResult::Waiting &&
              core.ObserveCurrent(
                  authority, Reading(authority, device, 3, 3), 3) ==
                  CommandResult::Waiting &&
              core.ObserveCurrent(
                  authority, Reading(authority, device, 3, 3), 23) ==
                  CommandResult::Resumed &&
              core.Drain(23) == CommandResult::Applied &&
              output.submitted.size() == 2 &&
              output.submitted.back() == GamepadState{},
          "a queued contained report below the resume barrier never reaches gameplay");
}

void LeaseAndOutputFailuresAreSafetyFaults() {
    const auto authority = Authority();
    const auto device = Device();
    FakeOutput playingOutput;
    auto playing = Started(playingOutput, authority, device);
    Check(playing.Tick(10'000) == CommandResult::Applied &&
              playing.mode() == RoutingMode::Playing,
          "host lease expiry never interrupts healthy closed gameplay");

    FakeOutput containedOutput;
    auto contained = Started(containedOutput, authority, device);
    Check(contained.EnterOverlay(authority, 1) == CommandResult::Applied &&
              contained.Tick(252) == CommandResult::Faulted &&
              contained.fault() == RoutingFault::HostLeaseExpired &&
              containedOutput.submitted.back() == GamepadState{},
          "a lost interactive host lease remains fail-closed and neutral");

    FakeOutput failedOutput;
    failedOutput.failNext = true;
    ControllerIsolationCore failed{failedOutput};
    Check(failed.BeginSession(
              authority, device, Reading(authority, device, 1, 0), 0) ==
              CommandResult::Faulted &&
              failed.fault() == RoutingFault::OutputSubmissionFailed &&
              failedOutput.removals == 1,
          "backend failure retires the owned target exactly once");
    Check(failed.StopSession(authority) == CommandResult::Applied &&
              failedOutput.removals == 1 &&
              failed.StopSession(authority) ==
                  CommandResult::RejectedAuthority &&
              failedOutput.removals == 1,
          "fault cleanup and repeated stop cannot retire or submit the target again");
}

void LatencyOutliersRemainAcceptanceEvidence() {
    LatencyAcceptanceEvidence evidence;
    evidence.ObservePhysicalToSubmission(50'000);
    evidence.ObserveEnterOverlayToNeutral(80'000);
    Check(evidence.observations() == 2 && evidence.outliers() == 2,
          "latency misses remain measurement evidence without routing side effects");
}

void GuardianRequiresExactIdentityAndFinalRecheck() {
    const WorkerIdentity worker{42, 100, Authority(), L"owned-target"};
    GuardianObservation healthy{worker, worker, 1'000, 1'250, false};
    Check(DecideGuardianAction(healthy) == GuardianAction::None,
          "the heartbeat boundary itself remains healthy");
    healthy.nowMilliseconds = 1'251;
    Check(DecideGuardianAction(healthy) ==
              GuardianAction::RecheckExactWorker,
          "the first stale observation requests an exact final recheck");
    healthy.finalRecheck = true;
    Check(DecideGuardianAction(healthy) ==
              GuardianAction::TerminateExactWorker,
          "only the same stale process identity may be terminated");
    auto replacement = worker;
    replacement.processCreationTime = 101;
    healthy.observed = replacement;
    Check(DecideGuardianAction(healthy) ==
              GuardianAction::RefuseMismatchedWorker,
          "PID reuse or another target can never inherit recovery authority");
    healthy.observed.reset();
    Check(DecideGuardianAction(healthy) ==
              GuardianAction::ExpectOwnedTargetRetired,
          "an already-dead owner expects backend target retirement without cleanup of peers");
}

void HidHideJournalPreservesOtherOwners() {
    HidHideSnapshot unsafeBefore{
        {L"existing.exe"}, {L"existing-device"}, false};
    const auto refusedActivation = PlanHidHideApply(
        unsafeBefore, L"controller-worker.exe", {L"selected-device"});
    Check(refusedActivation.status ==
              HidHideApplyStatus::ActivationConflict &&
              !refusedActivation.plan,
          "activating HidHide cannot silently enroll an unrelated disabled device");

    HidHideSnapshot before{{L"existing.exe"}, {}, false};
    const auto applyResult = PlanHidHideApply(
        before, L"controller-worker.exe", {L"selected-device"});
    Check(applyResult.status == HidHideApplyStatus::Ready &&
              applyResult.plan,
          "a selected-only inactive policy can be activated safely");
    const auto& apply = *applyResult.plan;
    Check(apply.desired.active &&
              apply.desired.applicationPaths ==
                  std::set<std::wstring>{L"controller-worker.exe", L"existing.exe"} &&
              apply.desired.deviceInstanceIds ==
                  std::set<std::wstring>{L"selected-device"},
          "apply merges only the worker and exact selected-device entries");
    const auto restored = PlanHidHideRestore(apply.journal, apply.desired);
    Check(restored.status == HidHideRestoreStatus::Restored &&
              restored.desired == before,
          "clean teardown restores only the journaled additions and active flag");

    auto partial = before;
    partial.applicationPaths.insert(L"controller-worker.exe");
    const auto recovered = PlanHidHideRecovery(apply.journal, partial);
    Check(recovered.status == HidHideRestoreStatus::Restored &&
              recovered.desired == before,
          "recovery removes the owned subset after a partial transaction");

    auto externallyExtended = apply.desired;
    externallyExtended.applicationPaths.insert(L"other-owner.exe");
    externallyExtended.deviceInstanceIds.insert(L"other-device");
    const auto preserve = PlanHidHideRestore(
        apply.journal, externallyExtended);
    Check(preserve.status ==
              HidHideRestoreStatus::ExternalAdditionsPreserved &&
              preserve.desired && preserve.desired->active &&
              preserve.desired->applicationPaths.contains(L"other-owner.exe") &&
              preserve.desired->deviceInstanceIds.contains(L"other-device") &&
              !preserve.desired->applicationPaths.contains(
                  L"controller-worker.exe") &&
              !preserve.desired->deviceInstanceIds.contains(L"selected-device"),
          "external additions survive while our exact entries are retired");

    auto activeBefore = HidHideSnapshot{
        {L"existing.exe"}, {L"existing-device"}, true};
    const auto activeApplyResult = PlanHidHideApply(
        activeBefore, L"controller-worker.exe", {L"selected-device"});
    Check(activeApplyResult.status == HidHideApplyStatus::Ready &&
              activeApplyResult.plan,
          "an already-active shared policy can preserve unrelated entries");
    const auto& activeApply = *activeApplyResult.plan;
    auto conflict = activeApply.desired;
    conflict.deviceInstanceIds.erase(L"existing-device");
    const auto refused = PlanHidHideRestore(activeApply.journal, conflict);
    Check(refused.status == HidHideRestoreStatus::Conflict &&
              !refused.desired,
          "missing original authority refuses restoration instead of overwriting shared state");

    auto inverted = before;
    inverted.applicationListInverted = true;
    Check(PlanHidHideApply(
              inverted, L"controller-worker.exe", {L"selected-device"}).status ==
              HidHideApplyStatus::InvalidInput,
          "inverse application policy is rejected rather than reinterpreted");
}

void StaleHostCanRecontainAwaitingNeutral() {
    FakeOutput output;
    const auto authority = Authority();
    auto core = Started(output, authority, Device());
    Check(core.EnterOverlay(authority, 1) == CommandResult::Applied,
          "stale-host fixture enters contained interaction");
    GamepadState held;
    held.buttons = 0x1000;
    const DeviceReading current{authority, Device().enrollmentToken, 2, 2, 2, held, true};
    Check(core.CloseOverlay(authority, current, 10, 2) ==
              CommandResult::Waiting &&
              core.HoldOverlay(authority, 3) == CommandResult::Applied &&
              core.mode() == RoutingMode::OverlayInteraction &&
              output.submitted.back() == GamepadState{},
          "uncertain host stall cancels close and keeps exact target neutral");
}

void StopIsExactAndGenerationBound() {
    FakeOutput output;
    const auto authority = Authority();
    const auto device = Device();
    auto core = Started(output, authority, device);
    Check(core.StopSession(Authority(2)) == CommandResult::RejectedAuthority &&
              output.removals == 0,
          "a stale session cannot retire the current target");
    Check(core.StopSession(authority) == CommandResult::Applied &&
              core.mode() == RoutingMode::Disabled && output.removals == 1,
          "the exact session neutralizes and removes only its owned target");
}

} // namespace

int main() {
    ExactAuthorityAndOrderedGameplay();
    InvalidBudgetsFailBeforeTargetOwnership();
    QueueBoundsNeverReplayStaleInput();
    OverlayNeutralAckAndHealthyCachedClose();
    CloseRequiresReleaseAndUsesHysteresis();
    PendingContainedReportsCannotEscapeCloseBarrier();
    LeaseAndOutputFailuresAreSafetyFaults();
    LatencyOutliersRemainAcceptanceEvidence();
    GuardianRequiresExactIdentityAndFinalRecheck();
    HidHideJournalPreservesOtherOwners();
    StaleHostCanRecontainAwaitingNeutral();
    StopIsExactAndGenerationBound();
    std::cout << "ControllerIsolationCoreTests passed (" << checks
              << " checks)\n";
    return 0;
}
