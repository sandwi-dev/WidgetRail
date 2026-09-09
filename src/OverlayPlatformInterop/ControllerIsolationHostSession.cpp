#include "ControllerIsolationHostSession.h"

#include "ControllerIsolationGuardianLifetime.h"
#include "ControllerIsolationProcessOwner.h"
#include "HidHideConfigurationAdapter.h"

#include <bcrypt.h>
#include <shlobj.h>

#include <array>
#include <algorithm>
#include <filesystem>
#include <limits>
#include <thread>

namespace widgetrail::isolation {
namespace {

[[nodiscard]] std::filesystem::path CurrentExecutable() {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    return length == 0 || length >= path.size()
        ? std::filesystem::path{}
        : std::filesystem::path(std::wstring_view(path.data(), length));
}

[[nodiscard]] bool RandomBytes(void* value, const ULONG bytes) noexcept {
    return BCryptGenRandom(
        nullptr, static_cast<PUCHAR>(value), bytes,
        BCRYPT_USE_SYSTEM_PREFERRED_RNG) == 0;
}

[[nodiscard]] bool NewRecord(
    const std::filesystem::path& journalPath,
    ControllerIsolationJournalRecord& record,
    std::wstring& diagnostic) noexcept {
    const auto host = CurrentExecutable();
    const auto directory = host.parent_path();
    record = {};
    record.hostPath = host.wstring();
    record.guardianPath =
        (directory / L"ControllerIsolationGuardian.exe").wstring();
    record.workerPath =
        (directory / L"ControllerIsolationWorker.exe").wstring();
    record.hostProcessId = GetCurrentProcessId();
    record.hostCreationTime = ProcessCreationTime(GetCurrentProcess());
    record.phase = ControllerIsolationJournalPhase::Prepared;
    std::array<std::uint64_t, 4> authority{};
    if (!RandomBytes(authority.data(), static_cast<ULONG>(sizeof(authority))) ||
        !RandomBytes(record.nonce.data(),
                     static_cast<ULONG>(record.nonce.size()))) {
        diagnostic = L"Controller isolation authority generation failed.";
        return false;
    }
    for (auto& value : authority) if (value == 0) value = 1;
    record.authority = {
        authority[0], authority[1], authority[2], authority[3]};
    std::uint32_t error{};
    if (!ControllerIsolationFileSha256(
            record.hostPath, record.hostSha256, error) ||
        !ControllerIsolationFileSha256(
            record.guardianPath, record.guardianSha256, error) ||
        !ControllerIsolationFileSha256(
            record.workerPath, record.workerSha256, error)) {
        diagnostic = L"Controller isolation artifact identity failed error=" +
            std::to_wstring(error);
        return false;
    }
    ControllerIsolationJournalStore store(journalPath);
    if (!store.SaveAtomic(record, error)) {
        diagnostic = L"Controller isolation journal creation failed error=" +
            std::to_wstring(error);
        return false;
    }
    return true;
}

[[nodiscard]] bool AuthenticatedShape(
    const ControlFrame& response,
    const ControlMessageKind expectedKind,
    const ControllerIsolationJournalRecord& record,
    const std::uint64_t sequence) noexcept {
    return response.magic == ControllerIsolationProtocolMagic &&
        response.version == ControllerIsolationProtocolVersion &&
        response.size == sizeof(ControlFrame) && response.reserved == 0 &&
        response.kind == expectedKind &&
        SameNonce(response.nonce, record.nonce) &&
        response.authority == record.authority &&
        response.sequence == sequence;
}

[[nodiscard]] bool ExactGuardianProcess(
    const ControllerIsolationPipeClient& client,
    const ControllerIsolationJournalRecord& record) noexcept {
    const auto processId = client.serverProcessId();
    if (!processId) return false;
    HANDLE process = OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION, FALSE, *processId);
    if (!process) return false;
    std::array<wchar_t, 32'768> path{};
    DWORD length = static_cast<DWORD>(path.size());
    const bool queried = QueryFullProcessImageNameW(
        process, 0, path.data(), &length) != FALSE;
    const bool exactPersistedIdentity =
        record.guardianProcessId != 0 &&
        *processId == record.guardianProcessId &&
        ProcessCreationTime(process) == record.guardianCreationTime;
    CloseHandle(process);
    if (!queried || !exactPersistedIdentity || _wcsicmp(
            std::wstring(path.data(), length).c_str(),
            record.guardianPath.c_str()) != 0) return false;
    std::array<std::uint8_t, 32> hash{};
    std::uint32_t error{};
    return ControllerIsolationFileSha256(path.data(), hash, error) &&
        hash == record.guardianSha256;
}

[[nodiscard]] ControllerIsolationCommandStatus ConvertStatus(
    const ControlProgress progress, const std::uint32_t remoteStatus) noexcept {
    if (remoteStatus != 0 || progress == ControlProgress::Terminal)
        return ControllerIsolationCommandStatus::RecoveryRequired;
    switch (progress) {
    case ControlProgress::PreparedNeutral:
    case ControlProgress::Queued:
        return ControllerIsolationCommandStatus::Prepared;
    case ControlProgress::AwaitingNeutral:
        return ControllerIsolationCommandStatus::AwaitingNeutral;
    case ControlProgress::Playing:
        return ControllerIsolationCommandStatus::Playing;
    case ControlProgress::Contained:
        return ControllerIsolationCommandStatus::Contained;
    case ControlProgress::None:
    case ControlProgress::Terminal:
        break;
    }
    return ControllerIsolationCommandStatus::Failed;
}

[[nodiscard]] bool RecoverWithoutGuardian(
    const std::filesystem::path& path,
    ControllerIsolationJournalRecord record,
    std::wstring& diagnostic) noexcept {
    std::uint32_t guardianError{};
    if (ObserveExactIsolationGuardian(record, guardianError) !=
        GuardianLifetimeStatus::Exited) {
        diagnostic = L"Controller isolation recovery requires proof that the exact Guardian exited error=" +
            std::to_wstring(guardianError);
        return false;
    }
    ControllerIsolationJournalStore store(path);
    std::uint32_t error{};
    if (record.phase == ControllerIsolationJournalPhase::Prepared) {
        if (store.Remove(error)) return true;
        diagnostic = L"Controller isolation empty journal removal failed error=" +
            std::to_wstring(error);
        return false;
    }
    HidHideConfigurationAdapter adapter;
    HidHideSnapshot current;
    if (adapter.ReadSnapshot(current, error) !=
        HidHideConfigurationStatus::Ready) {
        diagnostic = L"Controller isolation recovery read failed error=" +
            std::to_wstring(error);
        return false;
    }
    const auto plan = PlanHidHideRecovery(record.policy, current);
    if (plan.status == HidHideRestoreStatus::Conflict || !plan.desired) {
        diagnostic = L"Controller isolation recovery found foreign policy drift.";
        return false;
    }
    HidHideSnapshot observed;
    if (adapter.ApplySnapshot(
            current, *plan.desired, observed, error) !=
        HidHideConfigurationStatus::Ready || !store.Remove(error)) {
        diagnostic = L"Controller isolation recovery failed error=" +
            std::to_wstring(error);
        return false;
    }
    return true;
}

} // namespace

std::filesystem::path ControllerIsolationJournalPath() noexcept {
    PWSTR localAppData{};
    if (FAILED(SHGetKnownFolderPath(
            FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &localAppData)) ||
        !localAppData) return {};
    std::filesystem::path result(localAppData);
    CoTaskMemFree(localAppData);
    return result / L"WidgetRail" / L"controller-isolation" / L"session.bin";
}

ControllerIsolationHostSession::~ControllerIsolationHostSession() { Detach(); }

bool ControllerIsolationHostSession::Connect(
    const ControllerIsolationJournalRecord& record,
    std::wstring& diagnostic,
    const bool controlPipe) noexcept {
    record_ = record;
    std::uint32_t error{};
    if (!client_.Connect(
            controlPipe
                ? ControllerIsolationControlPipeName(record.authority)
                : ControllerIsolationPipeName(record.authority),
            ControllerIsolationStartupTimeoutMilliseconds, error)) {
        diagnostic = L"Controller isolation Guardian connection failed error=" +
            std::to_wstring(error);
        return false;
    }
    auto hello = MakeControlFrame(
        ControlMessageKind::Hello, record.nonce, record.authority,
        nextSequence_);
    hello.processId = GetCurrentProcessId();
    hello.observedAtMilliseconds = ProcessCreationTime(GetCurrentProcess());
    ControlFrame response;
    if (!client_.Exchange(
            hello, response, ControllerIsolationStartupTimeoutMilliseconds,
            error) ||
        !AuthenticatedShape(
            response, ControlMessageKind::HelloAccepted, record,
            nextSequence_)) {
        diagnostic = L"Controller isolation Guardian authentication failed error=" +
            std::to_wstring(error);
        client_.Close();
        return false;
    }
    if (!ExactGuardianProcess(client_, record)) {
        diagnostic = L"Controller isolation Guardian identity was rejected.";
        client_.Close();
        return false;
    }
    ++nextSequence_;
    progress_ = static_cast<ControlProgress>(response.observedAtMilliseconds);
    attached_ = true;
    if (response.status != 0) {
        diagnostic = L"Controller isolation recovery required error=" +
            std::to_wstring(response.status);
    }
    return true;
}

bool ControllerIsolationHostSession::AttachIfEnabled(
    std::wstring& diagnostic) noexcept {
    if (attached_) return true;
    const auto path = ControllerIsolationJournalPath();
    if (path.empty()) return false;
    ControllerIsolationJournalStore store(path);
    std::uint32_t error{};
    const auto record = store.Load(error);
    if (!record) {
        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
            diagnostic = L"Controller isolation journal is invalid error=" +
                std::to_wstring(error);
        return false;
    }
    return Connect(*record, diagnostic);
}

bool ControllerIsolationHostSession::Exchange(
    const ControlMessageKind kind, ControlFrame& response,
    std::wstring& diagnostic, const std::uint64_t value) noexcept {
    if (!attached_ || nextSequence_ == 0) return false;
    auto request = MakeControlFrame(
        kind, record_.nonce, record_.authority, nextSequence_);
    request.observedAtMilliseconds = value;
    std::uint32_t error{};
    const auto expected = kind == ControlMessageKind::Stop
        ? ControlMessageKind::Terminal : kind;
    const auto timeout = kind == ControlMessageKind::Stop ||
            kind == ControlMessageKind::Recover
        ? ControllerIsolationStartupTimeoutMilliseconds
        : ControllerIsolationCommandTimeoutMilliseconds;
    if (!client_.Exchange(
            request, response, timeout,
            error) ||
        !AuthenticatedShape(response, expected, record_, nextSequence_)) {
        diagnostic = L"Controller isolation command failed error=" +
            std::to_wstring(error);
        Detach();
        return false;
    }
    nextSequence_ = nextSequence_ == std::numeric_limits<std::uint64_t>::max()
        ? 0 : nextSequence_ + 1;
    progress_ = static_cast<ControlProgress>(response.observedAtMilliseconds);
    if (response.status != 0) {
        diagnostic = L"Controller isolation remote failure error=" +
            std::to_wstring(response.status);
        return false;
    }
    return true;
}

bool ControllerIsolationHostSession::PrepareOverlay(
    std::wstring& diagnostic) noexcept {
    if (!attached_) return true;
    ControlFrame response;
    if (progress_ == ControlProgress::Playing &&
        !Exchange(ControlMessageKind::EnterOverlay, response, diagnostic))
        return false;
    const auto startedAt = GetTickCount64();
    while (progress_ != ControlProgress::Contained) {
        if (progress_ == ControlProgress::Terminal ||
            GetTickCount64() - startedAt >
                ControllerIsolationCommandTimeoutMilliseconds) {
            diagnostic = L"Controller isolation containment timed out.";
            return false;
        }
        if (!Exchange(ControlMessageKind::Heartbeat, response, diagnostic))
            return false;
        Sleep(1);
    }
    return true;
}

void ControllerIsolationHostSession::CloseOverlay() noexcept {
    if (!attached_ || progress_ != ControlProgress::Contained) return;
    ControlFrame ignored;
    std::wstring diagnostic;
    (void)Exchange(ControlMessageKind::CloseOverlay, ignored, diagnostic, 10);
}

bool ControllerIsolationHostSession::Poll(
    ControllerIsolationHostReading& reading,
    std::wstring& diagnostic) noexcept {
    reading = {};
    if (!attached_) return false;
    if (pendingStates_.Pop(reading.state)) {
        reading.progress = progress_;
        reading.guideEvent = TakeGuide();
        reading.remainingInputStates = static_cast<std::uint32_t>(
            pendingStates_.size());
        return true;
    }
    if (!Pump(diagnostic)) return false;
    if (!pendingStates_.Pop(reading.state)) reading.state = latestState_;
    reading.progress = progress_;
    reading.guideEvent = TakeGuide();
    reading.remainingInputStates = static_cast<std::uint32_t>(
        pendingStates_.size());
    return true;
}

bool ControllerIsolationHostSession::PollGuide(
    std::uint64_t& guideEvent, std::wstring& diagnostic) noexcept {
    guideEvent = 0;
    if (!attached_ || !Pump(diagnostic)) return false;
    guideEvent = TakeGuide();
    return true;
}

bool ControllerIsolationHostSession::Pump(std::wstring& diagnostic) noexcept {
    ControlFrame response;
    if (!Exchange(ControlMessageKind::Heartbeat, response, diagnostic))
        return false;
    latestState_ = response.state;
    const auto& batch = response.inputBatch;
    const bool validBatch = batch.count <=
            ControllerIsolationInputBatchCapacity &&
        batch.reserved == 0 && std::ranges::all_of(
            batch.padding, [](std::uint8_t value) { return value == 0; }) &&
        (batch.count == 0
             ? batch.firstIngressOrdinal == 0 && batch.lastIngressOrdinal == 0
             : batch.firstIngressOrdinal != 0 &&
                 batch.lastIngressOrdinal >= batch.firstIngressOrdinal);
    if (!validBatch) {
        diagnostic = L"Controller isolation input batch was malformed.";
        Detach();
        return false;
    }
    if (!pendingStates_.Push(batch)) {
        diagnostic = L"Controller isolation host input queue overflowed.";
        Detach();
        return false;
    }
    if (response.deviceEnrollmentToken != 0) {
        if (pendingGuideCount_ == pendingGuideEvents_.size()) {
            diagnostic = L"Controller isolation Guide queue overflowed.";
            Detach();
            return false;
        }
        pendingGuideEvents_[
            (pendingGuideHead_ + pendingGuideCount_) %
            pendingGuideEvents_.size()] = response.deviceEnrollmentToken;
        ++pendingGuideCount_;
    }
    return true;
}

std::uint64_t ControllerIsolationHostSession::TakeGuide() noexcept {
    if (pendingGuideCount_ == 0) return 0;
    const auto event = pendingGuideEvents_[pendingGuideHead_];
    pendingGuideHead_ =
        (pendingGuideHead_ + 1) % pendingGuideEvents_.size();
    --pendingGuideCount_;
    return event;
}

void ControllerIsolationHostSession::Detach() noexcept {
    client_.Close();
    record_ = {};
    nextSequence_ = 1;
    progress_ = ControlProgress::None;
    attached_ = false;
    latestState_ = {};
    pendingStates_.Clear();
    pendingGuideHead_ = 0;
    pendingGuideCount_ = 0;
}

ControllerIsolationCommandStatus ControllerIsolationHostSession::ExecuteCommand(
    const ControllerIsolationCommand command,
    std::wstring& diagnostic) noexcept {
    const auto path = ControllerIsolationJournalPath();
    if (path.empty()) {
        diagnostic = L"Controller isolation LocalAppData path is unavailable.";
        return ControllerIsolationCommandStatus::Failed;
    }
    ControllerIsolationJournalStore store(path);
    std::uint32_t error{};
    auto record = store.Load(error);
    if (command == ControllerIsolationCommand::Enable && !record &&
        (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)) {
        ControllerIsolationJournalRecord created;
        if (!NewRecord(path, created, diagnostic))
            return ControllerIsolationCommandStatus::Failed;
        std::uint32_t processId{};
        if (LaunchIndependentIsolationGuardian(
                created.guardianPath, path, processId, error) !=
            GuardianLaunchStatus::Started) {
            (void)store.Remove(error);
            diagnostic = L"Controller isolation Guardian launch failed error=" +
                std::to_wstring(error);
            return ControllerIsolationCommandStatus::Failed;
        }
        const auto startedAt = GetTickCount64();
        do {
            record = store.Load(error);
            if (record && record->guardianProcessId != 0 &&
                record->guardianCreationTime != 0) break;
            Sleep(1);
        } while (GetTickCount64() - startedAt <
                 ControllerIsolationStartupTimeoutMilliseconds);
        if (!record || record->guardianProcessId == 0 ||
            record->guardianCreationTime == 0) {
            diagnostic = L"Controller isolation Guardian identity publication timed out error=" +
                std::to_wstring(error);
            return ControllerIsolationCommandStatus::Failed;
        }
    }
    if (!record) {
        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND) {
            diagnostic = L"Controller isolation journal is invalid error=" +
                std::to_wstring(error);
            return ControllerIsolationCommandStatus::RecoveryRequired;
        }
        if (command == ControllerIsolationCommand::Status ||
            command == ControllerIsolationCommand::Disable ||
            command == ControllerIsolationCommand::Recover)
            return ControllerIsolationCommandStatus::Disabled;
        return ControllerIsolationCommandStatus::Failed;
    }
    ControllerIsolationHostSession session;
    if (!session.Connect(*record, diagnostic, true)) {
        if (command == ControllerIsolationCommand::Recover &&
            RecoverWithoutGuardian(path, *record, diagnostic))
            return ControllerIsolationCommandStatus::Disabled;
        return record->phase ==
                ControllerIsolationJournalPhase::RecoveryRequired
            ? ControllerIsolationCommandStatus::RecoveryRequired
            : ControllerIsolationCommandStatus::Failed;
    }
    ControlFrame response;
    ControlMessageKind kind = ControlMessageKind::QueryStatus;
    if (command == ControllerIsolationCommand::Disable)
        kind = ControlMessageKind::Stop;
    else if (command == ControllerIsolationCommand::Recover)
        kind = ControlMessageKind::Recover;
    if (!session.Exchange(kind, response, diagnostic))
        return ControllerIsolationCommandStatus::RecoveryRequired;
    return kind == ControlMessageKind::Stop || kind == ControlMessageKind::Recover
        ? ControllerIsolationCommandStatus::Disabled
        : ConvertStatus(session.progress_, response.status);
}

} // namespace widgetrail::isolation
