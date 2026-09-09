#include "../../src/OverlayPlatformInterop/ControllerIsolationRoutingSession.h"

#include <cstdlib>
#include <functional>
#include <iostream>
#include <thread>
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
    std::function<void()> duringSample;

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
        if (duringSample) {
            auto callback = std::move(duringSample);
            callback();
        }
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

struct HostInputEvent final {
    GamepadState state{};
    std::uint64_t ordinal{};
};

struct FakeHostInputSink final : ControllerIsolationHostInputSink {
    std::vector<HostInputEvent> events;
    bool accepts{true};
    bool PublishInput(
        const GamepadState& state,
        const std::uint64_t ordinal) noexcept override {
        if (!accepts) return false;
        events.push_back({state, ordinal});
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

    FakeSource heldSource;
    heldSource.current.state.buttons = 0x1000;
    FakeOutput heldOutput;
    ControllerIsolationRoutingSession held{heldSource, heldOutput, guide};
    Check(held.PrepareSession(authority, Enrollment(), 0) ==
              ControllerIsolationRoutingResult::RejectedState &&
              heldSource.prepareCalls == 1 && heldOutput.opens == 0,
          "existing neutral policy rejects held input before target creation");
}

void SampleFenceContainsOnlyPreFenceHistory() {
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    GamepadState held;
    held.buttons = 0x1000;
    source.duringSample = [&] {
        Check(source.Publish(
                  ControllerReaderEventKind::Reading, 99, 0, held),
              "sample-race fixture publishes its delayed pre-fence callback");
    };
    Prepare(session, authority, enrollment, source, output);
    Check(session.Pump(0) == ControllerIsolationRoutingResult::Applied &&
              session.state() == ControllerIsolationRoutingState::PreparedNeutral,
          "delayed callback older than prepared current is contained without fault");
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied,
          "cached exact neutral remains valid after pre-fence history is contained");
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 101, 21, held) &&
              session.Pump(21) == ControllerIsolationRoutingResult::Applied &&
              output.reports.back() == held,
          "a real post-fence edge is preserved and forwarded after resume");
    Check(session.EnterOverlay(authority, 22) ==
              ControllerIsolationRoutingResult::Applied,
          "sample-fence fixture re-enters contained interaction");
    source.current = {110, 23, {}, true};
    Check(session.CloseOverlay(authority, 10, 23) ==
              ControllerIsolationRoutingResult::Waiting &&
              source.Publish(
                  ControllerReaderEventKind::Reading, 109, 22, held) &&
              session.Pump(43) == ControllerIsolationRoutingResult::Applied &&
              session.state() == ControllerIsolationRoutingState::Playing,
          "callback arriving after close but older than its current sample is contained");
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 111, 44, held) &&
              session.Pump(44) == ControllerIsolationRoutingResult::Applied &&
              output.reports.back() == held,
          "post-close reading newer than the sample fence is still forwarded");
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

void ReservedProducerDefersBarrierAndCachedNeutral() {
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, enrollment, source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied,
          "producer-pause fixture reaches Playing");

    GamepadState held;
    held.buttons = 0x1000;
    session.readerIngress().PauseNextProducerForTest();
    bool published{};
    std::thread producer([&] {
        published = source.Publish(
            ControllerReaderEventKind::Reading, 101, 21, held);
    });
    for (std::size_t attempt = 0;
         attempt < 10'000 &&
             !session.readerIngress().ProducerPausedForTest(); ++attempt) {
        std::this_thread::yield();
    }
    Check(session.readerIngress().ProducerPausedForTest(),
          "routing fixture pauses an actual producer after ticket reservation");
    const auto reportsBeforeEnter = output.reports.size();
    Check(session.EnterOverlay(authority, 21) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.state() == ControllerIsolationRoutingState::Playing &&
              output.reports.size() == reportsBeforeEnter,
          "EnterOverlay remains pending without an early neutral or applied result");
    Check(ServiceControllerIsolationRoutingBeforeControlWait(session, 22) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.state() == ControllerIsolationRoutingState::Playing,
          "pending producer yields to the serialized owner without false fault");
    session.readerIngress().ReleaseProducerForTest();
    producer.join();
    Check(published &&
              ServiceControllerIsolationRoutingBeforeControlWait(session, 23) ==
                  ControllerIsolationRoutingResult::Applied &&
              session.state() ==
                  ControllerIsolationRoutingState::OverlayInteraction &&
              output.reports[output.reports.size() - 2] == held &&
              output.reports.back() == GamepadState{},
          "pre-barrier reading drains before neutral and Enter becomes applied");

    source.current = {102, 24, {}, true};
    Check(session.CloseOverlay(authority, 10, 24) ==
              ControllerIsolationRoutingResult::Waiting,
          "close enters its neutral wait");
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 103, 25, {}),
          "a neutral prefix is published inside the next drain barrier");
    session.readerIngress().PauseNextProducerForTest();
    bool secondPublished{};
    std::thread secondProducer([&] {
        secondPublished = source.Publish(
            ControllerReaderEventKind::Reading, 104, 26, held);
    });
    for (std::size_t attempt = 0;
         attempt < 10'000 &&
             !session.readerIngress().ProducerPausedForTest(); ++attempt) {
        std::this_thread::yield();
    }
    Check(session.Pump(44) == ControllerIsolationRoutingResult::Waiting &&
              session.state() == ControllerIsolationRoutingState::AwaitingPlaying,
          "neutral prefix cannot resume while a held suffix is unpublished");
    session.readerIngress().ReleaseProducerForTest();
    secondProducer.join();
    source.current = {104, 26, held, true};
    Check(secondPublished &&
              session.Pump(45) == ControllerIsolationRoutingResult::Waiting &&
              session.state() == ControllerIsolationRoutingState::AwaitingPlaying,
          "completed barrier evaluates its final held reading without transient resume");
}

void PendingEnterCanBeRecontainedOnHostLoss() {
    const auto authority = Authority();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, Enrollment(), source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied,
          "host-loss fixture reaches Playing");
    session.readerIngress().PauseNextProducerForTest();
    bool published{};
    GamepadState held;
    held.buttons = 0x1000;
    std::thread producer([&] {
        published = source.Publish(
            ControllerReaderEventKind::Reading, 101, 21, held);
    });
    for (std::size_t attempt = 0;
         attempt < 10'000 &&
             !session.readerIngress().ProducerPausedForTest(); ++attempt) {
        std::this_thread::yield();
    }
    Check(session.EnterOverlay(authority, 21) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.HoldContained(authority, 22) ==
                  ControllerIsolationRoutingResult::Applied &&
              session.state() ==
                  ControllerIsolationRoutingState::OverlayInteraction &&
              output.reports.back() == GamepadState{},
          "uncertain host loss cancels queued entry and keeps output contained");
    session.readerIngress().ReleaseProducerForTest();
    producer.join();
    Check(published && session.Pump(23) ==
              ControllerIsolationRoutingResult::Applied &&
              output.reports.back() == GamepadState{},
          "late reserved input remains host-only after recontainment");
}

void ControlTrafficCannotStarveReadingPump() {
    const auto authority = Authority();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, Enrollment(), source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied,
          "control-traffic fixture reaches Playing");
    GamepadState down;
    down.buttons = 0x2000;
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 101, 21, down),
          "reading is queued before the synthetic request stream");
    for (std::uint64_t request = 0; request < 64; ++request) {
        Check(ServiceControllerIsolationRoutingBeforeControlWait(
                  session, 21 + request) ==
                  ControllerIsolationRoutingResult::Applied &&
                  session.Heartbeat(authority, 21 + request) ==
                      ControllerIsolationRoutingResult::Applied,
              "production pre-request service remains live during heartbeat traffic");
    }
    Check(output.reports.back() == down,
          "one admitted reading cannot be starved by ready control requests");
}

void StuckProducerFaultsOnlyAfterRealQueueAge() {
    const auto authority = Authority();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    ControllerIsolationRoutingSession session{source, output, guide};
    Prepare(session, authority, Enrollment(), source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied,
          "stuck-producer fixture reaches Playing");
    session.readerIngress().PauseNextProducerForTest();
    bool published{};
    std::thread producer([&] {
        published = source.Publish(
            ControllerReaderEventKind::Reading, 101, 21);
    });
    for (std::size_t attempt = 0;
         attempt < 10'000 &&
             !session.readerIngress().ProducerPausedForTest(); ++attempt) {
        std::this_thread::yield();
    }
    Check(session.EnterOverlay(authority, 21) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(121) == ControllerIsolationRoutingResult::Waiting &&
              session.state() == ControllerIsolationRoutingState::Playing,
          "reserved producer remains pending through the exact queue-age budget");
    Check(session.Pump(122) == ControllerIsolationRoutingResult::Faulted &&
              output.removals == 1,
          "final acquire check faults only after the real queue-age budget");
    session.readerIngress().ReleaseProducerForTest();
    producer.join();
    Check(published,
          "paused callback can retire after the session has failed closed");
}

void ContainedInputPreservesTapAndSustainedAnalogOrder() {
    const auto authority = Authority();
    FakeSource source;
    FakeOutput output;
    FakeGuideSink guide;
    FakeHostInputSink host;
    ControllerIsolationRoutingSession session{
        source, output, guide, {}, &host};
    Prepare(session, authority, Enrollment(), source, output);
    Check(session.CommitPlaying(authority, 10, 0) ==
              ControllerIsolationRoutingResult::Waiting &&
              session.Pump(20) == ControllerIsolationRoutingResult::Applied &&
              session.EnterOverlay(authority, 21) ==
                  ControllerIsolationRoutingResult::Applied,
          "host-input fixture reaches contained overlay ownership");
    GamepadState pressed;
    pressed.buttons = 0x1000;
    Check(source.Publish(
              ControllerReaderEventKind::Reading, 101, 22, pressed) &&
              source.Publish(
                  ControllerReaderEventKind::Reading, 102, 22, {}) &&
              session.Pump(22) == ControllerIsolationRoutingResult::Applied,
          "a complete short press and release enters one contained drain");
    for (std::uint64_t index = 0; index < 48; ++index) {
        GamepadState analog;
        analog.leftThumbX = static_cast<std::int16_t>(index * 500 - 12'000);
        Check(source.Publish(
                  ControllerReaderEventKind::Reading,
                  103 + index, 23 + index, analog) &&
                  session.Pump(23 + index) ==
                      ControllerIsolationRoutingResult::Applied,
              "sustained analog changes remain admitted under slower host consumption");
    }
    Check(host.events.size() == 50 &&
              host.events[0].state == pressed &&
              host.events[1].state == GamepadState{} &&
              host.events.front().ordinal < host.events.back().ordinal &&
              output.reports.back() == GamepadState{},
          "contained host stream retains press, release, and every analog state without game output");
}

} // namespace

int main() {
    HeartbeatCannotActivateDormantWorker();
    PreparationFailsBeforeOutputForUntrustedDevice();
    SampleFenceContainsOnlyPreFenceHistory();
    TotalOrderPreservesGameplayAndContainsOverlayInput();
    LeaseDisconnectAndSinkFailuresRetireOwnedTarget();
    ConflictingSourceTimestampFailsClosed();
    ReservedProducerDefersBarrierAndCachedNeutral();
    PendingEnterCanBeRecontainedOnHostLoss();
    ControlTrafficCannotStarveReadingPump();
    StuckProducerFaultsOnlyAfterRealQueueAge();
    ContainedInputPreservesTapAndSustainedAnalogOrder();
    std::cout << "ControllerIsolationRoutingSessionTests passed " << checks
              << " checks.\n";
    return EXIT_SUCCESS;
}
