#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <TlHelp32.h>
#include <Windows.h>

#include <algorithm>
#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

namespace fs = std::filesystem;
using widgetrail::host_testing::Handle;
using widgetrail::host_testing::HostProcess;
using widgetrail::host_testing::LocateHostWindow;
using widgetrail::host_testing::QuoteArgument;
using widgetrail::host_testing::ReadUtf8;
using widgetrail::host_testing::Require;
using widgetrail::host_testing::SendKey;
using widgetrail::host_testing::WaitUntil;
using widgetrail::host_testing::WideToUtf8;
using widgetrail::host_testing::Win32Error;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30000;
constexpr DWORD kOperationTimeoutMilliseconds = 15000;
constexpr UINT kProcessActivationMessage = WM_APP + 12;
constexpr UINT kDevelopmentTrayYHoldMessage = WM_APP + 14;
constexpr wchar_t kDevelopmentNonce[] =
    L"2092092092092092092092092092092092092092092092092092092092092092";
constexpr char kDevelopmentNonceUtf8[] =
    "2092092092092092092092092092092092092092092092092092092092092092";
constexpr wchar_t kCommunityId[] = L"dev.gbar.tests.tray-refresh-community";

struct Arguments final {
    fs::path installation;
    fs::path communityFixture;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if ((argument == L"--installation" ||
             argument == L"--community-fixture") && index + 1 < argc) {
            if (argument == L"--installation") result.installation = argv[++index];
            else result.communityFixture = argv[++index];
        } else {
            widgetrail::host_testing::Fail(
                "Usage: TrayRefreshHostTests --installation <dir> "
                "--community-fixture <exe>");
        }
    }
    Require(!result.installation.empty() && !result.communityFixture.empty(),
        "Both --installation and --community-fixture are required.");
    return result;
}

void RunCommunityInstaller(
    const fs::path& executable,
    const fs::path& catalogRoot) {
    Require(fs::is_regular_file(executable),
        "--community-fixture does not name the published test fixture");
    std::wstring command = QuoteArgument(executable.wstring()) +
        L" --install " + QuoteArgument(catalogRoot.wstring());
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    Require(CreateProcessW(
                executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
                CREATE_NO_WINDOW, nullptr, executable.parent_path().c_str(),
                &startup, &process),
        Win32Error("CreateProcessW(Community fixture installer)"));
    Handle processHandle(process.hProcess);
    Handle threadHandle(process.hThread);
    Require(WaitForSingleObject(
                processHandle.Get(), kStartupTimeoutMilliseconds) == WAIT_OBJECT_0,
        "Community fixture installation timed out.");
    DWORD exitCode{};
    Require(GetExitCodeProcess(processHandle.Get(), &exitCode) && exitCode == 0,
        "Community fixture installation failed.");
}

std::vector<DWORD> ProcessesNamed(const std::wstring_view executableName) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    Require(static_cast<bool>(snapshot),
        Win32Error("CreateToolhelp32Snapshot(processes)"));
    PROCESSENTRY32W entry{sizeof(entry)};
    std::vector<DWORD> result;
    if (Process32FirstW(snapshot.Get(), &entry)) {
        do {
            if (_wcsicmp(
                    entry.szExeFile, std::wstring(executableName).c_str()) != 0)
                continue;
            result.push_back(entry.th32ProcessID);
        } while (Process32NextW(snapshot.Get(), &entry));
    }
    std::sort(result.begin(), result.end());
    return result;
}

std::size_t CountOccurrences(
    const std::string_view text,
    const std::string_view value) {
    std::size_t count{};
    std::size_t cursor{};
    while ((cursor = text.find(value, cursor)) != std::string_view::npos) {
        ++count;
        cursor += value.size();
    }
    return count;
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& communityFixture) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
        Require(fs::is_directory(source / L"runtime"),
            "--installation does not contain runtime");
        Require(fs::is_regular_file(source / L"widget-catalog.json"),
            "--installation does not contain the packaged widget catalog");

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"wrail-tray-refresh-" + std::wstring(guidText));
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        widgetrail::host_testing::CopyNativeRuntimeDependencies(source, root_);
        fs::copy_file(
            source / L"widget-catalog.json", root_ / L"widget-catalog.json");
        fs::copy(source / L"runtime", root_ / L"runtime",
            fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        catalogRoot_ = localAppData_ / L"WidgetRail" / L"widgets";
        fs::create_directories(catalogRoot_);
        // This fixture uses rail wraparound to reach the final installed entry.
        // Do not inherit the new-install radial default's within-page navigation.
        widgetrail::host_testing::WriteUtf8(
            localAppData_ / L"WidgetRail" / L"platform-settings.json",
            R"json({"schemaVersion":1,"appearance":{"widgetSwitcher":"Rail"}})json");
        readyPath_ = root_ / L"host-ready.txt";
        RunCommunityInstaller(communityFixture, catalogRoot_);
    }

    ~TemporaryInstallation() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    const fs::path& Root() const noexcept { return root_; }
    const fs::path& LocalAppData() const noexcept { return localAppData_; }
    const fs::path& CatalogRoot() const noexcept { return catalogRoot_; }
    const fs::path& ReadyPath() const noexcept { return readyPath_; }
private:
    fs::path root_;
    fs::path localAppData_;
    fs::path catalogRoot_;
    fs::path readyPath_;
};

void RunWidgetScenario(
    const TemporaryInstallation& installation,
    const std::wstring_view widgetId,
    const std::wstring_view workerExecutable,
    const std::string_view displayName,
    const std::wstring_view profile) {
    std::error_code removeError;
    fs::remove(installation.ReadyPath(), removeError);
    Require(!removeError,
        "Could not retire the prior isolated readiness record.");
    const std::wstring hostArguments =
        L"--show --process-profile " + std::wstring(profile) +
        L" --development-catalog-root " +
            QuoteArgument(installation.CatalogRoot().wstring()) +
        L" --development-ready-path " +
            QuoteArgument(installation.ReadyPath().wstring()) +
        L" --development-ready-nonce " + kDevelopmentNonce +
        L" --development-widget-id settings"
        L" --development-widget-instance settings.default";
    const auto ambientWorkers = ProcessesNamed(workerExecutable);
    const auto scenarioWorkers = [&] {
        auto workers = ProcessesNamed(workerExecutable);
        std::erase_if(workers, [&](const DWORD processId) {
            return std::find(
                ambientWorkers.begin(), ambientWorkers.end(), processId) !=
                ambientWorkers.end();
        });
        return workers;
    };
    HostProcess host(
        installation.Root(), installation.LocalAppData(), hostArguments);
    const auto logPath = installation.LocalAppData() /
        L"WidgetRail" / L"overlay.log";
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        return ReadUtf8(installation.ReadyPath()).find(kDevelopmentNonceUtf8) !=
            std::string::npos;
    }), "Production host did not publish authenticated readiness; log=" +
        ReadUtf8(logPath));
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host.Id());
        return window && IsWindowVisible(window);
    }), "Production host did not create a visible HWND; log=" + ReadUtf8(logPath));

    const auto activate = [&] {
        DWORD_PTR ignored{};
        Require(SendMessageTimeoutW(
                    window, kProcessActivationMessage, 0, 0,
                    SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &ignored) != 0,
            Win32Error("SendMessageTimeoutW(process activation)"));
        Require(WaitUntil(5000, [&] { return IsWindowVisible(window); }),
            "Authenticated host activation did not restore visibility.");
    };
    activate();
    const auto admitted = "Widget presentation paint target=" +
        WideToUtf8(widgetId) + " content=admitted";
    if (widgetId != L"settings") {
        Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
            return ReadUtf8(logPath).find(
                "installed-catalog-terminal result=validated packages=1") != std::string::npos;
        }), "Installed catalog did not finish before tray navigation; log=" + ReadUtf8(logPath));
        // Authenticate readiness with the stable bundled Settings probe, then
        // reach the installed package through ordinary production tray
        // navigation. This keeps the fixture independent of private installed
        // generation derivation while exercising the real catalog/selection
        // route that owns the generic Hold-Y action.
        const auto selected = " selected=" + WideToUtf8(widgetId) + " ";
        // Installed widgets append after the trusted tray. Wrap left once from
        // Settings so the fixture does not start every intervening generic
        // bundled worker and can attribute the one new worker PID exactly.
        SendKey(window, VK_LEFT);
        const bool reachedInstalledWidget = WaitUntil(5000, [&] {
            return ReadUtf8(logPath).find(selected) != std::string::npos;
        });
        Require(reachedInstalledWidget,
            "Ordinary tray navigation did not select the installed Community widget; log=" +
                ReadUtf8(logPath));
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
        if (!IsWindowVisible(window)) activate();
        return ReadUtf8(logPath).find(admitted) != std::string::npos;
    }), std::string(displayName) +
        " did not reach an admitted production-host presentation; log=" +
        ReadUtf8(logPath));
    std::vector<DWORD> initialWorkers;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
        initialWorkers = scenarioWorkers();
        return initialWorkers.size() == 1;
    }), std::string(displayName) + " did not own exactly one worker process.");
    const DWORD initialWorker = initialWorkers.front();
    // Reload is a tray action. Keep tray focus throughout; entering the
    // widget and immediately backing out races its interactive admission.

    const auto sendGesture = [&](const WPARAM phase) {
        DWORD_PTR ignored{};
        Require(SendMessageTimeoutW(
                    window, kDevelopmentTrayYHoldMessage, phase, 0,
                    SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &ignored) != 0,
            Win32Error("SendMessageTimeoutW(tray Y gesture)"));
    };
    sendGesture(4);
    sendGesture(2);
    sendGesture(3);

    std::vector<DWORD> restartedWorkers;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
        restartedWorkers = scenarioWorkers();
        return restartedWorkers.size() == 1 &&
            restartedWorkers.front() != initialWorker;
    }), std::string(displayName) +
        " Hold Y did not replace exactly one selected worker; log=" +
        ReadUtf8(logPath));
    const DWORD restartedWorker = restartedWorkers.front();
    const auto artworkReset = "Widget reload retired failed artwork widget=" +
        WideToUtf8(widgetId) + " entries=";
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
        return ReadUtf8(logPath).find(artworkReset) != std::string::npos;
    }), std::string(displayName) + " successful Reload did not reset failed artwork.");
    Sleep(500);
    const auto settledWorkers =
        scenarioWorkers();
    Require(settledWorkers.size() == 1 &&
            settledWorkers.front() == restartedWorker,
        std::string(displayName) +
            " threshold repeat or release restarted the worker twice.");
    const auto log = ReadUtf8(logPath);
    Require(CountOccurrences(
                log, std::string(displayName) + " reloading") == 1,
        std::string(displayName) +
            " did not retain one exact host-owned restart diagnostic.");
    Require(CountOccurrences(log, artworkReset) == 1,
        "One Reload reset artwork failures more than once.");
}

void RunScenario(const Arguments& arguments) {
    // Each route starts with its own preferences and logs. Reload persists
    // widget focus, which must not become the next scenario's starting state.
    {
        TemporaryInstallation installation(arguments.installation, arguments.communityFixture);
        RunWidgetScenario(installation, L"settings", L"SettingsWidget.Worker.exe",
            "Settings", L"tray-refresh-settings-dlv209");
    }
    {
        TemporaryInstallation installation(arguments.installation, arguments.communityFixture);
        RunWidgetScenario(installation, kCommunityId, L"WidgetWorkerHost.exe",
            "Community Refresh Fixture", L"tray-refresh-community-dlv209");
    }
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    try {
        RunScenario(ParseArguments(argc, argv));
        std::cout << "TrayRefreshHostTests passed: 2 production restart routes\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "TrayRefreshHostTests failed: " << error.what() << '\n';
        return 1;
    }
}
