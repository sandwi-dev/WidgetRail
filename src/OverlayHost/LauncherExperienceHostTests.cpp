#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <algorithm>
#include <cwctype>
#include <exception>
#include <filesystem>
#include <fstream>
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
constexpr DWORD kStressWindowMilliseconds = 60'000;
constexpr wchar_t kEvidenceNonce[] =
    L"1461461461461461461461461461461461461461461461461461461461461461";
constexpr wchar_t kSeededGameName[] = L"DLV-146 Trusted Game";
constexpr wchar_t kMotionGameName[] = L"DLV-148 Motion Game";

struct ProjectionRecord final {
    std::string preset;
    long long sequence{};
    std::string instance;
    std::string scope;
    std::string focus;
    std::string effect;
    std::string background;
    std::string backgroundFocus;
    std::string railPrevious;
    std::string railNext;
    double inputP95Milliseconds{};
    std::size_t inputSamples{};
    std::size_t degraded{};
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
        if (scenario.starts_with("selection")) {
            WriteExperiencePackage(settings, "1.0.0", "cover-wall");
            WriteExperiencePackage(settings, "2.0.0", "carousel");
            WriteSelection("1.0.0", false);
        }
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
            const auto backend = ReadUtf8(BackendDiagnosticPath());
            if (!backend.empty())
                std::cerr << "LauncherExperienceHostTests backend:\n" <<
                    backend << '\n';
            const auto fixtureError = ReadUtf8(
                SettingsRoot() / L"launcher-experience-fixture-error.txt");
            if (!fixtureError.empty())
                std::cerr << "LauncherExperienceHostTests fixture:\n" <<
                    fixtureError << '\n';
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
    [[nodiscard]] fs::path BackendDiagnosticPath() const {
        return localAppData_ / L"GameBarAlternative" /
            L"launcher-experience-backend.txt";
    }
    [[nodiscard]] fs::path SettingsRoot() const {
        return localAppData_ / L"GameBarAlternative";
    }
    [[nodiscard]] fs::path ExperienceVersion(const std::wstring_view version) const {
        return SettingsRoot() / L"launcher-experiences" /
            L"dev.example.production" / std::wstring(version);
    }
    void WriteSelection(const std::string_view version, const bool global) const {
        WriteUtf8(
            SettingsRoot() / L"platform-settings.json",
            "{\"schemaVersion\":1,\"appearance\":{"
            "\"themeId\":\"builtin.default\",\"themeVersion\":\"1.0.0\","
            "\"interfaceScale\":1,\"textScale\":1,\"backdropOpacity\":0.64,"
            "\"motion\":\"system\"},\"launcherExperience\":{"
            "\"useGlobalAppearance\":" + std::string(global ? "true" : "false") +
            ",\"selectedId\":\"dev.example.production\",\"selectedVersion\":\"" +
            std::string(version) +
            "\",\"lastGoodId\":\"dev.example.production\",\"lastGoodVersion\":\"" +
            std::string(version) + "\"}}");
    }
    void WriteBuiltInRecovery() const {
        WriteUtf8(
            SettingsRoot() / L"platform-settings.json",
            "{\"schemaVersion\":1,\"appearance\":{"
            "\"themeId\":\"builtin.default\",\"themeVersion\":\"1.0.0\","
            "\"interfaceScale\":1,\"textScale\":1,\"backdropOpacity\":0.64,"
            "\"motion\":\"system\"},\"launcherExperience\":{"
            "\"useGlobalAppearance\":false,"
            "\"selectedId\":\"org.gbar.builtin.hero-rail\","
            "\"selectedVersion\":\"1.0.0\","
            "\"lastGoodId\":\"org.gbar.builtin.hero-rail\","
            "\"lastGoodVersion\":\"1.0.0\"}}");
    }
    void RewriteExperienceStyle(const std::wstring_view version) const {
        WriteUtf8(
            ExperienceVersion(version) / L"styles" / L"launcher.gbss",
            "launcher-game-rail { color: #80ff80; } "
            "launcher-details-panel { background: rgba(0, 0, 0, 0.5); }");
    }

private:
    void WriteExperiencePackage(
        const fs::path& settings,
        const std::string_view version,
        const std::string_view preset) const {
        const auto directory = settings / L"launcher-experiences" /
            L"dev.example.production" /
            std::wstring(version.begin(), version.end());
        fs::create_directories(directory / L"layouts");
        fs::create_directories(directory / L"styles");
        fs::create_directories(directory / L"assets");
        WriteUtf8(directory / L"launcher.json",
            "{\"schemaVersion\":1,\"id\":\"dev.example.production\","
            "\"publisher\":\"dev.example\",\"name\":\"Production Fixture\","
            "\"version\":\"" + std::string(version) + "\",\"layoutPreset\":\"" +
            std::string(preset) +
            "\",\"compositionFile\":\"layouts/layout.json\","
            "\"styleFile\":\"styles/launcher.gbss\","
            "\"previewFile\":\"assets/background.png\",\"parameters\":{"
            "\"backgroundMode\":\"pack-asset\",\"focusEffect\":\"lift\","
            "\"motionIntensity\":\"reduced\"}}");
        const std::string root =
            "{\"type\":\"overlay\",\"children\":["
            "{\"type\":\"region\",\"slot\":\"hero-background\",\"region\":{"
            "\"x\":0,\"y\":0,\"width\":1,\"height\":1}},"
            "{\"type\":\"region\",\"slot\":\"details-panel\",\"region\":{"
            "\"x\":0.08,\"y\":0.08,\"width\":0.5,\"height\":0.36}},"
            "{\"type\":\"region\",\"slot\":\"source-status\",\"region\":{"
            "\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1}},"
            "{\"type\":\"region\",\"slot\":\"game-rail\","
            "\"orientation\":\"horizontal\",\"region\":{"
            "\"x\":0.08,\"y\":0.55,\"width\":0.84,\"height\":0.27}},"
            "{\"type\":\"region\",\"slot\":\"controller-hints\",\"region\":{"
            "\"x\":0.52,\"y\":0.88,\"width\":0.4,\"height\":0.08}}]}";
        WriteUtf8(directory / L"layouts" / L"layout.json",
            "{\"schemaVersion\":1,\"branches\":{"
            "\"compact\":{\"root\":" + root + "},"
            "\"standard\":{\"root\":" + root + "},"
            "\"wide\":{\"root\":" + root + "}}}");
        WriteUtf8(directory / L"styles" / L"launcher.gbss",
            "launcher-game-rail { color: #ffffff; } "
            "launcher-details-panel { background: rgba(0, 0, 0, 0.5); }");
        constexpr unsigned char png[]{
            0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,0x00,0x00,0x00,0x0d,
            0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1f,0x15,0xc4,0x89,0x00,0x00,0x00,
            0x0d,0x49,0x44,0x41,0x54,0x78,0xda,0x63,0xfc,0xcf,0xc0,0x50,
            0x0f,0x00,0x05,0x83,0x02,0x7f,0x94,0xad,0xf1,0x59,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4e,0x44,0xae,0x42,0x60,0x82};
        std::ofstream output(
            directory / L"assets" / L"background.png",
            std::ios::binary | std::ios::trunc);
        output.write(reinterpret_cast<const char*>(png), sizeof(png));
        Require(output.good(), "Could not seed Launcher Experience background.");
    }

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
        const auto focus = Field(line, "focus=");
        const auto effect = Field(line, "effect=");
        const auto background = Field(line, "background=");
        const auto backgroundFocus = Field(line, "background-focus=");
        const auto railPrevious = Field(line, "rail-previous=");
        const auto railNext = Field(line, "rail-next=");
        const auto inputP95 = Field(line, "input-to-focus-p95-ms=");
        const auto inputSamples = Field(line, "input-samples=");
        const auto degraded = Field(line, "degraded=");
        if (preset && sequence && instance && scope) {
            ProjectionRecord record{*preset, std::stoll(*sequence), *instance, *scope};
            record.focus = focus.value_or("");
            record.effect = effect.value_or("");
            record.background = background.value_or("");
            record.backgroundFocus = backgroundFocus.value_or("");
            record.railPrevious = railPrevious.value_or("");
            record.railNext = railNext.value_or("");
            record.inputP95Milliseconds = inputP95 ? std::stod(*inputP95) : 0;
            record.inputSamples = inputSamples
                ? static_cast<std::size_t>(std::stoull(*inputSamples)) : 0;
            record.degraded = degraded
                ? static_cast<std::size_t>(std::stoull(*degraded)) : 0;
            records.push_back(std::move(record));
        }
        cursor = end == std::string::npos ? text.size() : end + 1;
    }
    return records;
}

ProjectionRecord WaitForFocusPresentation(
    const fs::path& log,
    const std::string_view preset,
    const std::wstring_view automationId,
    const std::size_t /*priorCount*/) {
    const auto rawFocus = WideToUtf8(
        automationId.starts_with(L"widget:") ? automationId.substr(7) : automationId);
    std::optional<ProjectionRecord> result;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto records = ProjectionRecords(log);
        const auto found = std::find_if(records.rbegin(), records.rend(),
            [&](const auto& record) {
                return record.preset == preset && record.focus == rawFocus &&
                    record.background == "ready" &&
                    record.backgroundFocus == rawFocus;
            });
        if (found == records.rend()) return false;
        result = *found;
        return true;
    }), "Production host did not atomically commit focused Launcher artwork.");
    return *result;
}

struct TraversedGames final {
    std::wstring first;
    std::wstring second;
};

TraversedGames TraverseTwoGames(
    IUIAutomation* automation,
    const HWND window,
    const fs::path& log,
    const std::string_view preset) {
    ComPtr<IUIAutomationElement> first;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        auto root = RootForWindow(automation, window);
        first = FindByAutomationIdPrefix(
            automation, root.Get(), L"widget:game-launcher.item.grid.game.");
        return first != nullptr;
    }), "Production host omitted the first trusted game tile.");
    const auto firstId = StringProperty(first.Get(), UIA_AutomationIdPropertyId);
    RequireInsideWindow(first.Get(), window);
    const auto firstCount = ProjectionRecords(log).size();
    RequireFocusedIdentity(automation, first.Get(), firstId);
    const auto firstPresentation = WaitForFocusPresentation(
        log, preset, firstId, firstCount);
    Require(!firstPresentation.railNext.empty() &&
            firstPresentation.railNext != "none",
        "The canonical Launcher frame omitted its second-game focus edge.");
    const std::wstring railNextWide(
        firstPresentation.railNext.begin(), firstPresentation.railNext.end());
    const std::wstring secondId = L"widget:" + railNextWide;
    Require(secondId != firstId,
        "The projected rail edge did not expose a distinct second game identity.");
    return {firstId, secondId};
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
        const std::string_view scenario,
        const std::wstring_view performanceState = L"interactive")
        : installation(source, fixtureBridge, scenario),
          host(installation.Root(), installation.LocalAppData(),
               Arguments(performanceState)) {
        Require(WaitUntil(kStartupTimeoutMilliseconds, [&] {
            window = LocateHostWindow(host.Id());
            return window && IsWindowVisible(window);
        }), "Production OverlayHost did not create a visible HWND.");
    }

    ~RunningHost() {
        if (window && IsWindow(window))
            (void)PostMessageW(window, WM_CLOSE, 0, 0);
    }

    [[nodiscard]] std::wstring Arguments(
        const std::wstring_view performanceState) const {
        return L"--show --process-profile " + installation.Profile() +
            L" --performance-state " + std::wstring(performanceState) +
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
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        return ReadUtf8(running.installation.BackendDiagnosticPath()).find(
            "items=2") != std::string::npos;
    }), "The trusted app-library backend did not seed exactly two games.");
    auto tray = WaitForElement(
        automation, running.window, L"tray:tray.game-launcher");
    RequireInsideWindow(tray.Get(), running.window);

    const auto first = WaitForNewProjection(
        running.installation.LogPath(), "hero-rail", 0);
    auto seededIdentity = AssertSeededGame(automation, running.window);
    std::optional<TraversedGames> stableGameIdentities;
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
        const auto games = TraverseTwoGames(
            automation, running.window, running.installation.LogPath(), preset);
        if (!stableGameIdentities) stableGameIdentities = games;
        Require(games.first == stableGameIdentities->first &&
                games.second == stableGameIdentities->second,
            "Game identity changed across Launcher Experience presets.");
        stableGameIdentities = games;
    }

    const auto stressStarted = GetTickCount64();
    const auto stressEnds = stressStarted + kStressWindowMilliseconds;
    bool moveToFirst = false;
    auto root = RootForWindow(automation, running.window);
    auto firstGame = FindByAutomationId(
        automation, root.Get(), stableGameIdentities->first.c_str());
    auto focusAlternate = FindByAutomationId(
        automation, root.Get(), L"widget:game-launcher.experiences.open");
    Require(firstGame && focusAlternate,
        "The production stress fixture omitted an actionable focus identity.");
    while (GetTickCount64() < stressEnds) {
        const auto expected = moveToFirst
            ? stableGameIdentities->first
            : std::wstring{L"widget:game-launcher.experiences.open"};
        auto* target = moveToFirst ? firstGame.Get() : focusAlternate.Get();
        Require(SUCCEEDED(target->SetFocus()),
            "Production UIA focus input failed during Launcher stress.");
        Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
            ComPtr<IUIAutomationElement> focused;
            return SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) &&
                focused && StringProperty(
                    focused.Get(), UIA_AutomationIdPropertyId) == expected;
        }), "Production actionable focus did not reach the expected identity.");
        moveToFirst = !moveToFirst;
        Sleep(75);
    }
    std::optional<ProjectionRecord> stressRecord;
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto records = ProjectionRecords(running.installation.LogPath());
        if (records.empty()) return false;
        const auto found = std::find_if(records.rbegin(), records.rend(),
            [](const auto& item) { return item.inputSamples >= 2; });
        if (found == records.rend()) return false;
        stressRecord = *found;
        return true;
    }), "The production host omitted Launcher input timing samples.");
    Require(stressRecord->inputP95Milliseconds < 50.0,
        "The production Launcher input-to-focus p95 exceeded 50 ms.");
    Require(stressRecord->degraded >= 2 && stressRecord->effect == "immediate",
        "The production Launcher did not deterministically degrade effects before focus semantics.");
    const auto log = ReadUtf8(running.installation.LogPath());
    Require(log.find("launcher_projection_render_failed") == std::string::npos &&
            log.find("launcher_projection_bitmap_unavailable") == std::string::npos &&
            log.find("launcher_background_bitmap") == std::string::npos,
        "The 60-second Launcher motion window exposed an incomplete frame.");

    SendKey(running.window, VK_ESCAPE);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> focused;
        return SUCCEEDED(automation->GetFocusedElement(focused.GetAddressOf())) &&
            focused && StringProperty(focused.Get(), UIA_AutomationIdPropertyId) ==
                L"tray:tray.game-launcher";
    }), "Game Launcher Back route did not return focus to its tray identity.");
    running.Stop();
    std::cout << "LauncherExperienceHostTests: deterministic live preset adoption and "
                 "60-second motion budget passed (p95=" <<
        stressRecord->inputP95Milliseconds << " ms, degraded=" <<
        stressRecord->degraded << ")\n";
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

void RunInstalledSelection(
    IUIAutomation* automation,
    const fs::path& installationPath,
    const fs::path& fixtureBridge) {
    RunningHost running(installationPath, fixtureBridge, "selection\n");
    auto tray = WaitForElement(
        automation, running.window, L"tray:tray.game-launcher");
    RequireInsideWindow(tray.Get(), running.window);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("preset=cover-wall") != std::string::npos &&
            log.find("selection=dev.example.production@1.0.0") != std::string::npos &&
            log.find("background-focus=pack:") != std::string::npos;
    }), "Installed exact Launcher Experience did not adopt recipe, style, and sealed asset.");

    running.installation.WriteSelection("1.0.0", true);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("selection=dev.example.production@1.0.0 appearance=global") !=
            std::string::npos;
    }), "Use global appearance did not retain the exact installed selection.");

    running.installation.WriteSelection("2.0.0", false);
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("preset=carousel") != std::string::npos &&
            log.find("selection=dev.example.production@2.0.0 appearance=launcher") !=
                std::string::npos;
    }), "Valid installed replacement was not published atomically.");

    running.installation.RewriteExperienceStyle(L"2.0.0");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find(
            "retained-diagnostic=The selected immutable Launcher Experience content digest changed.") !=
            std::string::npos;
    }), "A same-version content rewrite did not retain the immutable last-good digest.");
    const auto tamperRetained = ProjectionRecords(running.installation.LogPath());
    Require(!tamperRetained.empty() && tamperRetained.back().preset == "carousel",
        "A same-version content rewrite replaced the last-good production presentation.");

    std::error_code removeError;
    fs::remove_all(running.installation.ExperienceVersion(L"2.0.0"), removeError);
    Require(!removeError, "Could not remove the selected fixture version.");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("retained-diagnostic=") != std::string::npos &&
            log.find("Applied Launcher Experience revision") != std::string::npos;
    }), "Removed selected version did not retain last-good with one bounded diagnostic.");
    const auto retained = ProjectionRecords(running.installation.LogPath());
    Require(!retained.empty() && retained.back().preset == "carousel",
        "Invalid reload replaced the last-good production presentation.");

    running.installation.WriteBuiltInRecovery();
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("selection=org.gbar.builtin.hero-rail@1.0.0") !=
            std::string::npos;
    }), "Built-in controller-complete recovery selection did not activate.");
    running.Stop();
    std::cout << "LauncherExperienceHostTests: installed exact selection, global "
                 "appearance, atomic replacement, tamper/removal last-good reload, "
                 "and recovery passed\n";
}

void RunSafeStart(
    IUIAutomation* automation,
    const fs::path& installationPath,
    const fs::path& fixtureBridge) {
    RunningHost running(
        installationPath, fixtureBridge, "selection\n", L"safe-start");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find("selection=dev.example.production@1.0.0") !=
                std::string::npos &&
            log.find("safe-start=true") != std::string::npos &&
            log.find("background=fallback") != std::string::npos;
    }), "The production safe-start activation did not bypass custom pack presentation.");

    SendKey(running.window, VK_ESCAPE);
    auto tray = WaitForElement(
        automation, running.window, L"tray:tray.game-launcher");
    FocusAndActivate(tray.Get(), running.window, "tray:tray.game-launcher");
    Require(WaitUntil(kStepTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(running.installation.LogPath());
        return log.find(
            "selection=dev.example.production@1.0.0 appearance=launcher safe-start=false") !=
                std::string::npos &&
            log.find("background-focus=pack:") != std::string::npos;
    }), "Safe start did not expire after exactly one Game Launcher activation.");
    const auto settings = ReadUtf8(
        running.installation.SettingsRoot() / L"platform-settings.json");
    Require(settings.find("\"selectedVersion\":\"1.0.0\"") != std::string::npos &&
            settings.find("\"selectedId\":\"dev.example.production\"") !=
                std::string::npos,
        "Safe start mutated the trusted exact Launcher Experience selection.");
    running.Stop();
    std::cout << "LauncherExperienceHostTests: one-activation safe start and "
                 "unchanged exact selection passed\n";
}

void Run(const fs::path& installationPath, const fs::path& fixtureBridge) {
    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
        "Windows UI Automation client is unavailable.");
    RunInstalledSelection(automation.Get(), installationPath, fixtureBridge);
    RunSafeStart(automation.Get(), installationPath, fixtureBridge);
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
