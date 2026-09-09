#include "../../src/OverlayPlatformInterop/ControllerIsolationJournal.h"

#include <windows.h>

#include <cstdlib>
#include <atomic>
#include <filesystem>
#include <functional>
#include <iostream>
#include <thread>

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

ControllerIsolationJournalRecord Record() {
    ControllerIsolationJournalRecord record;
    record.authority = {1, 2, 3, 4};
    record.nonce.fill(5);
    record.hostProcessId = 6;
    record.hostCreationTime = 7;
    record.guardianProcessId = 8;
    record.guardianCreationTime = 9;
    record.workerProcessId = 10;
    record.workerCreationTime = 11;
    record.hostPath = L"C:\\host.exe";
    record.guardianPath = L"C:\\guardian.exe";
    record.workerPath = L"C:\\worker.exe";
    record.hostSha256.fill(8);
    record.guardianSha256.fill(9);
    record.workerSha256.fill(10);
    record.policy.before = {{L"existing.exe"}, {L"existing-device"}, true, false};
    record.policy.ownedApplicationPaths = {L"worker.exe"};
    record.policy.ownedDeviceInstanceIds = {L"physical-device"};
    record.policy.activatedBySession = false;
    record.phase = ControllerIsolationJournalPhase::PolicyPlanned;
    return record;
}
}

int main() {
    const auto directory = std::filesystem::temp_directory_path() /
        (L"wrail-controller-isolation-journal-" +
         std::to_wstring(GetCurrentProcessId()));
    const auto path = directory / L"session.bin";
    ControllerIsolationJournalStore store(path);
    std::uint32_t error{};
    const auto expected = Record();
    auto competing = expected;
    ++competing.hostProcessId;
    std::atomic_bool start{};
    std::atomic_int admitted{};
    const auto attempt = [&](const ControllerIsolationJournalRecord& candidate) {
        while (!start.load(std::memory_order_acquire)) std::this_thread::yield();
        std::uint32_t attemptError{};
        if (store.SaveAtomicIfAbsent(candidate, attemptError))
            admitted.fetch_add(1, std::memory_order_relaxed);
    };
    std::thread first(attempt, std::cref(expected));
    std::thread second(attempt, std::cref(competing));
    start.store(true, std::memory_order_release);
    first.join();
    second.join();
    const auto admittedRecord = store.Load(error);
    Check(admitted.load() == 1 && admittedRecord &&
              (admittedRecord->hostProcessId == expected.hostProcessId ||
               admittedRecord->hostProcessId == competing.hostProcessId),
          "cross-process startup exclusion admits exactly one absent-journal authority");
    Check(admittedRecord && store.RemoveIfCurrent(*admittedRecord, error),
          "startup winner alone can remove its exact record");
    Check(store.SaveAtomic(expected, error),
          "valid journal is written atomically");
    const auto loaded = store.Load(error);
    Check(loaded && loaded->authority == expected.authority &&
              loaded->nonce == expected.nonce &&
              loaded->guardianProcessId == expected.guardianProcessId &&
              loaded->guardianCreationTime == expected.guardianCreationTime &&
              loaded->workerProcessId == expected.workerProcessId &&
              loaded->workerCreationTime == expected.workerCreationTime &&
              loaded->hostPath == expected.hostPath &&
              loaded->guardianPath == expected.guardianPath &&
              loaded->workerPath == expected.workerPath &&
              loaded->hostSha256 == expected.hostSha256 &&
              loaded->guardianSha256 == expected.guardianSha256 &&
              loaded->workerSha256 == expected.workerSha256 &&
              loaded->policy.before == expected.policy.before &&
              loaded->policy.ownedApplicationPaths ==
                  expected.policy.ownedApplicationPaths &&
              loaded->policy.ownedDeviceInstanceIds ==
                  expected.policy.ownedDeviceInstanceIds &&
              loaded->phase == expected.phase,
          "journal round-trip retains exact authority and owned delta");
    auto stale = expected;
    ++stale.hostProcessId;
    auto replacement = expected;
    replacement.phase = ControllerIsolationJournalPhase::RecoveryRequired;
    Check(!store.SaveAtomicIfCurrent(stale, replacement, error) &&
              error == ERROR_REVISION_MISMATCH &&
              !store.RemoveIfCurrent(stale, error) &&
              error == ERROR_REVISION_MISMATCH &&
              store.Matches(expected, error),
          "stale startup or recovery owners cannot replace or remove the current journal");
    Check(store.SaveAtomicIfCurrent(expected, replacement, error) &&
              store.SaveAtomicIfCurrent(replacement, expected, error),
          "the exact serialized owner can advance and restore its journal phase");
    HANDLE file = CreateFileW(
        path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    LARGE_INTEGER end{};
    end.QuadPart = -1;
    BYTE value{};
    DWORD transferred{};
    Check(file != INVALID_HANDLE_VALUE &&
              SetFilePointerEx(file, end, nullptr, FILE_END) &&
              ReadFile(file, &value, 1, &transferred, nullptr) &&
              transferred == 1,
          "corruption fixture reads the final journal byte");
    value ^= 0x5A;
    end.QuadPart = -1;
    Check(SetFilePointerEx(file, end, nullptr, FILE_END) &&
              WriteFile(file, &value, 1, &transferred, nullptr) &&
              transferred == 1,
          "corruption fixture changes exactly one persisted byte");
    CloseHandle(file);
    Check(!store.Load(error) && error == ERROR_CRC,
          "journal corruption fails closed before recovery fields are used");
    Check(store.SaveAtomic(expected, error),
          "valid authority can atomically replace a corrupted journal");
    Check(store.Remove(error) && !std::filesystem::exists(path),
          "journal removal is exact and idempotent");
    Check(store.Remove(error), "missing journal removal remains successful");
    std::error_code ignored;
    std::filesystem::remove(directory, ignored);
    std::cout << "ControllerIsolationJournalTests passed (" << checks
              << " checks)\n";
}
