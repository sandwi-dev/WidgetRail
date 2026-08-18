#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <array>
#include <cstdlib>
#include <cwctype>
#include <filesystem>
#include <iostream>
#include <optional>
#include <string>
#include <string_view>

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
    IUIAutomationElement* root) {
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
            AutomationId(element.Get()).starts_with(L"tray:")) return element;
    }
    return {};
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
    if (actualHost.left != host->left || actualHost.top != host->top ||
        actualHost.right != host->right || actualHost.bottom != host->bottom) {
        std::cerr << "Host diagnostic=" << host->left << ',' << host->top << ','
                  << host->right - host->left << ',' << host->bottom - host->top
                  << " HWND=" << actualHost.left << ',' << actualHost.top << ','
                  << actualHost.right - actualHost.left << ','
                  << actualHost.bottom - actualHost.top << '\n';
    }
    Require(actualHost.left == host->left && actualHost.top == host->top &&
                actualHost.right == host->right && actualHost.bottom == host->bottom,
            "Composition diagnostic disagreed with the production HWND bounds.");
    Require(Contains(*host, *content) && content->bottom == host->bottom,
            "Visible content was not bottom anchored inside the host.");
    MONITORINFO monitor{sizeof(monitor)};
    Require(GetMonitorInfoW(
                MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitor) != FALSE,
            Win32Error("GetMonitorInfoW"));
    Require(Contains(monitor.rcWork, *host) && Contains(monitor.rcWork, *content),
            "Visible host/content bounds escaped live rcWork.");
    visibleContent = *content;
}

void VerifyDashboardUia(
    IUIAutomation* automation,
    HWND window,
    const RECT visibleContent) {
    ComPtr<IUIAutomationElement> root;
    ComPtr<IUIAutomationElement> title;
    ComPtr<IUIAutomationElement> selected;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        root.Reset();
        title.Reset();
        selected.Reset();
        if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf())) || !root)
            return false;
        title = FindByAutomationId(
            automation, root.Get(), L"host:host.dashboard.title");
        selected = FocusedTrayItem(automation, root.Get());
        return title && selected;
    }), "Production dashboard UIA title/focused tray item was unavailable.");
    RECT titleBounds{};
    RECT selectedBounds{};
    Require(SUCCEEDED(title->get_CurrentBoundingRectangle(&titleBounds)) &&
                SUCCEEDED(selected->get_CurrentBoundingRectangle(&selectedBounds)),
            "Production dashboard UIA bounds were unavailable.");
    Require(Contains(visibleContent, titleBounds) &&
                Contains(visibleContent, selectedBounds),
            "Dashboard title or focused tray UIA bounds escaped visible content.");
    Require(selectedBounds.top > titleBounds.bottom,
            "Focused tray UIA bounds did not remain below the dashboard title.");
    const POINT pointer{
        selectedBounds.left + (selectedBounds.right - selectedBounds.left) / 2,
        selectedBounds.top + (selectedBounds.bottom - selectedBounds.top) / 2};
    const LRESULT hit = SendMessageW(
        window, WM_NCHITTEST, 0, MAKELPARAM(pointer.x, pointer.y));
    Require(hit == HTCLIENT,
            "Focused tray UIA center did not map to the authored pointer surface.");
}

void VerifyFocusedUiaInside(
    IUIAutomation* automation,
    HWND window,
    const RECT visibleContent) {
    ComPtr<IUIAutomationElement> root;
    ComPtr<IUIAutomationElement> focused;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        root.Reset();
        focused.Reset();
        if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf())) || !root)
            return false;
        VARIANT value{};
        V_VT(&value) = VT_BOOL;
        V_BOOL(&value) = VARIANT_TRUE;
        ComPtr<IUIAutomationCondition> condition;
        return SUCCEEDED(automation->CreatePropertyCondition(
                   UIA_HasKeyboardFocusPropertyId, value,
                   condition.GetAddressOf())) && condition &&
            SUCCEEDED(root->FindFirst(
                TreeScope_Descendants, condition.Get(), focused.GetAddressOf())) && focused;
    }), "Re-shown production widget omitted its focused UIA descendant.");
    RECT bounds{};
    Require(SUCCEEDED(focused->get_CurrentBoundingRectangle(&bounds)) &&
                Contains(visibleContent, bounds),
            "Re-shown production widget focus escaped visible content bounds.");
}

void Run(const Arguments& arguments) {
    Require(fs::is_regular_file(arguments.installation / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
    TemporaryProfile profile;
    const std::wstring hostArguments =
        L"--show --process-profile " + profile.profile();
    HostProcess host(arguments.installation, profile.localAppData(), hostArguments);
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host.Id());
        return window && IsWindowVisible(window) != FALSE;
    }), "Cold production host did not expose its first visible window.");
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
    VerifyDashboardUia(automation.Get(), window, firstContent);

    const auto beforeWidget = ReadUtf8(profile.logPath()).size();
    SendKey(window, VK_RETURN);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(profile.logPath());
        return log.find(" surface=widget shell=shared ", beforeWidget) !=
                std::string::npos &&
            log.find(" content=admitted ", beforeWidget) != std::string::npos;
    }), "Opening the focused widget did not admit content in the shared production host.");
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
    VerifyFocusedUiaInside(automation.Get(), window, reshownContent);

    PostMessageW(window, WM_CLOSE, 0, 0);
    Require(WaitForSingleObject(host.Process(), kStepTimeoutMilliseconds) == WAIT_OBJECT_0,
            "Production OverlayHost did not stop after the cold dashboard scenario.");
    std::cout << "ColdDashboardHostTests: first commit, dashboard UIA/pointer, widget hide, and re-show passed\n";
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
        Run(ParseArguments(argc, argv));
        CoUninitialize();
        return EXIT_SUCCESS;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        CoUninitialize();
        return EXIT_FAILURE;
    }
}
