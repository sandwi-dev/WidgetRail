#include "ControllerIsolationJournal.h"
#include "ControllerIsolationProcessOwner.h"

#include <windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <cstring>
#include <limits>
#include <span>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr std::uint32_t kMagic = 0x4A494357; // WCIJ
constexpr wchar_t kLifecycleMutex[] =
    L"Local\\WidgetRail.ControllerIsolation.SessionLifecycle";
constexpr std::size_t kMaximumBytes = 128 * 1024;
constexpr std::size_t kMaximumStrings = 512;
constexpr std::size_t kMaximumStringCharacters = 32'768;

struct Header final {
    std::uint32_t magic{kMagic};
    std::uint32_t version{ControllerIsolationJournalRecord::Version};
    std::uint32_t bytes{};
    std::uint32_t hostProcessId{};
    std::uint64_t hostCreationTime{};
    std::uint32_t guardianProcessId{};
    std::uint64_t guardianCreationTime{};
    std::uint32_t workerProcessId{};
    std::uint64_t workerCreationTime{};
    RoutingAuthority authority{};
    ControllerIsolationNonce nonce{};
    std::array<std::uint8_t, 32> hostSha256{};
    std::array<std::uint8_t, 32> guardianSha256{};
    std::array<std::uint8_t, 32> workerSha256{};
    std::array<std::uint8_t, 32> contentSha256{};
    std::uint32_t flags{};
    ControllerIsolationJournalPhase phase{
        ControllerIsolationJournalPhase::Prepared};
    std::uint32_t beforeApplications{};
    std::uint32_t beforeDevices{};
    std::uint32_t ownedApplications{};
    std::uint32_t ownedDevices{};
};
static_assert(std::is_trivially_copyable_v<Header>);

[[nodiscard]] bool HashBytes(
    const std::span<const std::byte> bytes,
    std::array<std::uint8_t, 32>& digest) noexcept {
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    const bool succeeded = BCryptOpenAlgorithmProvider(
            &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0 &&
        bytes.size() <= std::numeric_limits<ULONG>::max() &&
        BCryptHashData(
            hash, reinterpret_cast<PUCHAR>(
                      const_cast<std::byte*>(bytes.data())),
            static_cast<ULONG>(bytes.size()), 0) == 0 &&
        BCryptFinishHash(
            hash, digest.data(), static_cast<ULONG>(digest.size()), 0) == 0;
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    return succeeded;
}

template <typename T>
void Append(std::vector<std::byte>& bytes, const T& value) {
    const auto* first = reinterpret_cast<const std::byte*>(&value);
    bytes.insert(bytes.end(), first, first + sizeof(value));
}

[[nodiscard]] bool AppendString(
    std::vector<std::byte>& bytes, const std::wstring& value) {
    if (value.empty() || value.size() > kMaximumStringCharacters ||
        value.size() > std::numeric_limits<std::uint32_t>::max()) return false;
    const auto characters = static_cast<std::uint32_t>(value.size());
    Append(bytes, characters);
    const auto* first = reinterpret_cast<const std::byte*>(value.data());
    bytes.insert(bytes.end(), first, first + value.size() * sizeof(wchar_t));
    return bytes.size() <= kMaximumBytes;
}

[[nodiscard]] bool AppendSet(
    std::vector<std::byte>& bytes, const std::set<std::wstring>& values) {
    if (values.size() > kMaximumStrings) return false;
    for (const auto& value : values)
        if (!AppendString(bytes, value)) return false;
    return true;
}

template <typename T>
[[nodiscard]] bool Read(
    const std::span<const std::byte> bytes, std::size_t& cursor,
    T& value) noexcept {
    if (cursor > bytes.size() || bytes.size() - cursor < sizeof(value))
        return false;
    std::memcpy(&value, bytes.data() + cursor, sizeof(value));
    cursor += sizeof(value);
    return true;
}

[[nodiscard]] bool ReadString(
    const std::span<const std::byte> bytes, std::size_t& cursor,
    std::wstring& value) {
    std::uint32_t characters{};
    if (!Read(bytes, cursor, characters) || characters == 0 ||
        characters > kMaximumStringCharacters) return false;
    const auto byteCount = static_cast<std::size_t>(characters) * sizeof(wchar_t);
    if (cursor > bytes.size() || bytes.size() - cursor < byteCount) return false;
    value.assign(
        reinterpret_cast<const wchar_t*>(bytes.data() + cursor), characters);
    cursor += byteCount;
    return value.find(L'\0') == std::wstring::npos;
}

[[nodiscard]] bool ReadSet(
    const std::span<const std::byte> bytes, std::size_t& cursor,
    const std::uint32_t count, std::set<std::wstring>& values) {
    if (count > kMaximumStrings) return false;
    for (std::uint32_t index = 0; index < count; ++index) {
        std::wstring value;
        if (!ReadString(bytes, cursor, value) ||
            !values.insert(std::move(value)).second) return false;
    }
    return true;
}

[[nodiscard]] std::vector<std::byte> Serialize(
    const ControllerIsolationJournalRecord& record) {
    Header header;
    header.hostProcessId = record.hostProcessId;
    header.hostCreationTime = record.hostCreationTime;
    header.guardianProcessId = record.guardianProcessId;
    header.guardianCreationTime = record.guardianCreationTime;
    header.workerProcessId = record.workerProcessId;
    header.workerCreationTime = record.workerCreationTime;
    header.authority = record.authority;
    header.nonce = record.nonce;
    header.hostSha256 = record.hostSha256;
    header.guardianSha256 = record.guardianSha256;
    header.workerSha256 = record.workerSha256;
    header.flags = (record.policy.before.active ? 1U : 0U) |
        (record.policy.before.applicationListInverted ? 2U : 0U) |
        (record.policy.activatedBySession ? 4U : 0U);
    header.phase = record.phase;
    header.beforeApplications =
        static_cast<std::uint32_t>(record.policy.before.applicationPaths.size());
    header.beforeDevices =
        static_cast<std::uint32_t>(record.policy.before.deviceInstanceIds.size());
    header.ownedApplications = static_cast<std::uint32_t>(
        record.policy.ownedApplicationPaths.size());
    header.ownedDevices = static_cast<std::uint32_t>(
        record.policy.ownedDeviceInstanceIds.size());
    std::vector<std::byte> bytes(sizeof(Header));
    if (!AppendString(bytes, record.hostPath) ||
        !AppendString(bytes, record.guardianPath) ||
        !AppendString(bytes, record.workerPath) ||
        !AppendSet(bytes, record.policy.before.applicationPaths) ||
        !AppendSet(bytes, record.policy.before.deviceInstanceIds) ||
        !AppendSet(bytes, record.policy.ownedApplicationPaths) ||
        !AppendSet(bytes, record.policy.ownedDeviceInstanceIds)) return {};
    header.bytes = static_cast<std::uint32_t>(bytes.size());
    std::memcpy(bytes.data(), &header, sizeof(header));
    if (!HashBytes(bytes, header.contentSha256)) return {};
    std::memcpy(bytes.data(), &header, sizeof(header));
    return bytes;
}

[[nodiscard]] bool WriteAll(
    HANDLE file, const std::span<const std::byte> bytes,
    std::uint32_t& error) noexcept {
    DWORD written{};
    if (!WriteFile(
            file, bytes.data(), static_cast<DWORD>(bytes.size()), &written,
            nullptr) || written != bytes.size() || !FlushFileBuffers(file)) {
        error = GetLastError();
        return false;
    }
    return true;
}

} // namespace

ControllerIsolationLifecycleLease::~ControllerIsolationLifecycleLease() {
    Release();
    if (mutex_) CloseHandle(mutex_);
}

bool ControllerIsolationLifecycleLease::Acquire(
    const DWORD timeoutMilliseconds, std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    if (owned_) return true;
    if (!mutex_) {
        mutex_ = CreateMutexW(nullptr, FALSE, kLifecycleMutex);
        if (!mutex_) {
            nativeError = GetLastError();
            return false;
        }
    }
    const auto wait = WaitForSingleObject(mutex_, timeoutMilliseconds);
    if (wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED) {
        owned_ = true;
        return true;
    }
    nativeError = wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
    return false;
}

void ControllerIsolationLifecycleLease::Release() noexcept {
    if (!owned_) return;
    ReleaseMutex(mutex_);
    owned_ = false;
}

bool ControllerIsolationJournalRecord::valid() const noexcept {
    const bool validPhase = phase == ControllerIsolationJournalPhase::Prepared ||
        phase == ControllerIsolationJournalPhase::PolicyPlanned ||
        phase == ControllerIsolationJournalPhase::Playing ||
        phase == ControllerIsolationJournalPhase::RecoveryRequired;
    const bool validGuardianIdentity =
        (phase == ControllerIsolationJournalPhase::Prepared &&
         ((guardianProcessId == 0 && guardianCreationTime == 0) ||
          (guardianProcessId != 0 && guardianCreationTime != 0))) ||
        (guardianProcessId != 0 && guardianCreationTime != 0);
    const bool validWorkerIdentity =
        (workerProcessId == 0 && workerCreationTime == 0) ||
        (workerProcessId != 0 && workerCreationTime != 0);
    return validPhase && validGuardianIdentity && validWorkerIdentity &&
        (phase != ControllerIsolationJournalPhase::Playing ||
         workerProcessId != 0) && authority.valid() &&
        hostProcessId != 0 && hostCreationTime != 0 &&
        ValidNonce(nonce) && !hostPath.empty() && !guardianPath.empty() &&
        !workerPath.empty() &&
        std::ranges::any_of(
            hostSha256, [](std::uint8_t value) { return value != 0; }) &&
        std::ranges::any_of(
            guardianSha256, [](std::uint8_t value) { return value != 0; }) &&
        std::ranges::any_of(
            workerSha256, [](std::uint8_t value) { return value != 0; });
}

bool ControllerIsolationFileSha256(
    const std::filesystem::path& path,
    std::array<std::uint8_t, 32>& digest,
    std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    digest = {};
    HANDLE file = CreateFileW(
        path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        nativeError = GetLastError();
        return false;
    }
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    bool ok = BCryptOpenAlgorithmProvider(
                  &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0;
    std::array<std::uint8_t, 64 * 1024> buffer{};
    while (ok) {
        DWORD read{};
        if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()),
                      &read, nullptr)) {
            nativeError = GetLastError();
            ok = false;
            break;
        }
        if (read == 0) break;
        ok = BCryptHashData(hash, buffer.data(), read, 0) == 0;
    }
    if (ok) ok = BCryptFinishHash(
        hash, digest.data(), static_cast<ULONG>(digest.size()), 0) == 0;
    if (!ok && nativeError == ERROR_SUCCESS) nativeError = ERROR_INVALID_DATA;
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    CloseHandle(file);
    return ok;
}

bool ControllerIsolationJournalStore::SaveAtomic(
    const ControllerIsolationJournalRecord& record,
    std::uint32_t& nativeError) const noexcept {
    nativeError = ERROR_SUCCESS;
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError))
        return false;
    if (!record.valid() || path_.empty()) {
        nativeError = ERROR_INVALID_DATA;
        return false;
    }
    try {
        const auto bytes = Serialize(record);
        if (bytes.empty() || bytes.size() > kMaximumBytes) {
            nativeError = ERROR_BUFFER_OVERFLOW;
            return false;
        }
        std::error_code directoryError;
        std::filesystem::create_directories(path_.parent_path(), directoryError);
        if (directoryError) {
            nativeError = ERROR_PATH_NOT_FOUND;
            return false;
        }
        auto temporary = path_;
        temporary += L".new";
        HANDLE file = CreateFileW(
            temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
            FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_NOT_CONTENT_INDEXED, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            nativeError = GetLastError();
            return false;
        }
        const bool written = WriteAll(file, bytes, nativeError);
        CloseHandle(file);
        if (!written || !MoveFileExW(
                temporary.c_str(), path_.c_str(),
                MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            if (written) nativeError = GetLastError();
            DeleteFileW(temporary.c_str());
            return false;
        }
        return true;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return false;
    }
}

bool ControllerIsolationJournalStore::Matches(
    const ControllerIsolationJournalRecord& expected,
    std::uint32_t& nativeError) const noexcept {
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError))
        return false;
    const auto current = Load(nativeError);
    if (!current) return false;
    try {
        const auto expectedBytes = Serialize(expected);
        const auto currentBytes = Serialize(*current);
        if (!expectedBytes.empty() && expectedBytes == currentBytes) return true;
        nativeError = ERROR_REVISION_MISMATCH;
        return false;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return false;
    }
}

bool ControllerIsolationJournalStore::SaveAtomicIfAbsent(
    const ControllerIsolationJournalRecord& record,
    std::uint32_t& nativeError) const noexcept {
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError))
        return false;
    std::uint32_t loadError{};
    if (Load(loadError)) {
        nativeError = ERROR_ALREADY_EXISTS;
        return false;
    }
    if (loadError != ERROR_FILE_NOT_FOUND && loadError != ERROR_PATH_NOT_FOUND) {
        nativeError = loadError;
        return false;
    }
    return SaveAtomic(record, nativeError);
}

bool ControllerIsolationJournalStore::SaveAtomicIfCurrent(
    const ControllerIsolationJournalRecord& expected,
    const ControllerIsolationJournalRecord& replacement,
    std::uint32_t& nativeError) const noexcept {
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError) ||
        !Matches(expected, nativeError)) return false;
    return SaveAtomic(replacement, nativeError);
}

std::optional<ControllerIsolationJournalRecord>
ControllerIsolationJournalStore::Load(std::uint32_t& nativeError) const noexcept {
    nativeError = ERROR_SUCCESS;
    HANDLE file = CreateFileW(
        path_.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        nativeError = GetLastError();
        return std::nullopt;
    }
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart < sizeof(Header) ||
        size.QuadPart > kMaximumBytes) {
        nativeError = ERROR_INVALID_DATA;
        CloseHandle(file);
        return std::nullopt;
    }
    std::vector<std::byte> bytes(static_cast<std::size_t>(size.QuadPart));
    DWORD read{};
    const bool loaded = ReadFile(
        file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr);
    CloseHandle(file);
    if (!loaded || read != bytes.size()) {
        nativeError = loaded ? ERROR_INVALID_DATA : GetLastError();
        return std::nullopt;
    }
    try {
        Header header;
        std::size_t cursor{};
        if (!Read(std::span<const std::byte>(bytes), cursor, header) ||
            header.magic != kMagic ||
            header.version != ControllerIsolationJournalRecord::Version ||
            header.bytes != bytes.size()) {
            nativeError = ERROR_INVALID_DATA;
            return std::nullopt;
        }
        const auto expectedHash = header.contentSha256;
        header.contentSha256 = {};
        std::memcpy(bytes.data(), &header, sizeof(header));
        std::array<std::uint8_t, 32> actualHash{};
        if (!HashBytes(bytes, actualHash) || actualHash != expectedHash) {
            nativeError = ERROR_CRC;
            return std::nullopt;
        }
        ControllerIsolationJournalRecord record;
        record.authority = header.authority;
        record.nonce = header.nonce;
        record.hostSha256 = header.hostSha256;
        record.guardianSha256 = header.guardianSha256;
        record.hostProcessId = header.hostProcessId;
        record.hostCreationTime = header.hostCreationTime;
        record.guardianProcessId = header.guardianProcessId;
        record.guardianCreationTime = header.guardianCreationTime;
        record.workerProcessId = header.workerProcessId;
        record.workerCreationTime = header.workerCreationTime;
        record.workerSha256 = header.workerSha256;
        record.phase = header.phase;
        record.policy.before.active = (header.flags & 1U) != 0;
        record.policy.before.applicationListInverted = (header.flags & 2U) != 0;
        record.policy.activatedBySession = (header.flags & 4U) != 0;
        if ((header.flags & ~7U) != 0 ||
            !ReadString(bytes, cursor, record.hostPath) ||
            !ReadString(bytes, cursor, record.guardianPath) ||
            !ReadString(bytes, cursor, record.workerPath) ||
            !ReadSet(bytes, cursor, header.beforeApplications,
                     record.policy.before.applicationPaths) ||
            !ReadSet(bytes, cursor, header.beforeDevices,
                     record.policy.before.deviceInstanceIds) ||
            !ReadSet(bytes, cursor, header.ownedApplications,
                     record.policy.ownedApplicationPaths) ||
            !ReadSet(bytes, cursor, header.ownedDevices,
                     record.policy.ownedDeviceInstanceIds) ||
            cursor != bytes.size() || !record.valid()) {
            nativeError = ERROR_INVALID_DATA;
            return std::nullopt;
        }
        return record;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return std::nullopt;
    }
}

bool ControllerIsolationJournalStore::Remove(
    std::uint32_t& nativeError) const noexcept {
    nativeError = ERROR_SUCCESS;
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError))
        return false;
    if (DeleteFileW(path_.c_str()) || GetLastError() == ERROR_FILE_NOT_FOUND)
        return true;
    nativeError = GetLastError();
    return false;
}

bool ControllerIsolationJournalStore::RemoveIfCurrent(
    const ControllerIsolationJournalRecord& expected,
    std::uint32_t& nativeError) const noexcept {
    ControllerIsolationLifecycleLease lease;
    if (!lease.Acquire(
            ControllerIsolationCommandTimeoutMilliseconds, nativeError) ||
        !Matches(expected, nativeError)) return false;
    return Remove(nativeError);
}

} // namespace widgetrail::isolation
