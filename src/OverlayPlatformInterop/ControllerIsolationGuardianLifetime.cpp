#include "ControllerIsolationGuardianLifetime.h"
#include "ControllerIsolationProcessOwner.h"

#include <array>
#include <string>
#include <vector>

namespace widgetrail::isolation {
namespace {

[[nodiscard]] bool QuoteArgument(
    const std::wstring& value, std::wstring& command) {
    if (value.empty() || value.find(L'\0') != std::wstring::npos) return false;
    command.push_back(L'"');
    std::size_t slashes{};
    for (const auto character : value) {
        if (character == L'\\') {
            ++slashes;
            continue;
        }
        if (character == L'"') {
            command.append(slashes * 2 + 1, L'\\');
            command.push_back(L'"');
        } else {
            command.append(slashes, L'\\');
            command.push_back(character);
        }
        slashes = 0;
    }
    command.append(slashes * 2, L'\\');
    command.push_back(L'"');
    return true;
}

} // namespace

[[nodiscard]] static GuardianLifetimeStatus ObserveExactProcess(
    const std::uint32_t processId,
    const std::uint64_t creationTime,
    const std::filesystem::path& expectedPath,
    const std::array<std::uint8_t, 32>& expectedHash,
    std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    if (processId == 0 || creationTime == 0 || expectedPath.empty()) {
        nativeError = ERROR_INVALID_DATA;
        return ClassifyGuardianLifetime(false, false, nativeError, false, 0);
    }
    HANDLE process = OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE,
        FALSE, processId);
    if (!process) {
        nativeError = GetLastError();
        return ClassifyGuardianLifetime(true, false, nativeError, false, 0);
    }
    std::array<wchar_t, 32'768> path{};
    DWORD length = static_cast<DWORD>(path.size());
    const bool exact = ProcessCreationTime(process) == creationTime &&
        QueryFullProcessImageNameW(process, 0, path.data(), &length) &&
        _wcsicmp(std::wstring(path.data(), length).c_str(),
                 expectedPath.c_str()) == 0;
    if (!exact) {
        nativeError = ERROR_REVISION_MISMATCH;
        CloseHandle(process);
        return ClassifyGuardianLifetime(true, true, 0, false, 0);
    }
    std::array<std::uint8_t, 32> hash{};
    if (!ControllerIsolationFileSha256(path.data(), hash, nativeError) ||
        hash != expectedHash) {
        if (nativeError == ERROR_SUCCESS) nativeError = ERROR_INVALID_IMAGE_HASH;
        CloseHandle(process);
        return ClassifyGuardianLifetime(true, true, 0, false, 0);
    }
    const auto wait = WaitForSingleObject(process, 0);
    CloseHandle(process);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_TIMEOUT)
        nativeError = GetLastError();
    return ClassifyGuardianLifetime(true, true, 0, true, wait);
}

GuardianLifetimeStatus ObserveExactIsolationGuardian(
    const ControllerIsolationJournalRecord& record,
    std::uint32_t& nativeError) noexcept {
    return ObserveExactProcess(
        record.guardianProcessId, record.guardianCreationTime,
        record.guardianPath, record.guardianSha256, nativeError);
}

GuardianLifetimeStatus ObserveExactIsolationWorker(
    const ControllerIsolationJournalRecord& record,
    std::uint32_t& nativeError) noexcept {
    return ObserveExactProcess(
        record.workerProcessId, record.workerCreationTime,
        record.workerPath, record.workerSha256, nativeError);
}

GuardianLaunchStatus LaunchIndependentIsolationGuardian(
    const std::filesystem::path& executable,
    const std::filesystem::path& journal,
    ControllerIsolationJournalRecord& record,
    std::uint32_t& processId,
    std::uint32_t& nativeError) noexcept {
    processId = 0;
    nativeError = ERROR_SUCCESS;
    std::error_code fileError;
    if (executable.empty() || journal.empty() ||
        !std::filesystem::is_regular_file(executable, fileError) || fileError) {
        nativeError = ERROR_INVALID_PARAMETER;
        return GuardianLaunchStatus::InvalidInput;
    }
    BOOL inJob{};
    DWORD creationFlags = DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP |
        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED;
    if (!IsProcessInJob(GetCurrentProcess(), nullptr, &inJob)) {
        nativeError = GetLastError();
        return GuardianLaunchStatus::CreateFailed;
    }
    if (inJob) {
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        if (!QueryInformationJobObject(
                nullptr, JobObjectExtendedLimitInformation, &limits,
                sizeof(limits), nullptr) ||
            (limits.BasicLimitInformation.LimitFlags &
             (JOB_OBJECT_LIMIT_BREAKAWAY_OK |
              JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK)) == 0) {
            nativeError = ERROR_ACCESS_DENIED;
            return GuardianLaunchStatus::ParentJobForbidsBreakaway;
        }
        creationFlags |= CREATE_BREAKAWAY_FROM_JOB;
    }
    try {
        std::wstring command;
        if (!QuoteArgument(executable.wstring(), command))
            return GuardianLaunchStatus::InvalidInput;
        command += L" --controller-isolation-session ";
        if (!QuoteArgument(journal.wstring(), command))
            return GuardianLaunchStatus::InvalidInput;
        command += L" --expected-authority " +
            std::to_wstring(record.authority.sessionGeneration) + L" " +
            std::to_wstring(record.authority.deviceGeneration) + L" " +
            std::to_wstring(record.authority.targetGeneration) + L" " +
            std::to_wstring(record.authority.leaseId);
        std::vector<wchar_t> mutableCommand(command.begin(), command.end());
        mutableCommand.push_back(L'\0');
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(
                executable.c_str(), mutableCommand.data(), nullptr, nullptr,
                FALSE, creationFlags, nullptr, executable.parent_path().c_str(),
                &startup, &process)) {
            nativeError = GetLastError();
            return GuardianLaunchStatus::CreateFailed;
        }
        auto replacement = record;
        replacement.guardianProcessId = process.dwProcessId;
        replacement.guardianCreationTime = ProcessCreationTime(process.hProcess);
        ControllerIsolationJournalStore store(journal);
        const bool published = replacement.guardianCreationTime != 0 &&
            store.SaveAtomicIfCurrent(record, replacement, nativeError);
        if (published) record = replacement;
        if (!published ||
            ResumeThread(process.hThread) == static_cast<DWORD>(-1)) {
            if (nativeError == ERROR_SUCCESS) nativeError = GetLastError();
            (void)TerminateProcess(process.hProcess, ERROR_PROCESS_ABORTED);
            (void)WaitForSingleObject(
                process.hProcess, ControllerIsolationShutdownTimeoutMilliseconds);
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return GuardianLaunchStatus::CreateFailed;
        }
        processId = process.dwProcessId;
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return GuardianLaunchStatus::Started;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return GuardianLaunchStatus::CreateFailed;
    }
}

} // namespace widgetrail::isolation
