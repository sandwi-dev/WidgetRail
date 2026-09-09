#include "../../src/OverlayPlatformInterop/ControllerIsolationProcessOwner.h"

#include <windows.h>

#include <array>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <string_view>
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

ControllerIsolationNonce Nonce(const std::uint8_t seed = 1) {
    ControllerIsolationNonce nonce{};
    for (std::size_t index = 0; index < nonce.size(); ++index)
        nonce[index] = static_cast<std::uint8_t>(seed + index);
    return nonce;
}

std::filesystem::path Sibling(const wchar_t* fileName) {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    Check(length != 0 && length < path.size(),
          "test executable path is available");
    return std::filesystem::path(
        std::wstring_view(path.data(), length)).parent_path() / fileName;
}

DWORD RunUnconfigured(const std::filesystem::path& executable) {
    std::wstring command = L"\"" + executable.wstring() + L"\"";
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    Check(CreateProcessW(
              executable.c_str(), mutableCommand.data(), nullptr, nullptr,
              FALSE, CREATE_NO_WINDOW, nullptr,
              executable.parent_path().c_str(), &startup, &process) != FALSE,
          "unconfigured fake helper starts for launch-guard proof");
    CloseHandle(process.hThread);
    Check(WaitForSingleObject(process.hProcess, 2'000) == WAIT_OBJECT_0,
          "unconfigured fake helper refuses without waiting indefinitely");
    DWORD exitCode{};
    Check(GetExitCodeProcess(process.hProcess, &exitCode) != FALSE,
          "unconfigured fake helper exit code is observable");
    CloseHandle(process.hProcess);
    return exitCode;
}

ControllerIsolationProcessOwner StartedGuardian() {
    ControllerIsolationProcessOwner guardian;
    std::wstring error;
    Check(guardian.Start(
              Sibling(L"ControllerIsolationGuardianTestHost.exe"),
              Authority(), 2, ControllerIsolationStartupTimeoutMilliseconds,
              error),
          "authenticated fake guardian and worker start");
    Check(guardian.startupResponse().kind ==
              ControlMessageKind::HelloAccepted &&
              guardian.startupResponse().processId != 0,
          "guardian admits the session only after its exact worker is ready");
    return guardian;
}

void ProtocolRejectsWrongAuthorityAndShape() {
    const auto nonce = Nonce();
    ControlSessionGate gate(nonce, Authority());
    auto hello = MakeControlFrame(
        ControlMessageKind::Hello, nonce, Authority(), 1);
    Check(gate.Admit(hello) == ControlFrameValidation::Accepted,
          "exact first frame is admitted");

    ControlSessionGate wrongNonceGate(nonce, Authority());
    auto wrongNonce = hello;
    wrongNonce.nonce[0] ^= 0xFF;
    Check(wrongNonceGate.Admit(wrongNonce) ==
              ControlFrameValidation::WrongNonce,
          "wrong nonce is rejected");

    ControlSessionGate wrongAuthorityGate(nonce, Authority());
    auto wrongAuthority = hello;
    wrongAuthority.authority = Authority(2);
    Check(wrongAuthorityGate.Admit(wrongAuthority) ==
              ControlFrameValidation::WrongAuthority,
          "wrong generation and lease authority are rejected");

    ControlSessionGate malformedGate(nonce, Authority());
    auto malformed = hello;
    malformed.size = 0;
    Check(malformedGate.Admit(malformed) ==
              ControlFrameValidation::InvalidShape,
          "malformed fixed frame is rejected");

    ControlSessionGate sequenceGate(nonce, Authority());
    auto skipped = hello;
    skipped.sequence = 2;
    Check(sequenceGate.Admit(skipped) ==
              ControlFrameValidation::WrongSequence,
          "skipped or replayed sequence is rejected");

    auto heartbeat = MakeControlFrame(
        ControlMessageKind::Heartbeat, nonce, Authority(), 2);
    auto heartbeatResponse = MakeControlFrame(
        ControlMessageKind::Heartbeat, nonce, Authority(), 2);
    Check(ValidateControlResponse(
              ControlMessageKind::Heartbeat, heartbeatResponse, nonce,
              Authority(), heartbeat.sequence) ==
              ControlResponseValidation::Accepted,
          "heartbeat admits only its exact successful response");
    heartbeatResponse.kind = ControlMessageKind::Terminal;
    Check(ValidateControlResponse(
              ControlMessageKind::Heartbeat, heartbeatResponse, nonce,
              Authority(), heartbeat.sequence) ==
              ControlResponseValidation::WrongResponseKind,
          "a successful terminal cannot impersonate a heartbeat response");
    heartbeatResponse.status = ERROR_ACCESS_DENIED;
    Check(ValidateControlResponse(
              ControlMessageKind::Heartbeat, heartbeatResponse, nonce,
              Authority(), heartbeat.sequence) ==
              ControlResponseValidation::RemoteFailure,
          "a nonzero terminal status remains a remote failure");
}

void DirectLaunchFailsBeforeAnyBackendAdmission() {
    Check(RunUnconfigured(Sibling(L"ControllerIsolationFakeWorker.exe")) ==
              ERROR_ACCESS_DENIED,
          "fake worker direct launch fails closed");
    Check(RunUnconfigured(
              Sibling(L"ControllerIsolationGuardianTestHost.exe")) ==
              ERROR_ACCESS_DENIED,
          "fake guardian direct launch fails closed");
}

void HeartbeatAndOrderlyStopAreCorrelated() {
    auto guardian = StartedGuardian();
    const auto workerId = guardian.startupResponse().processId;
    HANDLE worker = OpenProcess(SYNCHRONIZE, FALSE, workerId);
    Check(worker != nullptr, "exact fake worker process is observable");
    ControlFrame response;
    std::wstring error;
    Check(guardian.Send(
              ControlMessageKind::Heartbeat,
              ControllerIsolationCommandTimeoutMilliseconds,
              response, error) &&
              response.kind == ControlMessageKind::Heartbeat &&
              response.processId == workerId,
          "heartbeat is nonce, generation, sequence and worker correlated");
    Check(guardian.Send(
              ControlMessageKind::Stop,
              ControllerIsolationCommandTimeoutMilliseconds,
              response, error) &&
              response.kind == ControlMessageKind::Terminal,
          "orderly stop reaches a correlated terminal response");
    guardian.Stop();
    Check(WaitForSingleObject(worker, 1'000) == WAIT_OBJECT_0,
          "orderly guardian shutdown retires its exact fake worker");
    CloseHandle(worker);
}

void WorkerDeathAndHangAreBounded() {
    const std::array cases{
        std::pair{ControlMessageKind::TestExit,
                  static_cast<std::uint32_t>(ERROR_PROCESS_ABORTED)},
        std::pair{ControlMessageKind::TestHang,
                  static_cast<std::uint32_t>(ERROR_TIMEOUT)}};
    for (const auto [kind, terminalStatus] : cases) {
        auto guardian = StartedGuardian();
        HANDLE worker = OpenProcess(
            SYNCHRONIZE, FALSE, guardian.startupResponse().processId);
        Check(worker != nullptr, "failure case observes exact fake worker");
        ControlFrame response;
        std::wstring error;
        Check(!guardian.Send(
                  kind, ControllerIsolationStartupTimeoutMilliseconds,
                  response, error) &&
                  response.kind == ControlMessageKind::Terminal &&
                  response.status == terminalStatus &&
                  error.find(std::to_wstring(terminalStatus)) !=
                      std::wstring::npos,
              "worker death or hang remains one bounded remote failure");
        Check(WaitForSingleObject(worker, 1'000) == WAIT_OBJECT_0,
              "worker death or hang leaves no fake child alive");
        CloseHandle(worker);
        guardian.Stop();
    }
}

void InvalidResponsesNeverBecomeSuccess() {
    const std::array cases{
        ControlMessageKind::TestWrongResponseKind,
        ControlMessageKind::TestUnknownResponseKind,
        ControlMessageKind::TestNonzeroResponseStatus,
        ControlMessageKind::TestMalformedResponse};
    for (const auto kind : cases) {
        auto guardian = StartedGuardian();
        ControlFrame response;
        std::wstring error;
        Check(!guardian.Send(
                  kind, ControllerIsolationStartupTimeoutMilliseconds,
                  response, error) &&
                  !error.empty(),
              "wrong-kind, unknown, failed and malformed responses fail closed");
        if (kind == ControlMessageKind::TestNonzeroResponseStatus) {
            Check(response.status == ERROR_ACCESS_DENIED &&
                      error.find(std::to_wstring(ERROR_ACCESS_DENIED)) !=
                          std::wstring::npos,
                  "remote status is preserved without relabeling success");
        }
        Check(guardian.WaitForExitForTest(1'000),
              "invalid-response fake guardian terminates its session");
        guardian.Stop();
    }
}

void AdmissionSnapshotAndTerminalPriorityAreStable() {
    {
        auto guardian = StartedGuardian();
        guardian.CorruptSharedAdmissionForTest();
        ControlFrame response;
        std::wstring error;
        Check(guardian.Send(
                  ControlMessageKind::Heartbeat,
                  ControllerIsolationCommandTimeoutMilliseconds,
                  response, error) &&
                  response.kind == ControlMessageKind::Heartbeat,
              "child uses its validated admission snapshot after open");
        guardian.Stop();
    }
    {
        auto guardian = StartedGuardian();
        HANDLE worker = OpenProcess(
            SYNCHRONIZE, FALSE, guardian.startupResponse().processId);
        Check(worker != nullptr, "terminal-priority case observes exact worker");
        const auto request =
            guardian.NextFrameForTest(ControlMessageKind::Heartbeat);
        Check(guardian.SignalStopAndRequestForTest(request),
              "test signals stop and a valid request at the same boundary");
        Check(guardian.WaitForExitForTest(1'000) &&
                  WaitForSingleObject(worker, 1'000) == WAIT_OBJECT_0,
              "terminal stop wins and closes the worker job");
        CloseHandle(worker);
        guardian.Stop();
    }
}

void GuardianDeathClosesWorkerJob() {
    auto guardian = StartedGuardian();
    HANDLE worker = OpenProcess(
        SYNCHRONIZE, FALSE, guardian.startupResponse().processId);
    Check(worker != nullptr, "guardian-death case observes exact fake worker");
    Check(guardian.TerminateExactForTest(),
          "test terminates only the exact owned fake guardian handle");
    Check(guardian.WaitForExitForTest(1'000),
          "fake guardian termination is bounded");
    Check(WaitForSingleObject(worker, 1'000) == WAIT_OBJECT_0,
          "guardian death closes its kill-on-close worker job");
    CloseHandle(worker);
    guardian.Stop();
}

void LeaseAndTamperedFramesFailClosed() {
    {
        auto guardian = StartedGuardian();
        Check(guardian.WaitForExitForTest(2'000),
              "missing host lease retires the fake guardian and worker");
        guardian.Stop();
    }
    for (const int tamper : {0, 1, 2}) {
        auto guardian = StartedGuardian();
        auto request = guardian.NextFrameForTest(ControlMessageKind::Heartbeat);
        if (tamper == 0) request.nonce[0] ^= 0x80;
        if (tamper == 1) request.authority = Authority(9);
        if (tamper == 2) request.size = 0;
        ControlFrame response;
        std::wstring error;
        Check(!guardian.SendRawForTest(
                  request, ControllerIsolationStartupTimeoutMilliseconds,
                  response, error),
              "tampered subprocess frame receives no success response");
        Check(guardian.WaitForExitForTest(1'000),
              "tampered subprocess frame terminates its exact session");
        guardian.Stop();
    }
}

} // namespace

int main() {
    ProtocolRejectsWrongAuthorityAndShape();
    DirectLaunchFailsBeforeAnyBackendAdmission();
    HeartbeatAndOrderlyStopAreCorrelated();
    WorkerDeathAndHangAreBounded();
    InvalidResponsesNeverBecomeSuccess();
    AdmissionSnapshotAndTerminalPriorityAreStable();
    GuardianDeathClosesWorkerJob();
    LeaseAndTamperedFramesFailClosed();
    std::cout << "ControllerIsolationProcessTests passed (" << checks
              << " checks)\n";
    return 0;
}
