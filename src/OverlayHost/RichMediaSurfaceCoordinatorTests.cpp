#include "OverlayCompositionSurface.h"
#include "RichMediaSurfaceCoordinator.h"

#include <Windows.h>
#include <d2d1_1.h>
#include <psapi.h>
#include <TlHelp32.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <numeric>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

using namespace std::chrono_literals;
using Microsoft::WRL::ComPtr;

namespace widgetrail::richmedia {

class RichMediaSurfaceCoordinatorTestPeer final {
public:
    static void Fault(RichMediaSurfaceCoordinator& coordinator,
                      const std::wstring_view code) {
        coordinator.Fault(code, E_FAIL);
    }
    static bool IsAllowedNavigation(const std::wstring_view uri) {
        return RichMediaSurfaceCoordinator::IsAllowedNavigation(uri);
    }
    static bool IsAllowedMessageSource(const std::wstring_view source,
                                       const std::wstring_view page) {
        return RichMediaSurfaceCoordinator::IsAllowedMessageSource(source, page);
    }
    static bool IsAllowedFrameResource(
        const std::wstring_view uri,
        const std::vector<std::wstring>& allowedOrigins) {
        return RichMediaSurfaceCoordinator::IsAllowedFrameResource(
            uri, allowedOrigins);
    }
    static bool IsPlaybackCommandCorrelated(
        const std::uint64_t commandSequence, const std::wstring_view mediaKey,
        const std::optional<std::uint64_t> pendingSequence,
        const std::wstring_view pendingMediaKey) {
        return RichMediaSurfaceCoordinator::IsPlaybackCommandCorrelated(
            commandSequence, mediaKey, pendingSequence, pendingMediaKey);
    }
    static bool IsValidAdapterConfiguration(const Configuration& configuration) {
        return RichMediaSurfaceCoordinator::IsValidAdapterConfiguration(configuration);
    }
    static bool SurfaceLocalPoint(HWND ownerWindow, const RECT& bounds,
                                  const UINT message, const LPARAM lParam,
                                  POINT& point) {
        return RichMediaSurfaceCoordinator::SurfaceLocalPoint(
            ownerWindow, bounds, message, lParam, point);
    }
    static bool FocusedActionPoint(const ActionBounds& actionBounds,
                                   const RECT& surfaceBounds, POINT& point) {
        return RichMediaSurfaceCoordinator::FocusedActionPoint(
            actionBounds, surfaceBounds, point);
    }
    static bool FinalConfigurationReleased(
        const RichMediaSurfaceCoordinator& coordinator) {
        const auto& configuration = coordinator.configuration_;
        return !configuration.compositionTarget && !configuration.diagnostic &&
            !configuration.invalidate && !configuration.playbackEvent &&
            !configuration.setPresentationVisible;
    }
    static bool BrowserExitObserverActive(
        const RichMediaSurfaceCoordinator& coordinator) {
        return coordinator.BrowserProcessExitObserverActive();
    }
    static bool BrowserExitObserved(
        const RichMediaSurfaceCoordinator& coordinator) {
        return coordinator.BrowserProcessExitObserved();
    }
    static bool EnvironmentObjectsReleased(
        const RichMediaSurfaceCoordinator& coordinator) {
        return !coordinator.environment_ && !coordinator.environment5_ &&
            !coordinator.environmentSignal_ &&
            !coordinator.browserProcessExitedToken_.value;
    }
    static void RecordLiveUnexpectedBrowserExit(
        RichMediaSurfaceCoordinator& coordinator, const DWORD processId) {
        RichMediaSurfaceCoordinator::RecordBrowserProcessExit(
            coordinator.environmentSignal_, processId);
    }
    static void MarkEnvironmentFaulted(
        RichMediaSurfaceCoordinator& coordinator) {
        coordinator.environmentFaulted_ = true;
    }
    static bool PublicGeometryRetainsActivationAuthority(
        const RECT before, const RECT after, const bool pageReady) {
        RichMediaSurfaceCoordinator coordinator;
        coordinator.configuration_.bounds = before;
        coordinator.configuration_.rasterScale = 1.5;
        coordinator.configuration_.resources.push_back(
            {L"media/adapter.html", L"text/html", {1}});
        coordinator.pageReady_ = pageReady;
        coordinator.state_.focusedActionBounds = {0, 0, 320, 180};
        coordinator.state_.focusedActionBoundsCurrent = true;
        (void)coordinator.UpdateGeometry(after, 1.5);
        return coordinator.state_.focusedActionBoundsCurrent &&
            coordinator.state_.focusedActionBounds.width == after.right - after.left &&
            coordinator.state_.focusedActionBounds.height == after.bottom - after.top;
    }
};

} // namespace widgetrail::richmedia

namespace {

void Require(const bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

template <typename Predicate>
bool PumpUntil(Predicate predicate, const std::chrono::milliseconds timeout) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < deadline) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        if (predicate()) return true;
        MsgWaitForMultipleObjectsEx(0, nullptr, 10, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }
    return predicate();
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    return DefWindowProcW(window, message, wParam, lParam);
}

class Fixture final {
public:
    Fixture(const unsigned int ordinal, const bool visible,
            std::filesystem::path profileRoot = {},
            std::wstring adapterIdentity = {})
        : profileRoot_(profileRoot.empty()
              ? std::filesystem::temp_directory_path() /
                    (L"wrail-rich-media-proof-root-" +
                     std::to_wstring(GetCurrentProcessId()) + L"-" +
                     std::to_wstring(ordinal))
              : std::move(profileRoot)),
          adapterIdentity_(std::move(adapterIdentity)) {
        WNDCLASSW windowClass{};
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpfnWndProc = WindowProc;
        windowClass.lpszClassName = L"WidgetRail.RichMediaProofTests";
        RegisterClassW(&windowClass);
        window_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP,
            windowClass.lpszClassName, L"", WS_POPUP,
            0, 0, 800, 520, nullptr, nullptr, windowClass.hInstance, nullptr);
        Require(window_ != nullptr, "proof owner HWND creation failed");
        HRESULT result = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            factory_.ReleaseAndGetAddressOf());
        Require(SUCCEEDED(result), "D2D factory creation failed");
        std::wstring error;
        Require(composition_.Initialize(window_, factory_.Get(), error),
                "composition owner initialization failed");
        Require(SUCCEEDED(OpenSession(visible, false)),
                "coordinator initialization submission failed");
    }

    ~Fixture() {
        if (sessionOpen_) (void)CloseSession();
        coordinator_.Shutdown();
        composition_.Reset();
        factory_.Reset();
        if (window_) DestroyWindow(window_);
    }

    HRESULT OpenSession(const bool visible, const bool retry) {
        ComPtr<IUnknown> target;
        HRESULT result = composition_.CreateExternalContentTarget(&target);
        if (FAILED(result)) return result;
        auto configuration = ConfigurationFor(target, visible);
        result = retry ? coordinator_.Retry(std::move(configuration))
                       : coordinator_.Initialize(std::move(configuration));
        if (FAILED(result)) return result;
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        result = composition_.CommitExternalContentPresentation(
            {0, 0, 800, 520}, false, timing);
        if (SUCCEEDED(result)) sessionOpen_ = true;
        return result;
    }

    HRESULT CloseSession() {
        if (!sessionOpen_) return S_FALSE;
        coordinator_.BeginSessionTeardown();
        releasedBeforeDetach_ =
            widgetrail::richmedia::RichMediaSurfaceCoordinatorTestPeer::
                FinalConfigurationReleased(coordinator_);
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = composition_.DetachExternalContentTarget(timing);
        detachWaited_ = timing.waitedForCompletion;
        coordinator_.CompleteSessionTeardown();
        sessionOpen_ = false;
        return result;
    }

    widgetrail::richmedia::RichMediaSurfaceCoordinator& coordinator() {
        return coordinator_;
    }
    HRESULT Retry() {
        const HRESULT result = CloseSession();
        if (FAILED(result) || !detachWaited_) return FAILED(result) ? result : E_FAIL;
        return OpenSession(true, true);
    }
    HRESULT Reopen(const bool visible = true) {
        const HRESULT result = CloseSession();
        if (FAILED(result) || !detachWaited_) return FAILED(result) ? result : E_FAIL;
        return OpenSession(visible, false);
    }
    void SelectAdapter(std::wstring adapterIdentity) {
        Require(!sessionOpen_, "adapter identity changed while a session was active");
        adapterIdentity_ = std::move(adapterIdentity);
    }
    const std::filesystem::path& profileRoot() const { return profileRoot_; }
    bool presentationVisible() const { return presentationVisible_; }
    bool presentationCommitFailed() const { return presentationCommitFailed_; }
    HWND window() const { return window_; }
    bool sawDiagnostic(const std::wstring_view text) const {
        return std::any_of(diagnostics_.begin(), diagnostics_.end(),
            [&](const std::wstring& value) { return value.find(text) != std::wstring::npos; });
    }
    bool finalConfigurationReleased() const {
        return widgetrail::richmedia::RichMediaSurfaceCoordinatorTestPeer::
            FinalConfigurationReleased(coordinator_);
    }
    bool finalConfigurationReleasedBeforeDetach() const {
        return releasedBeforeDetach_;
    }
    bool finalDetachSucceededAndWaited() const {
        return detachWaited_;
    }

private:
    widgetrail::richmedia::Configuration ConfigurationFor(
        Microsoft::WRL::ComPtr<IUnknown> target, const bool visible) {
        widgetrail::richmedia::Configuration configuration;
        configuration.ownerWindow = window_;
        configuration.compositionTarget = std::move(target);
        configuration.bounds = {0, 0, 800, 520};
        configuration.rasterScale = 1.0;
        configuration.initiallyVisible = visible;
        configuration.profileRootDirectory = profileRoot_.wstring();
        if (!adapterIdentity_.empty()) {
            configuration.origin =
                L"https://wrail-media-" + adapterIdentity_ + L".invalid";
            configuration.entryAsset = L"adapter/index.html";
            const std::string firstId = adapterIdentity_ == L"aurora-adapter"
                ? "aurora.primary" : "cedar.primary";
            const std::string secondId = adapterIdentity_ == L"aurora-adapter"
                ? "aurora.secondary" : "cedar.secondary";
            const std::string page = R"HTML(<!doctype html><meta charset="utf-8">
<button id=")HTML" + firstId + R"HTML(">First</button><button id=")HTML" +
                secondId + R"HTML(">Second</button><script>
let a={},seq=0,focus=')HTML" + firstId + R"HTML(';
function emit(type,id){const r=document.activeElement.getBoundingClientRect();chrome.webview.postMessage({type,...a,eventSequence:++seq,commandId:id,focus,playing:false,bounds:{x:r.x,y:r.y,width:r.width,height:r.height}});}
chrome.webview.addEventListener('message',e=>{const m=e.data;if(m.command==='initialize'){a={environmentGeneration:m.environmentGeneration,surfaceGeneration:m.surfaceGeneration,sessionGeneration:m.sessionGeneration,controllerGeneration:m.controllerGeneration,documentGeneration:m.documentGeneration};document.getElementById(focus).focus();emit('ready',m.commandId);return;}if(Object.keys(a).some(k=>m[k]!==a[k]))return;if(m.command==='next'||m.command==='previous'){focus=m.command==='next'?')HTML" + secondId + R"HTML(':')HTML" + firstId + R"HTML(';document.getElementById(focus).focus();emit('focus',m.commandId);}});
</script>)HTML";
            configuration.resources.push_back({
                configuration.entryAsset, L"text/html",
                std::vector<std::uint8_t>(page.begin(), page.end())});
        }
        configuration.diagnostic = [this](const std::wstring_view message) {
            diagnostics_.emplace_back(message);
            std::wcout << L"diagnostic " << message << L'\n' << std::flush;
        };
        configuration.setPresentationVisible = [this](const bool shown) {
            presentationVisible_ = shown;
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            const HRESULT result = composition_.CommitExternalContentPresentation(
                {0, 0, 800, 520}, shown, timing);
            if (FAILED(result)) presentationCommitFailed_ = true;
        };
        return configuration;
    }
    HWND window_{};
    ComPtr<ID2D1Factory1> factory_;
    widgetrail::OverlayCompositionSurface composition_;
    widgetrail::richmedia::RichMediaSurfaceCoordinator coordinator_;
    std::filesystem::path profileRoot_;
    std::wstring adapterIdentity_;
    bool sessionOpen_{};
    bool presentationVisible_{};
    bool presentationCommitFailed_{};
    bool releasedBeforeDetach_{};
    bool detachWaited_{};
    std::vector<std::wstring> diagnostics_;
};

void RunContractCases() {
    using namespace widgetrail::richmedia;
    State state;
    state.authority = {2, 3, 7, 11, 13, 3};
    State next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "exact current correlated event was rejected");
    Require(next.focusedElement == L"seek" && next.authority.eventSequence == 4 &&
            next.lastAcknowledgedCommandId == 17 &&
            next.focusedActionBoundsCurrent && next.focusedActionBounds.x == 100.0,
            "exact event authority was not retained");
    next = state;
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":6,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale session was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":2,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale surface was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":10,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale controller was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":12,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale document was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":18,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "wrong command correlation was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60},"script":"bad"})",
        state.authority, 3, 17, next), "unknown page field was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"seek","playing":false,"bounds":{"x":-1,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "invalid focused bounds were admitted");
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"aurora.primary","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "valid document-local focus identity was rejected");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"invalid focus","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "invalid focus identity grammar was admitted");
    const std::wstring oversizedFocusJson =
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":")" +
        std::wstring(129, L'a') +
        LR"(","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})";
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        oversizedFocusJson, state.authority, 3, 17, next),
        "oversized document-local focus identity was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"script","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"aurora.primary","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "unknown event kind was admitted");
    next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"focus":"aurora.primary","playing":true,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"aurora-tone-2","playbackState":"playing","positionSeconds":12,"durationSeconds":60,"volume":0.72})",
        state.authority, 3, 17, next),
        "typed provider-neutral playback event was rejected");
    Require(next.mediaKey == L"aurora-tone-2" && next.playing &&
            next.positionSeconds == 12.0 && next.durationSeconds == 60.0,
            "typed playback state was not retained exactly");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":18,"focus":"cedar.primary","playing":true,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"cedar-tone","playbackState":"playing","positionSeconds":12,"durationSeconds":60,"volume":0.72})",
        state.authority, 3, 17, next),
        "uncorrelated typed playback event was admitted");
    Require(RichMediaSurfaceCoordinatorTestPeer::
                PublicGeometryRetainsActivationAuthority(
                    {10, 20, 650, 380}, {10, 20, 650, 380}, true) &&
            RichMediaSurfaceCoordinatorTestPeer::
                PublicGeometryRetainsActivationAuthority(
                    {10, 20, 650, 380}, {30, 40, 790, 465}, true),
            "public MediaViewport geometry revoked exact viewport activation authority");
    const auto command = RichMediaSurfaceCoordinator::CommandJson(
        Command::Activate, {5, 2, 9, 12, 14, 6}, 19);
    Require(command == LR"({"command":"arm-activate","environmentGeneration":5,"surfaceGeneration":2,"sessionGeneration":9,"controllerGeneration":12,"documentGeneration":14,"commandId":19})",
            "private command encoding drifted");
    Require(RichMediaSurfaceCoordinatorTestPeer::IsAllowedNavigation(
                L"https://wrail-rich-media.invalid/index.html"),
            "exact embedded navigation was denied");
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsAllowedNavigation(
                L"https://wrail-rich-media.invalid/frame.html"),
            "non-embedded frame navigation was admitted");
    Require(RichMediaSurfaceCoordinatorTestPeer::IsAllowedMessageSource(
                L"https://wrail-media-aurora.invalid/adapter/index.html",
                L"https://wrail-media-aurora.invalid/adapter/index.html") &&
            !RichMediaSurfaceCoordinatorTestPeer::IsAllowedMessageSource(
                L"https://wrong-origin.invalid/adapter/index.html",
                L"https://wrail-media-aurora.invalid/adapter/index.html"),
            "exact document message-origin admission drifted");
    const std::vector<std::wstring> allowedFrameOrigins{
        L"https://frames.aurora.invalid"};
    Require(RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                L"https://frames.aurora.invalid/embed/index.html",
                allowedFrameOrigins) &&
            RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                L"https://frames.aurora.invalid/media/tone.wav?revision=2",
                allowedFrameOrigins),
            "declared exact-origin frame traffic would be replaced by the local denial response");
    for (const auto uri : {
            L"https://frames.aurora.invalid.evil.example/embed/index.html",
            L"https://frames.aurora.invalid@evil.example/embed/index.html",
            L"https://cedar.invalid/embed/index.html",
            L"http://frames.aurora.invalid/embed/index.html"}) {
        Require(!RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                    uri, allowedFrameOrigins),
                "undeclared or confused frame origin was admitted");
    }
    for (const std::vector<std::wstring> invalidOrigins : {
            std::vector<std::wstring>{L"https://frames.aurora.invalid/"},
            std::vector<std::wstring>{L"HTTPS://frames.aurora.invalid"},
            std::vector<std::wstring>{L"https://user@frames.aurora.invalid"},
            std::vector<std::wstring>{L"https://*.aurora.invalid"},
            std::vector<std::wstring>{L"https://frames.aurora.invalid:443"}}) {
        Require(!RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                    L"https://frames.aurora.invalid/embed/index.html",
                    invalidOrigins),
                "non-canonical declared frame origin was admitted");
    }
    Require(RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                7, L"aurora-tone", 7, L"aurora-tone") &&
            RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                0, L"aurora-tone", std::nullopt, L"") &&
            !RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                8, L"aurora-tone", 7, L"aurora-tone") &&
            !RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                7, L"cedar-tone", 7, L"aurora-tone"),
            "typed playback command sequence/media-key correlation drifted");
    const auto adapterConfiguration = [](const std::wstring_view identity) {
        Configuration configuration;
        configuration.origin = L"https://wrail-media-" + std::wstring{identity} +
            L".invalid";
        configuration.entryAsset = L"media/index.html";
        configuration.resources = {
            {L"media/index.html", L"text/html",
                {static_cast<std::uint8_t>('<'), static_cast<std::uint8_t>('>')}},
            {L"media/tone.bin", L"application/octet-stream", {std::uint8_t{1}}},
        };
        return configuration;
    };
    auto aurora = adapterConfiguration(L"aurora");
    auto cedar = adapterConfiguration(L"cedar");
    aurora.allowedFrameOrigins = {L"https://frames.aurora.invalid"};
    cedar.allowedFrameOrigins = {L"https://frames.cedar.invalid"};
    Require(RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(aurora) &&
                RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(cedar),
            "provider-neutral adapters did not receive equal native admission");
    auto explicitPort = aurora;
    explicitPort.allowedFrameOrigins = {L"https://frames.aurora.invalid:443"};
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(
                explicitPort),
            "explicit-port frame origin crossed native configuration admission");
    auto unsafe = aurora;
    unsafe.entryAsset = L"../credential.txt";
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(unsafe),
            "unsafe adapter entry path was admitted");
    auto oversized = cedar;
    oversized.resources.front().content.resize(256 * 1024 + 1);
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(oversized),
            "oversized adapter resource was admitted");
    POINT activationPoint{};
    Require(RichMediaSurfaceCoordinatorTestPeer::FocusedActionPoint(
                {100.0, 40.0, 120.0, 60.0}, {12, 18, 712, 438},
                activationPoint) &&
                activationPoint.x == 160 && activationPoint.y == 70,
            "1.5x DPI client-space activation point was raster-scaled");
    std::cout << "RichMediaSurfaceCoordinator contract cases passed=25\n";
}

struct ProcessSample final {
    struct Entry final {
        DWORD processId{};
        DWORD parentProcessId{};
        std::wstring image;
        std::wstring role;
        std::size_t privateBytes{};
        std::uint64_t cpu100ns{};
    };
    std::size_t privateBytes{};
    std::uint64_t cpu100ns{};
    std::size_t processCount{};
    std::vector<Entry> entries;
};

struct ProcessIdentity final {
    DWORD processId{};
    DWORD parentProcessId{};
    std::uint64_t creation100ns{};
};

bool TryAdmitTemporalDescendant(
    const ProcessIdentity& candidate, std::vector<ProcessIdentity>& owned) {
    if (!candidate.processId || candidate.creation100ns == 0 ||
        std::any_of(owned.begin(), owned.end(), [&](const auto& current) {
            return current.processId == candidate.processId;
        })) return false;
    const auto parent = std::find_if(
        owned.begin(), owned.end(), [&](const auto& current) {
            return current.processId == candidate.parentProcessId;
        });
    if (parent == owned.end() || candidate.creation100ns < parent->creation100ns)
        return false;
    owned.push_back(candidate);
    return true;
}

std::uint64_t ProcessCreationTime(const HANDLE process) {
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!process || !GetProcessTimes(process, &created, &exited, &kernel, &user))
        return 0;
    ULARGE_INTEGER value{created.dwLowDateTime, created.dwHighDateTime};
    return value.QuadPart;
}

std::wstring ProcessCommandLine(const HANDLE process) {
    struct NativeUnicodeString final {
        USHORT length{};
        USHORT maximumLength{};
        PWSTR buffer{};
    };
    using NtQueryInformationProcessFn = LONG(NTAPI*)(
        HANDLE, ULONG, PVOID, ULONG, PULONG);
    static const auto query = reinterpret_cast<NtQueryInformationProcessFn>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryInformationProcess"));
    if (!query) return {};
    ULONG length{};
    (void)query(process, 60, nullptr, 0, &length);
    if (length < sizeof(NativeUnicodeString) || length > 64 * 1024) return {};
    std::vector<std::byte> storage(length);
    if (query(process, 60, storage.data(), length, &length) < 0) return {};
    const auto* command = reinterpret_cast<const NativeUnicodeString*>(storage.data());
    if (!command->buffer || command->length == 0 ||
        command->length > command->maximumLength) return {};
    return {command->buffer, command->length / sizeof(wchar_t)};
}

std::wstring ProcessRole(const DWORD processId, const DWORD parentProcessId,
                         const DWORD root, const std::wstring_view image,
                         const std::wstring_view commandLine) {
    if (processId == root) return L"test-host";
    if (image != L"msedgewebview2.exe") return L"owned-child";
    if (commandLine.find(L"--type=renderer") != std::wstring_view::npos)
        return L"renderer";
    if (commandLine.find(L"--type=gpu-process") != std::wstring_view::npos)
        return L"gpu";
    if (commandLine.find(L"--type=utility") != std::wstring_view::npos)
        return L"utility";
    if (commandLine.find(L"--type=crashpad-handler") != std::wstring_view::npos)
        return L"crashpad";
    return parentProcessId == root ? L"browser" : L"browser-child";
}

ProcessSample OwnedProcessSample() {
    const DWORD root = GetCurrentProcessId();
    std::vector<PROCESSENTRY32W> processEntries;
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    Require(snapshot != INVALID_HANDLE_VALUE, "process snapshot failed");
    PROCESSENTRY32W entry{sizeof(entry)};
    if (Process32FirstW(snapshot, &entry)) {
        do {
            processEntries.push_back(entry);
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    const HANDLE rootProcess = OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION, FALSE, root);
    Require(rootProcess != nullptr, "test-host process authority was unavailable");
    const auto rootCreation = ProcessCreationTime(rootProcess);
    CloseHandle(rootProcess);
    Require(rootCreation != 0, "test-host creation authority was unavailable");
    std::vector<ProcessIdentity> owned{{root, 0, rootCreation}};
    bool changed = true;
    while (changed) {
        changed = false;
        for (const auto& candidate : processEntries) {
            if (std::any_of(owned.begin(), owned.end(), [&](const auto& current) {
                    return current.processId == candidate.th32ProcessID;
                }) || !std::any_of(owned.begin(), owned.end(), [&](const auto& current) {
                    return current.processId == candidate.th32ParentProcessID;
                })) continue;
            const HANDLE process = OpenProcess(
                PROCESS_QUERY_LIMITED_INFORMATION, FALSE, candidate.th32ProcessID);
            if (!process) continue;
            const ProcessIdentity identity{
                candidate.th32ProcessID, candidate.th32ParentProcessID,
                ProcessCreationTime(process)};
            CloseHandle(process);
            if (TryAdmitTemporalDescendant(identity, owned)) changed = true;
        }
    }
    ProcessSample sample;
    for (const auto& identity : owned) {
        const DWORD processId = identity.processId;
        const HANDLE process = OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, processId);
        if (!process) continue;
        ProcessSample::Entry processSample;
        processSample.processId = processId;
        const auto processEntry = std::find_if(
            processEntries.begin(), processEntries.end(), [&](const auto& candidate) {
                return candidate.th32ProcessID == processId;
            });
        if (processEntry != processEntries.end()) {
            processSample.parentProcessId = processEntry->th32ParentProcessID;
            processSample.image = processEntry->szExeFile;
        }
        const auto commandLine = ProcessCommandLine(process);
        processSample.role = ProcessRole(
            processId, processSample.parentProcessId, root,
            processSample.image, commandLine);
        PROCESS_MEMORY_COUNTERS_EX counters{sizeof(counters)};
        FILETIME created{}, exited{}, kernel{}, user{};
        if (GetProcessMemoryInfo(
                process, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
                sizeof(counters))) {
            sample.privateBytes += counters.PrivateUsage;
            ++sample.processCount;
            processSample.privateBytes = counters.PrivateUsage;
        }
        if (GetProcessTimes(process, &created, &exited, &kernel, &user)) {
            ULARGE_INTEGER kernelValue{kernel.dwLowDateTime, kernel.dwHighDateTime};
            ULARGE_INTEGER userValue{user.dwLowDateTime, user.dwHighDateTime};
            sample.cpu100ns += kernelValue.QuadPart + userValue.QuadPart;
            processSample.cpu100ns = kernelValue.QuadPart + userValue.QuadPart;
        }
        sample.entries.push_back(std::move(processSample));
        CloseHandle(process);
    }
    return sample;
}

void RunProcessOwnershipCases() {
    std::vector<ProcessIdentity> owned{{100, 0, 1'000}};
    const std::array candidates{
        ProcessIdentity{102, 101, 1'200},
        ProcessIdentity{103, 101, 900},
        ProcessIdentity{101, 100, 1'100}};
    bool changed = true;
    while (changed) {
        changed = false;
        for (const auto& candidate : candidates)
            if (TryAdmitTemporalDescendant(candidate, owned)) changed = true;
    }
    const auto admitted = [&](const DWORD processId) {
        return std::any_of(owned.begin(), owned.end(), [&](const auto& process) {
            return process.processId == processId;
        });
    };
    Require(admitted(101) && admitted(102),
            "temporally valid descendant chain was rejected");
    Require(!admitted(103),
            "stale parent-PID-reuse process was admitted");
    std::cout << "RichMedia process ownership cases passed=2\n";
}

constexpr std::uint64_t kCpuAccountingQuantum100ns = 156'250;

std::uint64_t RoundedCpuBudget100ns(
    const std::uint64_t wall100ns, const std::uint64_t basisPoints) {
    constexpr std::uint64_t kBasisPointsPerWhole = 10'000;
    const auto exactCeiling =
        (wall100ns * basisPoints + kBasisPointsPerWhole - 1) /
        kBasisPointsPerWhole;
    return ((exactCeiling + kCpuAccountingQuantum100ns - 1) /
            kCpuAccountingQuantum100ns) * kCpuAccountingQuantum100ns;
}

bool CpuWithinRoundedBudget(const std::uint64_t cpu100ns,
                            const std::uint64_t wall100ns,
                            const std::uint64_t basisPoints) {
    return cpu100ns <= RoundedCpuBudget100ns(wall100ns, basisPoints);
}

void RunCpuBudgetCases() {
    const auto rounded = RoundedCpuBudget100ns(10'000'000, 150);
    Require(rounded == kCpuAccountingQuantum100ns &&
                CpuWithinRoundedBudget(150'001, 10'000'000, 150),
            "sub-quantum CPU remainder did not reach the rounded ceiling");
    Require(!CpuWithinRoundedBudget(
                rounded + kCpuAccountingQuantum100ns, 10'000'000, 150),
            "the next full CPU accounting quantum was admitted");
    std::cout << "RichMedia CPU budget arithmetic cases passed=2\n";
}

bool EnforcesPostCloseMemoryBudget(const std::string_view state) {
    return state == "post-close";
}

void RunMemoryBudgetScopeCases() {
    Require(!EnforcesPostCloseMemoryBudget("active-media"),
            "active-media was assigned a post-close memory ceiling");
    Require(EnforcesPostCloseMemoryBudget("post-close"),
            "post-close lost its retained-process memory ceiling");
    std::cout << "RichMedia memory budget scope cases passed=2\n";
}

struct CpuWorkloadPolicy final {
    std::uint64_t totalBasisPoints{};
    bool enforceFiveSampleIdleCeiling{};
};

CpuWorkloadPolicy CpuPolicyForState(const std::string_view state) {
    if (state == "active-media" || state == "hidden-before-close")
        return {300, false};
    return {150, true};
}

void RunCpuWorkloadPolicyCases() {
    const auto visible = CpuPolicyForState("visible-idle");
    const auto closed = CpuPolicyForState("post-close");
    const auto active = CpuPolicyForState("active-media");
    const auto hidden = CpuPolicyForState("hidden-before-close");
    Require(visible.totalBasisPoints == 150 &&
                visible.enforceFiveSampleIdleCeiling &&
                closed.totalBasisPoints == 150 &&
                closed.enforceFiveSampleIdleCeiling,
            "idle state lost its sustained or five-sample CPU ceiling");
    Require(active.totalBasisPoints == 300 &&
                !active.enforceFiveSampleIdleCeiling &&
                hidden.totalBasisPoints == 300 &&
                !hidden.enforceFiveSampleIdleCeiling,
            "playing state did not receive the active sustained CPU policy");
    std::cout << "RichMedia CPU workload policy cases passed=4\n";
}

void RequireReady(Fixture& fixture, const char* message,
                  const std::wstring_view expectedFocus = L"play") {
    Require(!expectedFocus.empty(), "ready focus authority must be explicit");
    Require(PumpUntil([&] {
        const auto state = fixture.coordinator().state();
        return (state.lifecycle == widgetrail::richmedia::Lifecycle::Visible ||
                state.lifecycle == widgetrail::richmedia::Lifecycle::ReadyHidden) &&
            state.focusedElement == expectedFocus;
    }, 15s), message);
}

void RequireRetainedEnvironment(
    const widgetrail::richmedia::EnvironmentState& expected,
    const widgetrail::richmedia::EnvironmentState& actual,
    const char* message) {
    Require(actual.lifecycle == widgetrail::richmedia::EnvironmentLifecycle::Ready &&
            actual.generation == expected.generation &&
            actual.browserProcessId == expected.browserProcessId &&
            actual.profileDirectory == expected.profileDirectory &&
            actual.observerActive && !actual.faulted,
            message);
}

void RunLifecycleCases() {
    using namespace widgetrail::richmedia;
    const auto root = std::filesystem::temp_directory_path() /
        (L"wrail-rich-media-proof-lifecycle-" + std::to_wstring(GetCurrentProcessId()));
    const auto retired = root / L"wrail-rich-media-retired-proof";
    const auto active = root / L"wrail-rich-media-active-proof";
    std::filesystem::create_directories(retired);
    std::filesystem::create_directories(active);
    std::ofstream(retired / L".wrail-retired-owner-4294967295") << "retired\n";
    std::ofstream(active / (L".wrail-retired-owner-" +
        std::to_wstring(GetCurrentProcessId()))) << "active\n";

    std::filesystem::path finalProfile;
    {
        Fixture fixture(1, true, root);
        RequireReady(fixture, "initial process-lifetime session did not become ready");
        const auto initialEnvironment = fixture.coordinator().environmentState();
        Require(initialEnvironment.lifecycle == EnvironmentLifecycle::Ready &&
                    initialEnvironment.generation > 0 &&
                    initialEnvironment.browserProcessId > 0 &&
                    initialEnvironment.observerActive &&
                    std::filesystem::exists(initialEnvironment.profileDirectory),
                "initial environment authority was incomplete");
        Require(!std::filesystem::exists(retired) && std::filesystem::exists(active),
                "deferred cleanup did not remove only an owner-absent marked profile");

        const auto before = fixture.coordinator().state().lastAcknowledgedCommandId;
        Require(fixture.coordinator().SendCommand(Command::Activate),
                "trusted spatial activation was rejected");
        Require(PumpUntil([&] {
            const auto state = fixture.coordinator().state();
            return state.lastAcknowledgedCommandId > before && state.playing;
        }, 2s), "local media did not become active");
        Require(SUCCEEDED(fixture.CloseSession()) && fixture.finalDetachSucceededAndWaited(),
                "session target detach was not completion-fenced");
        const auto teardown = fixture.coordinator().sessionTeardownResult();
        Require(teardown.sessionOwnersEmpty && teardown.environmentRetained &&
                    !teardown.callbackDeadlineExpired &&
                    fixture.finalConfigurationReleasedBeforeDetach() &&
                    fixture.coordinator().state().lifecycle == Lifecycle::Absent &&
                    !fixture.presentationVisible(),
                "normal close retained surface authority or released the environment");
        RequireRetainedEnvironment(initialEnvironment,
            fixture.coordinator().environmentState(),
            "normal close replaced process-lifetime environment authority");

        Require(SUCCEEDED(fixture.OpenSession(true, false)),
                "normal controller reopen submission failed");
        RequireReady(fixture, "normal controller reopen did not become ready");
        RequireRetainedEnvironment(initialEnvironment,
            fixture.coordinator().environmentState(),
            "normal reopen replaced process-lifetime environment authority");

        RichMediaSurfaceCoordinatorTestPeer::Fault(
            fixture.coordinator(), L"proof-explicit-fault");
        Require(fixture.coordinator().state().lifecycle == Lifecycle::Faulted &&
                    !fixture.coordinator().state().inputEnabled &&
                    !fixture.presentationVisible(),
                "fault retained visible or input authority");
        ComPtr<IRawElementProviderSimple> provider;
        Require(fixture.coordinator().GetAutomationProvider(&provider) == S_FALSE && !provider,
                "fault retained UIA authority");
        Require(SUCCEEDED(fixture.Retry()), "healthy-environment Retry submission failed");
        RequireReady(fixture, "healthy-environment Retry did not become ready");
        RequireRetainedEnvironment(initialEnvironment,
            fixture.coordinator().environmentState(),
            "healthy Retry replaced process-lifetime environment authority");

        RichMediaSurfaceCoordinatorTestPeer::MarkEnvironmentFaulted(fixture.coordinator());
        RichMediaSurfaceCoordinatorTestPeer::Fault(
            fixture.coordinator(), L"proof-browser-environment-fault");
        Require(SUCCEEDED(fixture.Retry()), "faulted-environment Retry submission failed");
        RequireReady(fixture, "faulted-environment Retry did not recreate a ready session");
        const auto recreated = fixture.coordinator().environmentState();
        Require(recreated.lifecycle == EnvironmentLifecycle::Ready &&
                    recreated.generation > initialEnvironment.generation &&
                    recreated.profileDirectory != initialEnvironment.profileDirectory &&
                    recreated.observerActive && !recreated.faulted,
                "environment fault did not establish one fresh environment generation");
        finalProfile = recreated.profileDirectory;
    }
    const auto marker = finalProfile /
        (L".wrail-retired-owner-" + std::to_wstring(GetCurrentProcessId()));
    Require(std::filesystem::exists(marker),
            "final shutdown did not mark the process-lifetime profile for deferred cleanup");
    std::cout << "RichMediaSurfaceCoordinator lifecycle cases passed=20\n";
}

void RunProviderNeutralAdapterCases() {
    using namespace widgetrail::richmedia;
    unsigned int ordinal = 40;
    for (const auto identity : {L"aurora-adapter", L"cedar-adapter"}) {
        const std::wstring_view adapterIdentity{identity};
        const std::wstring_view initialFocus = adapterIdentity == L"aurora-adapter"
            ? L"aurora.primary" : L"cedar.primary";
        const std::wstring_view nextFocus = adapterIdentity == L"aurora-adapter"
            ? L"aurora.secondary" : L"cedar.secondary";
        Fixture fixture(ordinal++, true, {}, identity);
        RequireReady(fixture, "provider-neutral adapter did not become ready",
                     initialFocus);
        const auto before = fixture.coordinator().state().lastAcknowledgedCommandId;
        Require(fixture.coordinator().SendCommand(Command::NavigateNext),
                "provider-neutral adapter command was rejected");
        Require(PumpUntil([&] {
            const auto state = fixture.coordinator().state();
            return state.lastAcknowledgedCommandId > before &&
                state.focusedElement == nextFocus;
        }, 2s), "provider-neutral adapter event was not exactly correlated");
    }

    Fixture replacement(ordinal++, true, {}, L"aurora-adapter");
    RequireReady(replacement, "replacement source adapter did not become ready",
                 L"aurora.primary");
    const auto environment = replacement.coordinator().environmentState();
    const auto sourceAuthority = replacement.coordinator().state().authority;
    Require(SUCCEEDED(replacement.CloseSession()) &&
                replacement.finalDetachSucceededAndWaited(),
            "replacement source session did not detach cleanly");
    replacement.SelectAdapter(L"cedar-adapter");
    Require(SUCCEEDED(replacement.OpenSession(true, false)),
            "replacement destination session submission failed");
    RequireReady(replacement, "replacement destination adapter did not become ready",
                 L"cedar.primary");
    const auto destinationAuthority = replacement.coordinator().state().authority;
    RequireRetainedEnvironment(environment,
        replacement.coordinator().environmentState(),
        "widget replacement discarded the process-lifetime environment");
    Require(destinationAuthority.environmentGeneration ==
                sourceAuthority.environmentGeneration &&
            destinationAuthority.surfaceGeneration > sourceAuthority.surfaceGeneration &&
            destinationAuthority.sessionGeneration > sourceAuthority.sessionGeneration &&
            destinationAuthority.controllerGeneration >
                sourceAuthority.controllerGeneration &&
            destinationAuthority.documentGeneration > sourceAuthority.documentGeneration,
            "widget replacement did not change only document/controller/session authority");
    std::cout << "RichMedia provider-neutral adapter cases passed=3\n";
}

void RunLifecycleAndPerformance() {
    using namespace widgetrail::richmedia;
    Fixture fixture(20, true);
    std::vector<long long> startupMilliseconds;
    auto started = std::chrono::steady_clock::now();
    RequireReady(fixture, "cold environment/session did not become ready");
    startupMilliseconds.push_back(std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - started).count());
    const auto environment = fixture.coordinator().environmentState();
    for (int run = 1; run < 5; ++run) {
        started = std::chrono::steady_clock::now();
        Require(SUCCEEDED(fixture.Reopen(true)), "controller restart submission failed");
        RequireReady(fixture, "controller restart did not become ready");
        startupMilliseconds.push_back(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started).count());
        RequireRetainedEnvironment(environment, fixture.coordinator().environmentState(),
            "controller restart replaced process-lifetime environment");
    }

    const auto sampleState = [&](const char* label, const bool visible,
                                 const bool activeMedia) {
        if (fixture.coordinator().state().lifecycle != Lifecycle::Absent)
            Require(SUCCEEDED(fixture.coordinator().SetVisible(visible)),
                    "visibility transition failed");
        if (activeMedia && !fixture.coordinator().state().playing) {
            const auto before = fixture.coordinator().state().lastAcknowledgedCommandId;
            Require(fixture.coordinator().SendCommand(Command::Activate),
                    "local media activation command was rejected");
            Require(PumpUntil([&] {
                const auto state = fixture.coordinator().state();
                return state.lastAcknowledgedCommandId > before && state.playing;
            }, 2s), "local media did not become active");
        }
        std::this_thread::sleep_for(3s);
        std::size_t maximumBytes{};
        std::vector<double> cpuPercent;
        std::vector<std::uint64_t> cpu100ns;
        std::vector<std::uint64_t> wall100ns;
        std::vector<long long> acknowledgements;
        auto previous = OwnedProcessSample();
        auto previousAt = std::chrono::steady_clock::now();
        const auto printMembership = [&](const ProcessSample& value) {
            for (const auto& process : value.entries)
                std::wcout << L"process-membership state="
                           << std::wstring{label,
                               label + std::char_traits<char>::length(label)}
                           << L" pid=" << process.processId
                           << L" parent=" << process.parentProcessId
                           << L" role=" << process.role
                           << L" image=" << process.image << L'\n';
            std::wcout << std::flush;
        };
        printMembership(previous);
        for (int sample = 0; sample < 30; ++sample) {
            if (activeMedia) {
                const auto before = fixture.coordinator().state().lastAcknowledgedCommandId;
                const auto commandStarted = std::chrono::steady_clock::now();
                Require(fixture.coordinator().SendCommand(Command::NavigateNext),
                        "interactive command was rejected");
                Require(PumpUntil([&] {
                    const auto state = fixture.coordinator().state();
                    return state.lastAcknowledgedCommandId > before && state.playing;
                }, 50ms), "input acknowledgement exceeded 50 ms");
                acknowledgements.push_back(std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - commandStarted).count());
            }
            std::this_thread::sleep_for(1s);
            const auto now = std::chrono::steady_clock::now();
            const auto current = OwnedProcessSample();
            const double wall = std::chrono::duration<double>(now - previousAt).count();
            const auto cpuDelta = current.cpu100ns >= previous.cpu100ns
                ? current.cpu100ns - previous.cpu100ns : 0;
            const auto wallDelta100ns = static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::duration<long long,
                    std::ratio<1, 10'000'000>>>(now - previousAt).count());
            const double cpu = wall > 0.0
                ? static_cast<double>(cpuDelta) / 1.0e7 / wall * 100.0 : 0.0;
            const auto hasPid = [](const ProcessSample& value, const DWORD processId) {
                return std::any_of(value.entries.begin(), value.entries.end(),
                    [&](const auto& process) { return process.processId == processId; });
            };
            const bool membershipChanged =
                current.entries.size() != previous.entries.size() ||
                std::any_of(current.entries.begin(), current.entries.end(),
                    [&](const auto& process) { return !hasPid(previous, process.processId); }) ||
                std::any_of(previous.entries.begin(), previous.entries.end(),
                    [&](const auto& process) { return !hasPid(current, process.processId); });
            cpuPercent.push_back(cpu);
            cpu100ns.push_back(cpuDelta);
            wall100ns.push_back(wallDelta100ns);
            maximumBytes = std::max(maximumBytes, current.privateBytes);
            std::cout << "sample state=" << label << " index=" << sample + 1
                      << " process-count=" << current.processCount
                      << " private-bytes=" << current.privateBytes
                      << " wall-seconds=" << wall
                      << " cpu-core-percent=" << cpu
                      << " membership-changed=" << (membershipChanged ? 1 : 0)
                      << '\n' << std::flush;
            if (membershipChanged) printMembership(current);
            previous = current;
            previousAt = now;
        }
        const double rawMaximumCpu =
            *std::max_element(cpuPercent.begin(), cpuPercent.end());
        double maximumFiveSecondCpu{};
        std::uint64_t maximumFiveCpu100ns{};
        std::uint64_t maximumFiveWall100ns{};
        std::uint64_t maximumFiveBudget100ns{};
        for (std::size_t first = 0; first + 5 <= cpuPercent.size(); ++first) {
            const auto windowCpu = std::accumulate(
                cpu100ns.begin() + first, cpu100ns.begin() + first + 5,
                std::uint64_t{});
            const auto windowWall = std::accumulate(
                wall100ns.begin() + first, wall100ns.begin() + first + 5,
                std::uint64_t{});
            const double average = windowWall
                ? static_cast<double>(windowCpu) /
                    static_cast<double>(windowWall) * 100.0
                : 0.0;
            if (first == 0 || average > maximumFiveSecondCpu) {
                maximumFiveSecondCpu = average;
                maximumFiveCpu100ns = windowCpu;
                maximumFiveWall100ns = windowWall;
                maximumFiveBudget100ns = RoundedCpuBudget100ns(windowWall, 300);
            }
        }
        const auto totalCpu100ns = std::accumulate(
            cpu100ns.begin(), cpu100ns.end(), std::uint64_t{});
        const auto totalWall100ns = std::accumulate(
            wall100ns.begin(), wall100ns.end(), std::uint64_t{});
        const double totalWindowMeanCpu = totalWall100ns
            ? static_cast<double>(totalCpu100ns) /
                static_cast<double>(totalWall100ns) * 100.0
            : 0.0;
        const auto cpuPolicy = CpuPolicyForState(label);
        const auto totalBudget100ns = RoundedCpuBudget100ns(
            totalWall100ns, cpuPolicy.totalBasisPoints);
        auto sortedCpuPercent = cpuPercent;
        std::sort(sortedCpuPercent.begin(), sortedCpuPercent.end());
        const double medianCpu = sortedCpuPercent[sortedCpuPercent.size() / 2];
        std::cout << "state-summary state=" << label
                  << " max-private-bytes=" << maximumBytes
                  << " median-cpu-core-percent=" << medianCpu
                  << " raw-max-cpu-core-percent=" << rawMaximumCpu
                  << " total-window-mean-cpu-core-percent=" << totalWindowMeanCpu
                  << " total-cpu-100ns=" << totalCpu100ns
                  << " total-wall-100ns=" << totalWall100ns
                  << " total-cpu-ceiling-100ns=" << totalBudget100ns
                  << " total-cpu-budget-basis-points="
                  << cpuPolicy.totalBasisPoints
                  << " max-five-second-cpu-core-percent=" << maximumFiveSecondCpu
                  << " max-five-cpu-100ns=" << maximumFiveCpu100ns
                  << " max-five-wall-100ns=" << maximumFiveWall100ns
                  << " max-five-cpu-ceiling-100ns=" << maximumFiveBudget100ns
                  << '\n' << std::flush;
        if (EnforcesPostCloseMemoryBudget(label))
            Require(maximumBytes <= 256ULL * 1024ULL * 1024ULL,
                    "post-close rich-media proof exceeded 256 MiB private memory");
        Require(CpuWithinRoundedBudget(
                    totalCpu100ns, totalWall100ns, cpuPolicy.totalBasisPoints) &&
                    (!cpuPolicy.enforceFiveSampleIdleCeiling ||
                        CpuWithinRoundedBudget(
                            maximumFiveCpu100ns, maximumFiveWall100ns, 300)),
                "rich-media proof exceeded bounded CPU budget");
        if (!acknowledgements.empty()) {
            std::sort(acknowledgements.begin(), acknowledgements.end());
            const auto p95 = acknowledgements[(acknowledgements.size() * 95 - 1) / 100];
            Require(p95 < 50, "rich-media input p95 exceeded 50 ms");
            std::cout << "input-ack p95-ms=" << p95
                      << " max-ms=" << acknowledgements.back() << '\n';
        }
    };

    sampleState("visible-idle", true, false);
    sampleState("active-media", true, true);
    sampleState("hidden-before-close", false, false);
    Require(SUCCEEDED(fixture.CloseSession()) && fixture.finalDetachSucceededAndWaited(),
            "performance session close was not completion-fenced");
    const auto teardown = fixture.coordinator().sessionTeardownResult();
    Require(teardown.sessionOwnersEmpty && teardown.environmentRetained &&
                !teardown.callbackDeadlineExpired &&
                fixture.coordinator().state().lifecycle == Lifecycle::Absent &&
                !fixture.presentationVisible(),
            "post-close surface retained active authority");
    RequireRetainedEnvironment(environment, fixture.coordinator().environmentState(),
        "post-close sampling lost process-lifetime environment authority");
    sampleState("post-close", false, false);
    std::sort(startupMilliseconds.begin(), startupMilliseconds.end());
    std::cout << "session-start min-ms=" << startupMilliseconds.front()
              << " median-ms=" << startupMilliseconds[startupMilliseconds.size() / 2]
              << " max-ms=" << startupMilliseconds.back() << '\n';
    std::cout << "RichMediaSurfaceCoordinator lifecycle/performance passed=1\n";
}

} // namespace

int wmain(int argc, wchar_t**) {
    const HRESULT initialize = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialize)) {
        std::cerr << "COM initialization failed hr=" << initialize << '\n';
        return 1;
    }
    try {
        RunContractCases();
        RunProcessOwnershipCases();
        RunCpuBudgetCases();
        RunMemoryBudgetScopeCases();
        RunCpuWorkloadPolicyCases();
        RunProviderNeutralAdapterCases();
        if (argc > 1) RunLifecycleAndPerformance();
        else RunLifecycleCases();
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "RichMediaSurfaceCoordinatorTests failed: " << error.what() << '\n';
        CoUninitialize();
        return 1;
    }
}
