#include "OverlayHostTestSupport.h"

#include <ole2.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <Windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <filesystem>
#include <iostream>
#include <memory>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
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
constexpr std::array<std::uint32_t, 4> kFullMotionOffsets{0, 32, 80, 145};

struct Arguments final {
    fs::path installation;
    fs::path fixtureWorker;
    std::optional<fs::path> evidenceRoot;
};

struct Frame final {
    int width{};
    int height{};
    std::vector<std::uint8_t> bgra;
};

struct Target final {
    const wchar_t* id;
    const wchar_t* label;
    UINT key;
    enum class Affinity { Audio, Network, Spotify, Games } affinity;
    std::uint32_t firstSnapshotDelayMilliseconds;
};

constexpr std::array<Target, 4> kTargets{{
    {L"audio-mixer", L"Audio Mixer", VK_RETURN, Target::Affinity::Audio, 0},
    {L"network-controls", L"Network Controls", VK_RIGHT, Target::Affinity::Network, 0},
    {L"spotify", L"Spotify", VK_RIGHT, Target::Affinity::Spotify, 240},
    {L"games-apps", L"Games & Apps", VK_RIGHT, Target::Affinity::Games, 320},
}};

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& fixtureWorker,
        const std::wstring_view profile) {
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
            (L"gba-widget-switch-" + std::wstring(profile) + L"-" + guidText);
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
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
    [[nodiscard]] fs::path StartupSignal(const std::wstring_view widgetId) const {
        return startupSignalRoot_ / (std::wstring(widgetId) + L".started");
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path startupSignalRoot_;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if ((argument == L"--installation" || argument == L"--fixture-worker" ||
             argument == L"--evidence-root") && index + 1 < argc) {
            if (argument == L"--installation") result.installation = argv[++index];
            else if (argument == L"--fixture-worker") result.fixtureWorker = argv[++index];
            else result.evidenceRoot = fs::path(argv[++index]);
        } else {
            Fail("Usage: WidgetSwitchHostTests --installation <dir> "
                 "--fixture-worker <exe> [--evidence-root <dir>]");
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

Frame CaptureFrame(HWND window) {
    Require(IsWindow(window) && IsWindowVisible(window),
            "Production host HWND became hidden during frame capture.");
    RECT client{};
    Require(GetClientRect(window, &client), Win32Error("GetClientRect"));
    const int width = client.right - client.left;
    const int height = client.bottom - client.top;
    Require(width > 0 && height > 0, "Production host client extent is empty.");

    HDC windowDc = GetDC(window);
    Require(windowDc != nullptr, Win32Error("GetDC"));
    HDC memoryDc = CreateCompatibleDC(windowDc);
    if (!memoryDc) {
        ReleaseDC(window, windowDc);
        Fail(Win32Error("CreateCompatibleDC"));
    }
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(info.bmiHeader);
    info.bmiHeader.biWidth = width;
    info.bmiHeader.biHeight = -height;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* bits{};
    HBITMAP bitmap = CreateDIBSection(windowDc, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (!bitmap || !bits) {
        DeleteDC(memoryDc);
        ReleaseDC(window, windowDc);
        Fail(Win32Error("CreateDIBSection"));
    }
    HGDIOBJ oldBitmap = SelectObject(memoryDc, bitmap);
    // Read the HWND client surface itself. PrintWindow composites color-keyed
    // pixels as black on some DWM versions, which would manufacture the exact
    // artifact this fixture is designed to detect.
    if (!BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, SRCCOPY)) {
        Require(PrintWindow(window, memoryDc, PW_CLIENTONLY), Win32Error("PrintWindow"));
    }
    Frame frame{width, height, std::vector<std::uint8_t>(
        static_cast<std::size_t>(width) * static_cast<std::size_t>(height) * 4)};
    std::copy_n(static_cast<const std::uint8_t*>(bits), frame.bgra.size(), frame.bgra.begin());
    SelectObject(memoryDc, oldBitmap);
    DeleteObject(bitmap);
    DeleteDC(memoryDc);
    ReleaseDC(window, windowDc);
    return frame;
}

std::array<std::uint8_t, 3> Pixel(const Frame& frame, const int x, const int y) {
    const auto offset = (static_cast<std::size_t>(y) * frame.width + x) * 4;
    return {frame.bgra[offset + 2], frame.bgra[offset + 1], frame.bgra[offset]};
}

bool IsKey(const std::array<std::uint8_t, 3>& pixel) {
    return pixel[0] == 1 && pixel[1] == 2 && pixel[2] == 3;
}

bool HasAffinity(const std::array<std::uint8_t, 3>& p, const Target::Affinity affinity) {
    switch (affinity) {
    case Target::Affinity::Audio:
        return p[0] > 65 && p[0] > p[1] + 24 && p[0] > p[2] + 14;
    case Target::Affinity::Network:
        return p[2] > 60 && p[2] > p[0] + 26 && p[1] > p[0] + 12;
    case Target::Affinity::Spotify:
        return p[1] > 62 && p[1] > p[0] + 28 && p[1] > p[2] + 14;
    case Target::Affinity::Games:
        return p[0] > 70 && p[1] > p[2] + 20 && p[0] > p[2] + 30;
    }
    return false;
}

std::size_t AffinityPixels(const Frame& frame, const Target::Affinity affinity) {
    std::size_t count{};
    for (int y = 0; y < frame.height; ++y) {
        for (int x = 0; x < frame.width; ++x) {
            if (HasAffinity(Pixel(frame, x, y), affinity)) ++count;
        }
    }
    return count;
}

void ValidateCompleteFrame(const Frame& frame, const std::string_view context) {
    const auto requireContext = [&](const bool condition, const char* detail) {
        Require(condition, std::string(context) + ": " + detail);
    };
    std::size_t nonKey{};
    std::size_t lowerBandNonKey{};
    std::size_t pureBlack{};
    std::size_t maximumBlackRun{};
    for (int y = 0; y < frame.height; ++y) {
        std::size_t run{};
        for (int x = 0; x < frame.width; ++x) {
            const auto pixel = Pixel(frame, x, y);
            if (!IsKey(pixel)) {
                ++nonKey;
                if (y >= frame.height * 3 / 4) ++lowerBandNonKey;
            }
            if (pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 0) {
                ++pureBlack;
                maximumBlackRun = std::max(maximumBlackRun, ++run);
            } else {
                run = 0;
            }
        }
    }
    const auto pixels = static_cast<std::size_t>(frame.width) * frame.height;
    const auto lowerPixels = static_cast<std::size_t>(frame.width) * (frame.height / 4);
    // DWM substitutes the desktop (and PrintWindow substitutes black) for
    // authored color-key pixels. Exact key/fringe correctness is therefore
    // asserted by OverlayChromeTests before this compositor-level fixture;
    // here we reject only visible continuous clears and missing chrome/body.
    // The host intentionally leaves generous transparent margins around its
    // floating panel. Requiring one twelfth of the client still proves a
    // substantial authored shell while permitting every supported extent.
    requireContext(nonKey > pixels / 12, "frame omitted the authored shell/chrome");
    requireContext(lowerBandNonKey > lowerPixels / 5,
                   "host-owned tray/chrome disappeared from the lower band");
    requireContext(pureBlack < pixels / 20, "frame exposed a cleared black surface");
    requireContext(maximumBlackRun < static_cast<std::size_t>(frame.width) / 3,
                   "frame exposed a continuous black border/spacing run");
}

void SavePng(IWICImagingFactory* factory, const fs::path& path, const Frame& frame) {
    fs::create_directories(path.parent_path());
    ComPtr<IWICStream> stream;
    Require(SUCCEEDED(factory->CreateStream(stream.GetAddressOf())) && stream,
            "WIC could not create an evidence stream.");
    Require(SUCCEEDED(stream->InitializeFromFilename(path.c_str(), GENERIC_WRITE)),
            "WIC could not open the evidence PNG.");
    ComPtr<IWICBitmapEncoder> encoder;
    Require(SUCCEEDED(factory->CreateEncoder(
                GUID_ContainerFormatPng, nullptr, encoder.GetAddressOf())) && encoder,
            "WIC could not create a PNG encoder.");
    Require(SUCCEEDED(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache)),
            "WIC could not initialize the PNG encoder.");
    ComPtr<IWICBitmapFrameEncode> encodedFrame;
    Require(SUCCEEDED(encoder->CreateNewFrame(encodedFrame.GetAddressOf(), nullptr)) &&
                encodedFrame,
            "WIC could not create a PNG frame.");
    Require(SUCCEEDED(encodedFrame->Initialize(nullptr)), "WIC frame initialization failed.");
    Require(SUCCEEDED(encodedFrame->SetSize(frame.width, frame.height)),
            "WIC frame sizing failed.");
    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
    Require(SUCCEEDED(encodedFrame->SetPixelFormat(&format)) &&
                IsEqualGUID(format, GUID_WICPixelFormat32bppBGRA),
            "WIC rejected the BGRA evidence pixel format.");
    const UINT stride = static_cast<UINT>(frame.width * 4);
    Require(SUCCEEDED(encodedFrame->WritePixels(
                frame.height, stride, static_cast<UINT>(frame.bgra.size()),
                const_cast<BYTE*>(frame.bgra.data()))),
            "WIC could not write evidence pixels.");
    Require(SUCCEEDED(encodedFrame->Commit()) && SUCCEEDED(encoder->Commit()),
            "WIC could not commit the evidence PNG.");
}

class Evidence final {
public:
    Evidence(std::optional<fs::path> root, IWICImagingFactory* factory)
        : root_(std::move(root)), factory_(factory) {
        if (root_) fs::create_directories(*root_);
    }

    void FrameFile(
        const std::wstring_view profile,
        const std::wstring_view target,
        const std::wstring_view phase,
        const Frame& frame) {
        if (!root_) return;
        const auto relative = fs::path(profile) /
            (std::wstring(target) + L"-" + std::wstring(phase) + L".png");
        SavePng(factory_, *root_ / relative, frame);
        manifest_ << "    {\"profile\":\"" << WideToUtf8(profile)
                  << "\",\"target\":\"" << WideToUtf8(target)
                  << "\",\"phase\":\"" << WideToUtf8(phase)
                  << "\",\"width\":" << frame.width
                  << ",\"height\":" << frame.height
                  << ",\"file\":\"" << WideToUtf8(relative.generic_wstring())
                  << "\"},\n";
    }

    void Commit() {
        if (!root_) return;
        auto frames = manifest_.str();
        if (frames.size() >= 2) frames.erase(frames.size() - 2);
        WriteUtf8(*root_ / L"manifest.json",
            "{\n  \"assignment\":\"DLV-020\",\n"
            "  \"route\":\"production OverlayHost HWND keyboard mapping\",\n"
            "  \"physicalControllerEvidence\":\"separate\",\n"
            "  \"reducedMotionEvidence\":\"OverlayTransitionTests\",\n"
            "  \"frames\":[\n" + frames + "\n  ]\n}\n");
    }

private:
    std::optional<fs::path> root_;
    IWICImagingFactory* factory_{};
    std::ostringstream manifest_;
};

std::string TransitionNeedle(const std::wstring_view target) {
    return " to=" + WideToUtf8(target) + " extent=";
}

Frame WaitForTarget(HWND window, const Target& target) {
    Frame current;
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                FenceWindow(window);
                current = CaptureFrame(window);
                const auto total = static_cast<std::size_t>(current.width) * current.height;
                return AffinityPixels(current, target.affinity) > total / 12;
            }), "Production host did not paint the expected " + WideToUtf8(target.label) +
                    " fixture surface.");
    return current;
}

std::wstring OffsetName(const std::uint32_t offset) {
    return L"t" + std::to_wstring(offset) + L"ms";
}

Frame CaptureFullMotionSwitch(
    HWND window,
    const fs::path& logPath,
    const Target& target,
    const fs::path& startupSignal,
    const Target* previousTarget,
    const Frame* previousSettled,
    Evidence& evidence) {
    const auto before = ReadUtf8(logPath).size();
    if (target.firstSnapshotDelayMilliseconds > 0) {
        Require(previousTarget && previousSettled,
                "Delayed first snapshot requires a committed prior widget frame.");
        std::error_code ignored;
        fs::remove(startupSignal, ignored);
    }
    if (target.firstSnapshotDelayMilliseconds > 0) {
        SendKeyDownAndPostRelease(window, target.key);
    } else {
        SendKey(window, target.key);
    }
    if (target.firstSnapshotDelayMilliseconds > 0) {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    std::error_code ignored;
                    return fs::exists(startupSignal, ignored);
                }), "Delayed worker did not enter its first snapshot request for " +
                        WideToUtf8(target.label));
        const ULONGLONG started = GetTickCount64();
        for (const auto offset : kFullMotionOffsets) {
            const ULONGLONG due = started + offset;
            const auto now = GetTickCount64();
            if (now < due) Sleep(static_cast<DWORD>(due - now));
            auto frame = CaptureFrame(window);
            ValidateCompleteFrame(frame, WideToUtf8(target.label) + " delayed " +
                std::to_string(offset) + " ms");
            evidence.FrameFile(L"production-current", target.id, OffsetName(offset), frame);
            Require(frame.width == previousSettled->width &&
                        frame.height == previousSettled->height,
                    "Worker startup changed the committed host extent before snapshot admission.");
            const auto total = static_cast<std::size_t>(frame.width) * frame.height;
            Require(AffinityPixels(frame, previousTarget->affinity) > total / 12,
                    "Worker startup replaced the prior admitted content with a transient surface.");
            Require(AffinityPixels(frame, target.affinity) <= total / 12,
                    "Destination content appeared before its delayed snapshot was admitted.");
        }
    } else {
        const ULONGLONG started = GetTickCount64();
        for (const auto offset : kFullMotionOffsets) {
            const ULONGLONG due = started + offset;
            const auto now = GetTickCount64();
            if (now < due) Sleep(static_cast<DWORD>(due - now));
            FenceWindow(window);
            auto frame = CaptureFrame(window);
            ValidateCompleteFrame(frame, WideToUtf8(target.label) + " " +
                std::to_string(offset) + " ms");
            evidence.FrameFile(L"production-current", target.id, OffsetName(offset), frame);
        }
    }
    auto firstSnapshot = WaitForTarget(window, target);
    ValidateCompleteFrame(firstSnapshot, WideToUtf8(target.label) + " first snapshot");
    evidence.FrameFile(L"production-current", target.id, L"snapshot-t0ms", firstSnapshot);
    auto transitionFrame = firstSnapshot;
    for (const auto offset : std::array<std::uint32_t, 3>{32, 80, 150}) {
        Sleep(offset == 32 ? 32 : offset == 80 ? 48 : 70);
        FenceWindow(window);
        transitionFrame = CaptureFrame(window);
        ValidateCompleteFrame(transitionFrame, WideToUtf8(target.label) +
            " admitted snapshot " + std::to_string(offset) + " ms");
        evidence.FrameFile(
            L"production-current", target.id,
            L"snapshot-" + OffsetName(offset), transitionFrame);
    }
    auto settled = transitionFrame;
    ValidateCompleteFrame(settled, WideToUtf8(target.label) + " settled");
    evidence.FrameFile(L"production-current", target.id, L"settled", settled);
    if (target.key != VK_RETURN) {
        Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                    const auto log = ReadUtf8(logPath);
                    return log.size() > before && log.find(TransitionNeedle(target.id), before) !=
                        std::string::npos;
                }), "overlay.log omitted the exact transition identity for " +
                        WideToUtf8(target.label));
        if (target.firstSnapshotDelayMilliseconds > 0) {
            const auto log = ReadUtf8(logPath);
            const auto transition = log.find(TransitionNeedle(target.id), before);
            const auto lineEnd = log.find('\n', transition);
            const auto record = log.substr(
                transition,
                lineEnd == std::string::npos ? std::string::npos : lineEnd - transition);
            Require(record.find("content=retained-until-snapshot") != std::string::npos &&
                        record.find("sizing=retained-until-snapshot") != std::string::npos,
                    "Transition diagnostics omitted retained content and extent authority for " +
                        WideToUtf8(target.label));
        }
    }
    return settled;
}

void RunFullMotion(
    const Arguments& arguments,
    Evidence& evidence) {
    auto installation = std::make_unique<TemporaryInstallation>(
        arguments.installation, arguments.fixtureWorker, L"production-full-motion");
    auto host = std::make_unique<HostProcess>(
        installation->Root(), installation->LocalAppData());
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                window = LocateHostWindow(host->Id());
                return window && IsWindowVisible(window);
            }), "Full-motion production host did not create a visible HWND.");
    const auto logPath = installation->LocalAppData() /
        L"GameBarAlternative" / L"overlay.log";

    auto settled = CaptureFullMotionSwitch(
        window,
        logPath,
        kTargets[0],
        installation->StartupSignal(kTargets[0].id),
        nullptr,
        nullptr,
        evidence);
    SendKey(window, VK_DOWN);
    FenceWindow(window);
    for (std::size_t index = 1; index < kTargets.size(); ++index) {
        settled = CaptureFullMotionSwitch(
            window,
            logPath,
            kTargets[index],
            installation->StartupSignal(kTargets[index].id),
            &kTargets[index - 1],
            &settled,
            evidence);
    }

    // Retarget a live reveal in both directions; the final identity owns the
    // complete frame and no intermediate HWND clear is permitted.
    SendKey(window, VK_LEFT);
    Sleep(16);
    SendKey(window, VK_RIGHT);
    for (const auto offset : std::array<std::uint32_t, 3>{0, 48, 130}) {
        if (offset != 0) Sleep(offset == 48 ? 48 : 82);
        FenceWindow(window);
        auto frame = CaptureFrame(window);
        ValidateCompleteFrame(frame, "rapid reversal " + std::to_string(offset) + " ms");
        evidence.FrameFile(
            L"production-current", L"rapid-reversal", OffsetName(offset), frame);
    }
    auto reversalSettled = WaitForTarget(window, kTargets.back());
    evidence.FrameFile(
        L"production-current", L"rapid-reversal", L"settled", reversalSettled);

    // Same-identity refresh is an explicit runtime replacement, not a stale
    // sequence refresh; the retained target must stay continuously composed.
    const auto priorLogSize = ReadUtf8(logPath).size();
    const auto gamesStartupSignal = installation->StartupSignal(kTargets.back().id);
    {
        std::error_code ignored;
        fs::remove(gamesStartupSignal, ignored);
    }
    PostKey(window, VK_F5);
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                std::error_code ignored;
                return fs::exists(gamesStartupSignal, ignored);
            }), "Games & Apps refresh did not enter its delayed first snapshot request.");
    const ULONGLONG refreshStarted = GetTickCount64();
    for (const auto offset : std::array<std::uint32_t, 3>{0, 50, 130}) {
        const ULONGLONG due = refreshStarted + offset;
        const auto now = GetTickCount64();
        if (now < due) Sleep(static_cast<DWORD>(due - now));
        auto frame = CaptureFrame(window);
        ValidateCompleteFrame(frame, "same-identity refresh " +
            std::to_string(offset) + " ms");
        Require(frame.width == reversalSettled.width &&
                    frame.height == reversalSettled.height,
                "Same-identity startup changed its committed host extent.");
        const auto total = static_cast<std::size_t>(frame.width) * frame.height;
        Require(AffinityPixels(frame, Target::Affinity::Games) > total / 12,
                "Same-identity startup replaced the committed Games & Apps content.");
        evidence.FrameFile(
            L"production-current", L"games-apps-refresh", OffsetName(offset), frame);
    }
    Require(WaitUntil(kOperationTimeoutMilliseconds, [&] {
                const auto log = ReadUtf8(logPath);
                return log.size() > priorLogSize &&
                    log.find("Games & Apps reloaded", priorLogSize) != std::string::npos;
            }), "Same-identity runtime refresh omitted its bounded completion record.");
    auto refreshSettled = WaitForTarget(window, kTargets.back());
    evidence.FrameFile(
        L"production-current", L"games-apps-refresh", L"settled", refreshSettled);

    const auto log = ReadUtf8(logPath);
    Require(log.find("Render-target resize failed") == std::string::npos,
            "A production switch forced render-target recreation.");
    Require(log.find("target=animated-resize-in-place") != std::string::npos,
            "Production diagnostics omitted the animated in-place resize decision.");
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
        const auto arguments = ParseArguments(argc, argv);
        ComPtr<IWICImagingFactory> factory;
        Require(SUCCEEDED(CoCreateInstance(
                    CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                    IID_PPV_ARGS(factory.GetAddressOf()))) && factory,
                "Windows Imaging Component is unavailable.");
        Evidence evidence(arguments.evidenceRoot, factory.Get());
        RunFullMotion(arguments, evidence);
        evidence.Commit();
        std::cout << "WidgetSwitchHostTests passed\n";
        // Release the apartment-owned WIC factory before CoUninitialize.
        // WRL's destructor would otherwise call Release after COM teardown.
        factory.Reset();
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "WidgetSwitchHostTests failed: " << error.what() << "\n";
        CoUninitialize();
        return 1;
    }
}
