#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <Windows.h>

#include <filesystem>
#include <iostream>
#include <memory>
#include <string>

namespace fs = std::filesystem;
using gba::host_testing::HostProcess;
using gba::host_testing::JsonEscape;
using gba::host_testing::LocateHostWindow;
using gba::host_testing::ReadUtf8;
using gba::host_testing::Require;
using gba::host_testing::SendKey;
using gba::host_testing::WaitUntil;
using gba::host_testing::Win32Error;
using gba::host_testing::WriteUtf8;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30000;
constexpr DWORD kOperationTimeoutMilliseconds = 15000;
constexpr UINT kDevelopmentTrayYHoldMessage = WM_APP + 14;
constexpr wchar_t kDevelopmentNonce[] =
    L"1931931931931931931931931931931931931931931931931931931931931931";
constexpr char kDevelopmentNonceUtf8[] =
    "1931931931931931931931931931931931931931931931931931931931931931";

struct Arguments final {
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
            gba::host_testing::Fail(
                "Usage: TrayRefreshHostTests --installation <dir> "
                "--fixture-worker <exe>");
        }
    }
    Require(!result.installation.empty() && !result.fixtureWorker.empty(),
            "Both --installation and --fixture-worker are required.");
    return result;
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(const fs::path& source, const fs::path& fixtureWorker) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
                "--installation does not contain OverlayHost.exe");
        Require(fs::is_directory(source / L"runtime"),
                "--installation does not contain runtime");
        Require(fs::is_regular_file(fixtureWorker),
                "--fixture-worker does not name a file");

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) / (L"gba-tray-refresh-" + std::wstring(guidText));
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
        readyPath_ = root_ / L"host-ready.txt";
        refreshPath_ = root_ / L"settings-refresh.txt";
        WriteUtf8(root_ / L"runtime" / L"tray-refresh.gbss",
                  ".refresh-root { background: #273044; padding: 24px; }\n");

        const auto worker = JsonEscape(fs::absolute(fixtureWorker).wstring());
        const auto refresh = JsonEscape(fs::absolute(refreshPath_).wstring());
        WriteUtf8(root_ / L"widget-catalog.json",
            "{\n  \"catalogVersion\":1,\n"
            "  \"genericWorkerExecutable\":\"runtime/WidgetWorkerHost/WidgetWorkerHost.exe\",\n"
            "  \"widgets\":[{"
            "\"id\":\"settings\",\"packageId\":\"org.gbar.tests.settings\","
            "\"publisherId\":\"org.gbar.tests\",\"name\":\"Settings\","
            "\"instanceId\":\"settings.default\",\"icon\":\"settings\","
            "\"workerExecutable\":\"" + worker + "\","
            "\"styleFile\":\"runtime/tray-refresh.gbss\","
            "\"memoryLimitMb\":64,\"residencyPolicy\":{\"schemaVersion\":1,"
            "\"mode\":\"suspend-when-hidden\"},"
            "\"workerArguments\":[\"--refresh-signal\",\"" + refresh + "\"],"
            "\"declaredCapabilities\":[],\"quickActions\":[{"
            "\"id\":\"refresh\",\"label\":\"Refresh\",\"actionId\":\"refresh\","
            "\"sourceElementId\":\"settings.refresh\",\"controllerButton\":null}]}],\n"
            "  \"bundledWidgets\":[]\n}\n");
    }

    ~TemporaryInstallation() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    const fs::path& Root() const noexcept { return root_; }
    const fs::path& LocalAppData() const noexcept { return localAppData_; }
    const fs::path& ReadyPath() const noexcept { return readyPath_; }
    const fs::path& RefreshPath() const noexcept { return refreshPath_; }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path readyPath_;
    fs::path refreshPath_;
};

void RunScenario(const Arguments& arguments) {
    auto installation = std::make_unique<TemporaryInstallation>(
        arguments.installation, arguments.fixtureWorker);
    const auto quoted = [](const fs::path& path) {
        return L"\"" + path.wstring() + L"\"";
    };
    const std::wstring hostArguments =
        L"--show --process-profile tray-refresh-dlv193"
        L" --development-catalog-root " + quoted(installation->Root()) +
        L" --development-ready-path " + quoted(installation->ReadyPath()) +
        L" --development-ready-nonce " + kDevelopmentNonce +
        L" --development-widget-id settings"
        L" --development-widget-instance settings.default";
    auto host = std::make_unique<HostProcess>(
        installation->Root(), installation->LocalAppData(), hostArguments);
    const auto logPath = installation->LocalAppData() /
        L"GameBarAlternative" / L"overlay.log";
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                return ReadUtf8(installation->ReadyPath()).find(kDevelopmentNonceUtf8) !=
                    std::string::npos;
            }), "Production host did not publish authenticated readiness; log=" +
                    ReadUtf8(logPath));
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                window = LocateHostWindow(host->Id());
                return window && IsWindowVisible(window);
            }), "Production host did not create a visible HWND; log=" + ReadUtf8(logPath));
    // The ordinary dashboard starts workers lazily. Enter and return through
    // normal host navigation so the selected Settings descriptor has the
    // admitted snapshot required by contextual action routing.
    SendKey(window, VK_RETURN);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadUtf8(logPath).find(
                    "Widget presentation paint target=settings content=admitted") !=
                    std::string::npos;
            }), "Settings did not reach an admitted production-host presentation; log=" +
                    ReadUtf8(logPath));
    SendKey(window, VK_ESCAPE);

    const auto sendGesture = [&](const WPARAM phase) {
        DWORD_PTR ignored{};
        Require(SendMessageTimeoutW(
                    window, kDevelopmentTrayYHoldMessage, phase, 0,
                    SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, &ignored) != 0,
                Win32Error("SendMessageTimeoutW(tray Y gesture)"));
    };
    // Tier 1 owns exact temporal boundaries. The authenticated seam crosses
    // the already-proven threshold atomically so the real controller timer's
    // deliberate no-device cancellation cannot invalidate this action-route
    // fixture between synthetic samples.
    sendGesture(4);
    sendGesture(2);
    sendGesture(2);
    sendGesture(3);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadUtf8(installation->RefreshPath()) == "refresh\n";
            }), "One held tray Y did not dispatch exactly one advertised Settings refresh; log=" +
                    ReadUtf8(logPath));
    Sleep(250);
    Require(ReadUtf8(installation->RefreshPath()) == "refresh\n",
            "Threshold repeat or release dispatched a second contextual refresh");
    const auto log = ReadUtf8(logPath);
    Require(log.find(
                "Tray Y contextual refresh widget=settings action=refresh handled=true") !=
                std::string::npos,
            "Production host omitted the exact contextual refresh verdict");
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    try {
        RunScenario(ParseArguments(argc, argv));
        std::cout << "TrayRefreshHostTests passed: 1 production route\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "TrayRefreshHostTests failed: " << error.what() << '\n';
        return 1;
    }
}
