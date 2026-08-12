#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <algorithm>
#include <cwctype>
#include <exception>
#include <filesystem>
#include <iostream>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using namespace gba::host_testing;

namespace {

constexpr DWORD kStartupTimeoutMilliseconds = 30'000;
constexpr DWORD kStepTimeoutMilliseconds = 10'000;
constexpr wchar_t kEvidenceNonce[] =
    L"1461461461461461461461461461461461461461461461461461461461461461";
constexpr wchar_t kSeededGameName[] = L"DLV-146 Trusted Game";

struct ProjectionRecord final {
    std::string preset;
    long long sequence{};
    std::string instance;
    std::string scope;
};

ComPtr<IUIAutomationElement> RootForWindow(
    IUIAutomation* automation,
    const HWND window) {
    ComPtr<IUIAutomationElement> result;
    if (automation && window)
        (void)automation->ElementFromHandle(window, result.GetAddressOf());
    return result;
}

std::wstring StringProperty(
    IUIAutomationElement* element,
    const PROPERTYID property) {
    if (!element) return {};
    BSTR value{};
    HRESULT result = E_INVALIDARG;
    if (property == UIA_AutomationIdPropertyId)
        result = element->get_CurrentAutomationId(&value);
    else if (property == UIA_NamePropertyId)
        result = element->get_CurrentName(&value);
    if (FAILED(result) || !value) return {};
    std::wstring copy(value, SysStringLen(value));
    SysFreeString(value);
    return copy;
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

ComPtr<IUIAutomationElement> FindByAutomationIdPrefix(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view prefix) {
    if (!automation || !root || prefix.empty()) return {};
    ComPtr<IUIAutomationCondition> condition;
    if (FAILED(automation->CreateTrueCondition(condition.GetAddressOf())) || !condition)
        return {};
    ComPtr<IUIAutomationElementArray> descendants;
    if (FAILED(root->FindAll(
            TreeScope_Subtree, condition.Get(), descendants.GetAddressOf())) ||
        !descendants)
        return {};
    int length{};
    if (FAILED(descendants->get_Length(&length))) return {};
    for (int index = 0; index < length; ++index) {
        ComPtr<IUIAutomationElement> candidate;
        if (SUCCEEDED(descendants->GetElement(index, candidate.GetAddressOf())) &&
            candidate && StringProperty(candidate.Get(), UIA_AutomationIdPropertyId)
                .starts_with(prefix))
            return candidate;
    }
    return {};
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
    ComPtr<IUIAutomationInvokePattern> invoke;
    Require(SUCCEEDED(element->GetCurrentPatternAs(
                UIA_InvokePatternId,
                IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) && invoke,
        "UIA element omitted InvokePattern: " + std::string(identity));
    Require(SUCCEEDED(invoke->Invoke()),
        "UIA element rejected InvokePattern: " + std::string(identity));
    (void)window;
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

void RequireFocusedIdentity(
    IUIAutomation* automation,
    IUIAutomationElement* element,
    const std::wstring_view automationId) {
    Require(element && SUCCEEDED(element->SetFocus()),
        "Seeded Game Launcher item rejected semantic focus.");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> focused;
        return SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) &&
            focused &&
            StringProperty(focused.Get(), UIA_AutomationIdPropertyId) == automationId;
    }), "Production UIA focus did not resolve to the seeded game's stable identity.");
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& fixtureBridge,
        const std::string_view scenario) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
        Require(fs::is_directory(source / L"runtime"),
            "--installation does not contain runtime");
        Require(fs::is_regular_file(source / L"widget-catalog.json"),
            "--installation does not contain widget-catalog.json");
        Require(fs::is_regular_file(
                    source / L"runtime" / L"GameLauncher" / L"manifest.json"),
            "--installation omitted the held Game Launcher package");
        Require(fs::is_regular_file(fixtureBridge),
            "--fixture-bridge does not name the published test fixture");

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

        const auto fixtureRoot = fixtureBridge.parent_path();
        const auto bridgeRoot = root_ / L"runtime" / L"Bridge";
        for (const auto& entry : fs::recursive_directory_iterator(fixtureRoot)) {
            const auto relative = fs::relative(entry.path(), fixtureRoot);
            const auto destination = bridgeRoot / relative;
            if (entry.is_directory()) {
                fs::create_directories(destination);
            } else if (entry.is_regular_file()) {
                fs::create_directories(destination.parent_path());
                fs::copy_file(entry.path(), destination,
                    fs::copy_options::overwrite_existing);
            }
        }
        fs::copy_file(fixtureBridge, bridgeRoot / L"WidgetBridge.exe",
            fs::copy_options::overwrite_existing);

        localAppData_ = root_ / L"local-app-data";
        const auto settings = localAppData_ / L"GameBarAlternative";
        fs::create_directories(settings);
        WriteUtf8(
            bridgeRoot / L"launcher-experience-provider-fixture.txt",
            std::string(scenario) + WideToUtf8(settings.wstring()) + "\n");
        performancePath_ = root_ / L"performance.txt";
    }

    ~TemporaryInstallation() {
        if (std::uncaught_exceptions() != 0) {
            const auto diagnostic = ReadUtf8(LogPath());
            if (!diagnostic.empty())
                std::cerr << "LauncherExperienceHostTests diagnostic:\n" <<
                    diagnostic << '\n';
        }
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    [[nodiscard]] const fs::path& Root() const noexcept { return root_; }
    [[nodiscard]] const fs::path& LocalAppData() const noexcept {
        return localAppData_;
    }
    [[nodiscard]] const fs::path& PerformancePath() const noexcept {
        return performancePath_;
    }
    [[nodiscard]] const std::wstring& Profile() const noexcept { return profile_; }
    [[nodiscard]] fs::path LogPath() const {
        return localAppData_ / L"GameBarAlternative" / L"overlay.log";
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path performancePath_;
    std::wstring profile_;
};

std::optional<std::string> Field(
    const std::string_view line,
    const std::string_view name) {
    const auto start = line.find(name);
    if (start == std::string_view::npos) return std::nullopt;
    const auto valueStart = start + name.size();
    const auto end = line.find(' ', valueStart);
    return std::string(line.substr(
        valueStart, end == std::string_view::npos ? line.size() - valueStart :
            end - valueStart));
}

std::vector<ProjectionRecord> ProjectionRecords(const fs::path& log) {
    constexpr std::string_view prefix =
        "Launcher Experience projection adopted widget=game-launcher ";
    const auto text = ReadUtf8(log);
    std::vector<ProjectionRecord> records;
    std::size_t cursor{};
    while ((cursor = text.find(prefix, cursor)) != std::string::npos) {
        const auto end = text.find('\n', cursor);
        const std::string_view line(text.data() + cursor,
            (end == std::string::npos ? text.size() : end) - cursor);
        const auto preset = Field(line, "preset=");
        const auto sequence = Field(line, "sequence=");
        const auto instance = Field(line, "instance=");
        const auto scope = Field(line, "scope=");
        if (preset && sequence && instance && scope) {
            records.push_back({*preset, std::stoll(*sequence), *instance, *scope});
        }
        cursor = end == std::string::npos ? text.size() : end + 1;
    }
    return records;
}

ProjectionRecord WaitForNewProjection(
    const fs::path& log,
    const std::string_view preset,
    const std::size_t priorCount) {
    std::optional<ProjectionRecord> result;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto records = ProjectionRecords(log);
        const auto count = static_cast<std::size_t>(std::count_if(
            records.begin(), records.end(), [&](const auto& record) {
                return record.preset == preset;
            }));
        if (count <= priorCount) return false;
        const auto found = std::find_if(records.rbegin(), records.rend(),
            [&](const auto& record) { return record.preset == preset; });
        if (found == records.rend()) return false;
        result = *found;
        return true;
    }), "Production host did not adopt Launcher Experience preset " +
        std::string(preset) + ".");
    return *result;
}

struct RunningHost final {
    TemporaryInstallation installation;
    HostProcess host;
    HWND window{};

    RunningHost(
        const fs::path& source,
        const fs::path& fixtureBridge,
        const std::string_view scenario)
        : installation(source, fixtureBridge, scenario),
          host(installation.Root(), installation.LocalAppData(), Arguments()) {
        Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
            window = LocateHostWindow(host.Id());
            return window && IsWindowVisible(window);
        }), "Production OverlayHost did not create a visible HWND.");
    }

    ~RunningHost() {
        if (window && IsWindow(window))
            (void)PostMessageW(window, WM_CLOSE, 0, 0);
    }

    [[nodiscard]] std::wstring Arguments() const {
        return L"--show --process-profile " + installation.Profile() +
            L" --performance-state interactive"
            L" --performance-widget-id game-launcher"
            L" --performance-diagnostics-path " +
            QuoteArgument(installation.PerformancePath().wstring()) +
            L" --performance-diagnostics-nonce " + kEvidenceNonce;
    }

    void Stop() {
        Require(PostMessageW(window, WM_CLOSE, 0, 0),
            Win32Error("PostMessageW(WM_CLOSE)"));
        Require(WaitForSingleObject(host.Process(), kStepTimeoutMilliseconds) ==
                WAIT_OBJECT_0,
            "Production OverlayHost did not stop after Launcher Experience proof.");
        window = nullptr;
    }
};

std::wstring AssertSeededGame(
    IUIAutomation* automation,
    const HWND window,
    const std::wstring_view expectedIdentity = {}) {
    ComPtr<IUIAutomationElement> seeded;
    if (expectedIdentity.empty()) {
        Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
            auto root = RootForWindow(automation, window);
            seeded = FindByAutomationIdPrefix(
                automation, root.Get(), L"widget:game-launcher.item.grid.game.");
            return seeded != nullptr;
        }), "Production host did not expose the seeded Game Launcher identity.");
    } else {
        auto root = RootForWindow(automation, window);
        seeded = FindByAutomationId(
            automation, root.Get(), std::wstring(expectedIdentity).c_str());
        Require(seeded != nullptr,
            "Production host omitted the seeded game's stable UIA identity.");
    }
    const auto identity = StringProperty(seeded.Get(), UIA_AutomationIdPropertyId);
    Require(identity.starts_with(L"widget:game-launcher.item.grid.game."),
        "Seeded game did not retain the ordinary Game Launcher collection identity.");
    Require(expectedIdentity.empty() || identity == expectedIdentity,
        "Seeded game changed stable collection/focus identity across presets.");
    RequireInsideWindow(seeded.Get(), window);
    const auto name = StringProperty(seeded.Get(), UIA_NamePropertyId);
    Require(name.find(kSeededGameName) != std::wstring::npos,
        "Seeded game UIA identity did not expose its trusted display name.");
    RequireFocusedIdentity(automation, seeded.Get(), identity);
    return identity;
}

void RequireStableRecord(
    const ProjectionRecord& record,
    const ProjectionRecord& first,
    const long long priorSequence) {
    Require(record.instance == first.instance && record.scope == first.scope,
        "Launcher Experience adoption changed instance or input-scope identity.");
    Require(record.sequence > priorSequence,
        "Launcher Experience adoption did not advance snapshot sequence.");
}

void RunAdoption(
    IUIAutomation* automation,
    const fs::path& installationPath,
    const fs::path& fixtureBridge) {
    RunningHost running(installationPath, fixtureBridge, "adoption\n");
    auto tray = WaitForElement(
        automation, running.window, L"tray:tray.game-launcher");
    RequireInsideWindow(tray.Get(), running.window);

    const auto first = WaitForNewProjection(
        running.installation.LogPath(), "hero-rail", 0);
    auto seededIdentity = AssertSeededGame(automation, running.window);
    long long lastSequence = first.sequence;
    for (const std::string_view preset :
         {"cover-wall", "carousel", "compact-grid", "hero-rail"}) {
        const auto before = ProjectionRecords(running.installation.LogPath());
        const auto priorCount = static_cast<std::size_t>(std::count_if(
            before.begin(), before.end(), [&](const auto& record) {
                return record.preset == preset;
            }));
        auto experience = WaitForElement(
            automation, running.window,
            L"widget:game-launcher.experiences.open");
        RequireInsideWindow(experience.Get(), running.window);
        FocusAndActivate(
            experience.Get(), running.window, "game-launcher.experiences.open");
        std::wstring presetWide(preset.begin(), preset.end());
        const auto choiceId = L"widget:game-launcher.experience." + presetWide;
        auto choice = WaitForElement(automation, running.window, choiceId.c_str());
        RequireInsideWindow(choice.Get(), running.window);
        FocusAndActivate(choice.Get(), running.window, preset);
        auto back = WaitForElement(
            automation, running.window, L"widget:game-launcher.experiences.back");
        RequireInsideWindow(back.Get(), running.window);
        FocusAndActivate(back.Get(), running.window, "game-launcher.experiences.back");
        const auto record = WaitForNewProjection(
            running.installation.LogPath(), preset, priorCount);
        RequireStableRecord(record, first, lastSequence);
        lastSequence = record.sequence;
        seededIdentity = AssertSeededGame(
            automation, running.window, seededIdentity);
    }

    SendKey(running.window, VK_ESCAPE);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> focused;
        return SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) &&
            focused && StringProperty(focused.Get(), UIA_AutomationIdPropertyId) ==
                L"tray:tray.game-launcher";
    }), "Game Launcher Back route did not return focus to its tray identity.");
    running.Stop();
    std::cout << "LauncherExperienceHostTests: deterministic live preset adoption passed\n";
}

void RunFallback(
    IUIAutomation* automation,
    const fs::path& installationPath,
    const fs::path& fixtureBridge) {
    RunningHost running(installationPath, fixtureBridge, "fallback\n");
    auto tray = WaitForElement(
        automation, running.window, L"tray:tray.game-launcher");
    RequireInsideWindow(tray.Get(), running.window);
    auto experience = WaitForElement(
        automation, running.window, L"widget:game-launcher.experiences.open");
    RequireInsideWindow(experience.Get(), running.window);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find(
                   "Widget presentation paint target=game-launcher content=admitted") !=
                   std::string::npos &&
            log.find("semantic-focus=widget:game-launcher.error.action") !=
                std::string::npos;
    }), "Provider-unavailable Game Launcher did not retain ordinary production paint.");
    Require(ProjectionRecords(running.installation.LogPath()).empty(),
        "Provider-unavailable snapshot incorrectly entered private projection.");
    running.Stop();
    std::cout << "LauncherExperienceHostTests: separate provider fallback passed\n";
}

void Run(const fs::path& installationPath, const fs::path& fixtureBridge) {
    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
        "Windows UI Automation client is unavailable.");
    RunAdoption(automation.Get(), installationPath, fixtureBridge);
    RunFallback(automation.Get(), installationPath, fixtureBridge);
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 5 || std::wstring_view(argv[1]) != L"--installation" ||
        std::wstring_view(argv[3]) != L"--fixture-bridge") {
        std::cerr << "Usage: LauncherExperienceHostTests --installation <dir> "
                     "--fixture-bridge <exe>\n";
        return 1;
    }
    const auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) {
        std::cerr << "LauncherExperienceHostTests failed: CoInitializeEx\n";
        return 1;
    }
    try {
        Run(fs::path(argv[2]), fs::path(argv[4]));
        std::cout << "LauncherExperienceHostTests: production host passed\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "LauncherExperienceHostTests failed: " << error.what() << '\n';
        CoUninitialize();
        return 1;
    }
}
