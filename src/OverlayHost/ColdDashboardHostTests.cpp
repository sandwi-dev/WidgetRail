#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <array>
#include <cstdlib>
#include <cwctype>
#include <filesystem>
#include <exception>
#include <iostream>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using namespace widgetrail::host_testing;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30000;
constexpr DWORD kStepTimeoutMilliseconds = 15000;

struct Arguments final {
    fs::path installation;
};

class TemporaryProfile final {
public:
    TemporaryProfile() {
        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"wrail-cold-dashboard-" + std::wstring(guidText));
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"WidgetRail");
        profile_ = L"cold-dashboard-";
        for (const wchar_t character : std::wstring_view(guidText)) {
            if (std::iswalnum(character))
                profile_.push_back(static_cast<wchar_t>(std::towlower(character)));
        }
    }

    ~TemporaryProfile() {
        if (std::uncaught_exceptions() != 0) {
            std::cerr << "Retained failed startup evidence: " << logPath().string() << '\n';
            return;
        }
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    [[nodiscard]] const fs::path& localAppData() const noexcept {
        return localAppData_;
    }
    [[nodiscard]] const std::wstring& profile() const noexcept { return profile_; }
    [[nodiscard]] fs::path logPath() const {
        return localAppData_ / L"WidgetRail" / L"overlay.log";
    }

private:
    fs::path root_;
    fs::path localAppData_;
    std::wstring profile_;
};

std::optional<RECT> ParseBounds(
    const std::string_view record,
    const std::string_view field) {
    const auto start = record.find(field);
    if (start == std::string_view::npos) return std::nullopt;
    std::array<long, 4> values{};
    std::size_t cursor = start + field.size();
    for (std::size_t index = 0; index < values.size(); ++index) {
        const auto end = record.find(index + 1 == values.size() ? ' ' : ',', cursor);
        const auto token = record.substr(cursor, end - cursor);
        try {
            std::size_t consumed{};
            values[index] = std::stol(std::string(token), &consumed);
            if (consumed != token.size()) return std::nullopt;
        } catch (...) {
            return std::nullopt;
        }
        if (end == std::string_view::npos) {
            if (index + 1 != values.size()) return std::nullopt;
        } else {
            cursor = end + 1;
        }
    }
    return RECT{
        values[0], values[1], values[0] + values[2], values[1] + values[3]};
}

std::optional<std::string> LatestFirstVisibleRecord(
    const std::string& log,
    const std::size_t after = 0) {
    constexpr std::string_view needle =
        "Composition placement committed content=complete";
    std::size_t cursor = after;
    std::optional<std::string> result;
    while ((cursor = log.find(needle, cursor)) != std::string::npos) {
        const auto end = log.find('\n', cursor);
        const auto record = log.substr(
            cursor, end == std::string::npos ? std::string::npos : end - cursor);
        if (record.find("first-visible=true") != std::string::npos)
            result = record;
        if (end == std::string::npos) break;
        cursor = end + 1;
    }
    return result;
}

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view expected) {
    VARIANT value{};
    V_VT(&value) = VT_BSTR;
    V_BSTR(&value) = SysAllocStringLen(
        expected.data(), static_cast<UINT>(expected.size()));
    ComPtr<IUIAutomationCondition> condition;
    if (!V_BSTR(&value) || FAILED(automation->CreatePropertyCondition(
            UIA_AutomationIdPropertyId, value, condition.GetAddressOf()))) {
        VariantClear(&value);
        return {};
    }
    VariantClear(&value);
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.GetAddressOf()))) return {};
    return element;
}

std::wstring AutomationId(IUIAutomationElement* element) {
    BSTR value{};
    if (!element || FAILED(element->get_CurrentAutomationId(&value)) || !value)
        return {};
    std::wstring result(value, SysStringLen(value));
    SysFreeString(value);
    return result;
}

ComPtr<IUIAutomationElement> FocusedTrayItem(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view expected) {
    VARIANT value{};
    V_VT(&value) = VT_BOOL;
    V_BOOL(&value) = VARIANT_TRUE;
    ComPtr<IUIAutomationCondition> condition;
    if (FAILED(automation->CreatePropertyCondition(
            UIA_HasKeyboardFocusPropertyId, value, condition.GetAddressOf()))) return {};
    ComPtr<IUIAutomationElementArray> elements;
    if (FAILED(root->FindAll(
            TreeScope_Descendants, condition.Get(), elements.GetAddressOf())) || !elements)
        return {};
    int count{};
    if (FAILED(elements->get_Length(&count))) return {};
    for (int index = 0; index < count; ++index) {
        ComPtr<IUIAutomationElement> element;
        if (SUCCEEDED(elements->GetElement(index, element.GetAddressOf())) &&
            AutomationId(element.Get()) == expected) return element;
    }
    return {};
}

bool IsFocused(IUIAutomationElement* element) {
    BOOL focused{};
    return element && SUCCEEDED(element->get_CurrentHasKeyboardFocus(&focused)) && focused;
}

bool IsSelected(IUIAutomationElement* element) {
    VARIANT selected{};
    const bool result = element &&
        SUCCEEDED(element->GetCurrentPropertyValue(
            UIA_SelectionItemIsSelectedPropertyId, &selected)) &&
        V_VT(&selected) == VT_BOOL && V_BOOL(&selected) == VARIANT_TRUE;
    VariantClear(&selected);
    return result;
}

bool HasSettingsWidgetInputPaint(
    const std::string& log,
    const std::size_t after) {
    constexpr std::string_view needle =
        "Widget presentation paint target=settings ";
    std::size_t cursor = after;
    while ((cursor = log.find(needle, cursor)) != std::string::npos) {
        const auto end = log.find('\n', cursor);
        const std::string_view record{
            log.data() + cursor,
            end == std::string::npos ? log.size() - cursor : end - cursor};
        if (record.find(" input-owner=widget ") != std::string_view::npos &&
            record.find(" selected=settings ") != std::string_view::npos)
            return true;
        if (end == std::string::npos) break;
        cursor = end + 1;
    }
    return false;
}

bool Contains(const RECT outer, const RECT inner) noexcept {
    return inner.left >= outer.left && inner.top >= outer.top &&
        inner.right <= outer.right && inner.bottom <= outer.bottom &&
        inner.right > inner.left && inner.bottom > inner.top;
}

void RequireBottomAnchoredRecord(
    const std::string& record,
    const HWND window,
    RECT& visibleContent) {
    const auto host = ParseBounds(record, "host-bounds=");
    const auto content = ParseBounds(record, "visible-content-bounds=");
    Require(host.has_value() && content.has_value(),
            "Composition diagnostic omitted absolute host/content bounds.");
    Require(record.find("anchor=bottom") != std::string::npos,
            "Composition diagnostic did not identify the bottom anchor.");
    RECT actualHost{};
    Require(GetWindowRect(window, &actualHost) != FALSE, Win32Error("GetWindowRect"));
    // Settings can replace its loading surface and resize after the first
    // commit, before this out-of-process reader observes the record. Validate
    // that frame's geometry and the retained bottom anchor, not an old height
    // against a later live HWND measurement.
    Require(actualHost.bottom == host->bottom,
            "The first-visible bottom anchor moved during content initialization.");
    Require(Contains(*host, *content) && content->bottom == host->bottom,
            "Visible content was not bottom anchored inside the host.");
    MONITORINFO monitor{sizeof(monitor)};
    Require(GetMonitorInfoW(
                MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitor) != FALSE,
            Win32Error("GetMonitorInfoW"));
    Require(Contains(monitor.rcWork, *host) && Contains(monitor.rcWork, *content) &&
                Contains(monitor.rcWork, actualHost),
            "Visible host/content bounds escaped live rcWork.");
    visibleContent = *content;
}

struct SettingsAuthority final {
    ComPtr<IUIAutomationElement> contentRoot;
    ComPtr<IUIAutomationElement> chromeRoot;
    ComPtr<IUIAutomationElement> settingsTray;
};

SettingsAuthority VerifyStartupSettingsUia(
    IUIAutomation* automation,
    HWND contentWindow,
    HWND chromeWindow) {
    ComPtr<IUIAutomationElement> contentRoot;
    ComPtr<IUIAutomationElement> chromeRoot;
    ComPtr<IUIAutomationElement> settingsCategory;
    ComPtr<IUIAutomationElement> legacyTitle;
    ComPtr<IUIAutomationElement> selected;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        contentRoot.Reset();
        chromeRoot.Reset();
        settingsCategory.Reset();
        legacyTitle.Reset();
        selected.Reset();
        if (FAILED(automation->ElementFromHandle(
                contentWindow, contentRoot.GetAddressOf())) || !contentRoot ||
            FAILED(automation->ElementFromHandle(
                chromeWindow, chromeRoot.GetAddressOf())) || !chromeRoot)
            return false;
        settingsCategory = FindByAutomationId(
            automation, contentRoot.Get(), L"widget:category.appearance");
        legacyTitle = FindByAutomationId(
            automation, contentRoot.Get(), L"host:host.dashboard.title");
        selected = FocusedTrayItem(
            automation, chromeRoot.Get(), L"tray:tray.settings");
        return !settingsCategory && !legacyTitle && selected && IsSelected(selected.Get());
    }), "Production Settings content/fixed-chrome tray UIA was unavailable.");
    RECT contentRootBounds{};
    RECT selectedBounds{};
    Require(SUCCEEDED(contentRoot->get_CurrentBoundingRectangle(&contentRootBounds)) &&
                SUCCEEDED(selected->get_CurrentBoundingRectangle(&selectedBounds)),
            "Production Settings/tray UIA bounds were unavailable.");
    RECT contentClient{};
    POINT contentOrigin{};
    Require(GetClientRect(contentWindow, &contentClient) != FALSE,
            Win32Error("GetClientRect(content)"));
    Require(ClientToScreen(contentWindow, &contentOrigin) != FALSE,
            Win32Error("ClientToScreen(content)"));
    const RECT contentClientBounds{
        contentOrigin.x,
        contentOrigin.y,
        contentOrigin.x + contentClient.right - contentClient.left,
        contentOrigin.y + contentClient.bottom - contentClient.top,
    };
    Require(Contains(contentClientBounds, contentRootBounds),
            "Settings content UIA root escaped the content HWND client.");
    RECT chromeBounds{};
    Require(GetWindowRect(chromeWindow, &chromeBounds) != FALSE,
            Win32Error("GetWindowRect(chrome)"));
    Require(Contains(chromeBounds, selectedBounds),
            "Focused Settings tray UIA bounds escaped fixed chrome.");
    const POINT pointer{
        selectedBounds.left + (selectedBounds.right - selectedBounds.left) / 2,
        selectedBounds.top + (selectedBounds.bottom - selectedBounds.top) / 2};
    const LRESULT hit = SendMessageW(
        chromeWindow, WM_NCHITTEST, 0, MAKELPARAM(pointer.x, pointer.y));
    Require(hit == HTCLIENT,
            "Focused Settings tray UIA center did not map to fixed chrome input.");
    return {
        std::move(contentRoot),
        std::move(chromeRoot),
        std::move(selected),
    };
}

void VerifyReshownSettingsFocus(
    IUIAutomation* automation,
    HWND window) {
    ComPtr<IUIAutomationElement> currentRoot;
    ComPtr<IUIAutomationElement> currentSettings;
    bool settingsFocused{};
    bool rootBoundsAvailable{};
    bool settingsBoundsAvailable{};
    bool clientBoundsAvailable{};
    bool clientOriginAvailable{};
    bool settingsInsideRoot{};
    bool settingsInsideClient{};
    if (WaitUntil(kStepTimeoutMilliseconds, [&] {
        currentRoot.Reset();
        currentSettings.Reset();
        settingsFocused = false;
        rootBoundsAvailable = false;
        settingsBoundsAvailable = false;
        clientBoundsAvailable = false;
        clientOriginAvailable = false;
        settingsInsideRoot = false;
        settingsInsideClient = false;

        if (FAILED(automation->ElementFromHandle(
                window, currentRoot.GetAddressOf())) || !currentRoot)
            return false;
        currentSettings = FindByAutomationId(
            automation, currentRoot.Get(), L"widget:category.appearance");
        if (!currentSettings) return false;

        settingsFocused = IsFocused(currentSettings.Get());
        RECT rootBounds{};
        RECT settingsBounds{};
        RECT client{};
        POINT clientOrigin{};
        rootBoundsAvailable = SUCCEEDED(
            currentRoot->get_CurrentBoundingRectangle(&rootBounds));
        settingsBoundsAvailable = SUCCEEDED(
            currentSettings->get_CurrentBoundingRectangle(&settingsBounds));
        clientBoundsAvailable = GetClientRect(window, &client) != FALSE;
        clientOriginAvailable = ClientToScreen(window, &clientOrigin) != FALSE;
        if (rootBoundsAvailable && settingsBoundsAvailable)
            settingsInsideRoot = Contains(rootBounds, settingsBounds);
        if (settingsBoundsAvailable && clientBoundsAvailable &&
            clientOriginAvailable) {
            const RECT clientBounds{
                clientOrigin.x,
                clientOrigin.y,
                clientOrigin.x + client.right - client.left,
                clientOrigin.y + client.bottom - client.top,
            };
            settingsInsideClient = Contains(clientBounds, settingsBounds);
        }
        return settingsFocused && rootBoundsAvailable && settingsBoundsAvailable &&
            clientBoundsAvailable && clientOriginAvailable &&
            settingsInsideRoot && settingsInsideClient;
    })) {
        return;
    }
    Require(currentRoot,
            "Re-shown production widget omitted its current content UIA root.");
    Require(currentSettings,
            "Re-shown production widget omitted the exact Settings action.");
    Require(settingsFocused,
            "Re-shown production widget did not restore focus to the exact Settings action.");
    Require(rootBoundsAvailable,
            "Re-shown production widget content root bounds were unavailable.");
    Require(settingsBoundsAvailable,
            "Re-shown production widget Settings bounds were unavailable.");
    Require(clientBoundsAvailable,
            "Re-shown production widget content client rectangle was unavailable.");
    Require(clientOriginAvailable,
            "Re-shown production widget content client origin was unavailable.");
    Require(settingsInsideRoot,
            "Re-shown production widget Settings bounds escaped the current content root.");
    Require(settingsInsideClient,
            "Re-shown production widget Settings bounds escaped the current content client.");
}

void Run(const Arguments& arguments, const bool startHidden) {
    std::cout << "Cold startup route=" << (startHidden ? "hidden-toggle" : "show") << std::endl;
    Require(fs::is_regular_file(arguments.installation / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
    TemporaryProfile profile;
    const std::wstring hostArguments =
        L"--show --process-profile " + profile.profile();
    HostProcess host(arguments.installation, profile.localAppData(), startHidden
        ? L"--process-profile " + profile.profile() : hostArguments);
    HWND window{};
    HWND chromeWindow{};
    if (startHidden) {
        Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
            window = LocateHostWindow(host.Id());
            return window != nullptr;
        }), "Hidden startup did not create its host window.");
        Require(IsWindowVisible(window) == FALSE,
                "Sign-in style startup must remain hidden before the first toggle.");
        Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
                Win32Error("PostMessageW(first startup toggle)"));
    }
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host.Id());
        chromeWindow = LocateHostWindow(host.Id(), L"WidgetRail.Chrome");
        return window && chromeWindow && IsWindowVisible(window) != FALSE &&
            IsWindowVisible(chromeWindow) != FALSE;
    }), "Cold production host did not expose its first visible content/chrome pair.");
    std::optional<std::string> firstRecord;
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(profile.logPath());
        firstRecord = LatestFirstVisibleRecord(log);
        return firstRecord.has_value();
    }), "Cold production host omitted its first-visible composition diagnostic.");
    RECT firstContent{};
    RequireBottomAnchoredRecord(*firstRecord, window, firstContent);

    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
            "Windows UI Automation client is unavailable.");
    const auto settingsAuthority = VerifyStartupSettingsUia(
        automation.Get(), window, chromeWindow);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(profile.logPath());
        const auto sampleStart = log.rfind("Composition child sample step=");
        if (sampleStart == std::string::npos) return false;
        const auto sampleEnd = log.find('\n', sampleStart);
        const auto sample = log.substr(sampleStart, sampleEnd - sampleStart);
        const auto guide = ParseBounds(sample, "guide=");
        const auto selected = ParseBounds(sample, "selected=");
        return log.find("content=admitted rendered=settings") != std::string::npos &&
            log.find("surface=dashboard") == std::string::npos &&
            guide && selected && selected->top >= guide->bottom;
    }), "First opening must paint Settings with the selected tray icon below the guide.");

    const auto beforeWidget = ReadUtf8(profile.logPath()).size();
    ComPtr<IUIAutomationInvokePattern> settingsTrayInvoke;
    Require(SUCCEEDED(settingsAuthority.settingsTray->GetCurrentPatternAs(
                UIA_InvokePatternId,
                IID_PPV_ARGS(settingsTrayInvoke.ReleaseAndGetAddressOf()))) &&
                settingsTrayInvoke,
            "Selected Settings tray item did not expose InvokePattern.");
    Require(SUCCEEDED(settingsTrayInvoke->Invoke()),
            "Selected Settings tray item rejected InvokePattern.");

    ComPtr<IUIAutomationElement> currentContentRoot;
    ComPtr<IUIAutomationElement> currentSettingsAction;
    ComPtr<IUIAutomationElement> currentChromeRoot;
    ComPtr<IUIAutomationElement> currentSettingsTray;
    BOOL sameContentRoot{};
    BOOL sameChromeRoot{};
    BOOL sameSettingsTray{};
    bool widgetInputPaint{};
    bool settingsBoundsAvailable{};
    bool settingsInsideContentRoot{};
    bool settingsInsideContentClient{};
    bool settingsFocused{};
    bool settingsTraySelected{};
    bool settingsTrayFocused{};
    const auto currentAuthorityPublished = [&] {
        const auto log = ReadUtf8(profile.logPath());
        widgetInputPaint = HasSettingsWidgetInputPaint(log, beforeWidget);

        currentContentRoot.Reset();
        currentSettingsAction.Reset();
        currentChromeRoot.Reset();
        currentSettingsTray.Reset();
        sameContentRoot = FALSE;
        sameChromeRoot = FALSE;
        sameSettingsTray = FALSE;
        settingsBoundsAvailable = false;
        settingsInsideContentRoot = false;
        settingsInsideContentClient = false;
        settingsFocused = false;
        settingsTraySelected = false;
        settingsTrayFocused = false;

        if (FAILED(automation->ElementFromHandle(
                window, currentContentRoot.GetAddressOf())) || !currentContentRoot ||
            FAILED(automation->ElementFromHandle(
                chromeWindow, currentChromeRoot.GetAddressOf())) || !currentChromeRoot)
            return false;
        currentSettingsAction = FindByAutomationId(
            automation.Get(), currentContentRoot.Get(), L"widget:category.appearance");
        currentSettingsTray = FindByAutomationId(
            automation.Get(), currentChromeRoot.Get(), L"tray:tray.settings");
        if (!currentSettingsAction || !currentSettingsTray) return false;

        const bool identitiesMatch =
            SUCCEEDED(automation->CompareElements(
                settingsAuthority.contentRoot.Get(), currentContentRoot.Get(),
                &sameContentRoot)) && sameContentRoot &&
            SUCCEEDED(automation->CompareElements(
                settingsAuthority.chromeRoot.Get(), currentChromeRoot.Get(),
                &sameChromeRoot)) && sameChromeRoot &&
            SUCCEEDED(automation->CompareElements(
                settingsAuthority.settingsTray.Get(), currentSettingsTray.Get(),
                &sameSettingsTray)) && sameSettingsTray;

        RECT contentRootBounds{};
        RECT settingsBounds{};
        RECT contentClient{};
        POINT contentOrigin{};
        settingsBoundsAvailable =
            SUCCEEDED(currentContentRoot->get_CurrentBoundingRectangle(
                &contentRootBounds)) &&
            SUCCEEDED(currentSettingsAction->get_CurrentBoundingRectangle(
                &settingsBounds)) &&
            GetClientRect(window, &contentClient) != FALSE &&
            ClientToScreen(window, &contentOrigin) != FALSE;
        if (settingsBoundsAvailable) {
            const RECT contentClientBounds{
                contentOrigin.x,
                contentOrigin.y,
                contentOrigin.x + contentClient.right - contentClient.left,
                contentOrigin.y + contentClient.bottom - contentClient.top,
            };
            settingsInsideContentRoot = Contains(contentRootBounds, settingsBounds);
            settingsInsideContentClient = Contains(contentClientBounds, settingsBounds);
        }
        settingsFocused = IsFocused(currentSettingsAction.Get());
        settingsTraySelected = IsSelected(currentSettingsTray.Get());
        settingsTrayFocused = IsFocused(currentSettingsTray.Get());
        return widgetInputPaint && identitiesMatch && settingsBoundsAvailable &&
            settingsInsideContentRoot && settingsInsideContentClient &&
            settingsFocused && settingsTraySelected && !settingsTrayFocused;
    };
    if (!WaitUntil(kStepTimeoutMilliseconds, currentAuthorityPublished)) {
        Require(widgetInputPaint,
                "Settings activation did not publish widget input ownership.");
        Require(currentContentRoot,
                "Settings activation did not retain the content UIA root.");
        Require(currentSettingsAction,
                "Settings activation did not retain the Settings content action.");
        Require(sameContentRoot,
                "Settings activation replaced the exact content UIA root.");
        Require(settingsBoundsAvailable,
                "Settings content bounds were unavailable after activation.");
        Require(settingsInsideContentRoot,
                "Settings content escaped the exact retained content root after activation.");
        Require(settingsInsideContentClient,
                "Settings content escaped the content HWND client after activation.");
        Require(settingsFocused,
                "Settings activation did not transfer keyboard focus to the Settings action.");
        Require(currentChromeRoot,
                "Settings activation did not retain the fixed-chrome UIA root.");
        Require(currentSettingsTray,
                "Settings activation did not retain the Settings tray item.");
        Require(sameChromeRoot,
                "Settings activation replaced the exact fixed-chrome UIA root.");
        Require(sameSettingsTray,
                "Settings activation replaced the exact Settings tray item.");
        Require(settingsTraySelected,
                "Settings activation cleared the selected Settings tray item.");
        Require(!settingsTrayFocused,
                "Settings tray item retained keyboard focus after widget activation.");
    }
    Require(PostMessageW(window, WM_HOTKEY, 1, 0) != FALSE,
            Win32Error("PostMessageW(close hotkey)"));
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        return IsWindowVisible(window) == FALSE;
    }), "Production widget did not hide.");

    const auto beforeReshow = ReadUtf8(profile.logPath()).size();
    {
        HostProcess activation(
            arguments.installation, profile.localAppData(), hostArguments);
        Require(WaitForSingleObject(activation.Process(), kStepTimeoutMilliseconds) ==
                    WAIT_OBJECT_0,
                "Authenticated resident --show activation did not complete.");
    }
    std::optional<std::string> reshowRecord;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(profile.logPath());
        reshowRecord = LatestFirstVisibleRecord(log, beforeReshow);
        return IsWindowVisible(window) != FALSE && reshowRecord.has_value();
    }), "Resident re-show omitted bottom-anchored first-visible geometry.");
    RECT reshownContent{};
    RequireBottomAnchoredRecord(*reshowRecord, window, reshownContent);
    VerifyReshownSettingsFocus(automation.Get(), window);

    // Inspect every applied frame, including the cold first open. Checking only
    // the final re-show would miss a startup overlap that repairs itself later.
    const auto geometryLog = ReadUtf8(profile.logPath());
    std::size_t sampleCursor{};
    std::size_t checkedSamples{};
    while ((sampleCursor = geometryLog.find("Composition child sample", sampleCursor)) != std::string::npos) {
        const auto end = geometryLog.find('\n', sampleCursor);
        const auto record = geometryLog.substr(sampleCursor,
            end == std::string::npos ? std::string::npos : end-sampleCursor);
        sampleCursor = end == std::string::npos ? geometryLog.size() : end+1;
        if (record.find("chrome-applied-exact=true") == std::string::npos) continue;
        const auto guide = ParseBounds(record, "guide=");
        const auto selected = ParseBounds(record, "selected=");
        Require(guide && selected, "Applied chrome sample omitted guide or tray icon bounds.");
        Require(selected->bottom <= guide->top || selected->top >= guide->bottom ||
                selected->right <= guide->left || selected->left >= guide->right,
            "Tray icon overlaps the guide during cold startup or re-show.");
        ++checkedSamples;
    }
    Require(checkedSamples > 0, "Cold startup omitted applied chrome geometry samples.");

    PostMessageW(window, WM_CLOSE, 0, 0);
    Require(WaitForSingleObject(host.Process(), kStepTimeoutMilliseconds) == WAIT_OBJECT_0,
            "Production OverlayHost did not stop after the cold dashboard scenario.");
    std::cout << "ColdDashboardHostTests: first commit, Settings/chrome UIA/pointer, widget hide, and re-show passed\n";
}

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        if (_wcsicmp(argv[index], L"--installation") == 0 && index + 1 < argc)
            result.installation = argv[++index];
        else
            Fail("Usage: ColdDashboardHostTests --installation <Release-host-directory>");
    }
    Require(!result.installation.empty(), "--installation is required");
    return result;
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) &&
        GetLastError() != ERROR_ACCESS_DENIED) {
        std::cerr << "FAIL: SetProcessDpiAwarenessContext failed error="
                  << GetLastError() << '\n';
        return EXIT_FAILURE;
    }
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized)) {
        std::cerr << "FAIL: CoInitializeEx failed\n";
        return EXIT_FAILURE;
    }
    try {
        const auto arguments = ParseArguments(argc, argv);
        Run(arguments, false);
        Run(arguments, true);
        CoUninitialize();
        return EXIT_SUCCESS;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        CoUninitialize();
        return EXIT_FAILURE;
    }
}
