#include "ControllerIsolationProcessOwner.h"

#include <bcrypt.h>

#include <array>
#include <cwchar>
#include <limits>
#include <string_view>
#include <utility>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr wchar_t kChannelSwitch[] = L"--controller-isolation-channel";

void CloseHandleIfPresent(HANDLE& handle) noexcept {
    if (handle && handle != INVALID_HANDLE_VALUE) CloseHandle(handle);
    handle = nullptr;
}

[[nodiscard]] bool ParseHandle(
    const wchar_t* text,
    HANDLE& handle) noexcept {
    if (!text || !*text) return false;
    wchar_t* end{};
    const auto value = _wcstoui64(text, &end, 10);
    if (!end || *end != L'\0' || value == 0 ||
        value > std::numeric_limits<std::uintptr_t>::max()) {
        return false;
    }
    handle = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(value));
    DWORD flags{};
    return GetHandleInformation(handle, &flags) != FALSE;
}

[[nodiscard]] std::wstring HandleText(HANDLE handle) {
    return std::to_wstring(reinterpret_cast<std::uintptr_t>(handle));
}

[[nodiscard]] bool SetInheritable(HANDLE handle, const bool inheritable) noexcept {
    return SetHandleInformation(
        handle, HANDLE_FLAG_INHERIT,
        inheritable ? HANDLE_FLAG_INHERIT : 0) != FALSE;
}

[[nodiscard]] bool FillNonce(ControllerIsolationNonce& nonce) noexcept {
    return BCryptGenRandom(
               nullptr, nonce.data(), static_cast<ULONG>(nonce.size()),
               BCRYPT_USE_SYSTEM_PREFERRED_RNG) == 0 &&
        ValidNonce(nonce);
}

[[nodiscard]] HANDLE CurrentProcessHandleForChild() noexcept {
    HANDLE result{};
    if (!DuplicateHandle(
            GetCurrentProcess(), GetCurrentProcess(), GetCurrentProcess(),
            &result, SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, TRUE, 0)) {
        return nullptr;
    }
    return result;
}

[[nodiscard]] bool ExactParent(
    HANDLE parent,
    const ProcessAdmission& admission,
    const wchar_t* expectedParentFileName) noexcept {
    if (!parent || WaitForSingleObject(parent, 0) != WAIT_TIMEOUT ||
        GetProcessId(parent) != admission.parentProcessId) {
        return false;
    }
    if (ProcessCreationTime(parent) != admission.parentCreationTime ||
        !expectedParentFileName || !*expectedParentFileName) {
        return false;
    }
    std::array<wchar_t, 32'768> parentPath{};
    DWORD parentPathLength = static_cast<DWORD>(parentPath.size());
    std::array<wchar_t, 32'768> childPath{};
    const auto childPathLength = GetModuleFileNameW(
        nullptr, childPath.data(), static_cast<DWORD>(childPath.size()));
    if (!QueryFullProcessImageNameW(
            parent, 0, parentPath.data(), &parentPathLength) ||
        childPathLength == 0 || childPathLength >= childPath.size()) {
        return false;
    }
    try {
        const std::filesystem::path parentImage(
            std::wstring_view(parentPath.data(), parentPathLength));
        const std::filesystem::path childImage(
            std::wstring_view(childPath.data(), childPathLength));
        return _wcsicmp(
                   parentImage.filename().c_str(), expectedParentFileName) == 0 &&
            _wcsicmp(
                parentImage.parent_path().c_str(),
                childImage.parent_path().c_str()) == 0;
    } catch (...) {
        return false;
    }
}

} // namespace

std::uint64_t ProcessCreationTime(HANDLE process) noexcept {
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!process || !GetProcessTimes(process, &created, &exited, &kernel, &user))
        return 0;
    ULARGE_INTEGER value{};
    value.LowPart = created.dwLowDateTime;
    value.HighPart = created.dwHighDateTime;
    return value.QuadPart;
}

ChildControlChannel::~ChildControlChannel() { Close(); }

ChildControlChannel::ChildControlChannel(ChildControlChannel&& other) noexcept {
    *this = std::move(other);
}

ChildControlChannel& ChildControlChannel::operator=(
    ChildControlChannel&& other) noexcept {
    if (this == &other) return *this;
    Close();
    mapping_ = std::exchange(other.mapping_, nullptr);
    requestEvent_ = std::exchange(other.requestEvent_, nullptr);
    responseEvent_ = std::exchange(other.responseEvent_, nullptr);
    stopEvent_ = std::exchange(other.stopEvent_, nullptr);
    parentProcess_ = std::exchange(other.parentProcess_, nullptr);
    shared_ = std::exchange(other.shared_, nullptr);
    admission_ = std::exchange(other.admission_, ProcessAdmission{});
    gate_ = std::move(other.gate_);
    return *this;
}

std::optional<ChildControlChannel> ChildControlChannel::Open(
    const int argumentCount,
    wchar_t** arguments,
    const wchar_t* expectedParentFileName,
    std::wstring& error) noexcept {
    if (argumentCount != 7 || !arguments ||
        _wcsicmp(arguments[1], kChannelSwitch) != 0) {
        error = L"Controller isolation requires one exact inherited channel.";
        return std::nullopt;
    }
    ChildControlChannel channel;
    if (!ParseHandle(arguments[2], channel.mapping_) ||
        !ParseHandle(arguments[3], channel.requestEvent_) ||
        !ParseHandle(arguments[4], channel.responseEvent_) ||
        !ParseHandle(arguments[5], channel.stopEvent_) ||
        !ParseHandle(arguments[6], channel.parentProcess_)) {
        error = L"Controller isolation inherited handle admission failed.";
        channel.Close();
        return std::nullopt;
    }
    const std::array handles{
        channel.mapping_, channel.requestEvent_, channel.responseEvent_,
        channel.stopEvent_, channel.parentProcess_};
    for (std::size_t left = 0; left < handles.size(); ++left) {
        for (std::size_t right = left + 1; right < handles.size(); ++right) {
            if (handles[left] == handles[right]) {
                error = L"Controller isolation inherited handles are not distinct.";
                channel.Close();
                return std::nullopt;
            }
        }
    }
    channel.shared_ = static_cast<ControlSharedMemory*>(MapViewOfFile(
        channel.mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0,
        sizeof(ControlSharedMemory)));
    if (!channel.shared_) {
        error = L"Controller isolation parent or nonce authority was rejected.";
        channel.Close();
        return std::nullopt;
    }
    const ProcessAdmission admission = channel.shared_->admission;
    if (!admission.authority.valid() || !ValidNonce(admission.nonce) ||
        !ExactParent(
            channel.parentProcess_, admission, expectedParentFileName)) {
        error = L"Controller isolation parent or nonce authority was rejected.";
        channel.Close();
        return std::nullopt;
    }
    channel.admission_ = admission;
    channel.gate_.emplace(
        channel.admission_.nonce, channel.admission_.authority, 1);
    return channel;
}

ChildWaitResult ChildControlChannel::WaitForRequest(
    const DWORD timeoutMilliseconds,
    ControlFrame& request) noexcept {
    if (!shared_ || !gate_) return ChildWaitResult::Failed;
    // Stop and parent death precede request admission when multiple objects
    // become signaled together. A zero-time final observation closes the race
    // at the timeout boundary without extending or retrying the deadline.
    const std::array waits{stopEvent_, parentProcess_, requestEvent_};
    auto result = WaitForMultipleObjects(
        static_cast<DWORD>(waits.size()), waits.data(), FALSE,
        timeoutMilliseconds);
    if (result == WAIT_TIMEOUT) {
        result = WaitForMultipleObjects(
            static_cast<DWORD>(waits.size()), waits.data(), FALSE, 0);
        if (result == WAIT_TIMEOUT) return ChildWaitResult::TimedOut;
    }
    if (result == WAIT_OBJECT_0) return ChildWaitResult::Stop;
    if (result == WAIT_OBJECT_0 + 1) return ChildWaitResult::ParentExited;
    if (result != WAIT_OBJECT_0 + 2) return ChildWaitResult::Failed;
    request = shared_->request;
    return gate_->Admit(request) == ControlFrameValidation::Accepted
        ? ChildWaitResult::Request
        : ChildWaitResult::InvalidFrame;
}

bool ChildControlChannel::Reply(
    const ControlMessageKind kind,
    const ControlFrame& request,
    const std::uint32_t status,
    const std::uint32_t processId,
    const ControlProgress progress,
    const GamepadState& state,
    const std::uint64_t eventOrdinal,
    const ControllerInputBatch& inputBatch) noexcept {
    if (!shared_) return false;
    shared_->response = MakeControlFrame(
        kind, admission_.nonce, admission_.authority, request.sequence);
    shared_->response.status = status;
    shared_->response.processId = processId;
    shared_->response.observedAtMilliseconds =
        static_cast<std::uint64_t>(progress);
    shared_->response.state = state;
    shared_->response.deviceEnrollmentToken = eventOrdinal;
    shared_->response.inputBatch = inputBatch;
    MemoryBarrier();
    return SetEvent(responseEvent_) != FALSE;
}

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
bool ChildControlChannel::ReplyRawForTest(
    const ControlFrame& response) noexcept {
    if (!shared_) return false;
    shared_->response = response;
    MemoryBarrier();
    return SetEvent(responseEvent_) != FALSE;
}
#endif

void ChildControlChannel::Close() noexcept {
    gate_.reset();
    admission_ = {};
    if (shared_) UnmapViewOfFile(shared_);
    shared_ = nullptr;
    CloseHandleIfPresent(mapping_);
    CloseHandleIfPresent(requestEvent_);
    CloseHandleIfPresent(responseEvent_);
    CloseHandleIfPresent(stopEvent_);
    CloseHandleIfPresent(parentProcess_);
}

ControllerIsolationProcessOwner::~ControllerIsolationProcessOwner() { Stop(); }

ControllerIsolationProcessOwner::ControllerIsolationProcessOwner(
    ControllerIsolationProcessOwner&& other) noexcept {
    *this = std::move(other);
}

ControllerIsolationProcessOwner& ControllerIsolationProcessOwner::operator=(
    ControllerIsolationProcessOwner&& other) noexcept {
    if (this == &other) return *this;
    Stop();
    mapping_ = std::exchange(other.mapping_, nullptr);
    requestEvent_ = std::exchange(other.requestEvent_, nullptr);
    responseEvent_ = std::exchange(other.responseEvent_, nullptr);
    stopEvent_ = std::exchange(other.stopEvent_, nullptr);
    childProcess_ = std::exchange(other.childProcess_, nullptr);
    job_ = std::exchange(other.job_, nullptr);
    shared_ = std::exchange(other.shared_, nullptr);
    nonce_ = std::exchange(other.nonce_, ControllerIsolationNonce{});
    authority_ = std::exchange(other.authority_, RoutingAuthority{});
    nextSequence_ = std::exchange(other.nextSequence_, 1);
    processId_ = std::exchange(other.processId_, 0);
    startupResponse_ = std::exchange(other.startupResponse_, ControlFrame{});
    return *this;
}

bool ControllerIsolationProcessOwner::Start(
    const std::filesystem::path& executable,
    const RoutingAuthority& authority,
    const std::uint32_t activeProcessLimit,
    const DWORD timeoutMilliseconds,
    std::wstring& error,
    const ControllerIsolationChildAdmission beforeResume,
    void* const admissionContext) noexcept {
    std::error_code fileError;
    const bool executablePresent =
        std::filesystem::is_regular_file(executable, fileError);
    if (childProcess_ || job_ || shared_ || !authority.valid() ||
        activeProcessLimit == 0 || activeProcessLimit > 2 ||
        executable.empty() || fileError || !executablePresent) {
        error = L"Controller isolation child launch authority is invalid.";
        return false;
    }
    authority_ = authority;
    if (!FillNonce(nonce_)) {
        error = L"Controller isolation nonce generation failed.";
        return false;
    }
    mapping_ = CreateFileMappingW(
        INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
        sizeof(ControlSharedMemory), nullptr);
    requestEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    responseEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    stopEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE parentProcess = CurrentProcessHandleForChild();
    if (!mapping_ || !requestEvent_ || !responseEvent_ || !stopEvent_ ||
        !parentProcess) {
        error = L"Controller isolation private channel creation failed.";
        CloseHandleIfPresent(parentProcess);
        Stop();
        return false;
    }
    shared_ = static_cast<ControlSharedMemory*>(MapViewOfFile(
        mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0,
        sizeof(ControlSharedMemory)));
    if (!shared_) {
        error = L"Controller isolation private channel mapping failed.";
        CloseHandleIfPresent(parentProcess);
        Stop();
        return false;
    }
    *shared_ = {};
    shared_->admission = {
        GetCurrentProcessId(), ProcessCreationTime(GetCurrentProcess()),
        authority_, nonce_};
    shared_->request = MakeControlFrame(
        ControlMessageKind::Hello, nonce_, authority_, nextSequence_);

    job_ = CreateJobObjectW(nullptr, nullptr);
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags =
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
    limits.BasicLimitInformation.ActiveProcessLimit = activeProcessLimit;
    if (!job_ || !SetInformationJobObject(
            job_, JobObjectExtendedLimitInformation, &limits, sizeof(limits))) {
        error = L"Controller isolation child job creation failed.";
        CloseHandleIfPresent(parentProcess);
        Stop();
        return false;
    }

    std::array inherited{
        mapping_, requestEvent_, responseEvent_, stopEvent_, parentProcess};
    for (const auto handle : inherited) {
        if (!SetInheritable(handle, true)) {
            error = L"Controller isolation handle allowlist creation failed.";
            for (const auto admitted : inherited)
                (void)SetInheritable(admitted, false);
            CloseHandleIfPresent(parentProcess);
            Stop();
            return false;
        }
    }
    SIZE_T attributeBytes{};
    InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
    std::vector<std::byte> attributeStorage(attributeBytes);
    auto* attributes = reinterpret_cast<PPROC_THREAD_ATTRIBUTE_LIST>(
        attributeStorage.data());
    if (!InitializeProcThreadAttributeList(
            attributes, 1, 0, &attributeBytes)) {
        error = L"Controller isolation restricted inheritance setup failed.";
        for (const auto handle : inherited) (void)SetInheritable(handle, false);
        CloseHandleIfPresent(parentProcess);
        Stop();
        return false;
    }
    if (!UpdateProcThreadAttribute(
            attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
            inherited.data(), sizeof(inherited), nullptr, nullptr)) {
        DeleteProcThreadAttributeList(attributes);
        error = L"Controller isolation restricted inheritance setup failed.";
        for (const auto handle : inherited) (void)SetInheritable(handle, false);
        CloseHandleIfPresent(parentProcess);
        Stop();
        return false;
    }
    std::wstring command = L"\"" + executable.wstring() + L"\" " +
        kChannelSwitch;
    for (const auto handle : inherited) command += L" " + HandleText(handle);
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = attributes;
    PROCESS_INFORMATION process{};
    const auto created = CreateProcessW(
        executable.c_str(), mutableCommand.data(), nullptr, nullptr, TRUE,
        CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
        nullptr, executable.parent_path().c_str(), &startup.StartupInfo,
        &process);
    DeleteProcThreadAttributeList(attributes);
    for (const auto handle : inherited) (void)SetInheritable(handle, false);
    CloseHandleIfPresent(parentProcess);
    if (!created) {
        error = L"Controller isolation child creation failed.";
        Stop();
        return false;
    }
    childProcess_ = process.hProcess;
    processId_ = process.dwProcessId;
    const auto creationTime = ProcessCreationTime(process.hProcess);
    if (!AssignProcessToJobObject(job_, process.hProcess) ||
        creationTime == 0 ||
        (beforeResume && !beforeResume(
            admissionContext, processId_, creationTime, error)) ||
        ResumeThread(process.hThread) == static_cast<DWORD>(-1)) {
        CloseHandle(process.hThread);
        (void)TerminateProcess(childProcess_, ERROR_PROCESS_ABORTED);
        (void)WaitForSingleObject(childProcess_, timeoutMilliseconds);
        if (error.empty())
            error = L"Controller isolation exact child job admission failed.";
        Stop();
        return false;
    }
    CloseHandle(process.hThread);
    MemoryBarrier();
    if (!SetEvent(requestEvent_) ||
        !WaitForResponse(
            ControlMessageKind::Hello, nextSequence_, timeoutMilliseconds,
            startupResponse_, error)) {
        Stop();
        return false;
    }
    ++nextSequence_;
    return true;
}

bool ControllerIsolationProcessOwner::Send(
    const ControlMessageKind kind,
    const DWORD timeoutMilliseconds,
    ControlFrame& response,
    std::wstring& error) noexcept {
    auto request = MakeControlFrame(kind, nonce_, authority_, nextSequence_);
    return Send(request, timeoutMilliseconds, response, error);
}

bool ControllerIsolationProcessOwner::Send(
    const ControlFrame& request,
    const DWORD timeoutMilliseconds,
    ControlFrame& response,
    std::wstring& error) noexcept {
    const auto kind = request.kind;
    if (!shared_ || !childProcess_ || nextSequence_ == 0 ||
        kind == ControlMessageKind::Hello ||
        kind == ControlMessageKind::HelloAccepted) {
        error = L"Controller isolation command state is invalid.";
        return false;
    }
    shared_->request = MakeControlFrame(kind, nonce_, authority_, nextSequence_);
    shared_->request.deviceEnrollmentToken = request.deviceEnrollmentToken;
    shared_->request.observedAtMilliseconds = request.observedAtMilliseconds;
    shared_->request.state = request.state;
    shared_->request.enrollment = request.enrollment;
    MemoryBarrier();
    if (!SetEvent(requestEvent_) ||
        !WaitForResponse(
            kind, nextSequence_, timeoutMilliseconds, response, error)) {
        return false;
    }
    if (nextSequence_ == std::numeric_limits<std::uint64_t>::max())
        nextSequence_ = 0;
    else
        ++nextSequence_;
    return true;
}

void ControllerIsolationProcessOwner::Stop() noexcept {
    if (stopEvent_) SetEvent(stopEvent_);
    if (childProcess_) {
        const auto result = WaitForSingleObject(
            childProcess_, ControllerIsolationShutdownTimeoutMilliseconds);
        if (result == WAIT_TIMEOUT) {
            (void)TerminateProcess(childProcess_, ERROR_PROCESS_ABORTED);
            (void)WaitForSingleObject(
                childProcess_, ControllerIsolationShutdownTimeoutMilliseconds);
        }
    }
    CloseProcessHandles();
    CloseChannelHandles();
    authority_ = {};
    nonce_ = {};
    nextSequence_ = 1;
    processId_ = 0;
    startupResponse_ = {};
}

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
bool ControllerIsolationProcessOwner::TerminateExactForTest() noexcept {
    return childProcess_ &&
        TerminateProcess(childProcess_, ERROR_PROCESS_ABORTED) != FALSE;
}

ControlFrame ControllerIsolationProcessOwner::NextFrameForTest(
    const ControlMessageKind kind) const noexcept {
    return MakeControlFrame(kind, nonce_, authority_, nextSequence_);
}

bool ControllerIsolationProcessOwner::SendRawForTest(
    const ControlFrame& request,
    const DWORD timeoutMilliseconds,
    ControlFrame& response,
    std::wstring& error) noexcept {
    if (!shared_ || !childProcess_) return false;
    shared_->request = request;
    MemoryBarrier();
    return SetEvent(requestEvent_) &&
        WaitForResponse(
            request.kind, request.sequence, timeoutMilliseconds,
            response, error);
}

bool ControllerIsolationProcessOwner::WaitForExitForTest(
    const DWORD timeoutMilliseconds) noexcept {
    return childProcess_ &&
        WaitForSingleObject(childProcess_, timeoutMilliseconds) == WAIT_OBJECT_0;
}

bool ControllerIsolationProcessOwner::SignalStopAndRequestForTest(
    const ControlFrame& request) noexcept {
    if (!shared_ || !childProcess_) return false;
    shared_->request = request;
    MemoryBarrier();
    return SetEvent(stopEvent_) != FALSE &&
        SetEvent(requestEvent_) != FALSE;
}

void ControllerIsolationProcessOwner::CorruptSharedAdmissionForTest() noexcept {
    if (shared_) shared_->admission = {};
}
#endif

bool ControllerIsolationProcessOwner::WaitForResponse(
    const ControlMessageKind requestKind,
    const std::uint64_t sequence,
    const DWORD timeoutMilliseconds,
    ControlFrame& response,
    std::wstring& error) noexcept {
    const std::array waits{responseEvent_, childProcess_};
    const auto result = WaitForMultipleObjects(
        static_cast<DWORD>(waits.size()), waits.data(), FALSE,
        timeoutMilliseconds);
    if (result == WAIT_TIMEOUT) {
        error = L"Controller isolation child response timed out.";
        return false;
    }
    if (result == WAIT_OBJECT_0 + 1) {
        error = L"Controller isolation child exited before responding.";
        return false;
    }
    if (result != WAIT_OBJECT_0) {
        error = L"Controller isolation child response wait failed.";
        return false;
    }
    MemoryBarrier();
    response = shared_->response;
    switch (ValidateControlResponse(
        requestKind, response, nonce_, authority_, sequence)) {
    case ControlResponseValidation::Accepted:
        return true;
    case ControlResponseValidation::RemoteFailure:
        error = L"Controller isolation child terminal status " +
            std::to_wstring(response.status) + L".";
        return false;
    case ControlResponseValidation::WrongResponseKind:
        error = L"Controller isolation child response kind was rejected.";
        return false;
    case ControlResponseValidation::InvalidShape:
    case ControlResponseValidation::InvalidKind:
    case ControlResponseValidation::WrongNonce:
    case ControlResponseValidation::WrongAuthority:
    case ControlResponseValidation::WrongSequence:
        error = L"Controller isolation child response authority was rejected.";
        return false;
    }
    error = L"Controller isolation child response validation failed.";
    return false;
}

void ControllerIsolationProcessOwner::CloseChannelHandles() noexcept {
    if (shared_) UnmapViewOfFile(shared_);
    shared_ = nullptr;
    CloseHandleIfPresent(mapping_);
    CloseHandleIfPresent(requestEvent_);
    CloseHandleIfPresent(responseEvent_);
    CloseHandleIfPresent(stopEvent_);
}

void ControllerIsolationProcessOwner::CloseProcessHandles() noexcept {
    CloseHandleIfPresent(childProcess_);
    CloseHandleIfPresent(job_);
}

} // namespace widgetrail::isolation
