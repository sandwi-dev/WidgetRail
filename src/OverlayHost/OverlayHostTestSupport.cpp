#include "OverlayHostTestSupport.h"

#include <objbase.h>

#include <algorithm>
#include <array>
#include <cstdlib>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <stdexcept>

namespace widgetrail::host_testing {

void Handle::Reset(HANDLE value) noexcept {
    if (*this) CloseHandle(value_);
    value_ = value;
}

[[noreturn]] void Fail(const std::string& message) {
    throw std::runtime_error(message);
}

void Require(const bool condition, const std::string& message) {
    if (!condition) Fail(message);
}

std::string Win32Error(const std::string_view operation) {
    return std::string(operation) + " failed with Win32 error " +
        std::to_string(GetLastError());
}

std::wstring QuoteArgument(const std::wstring_view argument) {
    if (argument.find_first_of(L" \t\"") == std::wstring_view::npos)
        return std::wstring(argument);
    std::wstring result{L'\"'};
    std::size_t slashes{};
    for (const wchar_t character : argument) {
        if (character == L'\\') {
            ++slashes;
            continue;
        }
        if (character == L'\"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(L'\"');
        } else {
            result.append(slashes, L'\\');
            result.push_back(character);
        }
        slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'\"');
    return result;
}

std::string WideToUtf8(const std::wstring_view value) {
    if (value.empty()) return {};
    const int size = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
        nullptr, 0, nullptr, nullptr);
    Require(size > 0, Win32Error("WideCharToMultiByte(size)"));
    std::string result(static_cast<std::size_t>(size), '\0');
    Require(WideCharToMultiByte(
                CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
                static_cast<int>(value.size()), result.data(), size, nullptr, nullptr) == size,
            Win32Error("WideCharToMultiByte"));
    return result;
}

std::string JsonEscape(const std::wstring_view value) {
    const std::string utf8 = WideToUtf8(value);
    std::string result;
    result.reserve(utf8.size() + 8);
    for (const unsigned char character : utf8) {
        switch (character) {
        case '\"': result += "\\\""; break;
        case '\\': result += "\\\\"; break;
        case '\b': result += "\\b"; break;
        case '\f': result += "\\f"; break;
        case '\n': result += "\\n"; break;
        case '\r': result += "\\r"; break;
        case '\t': result += "\\t"; break;
        default:
            if (character < 0x20)
                Fail("Value contains an unsupported JSON control character.");
            result.push_back(static_cast<char>(character));
            break;
        }
    }
    return result;
}

void WriteUtf8(
    const std::filesystem::path& path,
    const std::string_view contents) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    Require(static_cast<bool>(output), "Could not create " + path.string());
    output.write(contents.data(), static_cast<std::streamsize>(contents.size()));
    Require(static_cast<bool>(output), "Could not write " + path.string());
}

std::string ReadUtf8(const std::filesystem::path& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return {};
    return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}

namespace {

constexpr std::array<std::wstring_view, 1> kNativeRuntimeDependencies{{
    L"OverlayPlatformInterop.dll",
}};

class TemporaryDirectory final {
public:
    explicit TemporaryDirectory(const std::wstring_view prefix) {
        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        path_ = std::filesystem::path(temporaryRoot) /
            (std::wstring(prefix) + std::wstring(guidText));
        std::filesystem::create_directories(path_);
    }

    ~TemporaryDirectory() {
        std::error_code ignored;
        std::filesystem::remove_all(path_, ignored);
    }

    [[nodiscard]] const std::filesystem::path& Path() const noexcept {
        return path_;
    }

private:
    std::filesystem::path path_;
};

} // namespace

void ValidateNativeRuntimeDependencies(const std::filesystem::path& source) {
    for (const auto dependency : kNativeRuntimeDependencies) {
        Require(std::filesystem::is_regular_file(source / dependency),
                "--installation is missing required native dependency '" +
                    WideToUtf8(dependency) + "'");
    }
}

void CopyNativeRuntimeDependencies(
    const std::filesystem::path& source,
    const std::filesystem::path& destination) {
    ValidateNativeRuntimeDependencies(source);
    for (const auto dependency : kNativeRuntimeDependencies)
        std::filesystem::copy_file(source / dependency, destination / dependency);
}

void VerifyNativeRuntimeDependencyPolicy(
    const std::filesystem::path& installation) {
    TemporaryDirectory copied(L"wrail-native-dependency-copy-");
    CopyNativeRuntimeDependencies(installation, copied.Path());
    for (const auto dependency : kNativeRuntimeDependencies) {
        Require(std::filesystem::is_regular_file(copied.Path() / dependency),
                "Native dependency copy regression omitted " +
                    WideToUtf8(dependency));
        Require(std::filesystem::file_size(copied.Path() / dependency) ==
                    std::filesystem::file_size(installation / dependency),
                "Native dependency copy regression changed " +
                    WideToUtf8(dependency));
    }

    TemporaryDirectory omitted(L"wrail-native-dependency-omission-");
    bool rejected{};
    try {
        CopyNativeRuntimeDependencies(omitted.Path(), copied.Path());
    } catch (const std::exception& error) {
        rejected = std::string_view(error.what()) ==
            "--installation is missing required native dependency "
            "'OverlayPlatformInterop.dll'";
    }
    Require(rejected,
            "Missing native dependency did not fail before host launch with the "
            "precise OverlayPlatformInterop.dll diagnostic");
}

bool PathContainsDirectory(const std::filesystem::path& directory) {
    const DWORD required = GetEnvironmentVariableW(L"PATH", nullptr, 0);
    if (required == 0) return false;
    std::wstring value(required, L'\0');
    Require(GetEnvironmentVariableW(L"PATH", value.data(), required) > 0,
            Win32Error("GetEnvironmentVariableW(PATH)"));
    value.resize(wcslen(value.c_str()));
    const auto expected =
        std::filesystem::absolute(directory).lexically_normal().wstring();
    std::size_t cursor{};
    while (cursor <= value.size()) {
        const auto end = value.find(L';', cursor);
        std::wstring entry = value.substr(
            cursor,
            end == std::wstring::npos ? std::wstring::npos : end - cursor);
        if (entry.size() >= 2 && entry.front() == L'"' && entry.back() == L'"')
            entry = entry.substr(1, entry.size() - 2);
        if (!entry.empty()) {
            std::error_code ignored;
            const auto normalized =
                std::filesystem::absolute(entry, ignored).lexically_normal().wstring();
            if (!ignored && _wcsicmp(normalized.c_str(), expected.c_str()) == 0)
                return true;
        }
        if (end == std::wstring::npos) break;
        cursor = end + 1;
    }
    return false;
}

std::vector<wchar_t> ChildEnvironment(
    const std::filesystem::path& localAppData) {
    LPWCH environment = GetEnvironmentStringsW();
    Require(environment != nullptr, Win32Error("GetEnvironmentStringsW"));
    std::vector<std::wstring> entries;
    for (const wchar_t* current = environment; *current;) {
        std::wstring entry(current);
        current += entry.size() + 1;
        if (entry.rfind(L"LOCALAPPDATA=", 0) != 0 &&
            entry.rfind(L"LocalAppData=", 0) != 0)
            entries.push_back(std::move(entry));
    }
    FreeEnvironmentStringsW(environment);
    entries.push_back(L"LOCALAPPDATA=" + localAppData.wstring());
    std::sort(entries.begin(), entries.end(), [](const auto& left, const auto& right) {
        return _wcsicmp(left.c_str(), right.c_str()) < 0;
    });
    std::vector<wchar_t> block;
    for (const auto& entry : entries) {
        block.insert(block.end(), entry.begin(), entry.end());
        block.push_back(L'\0');
    }
    block.push_back(L'\0');
    return block;
}

HostProcess::HostProcess(
    const std::filesystem::path& installation,
    const std::filesystem::path& localAppData,
    const std::wstring_view arguments) {
    const auto executable = installation / L"OverlayHost.exe";
    std::wstring command = QuoteArgument(executable.wstring());
    if (!arguments.empty()) command += L" " + std::wstring(arguments);
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    auto environment = ChildEnvironment(localAppData);

    job_.Reset(CreateJobObjectW(nullptr, nullptr));
    Require(static_cast<bool>(job_), Win32Error("CreateJobObjectW"));
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    Require(SetInformationJobObject(
                job_.Get(), JobObjectExtendedLimitInformation, &limits, sizeof(limits)),
            Win32Error("SetInformationJobObject"));

    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    Require(CreateProcessW(
                executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
                CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED, environment.data(),
                installation.c_str(), &startup, &process),
            Win32Error("CreateProcessW(OverlayHost)"));
    process_.Reset(process.hProcess);
    thread_.Reset(process.hThread);
    processId_ = process.dwProcessId;
    if (!AssignProcessToJobObject(job_.Get(), process_.Get())) {
        TerminateProcess(process_.Get(), EXIT_FAILURE);
        Fail(Win32Error("AssignProcessToJobObject"));
    }
    Require(ResumeThread(thread_.Get()) != static_cast<DWORD>(-1),
            Win32Error("ResumeThread"));
}

HostProcess::~HostProcess() {
    if (process_ && WaitForSingleObject(process_.Get(), 0) == WAIT_TIMEOUT) {
        TerminateJobObject(job_.Get(), EXIT_FAILURE);
        WaitForSingleObject(process_.Get(), 3000);
    }
}

namespace {

struct WindowSearch final {
    DWORD processId{};
    const wchar_t* windowClass{};
    HWND window{};
};

BOOL CALLBACK FindHostWindow(HWND window, LPARAM parameter) {
    auto& search = *reinterpret_cast<WindowSearch*>(parameter);
    DWORD processId{};
    GetWindowThreadProcessId(window, &processId);
    if (processId != search.processId) return TRUE;
    wchar_t className[128]{};
    if (GetClassNameW(window, className, 128) > 0 &&
        wcscmp(className, search.windowClass) == 0) {
        search.window = window;
        return FALSE;
    }
    return TRUE;
}

} // namespace

HWND LocateHostWindow(const DWORD processId, const wchar_t* windowClass) {
    WindowSearch search{processId, windowClass, nullptr};
    EnumWindows(FindHostWindow, reinterpret_cast<LPARAM>(&search));
    return search.window;
}

void PostKey(HWND window, const WPARAM virtualKey) {
    Require(PostMessageW(window, WM_KEYDOWN, virtualKey, 1),
            Win32Error("PostMessageW(WM_KEYDOWN)"));
    Require(PostMessageW(window, WM_KEYUP, virtualKey, 1 | (1LL << 30) | (1LL << 31)),
            Win32Error("PostMessageW(WM_KEYUP)"));
}

void SendKey(HWND window, const WPARAM virtualKey) {
    DWORD_PTR ignored{};
    Require(SendMessageTimeoutW(
                window,
                WM_KEYDOWN,
                virtualKey,
                1,
                SMTO_ABORTIFHUNG | SMTO_BLOCK,
                5000,
                &ignored) != 0,
            Win32Error("SendMessageTimeoutW(WM_KEYDOWN)"));
    Require(SendMessageTimeoutW(
                window,
                WM_KEYUP,
                virtualKey,
                1 | (1LL << 30) | (1LL << 31),
                SMTO_ABORTIFHUNG | SMTO_BLOCK,
                5000,
                &ignored) != 0,
            Win32Error("SendMessageTimeoutW(WM_KEYUP)"));
}

void SendKeyDownAndPostRelease(HWND window, const WPARAM virtualKey) {
    DWORD_PTR ignored{};
    Require(SendMessageTimeoutW(
                window,
                WM_KEYDOWN,
                virtualKey,
                1,
                SMTO_ABORTIFHUNG | SMTO_BLOCK,
                5000,
                &ignored) != 0,
            Win32Error("SendMessageTimeoutW(WM_KEYDOWN)"));
    Require(PostMessageW(
                window,
                WM_KEYUP,
                virtualKey,
                1 | (1LL << 30) | (1LL << 31)),
            Win32Error("PostMessageW(WM_KEYUP)"));
}

} // namespace widgetrail::host_testing
