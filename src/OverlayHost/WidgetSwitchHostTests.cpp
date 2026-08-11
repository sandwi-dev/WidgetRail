#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <Windows.h>

#include <array>
#include <cstdint>
#include <cwctype>
#include <filesystem>
#include <iostream>
#include <memory>
#include <string>
#include <string_view>

namespace fs = std::filesystem;
using gba::host_testing::Fail;
using gba::host_testing::HostProcess;
using gba::host_testing::JsonEscape;
using gba::host_testing::LocateHostWindow;
using gba::host_testing::PostKey;
using gba::host_testing::ReadUtf8;
using gba::host_testing::Require;
using gba::host_testing::SendKey;
using gba::host_testing::SendKeyDownAndPostRelease;
using gba::host_testing::WaitUntil;
using gba::host_testing::WideToUtf8;
using gba::host_testing::Win32Error;
using gba::host_testing::WriteUtf8;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30000;
constexpr DWORD kOperationTimeoutMilliseconds = 15000;
constexpr wchar_t kDevelopmentNonce[] =
    L"0780780780780780780780780780780780780780780780780780780780780780";
constexpr char kDevelopmentNonceUtf8[] =
    "0780780780780780780780780780780780780780780780780780780780780780";

struct Arguments final {
    fs::path installation;
    fs::path fixtureWorker;
};

struct Target final {
    const wchar_t* id;
    const wchar_t* label;
    UINT key;
    std::uint32_t firstSnapshotDelayMilliseconds;
};

constexpr std::array<Target, 4> kTargets{{
    {L"audio-mixer", L"Audio Mixer", VK_RETURN, 0},
    {L"network-controls", L"Network Controls", VK_RIGHT, 0},
    {L"spotify", L"Spotify", VK_RIGHT, 240},
    {L"games-apps", L"Games & Apps", VK_RIGHT, 320},
}};

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& fixtureWorker) {
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
        root_ = fs::path(temporaryRoot) /
            (L"gba-widget-switch-retention-" + std::wstring(guidText));
        processProfile_ = L"widget-switch-";
        for (const wchar_t character : std::wstring_view(guidText)) {
            if (std::iswalnum(character))
                processProfile_.push_back(static_cast<wchar_t>(std::towlower(character)));
        }
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
        readyPath_ = root_ / L"host-ready.txt";
        startupSignalRoot_ = root_ / L"startup-signals";
        fs::create_directories(startupSignalRoot_);

        WriteUtf8(root_ / L"runtime" / L"switch-fixture.gbss",
            ".switch-surface { padding: 28px; gap: 18px; corner-radius: 18px; }\n"
            ".switch-title { color: #ffffff; font-size: 28px; font-weight: 700; }\n"
            ".switch-detail { color: #ffffff; font-size: 17px; }\n"
            ".switch-button { background: #f2f4f8; color: #101318; padding: 12px; }\n"
            ".audio-surface { background: #873449; }\n"
            ".network-surface { background: #225f83; }\n"
            ".spotify-surface { background: #176f3a; }\n"
            ".games-surface { background: #8a5c18; }\n");

        const std::string worker = JsonEscape(fs::absolute(fixtureWorker).wstring());
        const auto widget = [&](const char* id, const char* packageId, const char* name,
                                const char* instanceId, const char* icon) {
            const auto startupSignal = JsonEscape(
                fs::absolute(startupSignalRoot_ / (std::string(id) + ".started")).wstring());
            return std::string(
                "    {\"id\":\"") + id + "\",\"packageId\":\"" + packageId +
                "\",\"publisherId\":\"org.gbar.tests\",\"name\":\"" + name +
                "\",\"instanceId\":\"" + instanceId + "\",\"icon\":\"" + icon +
                "\",\"workerExecutable\":\"" + worker +
                "\",\"styleFile\":\"runtime/switch-fixture.gbss\","
                "\"memoryLimitMb\":64,\"residencyPolicy\":{\"schemaVersion\":1,"
                "\"mode\":\"suspend-when-hidden\"},\"workerArguments\":["
                "\"--first-snapshot-signal\",\"" + startupSignal + "\"],"
                "\"declaredCapabilities\":[],\"quickActions\":[]}";
        };
        const std::string catalog =
            "{\n  \"catalogVersion\":1,\n"
            "  \"genericWorkerExecutable\":\"runtime/WidgetWorkerHost/WidgetWorkerHost.exe\",\n"
            "  \"widgets\":[\n" +
            widget("audio-mixer", "org.gbar.tests.audio", "Audio Mixer",
                   "audio-mixer.default", "volume") + ",\n" +
            widget("network-controls", "org.gbar.tests.network", "Network Controls",
                   "network-controls.default", "wifi") + ",\n" +
            widget("spotify", "org.gbar.tests.spotify", "Spotify",
                   "spotify.default", "music") + ",\n" +
            widget("games-apps", "org.gbar.tests.games", "Games & Apps",
                   "games-apps.default", "play") +
            "\n  ],\n  \"bundledWidgets\":[]\n}\n";
        WriteUtf8(root_ / L"widget-catalog.json", catalog);
    }

    ~TemporaryInstallation() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    [[nodiscard]] const fs::path& Root() const noexcept { return root_; }
    [[nodiscard]] const fs::path& LocalAppData() const noexcept { return localAppData_; }
    [[nodiscard]] const fs::path& ReadyPath() const noexcept { return readyPath_; }
    [[nodiscard]] const std::wstring& ProcessProfile() const noexcept {
        return processProfile_;
    }
    [[nodiscard]] fs::path StartupSignal(const std::wstring_view widgetId) const {
        return startupSignalRoot_ / (std::wstring(widgetId) + L".started");
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path readyPath_;
    fs::path startupSignalRoot_;
    std::wstring processProfile_;
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
            Fail("Usage: WidgetSwitchHostTests --installation <dir> "
                 "--fixture-worker <exe>");
        }
    }
    Require(!result.installation.empty() && !result.fixtureWorker.empty(),
            "Both --installation and --fixture-worker are required.");
    return result;
}

void FenceWindow(HWND window) {
    DWORD_PTR ignored{};
    Require(SendMessageTimeoutW(
                window, WM_NULL, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 3000, &ignored) != 0,
            Win32Error("SendMessageTimeoutW(WM_NULL)"));
}

std::string TransitionNeedle(const std::wstring_view target) {
    return " to=" + WideToUtf8(target) + " extent=";
}

void RunRetentionScenario(const Arguments& arguments) {
    auto installation = std::make_unique<TemporaryInstallation>(
        arguments.installation, arguments.fixtureWorker);
    const auto quoted = [](const fs::path& path) {
        return L"\"" + path.wstring() + L"\"";
    };
    const std::wstring hostArguments =
        L"--show --process-profile " + installation->ProcessProfile() +
        L" --development-catalog-root " + quoted(installation->Root()) +
        L" --development-ready-path " + quoted(installation->ReadyPath()) +
        L" --development-ready-nonce " + kDevelopmentNonce +
        L" --development-widget-id audio-mixer"
        L" --development-widget-instance audio-mixer.default";
    auto host = std::make_unique<HostProcess>(
        installation->Root(), installation->LocalAppData(), hostArguments);
    const auto logPath = installation->LocalAppData() /
        L"GameBarAlternative" / L"overlay.log";
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                return ReadUtf8(installation->ReadyPath()).find(kDevelopmentNonceUtf8) !=
                    std::string::npos;
            }), "Production host did not publish authenticated development readiness; log=" +
                    ReadUtf8(logPath));
    HWND window{};
    const bool visible = WaitUntil(kStartupTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host->Id());
        return window && IsWindowVisible(window);
    });
    if (!visible) {
        DWORD exitCode = STILL_ACTIVE;
        GetExitCodeProcess(host->Process(), &exitCode);
        Fail("Production host did not create a visible HWND; exit=" +
             std::to_string(exitCode) + " log=" + ReadUtf8(logPath));
    }
    FenceWindow(window);

    const auto waitForPaint = [&](const std::size_t after,
                                  const Target& target,
                                  const std::wstring_view rendered,
                                  const std::string_view authority) {
        const std::string needle =
            "Widget presentation paint target=" + WideToUtf8(target.id) +
            " content=" + std::string(authority) +
            " rendered=" + WideToUtf8(rendered) + " sequence=";
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto log = ReadUtf8(logPath);
                    const auto recordAt = log.find(needle, after);
                    if (recordAt == std::string::npos) return false;
                    if (authority != "retained") return true;
                    const auto lineEnd = log.find('\n', recordAt);
                    return log.substr(
                        recordAt,
                        lineEnd == std::string::npos
                            ? std::string::npos
                            : lineEnd - recordAt).find("semantics=inert") != std::string::npos;
                }), "Production paint trace omitted " + std::string(authority) +
                        " content for " + WideToUtf8(target.label));
    };
    const auto switchTo = [&](const Target& target, const Target& previous) {
        const auto before = ReadUtf8(logPath).size();
        const auto signal = installation->StartupSignal(target.id);
        std::error_code ignored;
        fs::remove(signal, ignored);
        if (target.firstSnapshotDelayMilliseconds > 0)
            SendKeyDownAndPostRelease(window, target.key);
        else
            SendKey(window, target.key);
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    std::error_code signalError;
                    return fs::exists(signal, signalError);
                }), "Worker did not enter its first snapshot request for " +
                        WideToUtf8(target.label));
        waitForPaint(before, target, previous.id, "retained");
        const auto duringStartup = ReadUtf8(logPath);
        const std::string admittedNeedle =
            "Widget presentation paint target=" + WideToUtf8(target.id) +
            " content=admitted rendered=" + WideToUtf8(target.id) + " sequence=";
        if (target.firstSnapshotDelayMilliseconds > 0) {
            Require(duringStartup.find(admittedNeedle, before) == std::string::npos,
                    "Destination content was admitted before its delayed snapshot completed.");
        }
        waitForPaint(before, target, target.id, "admitted");
        const auto log = ReadUtf8(logPath);
        const auto transition = log.find(TransitionNeedle(target.id), before);
        Require(transition != std::string::npos,
                "Transition diagnostics omitted the destination identity for " +
                    WideToUtf8(target.label));
        const auto lineEnd = log.find('\n', transition);
        const auto record = log.substr(
            transition,
            lineEnd == std::string::npos ? std::string::npos : lineEnd - transition);
        Require(record.find("content=retained-until-snapshot") != std::string::npos &&
                    record.find("sizing=retained-until-snapshot") != std::string::npos,
                "Transition diagnostics omitted retained content and extent authority for " +
                    WideToUtf8(target.label));
    };

    const auto audioBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_RETURN);
    waitForPaint(audioBefore, kTargets[0], kTargets[0].id, "admitted");
    SendKey(window, VK_DOWN);
    FenceWindow(window);
    for (std::size_t index = 1; index < kTargets.size(); ++index)
        switchTo(kTargets[index], kTargets[index - 1]);

    const auto reversalBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_LEFT);
    SendKey(window, VK_RIGHT);
    waitForPaint(reversalBefore, kTargets.back(), kTargets.back().id, "admitted");

    const auto restartBefore = ReadUtf8(logPath).size();
    const auto restartSignal = installation->StartupSignal(kTargets.back().id);
    {
        std::error_code ignored;
        fs::remove(restartSignal, ignored);
    }
    PostKey(window, VK_F5);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(restartSignal, ignored);
            }), "Games & Apps refresh did not enter its delayed first snapshot request.");
    waitForPaint(restartBefore, kTargets.back(), kTargets.back().id, "retained");
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto log = ReadUtf8(logPath);
                return log.size() > restartBefore &&
                    log.find("Games & Apps reloaded", restartBefore) != std::string::npos;
            }), "Same-identity runtime refresh omitted its bounded completion record.");
    waitForPaint(restartBefore, kTargets.back(), kTargets.back().id, "admitted");

    const auto log = ReadUtf8(logPath);
    Require(log.find("Render-target resize failed") == std::string::npos,
            "A production switch forced render-target recreation.");
    Require(log.find("target=animated-resize-in-place") != std::string::npos,
            "Production diagnostics omitted the existing in-place resize decision.");
    host.reset();
    installation.reset();
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized)) {
        std::cerr << "CoInitializeEx failed\n";
        return 1;
    }
    try {
        RunRetentionScenario(ParseArguments(argc, argv));
        std::cout << "WidgetSwitchHostTests passed\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "WidgetSwitchHostTests failed: " << error.what() << "\n";
        CoUninitialize();
        return 1;
    }
}
