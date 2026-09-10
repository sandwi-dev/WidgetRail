#include "HidHideConfigurationAdapter.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <cwctype>
#include <limits>
#include <set>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr wchar_t kControlDevice[] = L"\\\\.\\HidHide";
constexpr DWORD kGetApplications = 0x80016000;
constexpr DWORD kSetApplications = 0x80016004;
constexpr DWORD kGetDevices = 0x80016008;
constexpr DWORD kSetDevices = 0x8001600C;
constexpr DWORD kGetActive = 0x80016010;
constexpr DWORD kSetActive = 0x80016014;
constexpr DWORD kGetInverse = 0x80016018;
constexpr std::size_t kMaximumMultiStringBytes = 64 * 1024;
constexpr std::size_t kMaximumEntries = 512;

class UniqueHandle final {
public:
    explicit UniqueHandle(HANDLE value) noexcept : value_(value) {}
    ~UniqueHandle() { Close(); }
    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;
    [[nodiscard]] HANDLE get() const noexcept { return value_; }
    [[nodiscard]] explicit operator bool() const noexcept {
        return value_ != INVALID_HANDLE_VALUE;
    }
    void Close() noexcept {
        if (value_ != INVALID_HANDLE_VALUE) CloseHandle(value_);
        value_ = INVALID_HANDLE_VALUE;
    }
private:
    HANDLE value_{INVALID_HANDLE_VALUE};
};

[[nodiscard]] UniqueHandle OpenControl(std::uint32_t& error) noexcept {
    const auto handle = CreateFileW(
        kControlDevice, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) error = GetLastError();
    return UniqueHandle(handle);
}

[[nodiscard]] bool ReadFlag(
    const HANDLE handle, const DWORD code, bool& value,
    std::uint32_t& error) noexcept {
    BYTE result{};
    DWORD returned{};
    if (!DeviceIoControl(
            handle, code, nullptr, 0, &result, sizeof(result), &returned,
            nullptr) || returned != sizeof(result)) {
        error = GetLastError();
        return false;
    }
    value = result != 0;
    return true;
}

[[nodiscard]] bool ReadMultiString(
    const HANDLE handle, const DWORD code, std::set<std::wstring>& values,
    std::uint32_t& error) noexcept {
    DWORD needed{};
    const auto probe = DeviceIoControl(
        handle, code, nullptr, 0, nullptr, 0, &needed, nullptr);
    if (!probe && needed == 0) {
        error = GetLastError();
        return false;
    }
    if (needed < sizeof(wchar_t) ||
        needed > kMaximumMultiStringBytes ||
        needed % sizeof(wchar_t) != 0) {
        error = ERROR_INVALID_DATA;
        return false;
    }
    std::vector<wchar_t> buffer(needed / sizeof(wchar_t));
    DWORD returned{};
    if (!DeviceIoControl(
            handle, code, nullptr, 0, buffer.data(), needed, &returned,
            nullptr) || returned != needed || buffer.back() != L'\0') {
        error = GetLastError();
        if (error == ERROR_SUCCESS) error = ERROR_INVALID_DATA;
        return false;
    }
    if (!ParseHidHideMultiString(buffer, values)) {
        error = ERROR_INVALID_DATA;
        return false;
    }
    return true;
}

[[nodiscard]] bool WriteFlag(
    const HANDLE handle, const DWORD code, const bool value,
    std::uint32_t& error) noexcept {
    const BYTE input = value ? 1 : 0;
    DWORD returned{};
    if (!DeviceIoControl(
            handle, code, const_cast<BYTE*>(&input), sizeof(input), nullptr, 0,
            &returned, nullptr)) {
        error = GetLastError();
        return false;
    }
    return true;
}

[[nodiscard]] bool WriteMultiString(
    const HANDLE handle, const DWORD code,
    const std::set<std::wstring>& values, std::uint32_t& error) noexcept {
    if (values.size() > kMaximumEntries) {
        error = ERROR_BUFFER_OVERFLOW;
        return false;
    }
    std::vector<wchar_t> buffer;
    buffer.reserve(2);
    for (const auto& value : values) {
        if (value.empty() || value.find(L'\0') != std::wstring::npos ||
            value.size() >= kMaximumMultiStringBytes / sizeof(wchar_t)) {
            error = ERROR_INVALID_DATA;
            return false;
        }
        buffer.insert(buffer.end(), value.begin(), value.end());
        buffer.push_back(L'\0');
    }
    buffer.push_back(L'\0');
    if (values.empty()) buffer.push_back(L'\0');
    const auto bytes = buffer.size() * sizeof(wchar_t);
    if (bytes > kMaximumMultiStringBytes ||
        bytes > std::numeric_limits<DWORD>::max()) {
        error = ERROR_BUFFER_OVERFLOW;
        return false;
    }
    DWORD returned{};
    if (!DeviceIoControl(
            handle, code, buffer.data(), static_cast<DWORD>(bytes), nullptr, 0,
            &returned, nullptr)) {
        error = GetLastError();
        return false;
    }
    return true;
}

[[nodiscard]] HidHideConfigurationStatus ReadSnapshotFromHandle(
    const HANDLE handle, HidHideSnapshot& snapshot,
    std::uint32_t& nativeError) noexcept {
    HidHideSnapshot read;
    if (!ReadMultiString(
            handle, kGetApplications, read.applicationPaths, nativeError) ||
        !ReadMultiString(
            handle, kGetDevices, read.deviceInstanceIds, nativeError) ||
        !ReadFlag(handle, kGetActive, read.active, nativeError) ||
        !ReadFlag(
            handle, kGetInverse, read.applicationListInverted,
            nativeError)) {
        return HidHideConfigurationStatus::ReadFailed;
    }
    snapshot = std::move(read);
    return snapshot.applicationListInverted
        ? HidHideConfigurationStatus::UnsupportedInversePolicy
        : HidHideConfigurationStatus::Ready;
}

bool TransactionRead(
    void* context, HidHideSnapshot& snapshot,
    std::uint32_t& error) noexcept {
    return ReadSnapshotFromHandle(
        static_cast<HANDLE>(context), snapshot, error) ==
        HidHideConfigurationStatus::Ready;
}

bool TransactionWriteApplications(
    void* context, const std::set<std::wstring>& values,
    std::uint32_t& error) noexcept {
    return WriteMultiString(
        static_cast<HANDLE>(context), kSetApplications, values, error);
}

bool TransactionWriteDevices(
    void* context, const std::set<std::wstring>& values,
    std::uint32_t& error) noexcept {
    return WriteMultiString(
        static_cast<HANDLE>(context), kSetDevices, values, error);
}

bool TransactionWriteActive(
    void* context, const bool active, std::uint32_t& error) noexcept {
    return WriteFlag(static_cast<HANDLE>(context), kSetActive, active, error);
}

} // namespace

bool ParseHidHideMultiString(
    const std::span<const wchar_t> characters,
    std::set<std::wstring>& values) noexcept {
    if (characters.empty() ||
        characters.size() > kMaximumMultiStringBytes / sizeof(wchar_t))
        return false;
    values.clear();
    std::size_t cursor{};
    while (cursor < characters.size() && characters[cursor] != L'\0') {
        const auto end = std::find(
            characters.begin() + static_cast<std::ptrdiff_t>(cursor),
            characters.end(), L'\0');
        if (end == characters.end() || values.size() == kMaximumEntries)
            return false;
        std::wstring value(
            characters.begin() + static_cast<std::ptrdiff_t>(cursor), end);
        if (value.empty() || !values.insert(std::move(value)).second)
            return false;
        cursor = static_cast<std::size_t>(end - characters.begin()) + 1;
    }
    return cursor < characters.size() &&
        std::ranges::all_of(
            characters.begin() + static_cast<std::ptrdiff_t>(cursor),
            characters.end(), [](const wchar_t value) { return value == L'\0'; });
}

bool ControllerIsolationDosDevicePath(
    const std::wstring& absolutePath,
    std::wstring& devicePath,
    std::uint32_t& nativeError) noexcept {
    devicePath.clear();
    nativeError = ERROR_SUCCESS;
    if (absolutePath.size() < 3 || absolutePath[1] != L':' ||
        (absolutePath[2] != L'\\' && absolutePath[2] != L'/')) {
        nativeError = ERROR_NOT_SUPPORTED;
        return false;
    }
    wchar_t drive[]{absolutePath[0], L':', L'\0'};
    std::array<wchar_t, 32'768> target{};
    const auto length = QueryDosDeviceW(
        drive, target.data(), static_cast<DWORD>(target.size()));
    if (length == 0 || target[0] == L'\0') {
        nativeError = GetLastError();
        return false;
    }
    try {
        devicePath.assign(target.data());
        devicePath.append(absolutePath.substr(2));
        std::ranges::transform(devicePath, devicePath.begin(), [](wchar_t value) {
            return static_cast<wchar_t>(std::towupper(value));
        });
        return true;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return false;
    }
}

HidHideConfigurationStatus HidHideConfigurationAdapter::ReadSnapshot(
    HidHideSnapshot& snapshot, std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    auto handle = OpenControl(nativeError);
    if (!handle) return HidHideConfigurationStatus::DriverUnavailable;
    return ReadSnapshotFromHandle(handle.get(), snapshot, nativeError);
}

HidHideConfigurationStatus ApplyHidHideSnapshotTransaction(
    const HidHideSnapshotTransaction& transaction,
    const HidHideSnapshot& expectedBefore,
    const HidHideSnapshot& desired,
    HidHideSnapshot& observedAfter,
    std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    observedAfter = {};
    if (desired.applicationListInverted)
        return HidHideConfigurationStatus::UnsupportedInversePolicy;
    if (!transaction.complete())
        return HidHideConfigurationStatus::InvalidPayload;
    if (!transaction.Read(
            transaction.context, observedAfter, nativeError))
        return HidHideConfigurationStatus::ReadFailed;
    if (observedAfter != expectedBefore)
        return HidHideConfigurationStatus::ConcurrentPolicyDrift;
    // Keep global activation last on apply. Restoration callers likewise pass
    // a complete desired state. The same exclusive handle owns compare,
    // writes, and exact readback so another policy owner cannot be overwritten.
    if (!transaction.WriteApplications(
            transaction.context, desired.applicationPaths, nativeError) ||
        !transaction.WriteDevices(
            transaction.context, desired.deviceInstanceIds, nativeError) ||
        !transaction.WriteActive(
            transaction.context, desired.active, nativeError)) {
        const auto writeError = nativeError;
        const auto reread = transaction.Read(
            transaction.context, observedAfter, nativeError);
        nativeError = writeError;
        return reread && observedAfter == expectedBefore
            ? HidHideConfigurationStatus::WriteFailed
            : HidHideConfigurationStatus::PartialMutation;
    }
    if (!transaction.Read(
            transaction.context, observedAfter, nativeError) ||
        observedAfter != desired)
        return HidHideConfigurationStatus::ReadbackMismatch;
    return HidHideConfigurationStatus::Ready;
}

HidHideConfigurationStatus HidHideConfigurationAdapter::ApplySnapshot(
    const HidHideSnapshot& expectedBefore,
    const HidHideSnapshot& desired,
    HidHideSnapshot& observedAfter,
    std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    auto handle = OpenControl(nativeError);
    if (!handle) return HidHideConfigurationStatus::DriverUnavailable;
    const HidHideSnapshotTransaction transaction{
        handle.get(), TransactionRead, TransactionWriteApplications,
        TransactionWriteDevices, TransactionWriteActive};
    return ApplyHidHideSnapshotTransaction(
        transaction, expectedBefore, desired, observedAfter, nativeError);
}

} // namespace widgetrail::isolation
