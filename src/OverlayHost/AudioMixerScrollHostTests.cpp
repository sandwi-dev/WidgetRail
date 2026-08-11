#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using namespace gba::host_testing;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 20'000;
constexpr DWORD kStepTimeoutMilliseconds = 5'000;
constexpr std::size_t kExpectedFocusTargets = 14;
constexpr wchar_t kTrayAutomationId[] = L"tray:tray.audio-mixer";
constexpr wchar_t kMasterAutomationId[] = L"widget:audio.master.volume.slider";
constexpr wchar_t kOpenCloseAutomationId[] = L"host:host.open.close";
constexpr wchar_t kDevelopmentNonce[] =
    L"0260260260260260260260260260260260260260260260260260260260260260";
constexpr char kDevelopmentNonceUtf8[] =
    "0260260260260260260260260260260260260260260260260260260260260260";

struct Arguments final {
    fs::path installation;
    fs::path fixtureWorker;
    std::optional<fs::path> evidenceRoot;
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
            Fail("Usage: AudioMixerScrollHostTests --installation <dir> "
                 "--fixture-worker <exe> [--evidence-root <dir>]");
        }
    }
    Require(!result.installation.empty() && !result.fixtureWorker.empty(),
            "Both --installation and --fixture-worker are required.");
    return result;
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& fixtureWorker,
        const float interfaceScale,
        const float textScale) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
                "--installation does not contain OverlayHost.exe");
        Require(fs::is_directory(source / L"runtime"),
                "--installation does not contain runtime");
        Require(fs::is_regular_file(fixtureWorker),
                "--fixture-worker does not name a file");
        const auto productionStyle = fixtureWorker.parent_path() / L"styles" / L"default.gbss";
        Require(fs::is_regular_file(productionStyle),
                "Audio Mixer fixture output omitted production default.gbss");

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"gba-audio-scroll-host-" + std::wstring(guidText));
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        fs::copy_file(
            productionStyle, root_ / L"runtime" / L"audio-mixer-scroll.gbss",
            fs::copy_options::overwrite_existing);

        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
        controlPath_ = root_ / L"fixture-control.txt";
        snapshotPath_ = root_ / L"fixture-snapshot.json";
        readyPath_ = root_ / L"host-ready.txt";
        scrollEvidencePath_ = root_ / L"scroll-evidence.txt";
        WriteUtf8(controlPath_, "full\n");

        std::ostringstream settings;
        settings << "{\"schemaVersion\":1,\"appearance\":{"
                 << "\"themeId\":\"org.gbar.builtin.cool-slate\","
                 << "\"themeVersion\":\"1.0.0\","
                 << "\"interfaceScale\":" << interfaceScale << ','
                 << "\"textScale\":" << textScale << ','
                 << "\"backdropOpacity\":0.64,\"motion\":\"reduced\"}}\n";
        WriteUtf8(
            localAppData_ / L"GameBarAlternative" / L"platform-settings.json",
            settings.str());

        const std::string catalog =
            "{\n"
            "  \"catalogVersion\": 1,\n"
            "  \"genericWorkerExecutable\": \"runtime/WidgetWorkerHost/WidgetWorkerHost.exe\",\n"
            "  \"widgets\": [{\n"
            "    \"id\": \"audio-mixer\",\n"
            "    \"packageId\": \"org.gbar.firstparty.audio-mixer\",\n"
            "    \"publisherId\": \"org.gbar.firstparty\",\n"
            "    \"name\": \"Audio Mixer\",\n"
            "    \"instanceId\": \"audio-mixer.default\",\n"
            "    \"icon\": \"volume\",\n"
            "    \"workerExecutable\": \"" +
                JsonEscape(fs::absolute(fixtureWorker).wstring()) + "\",\n"
            "    \"styleFile\": \"runtime/audio-mixer-scroll.gbss\",\n"
            "    \"memoryLimitMb\": 64,\n"
            "    \"residencyPolicy\": { \"schemaVersion\": 1, \"mode\": \"keep-alive\" },\n"
            "    \"workerArguments\": ["
                "\"--fixture-snapshot-path\", \"" +
                JsonEscape(snapshotPath_.wstring()) + "\", "
                "\"--fixture-control-path\", \"" +
                JsonEscape(controlPath_.wstring()) + "\"],\n"
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
    [[nodiscard]] const fs::path& ControlPath() const noexcept { return controlPath_; }
    [[nodiscard]] const fs::path& SnapshotPath() const noexcept { return snapshotPath_; }
    [[nodiscard]] const fs::path& ReadyPath() const noexcept { return readyPath_; }
    [[nodiscard]] const fs::path& ScrollEvidencePath() const noexcept {
        return scrollEvidencePath_;
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path controlPath_;
    fs::path snapshotPath_;
    fs::path readyPath_;
    fs::path scrollEvidencePath_;
};

std::optional<std::wstring> StringProperty(
    IUIAutomationElement* element,
    const PROPERTYID property) {
    VARIANT value{};
    if (!element || FAILED(element->GetCurrentPropertyValue(property, &value)))
        return std::nullopt;
    std::optional<std::wstring> result;
    if (V_VT(&value) == VT_BSTR && V_BSTR(&value))
        result.emplace(V_BSTR(&value), SysStringLen(V_BSTR(&value)));
    VariantClear(&value);
    return result;
}

ComPtr<IUIAutomationElement> RootForWindow(IUIAutomation* automation, HWND window) {
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf()))) return {};
    return root;
}

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation,
    IUIAutomationElement* root,
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
            TreeScope_Descendants, condition.Get(), result.GetAddressOf()))) return {};
    return result;
}

std::optional<std::wstring> FocusedWidgetAutomationId(
    IUIAutomation* automation,
    HWND window,
    RECT* bounds = nullptr) {
    auto root = RootForWindow(automation, window);
    if (!root) return std::nullopt;
    ComPtr<IUIAutomationCondition> condition;
    VARIANT value{};
    V_VT(&value) = VT_BOOL;
    V_BOOL(&value) = VARIANT_TRUE;
    if (FAILED(automation->CreatePropertyCondition(
            UIA_HasKeyboardFocusPropertyId, value, condition.GetAddressOf()))) return std::nullopt;
    ComPtr<IUIAutomationElementArray> focusedElements;
    if (FAILED(root->FindAll(
            TreeScope_Descendants, condition.Get(), focusedElements.GetAddressOf())) ||
        !focusedElements) return std::nullopt;
    int count{};
    if (FAILED(focusedElements->get_Length(&count))) return std::nullopt;
    for (int index = 0; index < count; ++index) {
        ComPtr<IUIAutomationElement> focused;
        if (FAILED(focusedElements->GetElement(index, focused.GetAddressOf())) || !focused)
            continue;
        auto id = StringProperty(focused.Get(), UIA_AutomationIdPropertyId);
        if (!id || !id->starts_with(L"widget:")) continue;
        if (bounds && FAILED(focused->get_CurrentBoundingRectangle(bounds))) continue;
        return id;
    }
    return std::nullopt;
}

struct Frame final {
    int width{};
    int height{};
    std::vector<std::uint8_t> bgra;
    std::string source;
};

struct PixelRegionEvidence final {
    std::size_t authoredPixels{};
    std::size_t pixelCount{};
    std::uint32_t minimumRgbSum{3U * 255U};
    std::uint32_t maximumRgbSum{};
};

PixelRegionEvidence InspectPixelRegion(const Frame& frame, RECT region) {
    region.left = std::clamp(region.left, 0L, static_cast<LONG>(frame.width));
    region.top = std::clamp(region.top, 0L, static_cast<LONG>(frame.height));
    region.right = std::clamp(region.right, region.left, static_cast<LONG>(frame.width));
    region.bottom = std::clamp(region.bottom, region.top, static_cast<LONG>(frame.height));
    PixelRegionEvidence result{};
    for (LONG y = region.top; y < region.bottom; ++y) {
        for (LONG x = region.left; x < region.right; ++x) {
            const auto offset =
                (static_cast<std::size_t>(y) * frame.width + x) * 4U;
            const auto blue = frame.bgra[offset];
            const auto green = frame.bgra[offset + 1];
            const auto red = frame.bgra[offset + 2];
            const auto rgbSum = static_cast<std::uint32_t>(blue) + green + red;
            ++result.pixelCount;
            if (blue > 16 || green > 16 || red > 16) ++result.authoredPixels;
            result.minimumRgbSum = std::min(result.minimumRgbSum, rgbSum);
            result.maximumRgbSum = std::max(result.maximumRgbSum, rgbSum);
        }
    }
    return result;
}

std::uint64_t HashFrame(const Frame& frame) {
    std::uint64_t hash = 1469598103934665603ULL;
    for (const auto value : frame.bgra) {
        hash ^= value;
        hash *= 1099511628211ULL;
    }
    return hash;
}

Frame CaptureFrame(HWND window) {
    Require(IsWindow(window) && IsWindowVisible(window),
            "Production host HWND became hidden during evidence capture.");
    RECT bounds{};
    Require(GetClientRect(window, &bounds), Win32Error("GetClientRect(capture)"));
    const int width = bounds.right - bounds.left;
    const int height = bounds.bottom - bounds.top;
    Require(width > 0 && height > 0, "Production host exposed an empty extent.");
    POINT clientOrigin{};
    Require(ClientToScreen(window, &clientOrigin),
            Win32Error("ClientToScreen(capture)"));
    // Prefer the authored redirected surface. Some DWM states legally return a
    // black PrintWindow result for a color-keyed HWND; validate RGB pixels and
    // then fall back to the same live composed screen rectangle. The later
    // UIA/edge/footer/tray checks reject an unrelated or incomplete fallback.
    HDC windowDc = GetDC(window);
    Require(windowDc != nullptr, Win32Error("GetDC(window)"));
    HDC memoryDc = CreateCompatibleDC(windowDc);
    Require(memoryDc != nullptr, Win32Error("CreateCompatibleDC"));
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = width;
    info.bmiHeader.biHeight = -height;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* bits{};
    HBITMAP bitmap = CreateDIBSection(
        windowDc, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    Require(bitmap != nullptr && bits, Win32Error("CreateDIBSection"));
    HGDIOBJ previous = SelectObject(memoryDc, bitmap);
    Require(previous != nullptr, Win32Error("SelectObject"));
    constexpr UINT kRenderFullContent = 0x00000002;
    Frame frame{width, height, std::vector<std::uint8_t>(
        static_cast<std::size_t>(width) * height * 4), {}};
    const auto copyPixels = [&] {
        std::copy_n(
            static_cast<const std::uint8_t*>(bits),
            frame.bgra.size(), frame.bgra.begin());
    };
    const auto hasAuthoredSurface = [&] {
        const auto pixels = InspectPixelRegion(
            frame, RECT{0, 0, frame.width, frame.height});
        return pixels.pixelCount > 0 &&
            pixels.authoredPixels * 12 > pixels.pixelCount;
    };
    if (PrintWindow(window, memoryDc, PW_CLIENTONLY | kRenderFullContent)) {
        copyPixels();
        if (hasAuthoredSurface()) frame.source = "print-window";
    }
    if (frame.source.empty()) {
        HDC screenDc = GetDC(nullptr);
        Require(screenDc != nullptr, Win32Error("GetDC(screen)"));
        const bool copied = BitBlt(
            memoryDc, 0, 0, width, height, screenDc,
            clientOrigin.x, clientOrigin.y, SRCCOPY | CAPTUREBLT);
        ReleaseDC(nullptr, screenDc);
        if (copied) {
            copyPixels();
            if (hasAuthoredSurface()) frame.source = "screen-composed";
        }
    }
    SelectObject(memoryDc, previous);
    DeleteObject(bitmap);
    DeleteDC(memoryDc);
    ReleaseDC(window, windowDc);
    Require(!frame.source.empty(),
            "Full-content HWND capture omitted the authored widget surface.");
    return frame;
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
            "WIC encoder initialization failed.");
    ComPtr<IWICBitmapFrameEncode> encoded;
    Require(SUCCEEDED(encoder->CreateNewFrame(encoded.GetAddressOf(), nullptr)) && encoded,
            "WIC could not create a PNG frame.");
    Require(SUCCEEDED(encoded->Initialize(nullptr)), "WIC frame initialization failed.");
    Require(SUCCEEDED(encoded->SetSize(frame.width, frame.height)),
            "WIC frame sizing failed.");
    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
    Require(SUCCEEDED(encoded->SetPixelFormat(&format)) &&
                format == GUID_WICPixelFormat32bppBGRA,
            "WIC rejected the BGRA evidence pixel format.");
    const UINT stride = static_cast<UINT>(frame.width * 4);
    Require(SUCCEEDED(encoded->WritePixels(
                frame.height, stride, static_cast<UINT>(frame.bgra.size()),
                const_cast<BYTE*>(frame.bgra.data()))),
            "WIC could not write evidence pixels.");
    Require(SUCCEEDED(encoded->Commit()) && SUCCEEDED(encoder->Commit()),
            "WIC could not commit the evidence PNG.");
}

struct ScrollEvidence final {
    std::wstring focus;
    std::wstring explicitTarget;
    std::wstring direction;
    std::wstring scope;
    bool revealable{};
    long long sequence{};
    float rootOffset{};
    float pixelScale{};
    float textScale{};
    std::array<float, 4> navigation{};
    std::array<float, 4> presentation{};
};

std::optional<std::array<float, 4>> ParseRect(const std::string_view value) {
    std::array<float, 4> result{};
    std::size_t start{};
    try {
        for (std::size_t index = 0; index < result.size(); ++index) {
            const auto separator = value.find(',', start);
            if (index + 1 < result.size() && separator == std::string_view::npos)
                return std::nullopt;
            const auto end = separator == std::string_view::npos ? value.size() : separator;
            result[index] = std::stof(std::string(value.substr(start, end - start)));
            start = end + 1;
        }
    } catch (...) {
        return std::nullopt;
    }
    return result;
}

std::optional<ScrollEvidence> ParseScrollEvidence(const std::string_view payload) {
    if (!payload.starts_with("gbar-scroll-evidence-v1\n")) return std::nullopt;
    ScrollEvidence result;
    bool hasOffset{};
    bool hasNavigation{};
    bool hasPresentation{};
    std::istringstream lines{std::string(payload)};
    std::string line;
    std::getline(lines, line);
    try {
        while (std::getline(lines, line)) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            const auto separator = line.find('=');
            if (separator == std::string::npos) continue;
            const auto key = line.substr(0, separator);
            const auto value = line.substr(separator + 1);
            const auto widen = [](const std::string& text) {
                return std::wstring(text.begin(), text.end());
            };
            if (key == "focus") result.focus = widen(value);
            else if (key == "explicitTarget") result.explicitTarget = widen(value);
            else if (key == "direction") result.direction = widen(value);
            else if (key == "scope") result.scope = widen(value);
            else if (key == "sequence") result.sequence = std::stoll(value);
            else if (key == "pixelScale") result.pixelScale = std::stof(value);
            else if (key == "textScale") result.textScale = std::stof(value);
            else if (key == "revealable") result.revealable = value == "true";
            else if (key == "navigation") {
                if (const auto parsed = ParseRect(value)) {
                    result.navigation = *parsed;
                    hasNavigation = true;
                }
            } else if (key == "presentation") {
                if (const auto parsed = ParseRect(value)) {
                    result.presentation = *parsed;
                    hasPresentation = true;
                }
            } else if (key == "scroll" && value.starts_with("audio.root,")) {
                result.rootOffset = std::stof(value.substr(11));
                hasOffset = true;
            }
        }
    } catch (...) {
        return std::nullopt;
    }
    if (result.focus.empty() || result.explicitTarget.empty() || result.scope.empty() ||
        !hasOffset || !hasNavigation || !hasPresentation) return std::nullopt;
    return result;
}

class Evidence final {
public:
    Evidence(
        const std::optional<fs::path>& root,
        IWICImagingFactory* factory,
        IUIAutomation* automation)
        : root_(root), factory_(factory), automation_(automation) {
        if (root_) fs::create_directories(*root_);
    }

    void Record(
        const std::wstring_view scenario,
        const std::wstring_view phase,
        const std::size_t step,
        const std::wstring_view focus,
        const ScrollEvidence& scroll,
        const RECT& bounds,
        HWND window) {
        if (!root_) return;
        const auto fileName = std::wstring(scenario) + L"-" + std::wstring(phase) +
            L"-" + std::to_wstring(step) + L".png";
        const auto frame = CaptureFrame(window);
        RECT client{};
        Require(GetClientRect(window, &client), Win32Error("GetClientRect(provenance)"));
        POINT origin{client.left, client.top};
        Require(ClientToScreen(window, &origin), Win32Error("ClientToScreen(provenance)"));
        const RECT clientBounds{
            origin.x,
            origin.y,
            origin.x + client.right - client.left,
            origin.y + client.bottom - client.top,
        };
        RECT windowBounds{};
        Require(GetWindowRect(window, &windowBounds),
                Win32Error("GetWindowRect(provenance)"));
        Require(frame.width == clientBounds.right - clientBounds.left &&
                    frame.height == clientBounds.bottom - clientBounds.top,
                "Captured frame dimensions do not cover the full host client.");
        Require(windowBounds.right - windowBounds.left == frame.width &&
                    windowBounds.bottom - windowBounds.top == frame.height,
                "Borderless production host window and client extents diverged.");
        const auto contains = [](const RECT& outer, const RECT& inner) {
            return inner.left >= outer.left && inner.top >= outer.top &&
                inner.right <= outer.right && inner.bottom <= outer.bottom;
        };
        Require(contains(clientBounds, bounds),
                "Focused UIA presentation bounds escaped the captured client.");
        auto root = RootForWindow(automation_, window);
        Require(static_cast<bool>(root),
                "Production UIA root disappeared during capture validation.");
        RECT rootBounds{};
        Require(SUCCEEDED(root->get_CurrentBoundingRectangle(&rootBounds)),
                "Production UIA root bounds were unavailable.");
        Require(contains(clientBounds, rootBounds) &&
                    rootBounds.left <= clientBounds.left + 1 &&
                    rootBounds.top <= clientBounds.top + 1 &&
                    rootBounds.right >= clientBounds.right - 1 &&
                    rootBounds.bottom >= clientBounds.bottom - 1,
                "Production UIA root did not cover the captured client.");
        auto tray = FindByAutomationId(automation_, root.Get(), kTrayAutomationId);
        RECT trayBounds{};
        Require(tray && SUCCEEDED(tray->get_CurrentBoundingRectangle(&trayBounds)),
                "Production tray bounds were unavailable during capture validation.");
        Require(contains(clientBounds, trayBounds) &&
                    trayBounds.top >= clientBounds.top + frame.height * 2 / 3,
                "Production tray was outside the expected captured lower extent.");
        const auto footerBoundsFor = [&](const wchar_t* automationId,
                                         const char* label) {
            const auto element = FindByAutomationId(
                automation_, root.Get(), automationId);
            RECT footerBounds{};
            Require(element && SUCCEEDED(
                        element->get_CurrentBoundingRectangle(&footerBounds)),
                    std::string("Production ") + label +
                        " footer bounds were unavailable during capture validation.");
            Require(contains(clientBounds, footerBounds),
                    "Production footer landmark escaped the captured client.");
            return footerBounds;
        };
        const auto closeBounds = footerBoundsFor(kOpenCloseAutomationId, "Close");
        const auto localRect = [&clientBounds](const RECT& screen) {
            return RECT{
                screen.left - clientBounds.left,
                screen.top - clientBounds.top,
                screen.right - clientBounds.left,
                screen.bottom - clientBounds.top,
            };
        };
        const auto localRoot = localRect(rootBounds);
        const auto rootWidth = localRoot.right - localRoot.left;
        const auto rootHeight = localRoot.bottom - localRoot.top;
        const auto edgeWidth = std::max<LONG>(8, rootWidth / 10);
        const auto verticalInset = std::max<LONG>(4, rootHeight / 20);
        const auto leftEdge = InspectPixelRegion(frame, RECT{
            localRoot.left,
            localRoot.top + verticalInset,
            localRoot.left + edgeWidth,
            localRoot.bottom - verticalInset,
        });
        const auto rightEdge = InspectPixelRegion(frame, RECT{
            localRoot.right - edgeWidth,
            localRoot.top + verticalInset,
            localRoot.right,
            localRoot.bottom - verticalInset,
        });
        const auto trayPixels = InspectPixelRegion(frame, localRect(trayBounds));
        const auto localClose = localRect(closeBounds);
        const auto leftFooterPixels = InspectPixelRegion(frame, RECT{
            0,
            localClose.top,
            std::max(1, frame.width / 3),
            localClose.bottom,
        });
        const auto closePixels = InspectPixelRegion(frame, localClose);
        auto localFocus = localRect(bounds);
        localFocus.left -= 6;
        localFocus.top -= 6;
        localFocus.right += 6;
        localFocus.bottom += 6;
        const auto focusPixels = InspectPixelRegion(frame, localFocus);
        const auto hasAuthoredCoverage = [](const PixelRegionEvidence& region,
                                            const std::size_t denominator) {
            return region.pixelCount > 0 &&
                region.authoredPixels * denominator >= region.pixelCount;
        };
        Require(hasAuthoredCoverage(leftEdge, 20) &&
                    hasAuthoredCoverage(rightEdge, 20),
                "Captured artifact omitted an authored horizontal host extent: left=" +
                    std::to_string(leftEdge.authoredPixels) + "/" +
                    std::to_string(leftEdge.pixelCount) + " right=" +
                    std::to_string(rightEdge.authoredPixels) + "/" +
                    std::to_string(rightEdge.pixelCount) + ".");
        Require(hasAuthoredCoverage(trayPixels, 10),
                "Captured artifact omitted the authored lower tray landmark.");
        Require(hasAuthoredCoverage(leftFooterPixels, 100) &&
                    hasAuthoredCoverage(closePixels, 100),
                "Captured artifact omitted an authored footer landmark.");
        Require(hasAuthoredCoverage(focusPixels, 10) &&
                    focusPixels.maximumRgbSum >= focusPixels.minimumRgbSum + 48,
                "Captured artifact did not contain the current focused control region.");
        const auto captureHash = HashFrame(frame);
        SavePng(factory_, *root_ / fileName, frame);
        records_ << "    {\"scenario\":\"" << JsonEscape(scenario)
                 << "\",\"phase\":\"" << JsonEscape(phase)
                 << "\",\"step\":" << step
                 << ",\"focusId\":\"" << JsonEscape(focus)
                 << "\",\"explicitTarget\":\"" << JsonEscape(scroll.explicitTarget)
                 << "\",\"direction\":\"" << JsonEscape(scroll.direction)
                 << "\",\"activeInputScope\":\"" << JsonEscape(scroll.scope)
                 << "\",\"scrollOffset\":" << scroll.rootOffset
                 << ",\"pixelScale\":" << scroll.pixelScale
                 << ",\"textScale\":" << scroll.textScale
                 << ",\"revealable\":" << (scroll.revealable ? "true" : "false")
                 << ",\"presentationBounds\":{\"x\":" << scroll.presentation[0]
                 << ",\"y\":" << scroll.presentation[1]
                 << ",\"width\":" << scroll.presentation[2]
                 << ",\"height\":" << scroll.presentation[3]
                 << "},\"navigationBounds\":{\"x\":" << scroll.navigation[0]
                 << ",\"y\":" << scroll.navigation[1]
                 << ",\"width\":" << scroll.navigation[2]
                 << ",\"height\":" << scroll.navigation[3]
                 << "},\"uiaBounds\":{\"left\":" << bounds.left
                 << ",\"top\":" << bounds.top << ",\"width\":"
                 << bounds.right - bounds.left << ",\"height\":"
                 << bounds.bottom - bounds.top << "},\"clientBounds\":{\"left\":"
                 << clientBounds.left << ",\"top\":" << clientBounds.top
                 << ",\"width\":" << clientBounds.right - clientBounds.left
                 << ",\"height\":" << clientBounds.bottom - clientBounds.top
                 << "},\"windowBounds\":{\"left\":" << windowBounds.left
                 << ",\"top\":" << windowBounds.top << ",\"width\":"
                 << windowBounds.right - windowBounds.left << ",\"height\":"
                 << windowBounds.bottom - windowBounds.top
                 << "},\"rootBounds\":{\"left\":" << rootBounds.left
                 << ",\"top\":" << rootBounds.top << ",\"width\":"
                 << rootBounds.right - rootBounds.left << ",\"height\":"
                 << rootBounds.bottom - rootBounds.top
                 << "},\"trayBounds\":{\"left\":" << trayBounds.left
                 << ",\"top\":" << trayBounds.top << ",\"width\":"
                 << trayBounds.right - trayBounds.left << ",\"height\":"
                 << trayBounds.bottom - trayBounds.top
                 << "},\"closeBounds\":{\"left\":" << closeBounds.left
                 << ",\"top\":" << closeBounds.top << ",\"width\":"
                 << closeBounds.right - closeBounds.left << ",\"height\":"
                 << closeBounds.bottom - closeBounds.top
                 << "},\"captureWidth\":" << frame.width
                 << ",\"captureHeight\":" << frame.height
                 << ",\"captureSource\":\"" << frame.source << "\""
                 << ",\"leftAuthoredPixels\":" << leftEdge.authoredPixels
                 << ",\"rightAuthoredPixels\":" << rightEdge.authoredPixels
                 << ",\"trayAuthoredPixels\":" << trayPixels.authoredPixels
                 << ",\"leftFooterAuthoredPixels\":"
                 << leftFooterPixels.authoredPixels
                 << ",\"closeAuthoredPixels\":" << closePixels.authoredPixels
                 << ",\"focusAuthoredPixels\":" << focusPixels.authoredPixels
                 << ",\"captureHash\":\"" << captureHash
                 << "\",\"capture\":\""
                 << JsonEscape(fileName) << "\"},\n";
    }

    void Semantic(
        const std::wstring_view scenario,
        const std::wstring_view phase,
        const std::string_view json) const {
        if (!root_) return;
        WriteUtf8(*root_ /
            (std::wstring(scenario) + L"-" + std::wstring(phase) + L"-semantic.json"), json);
    }

    void Diagnostic(const std::wstring_view scenario, const std::string_view log) const {
        if (!root_) return;
        WriteUtf8(*root_ / (std::wstring(scenario) + L"-overlay.log"), log);
    }

    void Commit() {
        if (!root_) return;
        auto records = records_.str();
        if (records.size() >= 2) records.erase(records.size() - 2);
        WriteUtf8(*root_ / L"manifest.json",
            "{\n  \"contract\":\"dlv026-audio-mixer-scroll-v1\",\n"
            "  \"focusRecords\":[\n" + records + "\n  ]\n}\n");
    }

private:
    std::optional<fs::path> root_;
    IWICImagingFactory* factory_{};
    IUIAutomation* automation_{};
    std::ostringstream records_;
};

std::wstring WaitForFocus(
    IUIAutomation* automation,
    HWND window,
    const std::optional<std::wstring_view> expected,
    RECT& bounds) {
    std::optional<std::wstring> focused;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                focused = FocusedWidgetAutomationId(automation, window, &bounds);
                return focused && (!expected || *focused == *expected);
            }), expected ? "Production focus did not reach the explicit target."
                         : "Production host did not publish focused widget UIA geometry.");
    return *focused;
}

void WaitForSemantic(
    const fs::path& snapshotPath,
    const std::string_view required,
    const std::string_view forbidden = {}) {
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                const auto json = ReadUtf8(snapshotPath);
                return json.find(required) != std::string::npos &&
                    (forbidden.empty() || json.find(forbidden) == std::string::npos);
            }), "Production Audio Mixer semantic sidecar did not reach the requested state.");
}

ScrollEvidence WaitForScrollEvidence(
    const fs::path& path,
    const std::wstring_view expectedFocus,
    const long long minimumSequence = 0) {
    std::optional<ScrollEvidence> result;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                result = ParseScrollEvidence(ReadUtf8(path));
                return result && result->focus == expectedFocus &&
                    result->sequence >= minimumSequence;
            }), "Host scroll evidence did not reach the focused production control.");
    Require(result->scope == L"audio-mixer",
            "Host scroll evidence escaped the Audio Mixer input scope.");
    Require(result->explicitTarget == expectedFocus,
            "Host scroll evidence did not retain the explicit focus target.");
    Require(result->revealable,
            "Focused production control was not retained as revealable.");
    return *result;
}

void ResizeConstrained(HWND window) {
    RECT bounds{};
    Require(GetWindowRect(window, &bounds), Win32Error("GetWindowRect"));
    const int width = bounds.right - bounds.left;
    const int currentHeight = bounds.bottom - bounds.top;
    const int constrainedHeight = std::max(420, currentHeight * 3 / 4);
    Require(SetWindowPos(
                window, nullptr, 0, 0, width, constrainedHeight,
                SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE),
            Win32Error("SetWindowPos(constrained)"));
    Require(UpdateWindow(window), Win32Error("UpdateWindow(constrained)"));
}

std::vector<std::wstring> Traverse(
    IUIAutomation* automation,
    HWND window,
    const std::wstring_view scenario,
    const fs::path& scrollEvidencePath,
    Evidence& evidence) {
    RECT bounds{};
    auto focused = WaitForFocus(automation, window, kMasterAutomationId, bounds);
    std::vector<std::wstring> order{focused};
    auto scroll = WaitForScrollEvidence(
        scrollEvidencePath, std::wstring_view(focused).substr(7));
    float priorOffset = scroll.rootOffset;
    evidence.Record(scenario, L"down", 0, focused, scroll, bounds, window);
    for (std::size_t step = 1; step < kExpectedFocusTargets; ++step) {
        const auto previous = focused;
        SendKey(window, VK_DOWN);
        Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                    const auto current = FocusedWidgetAutomationId(automation, window, &bounds);
                    if (!current || *current == previous) return false;
                    focused = *current;
                    return true;
                }), "Down did not move to the next production Audio Mixer control.");
        order.push_back(focused);
        scroll = WaitForScrollEvidence(
            scrollEvidencePath, std::wstring_view(focused).substr(7));
        Require(scroll.rootOffset + 0.01F >= priorOffset,
                "Down traversal moved the Audio Mixer root offset backward.");
        priorOffset = scroll.rootOffset;
        evidence.Record(scenario, L"down", step, focused, scroll, bounds, window);
    }

    for (std::size_t reverse = 1; reverse < order.size(); ++reverse) {
        const auto expected = std::wstring_view(order[order.size() - reverse - 1]);
        SendKey(window, VK_UP);
        focused = WaitForFocus(automation, window, expected, bounds);
        scroll = WaitForScrollEvidence(
            scrollEvidencePath, std::wstring_view(focused).substr(7));
        Require(scroll.rootOffset <= priorOffset + 0.01F,
                "Reverse traversal increased the Audio Mixer root offset.");
        priorOffset = scroll.rootOffset;
        evidence.Record(scenario, L"up", reverse, focused, scroll, bounds, window);
    }
    Require(focused == kMasterAutomationId,
            "Reverse traversal did not restore the true leading Audio Mixer control.");
    Require(std::abs(priorOffset) <= 0.01F,
            "Reverse traversal did not restore the true leading scroll boundary.");
    return order;
}

void RunScenario(
    const Arguments& arguments,
    const std::wstring_view name,
    const float interfaceScale,
    const float textScale,
    const bool constrained,
    const bool churn,
    IUIAutomation* automation,
    Evidence& evidence) {
    TemporaryInstallation installation(
        arguments.installation, arguments.fixtureWorker, interfaceScale, textScale);
    const auto quoted = [](const fs::path& path) {
        return L"\"" + path.wstring() + L"\"";
    };
    const std::wstring hostArguments =
        L"--show --development-catalog-root " + quoted(installation.Root()) +
        L" --development-ready-path " + quoted(installation.ReadyPath()) +
        L" --development-ready-nonce " + kDevelopmentNonce +
        L" --development-widget-id audio-mixer"
        L" --development-widget-instance audio-mixer.default"
        L" --scroll-evidence-path " + quoted(installation.ScrollEvidencePath());
    HostProcess host(
        installation.Root(), installation.LocalAppData(), hostArguments);
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                return ReadUtf8(installation.ReadyPath()).find(kDevelopmentNonceUtf8) !=
                    std::string::npos;
            }), "Production host did not publish authenticated development readiness.");
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
                window = LocateHostWindow(host.Id());
                return window && IsWindowVisible(window);
            }), "Production OverlayHost did not create a visible HWND in time.");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                auto root = RootForWindow(automation, window);
                return root && FindByAutomationId(
                    automation, root.Get(), kTrayAutomationId);
            }), "Audio Mixer did not appear in the production dashboard.");
    SendKey(window, VK_RETURN);
    WaitForSemantic(installation.SnapshotPath(), "Application 11");
    RECT initialBounds{};
    const auto initialFocus = WaitForFocus(
        automation, window, kMasterAutomationId, initialBounds);
    const auto appliedScale = WaitForScrollEvidence(
        installation.ScrollEvidencePath(), std::wstring_view(initialFocus).substr(7));
    const float expectedPixelScale =
        static_cast<float>(std::max(1U, GetDpiForWindow(window))) / 96.0F *
        interfaceScale;
    Require(std::abs(appliedScale.pixelScale - expectedPixelScale) <= 0.01F,
            "Production host applied pixel scale " +
                std::to_string(appliedScale.pixelScale) + " instead of " +
                std::to_string(expectedPixelScale) + ".");
    Require(std::abs(appliedScale.textScale - textScale) <= 0.01F,
            "Production host applied text scale " +
                std::to_string(appliedScale.textScale) + " instead of " +
                std::to_string(textScale) + ".");
    if (constrained) ResizeConstrained(window);
    std::vector<std::wstring> order;
    try {
        order = Traverse(
            automation, window, name, installation.ScrollEvidencePath(), evidence);
    } catch (...) {
        evidence.Diagnostic(
            name,
            ReadUtf8(installation.LocalAppData() /
                L"GameBarAlternative" / L"overlay.log"));
        throw;
    }
    evidence.Semantic(name, L"full", ReadUtf8(installation.SnapshotPath()));
    evidence.Diagnostic(
        name,
        ReadUtf8(installation.LocalAppData() /
            L"GameBarAlternative" / L"overlay.log"));

    if (churn) {
        RECT bounds{};
        for (std::size_t step = 1; step <= 7; ++step) {
            SendKey(window, VK_DOWN);
            (void)WaitForFocus(
                automation, window, std::wstring_view(order[step]), bounds);
        }
        const auto stableFocus = WaitForFocus(
            automation, window, std::wstring_view(order[7]), bounds);
        auto scroll = WaitForScrollEvidence(
            installation.ScrollEvidencePath(), std::wstring_view(stableFocus).substr(7));
        const auto fullSequence = scroll.sequence;
        WriteUtf8(installation.ControlPath(), "remove-unrelated\n");
        WaitForSemantic(installation.SnapshotPath(), "Application 10", "Application 11");
        (void)WaitForFocus(automation, window, std::wstring_view(stableFocus), bounds);
        scroll = WaitForScrollEvidence(
            installation.ScrollEvidencePath(), std::wstring_view(stableFocus).substr(7),
            fullSequence + 1);
        evidence.Record(name, L"remove-unrelated", 0, stableFocus, scroll, bounds, window);
        evidence.Semantic(name, L"remove-unrelated", ReadUtf8(installation.SnapshotPath()));

        WriteUtf8(installation.ControlPath(), "remove-focused\n");
        WaitForSemantic(installation.SnapshotPath(), "Application 06", "Application 05");
        const auto fallback = WaitForFocus(
            automation, window, std::wstring_view(order[8]), bounds);
        scroll = WaitForScrollEvidence(
            installation.ScrollEvidencePath(), std::wstring_view(fallback).substr(7),
            scroll.sequence + 1);
        evidence.Record(name, L"remove-focused", 0, fallback, scroll, bounds, window);
        evidence.Semantic(name, L"remove-focused", ReadUtf8(installation.SnapshotPath()));

        WriteUtf8(installation.ControlPath(), "added\n");
        WaitForSemantic(installation.SnapshotPath(), "Application 12");
        (void)WaitForFocus(automation, window, std::wstring_view(fallback), bounds);
        scroll = WaitForScrollEvidence(
            installation.ScrollEvidencePath(), std::wstring_view(fallback).substr(7),
            scroll.sequence + 1);
        evidence.Record(name, L"added", 0, fallback, scroll, bounds, window);
        evidence.Semantic(name, L"added", ReadUtf8(installation.SnapshotPath()));
    }

    Require(PostMessageW(window, WM_CLOSE, 0, 0), Win32Error("PostMessageW(WM_CLOSE)"));
    Require(WaitForSingleObject(host.Process(), kStepTimeoutMilliseconds) == WAIT_OBJECT_0,
            "Production OverlayHost did not stop after the scroll scenario.");
}

void Run(const Arguments& arguments) {
    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
            "Windows UI Automation client is unavailable.");
    ComPtr<IWICImagingFactory> imaging;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(imaging.GetAddressOf()))) && imaging,
            "Windows Imaging Component is unavailable.");
    Evidence evidence(arguments.evidenceRoot, imaging.Get(), automation.Get());
    RunScenario(arguments, L"preferred-100", 1.0F, 1.0F, false, true,
                automation.Get(), evidence);
    RunScenario(arguments, L"constrained-100", 1.0F, 1.0F, true, false,
                automation.Get(), evidence);
    RunScenario(arguments, L"preferred-150", 1.25F, 1.5F, false, false,
                automation.Get(), evidence);
    evidence.Commit();
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) &&
        GetLastError() != ERROR_ACCESS_DENIED) {
        std::cerr << "AudioMixerScrollHostTests failed: "
                  << Win32Error("SetProcessDpiAwarenessContext") << "\n";
        return 1;
    }
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        Run(ParseArguments(argc, argv));
        if (SUCCEEDED(initialized)) CoUninitialize();
        std::cout << "AudioMixerScrollHostTests passed\n";
        return 0;
    } catch (const std::exception& error) {
        if (SUCCEEDED(initialized)) CoUninitialize();
        std::cerr << "AudioMixerScrollHostTests failed: " << error.what() << "\n";
        return 1;
    }
}
