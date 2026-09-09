#include "../../src/OverlayPlatformInterop/ControllerIsolationGuardianSession.h"
#include "../../src/OverlayPlatformInterop/ControllerIsolationGuardianLifetime.h"
#include "../../src/OverlayPlatformInterop/ControllerIsolationHostSession.h"
#include "../../src/OverlayPlatformInterop/ControllerIsolationInputTransport.h"

#include <cstdlib>
#include <iostream>
#include <thread>
#include <vector>

namespace {
using namespace widgetrail::isolation;
int checks{};
void Check(const bool value, const char* message) {
    ++checks;
    if (!value) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}
} // namespace

int main() {
    Check(GuardianStartupAuthorityMatches({1, 2, 3, 4}, {1, 2, 3, 4}) &&
              !GuardianStartupAuthorityMatches({1, 2, 3, 4}, {1, 2, 3, 5}) &&
              !GuardianStartupAuthorityMatches({}, {1, 2, 3, 4}),
          "a delayed Guardian can claim only its exact expected journal authority");

    ControllerIsolationHostSession responsePath;
    responsePath.BeginResponsePathForTest();
    std::wstring responseDiagnostic;
    ControlFrame heartbeat;
    heartbeat.observedAtMilliseconds = static_cast<std::uint64_t>(
        ControlProgress::AwaitingNeutral);
    heartbeat.state.buttons = 0x1000;
    heartbeat.deviceEnrollmentToken = 41;
    heartbeat.inputBatch.count = 1;
    heartbeat.inputBatch.firstIngressOrdinal = 1;
    heartbeat.inputBatch.lastIngressOrdinal = 1;
    heartbeat.inputBatch.interactionGeneration = 7;
    heartbeat.inputBatch.states[0].buttons = 0x2000;
    Check(responsePath.ApplySuccessfulResponseForTest(
              heartbeat, responseDiagnostic),
          "the HostSession heartbeat response path ingests state, Guide, and input");
    const auto heartbeatReading = responsePath.TakeBufferedResponseForTest();
    Check(heartbeatReading.progress == ControlProgress::AwaitingNeutral &&
              heartbeatReading.state.buttons == 0x2000 &&
              heartbeatReading.guideEvent == 41 &&
              heartbeatReading.remainingInputStates == 0 &&
              responsePath.interactionGenerationForTest() == 7,
          "the HostSession exposes the exact correlated heartbeat payload");

    responsePath.BeginResponsePathForTest(false);
    ControlFrame managementStatus;
    managementStatus.observedAtMilliseconds = static_cast<std::uint64_t>(
        ControlProgress::AwaitingNeutral);
    Check(responsePath.ApplySuccessfulResponseForTest(
              managementStatus, responseDiagnostic) &&
              responsePath.interactionGenerationForTest() == 7,
          "an intentionally payload-free management reply cannot retire interaction input");
    responsePath.BeginResponsePathForTest();

    ControlFrame containmentProgress;
    containmentProgress.observedAtMilliseconds = static_cast<std::uint64_t>(
        ControlProgress::Contained);
    containmentProgress.state.buttons = 0x4000;
    containmentProgress.inputBatch.interactionGeneration = 7;
    Check(responsePath.ApplySuccessfulResponseForTest(
              containmentProgress, responseDiagnostic),
          "containment progress retains the active HostSession interaction");
    const auto containedReading = responsePath.TakeBufferedResponseForTest();
    Check(containedReading.progress == ControlProgress::Contained &&
              containedReading.state.buttons == 0x4000 &&
              containedReading.guideEvent == 0 &&
              responsePath.interactionGenerationForTest() == 7,
          "containment progress updates current state without retiring input");

    ControlFrame queuedBeforeClose = containmentProgress;
    queuedBeforeClose.inputBatch.count = 1;
    queuedBeforeClose.inputBatch.firstIngressOrdinal = 2;
    queuedBeforeClose.inputBatch.lastIngressOrdinal = 2;
    queuedBeforeClose.inputBatch.states[0].buttons = 0x8000;
    Check(responsePath.ApplySuccessfulResponseForTest(
              queuedBeforeClose, responseDiagnostic),
          "the HostSession has queued input to retire at close");
    ControlFrame closeResponse;
    closeResponse.observedAtMilliseconds = static_cast<std::uint64_t>(
        ControlProgress::Playing);
    Check(responsePath.ApplySuccessfulResponseForTest(
              closeResponse, responseDiagnostic),
          "the HostSession close response retires the active generation");
    const auto closedReading = responsePath.TakeBufferedResponseForTest();
    Check(closedReading.progress == ControlProgress::Playing &&
              closedReading.state == GamepadState{} &&
              closedReading.remainingInputStates == 0 &&
              responsePath.interactionGenerationForTest() == 0,
          "no queued UI input survives the HostSession close response");
    Check(!responsePath.ApplySuccessfulResponseForTest(
              queuedBeforeClose, responseDiagnostic),
          "a delayed response from the retired HostSession generation fails closed");
    Check(DecideGuardianStartupDisposition(
              ControllerIsolationJournalPhase::Prepared, true,
              ControllerIsolationJournalPhase::Playing) ==
              GuardianStartupDisposition::Serve,
          "successful Guardian start serves instead of self-classifying duplicate");
    Check(DecideGuardianStartupDisposition(
              ControllerIsolationJournalPhase::Playing, false,
              ControllerIsolationJournalPhase::Playing) ==
              GuardianStartupDisposition::RecoveryRequired &&
              DecideGuardianStartupDisposition(
                  ControllerIsolationJournalPhase::Prepared, false,
                  ControllerIsolationJournalPhase::Prepared) ==
                  GuardianStartupDisposition::RecoveryRequired,
          "stale or failed startup remains recovery-required");

    HANDLE admittedHost = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    Check(admittedHost != nullptr &&
              ObserveAdmittedHostLifetime(admittedHost) ==
                  AdmittedHostLifetime::Running,
          "an authenticated live host remains the interaction owner after pipe loss");
    std::thread delayedExit([&] { Check(SetEvent(admittedHost),
        "delayed host exit signal is published"); });
    delayedExit.join();
    Check(
              ObserveAdmittedHostLifetime(admittedHost) ==
                  AdmittedHostLifetime::Exited,
          "only the signaled admitted host handle proves interaction-owner death");
    CloseHandle(admittedHost);
    Check(ObserveAdmittedHostLifetime(nullptr) ==
              AdmittedHostLifetime::Unavailable,
          "missing host identity never synthesizes death proof");
    Check(AdmitHostReconnect(AdmittedHostLifetime::Running, true) &&
              !AdmitHostReconnect(AdmittedHostLifetime::Running, false) &&
              !AdmitHostReconnect(AdmittedHostLifetime::Unavailable, false) &&
              AdmitHostReconnect(AdmittedHostLifetime::Exited, false),
          "only the same authenticated process or proven prior exit supersedes retained ownership");
    Check(ClassifyGuardianLifetime(true, false, ERROR_INVALID_PARAMETER,
                                  false, 0) ==
              GuardianLifetimeStatus::Exited &&
              ClassifyGuardianLifetime(true, true, 0, true, WAIT_TIMEOUT) ==
                  GuardianLifetimeStatus::Running &&
              ClassifyGuardianLifetime(true, true, 0, false, WAIT_OBJECT_0) ==
                  GuardianLifetimeStatus::IdentityMismatch &&
              ClassifyGuardianLifetime(false, false, ERROR_INVALID_PARAMETER,
                                       false, 0) ==
                  GuardianLifetimeStatus::Unavailable,
          "orphan recovery requires persisted exact Guardian death proof");

    ControllerIsolationInputEventQueue worker;
    ControllerIsolationInputBatchQueue guardian;
    ControllerIsolationHostStateQueue host;
    std::vector<GamepadState> expected;
    std::vector<GamepadState> observed;
    Check(worker.BeginInteraction(),
          "first overlay interaction owns a nonzero input generation");
    for (std::uint64_t index = 0; index < 200; ++index) {
        GamepadState state;
        state.leftThumbX = static_cast<std::int16_t>(index * 250 - 25'000);
        if (index == 40) state.buttons = 0x1000;
        expected.push_back(state);
        Check(worker.Push(state, index + 1),
              "worker accepts bounded ordered input event");
        if (worker.size() == ControllerIsolationInputBatchCapacity)
            Check(guardian.Push(worker.TakeBatch()),
                  "worker batch enters bounded Guardian queue");
        if ((index + 1) % 24 == 0)
            Check(host.Push(guardian.TakeBatch()),
                  "slower host accepts one queued Guardian batch");
        if ((index + 1) % 32 == 0) {
            GamepadState stateAtHost;
            if (host.Pop(stateAtHost)) observed.push_back(stateAtHost);
        }
    }
    if (worker.size() != 0)
        Check(guardian.Push(worker.TakeBatch()),
              "final partial worker batch remains ordered");
    while (guardian.size() != 0)
        Check(host.Push(guardian.TakeBatch()),
              "Guardian backlog drains without per-poll state loss");
    GamepadState stateAtHost;
    while (host.Pop(stateAtHost)) observed.push_back(stateAtHost);
    Check(observed == expected && observed[40].buttons == 0x1000 &&
              observed[41].buttons == 0,
          "press, release, and sustained analog reports survive slower host consumption");

    GamepadState oldPress;
    oldPress.buttons = 0x1000;
    Check(worker.Push(oldPress, 201),
          "old interaction queues a final press before close");
    const auto oldBatch = worker.TakeBatch();
    const auto progressHeartbeat = worker.TakeBatch();
    Check(guardian.Push(oldBatch) && guardian.Push(progressHeartbeat) &&
              guardian.size() == 1 && host.Push(guardian.TakeBatch()) &&
              host.Push(progressHeartbeat) && host.size() == 1,
          "empty containment-progress heartbeats preserve queued current-generation input");
    worker.RetireInteraction();
    Check(guardian.Push(worker.TakeBatch()) &&
              host.Push(guardian.TakeBatch()) && host.size() == 0 &&
              host.interactionGeneration() == 0,
          "close retires every queued old-interaction UI input");
    Check(worker.BeginInteraction(),
          "reopen owns a fresh monotonic input generation");
    Check(guardian.Push(worker.TakeBatch()) &&
              host.Push(guardian.TakeBatch()) &&
              !guardian.Push(oldBatch) && !host.Push(oldBatch),
          "a delayed old generation cannot re-enter Guardian or host after reopen");
    GamepadState newPress;
    newPress.buttons = 0x2000;
    Check(worker.Push(newPress, 202) &&
              guardian.Push(worker.TakeBatch()) &&
              host.Push(guardian.TakeBatch()) && host.Pop(stateAtHost) &&
              stateAtHost == newPress,
          "only the fresh interaction generation reaches reopened UI input");

    std::cout << "ControllerIsolationInputTransportTests passed (" << checks
              << " checks)\n";
}
