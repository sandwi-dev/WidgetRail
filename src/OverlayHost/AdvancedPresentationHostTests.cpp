#include "OverlayHostTestSupport.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using namespace gba::host_testing;

namespace {

constexpr DWORD kTimeoutMilliseconds = 30'000;
constexpr wchar_t kNonce[] =
    L"2132132132132132132132132132132132132132132132132132132132132132";
constexpr char kNonceUtf8[] =
    "2132132132132132132132132132132132132132132132132132132132132132";

struct Arguments final {
    fs::path installation;
    fs::path communityFixture;
    fs::path candidatePackage;
    fs::path fixtureBridge;
};

Arguments ParseArguments(const int argc, wchar_t** argv) {
    Arguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument(argv[index]);
        if ((argument == L"--installation" ||
             argument == L"--community-fixture" ||
             argument == L"--candidate-package" ||
             argument == L"--fixture-bridge") && index + 1 < argc) {
            if (argument == L"--installation") result.installation = argv[++index];
            else if (argument == L"--community-fixture")
                result.communityFixture = argv[++index];
            else if (argument == L"--candidate-package")
                result.candidatePackage = argv[++index];
            else result.fixtureBridge = argv[++index];
        } else {
            Fail("Usage: AdvancedPresentationHostTests --installation <dir> "
                 "--community-fixture <exe> --candidate-package <gbarwidget> "
                 "--fixture-bridge <exe>");
        }
    }
    Require(!result.installation.empty() && !result.communityFixture.empty() &&
            !result.candidatePackage.empty() && !result.fixtureBridge.empty(),
        "All advanced presentation host fixture paths are required.");
    return result;
}

void InstallPackages(
    const fs::path& executable,
    const fs::path& catalogRoot,
    const fs::path& candidatePackage) {
    Require(fs::is_regular_file(executable),
        "--community-fixture does not name the published installer");
    Require(fs::is_regular_file(candidatePackage),
        "--candidate-package does not name the supported export");
    std::wstring command = QuoteArgument(executable.wstring()) + L" --install " +
        QuoteArgument(catalogRoot.wstring()) + L" --candidate-package " +
        QuoteArgument(candidatePackage.wstring());
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    Require(CreateProcessW(
                executable.c_str(), mutableCommand.data(), nullptr, nullptr,
                FALSE, CREATE_NO_WINDOW, nullptr,
                executable.parent_path().c_str(), &startup, &process),
        Win32Error("CreateProcessW(advanced presentation installer)"));
    Handle processHandle(process.hProcess);
    Handle threadHandle(process.hThread);
    Require(WaitForSingleObject(
                processHandle.Get(), kTimeoutMilliseconds) == WAIT_OBJECT_0,
        "Advanced presentation fixture installation timed out.");
    DWORD exitCode{};
    Require(GetExitCodeProcess(processHandle.Get(), &exitCode) && exitCode == 0,
        "Advanced presentation fixture installation failed.");
}

class TemporaryInstallation final {
public:
    TemporaryInstallation(
        const fs::path& source,
        const fs::path& communityFixture,
        const fs::path& candidatePackage,
        const fs::path& fixtureBridge) {
        Require(fs::is_regular_file(source / L"OverlayHost.exe"),
            "--installation does not contain OverlayHost.exe");
        wchar_t temporaryRoot[MAX_PATH + 1]{};
        const DWORD length = GetTempPathW(MAX_PATH, temporaryRoot);
        Require(length > 0 && length <= MAX_PATH, Win32Error("GetTempPathW"));
        GUID guid{};
        Require(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid failed");
        wchar_t guidText[64]{};
        Require(StringFromGUID2(guid, guidText, 64) > 0, "StringFromGUID2 failed");
        root_ = fs::path(temporaryRoot) /
            (L"wrail-advanced-presentation-" + std::wstring(guidText));
        fs::create_directories(root_);
        fs::copy_file(source / L"OverlayHost.exe", root_ / L"OverlayHost.exe");
        fs::copy_file(
            source / L"widget-catalog.json", root_ / L"widget-catalog.json");
        fs::copy(source / L"runtime", root_ / L"runtime",
            fs::copy_options::recursive | fs::copy_options::copy_symlinks);
        Require(fs::is_regular_file(fixtureBridge),
            "--fixture-bridge does not name the seeded broker fixture");
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
        catalogRoot_ = localAppData_ / L"WidgetRail" / L"widgets";
        fs::create_directories(catalogRoot_);
        readyPath_ = root_ / L"host-ready.txt";
        InstallPackages(communityFixture, catalogRoot_, candidatePackage);
        WriteUtf8(
            bridgeRoot / L"launcher-experience-provider-fixture.txt",
            "adoption\n" + WideToUtf8(
                (localAppData_ / L"WidgetRail").wstring()) + "\n");
    }

    ~TemporaryInstallation() {
        if (std::uncaught_exceptions() != 0) {
            const auto log = ReadUtf8(LogPath());
            if (!log.empty())
                std::cerr << "AdvancedPresentationHostTests host diagnostic:\n" <<
                    log << '\n';
            const auto backend = ReadUtf8(BackendDiagnosticPath());
            if (!backend.empty())
                std::cerr << "AdvancedPresentationHostTests backend diagnostic:\n" <<
                    backend << '\n';
            const auto fixtureError = ReadUtf8(
                localAppData_ / L"WidgetRail" /
                L"launcher-experience-fixture-error.txt");
            if (!fixtureError.empty())
                std::cerr << "AdvancedPresentationHostTests bridge diagnostic:\n" <<
                    fixtureError << '\n';
        }
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }

    const fs::path& Root() const noexcept { return root_; }
    const fs::path& LocalAppData() const noexcept { return localAppData_; }
    const fs::path& CatalogRoot() const noexcept { return catalogRoot_; }
    const fs::path& ReadyPath() const noexcept { return readyPath_; }
    fs::path LogPath() const {
        return localAppData_ / L"WidgetRail" / L"overlay.log";
    }
    fs::path BackendDiagnosticPath() const {
        return localAppData_ / L"WidgetRail" /
            L"launcher-experience-backend.txt";
    }

private:
    fs::path root_;
    fs::path localAppData_;
    fs::path catalogRoot_;
    fs::path readyPath_;
};

ComPtr<IUIAutomationElement> FindByAutomationId(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view automationId) {
    if (!automation || !root) return {};
    VARIANT value{};
    value.vt = VT_BSTR;
    value.bstrVal = SysAllocStringLen(
        automationId.data(), static_cast<UINT>(automationId.size()));
    Require(value.bstrVal != nullptr, "Could not allocate UIA identity.");
    ComPtr<IUIAutomationCondition> condition;
    const auto conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, value, condition.GetAddressOf());
    VariantClear(&value);
    Require(SUCCEEDED(conditionResult) && condition,
        "Could not create UIA identity condition.");
    ComPtr<IUIAutomationElement> result;
    (void)root->FindFirst(
        TreeScope_Subtree, condition.Get(), result.GetAddressOf());
    return result;
}

std::wstring AutomationIdOf(IUIAutomationElement* element) {
    BSTR value{};
    if (!element || FAILED(element->get_CurrentAutomationId(&value)) || !value)
        return {};
    std::wstring result(value, SysStringLen(value));
    SysFreeString(value);
    return result;
}

ComPtr<IUIAutomationElement> FindByAutomationIdPrefix(
    IUIAutomation* automation,
    IUIAutomationElement* root,
    const std::wstring_view prefix) {
    if (!automation || !root) return {};
    ComPtr<IUIAutomationCondition> condition;
    Require(SUCCEEDED(automation->CreateTrueCondition(condition.GetAddressOf())) &&
            condition,
        "Could not create the UIA descendant condition.");
    ComPtr<IUIAutomationElementArray> descendants;
    if (FAILED(root->FindAll(
            TreeScope_Subtree, condition.Get(), descendants.GetAddressOf())) ||
        !descendants) return {};
    int count{};
    if (FAILED(descendants->get_Length(&count))) return {};
    for (int index = 0; index < count; ++index) {
        ComPtr<IUIAutomationElement> candidate;
        if (FAILED(descendants->GetElement(index, candidate.GetAddressOf())) ||
            !candidate) continue;
        if (std::wstring_view{AutomationIdOf(candidate.Get())}.starts_with(prefix))
            return candidate;
    }
    return {};
}

ComPtr<IUIAutomationElement> WaitForElement(
    IUIAutomation* automation,
    const HWND window,
    const std::wstring_view automationId) {
    ComPtr<IUIAutomationElement> result;
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> root;
        if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf())) ||
            !root) return false;
        result = FindByAutomationId(automation, root.Get(), automationId);
        return static_cast<bool>(result);
    }), "Expected UIA element was absent: " + WideToUtf8(automationId));
    return result;
}

std::wstring NameOf(IUIAutomationElement* element) {
    BSTR value{};
    if (!element || FAILED(element->get_CurrentName(&value)) || !value) return {};
    std::wstring result(value, SysStringLen(value));
    SysFreeString(value);
    return result;
}

void FocusAndActivate(
    IUIAutomationElement* element,
    const HWND window,
    const std::string_view identity) {
    Require(element && SUCCEEDED(element->SetFocus()),
        "UIA focus failed for " + std::string(identity));
    ComPtr<IUIAutomationInvokePattern> invoke;
    Require(SUCCEEDED(element->GetCurrentPatternAs(
                UIA_InvokePatternId,
                IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) && invoke,
        "UIA element omitted InvokePattern: " + std::string(identity));
    Require(SUCCEEDED(invoke->Invoke()),
        "UIA element rejected InvokePattern: " + std::string(identity));
    (void)window;
}

void ExercisePackage(
    IUIAutomation* automation,
    const HWND window,
    const fs::path& logPath,
    const std::wstring_view widgetId,
    const std::wstring_view itemId,
    const std::wstring_view titleId,
    const std::string_view preset) {
    const auto trayId = L"tray:tray." + std::wstring(widgetId);
    auto tray = WaitForElement(automation, window, trayId);
    FocusAndActivate(tray.Get(), window, WideToUtf8(trayId));
    const auto adoption = "Launcher Experience projection adopted widget=" +
        WideToUtf8(widgetId) + " preset=" + std::string(preset);
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        return ReadUtf8(logPath).find(adoption) != std::string::npos;
    }), "Installed Community package was not adopted through the ordinary host: " +
        WideToUtf8(widgetId));

    const auto widgetItemId = L"widget:" + std::wstring(itemId);
    auto item = WaitForElement(automation, window, widgetItemId);
    FocusAndActivate(item.Get(), window, WideToUtf8(widgetItemId));
    const auto widgetTitleId = L"widget:" + std::wstring(titleId);
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        auto title = WaitForElement(automation, window, widgetTitleId);
        return NameOf(title.Get()) == L"Activated";
    }), "Exact authored action did not update the installed Community semantics.");

    SendKey(window, VK_ESCAPE);
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> root;
        return SUCCEEDED(automation->ElementFromHandle(
                   window, root.GetAddressOf())) && root &&
            static_cast<bool>(FindByAutomationId(
                automation, root.Get(), trayId));
    }), "Back did not return to the shared dashboard semantics.");
}

void ExerciseExportedCandidate(
    IUIAutomation* automation,
    const HWND window,
    const fs::path& logPath,
    const fs::path& backendDiagnosticPath) {
    constexpr std::wstring_view widgetId =
        L"widgetrail.community.reference.game-launcher";
    const auto trayId = L"tray:tray." + std::wstring(widgetId);
    auto tray = WaitForElement(automation, window, trayId);
    FocusAndActivate(tray.Get(), window, WideToUtf8(trayId));
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        return ReadUtf8(backendDiagnosticPath).find("items=2") !=
            std::string::npos;
    }), "The exact exported candidate did not receive the seeded app-library page.");
    const auto adoption =
        "Launcher Experience projection adopted widget=" + WideToUtf8(widgetId) +
        " preset=hero-rail";
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(logPath);
        return log.find(adoption) != std::string::npos &&
            log.find("advanced_presentation_invalid") == std::string::npos;
    }), "The exact exported candidate did not adopt the generic presentation.");

    ComPtr<IUIAutomationElement> game;
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        ComPtr<IUIAutomationElement> root;
        if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf())) ||
            !root) return false;
        game = FindByAutomationIdPrefix(
            automation, root.Get(), L"widget:game-launcher.item.grid.game.");
        return static_cast<bool>(game);
    }), "The exported candidate omitted its stable collection/UIA item.");
    const auto gameIdentity = AutomationIdOf(game.Get());
    Require(!gameIdentity.empty(),
        "The exported candidate collection item omitted semantic identity.");

    FocusAndActivate(game.Get(), window, WideToUtf8(gameIdentity));
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        if (ReadUtf8(backendDiagnosticPath).find("launch app=") ==
            std::string::npos) return false;
        ComPtr<IUIAutomationElement> focused;
        if (FAILED(automation->GetFocusedElement(focused.GetAddressOf())) || !focused)
            return false;
        ComPtr<IUIAutomationElement> root;
        if (FAILED(automation->ElementFromHandle(window, root.GetAddressOf())) ||
            !root) return false;
        const auto current = FindByAutomationId(automation, root.Get(), gameIdentity);
        return current && AutomationIdOf(focused.Get()) == gameIdentity;
    }), "The exported candidate did not preserve exact action, focus, collection, "
        "and UIA semantics.");

    SendKey(window, VK_ESCAPE);
    (void)WaitForElement(automation, window, trayId);
}

void ExercisePackagedCandidate(
    IUIAutomation* automation,
    const HWND window,
    const fs::path& logPath) {
    constexpr std::wstring_view trayId = L"tray:tray.game-launcher";
    auto tray = WaitForElement(automation, window, trayId);
    FocusAndActivate(tray.Get(), window, WideToUtf8(trayId));
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        const auto log = ReadUtf8(logPath);
        return log.find(
                   "Launcher Experience projection adopted widget=game-launcher "
                   "preset=hero-rail") != std::string::npos &&
            log.find("widget=game-launcher reason=advanced_presentation_invalid") ==
                std::string::npos;
    }), "The packaged Game Launcher fell back instead of adopting its declaration.");
    SendKey(window, VK_ESCAPE);
    (void)WaitForElement(automation, window, trayId);
}

void Run(const Arguments& arguments) {
    TemporaryInstallation installation(
        arguments.installation, arguments.communityFixture,
        arguments.candidatePackage, arguments.fixtureBridge);
    const std::wstring hostArguments =
        L"--show --process-profile advanced-presentation-dlv213 "
        L"--development-catalog-root " +
            QuoteArgument(installation.CatalogRoot().wstring()) +
        L" --development-ready-path " +
            QuoteArgument(installation.ReadyPath().wstring()) +
        L" --development-ready-nonce " + kNonce +
        L" --development-widget-id settings "
        L"--development-widget-instance settings.default";
    HostProcess host(
        installation.Root(), installation.LocalAppData(), hostArguments);
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        return ReadUtf8(installation.ReadyPath()).find(kNonceUtf8) !=
            std::string::npos;
    }), "Production host did not publish authenticated readiness; log=" +
        ReadUtf8(installation.LogPath()));
    HWND window{};
    Require(WaitUntil(kTimeoutMilliseconds, [&] {
        window = LocateHostWindow(host.Id());
        return window && IsWindowVisible(window);
    }), "Production host did not create a visible HWND.");

    ComPtr<IUIAutomation> automation;
    Require(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(automation.GetAddressOf()))) && automation,
        "Windows UI Automation client is unavailable.");
    ExerciseExportedCandidate(
        automation.Get(), window, installation.LogPath(),
        installation.BackendDiagnosticPath());
    ExercisePackage(
        automation.Get(), window, installation.LogPath(),
        L"net.unrelated.bravo.deck", L"q7.item", L"q7.title", "compact-grid");
    ExercisePackagedCandidate(
        automation.Get(), window, installation.LogPath());
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(apartment)) {
        std::cerr << "AdvancedPresentationHostTests failed: COM initialization failed\n";
        return 1;
    }
    try {
        Run(ParseArguments(argc, argv));
        CoUninitialize();
        std::cout << "AdvancedPresentationHostTests passed: 2 installed Community packages\n";
        return 0;
    } catch (const std::exception& error) {
        CoUninitialize();
        std::cerr << "AdvancedPresentationHostTests failed: " << error.what() << '\n';
        return 1;
    }
}
