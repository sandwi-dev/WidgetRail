#include "LocalControllerPolicy.h"
#include <shlobj.h>
#include <array>
#include <cstring>
#include <limits>

namespace widgetrail::isolation {
namespace {
constexpr std::size_t MaximumBytes = 128 * 1024;
constexpr std::size_t MaximumEntries = 512;
constexpr std::string_view Magic = "WidgetRailControllerIsolationLocal=1\n";
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
[[nodiscard]] std::wstring LocalOwnerMutexName() {
    return L"Local\\WidgetRail.ControllerIsolation.LocalOwner.Tests." +
        std::to_wstring(GetCurrentProcessId());
}
#endif
class File final {
public:
    explicit File(HANDLE value) noexcept : value_(value) {}
    ~File() { if (value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); }
    HANDLE get() const noexcept { return value_; }
    bool valid() const noexcept { return value_ != INVALID_HANDLE_VALUE; }
private:
    HANDLE value_;
};
bool Fail(std::wstring& diagnostic, const wchar_t* stage, DWORD error = ERROR_INVALID_DATA) {
    diagnostic = std::wstring(L"Controller isolation ") + stage + L" error=" + std::to_wstring(error);
    return false;
}
bool ReadRecord(HANDLE file, std::string& bytes, std::wstring& diagnostic) {
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size)) return Fail(diagnostic, L"journal size", GetLastError());
    if (size.QuadPart <= 0 || size.QuadPart > MaximumBytes) return Fail(diagnostic, L"journal size");
    bytes.resize(static_cast<std::size_t>(size.QuadPart));
    DWORD count{};
    if (!ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr))
        return Fail(diagnostic, L"journal read", GetLastError());
    return count == bytes.size() || Fail(diagnostic, L"journal truncated");
}
std::string Hex(const std::wstring& text) {
    constexpr char digits[] = "0123456789abcdef";
    if (text.empty() || text.size() > 32'768 || text.find(L'\0') != std::wstring::npos)
        return {};
    std::string result;
    for (const auto character : text) {
        const auto value = static_cast<std::uint16_t>(character);
        for (unsigned byte : std::array<unsigned, 2>{value & 255U, static_cast<unsigned>(value >> 8)}) {
            result += digits[byte >> 4]; result += digits[byte & 15];
        }
    }
    return result;
}
bool Unhex(std::string_view text, std::wstring& result) {
    if (text.empty() || text.size() % 4 || text.size() > 32'768 * 4) return false;
    const auto digit = [](char c) -> int {
        return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
    };
    result.clear();
    for (std::size_t i = 0; i < text.size(); i += 4) {
        const int a = digit(text[i]), b = digit(text[i+1]), c = digit(text[i+2]), d = digit(text[i+3]);
        if (a < 0 || b < 0 || c < 0 || d < 0) return false;
        const auto value = static_cast<wchar_t>((a * 16 + b) | ((c * 16 + d) << 8));
        if (!value) return false;
        result += value;
    }
    return true;
}
bool Parse(LocalPolicyRecord& record, std::wstring& diagnostic) {
    const auto& text = record.encoded;
    if (!text.starts_with(Magic)) return Fail(diagnostic, L"journal schema");
    record.policy = {};
    auto& policy = record.policy;
    unsigned booleanFields{};
    std::size_t entries{};
    for (std::size_t cursor = Magic.size(); cursor < text.size();) {
        const auto end = text.find('\n', cursor);
        if (end == std::string::npos || ++entries > MaximumEntries) return Fail(diagnostic, L"journal bounds");
        const std::string_view line(text.data() + cursor, end - cursor);
        cursor = end + 1;
        const auto equal = line.find('=');
        if (equal == std::string_view::npos) return Fail(diagnostic, L"journal field");
        const auto key = line.substr(0, equal), value = line.substr(equal + 1);
        unsigned bit{}; bool* flag{};
        if (key == "before-active") { bit = 1; flag = &policy.before.active; }
        if (key == "before-inverse") { bit = 2; flag = &policy.before.applicationListInverted; }
        if (key == "activated") { bit = 4; flag = &policy.activatedBySession; }
        if (flag) {
            if ((booleanFields & bit) || (value != "0" && value != "1")) return Fail(diagnostic, L"journal duplicate/invalid flag");
            booleanFields |= bit; *flag = value == "1"; continue;
        }
        std::set<std::wstring>* values{};
        if (key == "before-app") values = &policy.before.applicationPaths;
        if (key == "before-device") values = &policy.before.deviceInstanceIds;
        if (key == "owned-app") values = &policy.ownedApplicationPaths;
        if (key == "owned-device") values = &policy.ownedDeviceInstanceIds;
        std::wstring decoded;
        if (!values || !Unhex(value, decoded) || !values->insert(decoded).second)
            return Fail(diagnostic, L"journal duplicate/invalid entry");
    }
    if (booleanFields != 7 || policy.before.applicationListInverted ||
        policy.activatedBySession == policy.before.active)
        return Fail(diagnostic, L"journal policy flags");
    for (const auto& app : policy.ownedApplicationPaths)
        if (policy.before.applicationPaths.contains(app)) return Fail(diagnostic, L"journal application ownership");
    for (const auto& device : policy.ownedDeviceInstanceIds)
        if (policy.before.deviceInstanceIds.contains(device)) return Fail(diagnostic, L"journal device ownership");
    return true;
}
} // namespace

LocalOwnerLease::~LocalOwnerLease() {
    if (owned_) ReleaseMutex(mutex_);
    if (mutex_) CloseHandle(mutex_);
}
bool LocalOwnerLease::Acquire(std::wstring& diagnostic, DWORD timeout) {
    if (owned_) return true;
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
    const auto mutexName = LocalOwnerMutexName();
    mutex_ = CreateMutexW(nullptr, FALSE, mutexName.c_str());
#else
    mutex_ = CreateMutexW(nullptr, FALSE, L"Local\\WidgetRail.ControllerIsolation.LocalOwner");
#endif
    if (!mutex_) return Fail(diagnostic, L"owner mutex", GetLastError());
    const auto wait = WaitForSingleObject(mutex_, timeout);
    if (wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED) { owned_ = true; return true; }
    return Fail(diagnostic, L"owner busy", wait == WAIT_TIMEOUT ? ERROR_BUSY : GetLastError());
}
bool NativeLocalPolicyEffects::Read(HidHideSnapshot& state, std::uint32_t& error) {
    return adapter_.ReadSnapshot(state, error) == HidHideConfigurationStatus::Ready;
}
bool NativeLocalPolicyEffects::Apply(const HidHideSnapshot& expected, const HidHideSnapshot& desired,
    HidHideSnapshot& observed, std::uint32_t& error) {
    return adapter_.ApplySnapshot(expected, desired, observed, error) == HidHideConfigurationStatus::Ready;
}
std::filesystem::path LocalControllerJournalPath() {
    PWSTR local{};
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local))) return {};
    std::filesystem::path path;
    try { path = local; } catch (...) { CoTaskMemFree(local); throw; }
    CoTaskMemFree(local);
    return path / L"WidgetRail" / L"controller-isolation" / L"local-session.v1";
}
bool LoadLocalPolicy(const std::filesystem::path& path, LocalPolicyRecord& record,
    bool& found, std::wstring& diagnostic) {
    found = false;
    File file(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                          FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    if (!file.valid()) {
        const auto error = GetLastError();
        return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND || Fail(diagnostic, L"journal open", error);
    }
    found = true;
    return ReadRecord(file.get(), record.encoded, diagnostic) && Parse(record, diagnostic);
}
bool SaveLocalPolicy(const std::filesystem::path& path, const HidHideJournal& policy,
    LocalPolicyRecord& record, std::wstring& diagnostic) {
    record.encoded = std::string(Magic) + "before-active=" + (policy.before.active ? "1\n" : "0\n") +
        "before-inverse=" + (policy.before.applicationListInverted ? "1\n" : "0\n") +
        "activated=" + (policy.activatedBySession ? "1\n" : "0\n");
    const auto add = [&](const char* prefix, const std::set<std::wstring>& entries) {
        for (const auto& entry : entries) record.encoded += std::string(prefix) + Hex(entry) + "\n";
    };
    add("before-app=", policy.before.applicationPaths); add("before-device=", policy.before.deviceInstanceIds);
    add("owned-app=", policy.ownedApplicationPaths); add("owned-device=", policy.ownedDeviceInstanceIds);
    if (record.encoded.size() > MaximumBytes || !Parse(record, diagnostic)) return Fail(diagnostic, L"journal serialization");
    std::filesystem::create_directories(path.parent_path());
    const auto temporary = path.wstring() + L".tmp";
    {
        File file(CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr));
        if (!file.valid()) return Fail(diagnostic, L"journal create", GetLastError());
        DWORD count{};
        if (!WriteFile(file.get(), record.encoded.data(), static_cast<DWORD>(record.encoded.size()), &count, nullptr) ||
            count != record.encoded.size() || !FlushFileBuffers(file.get())) return Fail(diagnostic, L"journal write", GetLastError());
    }
    // Never replace an existing recovery record, even while holding the owner mutex.
    return MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_WRITE_THROUGH) || Fail(diagnostic, L"journal publish", GetLastError());
}
bool RestoreLocalPolicy(const std::filesystem::path& path, const LocalPolicyRecord& expected,
    LocalPolicyEffects& effects, std::wstring& diagnostic) {
    File file(CreateFileW(path.c_str(), GENERIC_READ | DELETE, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                          FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    if (!file.valid()) return Fail(diagnostic, L"recovery record open", GetLastError());
    LocalPolicyRecord currentRecord;
    if (!ReadRecord(file.get(), currentRecord.encoded, diagnostic) ||
        currentRecord.encoded != expected.encoded || !Parse(currentRecord, diagnostic))
        return Fail(diagnostic, L"recovery record changed");
    HidHideSnapshot current, observed;
    std::uint32_t error{};
    if (!effects.Read(current, error)) return Fail(diagnostic, L"recovery policy read", error);
    const auto plan = PlanHidHideRecovery(currentRecord.policy, current);
    if (!plan.desired || plan.status == HidHideRestoreStatus::Conflict) return Fail(diagnostic, L"recovery foreign policy drift");
    if (!effects.Apply(current, *plan.desired, observed, error) || observed != *plan.desired)
        return Fail(diagnostic, L"recovery policy restore", error);
    FILE_DISPOSITION_INFO disposition{TRUE};
    return SetFileInformationByHandle(file.get(), FileDispositionInfo, &disposition, sizeof(disposition)) ||
        Fail(diagnostic, L"recovery record removal", GetLastError());
}
bool RecoverLocalControllerPolicy(const std::filesystem::path& path, LocalPolicyEffects& effects,
    std::wstring& diagnostic) {
    LocalOwnerLease lease;
    if (!lease.Acquire(diagnostic)) return false;
    if (path.empty()) return Fail(diagnostic, L"journal path unavailable");
    if (std::filesystem::exists(path.parent_path() / L"session.bin")) return Fail(diagnostic, L"legacy journal requires original recovery");
    LocalPolicyRecord record; bool found{};
    if (!LoadLocalPolicy(path, record, found, diagnostic)) return false;
    if (found && !RestoreLocalPolicy(path, record, effects, diagnostic)) return false;
    diagnostic = found ? L"Controller isolation owned policy restored." : L"No local controller isolation recovery is pending.";
    return true;
}
LocalPolicyCleanup::LocalPolicyCleanup(const std::filesystem::path& path, const LocalPolicyRecord& record,
    LocalPolicyEffects& effects, std::wstring& diagnostic) noexcept
    : path_(path), record_(record), effects_(effects), diagnostic_(diagnostic) {}
LocalPolicyCleanup::~LocalPolicyCleanup() noexcept { if (armed_) (void)Finish(); }
bool LocalPolicyCleanup::Finish() noexcept {
    if (!armed_) return false;
    armed_ = false;
    try { return RestoreLocalPolicy(path_, record_, effects_, diagnostic_); }
    catch (...) { try { diagnostic_ = L"Controller isolation recovery exception; journal retained."; } catch (...) {} return false; }
}
} // namespace widgetrail::isolation
