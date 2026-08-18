#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <TlHelp32.h>
#include <wrl.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <iterator>
#include <optional>
#include <regex>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_set>
#include <utility>
#include <vector>

namespace {

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;
namespace fs = std::filesystem;

constexpr wchar_t kHostWindowClass[] = L"WidgetRail.OverlayHost";
constexpr wchar_t kWidgetId[] = L"ytmusic-fixture";
constexpr wchar_t kPlayPauseAutomationId[] = L"widget:play-pause";
constexpr wchar_t kOpenStatusAutomationId[] = L"host:host.open.status";
constexpr wchar_t kDashboardStatusAutomationId[] = L"host:host.dashboard.status";
constexpr wchar_t kTrayAutomationId[] = L"tray:tray.ytmusic-fixture";
constexpr wchar_t kExpectedStatus[] = L"YT Music action failed; try again";
constexpr char kSecretSentinel[] = "DLV014_SECRET_SENTINEL";
constexpr DWORD kPollMilliseconds = 25;
constexpr DWORD kStartupTimeoutMilliseconds = 15000;
constexpr DWORD kOperationTimeoutMilliseconds = 10000;

class Handle final {
public:
    Handle() = default;
    explicit Handle(HANDLE value) noexcept : value_(value) {}
    ~Handle() { Reset(); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : value_(std::exchange(other.value_, nullptr)) {}
    Handle& operator=(Handle&& other) noexcept {
        if (this != &other) Reset(std::exchange(other.value_, nullptr));
        return *this;
    }
    [[nodiscard]] HANDLE Get() const noexcept { return value_; }
    [[nodiscard]] explicit operator bool() const noexcept {
        return value_ && value_ != INVALID_HANDLE_VALUE;
    }
    void Reset(HANDLE value = nullptr) noexcept {
        if (*this) CloseHandle(value_);
        value_ = value;
    }

private:
    HANDLE value_{};
};

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

template <typename Predicate>
bool WaitUntil(const DWORD timeoutMilliseconds, Predicate&& predicate) {
    const ULONGLONG deadline = GetTickCount64() + timeoutMilliseconds;
    do {
        if (std::invoke(predicate)) return true;
        Sleep(kPollMilliseconds);
    } while (GetTickCount64() < deadline);
    return std::invoke(predicate);
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
            if (character < 0x20) Fail("Path contains an unsupported JSON control character.");
            result.push_back(static_cast<char>(character));
            break;
        }
    }
    return result;
}

void WriteUtf8(const fs::path& path, const std::string_view contents) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    Require(static_cast<bool>(output), "Could not create " + path.string());
    output.write(contents.data(), static_cast<std::streamsize>(contents.size()));
    Require(static_cast<bool>(output), "Could not write " + path.string());
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(const fs::path& source, const fs::path& fixtureWorker) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
                "--installation does not contain OverlayHost.exe");
        Require(fs::is_regular_file(source / L"widget-catalog.json"),
                "--installation does not contain widget-catalog.json");
        Require(fs::is_directory(source / L"runtime"),
                "--installation does not contain runtime");
        Require(fs::is_regular_file(fixtureWorker),
                "--fixture-worker does not name a file");
        widgetrail::host_testing::ValidateNativeRuntimeDependencies(source);

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD temporaryRootLength = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(temporaryRootLength > 0 && temporaryRootLength <= MAX_PATH,
                Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"wrail-action-failure-host-" + std::wstring(guidText));
        processProfile_ = L"action-failure-host-";
        for (const wchar_t character : std::wstring_view(guidText)) {
            if (std::iswalnum(character))
                processProfile_.push_back(
                    static_cast<wchar_t>(std::towlower(character)));
        }
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        widgetrail::host_testing::CopyNativeRuntimeDependencies(source, root_);
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_);

        const fs::path style = root_ / L"runtime" / L"action-failure-fixture.wrss";
        WriteUtf8(style,
                  ".ytmusic-fixture { padding: 16px; gap: 8px; }\n"
                  "button:focused { outline-width: 3px; }\n");

        const std::string catalog =
            "{\n"
            "  \"catalogVersion\": 1,\n"
            "  \"genericWorkerExecutable\": \"runtime/WidgetWorkerHost/WidgetWorkerHost.exe\",\n"
            "  \"widgets\": [{\n"
            "    \"id\": \"ytmusic-fixture\",\n"
            "    \"packageId\": \"widgetrail.tests.ytmusic-fixture\",\n"
            "    \"publisherId\": \"widgetrail.tests\",\n"
            "    \"name\": \"YT Music\",\n"
            "    \"instanceId\": \"ytmusic-fixture.default\",\n"
            "    \"icon\": \"music\",\n"
            "    \"workerExecutable\": \"" + JsonEscape(fs::absolute(fixtureWorker).wstring()) + "\",\n"
            "    \"styleFile\": \"runtime/action-failure-fixture.wrss\",\n"
            "    \"memoryLimitMb\": 64,\n"
            "    \"residencyPolicy\": { \"schemaVersion\": 1, \"mode\": \"keep-alive\" },\n"
            "    \"workerArguments\": [\"--fail-once\", \"" +
                JsonEscape((root_ / L"worker-failed-once.marker").wstring()) + "\"],\n"
            "    \"declaredCapabilities\": [],\n"
            "    \"quickActions\": []\n"
            "  }],\n"
            "  \"bundledWidgets\": []\n"
            "}\n";
        WriteUtf8(root_ / L"widget-catalog.json", catalog);
    }

    ~TemporaryInstallation() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    TemporaryInstallation(const TemporaryInstallation&) = delete;
    TemporaryInstallation& operator=(const TemporaryInstallation&) = delete;
    [[nodiscard]] const fs::path& Root() const noexcept { return root_; }
    [[nodiscard]] const fs::path& LocalAppData() const noexcept { return localAppData_; }
    [[nodiscard]] const std::wstring& ProcessProfile() const noexcept {
        return processProfile_;
    }

private:
    fs::path root_;
    fs::path localAppData_;
    std::wstring processProfile_;
};

std::vector<wchar_t> ChildEnvironment(const fs::path& localAppData) {
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

class HostProcess final {
public:
    HostProcess(const fs::path& installation, const fs::path& localAppData,
                const std::wstring_view processProfile) {
        const fs::path executable = installation / L"OverlayHost.exe";
        std::wstring command = QuoteArgument(executable.wstring()) +
            L" --show --process-profile " + std::wstring(processProfile);
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

    ~HostProcess() {
        if (process_ && WaitForSingleObject(process_.Get(), 0) == WAIT_TIMEOUT) {
            TerminateJobObject(job_.Get(), EXIT_FAILURE);
            WaitForSingleObject(process_.Get(), 3000);
        }
    }

    HostProcess(const HostProcess&) = delete;
    HostProcess& operator=(const HostProcess&) = delete;
    [[nodiscard]] DWORD Id() const noexcept { return processId_; }
    [[nodiscard]] HANDLE Process() const noexcept { return process_.Get(); }

private:
    Handle job_;
    Handle process_;
    Handle thread_;
    DWORD processId_{};
};

struct WindowSearch {
    DWORD processId{};
    HWND window{};
};

BOOL CALLBACK FindHostWindow(HWND window, LPARAM parameter) {
    auto& search = *reinterpret_cast<WindowSearch*>(parameter);
    DWORD processId{};
    GetWindowThreadProcessId(window, &processId);
    if (processId != search.processId) return TRUE;
    wchar_t className[128]{};
    if (GetClassNameW(window, className, 128) > 0 &&
        wcscmp(className, kHostWindowClass) == 0) {
        search.window = window;
        return FALSE;
    }
    return TRUE;
}

HWND LocateHostWindow(const DWORD processId) {
    WindowSearch search{processId, nullptr};
    EnumWindows(FindHostWindow, reinterpret_cast<LPARAM>(&search));
    return search.window;
}

class LiveRegionHandler final : public RuntimeClass<
    RuntimeClassFlags<ClassicCom>, IUIAutomationEventHandler> {
public:
    IFACEMETHODIMP HandleAutomationEvent(
        IUIAutomationElement*, const EVENTID eventId) noexcept override {
        if (eventId == UIA_LiveRegionChangedEventId) count_.fetch_add(1);
        return S_OK;
    }
    [[nodiscard]] int Count() const noexcept { return count_.load(); }

private:
    std::atomic<int> count_{};
};

std::optional<std::wstring> StringProperty(
    IUIAutomationElement* element, const PROPERTYID propertyId) {
    VARIANT value{};
    if (!element || FAILED(element->GetCurrentPropertyValue(propertyId, &value)))
        return std::nullopt;
    std::optional<std::wstring> result;
    if (V_VT(&value) == VT_BSTR && V_BSTR(&value))
        result.emplace(V_BSTR(&value), SysStringLen(V_BSTR(&value)));
    VariantClear(&value);
    return result;
}

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation, IUIAutomationElement* root,
    const std::wstring_view automationId) {
    VARIANT value{};
    V_VT(&value) = VT_BSTR;
    V_BSTR(&value) = SysAllocStringLen(
        automationId.data(), static_cast<UINT>(automationId.size()));
    ComPtr<IUIAutomationCondition> condition;
    if (!V_BSTR(&value) || FAILED(automation->CreatePropertyCondition(
            UIA_AutomationIdPropertyId, value, condition.GetAddressOf()))) {
        VariantClear(&value);
        return {};
    }
    VariantClear(&value);
    ComPtr<IUIAutomationElement> result;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), result.GetAddressOf())))
        return {};
    return result;
}

ComPtr<IUIAutomationElement> RootForWindow(IUIAutomation* automation, HWND window) {
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf()))) return {};
    return root;
}

bool IsFocused(IUIAutomationElement* element) {
    BOOL focused{};
    return element && SUCCEEDED(element->get_CurrentHasKeyboardFocus(&focused)) && focused;
}

bool HasExpectedStatus(
    IUIAutomation* automation, HWND window, const std::wstring_view automationId,
    const bool verifyProperties = true) {
    auto root = RootForWindow(automation, window);
    if (!root) return false;
    auto status = FindByAutomationId(automation, root.Get(), automationId);
    if (!status) return false;
    if (!verifyProperties) return true;
    const auto name = StringProperty(status.Get(), UIA_NamePropertyId);
    CONTROLTYPEID controlType{};
    VARIANT live{};
    const bool propertiesMatch = name && *name == kExpectedStatus &&
        SUCCEEDED(status->get_CurrentControlType(&controlType)) &&
        controlType == UIA_StatusBarControlTypeId &&
        SUCCEEDED(status->GetCurrentPropertyValue(UIA_LiveSettingPropertyId, &live)) &&
        V_VT(&live) == VT_I4 && V_I4(&live) == Polite;
    VariantClear(&live);
    return propertiesMatch;
}

std::string ReadLog(const fs::path& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return {};
    return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}

std::size_t MatchingFailureRecords(const std::string& log) {
    static const std::regex record(
        R"(Widget action failed: widget=ytmusic-fixture generation=([^\s]+) code=controllerActionFailed action=toggle-playback source=play-pause)");
    return static_cast<std::size_t>(
        std::distance(std::sregex_iterator(log.begin(), log.end(), record),
                      std::sregex_iterator()));
}

struct ProcessEntry {
    DWORD id{};
    DWORD parentId{};
};

std::vector<ProcessEntry> ProcessEntries() {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    if (!snapshot || snapshot.Get() == INVALID_HANDLE_VALUE) return {};
    PROCESSENTRY32W entry{sizeof(entry)};
    std::vector<ProcessEntry> result;
    if (!Process32FirstW(snapshot.Get(), &entry)) return result;
    do {
        result.push_back({entry.th32ProcessID, entry.th32ParentProcessID});
    } while (Process32NextW(snapshot.Get(), &entry));
    return result;
}

std::optional<fs::path> ProcessImage(const DWORD processId) {
    Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId));
    if (!process) return std::nullopt;
    std::wstring buffer(32768, L'\0');
    DWORD size = static_cast<DWORD>(buffer.size());
    if (!QueryFullProcessImageNameW(process.Get(), 0, buffer.data(), &size))
        return std::nullopt;
    buffer.resize(size);
    return fs::path(buffer);
}

std::wstring NormalizedPath(const fs::path& path) {
    std::error_code ignored;
    auto normalized = fs::weakly_canonical(path, ignored).wstring();
    if (ignored) normalized = fs::absolute(path, ignored).lexically_normal().wstring();
    std::transform(normalized.begin(), normalized.end(), normalized.begin(),
                   [](const wchar_t character) {
                       return static_cast<wchar_t>(std::towlower(character));
                   });
    return normalized;
}

std::vector<DWORD> FixtureDescendants(
    const DWORD hostProcessId, const fs::path& fixtureWorker) {
    const auto entries = ProcessEntries();
    std::unordered_set<DWORD> descendants{hostProcessId};
    bool changed{};
    do {
        changed = false;
        for (const auto& entry : entries) {
            if (!descendants.contains(entry.id) && descendants.contains(entry.parentId)) {
                descendants.insert(entry.id);
                changed = true;
            }
        }
    } while (changed);
    const auto expected = NormalizedPath(fixtureWorker);
    std::vector<DWORD> result;
    for (const DWORD processId : descendants) {
        if (processId == hostProcessId) continue;
        const auto image = ProcessImage(processId);
        if (image && NormalizedPath(*image) == expected) result.push_back(processId);
    }
    return result;
}

void PostKey(HWND window, const WPARAM virtualKey) {
    Require(PostMessageW(window, WM_KEYDOWN, virtualKey, 0), Win32Error("PostMessageW(WM_KEYDOWN)"));
}

struct Arguments {
    fs::path installation;
    fs::path fixtureWorker;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if ((argument == L"--installation" || argument == L"--fixture-worker") &&
            index + 1 < argc) {
            if (argument == L"--installation") result.installation = argv[++index];
            else result.fixtureWorker = argv[++index];
        } else {
            Fail("Usage: WidgetActionFailureHostTests --installation <dir> --fixture-worker <exe>");
        }
    }
    Require(!result.installation.empty() && !result.fixtureWorker.empty(),
            "Both --installation and --fixture-worker are required.");
    return result;
}

void Run(const Arguments& arguments) {
    Require(!widgetrail::host_testing::PathContainsDirectory(arguments.installation),
            "Fixture PATH must not contain the admitted installation directory");
    widgetrail::host_testing::VerifyNativeRuntimeDependencyPolicy(arguments.installation);
    TemporaryInstallation installation(arguments.installation, arguments.fixtureWorker);
    HostProcess host(
        installation.Root(), installation.LocalAppData(), installation.ProcessProfile());

    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                window = LocateHostWindow(host.Id());
                return window && IsWindowVisible(window);
            }), "Production OverlayHost did not create a visible HWND in time.");

    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
            "Windows UI Automation client is unavailable.");
    auto root = RootForWindow(automation.Get(), window);
    Require(root, "UI Automation could not acquire the production host root.");
    auto eventHandler = Make<LiveRegionHandler>();
    Require(eventHandler, "Could not create UI Automation event handler.");
    Require(SUCCEEDED(automation->AddAutomationEventHandler(
                UIA_LiveRegionChangedEventId, root.Get(), TreeScope_Subtree, nullptr,
                eventHandler.Get())),
            "Could not subscribe to production live-region events.");
    const auto removeEventHandler = [&] {
        (void)automation->RemoveAutomationEventHandler(
            UIA_LiveRegionChangedEventId, root.Get(), eventHandler.Get());
    };

    try {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    return currentRoot && FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kTrayAutomationId);
                }), "The fixture did not appear in the production dashboard.");
        const fs::path logPath = installation.LocalAppData() /
            L"WidgetRail" / L"overlay.log";
        const auto activationLogBoundary = ReadLog(logPath).size();
        PostKey(window, VK_RETURN);
        if (!WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadLog(logPath).find(
                    "YT Music failed: Widget worker", activationLogBoundary) !=
                    std::string::npos;
            })) {
            const auto log = ReadLog(logPath);
            const auto suffixStart = std::min(activationLogBoundary, log.size());
            const auto suffixLength = std::min<std::size_t>(
                8192, log.size() - suffixStart);
            Fail("The primary worker-start failure was not retained by the host. "
                 "Activation log suffix: " + log.substr(suffixStart, suffixLength));
        }
        const auto startupLog = ReadLog(logPath);
        Require(startupLog.find("hidden suspended widget has no cached snapshot") ==
                    std::string::npos,
                "The secondary missing-cache error replaced the primary startup failure.");
        PostKey(window, VK_RETURN);
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return FixtureDescendants(host.Id(), arguments.fixtureWorker).size() == 1 &&
                           [&] {
                               auto currentRoot = RootForWindow(automation.Get(), window);
                               return currentRoot && FindByAutomationId(
                                   automation.Get(), currentRoot.Get(),
                                   kPlayPauseAutomationId);
                           }();
                }), "A Retry did not recover with one fresh worker generation.");
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    return currentRoot && FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kPlayPauseAutomationId);
                }), "The real widget did not expose play-pause after opening.");

        Require(WaitUntil(3000, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    ComPtr<IUIAutomationElement> playPause;
                    if (currentRoot) playPause = FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kPlayPauseAutomationId);
                    return IsFocused(playPause.Get());
                }), "play-pause was not the initial UI Automation focus.");

        const ULONGLONG firstFailureAt = GetTickCount64();
        PostKey(window, VK_RETURN);
        if (!WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return HasExpectedStatus(
                    automation.Get(), window, kOpenStatusAutomationId);
            })) {
            auto currentRoot = RootForWindow(automation.Get(), window);
            auto status = currentRoot
                ? FindByAutomationId(
                    automation.Get(), currentRoot.Get(), kOpenStatusAutomationId)
                : ComPtr<IUIAutomationElement>{};
            const auto statusName = StringProperty(status.Get(), UIA_NamePropertyId);
            const auto log = ReadLog(logPath);
            const auto suffixStart = std::min(activationLogBoundary, log.size());
            const auto suffixLength = std::min<std::size_t>(
                8192, log.size() - suffixStart);
            Fail("The production host did not paint/project the exact polite status. "
                 "Projected status: " +
                 (statusName ? WideToUtf8(*statusName) : std::string{"<absent>"}) +
                 ". Activation log suffix: " +
                 log.substr(suffixStart, suffixLength));
        }
        Require(WaitUntil(3000, [&] { return eventHandler->Count() >= 1; }),
                "The production UI Automation provider did not raise LiveRegionChanged.");
        Require(WaitUntil(3000, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    ComPtr<IUIAutomationElement> playPause;
                    if (currentRoot) playPause = FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kPlayPauseAutomationId);
                    return IsFocused(playPause.Get());
                }), "Action failure moved focus away from widget:play-pause.");

        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return MatchingFailureRecords(ReadLog(logPath)) >= 1;
                }), "overlay.log omitted the exact failure identity/code/action/source record.");
        auto fixtureProcesses = FixtureDescendants(host.Id(), arguments.fixtureWorker);
        Require(WaitUntil(3000, [&] {
                    fixtureProcesses = FixtureDescendants(host.Id(), arguments.fixtureWorker);
                    return fixtureProcesses.size() == 1;
                }), "Expected exactly one fixture worker descendant after the failure.");
        const DWORD fixtureProcessId = fixtureProcesses.front();
        Handle fixtureProcess(OpenProcess(
            SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, fixtureProcessId));
        Require(static_cast<bool>(fixtureProcess),
                "Could not retain the fixture worker process handle.");

        while (GetTickCount64() < firstFailureAt + 2200) Sleep(kPollMilliseconds);
        const ULONGLONG replacementAt = GetTickCount64();
        PostKey(window, VK_RETURN);
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return MatchingFailureRecords(ReadLog(logPath)) >= 2;
                }), "The replacement action failure did not traverse the production route.");
        while (GetTickCount64() < firstFailureAt + 4300) Sleep(kPollMilliseconds);
        Require(HasExpectedStatus(automation.Get(), window, kOpenStatusAutomationId),
                "Replacement feedback did not extend beyond the first deadline.");
        const ULONGLONG replacementExpiryDeadline = replacementAt + 5500;
        const ULONGLONG replacementExpiryRemaining =
            GetTickCount64() < replacementExpiryDeadline
                ? replacementExpiryDeadline - GetTickCount64()
                : 1;
        Require(WaitUntil(static_cast<DWORD>(replacementExpiryRemaining), [&] {
                return !HasExpectedStatus(
                        automation.Get(), window, kOpenStatusAutomationId);
                }), "Replacement feedback did not expire after its bounded deadline.");

        PostKey(window, VK_RETURN);
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return HasExpectedStatus(
                        automation.Get(), window, kOpenStatusAutomationId);
                }), "The pre-hide action failure did not appear.");
        PostKey(window, VK_ESCAPE);
        Require(WaitUntil(3000, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    return currentRoot && FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kTrayAutomationId);
                }), "First Escape did not return the real host to its dashboard.");
        PostKey(window, VK_ESCAPE);
        Require(WaitUntil(3000, [&] { return !IsWindowVisible(window); }),
                "Second Escape did not hide the production overlay.");
        Require(PostMessageW(window, WM_HOTKEY, 1, MAKELPARAM(MOD_NOREPEAT, VK_F1)),
                Win32Error("PostMessageW(WM_HOTKEY)"));
        Require(WaitUntil(5000, [&] { return IsWindowVisible(window); }),
                "F1 WM_HOTKEY did not reopen the production overlay.");
        Require(WaitUntil(5000, [&] {
                    auto currentRoot = RootForWindow(automation.Get(), window);
                    return currentRoot && FindByAutomationId(
                        automation.Get(), currentRoot.Get(), kTrayAutomationId);
                }), "Reopened overlay did not republish its dashboard.");
        Require(!HasExpectedStatus(
                    automation.Get(), window, kDashboardStatusAutomationId) &&
                !HasExpectedStatus(
                    automation.Get(), window, kOpenStatusAutomationId),
                "Feedback resurrected after Hide and F1 reopen.");

        fixtureProcesses = FixtureDescendants(host.Id(), arguments.fixtureWorker);
        Require(fixtureProcesses.size() == 1 && fixtureProcesses.front() == fixtureProcessId,
                "The production host restarted or duplicated the fixture worker.");
        const std::string finalLog = ReadLog(logPath);
        Require(finalLog.find(kSecretSentinel) == std::string::npos,
                "The private exception sentinel leaked into overlay.log.");

        Require(PostMessageW(window, WM_CLOSE, 0, 0), Win32Error("PostMessageW(WM_CLOSE)"));
        Require(WaitForSingleObject(host.Process(), kOperationTimeoutMilliseconds) == WAIT_OBJECT_0,
                "Production OverlayHost did not exit after WM_CLOSE/Stop.");
        Require(WaitForSingleObject(fixtureProcess.Get(), kOperationTimeoutMilliseconds) ==
                    WAIT_OBJECT_0,
                "Fixture worker survived production WM_CLOSE/Stop.");
    } catch (...) {
        removeEventHandler();
        throw;
    }
    removeEventHandler();
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(com)) {
        std::cerr << "FAIL: CoInitializeEx failed\n";
        return EXIT_FAILURE;
    }
    try {
        Run(ParseArguments(argc, argv));
        std::cout << "WidgetActionFailureHostTests passed\n";
        CoUninitialize();
        return EXIT_SUCCESS;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        CoUninitialize();
        return EXIT_FAILURE;
    }
}
