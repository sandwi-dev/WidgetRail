#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <Windows.h>

#include <array>
#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cwctype>
#include <filesystem>
#include <iostream>
#include <memory>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

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

constexpr std::array<Target, 8> kTargets{{
    {L"audio-mixer", L"Audio Mixer", VK_RETURN, 0},
    {L"game-launcher", L"Game Launcher", VK_RIGHT, 420},
    {L"now-playing", L"Now Playing", VK_RIGHT, 180},
    {L"games-apps", L"Games & Apps", VK_RIGHT, 320},
    {L"network-controls", L"Network Controls", VK_RIGHT, 0},
    {L"yt-music", L"YT Music", VK_RIGHT, 240},
    {L"spotify", L"Spotify", VK_RIGHT, 240},
    {L"settings", L"Settings", VK_RIGHT, 0},
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
            ".games-surface { background: #8a5c18; }\n"
            ".launcher-surface { background: #54418a; }\n"
            ".now-playing-surface { background: #315f65; }\n"
            ".yt-music-surface { background: #8b2635; }\n"
            ".settings-surface { background: #3e4b5b; }\n");

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
            widget("game-launcher", "org.gbar.tests.launcher", "Game Launcher",
                   "game-launcher.default", "play") + ",\n" +
            widget("now-playing", "org.gbar.tests.now-playing", "Now Playing",
                   "now-playing.default", "music") + ",\n" +
            widget("games-apps", "org.gbar.tests.games", "Games & Apps",
                   "games-apps.default", "play") + ",\n" +
            widget("network-controls", "org.gbar.tests.network", "Network Controls",
                   "network-controls.default", "wifi") + ",\n" +
            widget("yt-music", "org.gbar.tests.yt-music", "YT Music",
                   "yt-music.default", "music") + ",\n" +
            widget("spotify", "org.gbar.tests.spotify", "Spotify",
                   "spotify.default", "music") + ",\n" +
            widget("settings", "org.gbar.tests.settings", "Settings",
                   "settings.default", "settings") +
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

std::uint64_t TimingField(
    const std::string_view record, const std::string_view field) {
    const auto start = record.find(field);
    Require(start != std::string_view::npos,
            "Composition timing record omitted " + std::string(field));
    const auto valueStart = start + field.size();
    const auto valueEnd = record.find_first_not_of("0123456789", valueStart);
    Require(valueEnd != valueStart, "Composition timing field was empty");
    return std::stoull(std::string(record.substr(valueStart, valueEnd - valueStart)));
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
    std::vector<std::uint64_t> drawTimings;
    std::vector<std::uint64_t> commitTimings;
    std::vector<std::uint64_t> geometryTimings;
    std::vector<std::uint64_t> motionCommitTimings;
    const auto recordComposition = [&](const std::size_t after,
                                       const std::wstring_view label) {
        constexpr std::string_view placementNeedle =
            "Composition placement committed content=complete";
        constexpr std::string_view frameNeedle =
            "Composition frame committed content=complete";
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto pending = ReadUtf8(logPath);
                    return pending.find(placementNeedle, after) != std::string::npos ||
                        pending.find(frameNeedle, after) != std::string::npos;
                }),
                "Production transition omitted a committed complete-content surface for " +
                    WideToUtf8(label));
        const auto log = ReadUtf8(logPath);
        const auto placementAt = log.find(placementNeedle, after);
        const auto frameAt = log.find(frameNeedle, after);
        const auto composedAt = placementAt == std::string::npos
            ? frameAt
            : frameAt == std::string::npos
                ? placementAt
                : std::min(placementAt, frameAt);
        Require(composedAt != std::string::npos,
                "Committed composition record disappeared during validation");
        const auto composedEnd = log.find('\n', composedAt);
        const auto composed = log.substr(
            composedAt,
            composedEnd == std::string::npos
                ? std::string::npos
                : composedEnd - composedAt);
        const bool geometryPlacement =
            composed.find("order=commit-place") != std::string::npos;
        const bool motionPlacement =
            composed.find("order=commit-motion-container") != std::string::npos;
        const bool sameGeometryCommit =
            composed.find("order=commit-no-geometry") != std::string::npos;
        Require(((geometryPlacement || motionPlacement) &&
                    composed.find("waited=false") != std::string::npos) ||
                    (sameGeometryCommit &&
                     composed.find("waited=false") != std::string::npos),
                "Composition did not commit before exposing changed content or geometry");
        Require(composed.find("alpha=premultiplied-clear") != std::string::npos ||
                    sameGeometryCommit,
                "Changed composition geometry omitted its transparent alpha contract");
        drawTimings.push_back(TimingField(composed, "draw-us="));
        commitTimings.push_back(TimingField(composed, "commit-us="));
        geometryTimings.push_back(TimingField(composed, "geometry-us="));
        if (motionPlacement) {
            constexpr std::string_view finalNeedle = "Composition motion final steps=";
            Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                        return ReadUtf8(logPath).find(finalNeedle, composedAt) !=
                            std::string::npos;
                    }), "Composition motion did not reach one final HWND handoff for " +
                        WideToUtf8(label));
            const auto settledLog = ReadUtf8(logPath);
            const auto startAt = settledLog.find("Composition motion start", composedAt);
            const auto finalAt = settledLog.find(finalNeedle, composedAt);
            Require(startAt != std::string::npos && finalAt != std::string::npos &&
                        startAt < finalAt,
                    "Composition motion ordering omitted start-before-final evidence");
            const auto motion = settledLog.substr(startAt, finalAt - startAt);
            Require(motion.find("destination=complete waited=false redraw=false") !=
                        std::string::npos,
                    "Composition motion did not retain a complete nonblocking destination");
            Require(motion.find("Composition frame committed") == std::string::npos,
                    "Composition motion redrew the complete surface on an animation tick");
            std::size_t stepAt{};
            std::size_t steps{};
            while ((stepAt = motion.find("Composition motion step index=", stepAt)) !=
                    std::string::npos) {
                const auto stepEnd = motion.find('\n', stepAt);
                const auto record = motion.substr(
                    stepAt, stepEnd == std::string::npos
                        ? std::string::npos : stepEnd - stepAt);
                Require(record.find("waited=false redraw=false") != std::string::npos,
                        "Composition motion step blocked or redrew the destination");
                motionCommitTimings.push_back(TimingField(record, "commit-us="));
                ++steps;
                stepAt = stepEnd == std::string::npos ? motion.size() : stepEnd + 1;
            }
            Require(steps >= 2 && steps <= 16,
                    "Composition motion used an unbounded or missing commit cadence");
            const auto finalEnd = settledLog.find('\n', finalAt);
            const auto finalRecord = settledLog.substr(
                finalAt, finalEnd == std::string::npos
                    ? std::string::npos : finalEnd - finalAt);
            Require(finalRecord.find("geometry=retained-container") != std::string::npos &&
                        finalRecord.find(
                            "waited=false redraw=false alpha=premultiplied-clear") !=
                            std::string::npos,
                    "Composition motion final handoff was not transparent and nonblocking");
        }
    };

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
                    const auto lineEnd = log.find('\n', recordAt);
                    const auto record = log.substr(
                        recordAt,
                        lineEnd == std::string::npos
                            ? std::string::npos
                            : lineEnd - recordAt);
                    if (authority != "retained") return true;
                    return record.find("semantics=inert") != std::string::npos &&
                        record.find("input-owner=tray") != std::string::npos &&
                        record.find("selected=" + WideToUtf8(target.id)) !=
                            std::string::npos &&
                        record.find("visual-focus=none") != std::string::npos &&
                        record.find("semantic-focus=tray:" + WideToUtf8(target.id)) !=
                            std::string::npos;
                }), "Production paint trace omitted " + std::string(authority) +
                        " content for " + WideToUtf8(target.label) + " after=" +
                        ReadUtf8(logPath).substr(after));
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
                        WideToUtf8(target.label) + "; log=" + ReadUtf8(logPath));
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
        const auto admittedAt = log.find(admittedNeedle, before);
        Require(admittedAt != std::string::npos,
                "Admitted paint trace disappeared before composition validation");
        const auto admittedEnd = log.find('\n', admittedAt);
        const auto admittedRecord = log.substr(
            admittedAt,
            admittedEnd == std::string::npos
                ? std::string::npos
                : admittedEnd - admittedAt);
        Require(admittedRecord.find("input-owner=tray") != std::string::npos &&
                    admittedRecord.find("selected=" + WideToUtf8(target.id)) !=
                        std::string::npos &&
                    admittedRecord.find("visual-focus=none") != std::string::npos &&
                    admittedRecord.find(
                        "semantic-focus=tray:" + WideToUtf8(target.id)) !=
                        std::string::npos,
                "Admitted destination did not preserve ordered tray focus authority for " +
                    WideToUtf8(target.label));
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
        recordComposition(admittedAt, target.label);
    };

    const auto audioBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_RETURN);
    waitForPaint(audioBefore, kTargets[0], kTargets[0].id, "admitted");
    const auto audioLog = ReadUtf8(logPath);
    const auto audioAdmittedAt = audioLog.find(
        "Widget presentation paint target=audio-mixer content=admitted", audioBefore);
    Require(audioAdmittedAt != std::string::npos,
            "Audio Mixer admitted trace disappeared before composition validation");
    recordComposition(audioAdmittedAt, kTargets[0].label);
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
            }), "Final widget refresh did not enter its first snapshot request.");
    waitForPaint(restartBefore, kTargets.back(), kTargets.back().id, "retained");
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto log = ReadUtf8(logPath);
                return log.size() > restartBefore &&
                    log.find("Settings reloaded", restartBefore) != std::string::npos;
            }), "Same-identity Settings refresh omitted its bounded completion record.");
    waitForPaint(restartBefore, kTargets.back(), kTargets.back().id, "admitted");

    RECT hostBounds{};
    Require(GetWindowRect(window, &hostBounds) != FALSE,
            Win32Error("GetWindowRect(composition host)"));
    const auto packPoint = [](const LONG x, const LONG y) {
        return MAKELPARAM(
            static_cast<WORD>(static_cast<short>(x)),
            static_cast<WORD>(static_cast<short>(y)));
    };
    const auto unusedHit = SendMessageW(
        window, WM_NCHITTEST, 0,
        packPoint(hostBounds.left + 1, hostBounds.top + 1));
    const auto authoredHit = SendMessageW(
        window, WM_NCHITTEST, 0,
        packPoint(
            (hostBounds.left + hostBounds.right) / 2,
            (hostBounds.top + hostBounds.bottom) / 2));
    Require(unusedHit == HTTRANSPARENT && authoredHit == HTCLIENT,
            "Fixed composition container did not exclude transparent client pixels from hit testing");
    Require(GetClassLongPtrW(window, GCLP_HBRBACKGROUND) == 0,
            "Composition HWND retained an opaque class background owner");

    const auto reopenBefore = ReadUtf8(logPath).size();
    // Queue the switch and close in order on the host thread. Waiting for a
    // flushed diagnostic here can consume the complete 140 ms transition and
    // would no longer exercise the interrupted close/reopen contract.
    PostKey(window, VK_RIGHT);
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(close hotkey)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) == FALSE;
            }), "Composition host did not complete the bounded close");
    const auto hiddenLog = ReadUtf8(logPath);
    Require(hiddenLog.find("Composition motion start", reopenBefore) !=
                std::string::npos,
            "Reopen scenario did not begin an interruptible composition motion");
    const auto retiredAt = hiddenLog.find(
        "Composition motion retired state=hidden", reopenBefore);
    Require(retiredAt != std::string::npos,
            "Hidden host did not retire its interrupted composition motion");
    const auto retiredEnd = hiddenLog.find('\n', retiredAt);
    const auto retiredRecord = hiddenLog.substr(
        retiredAt, retiredEnd == std::string::npos
            ? std::string::npos : retiredEnd - retiredAt);
    Require(retiredRecord.find("in-flight=true") != std::string::npos &&
                retiredRecord.find(
                    "future-work=false geometry=discarded") != std::string::npos,
            "Hidden host retained stale composition geometry or frame work; record=" +
                retiredRecord);
    const auto hiddenBoundary = retiredEnd == std::string::npos
        ? hiddenLog.size() : retiredEnd;
    std::this_thread::sleep_for(std::chrono::milliseconds(220));
    const auto afterHidden = ReadUtf8(logPath);
    Require(afterHidden.find("Composition motion step", hiddenBoundary) ==
                std::string::npos &&
                afterHidden.find("Composition frame committed", hiddenBoundary) ==
                    std::string::npos,
            "Hidden host continued composition frame work after retirement");

    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(reopen hotkey)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) != FALSE;
            }), "Composition host did not reopen after interrupted motion");
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                const auto placement = current.find(
                    "Composition placement committed content=complete", hiddenBoundary);
                return placement != std::string::npos &&
                    current.find("order=commit-place", placement) != std::string::npos &&
                    current.find("alpha=premultiplied-clear", placement) !=
                        std::string::npos;
            }), "Reopened host did not atomically place a transparent complete surface");

    const auto log = ReadUtf8(logPath);
    Require(log.find("Render-target resize failed") == std::string::npos,
            "A production switch forced render-target recreation.");
    Require(log.find("DirectComposition complete-content presentation owner active") !=
                std::string::npos,
            "Production host did not activate its DirectComposition owner.");
    Require(log.find(
                "alpha=premultiplied-clear hwnd=no-redirection "
                "opacity=composition-effect hwnd-background=none") !=
                std::string::npos &&
                log.find("target=composition-motion") != std::string::npos,
            "Production extent diagnostics omitted the alpha or motion decision.");
    Require(log.find("DirectComposition presentation disabled") == std::string::npos &&
                log.find("Overlay render target resized in place") == std::string::npos,
            "Production transition fell back to direct HWND presentation.");
    Require(drawTimings.size() == kTargets.size() &&
                commitTimings.size() == drawTimings.size() &&
                geometryTimings.size() == drawTimings.size(),
            "Production timing distribution omitted a widget transition.");
    constexpr std::uint64_t kTransitionBudgetMicroseconds = 100000;
    Require(*std::max_element(drawTimings.begin(), drawTimings.end()) <=
                kTransitionBudgetMicroseconds,
            "Complete destination drawing exceeded the bounded transition budget.");
    Require(*std::max_element(commitTimings.begin(), commitTimings.end()) <=
                kTransitionBudgetMicroseconds,
            "Synchronous replacement commit exceeded the bounded transition budget.");
    Require(*std::max_element(geometryTimings.begin(), geometryTimings.end()) <=
                kTransitionBudgetMicroseconds,
            "Coordinated commit and HWND geometry exceeded the bounded transition budget.");
    Require(!motionCommitTimings.empty(),
            "Production matrix omitted composition motion samples.");
    const auto maximumMotionCommit =
        *std::max_element(motionCommitTimings.begin(), motionCommitTimings.end());
    Require(maximumMotionCommit <= kTransitionBudgetMicroseconds,
            "Nonblocking composition motion commits exceeded the transition budget; max-us=" +
                std::to_string(maximumMotionCommit));
    std::cout << "Composition transition timing us draw-max="
              << *std::max_element(drawTimings.begin(), drawTimings.end())
              << " commit-max="
              << *std::max_element(commitTimings.begin(), commitTimings.end())
              << " geometry-max="
              << *std::max_element(geometryTimings.begin(), geometryTimings.end())
              << " motion-commit-max="
              << maximumMotionCommit
              << " samples=" << drawTimings.size() << '\n';
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
