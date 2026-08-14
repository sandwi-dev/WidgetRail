#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <TlHelp32.h>
#include <Windows.h>

#include <array>
#include <algorithm>
#include <chrono>
#include <cmath>
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
    std::string repositoryCommit;
    std::string hostSha256;
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
        Require(fs::is_regular_file(source / L"OverlayPlatformInterop.dll"),
                "--installation does not contain OverlayPlatformInterop.dll");
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
        fs::copy_file(source / L"OverlayPlatformInterop.dll",
                      root_ / L"OverlayPlatformInterop.dll");
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
        readyPath_ = root_ / L"host-ready.txt";
        startupSignalRoot_ = root_ / L"startup-signals";
        fs::create_directories(startupSignalRoot_);
        blockedSnapshotTrigger_ = root_ / L"block-snapshot.trigger";
        blockedSnapshotSignal_ = root_ / L"block-snapshot.started";
        blockedSnapshotRelease_ = root_ / L"block-snapshot.release";
        blockedSnapshotComplete_ = root_ / L"block-snapshot.completed";

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
                                const char* instanceId, const char* icon,
                                const bool blockable = false) {
            const auto startupSignal = JsonEscape(
                fs::absolute(startupSignalRoot_ / (std::string(id) + ".started")).wstring());
            const std::string blockingArguments = blockable
                ? ",\"--block-snapshot-trigger\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotTrigger_).wstring()) +
                    "\",\"--block-snapshot-signal\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotSignal_).wstring()) +
                    "\",\"--block-snapshot-release\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotRelease_).wstring()) +
                    "\",\"--block-snapshot-complete\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotComplete_).wstring()) + "\""
                : "";
            return std::string(
                "    {\"id\":\"") + id + "\",\"packageId\":\"" + packageId +
                "\",\"publisherId\":\"org.gbar.tests\",\"name\":\"" + name +
                "\",\"instanceId\":\"" + instanceId + "\",\"icon\":\"" + icon +
                "\",\"workerExecutable\":\"" + worker +
                "\",\"styleFile\":\"runtime/switch-fixture.gbss\","
                "\"memoryLimitMb\":64,\"residencyPolicy\":{\"schemaVersion\":1,"
                "\"mode\":\"suspend-when-hidden\"},\"workerArguments\":["
                "\"--first-snapshot-signal\",\"" + startupSignal + "\"" +
                blockingArguments + "],"
                "\"declaredCapabilities\":[],\"quickActions\":[]}";
        };
        catalog_ =
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
                   "settings.default", "settings", true) +
            "\n  ],\n  \"bundledWidgets\":[]\n}\n";
        catalogWithProbe_ =
            catalog_.substr(0, catalog_.find("\n  ],\n")) + ",\n" +
            widget("catalog-probe", "org.gbar.tests.catalog-probe", "Catalog Probe",
                   "catalog-probe.default", "settings") +
            "\n  ],\n  \"bundledWidgets\":[]\n}\n";
        WriteUtf8(root_ / L"widget-catalog.json", catalog_);
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
    void BlockNextSnapshot() const {
        std::error_code ignored;
        fs::remove(blockedSnapshotSignal_, ignored);
        fs::remove(blockedSnapshotRelease_, ignored);
        fs::remove(blockedSnapshotComplete_, ignored);
        WriteUtf8(blockedSnapshotTrigger_, "block");
    }
    void ReleaseBlockedSnapshot() const {
        WriteUtf8(blockedSnapshotRelease_, "release");
    }
    [[nodiscard]] const fs::path& BlockedSnapshotSignal() const noexcept {
        return blockedSnapshotSignal_;
    }
    [[nodiscard]] const fs::path& BlockedSnapshotComplete() const noexcept {
        return blockedSnapshotComplete_;
    }
    [[nodiscard]] long long BlockedSnapshotSequence() const {
        const auto signal = ReadUtf8(blockedSnapshotSignal_);
        const auto separator = signal.rfind(':');
        Require(separator != std::string::npos,
                "Blocked snapshot signal omitted its exact render sequence.");
        return std::stoll(signal.substr(separator + 1));
    }
    void PublishCatalogProbe(const bool present) const {
        WriteUtf8(
            root_ / L"widget-catalog.json",
            present ? catalogWithProbe_ : catalog_);
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path readyPath_;
    fs::path startupSignalRoot_;
    fs::path blockedSnapshotTrigger_;
    fs::path blockedSnapshotSignal_;
    fs::path blockedSnapshotRelease_;
    fs::path blockedSnapshotComplete_;
    std::wstring processProfile_;
    std::string catalog_;
    std::string catalogWithProbe_;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if ((argument == L"--installation" || argument == L"--fixture-worker" ||
             argument == L"--repository-commit" || argument == L"--host-sha256") &&
            index + 1 < argc) {
            if (argument == L"--installation") result.installation = argv[++index];
            else if (argument == L"--fixture-worker") result.fixtureWorker = argv[++index];
            else if (argument == L"--repository-commit")
                result.repositoryCommit = WideToUtf8(argv[++index]);
            else
                result.hostSha256 = WideToUtf8(argv[++index]);
        } else {
            Fail("Usage: WidgetSwitchHostTests --installation <dir> "
                 "--fixture-worker <exe> --repository-commit <sha> "
                 "--host-sha256 <sha256>");
        }
    }
    Require(!result.installation.empty() && !result.fixtureWorker.empty() &&
                !result.repositoryCommit.empty() && !result.hostSha256.empty(),
            "Installation, fixture worker, repository commit, and host SHA-256 are required.");
    return result;
}

std::uint64_t ProcessStartFileTime(const HANDLE process) {
    FILETIME created{}, exited{}, kernel{}, user{};
    Require(GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE,
            Win32Error("GetProcessTimes(OverlayHost)"));
    ULARGE_INTEGER value{};
    value.LowPart = created.dwLowDateTime;
    value.HighPart = created.dwHighDateTime;
    return value.QuadPart;
}

std::string ObservedChildRoles(
    const DWORD rootProcessId,
    std::vector<DWORD>* observedProcessIds = nullptr) {
    struct Row final {
        DWORD processId{};
        DWORD parentProcessId{};
        std::wstring name;
    };
    std::vector<Row> rows;
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    Require(snapshot != INVALID_HANDLE_VALUE,
            Win32Error("CreateToolhelp32Snapshot(process provenance)"));
    PROCESSENTRY32W entry{sizeof(entry)};
    if (Process32FirstW(snapshot, &entry)) {
        do {
            rows.push_back({entry.th32ProcessID, entry.th32ParentProcessID,
                            entry.szExeFile});
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);

    std::vector<DWORD> parents{rootProcessId};
    std::string result;
    for (std::size_t cursor = 0; cursor < parents.size(); ++cursor) {
        for (const auto& row : rows) {
            if (row.parentProcessId != parents[cursor]) continue;
            parents.push_back(row.processId);
            if (observedProcessIds) observedProcessIds->push_back(row.processId);
            std::string role = "other-child";
            if (_wcsicmp(row.name.c_str(), L"WidgetBridge.exe") == 0)
                role = "bridge";
            else if (row.name.find(L"WidgetWorker") != std::wstring::npos ||
                     row.name.find(L"WidgetSwitchFixture") != std::wstring::npos)
                role = "worker";
            if (!result.empty()) result += ',';
            result += role + ':' + WideToUtf8(row.name) + ':' +
                std::to_string(row.processId);
        }
    }
    return result.empty() ? "none-observed" : result;
}

bool ProcessIsRunning(const DWORD processId) {
    const HANDLE process = OpenProcess(SYNCHRONIZE, FALSE, processId);
    if (!process) return false;
    const bool running = WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
    CloseHandle(process);
    return running;
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

std::string TextField(
    const std::string_view record, const std::string_view field) {
    const auto start = record.find(field);
    Require(start != std::string_view::npos,
            "Production geometry record omitted " + std::string(field));
    const auto valueStart = start + field.size();
    const auto valueEnd = record.find(' ', valueStart);
    Require(valueEnd != valueStart, "Production geometry field was empty");
    return std::string(record.substr(valueStart, valueEnd - valueStart));
}

struct LoggedBounds final {
    float x{};
    float y{};
    float width{};
    float height{};
};

LoggedBounds ParseBounds(const std::string& value) {
    LoggedBounds result;
    std::size_t cursor{};
    for (float* field : {&result.x, &result.y, &result.width, &result.height}) {
        const auto end = value.find(',', cursor);
        *field = std::stof(value.substr(cursor, end - cursor));
        cursor = end == std::string::npos ? value.size() : end + 1;
    }
    return result;
}

bool SameBottomCenteredScreenBounds(
    const std::string& firstRecord,
    const std::string& secondRecord,
    const std::string_view field) {
    const auto firstShell = ParseBounds(TextField(firstRecord, "shell-bounds="));
    const auto secondShell = ParseBounds(TextField(secondRecord, "shell-bounds="));
    const auto first = ParseBounds(TextField(firstRecord, field));
    const auto second = ParseBounds(TextField(secondRecord, field));
    constexpr float tolerance = 0.01F;
    return std::abs(
               (first.x - firstShell.width * 0.5F) -
               (second.x - secondShell.width * 0.5F)) <= tolerance &&
        std::abs(
               (first.y - firstShell.height) -
               (second.y - secondShell.height)) <= tolerance &&
        std::abs(first.width - second.width) <= tolerance &&
        std::abs(first.height - second.height) <= tolerance;
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
    std::vector<std::uint64_t> inputToRetainedMilliseconds;
    std::vector<std::uint64_t> inputToAdmittedMilliseconds;
    std::uint64_t firstAdmittedActivationMilliseconds{};
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
                    if (record.find("tray-total=8") == std::string::npos ||
                        record.find("tray-selected-visible=true") ==
                            std::string::npos)
                        return false;
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
        const auto log = ReadUtf8(logPath);
        const auto recordAt = log.find(needle, after);
        const auto lineEnd = log.find('\n', recordAt);
        return log.substr(
            recordAt,
            lineEnd == std::string::npos ? std::string::npos : lineEnd - recordAt);
    };
    const auto switchTo = [&](const Target& target, const Target& previous) {
        const auto inputStarted = std::chrono::steady_clock::now();
        const auto before = ReadUtf8(logPath).size();
        const auto signal = installation->StartupSignal(target.id);
        std::error_code ignored;
        fs::remove(signal, ignored);
        if (target.firstSnapshotDelayMilliseconds > 0)
            SendKeyDownAndPostRelease(window, target.key);
        else
            SendKey(window, target.key);
        const auto retainedRecord = waitForPaint(
            before, target, previous.id, "retained");
        const auto retainedLog = ReadUtf8(logPath);
        const auto retainedAt = retainedLog.find(retainedRecord, before);
        Require(retainedAt != std::string::npos,
                "Retained paint trace disappeared before composition validation");
        const auto retainedEnd = retainedLog.find('\n', retainedAt);
        const auto afterRetainedPaint = retainedEnd == std::string::npos
            ? retainedLog.size() : retainedEnd + 1;
        constexpr std::string_view completeNeedle =
            "Composition frame committed content=complete";
        constexpr std::string_view placedNeedle =
            "Composition placement committed content=complete";
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto pending = ReadUtf8(logPath);
                    return pending.find(completeNeedle, afterRetainedPaint) !=
                               std::string::npos ||
                        pending.find(placedNeedle, afterRetainedPaint) !=
                               std::string::npos;
                }), "Retained source paint was not followed by a complete composition "
                    "commit for " + WideToUtf8(target.label));
        inputToRetainedMilliseconds.push_back(
            static_cast<std::uint64_t>(std::chrono::duration_cast<
                std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - inputStarted).count()));
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    std::error_code signalError;
                    return fs::exists(signal, signalError);
                }), "Worker did not enter its first snapshot request for " +
                        WideToUtf8(target.label) + "; log=" + ReadUtf8(logPath));
        const auto duringStartup = ReadUtf8(logPath);
        const std::string admittedNeedle =
            "Widget presentation paint target=" + WideToUtf8(target.id) +
            " content=admitted rendered=" + WideToUtf8(target.id) + " sequence=";
        if (target.firstSnapshotDelayMilliseconds > 0) {
            Require(duringStartup.find(admittedNeedle, before) == std::string::npos,
                    "Destination content was admitted before its delayed snapshot completed.");
        }
        const auto destinationRecord = waitForPaint(
            before, target, target.id, "admitted");
        Require(SameBottomCenteredScreenBounds(
                    retainedRecord, destinationRecord, "tray-bounds=") &&
                    SameBottomCenteredScreenBounds(
                        retainedRecord, destinationRecord, "tray-selected-bounds="),
                "Retained and admitted content moved bottom-centered tray geometry for " +
                    WideToUtf8(target.label));
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
        Require(admittedRecord.find("tray-visible=8") != std::string::npos &&
                    admittedRecord.find("tray-previous=false") != std::string::npos &&
                    admittedRecord.find("tray-next=false") != std::string::npos,
                "Widget switching changed shared tray capacity for " +
                    WideToUtf8(target.label) + "; record=" + admittedRecord);
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
        recordComposition(
            admittedEnd == std::string::npos ? log.size() : admittedEnd + 1,
            target.label);
        inputToAdmittedMilliseconds.push_back(
            static_cast<std::uint64_t>(std::chrono::duration_cast<
                std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - inputStarted).count()));
    };

    const auto firstActivationStarted = std::chrono::steady_clock::now();
    const auto audioBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_RETURN);
    waitForPaint(audioBefore, kTargets[0], kTargets[0].id, "admitted");
    const auto audioLog = ReadUtf8(logPath);
    const auto audioAdmittedAt = audioLog.find(
        "Widget presentation paint target=audio-mixer content=admitted", audioBefore);
    Require(audioAdmittedAt != std::string::npos,
            "Audio Mixer admitted trace disappeared before composition validation");
    const auto audioAdmittedEnd = audioLog.find('\n', audioAdmittedAt);
    const auto audioAdmittedRecord = audioLog.substr(
        audioAdmittedAt,
        audioAdmittedEnd == std::string::npos
            ? std::string::npos
            : audioAdmittedEnd - audioAdmittedAt);
    Require(audioAdmittedRecord.find("tray-total=8") != std::string::npos &&
                audioAdmittedRecord.find("tray-visible=8") != std::string::npos &&
                audioAdmittedRecord.find("tray-previous=false") != std::string::npos &&
                audioAdmittedRecord.find("tray-next=false") != std::string::npos &&
                audioAdmittedRecord.find("tray-selected-visible=true") !=
                    std::string::npos,
            "Shared production tray did not retain every identity at its stable capacity");
    recordComposition(
        audioAdmittedEnd == std::string::npos ? audioLog.size() : audioAdmittedEnd + 1,
        kTargets[0].label);
    firstAdmittedActivationMilliseconds =
        static_cast<std::uint64_t>(std::chrono::duration_cast<
            std::chrono::milliseconds>(
                std::chrono::steady_clock::now() - firstActivationStarted).count());

    const auto addedCatalogBefore = ReadUtf8(logPath).size();
    installation->PublishCatalogProbe(true);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                const auto paint = current.find(
                    "Widget presentation paint target=audio-mixer content=admitted",
                    addedCatalogBefore);
                if (paint == std::string::npos) return false;
                const auto end = current.find('\n', paint);
                const auto record = current.substr(
                    paint, end == std::string::npos ? std::string::npos : end - paint);
                return record.find("tray-total=9") != std::string::npos &&
                    record.find("tray-visible=9") != std::string::npos &&
                    record.find("selected=audio-mixer") != std::string::npos &&
                    record.find("tray-selected-visible=true") != std::string::npos &&
                    record.find("tray-next=false") != std::string::npos;
            }), "Production catalog addition did not synchronously republish the shared tray; log=" +
                    ReadUtf8(logPath).substr(addedCatalogBefore));

    const auto removedCatalogBefore = ReadUtf8(logPath).size();
    installation->PublishCatalogProbe(false);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                const auto paint = current.find(
                    "Widget presentation paint target=audio-mixer content=admitted",
                    removedCatalogBefore);
                if (paint == std::string::npos) return false;
                const auto end = current.find('\n', paint);
                const auto record = current.substr(
                    paint, end == std::string::npos ? std::string::npos : end - paint);
                return record.find("tray-total=8") != std::string::npos &&
                    record.find("tray-visible=8") != std::string::npos &&
                    record.find("selected=audio-mixer") != std::string::npos &&
                    record.find("tray-selected-visible=true") != std::string::npos;
            }), "Production catalog removal did not preserve exact compact tray focus; log=" +
                    ReadUtf8(logPath).substr(removedCatalogBefore));
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
    const auto settingsLastGood = waitForPaint(
        restartBefore, kTargets.back(), kTargets.back().id, "admitted");

    installation->BlockNextSnapshot();
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(installation->BlockedSnapshotSignal(), ignored);
            }), "Settings worker did not enter the never-completing snapshot seam.");
    const auto selectionRevokedSequence = installation->BlockedSnapshotSequence();
    const auto blockedBefore = ReadUtf8(logPath).size();
    const auto blockedNavigationStarted = std::chrono::steady_clock::now();
    SendKey(window, VK_LEFT);
    const auto spotifyWhileBlocked = waitForPaint(
        blockedBefore, kTargets[kTargets.size() - 2],
        kTargets[kTargets.size() - 2].id, "admitted");
    const auto blockedNavigationMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - blockedNavigationStarted).count());
    Require(spotifyWhileBlocked.find("semantic-focus=tray:spotify") !=
                std::string::npos,
            "Never-completing Settings snapshot disturbed ordinary tray focus.");

    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadUtf8(logPath).find(
                    "Dropped stale widget session completion for settings",
                    blockedBefore) != std::string::npos;
            }), "Selection-away did not record exact stale Settings revocation.");

    const auto workerCompletionStarted = std::chrono::steady_clock::now();
    installation->ReleaseBlockedSnapshot();
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(installation->BlockedSnapshotComplete(), ignored);
            }), "Cancellation-ignoring Settings worker did not publish its late completion.");
    const auto lateWorkerCompletionMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - workerCompletionStarted).count());

    const auto reselectionBefore = ReadUtf8(logPath).size();
    const auto blockedReselectionStarted = std::chrono::steady_clock::now();
    SendKey(window, VK_RIGHT);
    const auto settingsReselected = waitForPaint(
        reselectionBefore, kTargets.back(), kTargets.back().id, "admitted");
    const auto blockedReselectionMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - blockedReselectionStarted).count());
    const auto lastGoodBody = ParseBounds(TextField(settingsLastGood, "body-bounds="));
    const auto reselectedBody = ParseBounds(TextField(settingsReselected, "body-bounds="));
    const auto lastGoodViewport = ParseBounds(
        TextField(settingsLastGood, "viewport-bounds="));
    const auto reselectedViewport = ParseBounds(
        TextField(settingsReselected, "viewport-bounds="));
    Require(TextField(settingsReselected, "sequence=") ==
                TextField(settingsLastGood, "sequence=") &&
                reselectedBody.width == lastGoodBody.width &&
                reselectedBody.height == lastGoodBody.height &&
                reselectedViewport.width == lastGoodViewport.width &&
                reselectedViewport.height == lastGoodViewport.height,
            "Late revoked result changed retained content or extent on reselection; before=" +
                settingsLastGood + " after=" + settingsReselected);

    installation->BlockNextSnapshot();
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(installation->BlockedSnapshotSignal(), ignored);
            }), "Settings worker did not enter the close-time nonresponsive snapshot seam.");
    const auto closeRevokedSequence = installation->BlockedSnapshotSequence();
    const auto closeWhileBlockedBoundary = ReadUtf8(logPath).size();
    const auto closeStarted = std::chrono::steady_clock::now();
    SendKey(window, VK_ESCAPE);
    const auto blockedCloseDispatchMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - closeStarted).count());
    Require(blockedCloseDispatchMilliseconds <= 50,
            "Ordinary tray B dispatch exceeded 50 ms while a worker was nonresponsive; ms=" +
                std::to_string(blockedCloseDispatchMilliseconds));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) == FALSE;
            }), "Ordinary tray B did not close while a worker snapshot was nonresponsive.");
    const auto blockedCloseMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - closeStarted).count());
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadUtf8(logPath).find(
                    "Dropped stale widget session completion for settings",
                    closeWhileBlockedBoundary) != std::string::npos;
            }), "Hide/close did not revoke the nonresponsive Settings request.");
    installation->ReleaseBlockedSnapshot();

    const auto lateBoundary = ReadUtf8(logPath).size();
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(reopen after late snapshot)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) != FALSE;
            }), "Host did not reopen after cancellation-ignoring worker completion.");
    const auto settingsAfterLate = waitForPaint(
        lateBoundary, kTargets.back(), kTargets.back().id, "admitted");
    const auto afterLateBody = ParseBounds(
        TextField(settingsAfterLate, "body-bounds="));
    const auto afterLateViewport = ParseBounds(
        TextField(settingsAfterLate, "viewport-bounds="));
    const std::string revokedAdmission =
        "Widget presentation paint target=settings content=admitted "
        "rendered=settings sequence=" + std::to_string(selectionRevokedSequence);
    const std::string closeRevokedAdmission =
        "Widget presentation paint target=settings content=admitted "
        "rendered=settings sequence=" + std::to_string(closeRevokedSequence);
    const auto finalSlowWorkerLog = ReadUtf8(logPath);
    Require(finalSlowWorkerLog.find(revokedAdmission, blockedBefore) ==
                std::string::npos &&
                finalSlowWorkerLog.find(
                    closeRevokedAdmission, closeWhileBlockedBoundary) ==
                    std::string::npos &&
                TextField(settingsAfterLate, "sequence=") !=
                    std::to_string(selectionRevokedSequence) &&
                TextField(settingsAfterLate, "sequence=") !=
                    std::to_string(closeRevokedSequence) &&
                afterLateBody.width == lastGoodBody.width &&
                afterLateBody.height == lastGoodBody.height &&
                afterLateViewport.width == lastGoodViewport.width &&
                afterLateViewport.height == lastGoodViewport.height &&
                settingsAfterLate.find("semantic-focus=tray:settings") !=
                    std::string::npos,
            "Late revoked Settings sequence was admitted or changed extent/focus authority; before=" +
                settingsLastGood + " after=" + settingsAfterLate);

    std::vector<std::uint64_t> hostFocusMilliseconds =
        inputToRetainedMilliseconds;
    hostFocusMilliseconds.push_back(blockedNavigationMilliseconds);
    hostFocusMilliseconds.push_back(blockedReselectionMilliseconds);
    std::sort(hostFocusMilliseconds.begin(), hostFocusMilliseconds.end());
    const auto hostFocusP95 = hostFocusMilliseconds[
        (hostFocusMilliseconds.size() * 95 + 99) / 100 - 1];
    Require(hostFocusP95 <= 50,
            "Host focus p95 exceeded 50 ms while worker completion was isolated; p95=" +
                std::to_string(hostFocusP95));
    std::cout << "Slow-worker response timing ms host-focus-p95="
              << hostFocusP95
              << " tray-navigation=" << blockedNavigationMilliseconds
              << " tray-reselection=" << blockedReselectionMilliseconds
              << " b-dispatch=" << blockedCloseDispatchMilliseconds
              << " b-hide-complete=" << blockedCloseMilliseconds
              << " worker-late-completion=" << lateWorkerCompletionMilliseconds
              << " host-focus-samples=" << hostFocusMilliseconds.size() << '\n';

    RECT hostBounds{};
    Require(GetWindowRect(window, &hostBounds) != FALSE,
            Win32Error("GetWindowRect(composition host)"));
    const HMONITOR hostMonitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
    MONITORINFO hostMonitorInfo{sizeof(hostMonitorInfo)};
    Require(hostMonitor && GetMonitorInfoW(hostMonitor, &hostMonitorInfo) != FALSE,
            Win32Error("GetMonitorInfoW(composition host)"));
    Require(hostBounds.left >= hostMonitorInfo.rcWork.left &&
                hostBounds.top >= hostMonitorInfo.rcWork.top &&
                hostBounds.right <= hostMonitorInfo.rcWork.right &&
                hostBounds.bottom <= hostMonitorInfo.rcWork.bottom,
            "Production composition host escaped the selected monitor work area");

    const auto displayRefreshBefore = ReadUtf8(logPath).size();
    // System display messages are not a fixture protocol and may be rejected
    // across process boundaries. Use the host's existing private coalesced
    // placement refresh to force a fresh live rcWork/DPI resolve. The pure
    // placement group supplies changed taskbar, monitor, and DPI profiles.
    constexpr UINT kPlacementRefreshMessage = WM_APP + 6;
    Require(PostMessageW(window, kPlacementRefreshMessage, 0, 0) != FALSE,
            Win32Error("PostMessageW(private placement refresh)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                const auto placement = current.find(
                    "Overlay work-area placement work=", displayRefreshBefore);
                return placement != std::string::npos &&
                    current.find(" surface=widget shell=shared ", placement) !=
                        std::string::npos;
            }), "Runtime placement refresh did not re-resolve live rcWork/DPI for the shared production shell");
    Require(GetWindowRect(window, &hostBounds) != FALSE,
            Win32Error("GetWindowRect(refreshed composition host)"));
    Require(hostBounds.left >= hostMonitorInfo.rcWork.left &&
                hostBounds.top >= hostMonitorInfo.rcWork.top &&
                hostBounds.right <= hostMonitorInfo.rcWork.right &&
                hostBounds.bottom <= hostMonitorInfo.rcWork.bottom,
            "Runtime display reconciliation moved the host outside rcWork");
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
    // Queue a same-shell switch and close in order on the host thread. DLV-127
    // deliberately removes widget-to-widget shell motion, so hiding may retire
    // no in-flight transform; it must still discard geometry and future work.
    PostKey(window, VK_RIGHT);
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(close hotkey)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) == FALSE;
            }), "Composition host did not complete the bounded close");
    const auto hiddenLog = ReadUtf8(logPath);
    const auto retiredAt = hiddenLog.find(
        "Composition motion retired state=hidden", reopenBefore);
    Require(retiredAt != std::string::npos,
            "Hidden host did not retire its interrupted composition motion");
    const auto retiredEnd = hiddenLog.find('\n', retiredAt);
    const auto retiredRecord = hiddenLog.substr(
        retiredAt, retiredEnd == std::string::npos
            ? std::string::npos : retiredEnd - retiredAt);
    Require(retiredRecord.find(
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
                log.find(" surface=widget shell=shared ") != std::string::npos,
            "Production diagnostics omitted the alpha or shared-shell decision.");
    Require(log.find("Composition motion start") != std::string::npos &&
                log.find("anchor=bottom") != std::string::npos,
            "Variable widget extents omitted bottom-anchored composition continuity.");
    Require(log.find("intrinsic-passes=0 surface-axis=fillAvailable/preferred") !=
                std::string::npos,
            "Production host did not admit independent FillAvailable width without measurement.");
    Require(log.find("intrinsic-passes=1 surface-axis=preferred/content") !=
                std::string::npos,
            "Production host did not perform one Content-height intrinsic pass.");
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
    const auto maximumMotionCommit = motionCommitTimings.empty()
        ? 0ULL
        : *std::max_element(
            motionCommitTimings.begin(), motionCommitTimings.end());
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
    Require(inputToRetainedMilliseconds.size() == kTargets.size() - 1 &&
                inputToAdmittedMilliseconds.size() == kTargets.size() - 1,
            "Production temporal evidence omitted a widget switch.");
    std::cout << "Production host temporal timing ms first-admitted="
              << firstAdmittedActivationMilliseconds
              << " input-to-retained-complete-max="
              << *std::max_element(inputToRetainedMilliseconds.begin(),
                                   inputToRetainedMilliseconds.end())
              << " input-to-admitted-complete-max="
              << *std::max_element(inputToAdmittedMilliseconds.begin(),
                                   inputToAdmittedMilliseconds.end())
              << " switch-samples=" << inputToAdmittedMilliseconds.size() << '\n';
    std::vector<DWORD> observedProcessIds;
    const auto childRoles = ObservedChildRoles(host->Id(), &observedProcessIds);
    const DWORD rootProcessId = host->Id();
    observedProcessIds.push_back(rootProcessId);
    std::cout << "Production host provenance scenario=eight-widget-switch"
              << " root-pid=" << host->Id()
              << " root-start-filetime=" << ProcessStartFileTime(host->Process())
              << " profile=" << WideToUtf8(installation->ProcessProfile())
              << " commit=" << arguments.repositoryCommit
              << " host-sha256=" << arguments.hostSha256
              << " child-roles=" << childRoles << '\n';
    host.reset();
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return std::none_of(
                    observedProcessIds.begin(), observedProcessIds.end(),
                    ProcessIsRunning);
            }), "Production host job cleanup left an observed bridge/worker process running.");
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
