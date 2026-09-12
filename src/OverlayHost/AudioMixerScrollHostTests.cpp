#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cwctype>
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
using namespace widgetrail::host_testing;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 20'000;
constexpr DWORD kStepTimeoutMilliseconds = 5'000;
constexpr wchar_t kTrayAutomationId[] = L"tray:tray.audio-mixer";
constexpr wchar_t kMasterAutomationId[] = L"widget:audio.master.volume.slider";
constexpr wchar_t kMicrophoneAutomationId[] = L"widget:audio.input.volume.slider";
constexpr wchar_t kOutputDeviceAutomationId[] = L"widget:audio.devices.output.select";
constexpr wchar_t kInputDeviceAutomationId[] = L"widget:audio.devices.input.select";
constexpr wchar_t kSpatialAutomationId[] = L"widget:audio.spatial.retry";
constexpr wchar_t kFirstSessionName[] =
    L"Application 00 volume, audible. Press A to mute";
constexpr wchar_t kLastSessionName[] =
    L"Application 03 volume, audible. Press A to mute";
constexpr wchar_t kOpenCloseAutomationId[] = L"host:host.open.close";
constexpr wchar_t kDevelopmentNonce[] =
    L"0490490490490490490490490490490490490490490490490490490490490490";
constexpr char kDevelopmentNonceUtf8[] =
    "0490490490490490490490490490490490490490490490490490490490490490";

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
        ValidateNativeRuntimeDependencies(source);
        const auto productionStyle = fixtureWorker.parent_path() / L"styles" / L"default.wrss";
        Require(fs::is_regular_file(productionStyle),
                "Audio Mixer fixture output omitted production default.wrss");

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"wrail-audio-scroll-host-" + std::wstring(guidText));
        processProfile_ = L"audio-scroll-";
        for (const wchar_t character : std::wstring_view(guidText)) {
            if (std::iswalnum(character))
                processProfile_.push_back(
                    static_cast<wchar_t>(std::towlower(character)));
        }
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        CopyNativeRuntimeDependencies(source, root_);
        fs::copy(source / L"runtime", root_ / L"runtime",
                 fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        fs::copy_file(
            productionStyle, root_ / L"runtime" / L"audio-mixer-scroll.wrss",
            fs::copy_options::overwrite_existing);

        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"WidgetRail");
        controlPath_ = root_ / L"fixture-control.txt";
        snapshotPath_ = root_ / L"fixture-snapshot.json";
        readyPath_ = root_ / L"host-ready.txt";
        scrollEvidencePath_ = root_ / L"scroll-evidence.txt";
        WriteUtf8(controlPath_, "live-four\n");

        std::ostringstream settings;
        settings << "{\"schemaVersion\":1,\"appearance\":{"
                 << "\"themeId\":\"widgetrail.builtin.cool-slate\","
                 << "\"themeVersion\":\"1.0.0\","
                 << "\"interfaceScale\":" << interfaceScale << ','
                 << "\"textScale\":" << textScale << ','
                 << "\"backdropOpacity\":0.64,\"motion\":\"full\"}}\n";
        WriteUtf8(
            localAppData_ / L"WidgetRail" / L"platform-settings.json",
            settings.str());

        const std::string catalog =
            "{\n"
            "  \"catalogVersion\": 1,\n"
            "  \"genericWorkerExecutable\": \"runtime/WidgetWorkerHost/WidgetWorkerHost.exe\",\n"
            "  \"widgets\": [{\n"
            "    \"id\": \"audio-mixer\",\n"
            "    \"packageId\": \"widgetrail.firstparty.audio-mixer\",\n"
            "    \"publisherId\": \"widgetrail.firstparty\",\n"
            "    \"name\": \"Audio Mixer\",\n"
            "    \"instanceId\": \"audio-mixer.default\",\n"
            "    \"icon\": \"volume\",\n"
            "    \"workerExecutable\": \"" +
                JsonEscape(fs::absolute(fixtureWorker).wstring()) + "\",\n"
            "    \"styleFile\": \"runtime/audio-mixer-scroll.wrss\",\n"
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
    [[nodiscard]] const std::wstring& ProcessProfile() const noexcept {
        return processProfile_;
    }
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
    std::wstring processProfile_;
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

ComPtr<IUIAutomationElement> FindByProperty(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const PROPERTYID property,
    const std::wstring_view expected) {
    VARIANT value{};
    V_VT(&value) = VT_BSTR;
    V_BSTR(&value) = SysAllocStringLen(
        expected.data(), static_cast<UINT>(expected.size()));
    ComPtr<IUIAutomationCondition> condition;
    if (!V_BSTR(&value) || FAILED(automation->CreatePropertyCondition(
            property, value, condition.GetAddressOf()))) {
        VariantClear(&value);
        return {};
    }
    VariantClear(&value);
    ComPtr<IUIAutomationElement> result;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), result.GetAddressOf()))) return {};
    return result;
}

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view automationId) {
    return FindByProperty(
        automation, root, UIA_AutomationIdPropertyId, automationId);
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
    std::wstring upTarget;
    bool upRevealable{};
    std::array<float, 4> upNavigation{};
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
    if (!payload.starts_with("wrail-scroll-evidence-v1\n")) return std::nullopt;
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
            else if (key == "upTarget") result.upTarget = widen(value);
            else if (key == "upRevealable") result.upRevealable = value == "true";
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
            } else if (key == "upNavigation") {
                if (const auto parsed = ParseRect(value)) result.upNavigation = *parsed;
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
        IUIAutomation* automation)
        : root_(root), automation_(automation) {
        if (root_) fs::create_directories(*root_);
    }

    void Record(
        const std::wstring_view scenario,
        const std::wstring_view phase,
        const std::size_t step,
        const std::wstring_view focus,
        const ScrollEvidence& scroll,
        const float rootMaximum,
        const RECT& bounds,
        HWND window) {
        if (!root_) return;
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
        const auto clientWidth = clientBounds.right - clientBounds.left;
        const auto clientHeight = clientBounds.bottom - clientBounds.top;
        Require(windowBounds.right - windowBounds.left == clientWidth &&
                    windowBounds.bottom - windowBounds.top == clientHeight,
                "Borderless production host window and client extents diverged.");
        const auto contains = [](const RECT& outer, const RECT& inner) {
            return inner.left >= outer.left && inner.top >= outer.top &&
                inner.right <= outer.right && inner.bottom <= outer.bottom;
        };
        Require(contains(clientBounds, bounds),
                "Focused UIA presentation bounds escaped the host client.");
        auto root = RootForWindow(automation_, window);
        Require(static_cast<bool>(root),
                "Production UIA root disappeared during geometry validation.");
        RECT rootBounds{};
        Require(SUCCEEDED(root->get_CurrentBoundingRectangle(&rootBounds)),
                "Production UIA root bounds were unavailable.");
        Require(contains(clientBounds, rootBounds) &&
                    rootBounds.left <= clientBounds.left + 1 &&
                    rootBounds.top <= clientBounds.top + 1 &&
                    rootBounds.right >= clientBounds.right - 1 &&
                    rootBounds.bottom >= clientBounds.bottom - 1,
                "Production UIA root did not cover the host client.");
        auto tray = FindByAutomationId(automation_, root.Get(), kTrayAutomationId);
        RECT trayBounds{};
        Require(tray && SUCCEEDED(tray->get_CurrentBoundingRectangle(&trayBounds)),
                "Production tray bounds were unavailable during geometry validation.");
        Require(contains(clientBounds, trayBounds) &&
                    trayBounds.top >= clientBounds.top + clientHeight * 2 / 3,
                "Production tray was outside the expected host lower extent.");
        const auto footerBoundsFor = [&](const wchar_t* automationId,
                                         const char* label) {
            const auto element = FindByAutomationId(
                automation_, root.Get(), automationId);
            RECT footerBounds{};
            Require(element && SUCCEEDED(
                        element->get_CurrentBoundingRectangle(&footerBounds)),
                    std::string("Production ") + label +
                        " footer bounds were unavailable during geometry validation.");
            Require(contains(clientBounds, footerBounds),
                    "Production footer landmark escaped the host client.");
            return footerBounds;
        };
        const auto closeBounds = footerBoundsFor(kOpenCloseAutomationId, "Close");
        records_ << "    {\"scenario\":\"" << JsonEscape(scenario)
                 << "\",\"phase\":\"" << JsonEscape(phase)
                 << "\",\"step\":" << step
                 << ",\"focusId\":\"" << JsonEscape(focus)
                 << "\",\"explicitTarget\":\"" << JsonEscape(scroll.explicitTarget)
                 << "\",\"direction\":\"" << JsonEscape(scroll.direction)
                 << "\",\"activeInputScope\":\"" << JsonEscape(scroll.scope)
                 << "\",\"scrollOffset\":" << scroll.rootOffset
                 << ",\"scrollMaximum\":" << rootMaximum
                 << ",\"pixelScale\":" << scroll.pixelScale
                 << ",\"textScale\":" << scroll.textScale
                 << ",\"revealable\":" << (scroll.revealable ? "true" : "false")
                 << ",\"upTarget\":\"" << JsonEscape(scroll.upTarget)
                 << "\",\"upRevealable\":"
                 << (scroll.upRevealable ? "true" : "false")
                 << ",\"upNavigationBounds\":{\"x\":" << scroll.upNavigation[0]
                 << ",\"y\":" << scroll.upNavigation[1]
                 << ",\"width\":" << scroll.upNavigation[2]
                 << ",\"height\":" << scroll.upNavigation[3] << "}"
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
                 << closeBounds.bottom - closeBounds.top << "}},\n";
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
            "{\n  \"contract\":\"dlv049-audio-mixer-reverse-scroll-v1\",\n"
            "  \"focusRecords\":[\n" + records + "\n  ]\n}\n");
    }

private:
    std::optional<fs::path> root_;
    IUIAutomation* automation_{};
    std::ostringstream records_;
};

std::wstring WaitForFocus(
    IUIAutomation* automation,
    HWND window,
    const std::optional<std::wstring_view> expected,
    RECT& bounds) {
    std::optional<std::wstring> focused;
    const bool reached = WaitUntil(kStepTimeoutMilliseconds, [&] {
                focused = FocusedWidgetAutomationId(automation, window, &bounds);
                return focused && (!expected || *focused == *expected);
            });
    Require(reached,
            expected
                ? "Production focus did not reach explicit target " +
                    WideToUtf8(*expected) + "; observed=" +
                    (focused ? WideToUtf8(*focused) : std::string("none"))
                : "Production host did not publish focused widget UIA geometry.");
    return *focused;
}

std::wstring WaitForFocusChange(
    IUIAutomation* automation,
    HWND window,
    const std::wstring_view previous,
    RECT& bounds) {
    std::optional<std::wstring> focused;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                focused = FocusedWidgetAutomationId(automation, window, &bounds);
                return focused && *focused != previous;
            }), "Production focus did not advance to the next authored target.");
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

void RequireMicrophoneUpEdge(const std::string_view json) {
    Require(json.find("\"ActiveInputScopeId\": \"audio-mixer\"") !=
                std::string_view::npos,
            "Four-session snapshot omitted the admitted Audio Mixer input scope.");
    Require(json.find("\"Id\": \"audio.master.volume.slider\"") !=
                std::string_view::npos &&
                json.find("Application 00 volume, audible. Press A to mute") !=
                    std::string_view::npos &&
                json.find("Application 03 volume, audible. Press A to mute") !=
                    std::string_view::npos,
            "Four-session snapshot omitted an authored traversal endpoint.");
    constexpr std::string_view microphone =
        "\"Id\": \"audio.input.volume.slider\"";
    constexpr std::string_view masterEdge =
        "\"Up\": \"audio.devices.input.select\"";
    const auto microphoneAt = json.find(microphone);
    Require(microphoneAt != std::string_view::npos,
            "Four-session snapshot omitted the Microphone slider.");
    const auto nextNode = json.find("\"Id\":", microphoneAt + microphone.size());
    const auto edgeAt = json.find(masterEdge, microphoneAt + microphone.size());
    Require(edgeAt != std::string_view::npos &&
                (nextNode == std::string_view::npos || edgeAt < nextNode),
            "Emitted Microphone.Up edge did not target its device selector.");
}

void RequireElementName(
    IUIAutomation* automation,
    HWND window,
    const std::wstring_view automationId,
    const std::wstring_view expectedName) {
    auto root = RootForWindow(automation, window);
    Require(static_cast<bool>(root), "Production UIA root disappeared.");
    auto element = FindByAutomationId(automation, root.Get(), automationId);
    Require(static_cast<bool>(element),
            "Current production UIA focus target was absent after host reveal.");
    const auto name = StringProperty(element.Get(), UIA_NamePropertyId);
    Require(name && *name == expectedName,
            "Current production UIA focus target did not match the authored session.");
}

ScrollEvidence WaitForScrollEvidence(
    const fs::path& path,
    const std::wstring_view expectedFocus,
    const long long minimumSequence = 0,
    const bool requireExplicitTarget = true) {
    std::optional<ScrollEvidence> result;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                result = ParseScrollEvidence(ReadUtf8(path));
                return result && result->focus == expectedFocus &&
                    result->sequence >= minimumSequence;
            }), "Host scroll evidence did not reach the focused production control.");
    Require(result->scope == L"audio-mixer",
            "Host scroll evidence escaped the Audio Mixer input scope.");
    Require(!requireExplicitTarget || result->explicitTarget == expectedFocus,
            "Host scroll evidence did not retain the explicit focus target.");
    Require(result->revealable,
            "Focused production control was not retained as revealable.");
    return *result;
}

void ExerciseLiveFourReverseEdge(
    IUIAutomation* automation,
    HWND window,
    const fs::path& controlPath,
    const fs::path& semanticPath,
    const fs::path& scrollEvidencePath,
    Evidence& evidence) {
    RECT bounds{};
    const auto semantic = ReadUtf8(semanticPath);
    RequireMicrophoneUpEdge(semantic);

    for (const auto* target : {kOutputDeviceAutomationId, kSpatialAutomationId, kInputDeviceAutomationId, kMicrophoneAutomationId}) {
        SendKey(window, VK_DOWN);
        (void)WaitForFocus(automation, window, target, bounds);
    }
    std::wstring firstSession;
    std::wstring lastSession;
    std::wstring previous = kMicrophoneAutomationId;
    for (std::size_t index = 0; index < 4; ++index) {
        SendKey(window, VK_DOWN);
        const auto focused = WaitForFocusChange(
            automation, window, previous, bounds);
        if (index == 0) {
            firstSession = focused;
            RequireElementName(automation, window, firstSession, kFirstSessionName);
        }
        if (index == 3) lastSession = focused;
        previous = focused;
    }
    RequireElementName(automation, window, lastSession, kLastSessionName);
    auto scroll = WaitForScrollEvidence(
        scrollEvidencePath, std::wstring_view(lastSession).substr(7), 0, false);
    Require(scroll.rootOffset > 0.01F,
            "Down traversal to the fourth session did not establish a nonzero root offset.");
    const float trailingOffset = scroll.rootOffset;
    Require(std::isfinite(trailingOffset),
            "Four-session trailing root maximum was not finite.");
    evidence.Record(L"live-four", L"seed-last-session", 0,
                    lastSession, scroll, trailingOffset, bounds, window);

    previous = lastSession;
    for (std::size_t index = 0; index < 3; ++index) {
        SendKey(window, VK_UP);
        previous = WaitForFocusChange(automation, window, previous, bounds);
    }
    Require(previous == firstSession,
            "Reverse traversal did not return to the exact first session.");
    RequireElementName(automation, window, firstSession, kFirstSessionName);
    SendKey(window, VK_UP);
    (void)WaitForFocus(automation, window, kMicrophoneAutomationId, bounds);
    SendKey(window, VK_UP);
    (void)WaitForFocus(automation, window, kInputDeviceAutomationId, bounds);
    SendKey(window, VK_UP);
    (void)WaitForFocus(automation, window, kSpatialAutomationId, bounds);
    SendKey(window, VK_UP);
    const auto microphone = WaitForFocus(
        automation, window, kOutputDeviceAutomationId, bounds);
    scroll = WaitForScrollEvidence(
        scrollEvidencePath, std::wstring_view(microphone).substr(7), 0, false);

    const auto fourSequence = scroll.sequence;
    WriteUtf8(controlPath, "full\n");
    WaitForSemantic(semanticPath, "Application 11");
    scroll = WaitForScrollEvidence(
        scrollEvidencePath, L"audio.devices.output.select", fourSequence + 1, false);
    WriteUtf8(controlPath, "live-four\n");
    WaitForSemantic(semanticPath, "Application 03", "Application 04");
    scroll = WaitForScrollEvidence(
        scrollEvidencePath, L"audio.devices.output.select", scroll.sequence + 1, false);
    (void)WaitForFocus(automation, window, kOutputDeviceAutomationId, bounds);
    Require(scroll.rootOffset > 0.01F &&
                scroll.rootOffset <= trailingOffset + 0.01F,
            "Output selector setup did not retain the live nonzero scroll state.");
    Require(std::isfinite(scroll.rootOffset),
            "Output selector retained a non-finite root offset.");
    const float microphoneOffset = scroll.rootOffset;
    Require(scroll.upTarget == L"audio.master.volume.slider" &&
                scroll.upRevealable,
            "Emitted Output selector.Up target was not natively revealable before input.");
    Require(scroll.upNavigation[1] + scroll.upNavigation[3] <= 0.01F,
            "Master was not already above the rendered viewport before Up.");
    evidence.Record(L"live-four", L"microphone-before-up", 1,
                    microphone, scroll, trailingOffset, bounds, window);

    const auto stableSince = GetTickCount64();
    Require(WaitUntil(500, [&] {
                const auto current = ParseScrollEvidence(ReadUtf8(scrollEvidencePath));
                return GetTickCount64() - stableSince >= 200 && current &&
                    current->focus == L"audio.devices.output.select" &&
                    std::abs(current->rootOffset - microphoneOffset) <= 0.01F;
            }), "Output selector retained state did not settle after full-motion transition.");

    SendKey(window, VK_UP);
    const auto focusedMaster = WaitForFocus(
        automation, window, kMasterAutomationId, bounds);
    scroll = WaitForScrollEvidence(
        scrollEvidencePath, std::wstring_view(focusedMaster).substr(7));
    Require(scroll.direction == L"up" &&
                scroll.explicitTarget == L"audio.master.volume.slider",
            "Native host did not consume the exact emitted Output selector.Up edge.");
    Require(scroll.rootOffset + 0.01F < microphoneOffset,
            "One Up did not decrease the retained Audio Mixer root offset.");
    Require(std::abs(scroll.rootOffset) <= 0.01F,
            "One Up did not restore the true leading Audio Mixer boundary.");
    Require(std::isfinite(scroll.rootOffset) &&
                scroll.rootOffset <= trailingOffset + 0.01F,
            "One Up left root offset outside the four-session safe range.");
    evidence.Record(L"live-four", L"master-after-up", 2,
                    focusedMaster, scroll, trailingOffset, bounds, window);

    // Reopen the same stable worker/snapshot. This path used to normalize the
    // broken offset; it must now preserve an already-correct leading boundary.
    SendKey(window, VK_ESCAPE);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
                auto currentRoot = RootForWindow(automation, window);
                return currentRoot && FindByAutomationId(
                    automation, currentRoot.Get(), kTrayAutomationId);
            }), "Audio Mixer did not return to its dashboard tray item.");
    SendKey(window, VK_RETURN);
    const auto reopenedMaster = WaitForFocus(
        automation, window, kMasterAutomationId, bounds);
    scroll = WaitForScrollEvidence(
        scrollEvidencePath, std::wstring_view(reopenedMaster).substr(7));
    Require(std::abs(scroll.rootOffset) <= 0.01F,
            "Reopening Audio Mixer changed its canonical leading offset.");
    evidence.Record(L"live-four", L"master-after-reopen", 3,
                    reopenedMaster, scroll, trailingOffset, bounds, window);
}

void RunScenario(
    const Arguments& arguments,
    IUIAutomation* automation,
    Evidence& evidence) {
    Require(!PathContainsDirectory(arguments.installation),
            "Fixture PATH must not contain the admitted installation directory");
    VerifyNativeRuntimeDependencyPolicy(arguments.installation);
    TemporaryInstallation installation(
        arguments.installation, arguments.fixtureWorker, 1.0F, 1.0F);
    const auto quoted = [](const fs::path& path) {
        return L"\"" + path.wstring() + L"\"";
    };
    const std::wstring hostArguments =
        L"--show --process-profile " + installation.ProcessProfile() +
        L" --development-catalog-root " + quoted(installation.Root()) +
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
    WaitForSemantic(installation.SnapshotPath(), "Application 03", "Application 04");
    RECT initialBounds{};
    const auto initialFocus = WaitForFocus(
        automation, window, kMasterAutomationId, initialBounds);
    const auto appliedScale = WaitForScrollEvidence(
        installation.ScrollEvidencePath(), std::wstring_view(initialFocus).substr(7));
    const float expectedPixelScale =
        static_cast<float>(std::max(1U, GetDpiForWindow(window))) / 96.0F;
    Require(std::abs(appliedScale.pixelScale - expectedPixelScale) <= 0.01F,
            "Production host applied pixel scale " +
                std::to_string(appliedScale.pixelScale) + " instead of " +
                std::to_string(expectedPixelScale) + ".");
    Require(std::abs(appliedScale.textScale - 1.0F) <= 0.01F,
            "Production host applied text scale " +
                std::to_string(appliedScale.textScale) + " instead of 1.0.");
    try {
        ExerciseLiveFourReverseEdge(
            automation, window, installation.ControlPath(),
            installation.SnapshotPath(),
            installation.ScrollEvidencePath(), evidence);
    } catch (...) {
        evidence.Diagnostic(
            L"live-four-scroll", ReadUtf8(installation.ScrollEvidencePath()));
        evidence.Diagnostic(
            L"live-four",
            ReadUtf8(installation.LocalAppData() /
                L"WidgetRail" / L"overlay.log"));
        throw;
    }
    evidence.Semantic(
        L"live-four", L"emitted", ReadUtf8(installation.SnapshotPath()));
    const auto overlayLog = ReadUtf8(
        installation.LocalAppData() / L"WidgetRail" / L"overlay.log");
    Require(overlayLog.find("value_clamped [audio.root]") == std::string::npos,
            "Audio Mixer cycling still normalized a stale retained root offset.");
    evidence.Diagnostic(
        L"live-four", overlayLog);

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
    Evidence evidence(arguments.evidenceRoot, automation.Get());
    RunScenario(arguments, automation.Get(), evidence);
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
