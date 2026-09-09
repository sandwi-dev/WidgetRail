#include "ControllerIsolationGuardianSession.h"

#include "ControllerIsolationJournal.h"
#include "ControllerIsolationGuardianLifetime.h"
#include "ControllerIsolationInputTransport.h"
#include "ControllerIsolationProcessOwner.h"
#include "ControllerIsolationReconnect.h"
#include "ControllerIsolationReader.h"
#include "HidHideConfigurationAdapter.h"

#include <windows.h>

#include <array>
#include <atomic>
#include <deque>
#include <filesystem>
#include <optional>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include <utility>

namespace widgetrail::isolation {
namespace {

constexpr DWORD kAcceptSliceMilliseconds = 15;
constexpr DWORD kRequestSliceMilliseconds = 5;
constexpr std::uint64_t kHeartbeatCadenceMilliseconds = 15;
constexpr std::uint64_t kHostStallMilliseconds = 500;

[[nodiscard]] std::filesystem::path CurrentExecutable() {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    return length == 0 || length >= path.size()
        ? std::filesystem::path{}
        : std::filesystem::path(std::wstring_view(path.data(), length));
}

[[nodiscard]] bool SamePath(
    const std::filesystem::path& left,
    const std::filesystem::path& right) noexcept {
    std::error_code leftError;
    std::error_code rightError;
    const auto canonicalLeft = std::filesystem::weakly_canonical(left, leftError);
    const auto canonicalRight = std::filesystem::weakly_canonical(right, rightError);
    return !leftError && !rightError &&
        _wcsicmp(canonicalLeft.c_str(), canonicalRight.c_str()) == 0;
}

[[nodiscard]] bool ValidateFile(
    const std::filesystem::path& actual,
    const std::filesystem::path& expected,
    const std::array<std::uint8_t, 32>& expectedHash) noexcept {
    if (!SamePath(actual, expected)) return false;
    std::array<std::uint8_t, 32> hash{};
    std::uint32_t error{};
    return ControllerIsolationFileSha256(actual, hash, error) &&
        hash == expectedHash;
}

[[nodiscard]] bool ClientIdentity(
    const ControllerIsolationPipeServer& server,
    const ControlFrame& hello,
    const ControllerIsolationJournalRecord& record,
    HANDLE& admittedProcess) noexcept {
    admittedProcess = nullptr;
    const auto pipeProcessId = server.clientProcessId();
    if (!pipeProcessId || hello.processId == 0 ||
        *pipeProcessId != hello.processId) return false;
    HANDLE process = OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE,
        FALSE, *pipeProcessId);
    if (!process) return false;
    std::array<wchar_t, 32'768> path{};
    DWORD length = static_cast<DWORD>(path.size());
    const bool valid = ProcessCreationTime(process) ==
            hello.observedAtMilliseconds &&
        QueryFullProcessImageNameW(process, 0, path.data(), &length) &&
        ValidateFile(
            std::filesystem::path(std::wstring_view(path.data(), length)),
            record.hostPath, record.hostSha256);
    if (!valid) CloseHandle(process);
    else admittedProcess = process;
    return valid;
}

class RetainedHostProof final {
public:
    ~RetainedHostProof() { Reset(); }
    [[nodiscard]] bool present() const noexcept { return process_ != nullptr; }
    [[nodiscard]] bool Same(const ControlFrame& hello) const noexcept {
        return present() && hello.processId == processId_ &&
            hello.observedAtMilliseconds == creationTime_;
    }
    [[nodiscard]] AdmittedHostLifetime Observe() const noexcept {
        return ObserveAdmittedHostLifetime(process_);
    }
    void Retain(HANDLE& process, const ControlFrame& hello) noexcept {
        Reset();
        process_ = std::exchange(process, nullptr);
        processId_ = hello.processId;
        creationTime_ = hello.observedAtMilliseconds;
    }
    void Reset() noexcept {
        if (process_) CloseHandle(process_);
        process_ = nullptr;
        processId_ = 0;
        creationTime_ = 0;
    }

private:
    HANDLE process_{};
    std::uint32_t processId_{};
    std::uint64_t creationTime_{};
};

[[nodiscard]] ControlProgress ResponseProgress(
    const ControlFrame& response) noexcept {
    const auto progress = static_cast<ControlProgress>(
        response.observedAtMilliseconds);
    switch (progress) {
    case ControlProgress::None:
    case ControlProgress::Queued:
    case ControlProgress::PreparedNeutral:
    case ControlProgress::AwaitingNeutral:
    case ControlProgress::Playing:
    case ControlProgress::Contained:
    case ControlProgress::Terminal:
        return progress;
    }
    return ControlProgress::Terminal;
}

struct GuardianRuntime final {
    std::recursive_mutex effects;
    ControllerIsolationJournalStore store;
    ControllerIsolationJournalRecord record;
    ControllerIsolationJournalRecord persistedRecord;
    ControllerIsolationProcessOwner worker;
    ControlProgress progress{ControlProgress::Terminal};
    std::uint32_t lastError{};
    bool policyMayBeMutated{};
    GamepadState latestState{};
    std::deque<std::uint64_t> guideEvents;
    ControllerIsolationInputBatchQueue inputBatches;
    bool confirmedDisconnectedInteraction{};

    explicit GuardianRuntime(
        const std::filesystem::path& path,
        ControllerIsolationJournalRecord value)
        : store(path), record(std::move(value)), persistedRecord(record) {}

    [[nodiscard]] bool SavePhase(
        const ControllerIsolationJournalPhase phase) noexcept {
        const std::scoped_lock lock(effects);
        auto replacement = record;
        replacement.phase = phase;
        std::uint32_t error{};
        if (store.SaveAtomicIfCurrent(
                persistedRecord, replacement, error)) {
            record = replacement;
            persistedRecord = replacement;
            return true;
        }
        lastError = error;
        record = persistedRecord;
        return false;
    }

    [[nodiscard]] bool SendWorker(
        const ControlFrame& request, ControlFrame& response) noexcept {
        const std::scoped_lock lock(effects);
        std::wstring error;
        if (!worker.Send(
                request, ControllerIsolationCommandTimeoutMilliseconds,
                response, error)) {
            lastError = response.status != 0
                ? response.status : ERROR_PROCESS_ABORTED;
            progress = ControlProgress::Terminal;
            (void)SavePhase(
                ControllerIsolationJournalPhase::RecoveryRequired);
            return false;
        }
        progress = ResponseProgress(response);
        latestState = response.state;
        if (response.deviceEnrollmentToken != 0) {
            if (guideEvents.size() == 32) {
                lastError = ERROR_BUFFER_OVERFLOW;
                progress = ControlProgress::Terminal;
                (void)SavePhase(
                    ControllerIsolationJournalPhase::RecoveryRequired);
                return false;
            }
            guideEvents.push_back(response.deviceEnrollmentToken);
        }
        if (!inputBatches.Push(response.inputBatch)) {
                lastError = ERROR_BUFFER_OVERFLOW;
                progress = ControlProgress::Terminal;
                (void)SavePhase(
                    ControllerIsolationJournalPhase::RecoveryRequired);
                return false;
        }
        return true;
    }

    [[nodiscard]] std::uint64_t TakeGuide() noexcept {
        if (guideEvents.empty()) return 0;
        const auto event = guideEvents.front();
        guideEvents.pop_front();
        return event;
    }

    void DecorateHostResponse(
        const ControlMessageKind requestKind,
        ControlFrame& response) noexcept {
        const std::scoped_lock lock(effects);
        response.state = latestState;
        response.inputBatch.interactionGeneration =
            inputBatches.interactionGeneration();
        if (requestKind == ControlMessageKind::Heartbeat) {
            response.deviceEnrollmentToken = TakeGuide();
            response.inputBatch = inputBatches.TakeBatch();
        }
    }

    [[nodiscard]] bool Start() noexcept {
        const std::scoped_lock lock(effects);
        const auto self = CurrentExecutable();
        if (!ValidateFile(
                self, record.guardianPath, record.guardianSha256) ||
            !ValidateFile(
                record.workerPath, record.workerPath, record.workerSha256)) {
            lastError = ERROR_INVALID_IMAGE_HASH;
            return false;
        }
        SelectedControllerDescriptor descriptor;
        const auto discovery = DiscoverCurrentPhysicalController(
            record.authority.leaseId, descriptor);
        if (discovery != SelectedControllerDiscoveryStatus::Ready) {
            switch (discovery) {
            case SelectedControllerDiscoveryStatus::Unavailable:
                lastError = ERROR_DEVICE_NOT_CONNECTED;
                break;
            case SelectedControllerDiscoveryStatus::Ambiguous:
                lastError = ERROR_MORE_DATA;
                break;
            case SelectedControllerDiscoveryStatus::UnknownIdentity:
                lastError = ERROR_INVALID_DATA;
                break;
            case SelectedControllerDiscoveryStatus::Ready:
                break;
            }
            return false;
        }
        HidHideConfigurationAdapter adapter;
        HidHideSnapshot before;
        if (adapter.ReadSnapshot(before, lastError) !=
            HidHideConfigurationStatus::Ready) return false;
        std::wstring workerDevicePath;
        if (!ControllerIsolationDosDevicePath(
                record.workerPath, workerDevicePath, lastError)) return false;
        const std::set<std::wstring> devices{
            std::wstring(descriptor.deviceInstanceId.view())};
        const auto plan = PlanHidHideApply(
            before, workerDevicePath, devices);
        if (plan.status != HidHideApplyStatus::Ready || !plan.plan) {
            lastError = ERROR_ACCESS_DENIED;
            return false;
        }
        record.policy = plan.plan->journal;
        if (!SavePhase(ControllerIsolationJournalPhase::PolicyPlanned))
            return false;
        HidHideSnapshot observed;
        const auto applied = adapter.ApplySnapshot(
            before, plan.plan->desired, observed, lastError);
        if (applied != HidHideConfigurationStatus::Ready) {
            policyMayBeMutated = applied ==
                    HidHideConfigurationStatus::PartialMutation ||
                observed != before;
            return false;
        }
        policyMayBeMutated = true;
        std::wstring startError;
        if (!worker.Start(
                record.workerPath, record.authority, 1,
                ControllerIsolationStartupTimeoutMilliseconds, startError,
                [](void* context, const std::uint32_t processId,
                   const std::uint64_t creationTime,
                   std::wstring& callbackError) noexcept {
                    return static_cast<GuardianRuntime*>(context)->
                        RecordWorkerIdentity(
                            processId, creationTime, callbackError);
                },
                this)) {
            lastError = ERROR_PROCESS_ABORTED;
            return false;
        }
        auto prepare = MakeControlFrame(
            ControlMessageKind::PrepareSession, {}, record.authority, 1);
        prepare.enrollment = descriptor.enrollment;
        ControlFrame response;
        if (!SendWorker(prepare, response)) return false;
        auto commit = MakeControlFrame(
            ControlMessageKind::CommitPlaying, {}, record.authority, 1);
        commit.observedAtMilliseconds = 10;
        if (!SendWorker(commit, response)) return false;
        const auto startedAt = GetTickCount64();
        while (progress != ControlProgress::Playing) {
            if (GetTickCount64() - startedAt >
                ControllerIsolationStartupTimeoutMilliseconds) {
                lastError = ERROR_TIMEOUT;
                return false;
            }
            auto heartbeat = MakeControlFrame(
                ControlMessageKind::Heartbeat, {}, record.authority, 1);
            if (!SendWorker(heartbeat, response)) return false;
            Sleep(1);
        }
        return SavePhase(ControllerIsolationJournalPhase::Playing);
    }

    [[nodiscard]] bool Restore(const bool recovery) noexcept {
        const std::scoped_lock lock(effects);
        ControllerIsolationLifecycleLease lifecycle;
        if (!lifecycle.Acquire(
                ControllerIsolationStartupTimeoutMilliseconds, lastError))
            return false;
        if (!store.Matches(persistedRecord, lastError)) return false;
        worker.Stop();
        if (record.workerProcessId != 0 || record.workerCreationTime != 0) {
            std::uint32_t workerError{};
            if (ObserveExactIsolationWorker(record, workerError) !=
                GuardianLifetimeStatus::Exited) {
                lastError = workerError != 0
                    ? workerError : ERROR_PROCESS_ABORTED;
                return false;
            }
        }
        if (!policyMayBeMutated &&
            record.phase == ControllerIsolationJournalPhase::Prepared) {
            if (!store.RemoveIfCurrent(persistedRecord, lastError)) return false;
            progress = ControlProgress::Terminal;
            return true;
        }
        HidHideConfigurationAdapter adapter;
        HidHideSnapshot current;
        if (adapter.ReadSnapshot(current, lastError) !=
            HidHideConfigurationStatus::Ready) return false;
        const auto plan = recovery
            ? PlanHidHideRecovery(record.policy, current)
            : PlanHidHideRestore(record.policy, current);
        if (plan.status == HidHideRestoreStatus::Conflict || !plan.desired) {
            lastError = ERROR_REVISION_MISMATCH;
            return false;
        }
        HidHideSnapshot observed;
        if (adapter.ApplySnapshot(
                current, *plan.desired, observed, lastError) !=
            HidHideConfigurationStatus::Ready) return false;
        policyMayBeMutated = false;
        if (!store.RemoveIfCurrent(persistedRecord, lastError)) return false;
        progress = ControlProgress::Terminal;
        return true;
    }

    [[nodiscard]] bool RecordGuardianIdentity() noexcept {
        const std::scoped_lock lock(effects);
        const auto creationTime = ProcessCreationTime(GetCurrentProcess());
        if (record.phase == ControllerIsolationJournalPhase::Prepared &&
            record.guardianProcessId == GetCurrentProcessId() &&
            record.guardianCreationTime != 0 &&
            record.guardianCreationTime == creationTime) return true;
        lastError = ERROR_REVISION_MISMATCH;
        return false;
    }

    [[nodiscard]] bool RecordWorkerIdentity(
        const std::uint32_t processId,
        const std::uint64_t creationTime,
        std::wstring& errorText) noexcept {
        const std::scoped_lock lock(effects);
        if (processId == 0 || creationTime == 0 ||
            record.workerProcessId != 0 || record.workerCreationTime != 0) {
            errorText = L"Controller isolation Worker identity is invalid.";
            return false;
        }
        auto replacement = record;
        replacement.workerProcessId = processId;
        replacement.workerCreationTime = creationTime;
        std::uint32_t error{};
        if (!store.SaveAtomicIfCurrent(
                persistedRecord, replacement, error)) {
            lastError = error;
            errorText = L"Controller isolation Worker identity publication failed error=" +
                std::to_wstring(error);
            return false;
        }
        record = replacement;
        persistedRecord = replacement;
        return true;
    }

    [[nodiscard]] ControlProgress Progress() noexcept {
        const std::scoped_lock lock(effects);
        return progress;
    }

    [[nodiscard]] std::uint32_t LastError() noexcept {
        const std::scoped_lock lock(effects);
        return lastError;
    }

    void HoldUncertainInteraction() noexcept {
        const std::scoped_lock lock(effects);
        if (progress == ControlProgress::Playing ||
            progress == ControlProgress::Terminal ||
            progress == ControlProgress::Contained) return;
        auto hold = MakeControlFrame(
            ControlMessageKind::HoldContained, {}, record.authority, 1);
        ControlFrame ignored;
        (void)SendWorker(hold, ignored);
    }
};

[[nodiscard]] ControlFrame GuardianReply(
    const ControlFrame& request,
    const ControllerIsolationJournalRecord& record,
    const ControlProgress progress,
    const std::uint32_t status = 0) noexcept {
    auto response = MakeControlFrame(
        request.kind == ControlMessageKind::Hello
            ? ControlMessageKind::HelloAccepted
            : (request.kind == ControlMessageKind::Stop
                  ? ControlMessageKind::Terminal : request.kind),
        record.nonce, record.authority, request.sequence);
    response.status = status;
    response.processId = GetCurrentProcessId();
    response.observedAtMilliseconds = static_cast<std::uint64_t>(progress);
    return response;
}

void RunControlIngress(
    GuardianRuntime& runtime,
    ControllerIsolationPipeServer& server,
    std::atomic_bool& stopping,
    std::atomic_int& terminalStatus) noexcept {
    while (!stopping.load(std::memory_order_acquire)) {
        std::uint32_t error{};
        if (!server.Accept(kAcceptSliceMilliseconds, error)) {
            if (error == ERROR_TIMEOUT) continue;
            // The one-shot management ingress is not the interaction owner.
            // Losing it cannot retire the healthy primary host/worker route.
            return;
        }
        ControlFrame hello;
        HANDLE clientProcess{};
        if (!server.Receive(
                hello, ControllerIsolationStartupTimeoutMilliseconds, error) ||
            hello.kind != ControlMessageKind::Hello ||
            !ClientIdentity(server, hello, runtime.record, clientProcess)) {
            if (clientProcess) CloseHandle(clientProcess);
            server.Disconnect();
            continue;
        }
        CloseHandle(clientProcess);
        ControlSessionGate gate(
            runtime.record.nonce, runtime.record.authority, hello.sequence);
        if (gate.Admit(hello) != ControlFrameValidation::Accepted) {
            server.Disconnect();
            continue;
        }
        {
            const std::scoped_lock lock(runtime.effects);
            auto response = GuardianReply(
                hello, runtime.record, runtime.progress,
                runtime.progress == ControlProgress::Terminal
                    ? runtime.lastError : 0);
            if (!server.Reply(response, error)) {
                server.Disconnect();
                continue;
            }
        }
        ControlFrame request;
        if (!server.Receive(
                request, ControllerIsolationCommandTimeoutMilliseconds, error) ||
            gate.Admit(request) != ControlFrameValidation::Accepted) {
            server.Disconnect();
            continue;
        }
        ControlFrame response;
        bool terminal{};
        int exitStatus{};
        {
            const std::scoped_lock lock(runtime.effects);
            if (request.kind == ControlMessageKind::QueryStatus) {
                response = GuardianReply(
                    request, runtime.record, runtime.progress,
                    runtime.progress == ControlProgress::Terminal
                        ? runtime.lastError : 0);
            } else if (request.kind == ControlMessageKind::Stop ||
                       request.kind == ControlMessageKind::Recover) {
                const bool restored = runtime.Restore(
                    request.kind == ControlMessageKind::Recover);
                response = GuardianReply(
                    request, runtime.record, ControlProgress::Terminal,
                    restored ? 0 : runtime.lastError);
                terminal = true;
                exitStatus = restored ? 0 : static_cast<int>(runtime.lastError);
            } else {
                response = GuardianReply(
                    request, runtime.record, runtime.progress,
                    ERROR_INVALID_FUNCTION);
            }
        }
        (void)server.Reply(response, error);
        server.Disconnect();
        if (terminal) {
            terminalStatus.store(exitStatus, std::memory_order_release);
            stopping.store(true, std::memory_order_release);
            return;
        }
    }
}

} // namespace

int RunControllerIsolationGuardianSession(
    const std::filesystem::path& journalPath,
    const RoutingAuthority& expectedAuthority) noexcept {
    ControllerIsolationJournalStore store(journalPath);
    std::uint32_t error{};
    auto loaded = store.Load(error);
    if (!loaded) return static_cast<int>(
        error != 0 ? error : ERROR_INVALID_DATA);
    if (!GuardianStartupAuthorityMatches(
            expectedAuthority, loaded->authority))
        return ERROR_REVISION_MISMATCH;
    GuardianRuntime runtime(journalPath, std::move(*loaded));
    if (!runtime.RecordGuardianIdentity())
        return static_cast<int>(runtime.LastError());
    ControllerIsolationPipeServer server;
    ControllerIsolationPipeServer controlServer;
    const auto pipeName = ControllerIsolationPipeName(runtime.record.authority);
    if (!server.Open(pipeName, error)) return static_cast<int>(error);
    const auto controlPipeName =
        ControllerIsolationControlPipeName(runtime.record.authority);
    if (!controlServer.Open(controlPipeName, error))
        return static_cast<int>(error);
    const auto loadedPhase = runtime.record.phase;
    const bool startSucceeded = loadedPhase ==
            ControllerIsolationJournalPhase::Prepared &&
        runtime.Start();
    if (DecideGuardianStartupDisposition(
            loadedPhase, startSucceeded, runtime.record.phase) !=
        GuardianStartupDisposition::Serve) {
        if (runtime.lastError == ERROR_SUCCESS)
            runtime.lastError = loadedPhase ==
                    ControllerIsolationJournalPhase::Prepared
                ? ERROR_PROCESS_ABORTED : ERROR_ALREADY_EXISTS;
        runtime.progress = ControlProgress::Terminal;
        (void)runtime.SavePhase(
            ControllerIsolationJournalPhase::RecoveryRequired);
    }

    std::atomic_bool stopping{};
    std::atomic_int terminalStatus{};
    RetainedHostProof retainedHost;
    std::jthread controlThread([&] {
        RunControlIngress(runtime, controlServer, stopping, terminalStatus);
    });
    const auto runPrimary = [&]() noexcept -> int {
    for (;;) {
        if (stopping.load(std::memory_order_acquire))
            return terminalStatus.load(std::memory_order_acquire);
        if (retainedHost.present() &&
            retainedHost.Observe() == AdmittedHostLifetime::Exited) {
            retainedHost.Reset();
            runtime.confirmedDisconnectedInteraction = true;
        }
        if (!server.Accept(kAcceptSliceMilliseconds, error)) {
            if (error == ERROR_TIMEOUT) {
                if (runtime.Progress() != ControlProgress::Terminal) {
                    ControlFrame heartbeat = MakeControlFrame(
                        ControlMessageKind::Heartbeat, {},
                        runtime.record.authority, 1);
                    ControlFrame ignored;
                    (void)runtime.SendWorker(heartbeat, ignored);
                }
                if (runtime.confirmedDisconnectedInteraction &&
                    runtime.Progress() == ControlProgress::Contained) {
                    ControlFrame close = MakeControlFrame(
                        ControlMessageKind::CloseOverlay, {},
                        runtime.record.authority, 1);
                    close.observedAtMilliseconds = 10;
                    ControlFrame ignored;
                    (void)runtime.SendWorker(close, ignored);
                }
                if (runtime.Progress() == ControlProgress::Playing)
                    runtime.confirmedDisconnectedInteraction = false;
                continue;
            }
            return static_cast<int>(error);
        }
        ControlFrame hello;
        HANDLE admittedHost{};
        if (!server.Receive(
                hello, ControllerIsolationStartupTimeoutMilliseconds, error) ||
            hello.kind != ControlMessageKind::Hello ||
            !ClientIdentity(server, hello, runtime.record, admittedHost)) {
            if (admittedHost) CloseHandle(admittedHost);
            server.Disconnect();
            continue;
        }
        if (retainedHost.present()) {
            const auto retainedLifetime = retainedHost.Observe();
            if (!AdmitHostReconnect(
                    retainedLifetime, retainedHost.Same(hello))) {
                CloseHandle(admittedHost);
                server.Disconnect();
                continue;
            }
            retainedHost.Reset();
        }
        ControlSessionGate gate(
            runtime.record.nonce, runtime.record.authority, hello.sequence);
        if (gate.Admit(hello) != ControlFrameValidation::Accepted) {
            CloseHandle(admittedHost);
            server.Disconnect();
            continue;
        }
        const auto helloProgress = runtime.Progress();
        auto helloReply = GuardianReply(
            hello, runtime.record, helloProgress,
            helloProgress == ControlProgress::Terminal
                ? runtime.LastError() : 0);
        if (!server.Reply(helloReply, error)) {
            CloseHandle(admittedHost);
            server.Disconnect();
            continue;
        }
        runtime.confirmedDisconnectedInteraction = false;
        auto lastHostRequest = GetTickCount64();
        auto lastWorkerHeartbeat = lastHostRequest;
        for (;;) {
            if (stopping.load(std::memory_order_acquire)) break;
            const auto now = GetTickCount64();
            if (runtime.Progress() != ControlProgress::Terminal &&
                now - lastWorkerHeartbeat >= kHeartbeatCadenceMilliseconds) {
                ControlFrame heartbeat = MakeControlFrame(
                    ControlMessageKind::Heartbeat, {},
                    runtime.record.authority, 1);
                ControlFrame workerResponse;
                (void)runtime.SendWorker(heartbeat, workerResponse);
                lastWorkerHeartbeat = now;
            }
            if (runtime.Progress() == ControlProgress::AwaitingNeutral &&
                now - lastHostRequest > kHostStallMilliseconds) {
                ControlFrame hold = MakeControlFrame(
                    ControlMessageKind::HoldContained, {},
                    runtime.record.authority, 1);
                ControlFrame workerResponse;
                (void)runtime.SendWorker(hold, workerResponse);
            }
            ControlFrame request;
            if (!server.Receive(request, kRequestSliceMilliseconds, error)) {
                if (error == ERROR_TIMEOUT) continue;
                break;
            }
            lastHostRequest = now;
            if (gate.Admit(request) != ControlFrameValidation::Accepted) break;
            ControlFrame response;
            if (request.kind == ControlMessageKind::QueryStatus) {
                const auto currentProgress = runtime.Progress();
                response = GuardianReply(
                    request, runtime.record, currentProgress,
                    currentProgress == ControlProgress::Terminal
                        ? runtime.LastError() : 0);
                runtime.DecorateHostResponse(request.kind, response);
            } else if (request.kind == ControlMessageKind::Recover ||
                       request.kind == ControlMessageKind::Stop) {
                const bool restored = runtime.Restore(
                    request.kind == ControlMessageKind::Recover);
                response = GuardianReply(
                    request, runtime.record, ControlProgress::Terminal,
                    restored ? 0 : runtime.LastError());
                (void)server.Reply(response, error);
                return restored ? 0 : static_cast<int>(runtime.LastError());
            } else if (runtime.Progress() == ControlProgress::Terminal) {
                response = GuardianReply(
                    request, runtime.record, runtime.Progress(),
                    runtime.LastError() != 0
                        ? runtime.LastError() : ERROR_INVALID_STATE);
            } else {
                ControlFrame workerResponse;
                if (!runtime.SendWorker(request, workerResponse)) {
                    response = GuardianReply(
                        request, runtime.record, runtime.Progress(),
                        runtime.LastError());
                } else {
                    response = GuardianReply(
                        request, runtime.record, runtime.Progress());
                    runtime.DecorateHostResponse(request.kind, response);
                }
            }
            if (!server.Reply(response, error)) {
                break;
            }
        }
        server.Disconnect();
        const auto hostLifetime = ObserveAdmittedHostLifetime(admittedHost);
        if (hostLifetime == AdmittedHostLifetime::Exited &&
            runtime.Progress() != ControlProgress::Playing &&
            runtime.Progress() != ControlProgress::Terminal) {
            CloseHandle(admittedHost);
            runtime.confirmedDisconnectedInteraction = true;
        } else if (hostLifetime != AdmittedHostLifetime::Exited) {
            retainedHost.Retain(admittedHost, hello);
            runtime.HoldUncertainInteraction();
        } else {
            CloseHandle(admittedHost);
        }
    }
    };
    const auto result = runPrimary();
    stopping.store(true, std::memory_order_release);
    controlThread.request_stop();
    return result;
}

} // namespace widgetrail::isolation
