#include "../../src/OverlayPlatformInterop/ControllerIsolationRoutingSession.h"

#include <cstdlib>
#include <iostream>
#include <utility>
#include <vector>

namespace {

using namespace widgetrail::isolation;

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

RoutingAuthority Authority(const std::uint64_t generation = 1) {
    return {generation, generation + 10, generation + 20, generation + 30};
}

SelectedControllerEnrollment Enrollment(const std::uint8_t seed = 1) {
    SelectedControllerEnrollment result;
    result.enrollmentToken = seed;
    result.deviceId[0] = seed;
    result.deviceRootId[0] = static_cast<std::uint8_t>(seed + 1);
    result.containerId[0] = static_cast<std::uint8_t>(seed + 2);
    result.normalizedPnpPathDigest[0] =
        static_cast<std::uint8_t>(seed + 3);
    result.vendorId = 0x045E;
    result.productId = static_cast<std::uint16_t>(0x0200 + seed);
    result.deviceFamily = 1;
    result.connected = true;
    result.gamepadSupported = true;
    return result;
}

struct FakeSource final : SelectedControllerSource {
    SelectedControllerPrepareStatus prepareStatus{
        SelectedControllerPrepareStatus::Ready};
    SelectedControllerCurrent current{100, 0, {}, true};
    ControllerIsolationReaderIngress* ingress{};
    int prepareCalls{};
    int sampleCalls{};
    int stopCalls{};

    SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment&,
        ControllerIsolationReaderIngress& value) noexcept override {
        ++prepareCalls;
        if (prepareStatus == SelectedControllerPrepareStatus::Ready)
            ingress = &value;
        return prepareStatus;
    }

    bool SampleCurrent(SelectedControllerCurrent& value) noexcept override {
        ++sampleCalls;
        value = current;
        return current.connected;
    }

    void Stop() noexcept override {
        ++stopCalls;
        ingress = nullptr;
    }

    bool Publish(
        const ControllerReaderEventKind kind,
        const std::uint64_t timestamp,
        const std::uint64_t observedAt,
        const GamepadState& state = {},
        const bool connected = true) noexcept {
        return ingress && ingress->Publish(
            kind, timestamp, observedAt, state, connected);
    }
};

struct FakeOutput final : ControllerIsolationOutput {
    bool openSucceeds{true};
    bool submitSucceeds{true};
    int opens{};
    int removals{};
    std::vector<GamepadState> reports;

    bool OpenOwnedTarget() noexcept override {
        ++opens;
        return openSucceeds;
    }

    bool Submit(const GamepadState& state) noexcept override {
        reports.push_back(state);
        return submitSucceeds;
    }

    void RemoveOwnedTarget() noexcept override { ++removals; }
};

struct GuideEvent final {
    bool pressed{};
    std::uint64_t timestamp{};
    std::uint64_t ordinal{};
};

struct FakeGuideSink final : ControllerIsolationGuideSink {
    bool accepts{true};
    std::vector<GuideEvent> events;

    bool PublishGuide(
        const bool pressed,
        const std::uint64_t timestamp,
        const std::uint64_t ordinal) noexcept override {
        if (!accepts) return false;
        events.push_back({pressed, timestamp, ordinal});
        return true;
    }
};

void Prepare(
    ControllerIsolationRoutingSession& session,
    const RoutingAuthority& authority,
    const SelectedControllerEnrollment& enrollment,
    FakeSource& source,
    FakeOutput& output) {
    Check(session.PrepareSession(authority, enrollment, 0) ==
              ControllerIsolationRoutingResult::Applied,
          "explicit preparation admits exact selected-device authority");
    Check(session.state() == ControllerIsolationRoutingState::PreparedNeutral &&
              source.prepareCalls == 1 && source.sampleCalls == 1 &&
              output.opens == 1 && output.reports.size() == 2 &&
              output.reports[0] == GamepadState{} &&
              output.reports[1] == GamepadState{},
          "preparation acquires input before output and publishes neutral only");
}

void HeartbeatCannotActivateDormantWorker() {
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Check(session.Heartbeat(Authority(), 1) ==
              ControllerIsolationRoutingResult::RejectedAuthority &&
              session.Pump(1) ==
                  ControllerIsolationRoutingResult::RejectedState,
          "heartbeat and pump cannot activate a disabled session");
    Check(source.prepareCalls == 0 && output.opens == 0 &&
              output.reports.empty(),
          "no GameInput or virtual target owner runs before PrepareSession");
}

void PreparationFailsBeforeOutputForUntrustedDevice() {
    const auto authority = Authority();
    auto enrollment = Enrollment();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession invalid{source, output, guide};
    enrollment.knownVirtualOutput = true;
    Check(invalid.PrepareSession(authority, enrollment, 0) ==
              ControllerIsolationRoutingResult::RejectedAuthority &&
              source.prepareCalls == 0 && output.opens == 0,
          "known virtual output cannot reach reader or output acquisition");

    source.prepareStatus = SelectedControllerPrepareStatus::IdentityMismatch;
    ControllerIsolationRoutingSession mismatch{source, output, guide};
    Check(mismatch.PrepareSession(authority, Enrollment(), 0) ==
              ControllerIsolationRoutingResult::ReaderUnavailable &&
              source.prepareCalls == 1 && output.opens == 0 &&
              mismatch.state() == ControllerIsolationRoutingState::Fault,
          "reader identity mismatch fails before virtual target creation");

    FakeSource ready;
    FakeOutput unavailable;
    unavailable.openSucceeds = false;
    ControllerIsolationRoutingSession noOutput{ready, unavailable, guide};
    Check(noOutput.PrepareSession(authority, Enrollment(), 0) ==
              ControllerIsolationRoutingResult::OutputUnavailable &&
              ready.prepareCalls == 1 && ready.stopCalls == 1 &&
              unavailable.opens == 1,
          "output creation happens only after exact reader preparation");
}

void TotalOrderPreservesGameplayAndContainsOverlayInput() {
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, enrollment, source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(19) == ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied &&
              session.state() == ControllerIsolationRoutingState::Playing,
          "commit waits for the existing bounded neutral dwell before play");

    GamepadState a;
    a.buttons = 0x1000;
    Check(source.Publish(ControllerReaderEventKind::Reading, 101, 21, a) &&
              session.Pump(21) == ControllerIsolationRoutingResult::Applied &&
              output.reports.back() == a,
          "playing forwards the exact admitted selected-controller reading");
    Check(source.Publish(
              ControllerReaderEventKind::GuidePressed, 102, 22) &&
              source.Publish(
                  ControllerReaderEventKind::GuideReleased, 103, 22) &&
              session.Pump(22) == ControllerIsolationRoutingResult::Applied &&
              guide.events.size() == 2 && guide.events[0].pressed &&
              !guide.events[1].pressed &&
              guide.events[0].ordinal < guide.events[1].ordinal,
          "Guide remains a passive ordered signal outside gamepad output");

    GamepadState b;
    b.buttons = 0x2000;
    Check(source.Publish(ControllerReaderEventKind::Reading, 104, 23, b) &&
              session.EnterOverlay(authority, 23) ==
                  ControllerIsolationRoutingResult::Applied &&
              session.state() ==
                  ControllerIsolationRoutingState::OverlayInteraction &&
              output.reports.size() >= 2 &&
              output.reports[output.reports.size() - 2] == b &&
              output.reports.back() == GamepadState{},
          "pre-command input drains before the enter barrier then output neutralizes");

    const auto reportsAtOverlayEntry = output.reports.size();
    GamepadState x;
    x.buttons = 0x4000;
    Check(source.Publish(ControllerReaderEventKind::Reading, 105, 24, x) &&
              session.Pump(24) == ControllerIsolationRoutingResult::Applied &&
              output.reports.size() == reportsAtOverlayEntry,
          "overlay interaction observes input without forwarding it to the game");

    source.current = {105, 24, x, true};
    Check(session.CloseOverlay(authority, 10, 25) ==
              ControllerIsolationRoutingResult::Waiting,
          "close captures the exact held state without synthesizing release");
    source.current = {106, 26, {}, true};
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 106, 26) &&
              session.Pump(26) == ControllerIsolationRoutingResult::Waiting &&
              source.Publish(
                  ControllerReaderEventKind::Reading, 107, 46) &&
              session.Pump(46) == ControllerIsolationRoutingResult::Applied &&
              session.state() == ControllerIsolationRoutingState::Playing,
          "queued neutral readings resume only after the bounded dwell");
    GamepadState y;
    y.buttons = 0x8000;
    Check(source.Publish(ControllerReaderEventKind::Reading, 108, 47, y) &&
              session.Pump(47) == ControllerIsolationRoutingResult::Applied &&
              output.reports.back() == y,
          "post-barrier input resumes without replaying contained input");
    Check(session.Stop(authority) == ControllerIsolationRoutingResult::Applied &&
              output.removals == 1 && source.stopCalls == 1,
          "explicit stop retires only the owned output and selected source");
}

void LeaseDisconnectAndSinkFailuresRetireOwnedTarget() {
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    FakeSource leaseSource;
    FakeOutput leaseOutput;
    FakeGuideSink leaseGuide;
    ControllerIsolationRoutingSession lease{
        leaseSource, leaseOutput, leaseGuide};
    Prepare(lease, authority, enrollment, leaseSource, leaseOutput);
    Check(lease.Heartbeat(authority, 200) ==
              ControllerIsolationRoutingResult::Applied &&
              lease.Pump(450) == ControllerIsolationRoutingResult::Applied &&
              lease.Pump(451) == ControllerIsolationRoutingResult::Faulted &&
              leaseOutput.removals == 1,
          "host lease expiry fails closed even while no input arrives");

    FakeSource disconnectSource;
    FakeOutput disconnectOutput;
    FakeGuideSink disconnectGuide;
    ControllerIsolationRoutingSession disconnect{
        disconnectSource, disconnectOutput, disconnectGuide};
    Prepare(
        disconnect, authority, enrollment, disconnectSource,
        disconnectOutput);
    Check(disconnectSource.Publish(
              ControllerReaderEventKind::Disconnected, 101, 1, {}, false) &&
              disconnect.Pump(1) == ControllerIsolationRoutingResult::Faulted &&
              disconnectOutput.removals == 1,
          "selected-device disconnect retires the exact owned target");

    FakeSource sinkSource;
    FakeOutput sinkOutput;
    FakeGuideSink sinkGuide;
    sinkGuide.accepts = false;
    ControllerIsolationRoutingSession sink{sinkSource, sinkOutput, sinkGuide};
    Prepare(sink, authority, enrollment, sinkSource, sinkOutput);
    Check(sinkSource.Publish(
              ControllerReaderEventKind::GuidePressed, 101, 1) &&
              sink.Pump(1) == ControllerIsolationRoutingResult::Faulted &&
              sinkOutput.removals == 1,
          "bounded Guide transport pressure cannot silently drop a signal");
}

void ConflictingSourceTimestampFailsClosed() {
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, enrollment, source, output);
    GamepadState changed;
    changed.buttons = 0x1000;
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 100, 1, changed) &&
              session.Pump(1) == ControllerIsolationRoutingResult::Faulted &&
              output.removals == 1,
          "one source timestamp cannot authorize two different states");
}

} // namespace

int main() {
    HeartbeatCannotActivateDormantWorker();
    PreparationFailsBeforeOutputForUntrustedDevice();
    TotalOrderPreservesGameplayAndContainsOverlayInput();
    LeaseDisconnectAndSinkFailuresRetireOwnedTarget();
    ConflictingSourceTimestampFailsClosed();
    std::cout << "ControllerIsolationRoutingSessionTests passed " << checks
              << " checks.\n";
    return EXIT_SUCCESS;
}
