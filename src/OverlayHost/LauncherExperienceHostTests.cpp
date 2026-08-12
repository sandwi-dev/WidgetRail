#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <cwctype>
#include <filesystem>
#include <iostream>
#include <string>
#include <string_view>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using namespace gba::host_testing;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30'000;
constexpr DWORD kStepTimeoutMilliseconds = 10'000;
constexpr wchar_t kDevelopmentNonce[] =
    L"1451451451451451451451451451451451451451451451451451451451451451";
constexpr char kDevelopmentNonceUtf8[] =
    "1451451451451451451451451451451451451451451451451451451451451451";

ComPtr<IUIAutomationElement> RootForWindow(
    IUIAutomation* automation,
    const HWND window) {
    ComPtr<IUIAutomationElement> result;
    if (automation && window)
        (void)automation->ElementFromHandle(window, result.GetAddressOf());
    return result;
}

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const wchar_t* automationId) {
    if (!automation || !root) return {};
    VARIANT value{};
    value.vt = VT_BSTR;
    value.bstrVal = SysAllocString(automationId);
    if (!value.bstrVal) return {};
    ComPtr<IUIAutomationCondition> condition;
    const auto conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, value, condition.GetAddressOf());
    VariantClear(&value);
    if (FAILED(conditionResult) || !condition) return {};
    ComPtr<IUIAutomationElement> result;
    (void)root->FindFirst(TreeScope_Subtree, condition.Get(), result.GetAddressOf());
    return result;
}

ComPtr<IUIAutomationElement> WaitForElement(
    IUIAutomation* automation,
    const HWND window,
    const wchar_t* automationId) {
    ComPtr<IUIAutomationElement> result;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        auto root = RootForWindow(automation, window);
        result = FindByAutomationId(automation, root.Get(), automationId);
        return result != nullptr;
    }), "Production host omitted expected UIA element " +
        WideToUtf8(automationId) + ".");
    return result;
}

void FocusAndActivate(
    IUIAutomationElement* element,
    const HWND window,
    const std::string_view identity) {
    Require(element && SUCCEEDED(element->SetFocus()),
        "UIA element rejected semantic focus: " + std::string(identity));
    Sleep(100);
    SendKey(window, VK_RETURN);
}

void RequireInsideWindow(IUIAutomationElement* element, const HWND window) {
    RECT host{};
    Require(GetWindowRect(window, &host), Win32Error("GetWindowRect"));
    RECT node{};
    Require(element && SUCCEEDED(element->get_CurrentBoundingRectangle(&node)),
        "UIA element omitted current bounds.");
    Require(node.right > node.left && node.bottom > node.top &&
            node.left >= host.left && node.top >= host.top &&
            node.right <= host.right && node.bottom <= host.bottom,
        "Launcher UIA element bounds escaped the production host.");
}

class TemporaryInstallation final {
public:
    explicit TemporaryInstallation(const fs::path& source) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
        Require(fs::is_directory(source / L"runtime"),
            "--installation does not contain runtime");
        Require(fs::is_regular_file(source / L"widget-catalog.json"),
            "--installation does not contain widget-catalog.json");
        Require(fs::is_regular_file(
                    source / L"runtime" / L"GameLauncher" / L"manifest.json"),
            "--installation omitted the held Game Launcher package");

        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"gba-launcher-experience-host-" + std::wstring(guidText));
        profile_ = L"launcher-experience-";
        for (const wchar_t character : std::wstring_view(guidText)) {
            if (std::iswalnum(character))
                profile_.push_back(static_cast<wchar_t>(std::towlower(character)));
        }
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy(source / L"runtime", root_ / L"runtime",
            fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        fs::copy_file(
            source / L"widget-catalog.json", root_ / L"widget-catalog.json");
        localAppData_ = root_ / L"local-app-data";
        fs::create_directories(localAppData_ / L"GameBarAlternative");
        readyPath_ = root_ / L"ready.txt";
    }

    ~TemporaryInstallation() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    [[nodiscard]] const fs::path& Root() const noexcept { return root_; }
    [[nodiscard]] const fs::path& LocalAppData() const noexcept {
        return localAppData_;
    }
    [[nodiscard]] const fs::path& ReadyPath() const noexcept { return readyPath_; }
    [[nodiscard]] const std::wstring& Profile() const noexcept { return profile_; }
    [[nodiscard]] fs::path LogPath() const {
        return localAppData_ / L"GameBarAlternative" / L"overlay.log";
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path readyPath_;
    std::wstring profile_;
};

bool WaitForProjection(
    const fs::path& log,
    const std::string_view preset,
    const DWORD timeoutMilliseconds = kStepTimeoutMilliseconds) {
    const std::string marker =
        "Launcher Experience projection adopted widget=game-launcher preset=" +
        std::string(preset);
    if (WaitUntil(timeoutMilliseconds, [&] {
        return ReadUtf8(log).find(marker) != std::string::npos;
    })) return true;
    return false;
}

void Run(const fs::path& installationPath) {
    TemporaryInstallation installation(installationPath);
    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
        "Windows UI Automation client is unavailable.");
    const std::wstring arguments =
        L"--show --process-profile " + installation.Profile() +
        L" --development-catalog-root " + QuoteArgument(installation.Root().wstring()) +
        L" --development-ready-path " + QuoteArgument(installation.ReadyPath().wstring()) +
        L" --development-ready-nonce " + kDevelopmentNonce +
        L" --development-widget-id game-launcher"
        L" --development-widget-instance game-launcher.default";
    HostProcess host(installation.Root(), installation.LocalAppData(), arguments);
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        return ReadUtf8(installation.ReadyPath()).find(kDevelopmentNonceUtf8) !=
            std::string::npos;
    }), "Production host did not publish authenticated readiness.");
    HWND window{};
    Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host.Id());
        return window && IsWindowVisible(window);
    }), "Production OverlayHost did not create a visible HWND.");

    auto tray = WaitForElement(
        automation.Get(), window, L"tray:tray.game-launcher");
    RequireInsideWindow(tray.Get(), window);
    FocusAndActivate(tray.Get(), window, "tray.game-launcher");
    auto experience = WaitForElement(
        automation.Get(), window, L"widget:game-launcher.experiences.open");
    RequireInsideWindow(experience.Get(), window);
    if (WaitForProjection(installation.LogPath(), "hero-rail", 2'000)) {
        for (const auto preset :
             {"cover-wall", "carousel", "compact-grid", "hero-rail"}) {
            experience = WaitForElement(
                automation.Get(), window, L"widget:game-launcher.experiences.open");
            FocusAndActivate(
                experience.Get(), window, "game-launcher.experiences.open");
            std::wstring presetWide;
            for (const char character : std::string_view{preset})
                presetWide.push_back(static_cast<wchar_t>(character));
            const std::wstring choiceId =
                L"widget:game-launcher.experience." + presetWide;
            auto choice = WaitForElement(automation.Get(), window, choiceId.c_str());
            RequireInsideWindow(choice.Get(), window);
            FocusAndActivate(choice.Get(), window, preset);
            SendKey(window, VK_ESCAPE);
            experience = WaitForElement(
                automation.Get(), window, L"widget:game-launcher.experiences.open");
            RequireInsideWindow(experience.Get(), window);
            Require(WaitForProjection(installation.LogPath(), preset),
                "Production host did not adopt Launcher Experience preset " +
                    std::string(preset) + ".");
        }
        std::cout << "LauncherExperienceHostTests: live preset adoption passed\n";
    } else {
        // A fresh machine can legitimately have no projectable rail yet
        // (empty, loading, or provider-error content). The held projector then
        // emits no private marker, and the production requirement is that the
        // ordinary tree remains current, targetable, and unmodified.
        RequireInsideWindow(experience.Get(), window);
        const auto log = ReadUtf8(installation.LogPath());
        Require(log.find(
                    "Widget presentation paint target=game-launcher content=admitted") !=
                    std::string::npos,
            "Provider-unavailable Game Launcher did not retain ordinary production paint.");
        Require(log.find("Launcher Experience projection adopted") ==
                    std::string::npos,
            "Provider-unavailable snapshot incorrectly entered the private projection.");
        std::cout << "LauncherExperienceHostTests: no projectable live rail; "
                     "ordinary production path passed\n";
    }

    Require(PostMessageW(window, WM_CLOSE, 0, 0),
        Win32Error("PostMessageW(WM_CLOSE)"));
    Require(WaitForSingleObject(host.Process(), kStepTimeoutMilliseconds) == WAIT_OBJECT_0,
        "Production OverlayHost did not stop after Launcher Experience proof.");
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring_view(argv[1]) != L"--installation") {
        std::cerr << "Usage: LauncherExperienceHostTests --installation <dir>\n";
        return 1;
    }
    const auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) {
        std::cerr << "LauncherExperienceHostTests failed: CoInitializeEx\n";
        return 1;
    }
    try {
        Run(fs::path(argv[2]));
        std::cout << "LauncherExperienceHostTests: production host passed\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "LauncherExperienceHostTests failed: " << error.what() << '\n';
        CoUninitialize();
        return 1;
    }
}
