#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <TlHelp32.h>
#include <UIAutomation.h>
#include <Windows.h>
#include <wrl/client.h>

#include <array>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cwctype>
#include <filesystem>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace fs = std::filesystem;
using widgetrail::host_testing::Fail;
using widgetrail::host_testing::HostProcess;
using widgetrail::host_testing::JsonEscape;
using widgetrail::host_testing::LocateHostWindow;
using widgetrail::host_testing::PostKey;
using widgetrail::host_testing::ReadUtf8;
using widgetrail::host_testing::Require;
using widgetrail::host_testing::SendKey;
using widgetrail::host_testing::WaitUntil;
using widgetrail::host_testing::WideToUtf8;
using widgetrail::host_testing::Win32Error;
using widgetrail::host_testing::WriteUtf8;
using Microsoft::WRL::ComPtr;

#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "uiautomationcore.lib")

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30000;
constexpr DWORD kOperationTimeoutMilliseconds = 15000;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr wchar_t kDevelopmentNonce[] =
    L"0780780780780780780780780780780780780780780780780780780780780780";
constexpr char kDevelopmentNonceUtf8[] =
    "0780780780780780780780780780780780780780780780780780780780780780";

struct Arguments final {
    fs::path installation;
    fs::path fixtureWorker;
    std::string repositoryCommit;
    std::string hostSha256;
    fs::path durableDiagnosticsDirectory;
    fs::path fallbackAuthorityReplayLog;
    std::optional<std::size_t> fallbackAuthorityReplayMarker;
    std::optional<long long> fallbackAuthorityReplaySequence;
    bool geometryOnly{};
    bool fallbackAuthoritySelectionOnly{};
};

struct Target final {
    const wchar_t* id;
    const wchar_t* label;
    UINT key;
    std::uint32_t firstSnapshotDelayMilliseconds;
    const char* expectedCompositionContentPresentationExtent;
    const char* expectedHwndFallbackWindowExtent;
};

constexpr std::array<Target, 8> kTargets{{
    {L"audio-mixer", L"Audio Mixer", VK_RETURN, 0, "520x465", "592x698"},
    {L"wide-peer", L"Wide Peer", VK_RIGHT, 420, "980x645", "1052x878"},
    {L"now-playing", L"Now Playing", VK_RIGHT, 180, "760x425", "832x658"},
    {L"games-apps", L"Games & Apps", VK_RIGHT, 320, "820x375", "892x608"},
    // FillAvailable width is resolved from the selected monitor's live rcWork;
    // the transaction test covers the representative compact 632x878 profile.
    {L"network-controls", L"Network Controls", VK_RIGHT, 0, nullptr, nullptr},
    {L"yt-music", L"YT Music", VK_RIGHT, 240, "900x545", "972x778"},
    {L"spotify", L"Spotify", VK_RIGHT, 240, "980x505", "1052x738"},
    {L"settings", L"Settings", VK_RIGHT, 0, nullptr, nullptr},
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
            (L"wrail-widget-switch-retention-" + std::wstring(guidText));
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
        fs::create_directories(localAppData_ / L"WidgetRail");
        readyPath_ = root_ / L"host-ready.txt";
        startupSignalRoot_ = root_ / L"startup-signals";
        fs::create_directories(startupSignalRoot_);
        blockedSnapshotTrigger_ = root_ / L"block-snapshot.trigger";
        blockedSnapshotArmed_ = root_ / L"block-snapshot.armed";
        blockedSnapshotSignal_ = root_ / L"block-snapshot.started";
        blockedSnapshotRelease_ = root_ / L"block-snapshot.release";
        blockedSnapshotComplete_ = root_ / L"block-snapshot.completed";

        WriteUtf8(root_ / L"runtime" / L"switch-fixture.wrss",
            ".switch-surface { padding: 28px; gap: 18px; corner-radius: 18px; }\n"
            ".switch-title { color: #ffffff; font-size: 28px; font-weight: 700; }\n"
            ".switch-detail { color: #ffffff; font-size: 17px; }\n"
            ".switch-button { background: #f2f4f8; color: #101318; padding: 12px; }\n"
            ".audio-surface { background: #873449; }\n"
            ".network-surface { background: #225f83; }\n"
            ".spotify-surface { background: #176f3a; }\n"
            ".games-surface { background: #8a5c18; }\n"
            ".wide-peer-surface { background: #54418a; }\n"
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
                    "\",\"--block-snapshot-armed\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotArmed_).wstring()) +
                    "\",\"--block-snapshot-signal\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotSignal_).wstring()) +
                    "\",\"--block-snapshot-release\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotRelease_).wstring()) +
                    "\",\"--block-snapshot-complete\",\"" +
                    JsonEscape(fs::absolute(blockedSnapshotComplete_).wstring()) + "\""
                : "";
            return std::string(
                "    {\"id\":\"") + id + "\",\"packageId\":\"" + packageId +
                "\",\"publisherId\":\"widgetrail.tests\",\"name\":\"" + name +
                "\",\"instanceId\":\"" + instanceId + "\",\"icon\":\"" + icon +
                "\",\"workerExecutable\":\"" + worker +
                "\",\"styleFile\":\"runtime/switch-fixture.wrss\","
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
            widget("audio-mixer", "widgetrail.tests.audio", "Audio Mixer",
                   "audio-mixer.default", "volume") + ",\n" +
            widget("wide-peer", "widgetrail.tests.wide-peer", "Wide Peer",
                   "wide-peer.default", "play") + ",\n" +
            widget("now-playing", "widgetrail.tests.now-playing", "Now Playing",
                   "now-playing.default", "music") + ",\n" +
            widget("games-apps", "widgetrail.tests.games", "Games & Apps",
                   "games-apps.default", "play") + ",\n" +
            widget("network-controls", "widgetrail.tests.network", "Network Controls",
                   "network-controls.default", "wifi") + ",\n" +
            widget("yt-music", "widgetrail.tests.yt-music", "YT Music",
                   "yt-music.default", "music") + ",\n" +
            widget("spotify", "widgetrail.tests.spotify", "Spotify",
                   "spotify.default", "music") + ",\n" +
            widget("settings", "widgetrail.tests.settings", "Settings",
                   "settings.default", "settings", true) +
            "\n  ],\n  \"bundledWidgets\":[]\n}\n";
        catalogWithProbe_ =
            catalog_.substr(0, catalog_.find("\n  ],\n")) + ",\n" +
            widget("catalog-probe", "widgetrail.tests.catalog-probe", "Catalog Probe",
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
    [[nodiscard]] long long BlockNextSnapshot() {
        std::error_code ignored;
        fs::remove(blockedSnapshotArmed_, ignored);
        fs::remove(blockedSnapshotSignal_, ignored);
        fs::remove(blockedSnapshotRelease_, ignored);
        fs::remove(blockedSnapshotComplete_, ignored);
        const auto epoch = ++blockedSnapshotEpoch_;
        WriteUtf8(blockedSnapshotTrigger_, "epoch=" + std::to_string(epoch));
        return epoch;
    }
    void RetryBlockedSnapshot(const long long epoch) const {
        WriteUtf8(blockedSnapshotTrigger_, "epoch=" + std::to_string(epoch));
    }
    void ReleaseBlockedSnapshot(const long long epoch) const {
        WriteUtf8(blockedSnapshotRelease_, "epoch=" + std::to_string(epoch));
    }
    [[nodiscard]] std::size_t BlockedSnapshotArmedCount(const long long epoch) const {
        std::error_code ignored;
        if (!fs::exists(blockedSnapshotArmed_, ignored)) return 0;
        const std::string needle = "epoch=" + std::to_string(epoch) + " armed";
        const auto acknowledgements = ReadUtf8(blockedSnapshotArmed_);
        std::size_t count{};
        std::size_t at{};
        while ((at = acknowledgements.find(needle, at)) != std::string::npos) {
            ++count;
            at += needle.size();
        }
        return count;
    }
    [[nodiscard]] const fs::path& BlockedSnapshotSignal() const noexcept {
        return blockedSnapshotSignal_;
    }
    [[nodiscard]] const fs::path& BlockedSnapshotComplete() const noexcept {
        return blockedSnapshotComplete_;
    }
    [[nodiscard]] long long BlockedSnapshotSequence(const long long epoch) const {
        const auto signal = ReadUtf8(blockedSnapshotSignal_);
        const auto epochStart = signal.find("epoch=");
        const auto sequenceStart = signal.find("render-sequence=");
        Require(epochStart != std::string::npos && sequenceStart != std::string::npos,
                "Blocked snapshot start omitted epoch or render sequence; signal=" + signal);
        const auto epochValueStart = epochStart + std::string_view("epoch=").size();
        const auto epochValueEnd = signal.find(' ', epochValueStart);
        Require(signal.substr(epochValueStart, epochValueEnd - epochValueStart) ==
                    std::to_string(epoch),
                "Blocked snapshot start acknowledged a different epoch; signal=" + signal);
        const auto sequenceValueStart = sequenceStart + std::string_view("render-sequence=").size();
        const auto sequenceValueEnd = signal.find(' ', sequenceValueStart);
        const auto sequenceText = signal.substr(
            sequenceValueStart, sequenceValueEnd - sequenceValueStart);
        const auto sequence = std::stoll(sequenceText);
        Require(sequence > 0,
                "Blocked snapshot start omitted its exact positive render sequence; signal=" + signal);
        return sequence;
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
    fs::path blockedSnapshotArmed_;
    fs::path blockedSnapshotSignal_;
    fs::path blockedSnapshotRelease_;
    fs::path blockedSnapshotComplete_;
    long long blockedSnapshotEpoch_{};
    std::wstring processProfile_;
    std::string catalog_;
    std::string catalogWithProbe_;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if (argument == L"--geometry-only") {
            result.geometryOnly = true;
        } else if (argument == L"--fallback-authority-selection-only") {
            result.fallbackAuthoritySelectionOnly = true;
        } else if (argument == L"--fallback-authority-replay-log" && index + 1 < argc) {
            result.fallbackAuthorityReplayLog = argv[++index];
        } else if ((argument == L"--fallback-authority-marker" ||
                    argument == L"--fallback-authority-sequence") && index + 1 < argc) {
            const auto value = WideToUtf8(argv[++index]);
            try {
                std::size_t consumed{};
                const auto parsed = std::stoll(value, &consumed);
                Require(consumed == value.size() && parsed > 0,
                        "Fallback authority replay input must be positive.");
                if (argument == L"--fallback-authority-marker")
                    result.fallbackAuthorityReplayMarker =
                        static_cast<std::size_t>(parsed);
                else
                    result.fallbackAuthorityReplaySequence = parsed;
            } catch (...) {
                Fail("Fallback authority replay input was not a positive integer.");
            }
        } else if ((argument == L"--installation" || argument == L"--fixture-worker" ||
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
                 "--host-sha256 <sha256> [--geometry-only] "
                 "[--fallback-authority-selection-only] "
                 "[--fallback-authority-replay-log <path> "
                 "--fallback-authority-marker <offset> "
                 "--fallback-authority-sequence <sequence>]");
        }
    }
    const bool replayRequested = !result.fallbackAuthorityReplayLog.empty() ||
        result.fallbackAuthorityReplayMarker.has_value() ||
        result.fallbackAuthorityReplaySequence.has_value();
    if (replayRequested) {
        Require(!result.fallbackAuthoritySelectionOnly &&
                    !result.fallbackAuthorityReplayLog.empty() &&
                    result.fallbackAuthorityReplayMarker.has_value() &&
                    result.fallbackAuthorityReplaySequence.has_value(),
                "Fallback authority replay requires exactly log path, marker, and sequence.");
    }
    if (!result.fallbackAuthoritySelectionOnly && !replayRequested) {
        Require(!result.installation.empty() && !result.fixtureWorker.empty() &&
                    !result.repositoryCommit.empty() && !result.hostSha256.empty(),
                "Installation, fixture worker, repository commit, and host SHA-256 are required.");
    }
    const DWORD diagnosticsLength = GetEnvironmentVariableW(
        L"WRAIL_WIDGET_SWITCH_DIAGNOSTICS_DIR", nullptr, 0);
    if (diagnosticsLength != 0) {
        std::vector<wchar_t> diagnostics(diagnosticsLength);
        Require(GetEnvironmentVariableW(
                    L"WRAIL_WIDGET_SWITCH_DIAGNOSTICS_DIR", diagnostics.data(),
                    static_cast<DWORD>(diagnostics.size())) != 0,
                Win32Error("GetEnvironmentVariableW(WRAIL_WIDGET_SWITCH_DIAGNOSTICS_DIR)"));
        result.durableDiagnosticsDirectory = diagnostics.data();
    }
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

std::vector<DWORD> DirectChildProcessIds(
    const DWORD parentProcessId,
    const wchar_t* executableName) {
    std::vector<DWORD> result;
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    Require(snapshot != INVALID_HANDLE_VALUE,
            Win32Error("CreateToolhelp32Snapshot(direct children)"));
    PROCESSENTRY32W entry{sizeof(entry)};
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (entry.th32ParentProcessID == parentProcessId &&
                _wcsicmp(entry.szExeFile, executableName) == 0)
                result.push_back(entry.th32ProcessID);
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
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

std::optional<long long> ParsePositiveSequence(const std::string_view value) {
    if (value.empty()) return std::nullopt;
    try {
        std::size_t consumed{};
        const auto parsed = std::stoll(std::string(value), &consumed);
        return consumed == value.size() && parsed > 0
            ? std::optional<long long>{parsed}
            : std::nullopt;
    } catch (...) {
        return std::nullopt;
    }
}

struct FallbackCheckpointSelection final {
    std::optional<std::size_t> offset;
    std::size_t candidates{};
    std::size_t checkpointCandidates{};
    std::size_t currentWidgetCandidates{};
};

std::string_view RecordLine(const std::string_view text, const std::size_t start) {
    auto end = text.find('\n', start);
    if (end == std::string::npos) end = text.size();
    if (end > start && text[end - 1] == '\r') --end;
    return text.substr(start, end - start);
}

std::string_view RecordContainingLine(
    const std::string_view text, const std::size_t position) {
    const auto priorEnd = text.rfind('\n', position);
    return RecordLine(text, priorEnd == std::string_view::npos ? 0 : priorEnd + 1);
}

FallbackCheckpointSelection SelectCurrentFallbackCheckpoint(
    const std::string_view log,
    const std::size_t after,
    const std::string_view expectedWidget,
    const long long expectedSequence) {
    constexpr std::string_view needle = "Fallback placement mode=hwnd-fallback";
    FallbackCheckpointSelection result;
    for (std::size_t searchAfter = after + 1;;) {
        const auto candidateAt = log.find(needle, searchAfter);
        if (candidateAt == std::string::npos) return result;
        const auto candidateEnd = log.find('\n', candidateAt);
        const auto candidate = RecordLine(log, candidateAt);
        ++result.candidates;
        const auto phase = TextField(candidate, "phase=");
        if (phase == "presentation-checkpoint") ++result.checkpointCandidates;
        const auto sequence = ParsePositiveSequence(TextField(candidate, "sequence="));
        const bool currentWidgetAuthority =
            TextField(candidate, "widget=") == expectedWidget &&
            TextField(candidate, "instance=") != "none" &&
            TextField(candidate, "runtime=") != "none" &&
            TextField(candidate, "presentation=") != "none" &&
            sequence.has_value() && *sequence == expectedSequence;
        if (currentWidgetAuthority) ++result.currentWidgetCandidates;
        if (phase == "presentation-checkpoint" && currentWidgetAuthority) {
            result.offset = candidateAt;
            return result;
        }
        searchAfter = candidateEnd == std::string::npos
            ? log.size() : candidateEnd + 1;
        if (searchAfter >= log.size()) return result;
    }
}

std::string DescribeFallbackCheckpointSelection(
    const FallbackCheckpointSelection& selection,
    const std::size_t marker,
    const std::string_view expectedWidget,
    const long long expectedSequence) {
    return " marker-offset=" + std::to_string(marker) +
        " expected-widget=" + std::string(expectedWidget) +
        " expected-sequence=" + std::to_string(expectedSequence) +
        " candidates=" + std::to_string(selection.candidates) +
        " checkpoint-candidates=" + std::to_string(selection.checkpointCandidates) +
        " current-widget-candidates=" + std::to_string(selection.currentWidgetCandidates) +
        " selected=" + (selection.offset ? std::to_string(*selection.offset) : "none");
}

struct SettingsFallbackCorrelation final {
    std::optional<std::size_t> paintOffset;
    std::optional<std::size_t> checkpointOffset;
    std::string diagnostics;
};

SettingsFallbackCorrelation CorrelateLatestSettingsFallbackPresentation(
    const std::string_view log, const std::size_t after,
    const std::string_view liveWindow, const std::string_view liveClient) {
    SettingsFallbackCorrelation result;
    constexpr std::string_view resizeMarker = "Overlay render target resized in place";
    constexpr std::string_view setWindowMarker =
        "Fallback placement mode=hwnd-fallback phase=set-window-pos";
    const auto resizeAt = log.find(resizeMarker, after);
    const auto setWindowAt = log.find(setWindowMarker, after);
    const auto geometryAt = resizeAt == std::string::npos
        ? setWindowAt
        : setWindowAt == std::string::npos
            ? resizeAt
            : std::min(resizeAt, setWindowAt);
    if (geometryAt == std::string::npos) {
        result.diagnostics = " geometry-boundary=none";
        return result;
    }
    result.diagnostics = " live-window=" + std::string(liveWindow) +
        " live-client=" + std::string(liveClient) +
        " geometry-boundary=" +
        std::string(geometryAt == resizeAt ? "resize@" : "set-window-pos@") +
        std::to_string(geometryAt);
    const auto geometryEnd = log.find('\n', geometryAt);
    if (geometryEnd == std::string::npos) {
        result.diagnostics += " truncated";
        return result;
    }
    const std::array<std::string_view, 4> correlationMarkers{{
        "Widget presentation extent refresh",
        resizeMarker,
        setWindowMarker,
        "Widget presentation paint target=settings",
    }};
    const auto hasSupersedingBetween = [&](const std::size_t begin,
                                           const std::size_t end) {
        for (const auto marker : correlationMarkers) {
            const auto at = log.find(marker, begin);
            if (at != std::string::npos && at < end) return true;
        }
        return false;
    };
    for (std::size_t paintAt = geometryEnd + 1;;) {
        paintAt = log.find("Widget presentation paint target=settings", paintAt);
        if (paintAt == std::string::npos) break;
        const auto paint = RecordLine(log, paintAt);
        const auto sequence = ParsePositiveSequence(TextField(paint, "sequence="));
        const auto content = TextField(paint, "content=");
        result.diagnostics += " paint@" + std::to_string(paintAt) +
            " content=" + content + " sequence=" + TextField(paint, "sequence=") +
            " desired=" + TextField(paint, "desired-extent=") +
            " presented=" + TextField(paint, "presented-extent=");
        if (sequence && TextField(paint, "desired-extent=") == TextField(paint, "presented-extent=")) {
            auto checkpoint = SelectCurrentFallbackCheckpoint(
                log, paintAt + paint.size(), "settings", *sequence);
            if (checkpoint.offset && hasSupersedingBetween(
                    paintAt + paint.size(), *checkpoint.offset))
                checkpoint.offset.reset();
            if (!checkpoint.offset) {
                const auto precedingAt = log.rfind(
                    "Fallback placement mode=hwnd-fallback phase=", paintAt);
                if (precedingAt != std::string::npos && precedingAt >= geometryEnd + 1) {
                    const auto preceding = RecordLine(log, precedingAt);
                    const auto precedingSequence = ParsePositiveSequence(
                        TextField(preceding, "sequence="));
                    if (TextField(preceding, "phase=") == "presentation-checkpoint" &&
                        TextField(preceding, "widget=") == "settings" &&
                        TextField(preceding, "instance=") == "settings.default" &&
                        !TextField(preceding, "runtime=").empty() &&
                        !TextField(preceding, "presentation=").empty() &&
                        precedingSequence == sequence &&
                        !hasSupersedingBetween(
                            precedingAt + preceding.size(), paintAt))
                        checkpoint.offset = precedingAt;
                }
            }
            if (checkpoint.offset) {
                const auto record = RecordLine(log, *checkpoint.offset);
                const auto window = TextField(record, "content-window=");
                const auto client = TextField(record, "content-client-screen=");
                result.diagnostics += " checkpoint@" + std::to_string(*checkpoint.offset) +
                    " window=" + window + " client=" + client;
                result.diagnostics += " exact-window=" +
                    std::string(window == liveWindow ? "true" : "false") +
                    " exact-client=" +
                    std::string(client == liveClient ? "true" : "false");
                if (TextField(record, "instance=") == "settings.default" &&
                    !TextField(record, "runtime=").empty() &&
                    !TextField(record, "presentation=").empty() &&
                    window == liveWindow && client == liveClient) {
                    result.paintOffset = paintAt;
                    result.checkpointOffset = *checkpoint.offset;
                }
            }
        }
        paintAt += paint.size();
    }
    if (!result.checkpointOffset) return result;
    const auto checkpoint = RecordLine(log, *result.checkpointOffset);
    const auto checkpointEnd = std::max(
        *result.checkpointOffset + checkpoint.size(),
        *result.paintOffset + RecordLine(log, *result.paintOffset).size());
    constexpr std::array<std::string_view, 3> geometrySupersedingMarkers{{
        "Widget presentation extent refresh",
        resizeMarker,
        setWindowMarker,
    }};
    for (const auto marker : geometrySupersedingMarkers) {
        const auto supersedingAt = log.find(marker, checkpointEnd);
        if (supersedingAt == std::string::npos) continue;
        result.diagnostics += " superseded@" + std::to_string(supersedingAt) +
            " marker=" + std::string(marker);
        result.paintOffset.reset();
        result.checkpointOffset.reset();
        return result;
    }
    result.diagnostics += " terminal";
    return result;
}

void RunFallbackCheckpointSelectionTests() {
    const auto record = [](const std::string_view phase, const std::string_view widget,
                           const std::string_view instance, const std::string_view runtime,
                           const std::string_view presentation, const long long sequence) {
        return "Fallback placement mode=hwnd-fallback phase=" + std::string(phase) +
            " widget=" + std::string(widget) + " instance=" + std::string(instance) +
            " runtime=" + std::string(runtime) + " presentation=" + std::string(presentation) +
            " sequence=" + std::to_string(sequence) + "\n";
    };
    const std::string marker = "DirectComposition presentation disabled; using HWND fallback:\n";
    struct TableCase final {
        const char* name;
        std::string log;
        std::string expectedWidget;
        bool expectsMatch;
    };
    auto crlfTerminalRecord = record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 7);
    crlfTerminalRecord.insert(crlfTerminalRecord.size() - 1, "\r");
    const std::array<TableCase, 8> cases{{
        {"pre-marker", record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 7) + marker, "audio-mixer", false},
        {"wrong-phase", marker + record("set-window-pos", "audio-mixer", "i", "r", "p", 7), "audio-mixer", false},
        {"stale-identity", marker + record("presentation-checkpoint", "audio-mixer", "none", "r", "p", 7), "audio-mixer", false},
        {"stale-sequence", marker + record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 6), "audio-mixer", false},
        {"first-exact-current", marker +
            record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 6) +
            record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 7) +
            record("presentation-checkpoint", "audio-mixer", "i", "r", "p", 7), "audio-mixer", true},
        {"windows-crlf-terminal-sequence", marker + crlfTerminalRecord, "audio-mixer", true},
        {"no-match", marker + record("composition-transition", "settings", "i", "r", "p", 7), "audio-mixer", false},
        {"settings-skips-placement-and-wrong-sequence", marker +
            record("set-window-pos", "settings", "settings.default", "r", "p", 7) +
            record("presentation-checkpoint", "settings", "settings.default", "r", "p", 6) +
            record("presentation-checkpoint", "settings", "settings.default", "r", "p", 7), "settings", true},
    }};
    for (const auto& testCase : cases) {
        const auto& [name, log, expectedWidget, expectedMatch] = testCase;
        const auto markerAt = log.find(marker);
        Require(markerAt != std::string::npos, "Fallback selector case omitted marker");
        const auto selection = SelectCurrentFallbackCheckpoint(log, markerAt, expectedWidget, 7);
        Require(selection.offset.has_value() == expectedMatch,
                "Fallback selector case failed name=" + std::string(name) +
                DescribeFallbackCheckpointSelection(selection, markerAt, expectedWidget, 7));
        if (expectedMatch) {
            const auto selected = RecordLine(log, *selection.offset);
            Require(TextField(selected, "sequence=") == "7",
                    "Fallback selector did not select the first exact-current checkpoint" +
                        DescribeFallbackCheckpointSelection(selection, markerAt, expectedWidget, 7));
            if (std::string_view(name) == "first-exact-current") {
                Require(selection.candidates == 2 &&
                            selection.checkpointCandidates == 2 &&
                            selection.currentWidgetCandidates == 1,
                        "Fallback selector did not stop at the first exact-current checkpoint" +
                            DescribeFallbackCheckpointSelection(selection, markerAt, expectedWidget, 7));
            }
        }
    }
    const std::string geometryLog = marker +
        "Widget presentation paint target=settings content=admitted sequence=7 desired-extent=772x828 presented-extent=772x828\n" +
        "Fallback placement mode=hwnd-fallback phase=presentation-checkpoint widget=settings instance=settings.default runtime=r presentation=p sequence=7 content-window=2077,702,965,698 content-client-screen=2077,702,965,698\n" +
        "Overlay render target resized in place width=965 height=1035\n" +
        record("set-window-pos", "settings", "settings.default", "r", "p", 7) +
        record("presentation-checkpoint", "settings", "settings.default", "r", "p", 6) +
        "Widget presentation paint target=settings content=refresh-retained sequence=7 desired-extent=772x828 presented-extent=772x828\n" +
        "Fallback placement mode=hwnd-fallback phase=presentation-checkpoint widget=settings instance=settings.default runtime=r presentation=p sequence=7 content-window=2077,365,965,1035 content-client-screen=2077,365,965,1035\n";
    const auto geometry = CorrelateLatestSettingsFallbackPresentation(
        geometryLog, marker.size(), "2077,365,965,1035", "2077,365,965,1035");
    Require(geometry.paintOffset && geometry.checkpointOffset &&
                RecordLine(geometryLog, *geometry.paintOffset).find("content=refresh-retained") !=
                    std::string_view::npos,
            "Fallback geometry correlation did not select the latest matching retained paint;" +
                geometry.diagnostics);
    const std::string splitAuthorityLog = geometryLog +
        "Widget presentation paint target=settings content=admitted sequence=8 desired-extent=772x558 presented-extent=772x828\n" +
        "Fallback placement mode=hwnd-fallback phase=presentation-checkpoint widget=settings instance=settings.default runtime=r presentation=p sequence=8 content-window=2077,365,965,1035 content-client-screen=2077,365,965,1035\n";
    const auto splitAuthority = CorrelateLatestSettingsFallbackPresentation(
        splitAuthorityLog, marker.size(), "2077,365,965,1035", "2077,365,965,1035");
    Require(splitAuthority.paintOffset && splitAuthority.checkpointOffset &&
                RecordLine(splitAuthorityLog, *splitAuthority.paintOffset).find(
                    "content=refresh-retained sequence=7") != std::string_view::npos,
            "Fallback geometry correlation let a later semantic-only paint erase exact settled geometry;" +
                splitAuthority.diagnostics);
    const std::string stableWindowLog = marker +
        record("set-window-pos", "settings", "settings.default", "r", "p", 7) +
        "Widget presentation paint target=settings content=refresh-retained sequence=7 desired-extent=772x828 presented-extent=772x828\n" +
        "Fallback placement mode=hwnd-fallback phase=presentation-checkpoint widget=settings instance=settings.default runtime=r presentation=p sequence=7 content-window=2077,365,965,1035 content-client-screen=2077,365,965,1035\n";
    const auto stableWindow = CorrelateLatestSettingsFallbackPresentation(
        stableWindowLog, marker.size(), "2077,365,965,1035", "2077,365,965,1035");
    Require(stableWindow.paintOffset && stableWindow.checkpointOffset,
            "Fallback geometry correlation rejected a fresh exact set-window-pos transaction when no resize was required;" +
                stableWindow.diagnostics);
    std::cout << "WidgetSwitch fallback-authority selector table cases=8 passed\n";
}

void RunFallbackCheckpointReplay(const Arguments& arguments) {
    Require(arguments.fallbackAuthorityReplayMarker.has_value() &&
                arguments.fallbackAuthorityReplaySequence.has_value(),
            "Fallback authority replay inputs were not admitted.");
    const auto log = ReadUtf8(arguments.fallbackAuthorityReplayLog);
    const auto selection = SelectCurrentFallbackCheckpoint(
        log, *arguments.fallbackAuthorityReplayMarker, "audio-mixer",
        *arguments.fallbackAuthorityReplaySequence);
    Require(selection.offset.has_value(),
            "Fallback authority replay selected no exact-current checkpoint;" +
            DescribeFallbackCheckpointSelection(
                selection, *arguments.fallbackAuthorityReplayMarker, "audio-mixer",
                *arguments.fallbackAuthorityReplaySequence));
    std::cout << "WidgetSwitch fallback-authority replay selected-offset=" <<
        *selection.offset << DescribeFallbackCheckpointSelection(
            selection, *arguments.fallbackAuthorityReplayMarker, "audio-mixer",
            *arguments.fallbackAuthorityReplaySequence) << '\n';
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

std::optional<std::uint64_t> DiagnosticTimestampMilliseconds(
    const std::string_view record) {
    int year{}, month{}, day{}, hour{}, minute{}, second{}, millisecond{};
    const std::string text(record);
    if (sscanf_s(
            text.c_str(), "%d-%d-%d %d:%d:%d.%d",
            &year, &month, &day, &hour, &minute, &second, &millisecond) != 7 ||
        year < 2000 || year > 65535 || month < 1 || month > 12 || day < 1 || day > 31 ||
        hour < 0 || hour > 23 || minute < 0 || minute > 59 ||
        second < 0 || second > 59 || millisecond < 0 || millisecond > 999) {
        return std::nullopt;
    }
    SYSTEMTIME timestamp{};
    timestamp.wYear = static_cast<WORD>(year);
    timestamp.wMonth = static_cast<WORD>(month);
    timestamp.wDay = static_cast<WORD>(day);
    timestamp.wHour = static_cast<WORD>(hour);
    timestamp.wMinute = static_cast<WORD>(minute);
    timestamp.wSecond = static_cast<WORD>(second);
    timestamp.wMilliseconds = static_cast<WORD>(millisecond);
    FILETIME fileTime{};
    if (!SystemTimeToFileTime(&timestamp, &fileTime)) return std::nullopt;
    ULARGE_INTEGER ticks{};
    ticks.LowPart = fileTime.dwLowDateTime;
    ticks.HighPart = fileTime.dwHighDateTime;
    return ticks.QuadPart / 10000;
}

std::uint64_t DiagnosticLatencyMilliseconds(
    const std::string_view startRecord,
    const std::string_view endRecord,
    const std::string_view context) {
    const auto start = DiagnosticTimestampMilliseconds(startRecord);
    const auto end = DiagnosticTimestampMilliseconds(endRecord);
    Require(start && end,
            std::string(context) + " omitted valid host diagnostic timestamps; start=" +
                std::string(startRecord) + " end=" + std::string(endRecord));
    return *end >= *start ? *end - *start : *start - *end;
}

void RunDiagnosticLatencyTableTests() {
    struct Case final {
        const char* name;
        const char* start;
        const char* end;
        std::uint64_t expectedMilliseconds;
    };
    constexpr std::array cases{
        Case{"forward-order", "2026-8-22 17:49:37.053", "2026-8-22 17:49:37.059", 6},
        Case{"reversed-append-order", "2026-8-22 17:49:37.059", "2026-8-22 17:49:37.053", 6},
        Case{"calendar-boundary", "2026-12-31 23:59:59.990", "2027-1-1 0:0:0.010", 20},
    };
    for (const auto& test : cases) {
        const auto actual = DiagnosticLatencyMilliseconds(test.start, test.end, test.name);
        Require(actual == test.expectedMilliseconds,
                std::string("Diagnostic latency table case failed: ") + test.name +
                    " expected=" + std::to_string(test.expectedMilliseconds) +
                    " actual=" + std::to_string(actual));
    }
    std::cout << "WidgetSwitch diagnostic latency table cases=" << cases.size() << " passed\n";
}

std::optional<double> InclusiveLinearPercentile(
    std::vector<double> samples,
    const double percentile) {
    if (samples.empty() || !std::isfinite(percentile) || percentile < 0.0 ||
        percentile > 1.0 || std::any_of(samples.begin(), samples.end(), [](const double sample) {
            return !std::isfinite(sample);
        })) {
        return std::nullopt;
    }
    std::sort(samples.begin(), samples.end());
    const double position = percentile * static_cast<double>(samples.size() - 1);
    const auto lower = static_cast<std::size_t>(std::floor(position));
    const auto upper = static_cast<std::size_t>(std::ceil(position));
    const double fraction = position - static_cast<double>(lower);
    return samples[lower] + (samples[upper] - samples[lower]) * fraction;
}

void RunInclusivePercentileTableTests() {
    const auto isolatedOutlier = InclusiveLinearPercentile(
        {0, 2, 2, 2, 2, 2, 2, 7, 54}, 0.95);
    Require(isolatedOutlier && *isolatedOutlier < 50.0,
            "Inclusive p95 table did not keep the isolated-outlier distribution below 50 ms");
    const auto sustainedSlowTail = InclusiveLinearPercentile(
        {0, 2, 2, 2, 2, 2, 54, 54, 54}, 0.95);
    Require(sustainedSlowTail && *sustainedSlowTail > 50.0,
            "Inclusive p95 table did not keep a sustained slow tail above 50 ms");
    Require(!InclusiveLinearPercentile({}, 0.95),
            "Inclusive p95 table admitted an empty sample set");
    Require(!InclusiveLinearPercentile(
                {0, std::numeric_limits<double>::quiet_NaN()}, 0.95),
            "Inclusive p95 table admitted a non-finite sample");
    std::cout << "WidgetSwitch inclusive percentile table cases=4 passed\n";
}

ComPtr<IUIAutomationElement> FindAutomationElement(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const wchar_t* automationId) {
    if (!automation || !root) return {};
    VARIANT value;
    VariantInit(&value);
    value.vt = VT_BSTR;
    value.bstrVal = SysAllocString(automationId);
    if (!value.bstrVal) return {};
    ComPtr<IUIAutomationCondition> condition;
    const HRESULT conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, value, condition.GetAddressOf());
    VariantClear(&value);
    if (FAILED(conditionResult) || !condition) return {};
    ComPtr<IUIAutomationElement> result;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), result.GetAddressOf()))) {
        return {};
    }
    return result;
}

bool QueryAutomationId(
    IUIAutomation* automation,
    const HWND window,
    const wchar_t* automationId,
    bool& found) {
    found = false;
    ComPtr<IUIAutomationElement> root;
    if (!automation || FAILED(automation->ElementFromHandle(
            window, root.GetAddressOf())) || !root) {
        return false;
    }
    found = static_cast<bool>(FindAutomationElement(
        automation, root.Get(), automationId));
    return true;
}

bool IsKeyboardFocused(IUIAutomationElement* element) {
    BOOL focused{};
    return element &&
        SUCCEEDED(element->get_CurrentHasKeyboardFocus(&focused)) && focused;
}

bool IsSelectionItemSelected(IUIAutomationElement* element) {
    VARIANT selected{};
    const bool result = element &&
        SUCCEEDED(element->GetCurrentPropertyValue(
            UIA_SelectionItemIsSelectedPropertyId, &selected)) &&
        V_VT(&selected) == VT_BOOL && V_BOOL(&selected) == VARIANT_TRUE;
    VariantClear(&selected);
    return result;
}

bool IsEnabled(IUIAutomationElement* element) {
    BOOL enabled{};
    return element &&
        SUCCEEDED(element->get_CurrentIsEnabled(&enabled)) && enabled;
}

std::string AutomationIdOf(IUIAutomationElement* element) {
    BSTR value{};
    if (!element || FAILED(element->get_CurrentAutomationId(&value)) || !value)
        return {};
    const std::string result = WideToUtf8(std::wstring_view(value, SysStringLen(value)));
    SysFreeString(value);
    return result;
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
    std::vector<DWORD> recoveryProcessIds;
    const auto logPath = installation->LocalAppData() /
        L"WidgetRail" / L"overlay.log";
    const bool ready = WaitUntil(kStartupTimeoutMilliseconds, [&] {
        return ReadUtf8(installation->ReadyPath()).find(kDevelopmentNonceUtf8) !=
            std::string::npos;
    });
    if (!ready) {
        DWORD exitCode = STILL_ACTIVE;
        GetExitCodeProcess(host->Process(), &exitCode);
        Fail("Production host did not publish authenticated development readiness; exit=" +
             std::to_string(exitCode) + " log=" + ReadUtf8(logPath) +
             " startup-error=" +
             ReadUtf8(installation->LocalAppData() /
                 L"WidgetRail" / L"startup-error.log"));
    }
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
    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
            "UI Automation initializes for retained-authority verification");
    const auto waitForWidgetAutomation = [&](const wchar_t* automationId,
                                              const bool expected) {
        return WaitUntil(kOperationTimeoutMilliseconds, [&] {
            bool found{};
            return QueryAutomationId(
                       automation.Get(), window, automationId, found) &&
                found == expected;
        });
    };
    const auto invokeCurrentSettingsReady = [&]() {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "Current content HWND did not resolve before invoking fixture Ready");
        ComPtr<IUIAutomationElement> ready = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"widget:settings-ready");
        Require(ready && IsEnabled(ready.Get()),
                "Current Settings fixture Ready UIA node was absent or disabled");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(ready->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Current Settings fixture Ready UIA node did not expose InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Current Settings fixture Ready UIA InvokePattern invocation failed");
    };
    const auto retainBlockedSnapshotFailure = [&](const long long epoch,
                                                  const std::size_t triggerBoundary) {
        std::string retainedLog = "not-supplied";
        if (!arguments.durableDiagnosticsDirectory.empty()) {
            std::error_code error;
            fs::create_directories(arguments.durableDiagnosticsDirectory, error);
            const auto destination = arguments.durableDiagnosticsDirectory /
                (L"WidgetSwitchHostTests-blocked-snapshot-" + std::to_wstring(epoch) +
                 L"-overlay.log");
            if (!error) {
                fs::copy_file(logPath, destination, fs::copy_options::overwrite_existing, error);
                if (!error) retainedLog = destination.string();
            }
        }
        const auto log = ReadUtf8(logPath);
        const auto postTrigger = log.substr(std::min(triggerBoundary, log.size()));
        std::string lifecycleAndRequests;
        std::size_t cursor{};
        std::size_t records{};
        while (cursor < postTrigger.size() && records < 12) {
            const auto end = postTrigger.find('\n', cursor);
            const auto line = postTrigger.substr(
                cursor, end == std::string::npos ? std::string::npos : end - cursor);
            if (line.find("Widget lifetime") != std::string::npos ||
                line.find("Admission trace") != std::string::npos) {
                if (!lifecycleAndRequests.empty()) lifecycleAndRequests += " | ";
                lifecycleAndRequests += line;
                ++records;
            }
            cursor = end == std::string::npos ? postTrigger.size() : end + 1;
        }
        return " post-trigger-lifecycle-request-summary=" + lifecycleAndRequests +
            " retained-overlay-log=" + retainedLog;
    };
    const auto blockSnapshot = [&]() {
        const auto triggerBoundary = ReadUtf8(logPath).size();
        const auto epoch = installation->BlockNextSnapshot();
        invokeCurrentSettingsReady();
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return installation->BlockedSnapshotArmedCount(epoch) >= 1;
                }),
                "Settings worker did not acknowledge armed epoch=" +
                    std::to_string(epoch));
        if (!WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(installation->BlockedSnapshotSignal(), ignored);
            })) {
            installation->RetryBlockedSnapshot(epoch);
            invokeCurrentSettingsReady();
            Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                        return installation->BlockedSnapshotArmedCount(epoch) >= 2;
                    }),
                    "Settings worker did not acknowledge same-epoch re-invalidation; epoch=" +
                        std::to_string(epoch) + " acknowledgements=" +
                        std::to_string(installation->BlockedSnapshotArmedCount(epoch)));
            Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                        std::error_code ignored;
                        return fs::exists(installation->BlockedSnapshotSignal(), ignored);
                    }),
                    "Settings worker armed but never started exact epoch=" +
                        std::to_string(epoch) + " acknowledgements=" +
                        std::to_string(installation->BlockedSnapshotArmedCount(epoch)) +
                        retainBlockedSnapshotFailure(epoch, triggerBoundary));
        }
        return epoch;
    };
    std::vector<std::uint64_t> drawTimings;
    std::vector<std::uint64_t> commitTimings;
    std::vector<std::uint64_t> geometryTimings;
    std::vector<std::uint64_t> motionCommitTimings;
    std::vector<std::uint64_t> inputToRetainedMilliseconds;
    std::vector<std::uint64_t> inputToAdmittedMilliseconds;
    std::uint64_t firstAdmittedActivationMilliseconds{};
    const auto CurrentPresentationMode = [](const std::string_view log) -> std::optional<bool> {
        constexpr std::string_view active =
            "DirectComposition complete-content presentation owner active";
        constexpr std::string_view disabled =
            "DirectComposition presentation disabled; using HWND fallback:";
        constexpr std::string_view unavailable =
            "DirectComposition unavailable; retaining HWND render-target fallback:";
        const auto activeAt = log.rfind(active);
        const auto disabledAt = log.rfind(disabled);
        const auto unavailableAt = log.rfind(unavailable);
        const auto fallbackAt = disabledAt == std::string::npos ? unavailableAt :
            unavailableAt == std::string::npos ? disabledAt : std::max(disabledAt, unavailableAt);
        if (activeAt == std::string::npos && fallbackAt == std::string::npos)
            return std::nullopt;
        return fallbackAt == std::string::npos ||
            (activeAt != std::string::npos && activeAt > fallbackAt);
    };
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
            Require(finalRecord.find("geometry=destination-settled") !=
                        std::string::npos &&
                        finalRecord.find(
                            "waited=false redraw=false alpha=premultiplied-clear") !=
                            std::string::npos,
                    "Composition motion final handoff was not transparent and nonblocking");
        }
    };

    const auto waitForPaint = [&](const std::size_t after,
                                  const Target& target,
                                  const std::wstring_view rendered,
                                  const std::string_view authority,
                                  const bool retainedWidgetFocus = false) {
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
                    if (retainedWidgetFocus)
                        return record.find("semantics=inert") != std::string::npos &&
                            record.find("input-owner=widget") != std::string::npos &&
                            record.find("selected=" + WideToUtf8(target.id)) !=
                                std::string::npos &&
                            record.find("visual-focus=settings-ready") != std::string::npos &&
                            record.find("semantic-focus=none") != std::string::npos;
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
    const auto extentFailureDetails = [&](
        const std::string_view log,
        const std::size_t admittedAt,
        const std::string_view admittedRecord) {
        constexpr std::string_view placementNeedle =
            "Overlay work-area placement work=";
        const auto placementAt = log.rfind(placementNeedle, admittedAt);
        std::string placementBodyPreferred = "missing";
        if (placementAt != std::string_view::npos) {
            const auto placementEnd = log.find('\n', placementAt);
            const auto placementRecord = log.substr(
                placementAt,
                placementEnd == std::string_view::npos
                    ? std::string_view::npos
                    : placementEnd - placementAt);
            placementBodyPreferred = TextField(
                placementRecord, "body-preferred=");
        }
        return " sequence=" + TextField(admittedRecord, "sequence=") +
            " desired-extent=" + TextField(admittedRecord, "desired-extent=") +
            " presented-extent=" + TextField(admittedRecord, "presented-extent=") +
            " body-bounds=" + TextField(admittedRecord, "body-bounds=") +
            " viewport-bounds=" + TextField(admittedRecord, "viewport-bounds=") +
            " placement-body-preferred=" + placementBodyPreferred +
            " admitted-record=" + std::string(admittedRecord);
    };
    struct TrayPlacementAuthority final {
        std::string placementCount;
        std::string chromeHwnd;
        std::string trayClient;
        std::string trayScreen;
        LoggedBounds trayCorners;
    };
    struct PaintAuthority final {
        std::string target;
        std::string sequence;
    };
    struct FallbackPresentationAuthority final {
        std::string phase;
        LoggedBounds target;
        LoggedBounds window;
        LoggedBounds client;
        std::string widget;
        std::string instance;
        std::string runtime;
        std::string presentation;
        std::string sequence;
    };
    std::optional<TrayPlacementAuthority> sessionTrayAuthority;
    std::optional<PaintAuthority> postRemovalAudioAuthority;
    std::optional<PaintAuthority> preSwitchAudioAuthority;
    std::optional<FallbackPresentationAuthority> firstFallbackPresentationAuthority;
    std::optional<bool> firstDestinationUsesComposition;
    std::optional<bool> firstFallbackHasDisabledTransition;
    std::optional<std::size_t> fallbackTransitionAuthorityBoundary;
    std::optional<std::size_t> fallbackStartupAuthorityBoundary;
    bool firstPeerSwitchPending{true};
    const auto parseSnapshotSequence = ParsePositiveSequence;
    const auto placementAuthorityDetails = [&](
        const std::string_view phase,
        const std::string_view target,
        const std::string_view sequence,
        const std::string_view placementCount,
        const std::string_view chromeHwnd,
        const std::string_view trayClient,
        const std::string_view trayScreen) {
        return " phase=" + std::string(phase) +
            " target=" + std::string(target) +
            " sequence=" + std::string(sequence) +
            " chrome-placement-count=" + std::string(placementCount) +
            " chrome-hwnd=" + std::string(chromeHwnd) +
            " tray-client=" + std::string(trayClient) +
            " tray-screen=" + std::string(trayScreen) +
            " conversion=fixed-chrome-client-to-screen";
    };
    const auto requireTrayPlacementSample = [&](
        const std::size_t after,
        const std::string_view paintRecord,
        const std::wstring_view expectedTarget,
        const std::string_view phase) {
        Require(sessionTrayAuthority.has_value(),
                "Tray placement authority was not established after catalog removal");
        const auto target = TextField(paintRecord, "target=");
        const auto sequence = TextField(paintRecord, "sequence=");
        Require(target == WideToUtf8(expectedTarget),
                "Tray placement paint target changed before composition admission;" +
                    placementAuthorityDetails(
                        phase, target, sequence,
                        sessionTrayAuthority->placementCount,
                        sessionTrayAuthority->chromeHwnd,
                        sessionTrayAuthority->trayClient,
                        sessionTrayAuthority->trayScreen));

        std::string sample;
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    const auto paintAt = current.find(paintRecord, after);
                    if (paintAt == std::string::npos) return false;
                    const auto sampleAt = current.find(
                        "Composition child sample step=", paintAt + paintRecord.size());
                    if (sampleAt == std::string::npos) return false;
                    const auto sampleEnd = current.find('\n', sampleAt);
                    sample = current.substr(
                        sampleAt, sampleEnd == std::string::npos
                            ? std::string::npos : sampleEnd - sampleAt);

                    auto interveningPaint = current.find(
                        "Widget presentation paint", paintAt + paintRecord.size());
                    while (interveningPaint != std::string::npos &&
                           interveningPaint < sampleAt) {
                        const auto interveningEnd = current.find('\n', interveningPaint);
                        const auto intervening = current.substr(
                            interveningPaint,
                            interveningEnd == std::string::npos
                                ? std::string::npos
                                : interveningEnd - interveningPaint);
                        Require(TextField(intervening, "target=") == target &&
                                    TextField(intervening, "sequence=") == sequence,
                                "Tray placement target/sequence authority changed "
                                "before the correlated composition sample; prior-target=" +
                                    target + " prior-sequence=" + sequence +
                                    " current-target=" +
                                    TextField(intervening, "target=") +
                                    " current-sequence=" +
                                    TextField(intervening, "sequence=") +
                                    placementAuthorityDetails(
                                        phase, target, sequence,
                                        TextField(sample, "chrome-placement-count="),
                                        TextField(sample, "chrome-hwnd="),
                                        sessionTrayAuthority->trayClient,
                                        TextField(sample, "tray=")));
                        interveningPaint = current.find(
                            "Widget presentation paint",
                            interveningEnd == std::string::npos
                                ? sampleAt : interveningEnd + 1);
                    }
                    return true;
                }), "Production paint was not followed by a current composition child "
                    "sample;" + placementAuthorityDetails(
                        phase, target, sequence,
                        sessionTrayAuthority->placementCount,
                        sessionTrayAuthority->chromeHwnd,
                        sessionTrayAuthority->trayClient,
                        sessionTrayAuthority->trayScreen));

        const auto placementCount = TextField(sample, "chrome-placement-count=");
        const auto chromeHwnd = TextField(sample, "chrome-hwnd=");
        const auto trayScreen = TextField(sample, "tray=");
        const auto details = placementAuthorityDetails(
            phase, target, sequence, placementCount, chromeHwnd,
            sessionTrayAuthority->trayClient, trayScreen);
        Require(sample.find("chrome-applied-exact=true") != std::string::npos,
                "Tray placement sample did not carry exact applied chrome authority;" +
                    details);
        Require(placementCount == sessionTrayAuthority->placementCount,
                "Tray placement changed fixed-chrome revision unexpectedly;" + details);
        Require(chromeHwnd == sessionTrayAuthority->chromeHwnd,
                "Tray placement changed the fixed-chrome HWND rectangle unexpectedly;" + details);
        const auto current = ParseBounds(trayScreen);
        const auto expected = sessionTrayAuthority->trayCorners;
        Require(current.x == expected.x && current.y == expected.y &&
                    current.x + current.width == expected.x + expected.width &&
                    current.y + current.height == expected.y + expected.height,
                "Tray screen rectangle changed within one fixed-chrome authority; "
                "expected-corners=" + std::to_string(expected.x) + "," +
                    std::to_string(expected.y) + "," +
                    std::to_string(expected.x + expected.width) + "," +
                    std::to_string(expected.y + expected.height) +
                    " observed-corners=" + std::to_string(current.x) + "," +
                    std::to_string(current.y) + "," +
                    std::to_string(current.x + current.width) + "," +
                    std::to_string(current.y + current.height) + ";" + details);
    };
    const auto requireCurrentPresentationCompletion = [&](
        const std::size_t after,
        const std::string_view paintRecord,
        const std::wstring_view expectedTarget,
        const std::string_view phase,
        const std::wstring_view expectedFocusedWidget = {},
        const bool allowPriorGeometry = false,
        const bool requireCurrentUiAuthority = true,
        const bool allowGeometrySequenceReset = false) {
        const auto mode = CurrentPresentationMode(ReadUtf8(logPath));
        Require(mode.has_value(),
                "Presentation completion lacked a positive mode record phase=" +
                    std::string(phase));
        if (*mode) {
            requireTrayPlacementSample(after, paintRecord, expectedTarget, phase);
            return;
        }
        const auto target = WideToUtf8(expectedTarget);
        const auto sequence = ParsePositiveSequence(TextField(paintRecord, "sequence="));
        const auto current = ReadUtf8(logPath);
        const auto paintAt = current.find(paintRecord, after);
        Require(sequence.has_value() && paintAt != std::string::npos,
                "HWND fallback paint lacked retained positive transaction authority phase=" +
                    std::string(phase) + " target=" + target + " record=" +
                    std::string(paintRecord));
        const auto checkpointAt = current.rfind(
            "Fallback placement mode=hwnd-fallback phase=", paintAt);
        Require(checkpointAt != std::string::npos &&
                    (allowPriorGeometry || checkpointAt >= after),
                "HWND fallback paint lacked a preceding geometry transaction phase=" +
                    std::string(phase) + " target=" + target + " record=" +
                    std::string(paintRecord));
        const auto checkpoint = RecordLine(current, checkpointAt);
        const auto checkpointEnd = checkpointAt + checkpoint.size();
        const auto checkpointSequence = ParsePositiveSequence(
            TextField(checkpoint, "sequence="));
        bool superseded = false;
        for (const auto marker : {"Widget presentation extent refresh",
                                  "Overlay render target resized in place",
                                  "Fallback placement mode=hwnd-fallback phase=set-window-pos"}) {
            const auto markerAt = current.find(marker, checkpointEnd);
            superseded = superseded ||
                (markerAt != std::string::npos && markerAt < paintAt);
        }
        Require(!superseded &&
                    (TextField(checkpoint, "phase=") == "presentation-checkpoint" ||
                     TextField(checkpoint, "phase=") == "set-window-pos") &&
                    TextField(checkpoint, "widget=") == target &&
                    checkpointSequence.has_value() &&
                    (allowGeometrySequenceReset || *checkpointSequence <= *sequence) &&
                    !TextField(checkpoint, "instance=").empty() &&
                    !TextField(checkpoint, "runtime=").empty() &&
                    !TextField(checkpoint, "presentation=").empty(),
                "HWND fallback paint lacked exact unsuperseded preceding geometry authority phase=" +
                    std::string(phase) + " record=" + std::string(paintRecord) +
                    " authority=" + std::string(checkpoint));
        const auto loggedWindow = ParseBounds(TextField(checkpoint, "content-window="));
        const auto damage = ParseBounds(TextField(paintRecord, "damage="));
        Require(paintRecord.find("work=full") != std::string_view::npos &&
                    damage.x == 0.0F && damage.y == 0.0F &&
                    damage.width == loggedWindow.width &&
                    damage.height == loggedWindow.height,
                "HWND fallback paint did not match its exact transaction geometry phase=" +
                    std::string(phase) + " record=" + std::string(paintRecord) +
                    " authority=" + std::string(checkpoint));
        if (requireCurrentUiAuthority) {
            ComPtr<IUIAutomationElement> contentRoot;
            Require(SUCCEEDED(automation->ElementFromHandle(window, contentRoot.GetAddressOf())) && contentRoot,
                    "HWND fallback completion lacked a current content UIA root");
            const std::wstring trayId = L"tray:tray." + std::wstring(expectedTarget);
            ComPtr<IUIAutomationElement> tray = FindAutomationElement(
                automation.Get(), contentRoot.Get(), trayId.c_str());
            Require(tray && IsSelectionItemSelected(tray.Get()),
                    "HWND fallback completion lost exact selected tray authority phase=" +
                        std::string(phase));
            if (expectedFocusedWidget.empty()) {
                Require(IsKeyboardFocused(tray.Get()),
                        "HWND fallback completion lost keyboard-focused exact tray authority phase=" +
                            std::string(phase));
            } else {
                const std::wstring widgetId(expectedFocusedWidget);
                const auto focusedWidget = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), widgetId.c_str());
                Require(!IsKeyboardFocused(tray.Get()) && focusedWidget &&
                            IsKeyboardFocused(focusedWidget.Get()),
                        "HWND fallback completion lost exact widget-focus authority phase=" +
                            std::string(phase));
            }
        }
    };
    const auto validateFirstFallbackDestination = [&](
        const std::size_t after,
        const std::wstring_view expectedTarget,
        const std::string_view expectedBodyPreferred,
        const std::string_view expectedCompositionContentExtent,
        const std::string_view expectedHwndFallbackWindowExtent,
        const std::string_view phase,
        const std::string_view admittedDestinationRecord,
        const PaintAuthority& admittedDestinationAuthority) {
        Require(firstDestinationUsesComposition.has_value(),
                "First destination rendering mode was not established before correlation");
        Require(!*firstDestinationUsesComposition,
                "Fallback destination validation was used for a composition presentation");
        const auto target = WideToUtf8(expectedTarget);
        const std::string paintNeedle =
            "Widget presentation paint target=" + target +
            " content=admitted rendered=" + target + " sequence=";

        std::string placement;
        std::string destinationPaint;
        std::string sample;
        std::string exactExtentPaint;
        std::optional<std::string> destinationSequence;
        std::size_t destinationPaintEndPosition{std::string::npos};
        if (!*firstDestinationUsesComposition) {
            Require(firstFallbackPresentationAuthority.has_value(),
                    "HWND fallback first destination lacked a current pre-Right fallback-state authority");
            Require(admittedDestinationAuthority.target == target &&
                        admittedDestinationAuthority.sequence ==
                            TextField(admittedDestinationRecord, "sequence=") &&
                        expectedHwndFallbackWindowExtent != "missing" &&
                        TextField(admittedDestinationRecord, "desired-extent=") ==
                            expectedHwndFallbackWindowExtent &&
                        TextField(admittedDestinationRecord, "presented-extent=") ==
                            expectedHwndFallbackWindowExtent,
                    "HWND fallback first destination did not retain its exact fallback-window "
                    "extent; expected-desired=" + std::string(expectedHwndFallbackWindowExtent) +
                    " expected-presented=" + std::string(expectedHwndFallbackWindowExtent) +
                    " actual-desired=" + TextField(admittedDestinationRecord, "desired-extent=") +
                    " actual-presented=" + TextField(admittedDestinationRecord, "presented-extent=") +
                    " record=" + std::string(admittedDestinationRecord));
            RECT windowBounds{};
            Require(GetWindowRect(window, &windowBounds) != FALSE,
                    Win32Error("GetWindowRect(HWND fallback content)"));
            RECT client{};
            POINT origin{};
            Require(GetClientRect(window, &client) != FALSE && ClientToScreen(window, &origin) != FALSE,
                    "HWND fallback content client geometry was unavailable");
            const RECT clientScreen{origin.x, origin.y,
                origin.x + client.right - client.left, origin.y + client.bottom - client.top};
            const auto& presentationAuthority = *firstFallbackPresentationAuthority;
            Require(windowBounds.left == static_cast<LONG>(presentationAuthority.window.x) &&
                        windowBounds.top == static_cast<LONG>(presentationAuthority.window.y) &&
                    windowBounds.right - windowBounds.left ==
                            static_cast<LONG>(presentationAuthority.window.width) &&
                    windowBounds.bottom - windowBounds.top ==
                            static_cast<LONG>(presentationAuthority.window.height),
                    "HWND fallback destination content window drifted from its exact "
                    "pre-Right typed fallback-state authority");
            Require(clientScreen.left == static_cast<LONG>(presentationAuthority.client.x) &&
                        clientScreen.top == static_cast<LONG>(presentationAuthority.client.y) &&
                clientScreen.right - clientScreen.left ==
                            static_cast<LONG>(presentationAuthority.client.width) &&
                clientScreen.bottom - clientScreen.top ==
                            static_cast<LONG>(presentationAuthority.client.height),
                    "HWND fallback destination content client drifted from its exact "
                    "pre-Right fallback-state authority");
            ComPtr<IUIAutomationElement> contentRoot;
            Require(SUCCEEDED(automation->ElementFromHandle(window, contentRoot.GetAddressOf())) && contentRoot,
                    "HWND fallback destination content root was unavailable");
            const std::wstring trayId = L"tray:tray." + std::wstring(expectedTarget);
            ComPtr<IUIAutomationElement> tray = FindAutomationElement(
                automation.Get(), contentRoot.Get(), trayId.c_str());
            Require(tray && IsSelectionItemSelected(tray.Get()) && IsKeyboardFocused(tray.Get()),
                    "HWND fallback destination tray did not retain exact selected keyboard focus");
            const wchar_t* destinationReady = expectedTarget == L"wide-peer"
                ? L"widget:wide-peer-ready" : L"widget:settings-ready";
            ComPtr<IUIAutomationElement> destination = FindAutomationElement(
                automation.Get(), contentRoot.Get(), destinationReady);
            RECT destinationBounds{};
            Require(destination && SUCCEEDED(destination->get_CurrentBoundingRectangle(&destinationBounds)) &&
                        destinationBounds.left >= clientScreen.left && destinationBounds.top >= clientScreen.top &&
                        destinationBounds.right <= clientScreen.right && destinationBounds.bottom <= clientScreen.bottom,
                    "HWND fallback destination semantic bounds escaped the current content client");
            ComPtr<IUIAutomationElement> currentContentRoot;
            ComPtr<IUIAutomationElement> currentTray;
            ComPtr<IUIAutomationElement> currentDestination;
            Require(SUCCEEDED(automation->ElementFromHandle(
                        window, currentContentRoot.GetAddressOf())) && currentContentRoot,
                    "HWND fallback content root disappeared during destination authority pin");
            currentTray = FindAutomationElement(
                automation.Get(), currentContentRoot.Get(), trayId.c_str());
            currentDestination = FindAutomationElement(
                automation.Get(), currentContentRoot.Get(), destinationReady);
            BOOL sameRoot{};
            BOOL sameTray{};
            BOOL sameDestination{};
            Require(currentTray && currentDestination &&
                        SUCCEEDED(automation->CompareElements(
                            contentRoot.Get(), currentContentRoot.Get(), &sameRoot)) && sameRoot &&
                        SUCCEEDED(automation->CompareElements(
                            tray.Get(), currentTray.Get(), &sameTray)) && sameTray &&
                        SUCCEEDED(automation->CompareElements(
                            destination.Get(), currentDestination.Get(), &sameDestination)) &&
                        sameDestination,
                    "HWND fallback destination UIA authority drifted during immediate pin");
            return after;
        }
        const bool correlated = WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    auto fallbackAt = current.find(paintNeedle, after);
                    while (fallbackAt != std::string::npos) {
                        const auto fallbackEnd = current.find('\n', fallbackAt);
                        const auto candidate = current.substr(
                            fallbackAt,
                            fallbackEnd == std::string::npos
                                ? std::string::npos
                                : fallbackEnd - fallbackAt);
                        if (TextField(candidate, "desired-extent=") ==
                                expectedCompositionContentExtent &&
                            TextField(candidate, "presented-extent=") ==
                                expectedCompositionContentExtent) {
                            exactExtentPaint = candidate;
                        }
                        fallbackAt = current.find(
                            paintNeedle,
                            fallbackEnd == std::string::npos
                                ? current.size()
                                : fallbackEnd + 1);
                    }

                    const auto placementAt = current.find(
                        "Fixed chrome placement reason=catalog-order", after);
                    if (placementAt == std::string::npos) return false;
                    const auto placementEnd = current.find('\n', placementAt);
                    if (placementEnd == std::string::npos) return false;
                    placement = current.substr(
                        placementAt, placementEnd - placementAt);
                    if (placement.find(" exact=true") == std::string::npos)
                        return false;

                    const auto paintAt = current.find(paintNeedle, placementEnd + 1);
                    if (paintAt == std::string::npos) return false;
                    if (current.find("Widget presentation paint", placementEnd + 1) !=
                        paintAt) {
                        return false;
                    }
                    const auto paintEnd = current.find('\n', paintAt);
                    if (paintEnd == std::string::npos) return false;
                    destinationPaint = current.substr(paintAt, paintEnd - paintAt);
                    destinationPaintEndPosition = paintEnd + 1;
                    const auto candidateSequence = TextField(destinationPaint, "sequence=");
                    if (!parseSnapshotSequence(candidateSequence) ||
                        TextField(destinationPaint, "desired-extent=") !=
                            expectedCompositionContentExtent ||
                        TextField(destinationPaint, "presented-extent=") !=
                            expectedCompositionContentExtent) {
                        return false;
                    }
                    destinationSequence = candidateSequence;
                    const auto compositionCommitAt = current.find(
                        "Composition placement committed content=complete",
                        paintEnd + 1);
                    const auto sampleAt = current.find(
                        "Composition child sample step=",
                        paintEnd + 1);
                    if (compositionCommitAt == std::string::npos ||
                        sampleAt == std::string::npos ||
                        compositionCommitAt >= sampleAt) {
                        return false;
                    }
                    const auto laterPlacement = current.find(
                        "Fixed chrome placement reason=", placementEnd + 1);
                    if (laterPlacement != std::string::npos && laterPlacement < sampleAt)
                        return false;
                    const auto interveningPaint = current.find(
                        "Widget presentation paint", paintEnd + 1);
                    if (interveningPaint != std::string::npos &&
                        interveningPaint < sampleAt) {
                        return false;
                    }
                    const auto sampleEnd = current.find('\n', sampleAt);
                    if (sampleEnd == std::string::npos) return false;
                    sample = current.substr(
                        sampleAt, sampleEnd - sampleAt);
                    const auto workPlacementAt = current.find(
                        "Overlay work-area placement work=", sampleEnd + 1);
                    if (workPlacementAt == std::string::npos) return false;
                    const auto workPlacementEnd = current.find('\n', workPlacementAt);
                    const auto workPlacement = current.substr(
                        workPlacementAt,
                        workPlacementEnd == std::string::npos
                            ? std::string::npos
                            : workPlacementEnd - workPlacementAt);
                    if (TextField(workPlacement, "body-preferred=") !=
                            expectedBodyPreferred ||
                        workPlacement.find(" surface=widget shell=shared ") ==
                            std::string::npos) {
                        return false;
                    }
                    const auto paintBeforeWorkPlacement = current.find(
                        "Widget presentation paint", sampleEnd + 1);
                    if (paintBeforeWorkPlacement != std::string::npos &&
                        paintBeforeWorkPlacement < workPlacementAt) {
                        return false;
                    }
                    const auto fixedPlacementBeforeWorkPlacement = current.find(
                        "Fixed chrome placement reason=", sampleEnd + 1);
                    if (fixedPlacementBeforeWorkPlacement != std::string::npos &&
                        fixedPlacementBeforeWorkPlacement < workPlacementAt) {
                        return false;
                    }
                    return true;
                });
        const std::string_view destinationSequenceForDiagnostics = destinationSequence
            ? std::string_view(*destinationSequence)
            : std::string_view("missing");
        Require(correlated,
                "First natural extent-changing switch did not establish exact "
                "980x700 wide-peer placement, positive destination sequence, "
                "980x645 destination paint, complete placement commit, and "
                "composition sample;" +
                    placementAuthorityDetails(
                        phase, target, destinationSequenceForDiagnostics,
                        "missing", "missing", "missing", "missing"));
        Require(destinationSequence.has_value(),
                "DirectComposition destination correlation omitted its positive sequence");
        const auto& sequence = *destinationSequence;

        const auto placementCount = TextField(placement, "placement-count=");
        const auto chromeHwnd = TextField(placement, "actual=");
        const auto trayClient = TextField(placement, "tray-client=");
        const auto trayScreen = TextField(placement, "tray-screen=");
        const auto details = placementAuthorityDetails(
            phase, target, sequence, placementCount, chromeHwnd,
            trayClient, trayScreen);
        Require(!placementCount.empty(),
                "Tray placement baseline omitted placement revision;" + details);
        Require(!chromeHwnd.empty(),
                "Tray placement baseline omitted chrome HWND rectangle;" + details);
        Require(!trayClient.empty(),
                "Tray placement baseline omitted tray client rectangle;" + details);
        Require(!trayScreen.empty(),
                "Tray placement baseline omitted tray screen rectangle;" + details);
        Require(sample.find("chrome-applied-exact=true") != std::string::npos,
                "Tray placement baseline sample omitted exact chrome authority;" +
                    details);
        Require(TextField(sample, "chrome-placement-count=") == placementCount,
                "Tray placement baseline sample crossed placement revision;" +
                    details);
        Require(TextField(sample, "chrome-hwnd=") == chromeHwnd,
                "Tray placement baseline sample crossed chrome HWND rectangle;" +
                    details);
        Require(TextField(sample, "tray=") == trayScreen,
                "Tray placement baseline sample changed tray screen projection;" +
                    details);
        Fail("Fallback destination validation reached an obsolete composition path");
        return destinationPaintEndPosition;
    };
    const auto switchTo = [&](const Target& target, const Target& previous) {
        const auto inputStarted = std::chrono::steady_clock::now();
        auto before = ReadUtf8(logPath).size();
        const bool firstPeerSwitch = firstPeerSwitchPending;
        std::optional<PaintAuthority> firstSwitchInputBoundary;
        const auto hasCurrentAdmittedAudioBacking = [](
            const std::string_view log, const std::size_t through,
            const long long retainedSequence) {
            for (std::size_t paintAt = log.find("Widget presentation paint");
                 paintAt != std::string::npos && paintAt <= through;) {
                const auto paint = RecordLine(log, paintAt);
                const auto sequence = ParsePositiveSequence(TextField(paint, "sequence="));
                if (TextField(paint, "target=") == "audio-mixer" &&
                    TextField(paint, "rendered=") == "audio-mixer" &&
                    TextField(paint, "content=") == "admitted" &&
                    TextField(paint, "semantics=") == "current" &&
                    sequence.has_value() && *sequence == retainedSequence) {
                    return true;
                }
                const auto nextLine = log.find('\n', paintAt);
                paintAt = nextLine == std::string::npos
                    ? std::string::npos
                    : log.find("Widget presentation paint", nextLine + 1);
            }
            return false;
        };
        Require(sessionTrayAuthority.has_value(),
                "Peer switch lacked the catalog-removal tray placement authority");
        const auto beforeSelection = ReadUtf8(logPath);
        const auto priorPaint = beforeSelection.rfind("Widget presentation paint");
        Require(priorPaint != std::string::npos,
                "Tray placement authority had no current paint before selection");
        const auto priorEnd = beforeSelection.find('\n', priorPaint);
        const auto priorRecord = beforeSelection.substr(
            priorPaint, priorEnd == std::string::npos
                ? std::string::npos : priorEnd - priorPaint);
        requireTrayPlacementSample(
            priorPaint, priorRecord, previous.id, "before-selection");
        if (firstPeerSwitch) {
            Require(preSwitchAudioAuthority.has_value(),
                    "First switch lacked current pre-switch Audio authority");
            const auto boundarySequence = parseSnapshotSequence(
                preSwitchAudioAuthority->sequence);
            Require(preSwitchAudioAuthority->target == "audio-mixer" &&
                        boundarySequence.has_value(),
                    "First switch lacked exact admitted Audio visual authority; target=" +
                        preSwitchAudioAuthority->target + " sequence=" +
                        preSwitchAudioAuthority->sequence);
            if (firstDestinationUsesComposition.has_value() &&
                !*firstDestinationUsesComposition) {
                Require(firstFallbackHasDisabledTransition.has_value(),
                        "HWND fallback first switch had no positive fallback origin authority");
                const bool hasDisabledTransition = *firstFallbackHasDisabledTransition;
                const auto fallbackAuthorityBoundary = hasDisabledTransition
                    ? fallbackTransitionAuthorityBoundary
                    : fallbackStartupAuthorityBoundary;
                Require(fallbackAuthorityBoundary.has_value(),
                        hasDisabledTransition
                            ? "HWND fallback first switch omitted its DirectComposition-disabled "
                              "transition marker"
                            : "HWND fallback first switch omitted its startup-unavailable marker");
                FallbackCheckpointSelection checkpointSelection;
                const auto selectCheckpoint = [&] {
                    checkpointSelection = SelectCurrentFallbackCheckpoint(
                        ReadUtf8(logPath), *fallbackAuthorityBoundary, "audio-mixer",
                        *boundarySequence);
                    return checkpointSelection.offset.has_value();
                };
                const auto retainFallbackAuthorityFailure = [&] {
                    std::string retainedLog = "not-supplied";
                    if (!arguments.durableDiagnosticsDirectory.empty()) {
                        std::error_code error;
                        fs::create_directories(arguments.durableDiagnosticsDirectory, error);
                        const auto destination = arguments.durableDiagnosticsDirectory /
                            L"WidgetSwitchHostTests-fallback-authority-overlay.log";
                        if (!error) {
                            fs::copy_file(logPath, destination,
                                          fs::copy_options::overwrite_existing, error);
                        }
                        retainedLog = error ? "copy-failed=" + error.message()
                                            : destination.string();
                    }
                    return DescribeFallbackCheckpointSelection(
                        checkpointSelection, *fallbackAuthorityBoundary, "audio-mixer",
                        *boundarySequence) +
                        " retained-overlay-log=" + retainedLog;
                };
                if (hasDisabledTransition) {
                    Require(WaitUntil(kOperationTimeoutMilliseconds, selectCheckpoint),
                            "HWND fallback first switch had no typed current presentation "
                            "checkpoint after its DirectComposition-disabled transition;" +
                            retainFallbackAuthorityFailure());
                } else {
                    selectCheckpoint();
                    Require(checkpointSelection.offset.has_value(),
                            "HWND startup fallback first switch had no typed current presentation "
                            "checkpoint after startup-unavailable;" +
                            retainFallbackAuthorityFailure());
                }
                const auto checkpointAt = *checkpointSelection.offset;
                const auto checkpointLog = ReadUtf8(logPath);
                const auto checkpoint = RecordLine(checkpointLog, checkpointAt);
                const auto phase = TextField(checkpoint, "phase=");
                Require(phase == "presentation-checkpoint",
                        "HWND fallback first switch admitted a non-checkpoint phase=" + phase);
                const auto work = ParseBounds(TextField(checkpoint, "work="));
                const auto checkpointTarget = ParseBounds(TextField(checkpoint, "target="));
                const auto recordedWindow = ParseBounds(
                    TextField(checkpoint, "content-window="));
                const auto recordedClient = ParseBounds(
                    TextField(checkpoint, "content-client-screen="));
                const auto dpi = parseSnapshotSequence(TextField(checkpoint, "dpi="));
                Require(dpi.has_value(),
                        "HWND fallback presentation checkpoint omitted a positive DPI authority");
                Require(TextField(checkpoint, "interface-scale=") == "1.000000",
                        "HWND fallback presentation checkpoint changed the fixture interface-scale");
                Require(TextField(checkpoint, "widget=") == "audio-mixer" &&
                            TextField(checkpoint, "instance=") != "none" &&
                            TextField(checkpoint, "runtime=") != "none" &&
                            TextField(checkpoint, "presentation=") != "none",
                        "HWND fallback presentation checkpoint omitted current Audio authority");
                const auto recordSequence = parseSnapshotSequence(
                    TextField(checkpoint, "sequence="));
                Require(recordSequence.has_value() && *recordSequence == *boundarySequence,
                        "HWND fallback presentation checkpoint did not carry the exact current "
                        "Audio sequence");
                RECT windowBounds{};
                RECT client{};
                POINT origin{};
                const bool capturedContentGeometry =
                    GetWindowRect(window, &windowBounds) != FALSE &&
                    GetClientRect(window, &client) != FALSE &&
                    ClientToScreen(window, &origin) != FALSE;
                const LoggedBounds currentWindow{
                    static_cast<float>(windowBounds.left),
                    static_cast<float>(windowBounds.top),
                    static_cast<float>(windowBounds.right - windowBounds.left),
                    static_cast<float>(windowBounds.bottom - windowBounds.top),
                };
                const LoggedBounds currentClient{
                    static_cast<float>(origin.x),
                    static_cast<float>(origin.y),
                    static_cast<float>(client.right - client.left),
                    static_cast<float>(client.bottom - client.top),
                };
                const HMONITOR monitor = MonitorFromWindow(
                    window, MONITOR_DEFAULTTONEAREST);
                MONITORINFO monitorInfo{sizeof(monitorInfo)};
                const bool capturedMonitor =
                    monitor && GetMonitorInfoW(monitor, &monitorInfo) != FALSE;
                const LoggedBounds currentWork{
                    static_cast<float>(monitorInfo.rcWork.left),
                    static_cast<float>(monitorInfo.rcWork.top),
                    static_cast<float>(monitorInfo.rcWork.right - monitorInfo.rcWork.left),
                    static_cast<float>(monitorInfo.rcWork.bottom - monitorInfo.rcWork.top),
                };
                const auto currentDpi = static_cast<long long>(GetDpiForWindow(window));
                const bool targetWithinWork =
                    checkpointTarget.width > 0.0F && checkpointTarget.height > 0.0F &&
                    checkpointTarget.x >= work.x && checkpointTarget.y >= work.y &&
                    checkpointTarget.x + checkpointTarget.width <= work.x + work.width &&
                    checkpointTarget.y + checkpointTarget.height <= work.y + work.height;
                const bool workMatchesCurrent = capturedMonitor &&
                    work.x == currentWork.x && work.y == currentWork.y &&
                    work.width == currentWork.width && work.height == currentWork.height;
                const bool dpiMatchesCurrent = currentDpi > 0 && *dpi == currentDpi;
                const bool recordedWindowMatchesCurrent = capturedContentGeometry &&
                    recordedWindow.x == currentWindow.x && recordedWindow.y == currentWindow.y &&
                    recordedWindow.width == currentWindow.width &&
                    recordedWindow.height == currentWindow.height;
                const bool recordedClientMatchesCurrent = capturedContentGeometry &&
                    recordedClient.x == currentClient.x && recordedClient.y == currentClient.y &&
                    recordedClient.width == currentClient.width &&
                    recordedClient.height == currentClient.height;
                if (!capturedContentGeometry || !capturedMonitor || !targetWithinWork ||
                    !workMatchesCurrent || !dpiMatchesCurrent ||
                    !recordedWindowMatchesCurrent || !recordedClientMatchesCurrent) {
                    const auto formatBounds = [](const LoggedBounds& bounds) {
                        return std::to_string(bounds.x) + "," + std::to_string(bounds.y) + "," +
                            std::to_string(bounds.width) + "," + std::to_string(bounds.height);
                    };
                    std::string retainedLog = "not-supplied";
                    if (!arguments.durableDiagnosticsDirectory.empty()) {
                        std::error_code error;
                        fs::create_directories(arguments.durableDiagnosticsDirectory, error);
                        const auto destination = arguments.durableDiagnosticsDirectory /
                            L"WidgetSwitchHostTests-fallback-geometry-overlay.log";
                        if (!error) {
                            fs::copy_file(logPath, destination,
                                          fs::copy_options::overwrite_existing, error);
                        }
                        retainedLog = error ? "copy-failed=" + error.message()
                                            : destination.string();
                    }
                    Fail("HWND fallback presentation checkpoint geometry mismatch" +
                         std::string(" selected-offset=") + std::to_string(checkpointAt) +
                         " record=" + std::string(checkpoint) +
                         " checkpoint-work=" + formatBounds(work) +
                         " checkpoint-target=" + formatBounds(checkpointTarget) +
                         " checkpoint-content-window=" + formatBounds(recordedWindow) +
                         " checkpoint-client-screen=" + formatBounds(recordedClient) +
                         " checkpoint-dpi=" + std::to_string(*dpi) +
                         " content-hwnd=" + std::to_string(reinterpret_cast<std::uintptr_t>(window)) +
                         " monitor=" + std::to_string(reinterpret_cast<std::uintptr_t>(monitor)) +
                         " monitor-work=" + formatBounds(currentWork) +
                         " live-window=" + formatBounds(currentWindow) +
                         " live-client-screen=" + formatBounds(currentClient) +
                         " live-dpi=" + std::to_string(currentDpi) +
                         " captured-content-geometry=" +
                             (capturedContentGeometry ? "true" : "false") +
                         " captured-monitor=" + (capturedMonitor ? "true" : "false") +
                         " target-within-work=" + (targetWithinWork ? "true" : "false") +
                         " work-matches-current=" + (workMatchesCurrent ? "true" : "false") +
                         " dpi-matches-current=" + (dpiMatchesCurrent ? "true" : "false") +
                         " recorded-window-matches-current=" +
                             (recordedWindowMatchesCurrent ? "true" : "false") +
                         " recorded-client-matches-current=" +
                             (recordedClientMatchesCurrent ? "true" : "false") +
                         " retained-overlay-log=" + retainedLog);
                }
                firstFallbackPresentationAuthority = FallbackPresentationAuthority{
                    phase,
                    checkpointTarget,
                    recordedWindow,
                    recordedClient,
                    TextField(checkpoint, "widget="),
                    TextField(checkpoint, "instance="),
                    TextField(checkpoint, "runtime="),
                    TextField(checkpoint, "presentation="),
                    TextField(checkpoint, "sequence="),
                };
            }
        }
        if (firstPeerSwitch) {
            const auto inputBoundaryLog = ReadUtf8(logPath);
            const auto paintAt = inputBoundaryLog.rfind("Widget presentation paint");
            Require(paintAt != std::string::npos,
                    "First switch lacked an admitted/current Audio paint at its input boundary");
            const auto paint = RecordLine(inputBoundaryLog, paintAt);
            const auto sequence = parseSnapshotSequence(TextField(paint, "sequence="));
            Require(TextField(paint, "target=") == "audio-mixer" &&
                        TextField(paint, "rendered=") == "audio-mixer" &&
                        TextField(paint, "semantics=") == "current" && sequence.has_value(),
                    "First switch input boundary lacked exact admitted/current Audio authority; record=" +
                        std::string(paint));
            firstSwitchInputBoundary = PaintAuthority{
                TextField(paint, "target="), TextField(paint, "sequence=")};
            before = inputBoundaryLog.size();
        }
        const auto signal = installation->StartupSignal(target.id);
        std::error_code ignored;
        fs::remove(signal, ignored);
        SendKey(window, target.key);
        if (firstPeerSwitch) {
            const auto transitionLog = ReadUtf8(logPath);
            const auto transitionAt = transitionLog.find(
                "Widget presentation transition from=audio-mixer", before);
            Require(transitionAt != std::string::npos,
                    "First switch did not emit an Audio source transition");
            const auto transitionEnd = transitionLog.find('\n', transitionAt);
            const auto transitionRecord = transitionLog.substr(
                transitionAt,
                transitionEnd == std::string::npos
                    ? std::string::npos
                    : transitionEnd - transitionAt);
            Require(transitionRecord.find(" to=wide-peer ") !=
                        std::string::npos,
                    "First switch transition did not target the exact wide peer; record=" +
                        transitionRecord);
            Require(transitionRecord.find(" content=retained-until-snapshot ") !=
                        std::string::npos,
                    "First switch transition did not retain committed Audio content; record=" +
                        transitionRecord);
            Require(transitionRecord.find(" sizing=retained-until-snapshot ") !=
                        std::string::npos,
                    "First switch transition did not retain source sizing; record=" +
                        transitionRecord);
            Require(transitionRecord.find(" retained-from=audio-mixer") !=
                        std::string::npos,
                    "First switch transition did not name exact retained Audio authority; "
                    "record=" + transitionRecord);
        }
        const auto retainedRecord = waitForPaint(
            before, target, previous.id, "retained");
        const auto retainedLog = ReadUtf8(logPath);
        const auto retainedAt = retainedLog.find(retainedRecord, before);
        Require(retainedAt != std::string::npos,
                "Retained paint trace disappeared before composition validation");
        requireTrayPlacementSample(
            before, retainedRecord, target.id, "selected-identity-changed");
        if (firstPeerSwitch) {
            const auto sequence = parseSnapshotSequence(TextField(retainedRecord, "sequence="));
            const auto boundary = parseSnapshotSequence(firstSwitchInputBoundary->sequence);
            Require(sequence.has_value() && boundary.has_value() && *sequence >= *boundary,
                    "First retained switch paint regressed its input-boundary Audio sequence; expected-at-least=" +
                        firstSwitchInputBoundary->sequence +
                        " current=" + TextField(retainedRecord, "sequence="));
            Require(hasCurrentAdmittedAudioBacking(retainedLog, retainedAt, *sequence),
                    "First retained switch paint lacked exact admitted/current Audio backing; record=" +
                        retainedRecord);
        }
        const auto retainedTransitionAt = retainedLog.find(
            TransitionNeedle(target.id), before);
        Require(retainedTransitionAt != std::string::npos,
                "Retained switch timing lacked its exact host transition for " +
                    WideToUtf8(target.label));
        inputToRetainedMilliseconds.push_back(DiagnosticLatencyMilliseconds(
            RecordContainingLine(retainedLog, retainedTransitionAt),
            RecordContainingLine(retainedLog, retainedAt),
            "Retained switch timing for " + WideToUtf8(target.label)));
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
        const auto log = ReadUtf8(logPath);
        const auto admittedAt = log.find(admittedNeedle, before);
        Require(admittedAt != std::string::npos,
                "Admitted paint trace disappeared before composition validation");
        if (firstPeerSwitch) {
            auto sourcePaintAt = log.find("Widget presentation paint", before);
            while (sourcePaintAt != std::string::npos &&
                   sourcePaintAt < admittedAt) {
                const auto sourcePaintEnd = log.find('\n', sourcePaintAt);
                const auto sourcePaint = log.substr(
                    sourcePaintAt,
                    sourcePaintEnd == std::string::npos
                        ? std::string::npos
                        : sourcePaintEnd - sourcePaintAt);
                if (TextField(sourcePaint, "target=") == WideToUtf8(target.id) &&
                    TextField(sourcePaint, "rendered=") == WideToUtf8(previous.id)) {
                    const auto sequence = parseSnapshotSequence(TextField(sourcePaint, "sequence="));
                    const auto boundary = parseSnapshotSequence(firstSwitchInputBoundary->sequence);
                    Require(sequence.has_value() && boundary.has_value() && *sequence >= *boundary,
                            "First switch retained source regressed input-boundary Audio "
                            "sequence authority before destination admission; expected-at-least=" +
                                firstSwitchInputBoundary->sequence +
                                " current=" + TextField(sourcePaint, "sequence=") +
                                " record=" + sourcePaint);
                    Require(hasCurrentAdmittedAudioBacking(log, sourcePaintAt, *sequence),
                            "First switch retained source lacked exact admitted/current Audio backing; record=" +
                                sourcePaint);
                }
                sourcePaintAt = log.find(
                    "Widget presentation paint",
                    sourcePaintEnd == std::string::npos
                        ? admittedAt
                        : sourcePaintEnd + 1);
            }
        }
        const auto admittedEnd = log.find('\n', admittedAt);
        const auto admittedRecord = log.substr(
            admittedAt,
            admittedEnd == std::string::npos
                ? std::string::npos
                : admittedEnd - admittedAt);
        const PaintAuthority destinationAuthority{
            TextField(admittedRecord, "target="),
            TextField(admittedRecord, "sequence="),
        };
        const auto destinationSequence = parseSnapshotSequence(
            destinationAuthority.sequence);
        Require(destinationAuthority.target == WideToUtf8(target.id),
                "Admitted destination paint did not own the exact target for " +
                    WideToUtf8(target.label));
        Require(destinationSequence.has_value(),
                "Admitted destination paint omitted an independent positive sequence for " +
                    WideToUtf8(target.label));
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
        const auto retainedDesired = TextField(retainedRecord, "desired-extent=");
        const auto admittedDesired = TextField(admittedRecord, "desired-extent=");
        Require(admittedDesired != retainedDesired &&
                    TextField(admittedRecord, "viewport-bounds=") !=
                        TextField(retainedRecord, "viewport-bounds="),
                "Admitted destination reused retained source layout geometry for " +
                    WideToUtf8(target.label) + "; retained=" + retainedRecord +
                    " admitted=" + admittedRecord);
        if (target.expectedCompositionContentPresentationExtent && sessionTrayAuthority) {
            Require(
                admittedDesired == target.expectedCompositionContentPresentationExtent,
                "Admitted destination did not own its authored content presentation "
                "extent for " + WideToUtf8(target.label) + ";" +
                    extentFailureDetails(log, admittedAt, admittedRecord));
        }
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
        requireTrayPlacementSample(
            before, destinationRecord, target.id, "destination-admitted");
        if (firstPeerSwitch && firstDestinationUsesComposition.has_value() &&
            !*firstDestinationUsesComposition) {
            (void)validateFirstFallbackDestination(
                before, target.id, "980.000000x700.000000",
                target.expectedCompositionContentPresentationExtent,
                target.expectedHwndFallbackWindowExtent
                    ? target.expectedHwndFallbackWindowExtent : "missing",
                "first-natural-extent-changing-switch",
                admittedRecord, destinationAuthority);
        }
        firstPeerSwitchPending = false;
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
    Require(
        TextField(audioAdmittedRecord, "desired-extent=") ==
            kTargets[0].expectedCompositionContentPresentationExtent,
        "Audio Mixer did not establish the expected authored content presentation "
        "extent;" + extentFailureDetails(
            audioLog, audioAdmittedAt, audioAdmittedRecord));
    recordComposition(
        audioAdmittedEnd == std::string::npos ? audioLog.size() : audioAdmittedEnd + 1,
        kTargets[0].label);
    firstAdmittedActivationMilliseconds =
        static_cast<std::uint64_t>(std::chrono::duration_cast<
            std::chrono::milliseconds>(
                std::chrono::steady_clock::now() - firstActivationStarted).count());

    const auto addedCatalogBefore = ReadUtf8(logPath).size();
    installation->PublishCatalogProbe(true);
    std::string postRemovalAudioPaint;
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

    const auto preRemovalLog = ReadUtf8(logPath);
    const auto removalCompositionMode = CurrentPresentationMode(preRemovalLog);
    Require(removalCompositionMode.has_value(),
            "Catalog removal lacked a positive current presentation-mode authority");
    ComPtr<IUIAutomationElement> focusedBeforeRemoval;
    Require(SUCCEEDED(automation->GetFocusedElement(
                focusedBeforeRemoval.GetAddressOf())) && focusedBeforeRemoval,
            "Catalog removal lacked an exact pre-removal keyboard-focus authority");
    const auto focusedBeforeRemovalId = AutomationIdOf(focusedBeforeRemoval.Get());
    Require(!focusedBeforeRemovalId.empty(),
            "Catalog removal precondition focused an element without AutomationId authority");
    const auto removedCatalogBefore = preRemovalLog.size();
    installation->PublishCatalogProbe(false);
    std::string postRemovalPlacement;
    std::string postRemovalCompositionSample;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                auto paintSearchBoundary = removedCatalogBefore;
                if (*removalCompositionMode) {
                    const auto placement = current.find(
                        "Fixed chrome placement reason=catalog-order",
                        removedCatalogBefore);
                    if (placement == std::string::npos) return false;
                    const auto placementEnd = current.find('\n', placement);
                    if (placementEnd == std::string::npos) return false;
                    postRemovalPlacement = std::string(RecordLine(current, placement));
                    if (postRemovalPlacement.find(" exact=true") ==
                        std::string::npos) {
                        return false;
                    }
                    paintSearchBoundary = placementEnd + 1;
                }
                const auto paint = current.find(
                    "Widget presentation paint target=audio-mixer content=admitted",
                    paintSearchBoundary);
                if (paint == std::string::npos) return false;
                const auto end = current.find('\n', paint);
                if (end == std::string::npos) return false;
                const auto record = current.substr(
                    paint, end - paint);
                const bool currentAuthority =
                    record.find("tray-total=8") != std::string::npos &&
                    record.find("tray-visible=8") != std::string::npos &&
                    record.find("selected=audio-mixer") != std::string::npos &&
                    record.find("tray-selected-visible=true") != std::string::npos;
                if (!currentAuthority) return false;
                if (*removalCompositionMode) {
                    const auto disabled = current.find(
                        "DirectComposition presentation disabled; using HWND fallback:",
                        removedCatalogBefore);
                    const auto sample = current.find(
                        "Composition child sample step=", end + 1);
                    if ((disabled != std::string::npos && disabled < sample) ||
                        sample == std::string::npos) {
                        return false;
                    }
                    const auto sampleEnd = current.find('\n', sample);
                    if (sampleEnd == std::string::npos) return false;
                    postRemovalCompositionSample = std::string(RecordLine(current, sample));
                    if (postRemovalCompositionSample.find(
                            "chrome-placement-reason=catalog-order") ==
                            std::string::npos ||
                        postRemovalCompositionSample.find(
                            "chrome-applied-exact=true") == std::string::npos) {
                        return false;
                    }
                }
                postRemovalAudioPaint = record;
                return true;
            }), "Production catalog removal did not re-establish fixed chrome before "
                    "synchronous repaint while preserving exact compact tray authority; log=" +
                    ReadUtf8(logPath).substr(removedCatalogBefore));
    Require(IsWindowVisible(window) != FALSE,
            "Catalog removal stranded the main content HWND");
    const HWND postRemovalChrome = LocateHostWindow(host->Id(), L"WidgetRail.Chrome");
    if (*removalCompositionMode) {
        Require(postRemovalChrome && IsWindowVisible(postRemovalChrome) != FALSE,
                "Catalog removal stranded the fixed-chrome HWND after exact composition placement");
    }
    const HWND postRemovalTrayWindow = *removalCompositionMode
        ? postRemovalChrome : window;
    ComPtr<IUIAutomationElement> postRemovalTrayRoot;
    Require(postRemovalTrayWindow && SUCCEEDED(automation->ElementFromHandle(
                postRemovalTrayWindow, postRemovalTrayRoot.GetAddressOf())) &&
                postRemovalTrayRoot,
            "Catalog removal did not retain the current tray UIA root");
    ComPtr<IUIAutomationElement> postRemovalAudioTray = FindAutomationElement(
        automation.Get(), postRemovalTrayRoot.Get(), L"tray:tray.audio-mixer");
    Require(postRemovalAudioTray && IsSelectionItemSelected(postRemovalAudioTray.Get()),
            "Catalog removal did not retain exact selected tray:tray.audio-mixer authority");
    ComPtr<IUIAutomationElement> focusedAfterRemoval;
    Require(SUCCEEDED(automation->GetFocusedElement(
                focusedAfterRemoval.GetAddressOf())) && focusedAfterRemoval &&
                AutomationIdOf(focusedAfterRemoval.Get()) == focusedBeforeRemovalId,
            "Catalog removal changed the exact surviving keyboard-focus authority");

    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(Guide hide after catalog removal)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) == FALSE &&
                    (!postRemovalChrome || IsWindowVisible(postRemovalChrome) == FALSE);
            }), "Guide did not hide both current overlay HWNDs after catalog removal");
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(Guide reopen after catalog removal)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                if (IsWindowVisible(window) == FALSE) return false;
                const HWND trayWindow = *removalCompositionMode
                    ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome") : window;
                if (!trayWindow || IsWindowVisible(trayWindow) == FALSE) return false;
                ComPtr<IUIAutomationElement> trayRoot;
                if (FAILED(automation->ElementFromHandle(
                        trayWindow, trayRoot.GetAddressOf())) || !trayRoot) return false;
                const auto audioTray = FindAutomationElement(
                    automation.Get(), trayRoot.Get(), L"tray:tray.audio-mixer");
                ComPtr<IUIAutomationElement> focused;
                return audioTray && IsSelectionItemSelected(audioTray.Get()) &&
                    SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) && focused &&
                    AutomationIdOf(focused.Get()) == focusedBeforeRemovalId;
            }), "Guide reopen did not restore visible main overlay, exact Audio selection, "
                    "and surviving keyboard focus after catalog removal");
    const auto postRemovalPlacementCount = TextField(
        postRemovalPlacement, "placement-count=");
    const auto postRemovalChromeHwnd = TextField(
        postRemovalPlacement, "actual=");
    const auto postRemovalTrayClient = TextField(
        postRemovalPlacement, "tray-client=");
    const auto postRemovalTrayScreen = TextField(
        postRemovalPlacement, "tray-screen=");
    const auto postRemovalTrayCorners = ParseBounds(postRemovalTrayScreen);
    Require(!postRemovalPlacementCount.empty() &&
                !postRemovalChromeHwnd.empty() &&
                !postRemovalTrayClient.empty() &&
                postRemovalTrayCorners.width > 0.0F &&
                postRemovalTrayCorners.height > 0.0F,
            "Catalog removal placement omitted exact fixed-chrome/tray authority; record=" +
                postRemovalPlacement);
    Require(TextField(postRemovalCompositionSample, "chrome-placement-count=") ==
                postRemovalPlacementCount &&
                TextField(postRemovalCompositionSample, "chrome-hwnd=") ==
                    postRemovalChromeHwnd &&
                TextField(postRemovalCompositionSample, "tray=") ==
                    postRemovalTrayScreen,
            "Catalog removal composition sample did not carry the exact committed "
            "fixed-chrome/tray authority; placement=" + postRemovalPlacement +
                " sample=" + postRemovalCompositionSample);
    sessionTrayAuthority = TrayPlacementAuthority{
        postRemovalPlacementCount,
        postRemovalChromeHwnd,
        postRemovalTrayClient,
        postRemovalTrayScreen,
        postRemovalTrayCorners,
    };
    postRemovalAudioAuthority = PaintAuthority{
        TextField(postRemovalAudioPaint, "target="),
        TextField(postRemovalAudioPaint, "sequence="),
    };
    const auto postRemovalSequence = parseSnapshotSequence(
        postRemovalAudioAuthority->sequence);
    Require(postRemovalAudioAuthority->target == "audio-mixer" &&
                postRemovalSequence.has_value(),
            "Post-removal Audio paint omitted exact target/sequence authority");
    SendKey(window, VK_DOWN);
    FenceWindow(window);
    const auto preSwitchLog = ReadUtf8(logPath);
    const auto preSwitchPaintAt = preSwitchLog.rfind(
        "Widget presentation paint target=audio-mixer content=admitted");
    Require(preSwitchPaintAt != std::string::npos &&
                preSwitchPaintAt >= removedCatalogBefore,
            "Down/fence did not leave a current admitted Audio paint authority");
    const auto preSwitchPaintEnd = preSwitchLog.find('\n', preSwitchPaintAt);
    const auto preSwitchPaint = preSwitchLog.substr(
        preSwitchPaintAt,
        preSwitchPaintEnd == std::string::npos
            ? std::string::npos : preSwitchPaintEnd - preSwitchPaintAt);
    preSwitchAudioAuthority = PaintAuthority{
        TextField(preSwitchPaint, "target="),
        TextField(preSwitchPaint, "sequence="),
    };
    const auto preSwitchSequence = parseSnapshotSequence(
        preSwitchAudioAuthority->sequence);
    Require(preSwitchAudioAuthority->target == "audio-mixer",
            "Down/fence changed the exact Audio target before the first switch");
    Require(TextField(preSwitchPaint, "rendered=") == "audio-mixer" &&
                TextField(preSwitchPaint, "semantics=") == "current",
            "Down/fence did not retain exact admitted/current Audio visual authority");
    Require(preSwitchPaint.find("tray-total=8") != std::string::npos &&
                preSwitchPaint.find("tray-visible=8") != std::string::npos &&
                preSwitchPaint.find("selected=audio-mixer") != std::string::npos &&
                preSwitchPaint.find("tray-selected-visible=true") !=
                    std::string::npos,
            "Down/fence did not preserve the validated eight-item Audio tray/catalog");
    Require(preSwitchSequence.has_value() &&
                *preSwitchSequence >= *postRemovalSequence,
            "Down/fence produced an invalid or regressed Audio snapshot sequence; "
            "post-removal=" + postRemovalAudioAuthority->sequence +
                " current=" + preSwitchAudioAuthority->sequence);
    const auto preSwitchSampleAt = preSwitchLog.find(
        "Composition child sample step=", preSwitchPaintAt + preSwitchPaint.size());
    Require(preSwitchSampleAt != std::string::npos,
            "Down/fence Audio paint was not followed by an exact composition child sample");
    const auto preSwitchSample = RecordLine(preSwitchLog, preSwitchSampleAt);
    const auto restoredPlacementCount = TextField(
        preSwitchSample, "chrome-placement-count=");
    const auto restoredChromeHwnd = TextField(preSwitchSample, "chrome-hwnd=");
    const auto restoredTrayScreen = TextField(preSwitchSample, "tray=");
    const auto restoredPlacementRevision = parseSnapshotSequence(restoredPlacementCount);
    const auto postRemovalPlacementRevision = parseSnapshotSequence(
        postRemovalPlacementCount);
    Require(preSwitchSample.find("chrome-applied-exact=true") != std::string::npos &&
                restoredPlacementRevision.has_value() &&
                postRemovalPlacementRevision.has_value() &&
                *restoredPlacementRevision >= *postRemovalPlacementRevision,
            "Guide restoration composition sample omitted an exact non-regressing "
            "fixed-chrome revision; sample=" + std::string(preSwitchSample));
    Require(restoredChromeHwnd == postRemovalChromeHwnd &&
                restoredTrayScreen == postRemovalTrayScreen,
            "Guide restoration changed fixed-chrome HWND or tray screen bounds; "
            "pre-hide-hwnd=" + postRemovalChromeHwnd +
                " restored-hwnd=" + restoredChromeHwnd +
                " pre-hide-tray=" + postRemovalTrayScreen +
                " restored-tray=" + restoredTrayScreen);
    const auto restoredChromeBounds = ParseBounds(restoredChromeHwnd);
    const auto restoredTrayCorners = ParseBounds(restoredTrayScreen);
    const auto postRemovalTrayClientBounds = ParseBounds(postRemovalTrayClient);
    const LoggedBounds restoredTrayClient{
        restoredTrayCorners.x - restoredChromeBounds.x,
        restoredTrayCorners.y - restoredChromeBounds.y,
        restoredTrayCorners.width,
        restoredTrayCorners.height,
    };
    Require(restoredTrayClient.x == postRemovalTrayClientBounds.x &&
                restoredTrayClient.y == postRemovalTrayClientBounds.y &&
                restoredTrayClient.width == postRemovalTrayClientBounds.width &&
                restoredTrayClient.height == postRemovalTrayClientBounds.height &&
                restoredTrayCorners.x == postRemovalTrayCorners.x &&
                restoredTrayCorners.y == postRemovalTrayCorners.y &&
                restoredTrayCorners.x + restoredTrayCorners.width ==
                    postRemovalTrayCorners.x + postRemovalTrayCorners.width &&
                restoredTrayCorners.y + restoredTrayCorners.height ==
                    postRemovalTrayCorners.y + postRemovalTrayCorners.height,
            "Guide restoration changed exact tray client/screen corner authority");
    sessionTrayAuthority = TrayPlacementAuthority{
        restoredPlacementCount,
        restoredChromeHwnd,
        postRemovalTrayClient,
        restoredTrayScreen,
        restoredTrayCorners,
    };
    const auto preBackLog = ReadUtf8(logPath);
    constexpr std::string_view compositionDisabledNeedle =
        "DirectComposition presentation disabled; using HWND fallback:";
    constexpr std::string_view compositionUnavailableNeedle =
        "DirectComposition unavailable; retaining HWND render-target fallback:";
    const auto compositionDisabledAt = preBackLog.rfind(compositionDisabledNeedle);
    const auto compositionUnavailableAt = preBackLog.rfind(compositionUnavailableNeedle);
    auto fallbackAt = compositionDisabledAt;
    if (compositionUnavailableAt != std::string::npos &&
        (fallbackAt == std::string::npos || compositionUnavailableAt > fallbackAt)) {
        fallbackAt = compositionUnavailableAt;
    }
    const auto preBackMode = CurrentPresentationMode(preBackLog);
    Require(preBackMode.has_value(),
            "Back route did not have a positive current rendering-mode authority");
    const bool compositionCurrent = *preBackMode;
    const bool fallbackCurrent = !compositionCurrent;
    const std::string backRenderMode = compositionCurrent
        ? "direct-composition"
        : "hwnd-fallback";
    firstDestinationUsesComposition = compositionCurrent;
    if (fallbackCurrent) {
        const bool disabledTransitionCurrent = fallbackAt == compositionDisabledAt;
        firstFallbackHasDisabledTransition = disabledTransitionCurrent;
        if (disabledTransitionCurrent)
            fallbackTransitionAuthorityBoundary = fallbackAt;
        else
            fallbackStartupAuthorityBoundary = fallbackAt;
    }
    const auto trayReturnBefore = preBackLog.size();
    std::string preBackNodeAuthority = "not-applicable";
    std::string preBackFocusedAutomationId = "not-applicable";
    std::string postBackFocusedAutomationId = "not-observed";
    bool postBackAudioTraySelected{};
    bool postBackAudioTrayFocused{};
    bool postBackBackNodePresent{};
    if (fallbackCurrent) {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "HWND fallback content root was unavailable before Back");
        ComPtr<IUIAutomationElement> back = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"host:host.open.back");
        Require(back && IsEnabled(back.Get()),
                "HWND fallback content root omitted exact enabled host:host.open.back before Back");
        preBackNodeAuthority = AutomationIdOf(back.Get()) +
            " enabled=" + (IsEnabled(back.Get()) ? "true" : "false");
        ComPtr<IUIAutomationElement> focused;
        Require(SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) && focused,
                "HWND fallback did not expose a focused widget element before Back");
        preBackFocusedAutomationId = AutomationIdOf(focused.Get());
        Require(!preBackFocusedAutomationId.empty() &&
                    preBackFocusedAutomationId.rfind("widget:", 0) == 0,
                "HWND fallback focused element was not an exact widget AutomationId before Back");
    }
    const auto captureBackFailure = [&](const std::string_view failure,
                                        const bool updateRegionPending,
                                        const RECT& updateRegion) {
        const auto log = ReadUtf8(logPath);
        const auto recordAt = [&](const std::string_view needle) {
            const auto at = log.find(needle, trayReturnBefore);
            if (at == std::string::npos) return std::string{"missing"};
            const auto end = log.find('\n', at);
            return log.substr(
                at, end == std::string::npos ? std::string::npos : end - at);
        };
        const HWND currentContentWindow = LocateHostWindow(
            host->Id(), L"WidgetRail.OverlayHost");
        const HWND currentChromeWindow = LocateHostWindow(
            host->Id(), L"WidgetRail.Chrome");
        RECT contentUpdate{};
        RECT chromeUpdate{};
        const bool contentUpdatePending = currentContentWindow &&
            GetUpdateRect(currentContentWindow, &contentUpdate, FALSE) != FALSE;
        const bool chromeUpdatePending = currentChromeWindow &&
            GetUpdateRect(currentChromeWindow, &chromeUpdate, FALSE) != FALSE;
        std::string postBackState = "content-root-unavailable";
        if (currentContentWindow) {
            ComPtr<IUIAutomationElement> contentRoot;
            if (SUCCEEDED(automation->ElementFromHandle(
                    currentContentWindow, contentRoot.GetAddressOf())) && contentRoot) {
                ComPtr<IUIAutomationElement> tray = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"tray:tray.audio-mixer");
                ComPtr<IUIAutomationElement> back = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"host:host.open.back");
                postBackAudioTraySelected = IsSelectionItemSelected(tray.Get());
                postBackAudioTrayFocused = IsKeyboardFocused(tray.Get());
                postBackBackNodePresent = static_cast<bool>(back);
                ComPtr<IUIAutomationElement> focused;
                if (SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) && focused)
                    postBackFocusedAutomationId = AutomationIdOf(focused.Get());
                postBackState = "tray-selected=" +
                    std::string(postBackAudioTraySelected ? "true" : "false") +
                    " tray-focused=" +
                    std::string(postBackAudioTrayFocused ? "true" : "false") +
                    " back-node=" +
                    std::string(postBackBackNodePresent ? "present" : "absent");
            }
        }
        const std::string classification = postBackBackNodePresent &&
                !postBackAudioTraySelected
            ? "unhandled-root-back"
            : "semantic-transition-without-required-tray-focus";
        std::string retainedLog = "not-supplied";
        if (!arguments.durableDiagnosticsDirectory.empty()) {
            std::error_code error;
            fs::create_directories(arguments.durableDiagnosticsDirectory, error);
            const auto destination = arguments.durableDiagnosticsDirectory /
                L"WidgetSwitchHostTests-back-overlay.log";
            if (!error) {
                fs::copy_file(logPath, destination,
                              fs::copy_options::overwrite_existing, error);
            }
            retainedLog = error
                ? "copy-failed=" + error.message()
                : destination.string();
        }
        return std::string(failure) +
            " render-mode=" + backRenderMode +
            " classification=" + classification +
            " pre-back-node=" + preBackNodeAuthority +
            " pre-focused=" + preBackFocusedAutomationId +
            " post-focused=" + postBackFocusedAutomationId +
            " post-state=" + postBackState +
            " content-hwnd=" + std::to_string(
                reinterpret_cast<std::uintptr_t>(currentContentWindow)) +
            " content-visible=" + std::string(currentContentWindow &&
                IsWindowVisible(currentContentWindow) != FALSE ? "true" : "false") +
            " chrome-hwnd=" + std::to_string(
                reinterpret_cast<std::uintptr_t>(currentChromeWindow)) +
            " chrome-visible=" + std::string(currentChromeWindow &&
                IsWindowVisible(currentChromeWindow) != FALSE ? "true" : "false") +
            " back-update-region=" + (updateRegionPending ? "pending" : "absent") +
            " rect=" + std::to_string(updateRegion.left) + "," +
                std::to_string(updateRegion.top) + "," +
                std::to_string(updateRegion.right) + "," +
                std::to_string(updateRegion.bottom) +
            " content-update-region=" + (contentUpdatePending ? "pending" : "absent") +
            " chrome-update-region=" + (chromeUpdatePending ? "pending" : "absent") +
            " post-boundary-first-paint=" + recordAt("Widget presentation paint") +
            " post-boundary-first-action=" + recordAt("Widget action") +
            " post-boundary-terminal-frame=" + recordAt(
                "Composition frame committed content=complete") +
            " post-boundary-child-sample=" + recordAt(
                "Composition child sample step=") +
            " retained-overlay-log=" + retainedLog;
    };
    SendKey(window, VK_ESCAPE);
    RECT backUpdateRegion{};
    const bool backUpdateRegionPending =
        GetUpdateRect(window, &backUpdateRegion, FALSE) != FALSE;
    const auto requireBackAuthority = [&](const bool condition,
                                          const std::string_view message) {
        if (!condition) {
            Fail(captureBackFailure(message, backUpdateRegionPending, backUpdateRegion));
        }
    };
    if (compositionCurrent) {
        requireBackAuthority(UpdateWindow(window) != FALSE,
            Win32Error("Back route did not synchronously realize the already-invalidated "
                       "content HWND"));
        std::string trayReturnSample;
        requireBackAuthority(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto log = ReadUtf8(logPath);
                    const auto sampleAt = log.find(
                        "Composition child sample step=", trayReturnBefore);
                    if (sampleAt == std::string::npos) return false;
                    const auto sampleEnd = log.find('\n', sampleAt);
                    trayReturnSample = log.substr(
                        sampleAt,
                        sampleEnd == std::string::npos
                            ? std::string::npos : sampleEnd - sampleAt);
                    return trayReturnSample.find("chrome-hwnd=") != std::string::npos &&
                        trayReturnSample.find("chrome-applied-exact=true") !=
                            std::string::npos;
                }), "Back route did not commit its next fixed-chrome composition sample");
        HWND chromeWindow = LocateHostWindow(host->Id(), L"WidgetRail.Chrome");
        requireBackAuthority(chromeWindow && IsWindow(chromeWindow) != FALSE,
                "Back fixed-chrome composition fence completed but current WidgetRail.Chrome "
                "HWND is missing");
        ComPtr<IUIAutomationElement> chromeRoot;
        requireBackAuthority(SUCCEEDED(automation->ElementFromHandle(
                    chromeWindow, chromeRoot.GetAddressOf())) && chromeRoot,
                "Current WidgetRail.Chrome HWND did not resolve its UIA root after "
                "the fixed-chrome composition fence");
        ComPtr<IUIAutomationElement> audioTray = FindAutomationElement(
            automation.Get(), chromeRoot.Get(), L"tray:tray.audio-mixer");
        requireBackAuthority(audioTray,
                "Current WidgetRail.Chrome UIA root omitted exact tray:tray.audio-mixer "
                "after the fixed-chrome composition fence");
        const HWND currentChromeWindow = LocateHostWindow(
            host->Id(), L"WidgetRail.Chrome");
        requireBackAuthority(currentChromeWindow == chromeWindow,
                "WidgetRail.Chrome HWND identity changed during immediate Back authority pin");
        ComPtr<IUIAutomationElement> currentChromeRoot;
        requireBackAuthority(SUCCEEDED(automation->ElementFromHandle(
                    currentChromeWindow, currentChromeRoot.GetAddressOf())) && currentChromeRoot,
                "WidgetRail.Chrome UIA root disappeared during immediate Back authority pin");
        ComPtr<IUIAutomationElement> currentAudioTray = FindAutomationElement(
            automation.Get(), currentChromeRoot.Get(), L"tray:tray.audio-mixer");
        requireBackAuthority(currentAudioTray,
                "Exact tray:tray.audio-mixer UIA identity disappeared during immediate "
                "Back authority pin");
        BOOL sameChromeRoot{};
        BOOL sameAudioTray{};
        requireBackAuthority(SUCCEEDED(automation->CompareElements(
                    chromeRoot.Get(), currentChromeRoot.Get(), &sameChromeRoot)) &&
                    sameChromeRoot,
                "WidgetRail.Chrome UIA root identity drifted during immediate Back authority pin");
        requireBackAuthority(SUCCEEDED(automation->CompareElements(
                    audioTray.Get(), currentAudioTray.Get(), &sameAudioTray)) &&
                    sameAudioTray,
                "Exact tray:tray.audio-mixer UIA identity drifted during immediate Back "
                "authority pin");
        requireBackAuthority(IsSelectionItemSelected(currentAudioTray.Get()),
                "Exact tray:tray.audio-mixer SelectionItemIsSelected=false immediately "
                "before first Right");
        requireBackAuthority(IsKeyboardFocused(currentAudioTray.Get()),
                "Exact tray:tray.audio-mixer HasKeyboardFocus=false immediately before "
                "first Right");
    } else {
        requireBackAuthority(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    ComPtr<IUIAutomationElement> contentRoot;
                    if (!window || FAILED(automation->ElementFromHandle(
                            window, contentRoot.GetAddressOf())) || !contentRoot) {
                        return false;
                    }
                    ComPtr<IUIAutomationElement> tray = FindAutomationElement(
                        automation.Get(), contentRoot.Get(), L"tray:tray.audio-mixer");
                    ComPtr<IUIAutomationElement> back = FindAutomationElement(
                        automation.Get(), contentRoot.Get(), L"host:host.open.back");
                    postBackAudioTraySelected = IsSelectionItemSelected(tray.Get());
                    postBackAudioTrayFocused = IsKeyboardFocused(tray.Get());
                    postBackBackNodePresent = static_cast<bool>(back);
                    ComPtr<IUIAutomationElement> focused;
                    if (SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) && focused)
                        postBackFocusedAutomationId = AutomationIdOf(focused.Get());
                    return tray && postBackAudioTraySelected && postBackAudioTrayFocused;
                }), "HWND fallback did not restore exact selected keyboard focus to "
                    "tray:tray.audio-mixer after Back");
        requireBackAuthority(window && IsWindow(window) != FALSE,
                "HWND fallback lost the current content HWND after Back");
        ComPtr<IUIAutomationElement> contentRoot;
        requireBackAuthority(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "HWND fallback content HWND did not resolve its UIA root after exact "
                "Audio tray paint");
        ComPtr<IUIAutomationElement> audioTray = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"tray:tray.audio-mixer");
        requireBackAuthority(audioTray,
                "HWND fallback content UIA root omitted exact tray:tray.audio-mixer");
        ComPtr<IUIAutomationElement> currentContentRoot;
        requireBackAuthority(SUCCEEDED(automation->ElementFromHandle(
                    window, currentContentRoot.GetAddressOf())) && currentContentRoot,
                "HWND fallback content UIA root disappeared during immediate Back "
                "authority pin");
        ComPtr<IUIAutomationElement> currentAudioTray = FindAutomationElement(
            automation.Get(), currentContentRoot.Get(), L"tray:tray.audio-mixer");
        requireBackAuthority(currentAudioTray,
                "HWND fallback exact tray:tray.audio-mixer disappeared during immediate "
                "Back authority pin");
        BOOL sameContentRoot{};
        BOOL sameAudioTray{};
        requireBackAuthority(SUCCEEDED(automation->CompareElements(
                    contentRoot.Get(), currentContentRoot.Get(), &sameContentRoot)) &&
                    sameContentRoot,
                "HWND fallback content UIA root identity drifted during immediate Back "
                "authority pin");
        requireBackAuthority(SUCCEEDED(automation->CompareElements(
                    audioTray.Get(), currentAudioTray.Get(), &sameAudioTray)) &&
                    sameAudioTray,
                "HWND fallback exact tray:tray.audio-mixer UIA identity drifted during "
                "immediate Back authority pin");
        requireBackAuthority(IsSelectionItemSelected(currentAudioTray.Get()),
                "HWND fallback exact tray:tray.audio-mixer SelectionItemIsSelected=false "
                "immediately before first Right");
        requireBackAuthority(IsKeyboardFocused(currentAudioTray.Get()),
                "HWND fallback exact tray:tray.audio-mixer HasKeyboardFocus=false "
                "immediately before first Right");
    }
    for (std::size_t index = 1; index < kTargets.size(); ++index)
        switchTo(kTargets[index], kTargets[index - 1]);
    // Geometry proof is the ordinary one-at-a-time cycle above. The
    // deliberately rapid reversal below is a separate lifecycle scenario
    // whose second selection is itself allowed one whole-tray repaint.
    const auto ordinaryCycleGeometryEnd = ReadUtf8(logPath).size();

    const auto reversalBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_LEFT);
    SendKey(window, VK_RIGHT);
    const auto settingsSelection = waitForPaint(
        reversalBefore, kTargets.back(), kTargets.back().id, "admitted");
    requireTrayPlacementSample(
        reversalBefore, settingsSelection, kTargets.back().id,
        "rapid-reversal-settings-admitted");

    std::string selectionRefreshCompletion;
    std::string selectionRefreshTransition;
    std::string selectionRefreshRequest;
    std::string selectionRefreshGeneration;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                std::size_t anchors{};
                bool invalidAnchor{};
                selectionRefreshTransition.clear();
                selectionRefreshRequest.clear();
                selectionRefreshGeneration.clear();
                for (auto at = current.find("Admission trace", reversalBefore);
                     at != std::string::npos;
                     at = current.find("Admission trace", at + 1)) {
                    const auto record = RecordLine(current, at);
                    if (TextField(record, "stage=") != "request-queued" ||
                        TextField(record, "action=") != "deduplicated" ||
                        TextField(record, "reason=") != "existing-request" ||
                        (TextField(record, "target=") != "settings" &&
                         TextField(record, "widget=") != "settings")) {
                        continue;
                    }
                    ++anchors;
                    const auto transition = TextField(record, "transition=");
                    const auto request = TextField(record, "request=");
                    const auto generation = TextField(record, "generation=");
                    const bool exact =
                        TextField(record, "selected=") == "settings" &&
                        TextField(record, "active=") == "settings" &&
                        TextField(record, "target=") == "settings" &&
                        TextField(record, "widget=") == "settings" &&
                        TextField(record, "lifecycle=") == "visible" &&
                        TextField(record, "kind=") == "snapshot" &&
                        ParsePositiveSequence(transition) &&
                        ParsePositiveSequence(request) &&
                        ParsePositiveSequence(generation);
                    invalidAnchor = invalidAnchor || !exact;
                    selectionRefreshTransition = transition;
                    selectionRefreshRequest = request;
                    selectionRefreshGeneration = generation;
                }
                return !invalidAnchor && anchors == 1;
            }),
            "Post-reversal Settings selection refresh did not expose exactly one positive "
            "deduplicated Snapshot anchor; log=" +
                ReadUtf8(logPath).substr(reversalBefore));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                bool refreshPosted{};
                bool refreshDequeued{};
                std::size_t terminals{};
                bool invalidTerminal{};
                selectionRefreshCompletion.clear();
                for (auto at = current.find("Admission trace", reversalBefore);
                     at != std::string::npos;
                     at = current.find("Admission trace", at + 1)) {
                    const auto record = RecordLine(current, at);
                    const auto stage = TextField(record, "stage=");
                    if (TextField(record, "transition=") == selectionRefreshTransition &&
                        TextField(record, "widget=") == "settings") {
                        if (stage == "refresh-posted" &&
                            TextField(record, "posted=") == "true") {
                            refreshPosted = true;
                        } else if (stage == "refresh-dequeued") {
                            refreshDequeued = true;
                        }
                    }
                    if (stage == "request-completed" &&
                        TextField(record, "request=") == selectionRefreshRequest) {
                        ++terminals;
                        const bool exact =
                            TextField(record, "transition=") == selectionRefreshTransition &&
                            TextField(record, "selected=") == "settings" &&
                            TextField(record, "active=") == "settings" &&
                            TextField(record, "target=") == "settings" &&
                            TextField(record, "widget=") == "settings" &&
                            TextField(record, "generation=") ==
                                selectionRefreshGeneration &&
                            TextField(record, "lifecycle=") == "visible" &&
                            TextField(record, "kind=") == "snapshot" &&
                            TextField(record, "disposition=") == "admitted";
                        invalidTerminal = invalidTerminal || !exact;
                        selectionRefreshCompletion = std::string(record);
                    }
                }
                return refreshPosted && refreshDequeued &&
                    !invalidTerminal && terminals == 1;
            }),
            "Post-reversal Settings selection refresh did not terminally reach exact "
            "Snapshot disposition=admitted before focus admission; log=" +
                ReadUtf8(logPath).substr(reversalBefore));
    Require(TextField(selectionRefreshCompletion, "widget=") == "settings" &&
                TextField(selectionRefreshCompletion, "transition=") ==
                    selectionRefreshTransition &&
                TextField(selectionRefreshCompletion, "request=") ==
                    selectionRefreshRequest &&
                TextField(selectionRefreshCompletion, "generation=") ==
                    selectionRefreshGeneration &&
                TextField(selectionRefreshCompletion, "lifecycle=") == "visible" &&
                TextField(selectionRefreshCompletion, "kind=") == "snapshot" &&
                TextField(selectionRefreshCompletion, "disposition=") == "admitted",
            "Post-reversal Settings deduplicated refresh terminated with stale, failed, "
            "cancelled, or mismatched authority; request=" + selectionRefreshRequest +
                " generation=" + selectionRefreshGeneration +
                " completion=" + selectionRefreshCompletion);
    FenceWindow(window);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto ready = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                const auto tray = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"tray:tray.settings");
                return ready && IsEnabled(ready.Get()) &&
                    !IsKeyboardFocused(ready.Get()) && tray &&
                    IsSelectionItemSelected(tray.Get()) && IsKeyboardFocused(tray.Get());
            }),
            "Admitted post-reversal Settings refresh did not restore exact Current "
            "presentation/UIA authority with no retained refresh owner; completion=" +
                selectionRefreshCompletion);

    const auto focusBefore = ReadUtf8(logPath).size();
    SendKey(window, VK_UP);
    const auto settingsFocused = waitForPaint(
        focusBefore, kTargets.back(), kTargets.back().id, "admitted");
    auto focusLog = ReadUtf8(logPath);
    const auto focusMode = CurrentPresentationMode(focusLog);
    Require(focusMode.has_value(),
            "Ordinary Settings focus movement lacked positive presentation-mode authority; record=" +
                settingsFocused);
    const bool focusUsesComposition = *focusMode;
    const std::string focusPresentationMode = focusUsesComposition
        ? "direct-composition"
        : "hwnd-fallback";
    Require(settingsFocused.find("semantic-focus=widget:settings-ready") != std::string::npos,
            "Ordinary Settings focus movement lost semantic focus; presentation-mode=" +
                focusPresentationMode + " record=" + settingsFocused);
    const auto focusDamage = ParseBounds(TextField(settingsFocused, "damage="));
    if (focusUsesComposition) {
        const auto focusSurface = ParseBounds(TextField(settingsFocused, "shell-bounds="));
        const bool paintOnly =
            settingsFocused.find("work=paint-only") != std::string::npos;
        if (paintOnly) {
            Require(focusDamage.width > 0.0F && focusDamage.height > 0.0F &&
                        focusDamage.width * focusDamage.height <
                            focusSurface.width * focusSurface.height,
                    "Ordinary Settings focus movement damaged the complete content surface; "
                    "presentation-mode=" + focusPresentationMode + " record=" + settingsFocused);
        } else {
            const auto selectionSequence = ParsePositiveSequence(
                TextField(settingsSelection, "sequence="));
            const auto focusedSequence = ParsePositiveSequence(
                TextField(settingsFocused, "sequence="));
            const auto parseExtent = [](const std::string& value) {
                std::optional<std::array<long long, 2>> extent;
                const auto separator = value.find('x');
                if (separator == std::string::npos) return extent;
                const auto width = ParsePositiveSequence(value.substr(0, separator));
                const auto height = ParsePositiveSequence(value.substr(separator + 1));
                if (width && height) extent = std::array{*width, *height};
                return extent;
            };
            const auto desiredExtent = parseExtent(
                TextField(settingsFocused, "desired-extent="));
            const auto presentedExtent = parseExtent(
                TextField(settingsFocused, "presented-extent="));
            const bool activeTransition = desiredExtent && presentedExtent &&
                *desiredExtent != *presentedExtent;
            const bool stableSizeShellFocus = desiredExtent && presentedExtent &&
                *desiredExtent == *presentedExtent;
            Require(settingsFocused.find("work=full") != std::string::npos &&
                        focusDamage.x == focusSurface.x &&
                        focusDamage.y == focusSurface.y &&
                        focusDamage.width == focusSurface.width &&
                        focusDamage.height == focusSurface.height &&
                        TextField(settingsFocused, "target=") == "settings" &&
                        TextField(settingsFocused, "rendered=") == "settings" &&
                        selectionSequence && focusedSequence &&
                        *focusedSequence >= *selectionSequence &&
                        settingsFocused.find("input-owner=widget") !=
                            std::string::npos &&
                        settingsFocused.find("visual-focus=settings-ready") !=
                            std::string::npos &&
                        settingsFocused.find(
                            "semantic-focus=widget:settings-ready") !=
                            std::string::npos &&
                        (activeTransition || stableSizeShellFocus),
                    "Ordinary Settings focus movement lacked an exact bounded "
                    "active-transition or stable-size shell-focus full-raster "
                    "fallback; presentation-mode=" +
                        focusPresentationMode + " record=" + settingsFocused);
            requireTrayPlacementSample(
                focusBefore, settingsFocused, kTargets.back().id,
                "settings-focus-active-transition-full-raster");
        }
    } else {
        const auto focusedAt = focusLog.find(settingsFocused, focusBefore);
        const auto focusedSequence = ParsePositiveSequence(TextField(settingsFocused, "sequence="));
        Require(focusedAt != std::string::npos && focusedSequence.has_value(),
                "Ordinary Settings fallback focus paint lacked exact record/sequence authority; record=" +
                    settingsFocused);
        FallbackCheckpointSelection focusCheckpoint;
        const auto checkpointAt = focusLog.rfind("Fallback placement mode=hwnd-fallback phase=", focusedAt);
        Require(checkpointAt != std::string::npos, "Ordinary Settings fallback focus paint lacked a preceding geometry transaction");
        const auto checkpoint = RecordLine(focusLog, checkpointAt);
        const auto afterCheckpoint = checkpointAt + checkpoint.size();
        const std::array<std::string_view, 3> superseding{{
            "Widget presentation extent refresh", "Overlay render target resized in place",
            "Fallback placement mode=hwnd-fallback phase=set-window-pos"}};
        auto barrier = focusedAt;
        for (const auto marker : superseding) {
            const auto at = focusLog.find(marker, afterCheckpoint);
            if (at != std::string::npos) barrier = std::min(barrier, at);
        }
        const auto checkpointWindow = ParseBounds(TextField(checkpoint, "content-window="));
        bool interveningPaintsExact = true;
        for (auto paintAt = focusLog.find("Widget presentation paint target=settings", afterCheckpoint);
             paintAt != std::string::npos && paintAt < focusedAt;
             paintAt = focusLog.find("Widget presentation paint target=settings", paintAt + 1)) {
            const auto paint = RecordLine(focusLog, paintAt);
            const auto damage = ParseBounds(TextField(paint, "damage="));
            interveningPaintsExact = interveningPaintsExact &&
                ParsePositiveSequence(TextField(paint, "sequence=")) == focusedSequence &&
                damage.x == 0.0F && damage.y == 0.0F &&
                damage.width == checkpointWindow.width && damage.height == checkpointWindow.height;
        }
        Require(barrier == focusedAt && interveningPaintsExact && (TextField(checkpoint, "phase=") == "presentation-checkpoint" || TextField(checkpoint, "phase=") == "set-window-pos") && TextField(checkpoint, "widget=") == "settings" &&
                    TextField(checkpoint, "instance=") == "settings.default" &&
                    ParsePositiveSequence(TextField(checkpoint, "sequence=")) == focusedSequence &&
                    !TextField(checkpoint, "runtime=").empty() && !TextField(checkpoint, "presentation=").empty(),
                "Ordinary Settings fallback focus paint lacked an unambiguous preceding current checkpoint; checkpoint=" + std::string(checkpoint));
        focusCheckpoint.offset = checkpointAt;
        Require(settingsFocused.find("work=full") != std::string::npos &&
                    focusDamage.x == 0.0F && focusDamage.y == 0.0F &&
                    focusDamage.width == checkpointWindow.width &&
                    focusDamage.height == checkpointWindow.height,
                "Ordinary Settings focus movement did not use full-surface HWND fallback work; "
                "presentation-mode=" + focusPresentationMode + " record=" + settingsFocused +
                    " checkpoint=" + std::string(RecordLine(focusLog, *focusCheckpoint.offset)));
    }
    Require(waitForWidgetAutomation(L"widget:settings-ready", true),
            "Current Settings presentation omitted its actionable widget UIA node");
    const auto settingsInteractive = settingsFocused;
    const auto settingsInteractiveSequence = TextField(settingsInteractive, "sequence=");
    const auto settingsInteractiveSequenceValue = ParsePositiveSequence(settingsInteractiveSequence);
    Require(settingsInteractiveSequenceValue.has_value(),
            "Current Settings interactive presentation omitted its positive sequence");
    const auto settingsInteractiveLog = ReadUtf8(logPath);
    const auto settingsInteractiveAt = settingsInteractiveLog.find(
        settingsInteractive, focusBefore);
    Require(settingsInteractiveAt != std::string::npos,
            "Current Settings interactive presentation record was not retained in the log");
    const auto interactiveMode = focusMode;
    if (*interactiveMode) {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return ReadUtf8(logPath).find(
                        "Composition frame committed", settingsInteractiveAt +
                        settingsInteractive.size()) != std::string::npos;
                }), "Current Settings interactive composition presentation did not complete");
    } else {
        const auto interactiveLog = ReadUtf8(logPath);
        const auto authorityAt = interactiveLog.rfind(
            "Fallback placement mode=hwnd-fallback phase=", settingsInteractiveAt);
        Require(authorityAt != std::string::npos,
                "Current Settings interactive HWND fallback presentation lacked preceding geometry authority");
        const auto authority = RecordLine(interactiveLog, authorityAt);
        const auto authorityEnd = authorityAt + authority.size();
        bool superseded = false;
        for (const auto marker : {"Widget presentation extent refresh",
                                  "Overlay render target resized in place",
                                  "Fallback placement mode=hwnd-fallback phase=set-window-pos"}) {
            const auto at = interactiveLog.find(marker, authorityEnd);
            superseded = superseded || (at != std::string::npos && at < settingsInteractiveAt);
        }
        Require(!superseded && TextField(authority, "widget=") == "settings" &&
                    TextField(authority, "instance=") == "settings.default" &&
                    ParsePositiveSequence(TextField(authority, "sequence=")) ==
                        settingsInteractiveSequenceValue &&
                    !TextField(authority, "runtime=").empty() &&
                    !TextField(authority, "presentation=").empty(),
                "Current Settings interactive HWND fallback presentation lacked exact current geometry authority; record=" +
                    std::string(authority));
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(window, contentRoot.GetAddressOf())) ||
                    !contentRoot) return false;
                const auto ready = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                return ready && IsEnabled(ready.Get()) && IsKeyboardFocused(ready.Get());
            }), "Current Settings widget Ready UIA node was not enabled and keyboard-focused before block handshake");

    if (!*interactiveMode) {
        const auto primingBefore = ReadUtf8(logPath).size();
        invokeCurrentSettingsReady();
        std::string primedSettings;
        std::string primedCheckpoint;
        std::string primingCandidates;
        RECT primedWindow{};
        RECT primedClient{};
        POINT primedOrigin{};
        const bool primingConverged = WaitUntil(kOperationTimeoutMilliseconds, [&] {
            const auto current = ReadUtf8(logPath);
            RECT liveWindow{};
            RECT liveClient{};
            POINT liveOrigin{};
            if (GetWindowRect(window, &liveWindow) == FALSE ||
                GetClientRect(window, &liveClient) == FALSE ||
                ClientToScreen(window, &liveOrigin) == FALSE) return false;
            const auto describeRect = [](const RECT& rect) {
                return std::to_string(rect.left) + "," + std::to_string(rect.top) + "," +
                    std::to_string(rect.right - rect.left) + "," +
                    std::to_string(rect.bottom - rect.top);
            };
            const RECT clientScreen{liveOrigin.x, liveOrigin.y,
                liveOrigin.x + liveClient.right - liveClient.left,
                liveOrigin.y + liveClient.bottom - liveClient.top};
            const auto correlation = CorrelateLatestSettingsFallbackPresentation(
                current, primingBefore, describeRect(liveWindow), describeRect(clientScreen));
            primingCandidates = correlation.diagnostics;
            if (!correlation.paintOffset || !correlation.checkpointOffset) return false;
            FenceWindow(window);
            const auto fenced = ReadUtf8(logPath);
            liveOrigin = {};
            if (GetWindowRect(window, &liveWindow) == FALSE ||
                GetClientRect(window, &liveClient) == FALSE ||
                ClientToScreen(window, &liveOrigin) == FALSE) return false;
            const RECT fencedClientScreen{liveOrigin.x, liveOrigin.y,
                liveOrigin.x + liveClient.right - liveClient.left,
                liveOrigin.y + liveClient.bottom - liveClient.top};
            const auto fencedCorrelation = CorrelateLatestSettingsFallbackPresentation(
                fenced, primingBefore, describeRect(liveWindow),
                describeRect(fencedClientScreen));
            primingCandidates = fencedCorrelation.diagnostics;
            if (!fencedCorrelation.paintOffset || !fencedCorrelation.checkpointOffset) return false;
            primedSettings = std::string(RecordLine(fenced, *fencedCorrelation.paintOffset));
            primedCheckpoint = std::string(RecordLine(fenced, *fencedCorrelation.checkpointOffset));
            primedWindow = liveWindow;
            primedClient = liveClient;
            primedOrigin = liveOrigin;
            return true;
        });
        Require(primingConverged, "Unarmed Settings Ready priming did not atomically converge its latest equal extent, "
                "current checkpoint, and live HWND geometry; candidates=" + primingCandidates +
                " post-priming-log=" + ReadUtf8(logPath).substr(primingBefore));
        const auto checkpointWindow = ParseBounds(TextField(primedCheckpoint, "content-window="));
        const auto checkpointClient = ParseBounds(TextField(primedCheckpoint, "content-client-screen="));
        Require(primedWindow.left == static_cast<LONG>(checkpointWindow.x) &&
                    primedWindow.top == static_cast<LONG>(checkpointWindow.y) &&
                    primedWindow.right - primedWindow.left == static_cast<LONG>(checkpointWindow.width) &&
                    primedWindow.bottom - primedWindow.top == static_cast<LONG>(checkpointWindow.height) &&
                    primedOrigin.x == static_cast<LONG>(checkpointClient.x) &&
                    primedOrigin.y == static_cast<LONG>(checkpointClient.y) &&
                    primedClient.right - primedClient.left == static_cast<LONG>(checkpointClient.width) &&
                    primedClient.bottom - primedClient.top == static_cast<LONG>(checkpointClient.height),
                "Unarmed Settings Ready priming fallback checkpoint geometry was not current; record=" +
                    primedCheckpoint);
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    ComPtr<IUIAutomationElement> contentRoot;
                    if (FAILED(automation->ElementFromHandle(
                            window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                    const auto ready = FindAutomationElement(
                        automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                    return ready && IsKeyboardFocused(ready.Get()) && IsEnabled(ready.Get());
                }), "Unarmed Settings Ready priming did not restore exact current Ready UIA authority");
    }

    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                for (auto at = current.find("Admission trace", focusBefore);
                     at != std::string::npos;
                     at = current.find("Admission trace", at + 1)) {
                    const auto record = RecordLine(current, at);
                    if (TextField(record, "widget=") == "settings" &&
                        TextField(record, "stage=") == "request-completed" &&
                        TextField(record, "lifecycle=") == "interactive" &&
                        TextField(record, "kind=") == "lifecycle" &&
                        TextField(record, "disposition=") == "admitted") {
                        return true;
                    }
                }
                return false;
            }), "Settings interactive lifecycle request did not terminally complete before the blocked refresh; log=" +
                ReadUtf8(logPath).substr(focusBefore));

    const auto ordinaryRefreshBefore = ReadUtf8(logPath).size();
    const auto ordinaryRefreshEpoch = blockSnapshot();
    const auto handshakeLog = ReadUtf8(logPath).substr(ordinaryRefreshBefore);
    Require(handshakeLog.find("kind=lifecycle") == std::string::npos,
            "Ready-action block handshake introduced a lifecycle transition; log=" + handshakeLog);
    auto blockedRefreshBefore = ReadUtf8(logPath).size();
    std::string fallbackBlockedCheckpoint;
    if (!*interactiveMode) {
        std::string blockedCandidates;
        const auto describeWindow = [](const RECT& window, const RECT& client, const POINT& origin) {
            return std::array<std::string, 2>{
                std::to_string(window.left) + "," + std::to_string(window.top) + "," +
                    std::to_string(window.right-window.left) + "," + std::to_string(window.bottom-window.top),
                std::to_string(origin.x) + "," + std::to_string(origin.y) + "," +
                    std::to_string(client.right-client.left) + "," + std::to_string(client.bottom-client.top)};
        };
        const auto selectBlockedFallback = [&](const std::string_view log,
                                                const std::array<std::string, 2>& geometry) {
            std::optional<std::string> selected;
            for (auto paintAt = log.find("Widget presentation paint target=settings", ordinaryRefreshBefore);
                 paintAt != std::string::npos;
                 paintAt = log.find("Widget presentation paint target=settings", paintAt + 1)) {
                const auto paint = RecordLine(log, paintAt);
                const auto sequence = ParsePositiveSequence(TextField(paint, "sequence="));
                if (!sequence || TextField(paint, "desired-extent=") != TextField(paint, "presented-extent="))
                    continue;
                const auto authorityAt = log.rfind(
                    "Fallback placement mode=hwnd-fallback phase=", paintAt);
                if (authorityAt == std::string::npos || authorityAt < ordinaryRefreshBefore) continue;
                const auto authority = RecordLine(log, authorityAt);
                const auto authorityEnd = authorityAt + authority.size();
                bool superseded = false;
                for (const auto marker : {"Widget presentation extent refresh",
                                          "Overlay render target resized in place",
                                          "Fallback placement mode=hwnd-fallback phase=set-window-pos"}) {
                    const auto at = log.find(marker, authorityEnd);
                    superseded = superseded || (at != std::string::npos && at < paintAt);
                }
                if (!superseded && TextField(authority, "widget=") == "settings" &&
                    TextField(authority, "instance=") == "settings.default" &&
                    ParsePositiveSequence(TextField(authority, "sequence=")) == sequence &&
                    TextField(authority, "content-window=") == geometry[0] &&
                    TextField(authority, "content-client-screen=") == geometry[1])
                    selected = std::string(authority);
            }
            return selected;
        };
        const bool blockedConverged = WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    RECT liveWindow{}, liveClient{};
                    POINT origin{};
                    if (!GetWindowRect(window, &liveWindow) || !GetClientRect(window, &liveClient) ||
                        !ClientToScreen(window, &origin)) return false;
                    const auto geometry = describeWindow(liveWindow, liveClient, origin);
                    const auto correlation = selectBlockedFallback(current, geometry);
                    blockedCandidates = correlation ? *correlation : "none";
                    if (!correlation) return false;
                    FenceWindow(window);
                    const auto fenced = ReadUtf8(logPath);
                    origin = {};
                    if (!GetWindowRect(window, &liveWindow) || !GetClientRect(window, &liveClient) ||
                        !ClientToScreen(window, &origin)) return false;
                    const auto fencedGeometry = describeWindow(liveWindow, liveClient, origin);
                    const auto fencedCorrelation = selectBlockedFallback(fenced, fencedGeometry);
                    blockedCandidates = fencedCorrelation ? *fencedCorrelation : "none";
                    if (!fencedCorrelation) return false;
                    fallbackBlockedCheckpoint = *fencedCorrelation;
                    return true;
                });
        Require(blockedConverged,
                "Blocked Settings fallback cascade did not converge under the blocked render; candidates=" +
                    blockedCandidates);
        blockedRefreshBefore = ReadUtf8(logPath).size();
    }
    FenceWindow(window);
    Require(waitForWidgetAutomation(L"widget:settings-ready", false),
            "RefreshRetained exposed stale widget UIA/action authority");
    RECT pendingRefreshPaint{};
    Require(GetUpdateRect(window, &pendingRefreshPaint, FALSE) == FALSE,
            "Exact RefreshRetained scheduled an inert content raster");
    const auto heldRefreshLog = ReadUtf8(logPath).substr(
        *interactiveMode ? ordinaryRefreshBefore : blockedRefreshBefore);
    const auto retainedPaintAt = heldRefreshLog.find(
        "Widget presentation paint target=settings content=refresh-retained");
    if (retainedPaintAt == std::string::npos) {
        Require(heldRefreshLog.find("Composition frame committed") ==
                        std::string::npos &&
                    heldRefreshLog.find("Composition child sample") ==
                        std::string::npos &&
                    heldRefreshLog.find("Widget presentation extent refresh") ==
                        std::string::npos,
                "Stable-size RefreshRetained changed paint, composition, or extent; log=" +
                    heldRefreshLog + " fallback-checkpoint=" + fallbackBlockedCheckpoint);
    } else {
        const auto retainedPaint = RecordLine(heldRefreshLog, retainedPaintAt);
        const auto desiredExtent = TextField(retainedPaint, "desired-extent=");
        const auto presentedExtent = TextField(retainedPaint, "presented-extent=");
        const auto damage = ParseBounds(TextField(retainedPaint, "damage="));
        const auto shellBounds = ParseBounds(TextField(retainedPaint, "shell-bounds="));
        Require(TextField(retainedPaint, "rendered=") == "settings" &&
                    TextField(retainedPaint, "sequence=") == settingsInteractiveSequence &&
                    TextField(retainedPaint, "input-owner=") == "widget" &&
                    TextField(retainedPaint, "semantics=") == "inert" &&
                    TextField(retainedPaint, "visual-focus=") == "none" &&
                    TextField(retainedPaint, "semantic-focus=") == "none" &&
                    TextField(retainedPaint, "work=") == "full" &&
                    desiredExtent != presentedExtent &&
                    damage.x == shellBounds.x && damage.y == shellBounds.y &&
                    damage.width == shellBounds.width && damage.height == shellBounds.height,
                "Active-extent RefreshRetained did not preserve exact inert Settings authority; record=" +
                    std::string(retainedPaint));

        const auto extentAt = heldRefreshLog.rfind(
            "Widget presentation extent refresh widget=settings", retainedPaintAt);
        const auto placementAt = heldRefreshLog.find(
            "Composition placement committed content=complete",
            retainedPaintAt + retainedPaint.size());
        const auto motionAt = heldRefreshLog.find("Composition motion start", placementAt);
        Require(extentAt != std::string::npos && placementAt != std::string::npos &&
                    motionAt != std::string::npos && extentAt < retainedPaintAt &&
                    retainedPaintAt < placementAt && placementAt < motionAt &&
                    heldRefreshLog.find("Widget presentation extent refresh", extentAt + 1) ==
                        std::string::npos &&
                    heldRefreshLog.find("Composition placement committed content=complete", placementAt + 1) ==
                        std::string::npos &&
                    heldRefreshLog.find("Composition motion start", motionAt + 1) ==
                        std::string::npos,
                "Active-extent RefreshRetained omitted or duplicated its one placement transition; log=" +
                    heldRefreshLog);
        std::size_t retainedPaintsInTransition{};
        bool invalidRetainedAuthority{};
        for (auto paintAt = heldRefreshLog.find(
                 "Widget presentation paint target=", extentAt);
             paintAt != std::string::npos && paintAt < placementAt;
             paintAt = heldRefreshLog.find(
                 "Widget presentation paint target=", paintAt + 1)) {
            const auto paint = RecordLine(heldRefreshLog, paintAt);
            if (TextField(paint, "content=") != "refresh-retained") continue;
            ++retainedPaintsInTransition;
            const auto candidateDamage = ParseBounds(TextField(paint, "damage="));
            const auto candidateShell = ParseBounds(TextField(paint, "shell-bounds="));
            invalidRetainedAuthority = invalidRetainedAuthority ||
                TextField(paint, "target=") != "settings" ||
                TextField(paint, "rendered=") != "settings" ||
                TextField(paint, "sequence=") != settingsInteractiveSequence ||
                TextField(paint, "input-owner=") != "widget" ||
                TextField(paint, "semantics=") != "inert" ||
                TextField(paint, "visual-focus=") != "none" ||
                TextField(paint, "semantic-focus=") != "none" ||
                TextField(paint, "work=") != "full" ||
                TextField(paint, "desired-extent=") != desiredExtent ||
                TextField(paint, "presented-extent=") != presentedExtent ||
                candidateDamage.x != candidateShell.x ||
                candidateDamage.y != candidateShell.y ||
                candidateDamage.width != candidateShell.width ||
                candidateDamage.height != candidateShell.height;
        }
        Require(retainedPaintsInTransition == 1 && !invalidRetainedAuthority,
                "Active-extent RefreshRetained transition bracket did not contain "
                "exactly one matching inert Settings paint; log=" + heldRefreshLog);
        const auto extent = RecordLine(heldRefreshLog, extentAt);
        const auto placement = RecordLine(heldRefreshLog, placementAt);
        const auto motion = RecordLine(heldRefreshLog, motionAt);
        Require(TextField(extent, "from=") == presentedExtent &&
                    TextField(extent, "to=") == desiredExtent &&
                    TextField(extent, "identity=") == "retained" &&
                    TextField(extent, "target=") == "composition-motion" &&
                    TextField(placement, "order=") == "commit-motion-container" &&
                    TextField(placement, "from=") == TextField(motion, "from=") &&
                    TextField(placement, "to=") == TextField(motion, "to="),
                "Active-extent RefreshRetained did not atomically advance prior-presented to desired extent; paint=" +
                    std::string(retainedPaint) + " extent=" + std::string(extent) +
                    " placement=" + std::string(placement) + " motion=" + std::string(motion));

        const auto completeHeldLog = ReadUtf8(logPath);
        const auto retainedPaintGlobalAt = completeHeldLog.size() - heldRefreshLog.size() + retainedPaintAt;
        const auto priorSampleAt = completeHeldLog.rfind(
            "Composition child sample step=", retainedPaintGlobalAt);
        const auto firstSampleAt = heldRefreshLog.find("Composition child sample step=", motionAt);
        Require(priorSampleAt != std::string::npos && firstSampleAt != std::string::npos,
                "Active-extent RefreshRetained lacked its immediate pre/post setup samples; log=" +
                    heldRefreshLog);
        const auto priorSample = RecordLine(completeHeldLog, priorSampleAt);
        const auto firstSample = RecordLine(heldRefreshLog, firstSampleAt);
        const auto paintCount = [](const std::string_view sample, const std::string_view owner) {
            const auto paints = TextField(sample, "paints=");
            return TimingField(paints, owner);
        };
        const auto priorContentPaints = paintCount(priorSample, "content:");
        const auto priorGuidePaints = paintCount(priorSample, "guide:");
        const auto priorTrayPaints = paintCount(priorSample, "tray:");
        const auto retainedContentPaints = paintCount(firstSample, "content:");
        const auto retainedGuidePaints = paintCount(firstSample, "guide:");
        const auto retainedTrayPaints = paintCount(firstSample, "tray:");
        Require(sessionTrayAuthority.has_value() &&
                    retainedContentPaints == priorContentPaints + 1 &&
                    retainedGuidePaints == priorGuidePaints + 1 &&
                    retainedTrayPaints == priorTrayPaints &&
                    TextField(firstSample, "guide=") == TextField(priorSample, "guide=") &&
                    TextField(firstSample, "tray=") == sessionTrayAuthority->trayScreen &&
                    TextField(firstSample, "chrome-hwnd=") == sessionTrayAuthority->chromeHwnd &&
                    TextField(firstSample, "chrome-placement-count=") ==
                        sessionTrayAuthority->placementCount &&
                    TextField(firstSample, "selected=") == TextField(priorSample, "selected=") &&
                    firstSample.find("chrome-applied-exact=true") != std::string::npos,
                "Active-extent RefreshRetained changed fixed chrome or painted outside its one setup frame; prior=" +
                    std::string(priorSample) + " retained=" + std::string(firstSample));

        const auto heldLogStart = completeHeldLog.size() - heldRefreshLog.size();
        const auto motionGlobalAt = heldLogStart + motionAt;
        const auto firstSampleGlobalAt = heldLogStart + firstSampleAt;
        std::string settledMotionLog;
        std::size_t motionFinalAt{};
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    if (motionGlobalAt >= current.size() ||
                        RecordLine(current, motionGlobalAt) != motion) return false;
                    const auto finalAt = current.find(
                        "Composition motion final steps=", motionGlobalAt + motion.size());
                    const auto nextMotionAt = current.find(
                        "Composition motion start", motionGlobalAt + motion.size());
                    if (finalAt == std::string::npos ||
                        (nextMotionAt != std::string::npos && nextMotionAt < finalAt)) return false;
                    settledMotionLog = current;
                    motionFinalAt = finalAt;
                    return true;
                }), "Active-extent RefreshRetained motion did not reach its exact bounded final; motion=" +
                    std::string(motion));
        for (auto sampleAt = firstSampleGlobalAt;
             sampleAt != std::string::npos && sampleAt < motionFinalAt;
             sampleAt = settledMotionLog.find("Composition child sample step=", sampleAt + 1)) {
            const auto sample = RecordLine(settledMotionLog, sampleAt);
            Require(paintCount(sample, "content:") == retainedContentPaints &&
                        paintCount(sample, "guide:") == retainedGuidePaints &&
                        paintCount(sample, "tray:") == retainedTrayPaints &&
                        TextField(sample, "guide=") == TextField(firstSample, "guide=") &&
                        TextField(sample, "tray=") == sessionTrayAuthority->trayScreen &&
                        TextField(sample, "chrome-hwnd=") == sessionTrayAuthority->chromeHwnd &&
                        TextField(sample, "chrome-placement-count=") ==
                            sessionTrayAuthority->placementCount &&
                        TextField(sample, "selected=") == TextField(firstSample, "selected=") &&
                        sample.find("chrome-applied-exact=true") != std::string::npos,
                    "Active-extent RefreshRetained repainted or moved fixed chrome during motion; sample=" +
                        std::string(sample));
        }
    }
    const auto observerCorrelation = ParsePositiveSequence(selectionRefreshTransition);
    Require(observerCorrelation.has_value(),
            "Blocked Settings refresh lacked a positive retained selection correlation");
    const auto observerBefore = ReadUtf8(logPath).size();
    Require(PostMessageW(
                window, kSnapshotRefreshMessage,
                static_cast<WPARAM>(*observerCorrelation), 0) != FALSE,
            Win32Error("PostMessageW(correlated blocked snapshot observer)"));
    std::string observerRequest;
    std::string observerGeneration;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                std::size_t matches{};
                observerRequest.clear();
                observerGeneration.clear();
                for (auto at = current.find("Admission trace", observerBefore);
                     at != std::string::npos;
                     at = current.find("Admission trace", at + 1)) {
                    const auto record = RecordLine(current, at);
                    if (TextField(record, "transition=") != selectionRefreshTransition ||
                        TextField(record, "widget=") != "settings" ||
                        TextField(record, "stage=") != "request-queued" ||
                        TextField(record, "action=") != "deduplicated" ||
                        TextField(record, "reason=") != "existing-request" ||
                        TextField(record, "kind=") != "snapshot") {
                        continue;
                    }
                    ++matches;
                    observerRequest = TextField(record, "request=");
                    observerGeneration = TextField(record, "generation=");
                }
                return matches == 1 && ParsePositiveSequence(observerRequest) &&
                    ParsePositiveSequence(observerGeneration);
            }),
            "Blocked Settings owner did not admit exactly one positive correlated "
            "deduplicated Snapshot observer; log=" +
                ReadUtf8(logPath).substr(observerBefore));
    installation->ReleaseBlockedSnapshot(ordinaryRefreshEpoch);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(installation->BlockedSnapshotComplete(), ignored);
            }), "Ordinary Settings refresh did not complete after release");
    std::string ordinaryRefreshCompletion;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto current = ReadUtf8(logPath);
                std::size_t matches{};
                ordinaryRefreshCompletion.clear();
                for (auto at = current.find("Admission trace", observerBefore);
                     at != std::string::npos;
                     at = current.find("Admission trace", at + 1)) {
                    const auto record = RecordLine(current, at);
                    if (TextField(record, "transition=") == selectionRefreshTransition &&
                        TextField(record, "widget=") == "settings" &&
                        TextField(record, "stage=") == "request-completed" &&
                        TextField(record, "request=") == observerRequest &&
                        TextField(record, "generation=") == observerGeneration &&
                        TextField(record, "kind=") == "snapshot") {
                        ++matches;
                        ordinaryRefreshCompletion = std::string(record);
                    }
                }
                return matches == 1;
            }), "Released Settings snapshot omitted its exact deduplicated observer terminal; log=" +
                ReadUtf8(logPath).substr(observerBefore));
    Require(TextField(ordinaryRefreshCompletion, "transition=") ==
                    selectionRefreshTransition &&
                TextField(ordinaryRefreshCompletion, "request=") == observerRequest &&
                TextField(ordinaryRefreshCompletion, "generation=") ==
                    observerGeneration &&
                TextField(ordinaryRefreshCompletion, "lifecycle=") == "interactive" &&
                TextField(ordinaryRefreshCompletion, "disposition=") == "admitted",
            "Released Settings observer did not terminally restore exact Current authority; completion=" +
                ordinaryRefreshCompletion);
    const auto ordinaryRefreshLog = ReadUtf8(logPath);
    const std::string ordinaryRefreshNeedle =
        "Widget presentation paint target=settings content=admitted rendered=settings sequence=";
    const auto ordinaryRefreshPaintAt = ordinaryRefreshLog.find(
        ordinaryRefreshNeedle, ordinaryRefreshBefore);
    if (ordinaryRefreshPaintAt != std::string::npos) {
        const auto ordinaryRefreshAdmitted = RecordLine(
            ordinaryRefreshLog, ordinaryRefreshPaintAt);
        requireCurrentPresentationCompletion(
            ordinaryRefreshBefore, ordinaryRefreshAdmitted,
            kTargets.back().id, "ordinary-refresh-admitted", L"widget:settings-ready");
    } else {
        const auto currentCompletionAt = ordinaryRefreshLog.find(
            ordinaryRefreshCompletion, ordinaryRefreshBefore);
        Require(currentCompletionAt != std::string::npos,
                "Released Settings snapshot completion was not retained in the host log");
        const auto currentSuffix = ordinaryRefreshLog.substr(currentCompletionAt);
        Require(currentSuffix.find("Widget presentation extent refresh") ==
                        std::string::npos &&
                    currentSuffix.find("Overlay render target resized in place") ==
                        std::string::npos &&
                    currentSuffix.find("Composition placement committed") ==
                        std::string::npos &&
                    currentSuffix.find(
                        "Fallback placement mode=hwnd-fallback phase=set-window-pos") ==
                        std::string::npos,
                "No-raster Current admission changed Settings composition/chrome/geometry authority; log=" +
                    currentSuffix);
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto ready = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                return ready && IsEnabled(ready.Get()) && IsKeyboardFocused(ready.Get());
            }), "Fresh Current admission did not restore enabled/focused Settings UIA/action authority");

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
    const auto settingsRetained = waitForPaint(
        restartBefore, kTargets.back(), kTargets.back().id, "retained", true);
    requireCurrentPresentationCompletion(
        restartBefore, settingsRetained,
        kTargets.back().id, "same-destination-lifecycle-retained",
        {}, true, false);
    Require(waitForWidgetAutomation(L"widget:settings-ready", false),
            "Same-destination retained lifecycle exposed stale actionable Settings UIA authority");
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto log = ReadUtf8(logPath);
                return log.size() > restartBefore &&
                    log.find("Settings reloaded", restartBefore) != std::string::npos;
            }), "Same-identity Settings refresh omitted its bounded completion record.");
    const auto settingsLastGood = waitForPaint(
        restartBefore, kTargets.back(), kTargets.back().id, "admitted");
    const auto sameDestinationMode = CurrentPresentationMode(ReadUtf8(logPath));
    Require(sameDestinationMode.has_value(),
            "Same-destination refresh lacked positive presentation-mode authority");
    if (*sameDestinationMode) {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto pending = ReadUtf8(logPath);
                    return pending.find(
                        "Composition frame committed content=complete", restartBefore) !=
                        std::string::npos;
                }), "Same-destination refresh omitted its complete repaint commit");
    }
    requireCurrentPresentationCompletion(
        restartBefore, settingsLastGood,
        kTargets.back().id, "same-destination-snapshot-admitted",
        L"widget:settings-ready", true, true, true);
    const auto sameDestinationLog = ReadUtf8(logPath).substr(restartBefore);
    if (*sameDestinationMode) {
        Require(sameDestinationLog.find("Composition motion start") ==
                    std::string::npos &&
                    sameDestinationLog.find("Composition placement committed") ==
                    std::string::npos &&
                    sameDestinationLog.find("Widget presentation extent refresh") ==
                    std::string::npos,
                "Same-destination lifecycle/snapshot refresh restarted placement or motion; log=" +
                    sameDestinationLog);
    }

    if (arguments.geometryOnly) {
        const auto completeLog = ReadUtf8(logPath);
        const auto log = completeLog.substr(0, ordinaryCycleGeometryEnd);
        std::size_t motionAt{};
        std::size_t motionCount{};
        while ((motionAt = log.find("Composition motion start", motionAt)) !=
               std::string::npos) {
            const auto motionPaintAt = log.rfind(
                "Widget presentation paint", motionAt);
            Require(motionPaintAt != std::string::npos,
                    "Composition motion omitted its preceding paint authority");
            const auto motionPaintEnd = log.find('\n', motionPaintAt);
            const auto motionPaint = log.substr(
                motionPaintAt,
                motionPaintEnd == std::string::npos
                    ? std::string::npos : motionPaintEnd - motionPaintAt);
            const auto motionTarget = TextField(motionPaint, "target=");
            const auto motionSequence = TextField(motionPaint, "sequence=");
            const auto finalAt = log.find("Composition motion final steps=", motionAt);
            Require(finalAt != std::string::npos,
                    "Bounded geometry route found an unterminated composition motion");
            const auto nextMotionAt = log.find("Composition motion start", finalAt);
            const auto finalEnd = log.find('\n', finalAt);
            const auto segment = log.substr(
                motionAt, finalEnd == std::string::npos
                    ? std::string::npos : finalEnd - motionAt);
            std::size_t sampleAt{};
            std::size_t sampleCount{};
            std::string fixedGuide;
            std::uint64_t fixedTrayPaints{};
            while ((sampleAt = segment.find(
                        "Composition child sample step=", sampleAt)) !=
                   std::string::npos) {
                const auto end = segment.find('\n', sampleAt);
                const auto sample = segment.substr(
                    sampleAt, end == std::string::npos
                        ? std::string::npos : end - sampleAt);
                const auto guide = TextField(sample, "guide=");
                const auto tray = TextField(sample, "tray=");
                const auto placementCount = TextField(
                    sample, "chrome-placement-count=");
                const auto chromeHwnd = TextField(sample, "chrome-hwnd=");
                const auto paints = TextField(sample, "paints=");
                const auto trayPaintAt = paints.find("tray:");
                Require(trayPaintAt != std::string::npos,
                        "Motion sample omitted the retained tray paint counter");
                const auto trayPaints = paints.substr(trayPaintAt);
                const auto trayPaintCount = TimingField(trayPaints, "tray:");
                Require(sample.find("uia-content-transform=matched") !=
                            std::string::npos,
                        "Motion sample omitted shared content/UIA coordinate authority");
                const auto motionDetails = placementAuthorityDetails(
                    "composition-motion", motionTarget, motionSequence,
                    placementCount, chromeHwnd,
                    sessionTrayAuthority ? sessionTrayAuthority->trayClient : "missing",
                    tray);
                Require(sessionTrayAuthority.has_value() &&
                            sample.find("chrome-applied-exact=true") !=
                                std::string::npos,
                        "Motion sample omitted exact fixed-chrome authority;" +
                            motionDetails);
                Require(placementCount == sessionTrayAuthority->placementCount,
                        "Motion sample crossed a fixed-chrome placement revision;" +
                            motionDetails);
                Require(chromeHwnd == sessionTrayAuthority->chromeHwnd,
                        "Motion sample crossed a fixed-chrome HWND rectangle;" +
                            motionDetails);
                if (sampleCount == 0) {
                    fixedGuide = guide;
                    fixedTrayPaints = trayPaintCount;
                } else {
                    Require(guide == fixedGuide,
                            "Actual guide bounds moved during content motion; guide=" +
                                fixedGuide + " -> " + guide);
                    Require(trayPaintCount == fixedTrayPaints,
                            "Content motion repainted the retained tray surface; "
                            "expected=" + std::to_string(fixedTrayPaints) +
                            " observed=" + std::to_string(trayPaintCount) +
                            " sample=" + sample);
                }
                Require(tray == sessionTrayAuthority->trayScreen,
                        "Motion sample changed the exact screen-projected tray rectangle; "
                        "expected=" + sessionTrayAuthority->trayScreen +
                            " observed=" + tray + ";" + motionDetails);
                ++sampleCount;
                sampleAt = end == std::string::npos ? segment.size() : end + 1;
            }
            Require(sampleCount >= 3 && sampleCount <= 20,
                    "Bounded motion expected 3..20 start/mid/end child-coordinate "
                    "samples; observed " + std::to_string(sampleCount));
            ++motionCount;
            motionAt = nextMotionAt == std::string::npos
                ? log.size() : nextMotionAt;
        }
        Require(log.find("DirectComposition presentation disabled") ==
                    std::string::npos,
                "Eight-widget geometry route fell back from retained child visuals");

        std::vector<DWORD> observedProcessIds;
        const auto childRoles = ObservedChildRoles(host->Id(), &observedProcessIds);
        observedProcessIds.push_back(host->Id());
        std::cout << "Production host provenance scenario=eight-widget-geometry-only"
                  << " root-pid=" << host->Id()
                  << " root-start-filetime=" << ProcessStartFileTime(host->Process())
                  << " profile=" << WideToUtf8(installation->ProcessProfile())
                  << " commit=" << arguments.repositoryCommit
                  << " host-sha256=" << arguments.hostSha256
                  << " child-roles=" << childRoles
                  << " motions=" << motionCount << '\n';
        host.reset();
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    return std::none_of(
                        observedProcessIds.begin(), observedProcessIds.end(),
                        ProcessIsRunning);
                }), "Bounded geometry route left an observed bridge/worker process running.");
        installation.reset();
        return;
    }

    std::vector<DWORD> beforeRecoveryProcessIds;
    (void)ObservedChildRoles(host->Id(), &beforeRecoveryProcessIds);
    recoveryProcessIds.insert(
        recoveryProcessIds.end(), beforeRecoveryProcessIds.begin(),
        beforeRecoveryProcessIds.end());
    const auto bridgeBeforeSelection =
        DirectChildProcessIds(host->Id(), L"WidgetBridge.exe");
    Require(bridgeBeforeSelection.size() == 1,
            "Slow-worker scenario did not begin with one authoritative bridge process");

    {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "Blocked Settings route lacked a current content root before Back");
        const auto back = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"host:host.open.back");
        Require(back && IsEnabled(back.Get()),
                "Blocked Settings route lacked exact enabled host Back authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(back->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Blocked Settings host Back authority lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Blocked Settings exact host Back invocation failed");
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto tray = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"tray:tray.settings");
                const auto ready = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                return tray && IsSelectionItemSelected(tray.Get()) &&
                    IsKeyboardFocused(tray.Get()) &&
                    (!ready || !IsKeyboardFocused(ready.Get()));
            }), "Blocked Settings route did not return to exact selected/focused tray authority before selection-away");
    {
        const HWND trayWindow = *interactiveMode
            ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
            : window;
        Require(trayWindow && IsWindow(trayWindow) != FALSE,
                "Blocked Settings setup lacked its mode-authoritative tray HWND");
        ComPtr<IUIAutomationElement> trayRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    trayWindow, trayRoot.GetAddressOf())) && trayRoot,
                "Blocked Settings setup lacked its mode-authoritative tray root");
        const auto settingsTray = FindAutomationElement(
            automation.Get(), trayRoot.Get(), L"tray:tray.settings");
        Require(settingsTray && IsSelectionItemSelected(settingsTray.Get()) &&
                    IsKeyboardFocused(settingsTray.Get()) && IsEnabled(settingsTray.Get()),
                "Blocked Settings setup lost exact selected/focused Settings tray authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(settingsTray->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Blocked Settings setup tray authority lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Blocked Settings setup exact tray invocation failed");
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto ready = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"widget:settings-ready");
                return ready && IsEnabled(ready.Get()) && IsKeyboardFocused(ready.Get());
            }), "Blocked Settings setup did not restore exact Ready widget focus");
    const auto selectionRevokedEpoch = blockSnapshot();
    const auto selectionRevokedSequence = installation->BlockedSnapshotSequence(selectionRevokedEpoch);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto back = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"host:host.open.back");
                return back && IsEnabled(back.Get());
            }), "Blocked Settings route did not publish exact non-current host Back authority");
    {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "Blocked Settings route lost its content root before host Back");
        const auto back = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"host:host.open.back");
        Require(back && IsEnabled(back.Get()),
                "Blocked Settings route lost exact non-current host Back authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(back->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Blocked Settings non-current host Back lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Blocked Settings exact non-current host Back invocation failed");
    }
    const HWND blockedTrayWindow = *interactiveMode
        ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
        : window;
    Require(blockedTrayWindow && IsWindow(blockedTrayWindow) != FALSE,
            "Blocked Settings route lacked its mode-authoritative tray HWND");
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const HWND trayWindow = *interactiveMode
                    ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
                    : window;
                if (!trayWindow || trayWindow != blockedTrayWindow ||
                    IsWindow(trayWindow) == FALSE) return false;
                ComPtr<IUIAutomationElement> trayRoot;
                if (FAILED(automation->ElementFromHandle(
                        trayWindow, trayRoot.GetAddressOf())) || !trayRoot) return false;
                const auto tray = FindAutomationElement(
                    automation.Get(), trayRoot.Get(), L"tray:tray.settings");
                return tray && IsSelectionItemSelected(tray.Get()) &&
                    IsKeyboardFocused(tray.Get());
            }), "Blocked Settings request disturbed exact selected/focused mode-authoritative tray authority");
    const auto blockedBefore = ReadUtf8(logPath).size();
    const auto blockedNavigationStarted = std::chrono::steady_clock::now();
    {
        const HWND trayWindow = *interactiveMode
            ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
            : window;
        Require(trayWindow == blockedTrayWindow && IsWindow(trayWindow) != FALSE,
                "Blocked Settings route lost its mode-authoritative tray HWND before selecting Spotify");
        ComPtr<IUIAutomationElement> trayRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    trayWindow, trayRoot.GetAddressOf())) && trayRoot,
                "Blocked Settings route lacked its mode-authoritative tray root before selecting Spotify");
        const auto spotifyTray = FindAutomationElement(
            automation.Get(), trayRoot.Get(), L"tray:tray.spotify");
        Require(spotifyTray && IsEnabled(spotifyTray.Get()),
                "Blocked Settings route lacked exact enabled Spotify tray authority");
        ComPtr<IUIAutomationSelectionItemPattern> select;
        Require(SUCCEEDED(spotifyTray->GetCurrentPatternAs(
                    UIA_SelectionItemPatternId,
                    IID_PPV_ARGS(select.GetAddressOf()))) && select,
                "Blocked Settings Spotify tray authority lacked SelectionItemPattern");
        Require(SUCCEEDED(select->Select()),
                "Blocked Settings exact Spotify tray selection failed");
    }
    const auto spotifyWhileBlocked = waitForPaint(
        blockedBefore, kTargets[kTargets.size() - 2],
        kTargets[kTargets.size() - 2].id, "refresh-retained");
    const auto blockedNavigationMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - blockedNavigationStarted).count());
    const auto blockedNavigationLog = ReadUtf8(logPath);
    const auto blockedNavigationTransitionAt = blockedNavigationLog.find(
        TransitionNeedle(L"spotify"), blockedBefore);
    const auto blockedNavigationPaintAt = blockedNavigationLog.find(
        "Widget presentation paint target=spotify content=refresh-retained "
        "rendered=spotify sequence=", blockedBefore);
    Require(blockedNavigationTransitionAt != std::string::npos &&
                blockedNavigationPaintAt != std::string::npos,
            "Blocked navigation timing lacked exact Spotify transition/paint authority");
    const auto blockedNavigationHostFocusMilliseconds =
        DiagnosticLatencyMilliseconds(
            RecordContainingLine(blockedNavigationLog, blockedNavigationTransitionAt),
            RecordContainingLine(blockedNavigationLog, blockedNavigationPaintAt),
            "Blocked Spotify navigation timing");
    Require(spotifyWhileBlocked.find("semantic-focus=tray:spotify") !=
                std::string::npos,
            "Never-completing Settings snapshot disturbed ordinary tray focus.");
    Require(DirectChildProcessIds(host->Id(), L"WidgetBridge.exe") ==
                bridgeBeforeSelection && ProcessIsRunning(bridgeBeforeSelection.front()),
            "Selection-away replaced the healthy serialized bridge while Settings remained blocked");
    installation->ReleaseBlockedSnapshot(selectionRevokedEpoch);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return ReadUtf8(logPath).find(
                    "Dropped stale widget session completion for settings",
                    blockedBefore) != std::string::npos;
            }), "Selection-away did not record exact stale Settings revocation.");
    {
        const HWND trayWindow = *interactiveMode
            ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
            : window;
        Require(trayWindow == blockedTrayWindow && IsWindow(trayWindow) != FALSE,
                "Released Settings route lost its mode-authoritative tray HWND");
        ComPtr<IUIAutomationElement> trayRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    trayWindow, trayRoot.GetAddressOf())) && trayRoot,
                "Released Settings route lacked its mode-authoritative tray root");
        const auto spotifyTray = FindAutomationElement(
            automation.Get(), trayRoot.Get(), L"tray:tray.spotify");
        Require(spotifyTray && IsEnabled(spotifyTray.Get()) &&
                    IsSelectionItemSelected(spotifyTray.Get()) &&
                    IsKeyboardFocused(spotifyTray.Get()),
                "Released Settings route lost exact selected/focused Spotify tray authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(spotifyTray->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Released Settings Spotify tray authority lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Released Settings exact Spotify tray invocation failed");
    }
    (void)waitForPaint(
        blockedBefore, kTargets[kTargets.size() - 2],
        kTargets[kTargets.size() - 2].id, "admitted");
    Require(DirectChildProcessIds(host->Id(), L"WidgetBridge.exe") ==
                bridgeBeforeSelection && ProcessIsRunning(bridgeBeforeSelection.front()),
            "Released stale Settings completion replaced the healthy serialized bridge");
    const auto bridgeSelectionContinuityMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - blockedNavigationStarted).count());

    {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "Spotify reselection boundary lacked its current content root");
        const auto back = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"host:host.open.back");
        Require(back && IsEnabled(back.Get()),
                "Spotify reselection boundary lacked exact enabled host Back authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(back->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Spotify reselection host Back lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Spotify reselection exact host Back invocation failed");
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const HWND trayWindow = *interactiveMode
                    ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
                    : window;
                if (!trayWindow || IsWindow(trayWindow) == FALSE) return false;
                ComPtr<IUIAutomationElement> trayRoot;
                if (FAILED(automation->ElementFromHandle(
                        trayWindow, trayRoot.GetAddressOf())) || !trayRoot) return false;
                const auto spotifyTray = FindAutomationElement(
                    automation.Get(), trayRoot.Get(), L"tray:tray.spotify");
                return spotifyTray && IsSelectionItemSelected(spotifyTray.Get()) &&
                    IsKeyboardFocused(spotifyTray.Get());
            }), "Spotify reselection boundary did not return exact focus to its tray item");
    const auto reselectionBefore = ReadUtf8(logPath).size();
    const auto blockedReselectionStarted = std::chrono::steady_clock::now();
    {
        const HWND trayWindow = *interactiveMode
            ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
            : window;
        Require(trayWindow && IsWindow(trayWindow) != FALSE,
                "Settings reselection lacked its mode-authoritative tray HWND");
        ComPtr<IUIAutomationElement> trayRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    trayWindow, trayRoot.GetAddressOf())) && trayRoot,
                "Settings reselection lacked its mode-authoritative tray root");
        const auto settingsTray = FindAutomationElement(
            automation.Get(), trayRoot.Get(), L"tray:tray.settings");
        Require(settingsTray && IsEnabled(settingsTray.Get()),
                "Settings reselection lacked exact enabled tray authority");
        ComPtr<IUIAutomationSelectionItemPattern> select;
        Require(SUCCEEDED(settingsTray->GetCurrentPatternAs(
                    UIA_SelectionItemPatternId,
                    IID_PPV_ARGS(select.GetAddressOf()))) && select,
                "Settings reselection tray authority lacked SelectionItemPattern");
        Require(SUCCEEDED(select->Select()),
                "Settings reselection exact tray selection failed");
    }
    const auto settingsReselected = waitForPaint(
        reselectionBefore, kTargets.back(), kTargets.back().id, "admitted");
    const auto blockedReselectionMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - blockedReselectionStarted).count());
    const auto blockedReselectionLog = ReadUtf8(logPath);
    const auto blockedReselectionTransitionAt = blockedReselectionLog.find(
        TransitionNeedle(L"settings"), reselectionBefore);
    const auto blockedReselectionPaintAt = blockedReselectionLog.find(
        "Widget presentation paint target=settings content=admitted "
        "rendered=settings sequence=", reselectionBefore);
    Require(blockedReselectionTransitionAt != std::string::npos &&
                blockedReselectionPaintAt != std::string::npos,
            "Blocked reselection timing lacked exact Settings transition/paint authority");
    const auto blockedReselectionHostFocusMilliseconds =
        DiagnosticLatencyMilliseconds(
            RecordContainingLine(blockedReselectionLog, blockedReselectionTransitionAt),
            RecordContainingLine(blockedReselectionLog, blockedReselectionPaintAt),
            "Blocked Settings reselection timing");
    const auto lastGoodBody = ParseBounds(TextField(settingsLastGood, "body-bounds="));
    const auto reselectedBody = ParseBounds(TextField(settingsReselected, "body-bounds="));
    const auto lastGoodViewport = ParseBounds(
        TextField(settingsLastGood, "viewport-bounds="));
    const auto reselectedViewport = ParseBounds(
        TextField(settingsReselected, "viewport-bounds="));
    const auto lastGoodSequence = ParsePositiveSequence(
        TextField(settingsLastGood, "sequence="));
    const auto reselectedSequence = ParsePositiveSequence(
        TextField(settingsReselected, "sequence="));
    Require(lastGoodSequence && reselectedSequence &&
                *reselectedSequence >= *lastGoodSequence &&
                reselectedBody.width == lastGoodBody.width &&
                reselectedBody.height == lastGoodBody.height &&
                reselectedViewport.width == lastGoodViewport.width &&
                reselectedViewport.height == lastGoodViewport.height,
            "Late revoked result regressed authority or changed retained content/extent on reselection; before=" +
                settingsLastGood + " after=" + settingsReselected);

    const auto rearmBefore = ReadUtf8(logPath).size();
    {
        std::error_code ignored;
        fs::remove(restartSignal, ignored);
    }
    PostKey(window, VK_F5);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(restartSignal, ignored);
            }), "Replacement bridge did not restart Settings before the close-time seam.");
    (void)waitForPaint(rearmBefore, kTargets.back(), kTargets.back().id, "retained");
    (void)waitForPaint(rearmBefore, kTargets.back(), kTargets.back().id, "admitted");

    const auto bridgeBeforeClose =
        DirectChildProcessIds(host->Id(), L"WidgetBridge.exe");
    Require(bridgeBeforeClose.size() == 1,
            "Close-time cancellation did not begin with one authoritative bridge process");

    const auto closeRevokedEpoch = blockSnapshot();
    const auto closeRevokedSequence = installation->BlockedSnapshotSequence(closeRevokedEpoch);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                ComPtr<IUIAutomationElement> contentRoot;
                if (FAILED(automation->ElementFromHandle(
                        window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                const auto back = FindAutomationElement(
                    automation.Get(), contentRoot.Get(), L"host:host.open.back");
                return back && IsEnabled(back.Get());
            }), "Close-time blocked Settings route omitted exact non-current host Back authority");
    {
        ComPtr<IUIAutomationElement> contentRoot;
        Require(SUCCEEDED(automation->ElementFromHandle(
                    window, contentRoot.GetAddressOf())) && contentRoot,
                "Close-time blocked Settings route lost its content root before host Back");
        const auto back = FindAutomationElement(
            automation.Get(), contentRoot.Get(), L"host:host.open.back");
        Require(back && IsEnabled(back.Get()),
                "Close-time blocked Settings route lost exact non-current host Back authority");
        ComPtr<IUIAutomationInvokePattern> invoke;
        Require(SUCCEEDED(back->GetCurrentPatternAs(
                    UIA_InvokePatternId, IID_PPV_ARGS(invoke.GetAddressOf()))) && invoke,
                "Close-time blocked Settings host Back lacked InvokePattern");
        Require(SUCCEEDED(invoke->Invoke()),
                "Close-time blocked Settings exact host Back invocation failed");
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const HWND trayWindow = *interactiveMode
                    ? LocateHostWindow(host->Id(), L"WidgetRail.Chrome")
                    : window;
                if (!trayWindow || IsWindow(trayWindow) == FALSE) return false;
                ComPtr<IUIAutomationElement> trayRoot;
                if (FAILED(automation->ElementFromHandle(
                        trayWindow, trayRoot.GetAddressOf())) || !trayRoot) return false;
                const auto settingsTray = FindAutomationElement(
                    automation.Get(), trayRoot.Get(), L"tray:tray.settings");
                return settingsTray && IsSelectionItemSelected(settingsTray.Get()) &&
                    IsKeyboardFocused(settingsTray.Get());
            }), "Close-time blocked Settings host Back did not restore exact tray authority");
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
    installation->ReleaseBlockedSnapshot(closeRevokedEpoch);

    const auto lateBoundary = ReadUtf8(logPath).size();
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(reopen after late snapshot)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) != FALSE;
            }), "Host did not reopen after cancellation-ignoring worker completion.");
    const auto settingsAfterLate = waitForPaint(
        lateBoundary, kTargets.back(), kTargets.back().id, "admitted");
    Require(DirectChildProcessIds(host->Id(), L"WidgetBridge.exe") ==
                bridgeBeforeClose && ProcessIsRunning(bridgeBeforeClose.front()),
            "Reopen replaced the healthy serialized bridge after a stale close-time completion");
    const auto bridgeCloseContinuityMilliseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - closeStarted).count());
    const auto afterLateBody = ParseBounds(
        TextField(settingsAfterLate, "body-bounds="));
    const auto afterLateViewport = ParseBounds(
        TextField(settingsAfterLate, "viewport-bounds="));
    const auto afterLateSequence = ParsePositiveSequence(
        TextField(settingsAfterLate, "sequence="));
    Require(afterLateSequence && lastGoodSequence &&
                *afterLateSequence >= *lastGoodSequence &&
                afterLateBody.width == lastGoodBody.width &&
                afterLateBody.height == lastGoodBody.height &&
                afterLateViewport.width == lastGoodViewport.width &&
                afterLateViewport.height == lastGoodViewport.height &&
                settingsAfterLate.find("semantic-focus=tray:settings") !=
                    std::string::npos,
            "Reopened Settings authority regressed or changed retained extent/focus after stale drops; "
                "selection-revoked-sequence=" + std::to_string(selectionRevokedSequence) +
                " close-revoked-sequence=" + std::to_string(closeRevokedSequence) +
                " before=" + settingsLastGood + " after=" + settingsAfterLate);

    std::vector<std::uint64_t> hostFocusMilliseconds =
        inputToRetainedMilliseconds;
    hostFocusMilliseconds.push_back(blockedNavigationHostFocusMilliseconds);
    hostFocusMilliseconds.push_back(blockedReselectionHostFocusMilliseconds);
    std::sort(hostFocusMilliseconds.begin(), hostFocusMilliseconds.end());
    const auto hostFocusP95 = InclusiveLinearPercentile(
        std::vector<double>(hostFocusMilliseconds.begin(), hostFocusMilliseconds.end()), 0.95);
    std::string hostFocusSamples;
    for (const auto sample : hostFocusMilliseconds) {
        if (!hostFocusSamples.empty()) hostFocusSamples += ",";
        hostFocusSamples += std::to_string(sample);
    }
    Require(hostFocusP95 && *hostFocusP95 <= 50.0,
            "Host focus p95 exceeded 50 ms while worker completion was isolated; p95=" +
                (hostFocusP95 ? std::to_string(*hostFocusP95) : "invalid") +
                " samples=" + hostFocusSamples);
    std::cout << "Slow-worker response timing ms host-focus-p95="
              << *hostFocusP95
              << " tray-navigation=" << blockedNavigationMilliseconds
              << " tray-navigation-host=" << blockedNavigationHostFocusMilliseconds
              << " tray-reselection=" << blockedReselectionMilliseconds
              << " tray-reselection-host=" << blockedReselectionHostFocusMilliseconds
              << " b-dispatch=" << blockedCloseDispatchMilliseconds
              << " b-hide-complete=" << blockedCloseMilliseconds
              << " bridge-selection-continuity=" << bridgeSelectionContinuityMilliseconds
              << " bridge-close-continuity=" << bridgeCloseContinuityMilliseconds
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
    const auto finalPresentationMode = CurrentPresentationMode(ReadUtf8(logPath));
    Require(finalPresentationMode.has_value(),
            "Final switch route lacked positive presentation-mode authority");
    const bool finalUsesComposition = *finalPresentationMode;
    if (finalUsesComposition) {
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
    }

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
    std::size_t hiddenBoundary = hiddenLog.size();
    if (finalUsesComposition) {
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
        hiddenBoundary = retiredEnd == std::string::npos
            ? hiddenLog.size() : retiredEnd;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(220));
    const auto afterHidden = ReadUtf8(logPath);
    if (finalUsesComposition) {
        Require(afterHidden.find("Composition motion step", hiddenBoundary) ==
                    std::string::npos &&
                    afterHidden.find("Composition frame committed", hiddenBoundary) ==
                        std::string::npos,
                "Hidden host continued composition frame work after retirement");
    } else {
        Require(afterHidden.find("Widget presentation paint", hiddenBoundary) ==
                    std::string::npos,
                "Hidden HWND fallback host continued presentation paint work");
    }

    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(reopen hotkey)"));
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                return IsWindowVisible(window) != FALSE;
            }), "Composition host did not reopen after interrupted motion");
    if (finalUsesComposition) {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    const auto placement = current.find(
                        "Composition placement committed content=complete", hiddenBoundary);
                    return placement != std::string::npos &&
                        current.find("order=commit-place", placement) != std::string::npos &&
                        current.find("alpha=premultiplied-clear", placement) !=
                            std::string::npos;
                }), "Reopened host did not atomically place a transparent complete surface");
    } else {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto current = ReadUtf8(logPath);
                    constexpr std::string_view paintNeedle =
                        "Widget presentation paint target=audio-mixer content=admitted rendered=audio-mixer sequence=";
                    const auto paintAt = current.find(paintNeedle, hiddenBoundary);
                    if (paintAt == std::string::npos) return false;
                    const auto paint = RecordLine(current, paintAt);
                    const auto sequence = ParsePositiveSequence(TextField(paint, "sequence="));
                    if (!sequence) return false;
                    const auto checkpoint = SelectCurrentFallbackCheckpoint(
                        current, paintAt + paint.size(), "audio-mixer", *sequence);
                    if (!checkpoint.offset) return false;
                    const auto authority = RecordLine(current, *checkpoint.offset);
                    RECT liveWindow{}, liveClient{};
                    POINT origin{};
                    if (!GetWindowRect(window, &liveWindow) ||
                        !GetClientRect(window, &liveClient) ||
                        !ClientToScreen(window, &origin)) return false;
                    const auto rect = [](const LONG left, const LONG top,
                                         const LONG width, const LONG height) {
                        return std::to_string(left) + "," + std::to_string(top) + "," +
                            std::to_string(width) + "," + std::to_string(height);
                    };
                    if (TextField(authority, "content-window=") !=
                            rect(liveWindow.left, liveWindow.top,
                                 liveWindow.right - liveWindow.left,
                                 liveWindow.bottom - liveWindow.top) ||
                        TextField(authority, "content-client-screen=") !=
                            rect(origin.x, origin.y,
                                 liveClient.right - liveClient.left,
                                 liveClient.bottom - liveClient.top)) return false;
                    ComPtr<IUIAutomationElement> contentRoot;
                    if (FAILED(automation->ElementFromHandle(
                            window, contentRoot.GetAddressOf())) || !contentRoot) return false;
                    const auto tray = FindAutomationElement(
                        automation.Get(), contentRoot.Get(), L"tray:tray.audio-mixer");
                    return tray && IsSelectionItemSelected(tray.Get()) &&
                        IsKeyboardFocused(tray.Get());
                }), "Reopened HWND fallback host lacked exact admitted paint/checkpoint/live HWND/UIA authority");
    }

    const auto log = ReadUtf8(logPath);
    Require(log.find("Render-target resize failed") == std::string::npos,
            "A production switch forced render-target recreation.");
    Require(log.find(" surface=widget shell=shared ") != std::string::npos,
            "Production diagnostics omitted the shared-shell decision.");
    if (log.find("intrinsic-passes=0 surface-axis=fillAvailable/preferred") ==
        std::string::npos) {
        std::string axisRecords;
        for (auto at = log.find("surface-axis="); at != std::string::npos;) {
            const auto lineStart = log.rfind('\n', at);
            const auto recordAt = lineStart == std::string::npos ? 0 : lineStart + 1;
            const auto record = RecordLine(log, recordAt);
            axisRecords += " [" + std::string(record) + "]";
            at = log.find("surface-axis=", at + 1);
        }
        const auto admittedAt = log.find(
            "Widget presentation paint target=network-controls content=admitted "
            "rendered=network-controls sequence=");
        const auto admitted = admittedAt == std::string::npos
            ? std::string_view{} : RecordLine(log, admittedAt);
        const auto sequence = ParsePositiveSequence(TextField(admitted, "sequence="));
        const auto checkpointAt = admittedAt == std::string::npos
            ? std::string::npos
            : log.find(
                "Fallback placement mode=hwnd-fallback phase=presentation-checkpoint",
                admittedAt + admitted.size());
        const auto checkpoint = checkpointAt == std::string::npos
            ? std::string_view{} : RecordLine(log, checkpointAt);
        const auto work = ParseBounds(TextField(checkpoint, "work="));
        const auto target = ParseBounds(TextField(checkpoint, "target="));
        const auto dpi = ParsePositiveSequence(TextField(checkpoint, "dpi="));
        float interfaceScale{};
        try {
            std::size_t consumed{};
            const auto value = TextField(checkpoint, "interface-scale=");
            interfaceScale = std::stof(value, &consumed);
            if (consumed != value.size()) interfaceScale = 0.0F;
        } catch (...) {
            interfaceScale = 0.0F;
        }
        const auto desired = TextField(admitted, "desired-extent=");
        const auto extentSeparator = desired.find('x');
        const auto desiredWidth = extentSeparator == std::string::npos
            ? std::optional<long long>{}
            : ParsePositiveSequence(desired.substr(0, extentSeparator));
        const auto desiredHeight = extentSeparator == std::string::npos
            ? std::optional<long long>{}
            : ParsePositiveSequence(desired.substr(extentSeparator + 1));
        const auto sideMarginPixels = dpi
            ? static_cast<long long>(std::llround(24.0 * static_cast<double>(*dpi) / 96.0))
            : 0LL;
        const auto availableWidthPixels =
            static_cast<long long>(std::llround(work.width)) - sideMarginPixels * 2;
        const double pixelsPerDesignDip = dpi
            ? static_cast<double>(*dpi) / 96.0 * interfaceScale
            : 0.0;
        const auto expectedWidthDip = pixelsPerDesignDip > 0.0
            ? static_cast<long long>(std::llround(
                static_cast<double>(availableWidthPixels) / pixelsPerDesignDip))
            : 0LL;
        const bool exactFallbackFillAvailable =
            !finalUsesComposition && admittedAt != std::string::npos &&
            sequence.has_value() &&
            TextField(admitted, "semantics=") == "current" &&
            TextField(admitted, "desired-extent=") ==
                TextField(admitted, "presented-extent=") &&
            checkpointAt != std::string::npos &&
            TextField(checkpoint, "widget=") == "network-controls" &&
            TextField(checkpoint, "instance=") == "network-controls.default" &&
            !TextField(checkpoint, "runtime=").empty() &&
            !TextField(checkpoint, "presentation=").empty() &&
            ParsePositiveSequence(TextField(checkpoint, "sequence=")) == sequence &&
            dpi.has_value() && interfaceScale > 0.0F &&
            desiredWidth && *desiredWidth == expectedWidthDip &&
            desiredHeight && *desiredHeight == 878 &&
            availableWidthPixels > 0 &&
            static_cast<long long>(std::llround(target.width)) == availableWidthPixels &&
            target.x >= work.x && target.y >= work.y &&
            target.x + target.width <= work.x + work.width &&
            target.y + target.height <= work.y + work.height;
        if (!exactFallbackFillAvailable) {
            std::string retainedLog = "not-supplied";
            if (!arguments.durableDiagnosticsDirectory.empty()) {
                std::error_code error;
                fs::create_directories(arguments.durableDiagnosticsDirectory, error);
                const auto destination = arguments.durableDiagnosticsDirectory /
                    L"WidgetSwitchHostTests-complete-overlay.log";
                if (!error) {
                    fs::copy_file(logPath, destination,
                                  fs::copy_options::overwrite_existing, error);
                }
                retainedLog = error ? "copy-failed=" + error.message()
                                    : destination.string();
            }
            Fail("Production host did not admit independent FillAvailable width without measurement; "
                 "surface-axis-records=" + axisRecords +
                 " admitted-record=" + std::string(admitted) +
                 " checkpoint-record=" + std::string(checkpoint) +
                 " expected-width-dip=" + std::to_string(expectedWidthDip) +
                 " available-width-px=" + std::to_string(availableWidthPixels) +
                 " retained-overlay-log=" + retainedLog);
        }
    }
    Require(log.find("intrinsic-passes=1 surface-axis=preferred/content") !=
                std::string::npos,
            "Production host did not perform one Content-height intrinsic pass.");
    constexpr std::uint64_t kTransitionBudgetMicroseconds = 100000;
    if (finalUsesComposition) {
        Require(log.find("DirectComposition complete-content presentation owner active") !=
                    std::string::npos &&
                    log.find(
                        "alpha=premultiplied-clear hwnd=no-redirection "
                        "opacity=composition-effect hwnd-background=none") !=
                    std::string::npos,
                "Production diagnostics omitted the active composition alpha owner.");
        Require(log.find("Composition motion start") != std::string::npos &&
                    log.find("anchor=bottom") != std::string::npos,
                "Variable widget extents omitted bottom-anchored composition continuity.");
        Require(log.find("DirectComposition presentation disabled") == std::string::npos &&
                    log.find("Overlay render target resized in place") == std::string::npos,
                "Positive composition route fell back to direct HWND presentation.");
        Require(drawTimings.size() == kTargets.size() &&
                    commitTimings.size() == drawTimings.size() &&
                    geometryTimings.size() == drawTimings.size(),
                "Production timing distribution omitted a widget transition.");
        Require(*std::max_element(drawTimings.begin(), drawTimings.end()) <=
                    kTransitionBudgetMicroseconds &&
                    *std::max_element(commitTimings.begin(), commitTimings.end()) <=
                    kTransitionBudgetMicroseconds &&
                    *std::max_element(geometryTimings.begin(), geometryTimings.end()) <=
                    kTransitionBudgetMicroseconds,
                "Composition transition timing exceeded the bounded budget.");
        const auto maximumMotionCommit = motionCommitTimings.empty()
            ? 0ULL
            : *std::max_element(motionCommitTimings.begin(), motionCommitTimings.end());
        Require(maximumMotionCommit <= kTransitionBudgetMicroseconds,
                "Nonblocking composition motion commits exceeded the transition budget; max-us=" +
                    std::to_string(maximumMotionCommit));
        std::cout << "Composition transition timing us draw-max="
                  << *std::max_element(drawTimings.begin(), drawTimings.end())
                  << " commit-max=" << *std::max_element(commitTimings.begin(), commitTimings.end())
                  << " geometry-max=" << *std::max_element(geometryTimings.begin(), geometryTimings.end())
                  << " motion-commit-max=" << maximumMotionCommit
                  << " samples=" << drawTimings.size() << '\n';
    } else {
        Require(log.find("DirectComposition presentation disabled") != std::string::npos,
                "HWND fallback proof lacked its positive presentation-mode marker");
    }
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
    std::vector<DWORD> observedProcessIds = std::move(recoveryProcessIds);
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
    if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) &&
        GetLastError() != ERROR_ACCESS_DENIED) {
        std::cerr << "WidgetSwitchHostTests failed: "
                  << Win32Error("SetProcessDpiAwarenessContext") << "\n";
        return 1;
    }
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized)) {
        std::cerr << "CoInitializeEx failed\n";
        return 1;
    }
    try {
        const auto arguments = ParseArguments(argc, argv);
        RunDiagnosticLatencyTableTests();
        RunInclusivePercentileTableTests();
        if (arguments.fallbackAuthoritySelectionOnly)
            RunFallbackCheckpointSelectionTests();
        else if (!arguments.fallbackAuthorityReplayLog.empty())
            RunFallbackCheckpointReplay(arguments);
        else
            RunRetentionScenario(arguments);
        std::cout << "WidgetSwitchHostTests passed\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "WidgetSwitchHostTests failed: " << error.what() << "\n";
        CoUninitialize();
        return 1;
    }
}
