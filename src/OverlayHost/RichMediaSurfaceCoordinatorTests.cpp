#include "OverlayCompositionSurface.h"
#include "EmbeddedMediaResourceContract.h"
#include "RichMediaSurfaceCoordinator.h"
#include "WidgetProtocolPresentationContract.generated.h"

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
#include <iterator>
#include <memory>
#include <numeric>
#include <regex>
#include <stdexcept>
#include <string>
#include <thread>
#include <tuple>
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
        const std::vector<std::wstring>& allowedOrigins,
        const std::vector<std::wstring>& allowedFamilies = {}) {
        return RichMediaSurfaceCoordinator::IsAllowedFrameResource(
            uri, allowedOrigins, allowedFamilies);
    }
    static bool IsAllowedFrameNavigation(
        const std::wstring_view uri,
        const std::vector<std::wstring>& allowedOrigins) {
        return RichMediaSurfaceCoordinator::IsAllowedFrameNavigation(
            uri, allowedOrigins);
    }
    static std::optional<std::wstring> ManifestAssemblyIdentity(
        const std::wstring_view manifest) {
        return RichMediaSurfaceCoordinator::ManifestAssemblyIdentity(manifest);
    }
    static std::optional<std::wstring> FormatInstalledAppReferer(
        const std::wstring_view identity) {
        return RichMediaSurfaceCoordinator::FormatInstalledAppReferer(identity);
    }
    static std::optional<std::wstring> SelectInstalledAppReferer(
        const bool packaged, const std::wstring_view packageIdentity,
        const std::wstring_view manifestIdentity) {
        return RichMediaSurfaceCoordinator::SelectInstalledAppReferer(
            packaged, packageIdentity, manifestIdentity);
    }
    static auto ApplyInstalledAppReferer(
        const std::wstring_view uri,
        const std::vector<std::wstring>& allowedOrigins,
        const std::wstring_view referer,
        const std::function<HRESULT(const wchar_t*, const wchar_t*)>& setHeader,
        const std::vector<std::wstring>& allowedFamilies = {}) {
        return static_cast<int>(RichMediaSurfaceCoordinator::ApplyInstalledAppReferer(
            uri, allowedOrigins, referer, setHeader, allowedFamilies));
    }
    static bool IsPlaybackCommandCorrelated(
        const std::uint64_t commandSequence, const std::wstring_view mediaKey,
        const std::optional<std::uint64_t> pendingSequence,
        const std::wstring_view pendingMediaKey) {
        return RichMediaSurfaceCoordinator::IsPlaybackCommandCorrelated(
            commandSequence, mediaKey, pendingSequence, pendingMediaKey);
    }
    static PlaybackCommandDispatchResult ClassifyPlaybackCommandDispatch(
        const Lifecycle lifecycle) {
        RichMediaSurfaceCoordinator coordinator;
        coordinator.state_.lifecycle = lifecycle;
        coordinator.state_.inputEnabled = lifecycle == Lifecycle::Visible;
        return coordinator.DispatchPlaybackCommand({
            41, PlaybackCommandKind::Pause, L"neutral-media"});
    }
    static bool IsValidAdapterConfiguration(const Configuration& configuration) {
        return RichMediaSurfaceCoordinator::IsValidAdapterConfiguration(configuration);
    }
    static auto SealedResourcePlan(const std::wstring_view range,
                                   const std::wstring_view contentType,
                                   const std::size_t length,
                                   const bool eligible = true) {
        const auto plan = RichMediaSurfaceCoordinator::PlanSealedResourceResponse(
            range, contentType, length, eligible);
        return std::tuple{plan.status, plan.offset, plan.length, plan.headers};
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
    static RichMediaEnvironmentHandle SharedEnvironment(
        const RichMediaSurfaceCoordinator& coordinator) {
        return coordinator.sharedEnvironment_;
    }
    static void HoldSharedEnvironmentCreating(
        RichMediaSurfaceCoordinator& coordinator) {
        coordinator.HoldSharedEnvironmentCreatingForTest();
    }
    static void ReleaseSharedEnvironmentReady(
        RichMediaSurfaceCoordinator& coordinator) {
        coordinator.ReleaseSharedEnvironmentReadyForTest();
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
    static bool DocumentAudioOutputActive(
        const RichMediaSurfaceCoordinator& coordinator) {
        ComPtr<ICoreWebView2_8> core8;
        BOOL muted = TRUE;
        BOOL playing = FALSE;
        return coordinator.core_ && SUCCEEDED(coordinator.core_.As(&core8)) &&
            SUCCEEDED(core8->get_IsMuted(&muted)) &&
            SUCCEEDED(core8->get_IsDocumentPlayingAudio(&playing)) &&
            !muted && playing;
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

struct FixtureCallbackProbe final {
    std::atomic_bool retired{};
    std::atomic_uint32_t callbacksAfterRetirement{};

    void Record() noexcept {
        if (retired.load(std::memory_order_acquire))
            callbacksAfterRetirement.fetch_add(1, std::memory_order_relaxed);
    }
};

class Fixture final {
public:
    Fixture(const unsigned int ordinal, const bool visible,
            std::filesystem::path profileRoot = {},
            std::wstring adapterIdentity = {},
            std::vector<widgetrail::richmedia::Configuration::Resource>
                adapterResources = {},
            std::shared_ptr<FixtureCallbackProbe> callbackProbe = {},
            widgetrail::richmedia::RichMediaEnvironmentHandle sharedEnvironment = {},
            const bool deferInitialPresentation = false)
        : coordinator_(sharedEnvironment
              ? std::move(sharedEnvironment)
              : widgetrail::richmedia::RichMediaSurfaceCoordinator::
                    CreateSharedEnvironment()),
          profileRoot_(profileRoot.empty()
              ? std::filesystem::temp_directory_path() /
                    (L"wrail-rich-media-proof-root-" +
                     std::to_wstring(GetCurrentProcessId()) + L"-" +
                     std::to_wstring(ordinal))
              : std::move(profileRoot)),
          adapterIdentity_(std::move(adapterIdentity)),
          adapterResources_(std::move(adapterResources)),
          deferInitialPresentation_(deferInitialPresentation),
          callbackProbe_(std::move(callbackProbe)) {
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
        sessionOpen_ = true;
        if (deferInitialPresentation_) return S_OK;
        return CommitInitialPresentation();
    }

    HRESULT CommitInitialPresentation() {
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = composition_.CommitExternalContentPresentation(
            {0, 0, 800, 520}, false, timing);
        return result;
    }

    HRESULT SuspendPresentation() {
        HRESULT result = coordinator_.BeginPresentationTransfer();
        if (FAILED(result)) return result;
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        result = composition_.DetachExternalContentTarget(timing);
        detachWaited_ = timing.waitedForCompletion;
        return result;
    }

    HRESULT ResumePresentation() {
        ComPtr<IUnknown> target;
        HRESULT result = composition_.CreateExternalContentTarget(&target);
        if (FAILED(result)) return result;
        widgetrail::richmedia::PresentationTarget presentation;
        presentation.ownerWindow = window_;
        presentation.compositionTarget = std::move(target);
        presentation.bounds = {0, 0, 800, 520};
        presentation.rasterScale = 1.0;
        presentation.setPresentationVisible = PresentationVisibilityCallback();
        return coordinator_.CompletePresentationTransfer(std::move(presentation));
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
    std::uint64_t presentationApplications() const {
        return presentationApplications_;
    }
    bool presentationCommitFailed() const { return presentationCommitFailed_; }
    HWND window() const { return window_; }
    bool sawDiagnostic(const std::wstring_view text) const {
        return std::any_of(diagnostics_.begin(), diagnostics_.end(),
            [&](const std::wstring& value) { return value.find(text) != std::wstring::npos; });
    }
    std::size_t countDiagnostic(const std::wstring_view text) const {
        return std::count_if(diagnostics_.begin(), diagnostics_.end(),
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
    const std::vector<widgetrail::richmedia::PlaybackEvent>& playbackEvents() const {
        return playbackEvents_;
    }
    void RetireCallbackProbe() {
        if (callbackProbe_)
            callbackProbe_->retired.store(true, std::memory_order_release);
    }
    std::uint32_t callbacksAfterRetirement() const {
        return callbackProbe_
            ? callbackProbe_->callbacksAfterRetirement.load(std::memory_order_acquire)
            : 0;
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
        if (!adapterResources_.empty()) {
            configuration.origin = L"https://wrail-media-local-sample.invalid";
            configuration.entryAsset = L"payload/media/adapter.html";
            configuration.resources = adapterResources_;
        } else if (!adapterIdentity_.empty()) {
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
function emit(type,id){const r=document.activeElement.getBoundingClientRect();chrome.webview.postMessage({type,...a,eventSequence:++seq,commandId:id,commandSequence:id,focus,playing:false,bounds:{x:r.x,y:r.y,width:r.width,height:r.height}});}
chrome.webview.addEventListener('message',e=>{const m=e.data;if(m.command==='initialize'){a={environmentGeneration:m.environmentGeneration,surfaceGeneration:m.surfaceGeneration,sessionGeneration:m.sessionGeneration,controllerGeneration:m.controllerGeneration,documentGeneration:m.documentGeneration};document.getElementById(focus).focus();emit('ready',m.commandId);return;}if(Object.keys(a).some(k=>m[k]!==a[k]))return;if(m.command==='next'||m.command==='previous'){focus=m.command==='next'?')HTML" + secondId + R"HTML(':')HTML" + firstId + R"HTML(';document.getElementById(focus).focus();emit('focus',m.commandId);}});
</script>)HTML";
            configuration.resources.push_back({
                configuration.entryAsset, L"text/html",
                std::vector<std::uint8_t>(page.begin(), page.end())});
        }
        configuration.diagnostic = [this, probe = callbackProbe_](
                const std::wstring_view message) {
            if (probe) probe->Record();
            diagnostics_.emplace_back(message);
            std::wcout << L"diagnostic " << message << L'\n' << std::flush;
        };
        configuration.playbackEvent = [this, probe = callbackProbe_](
                const widgetrail::richmedia::PlaybackEvent& event) {
            if (probe) probe->Record();
            playbackEvents_.push_back(event);
        };
        configuration.setPresentationVisible = PresentationVisibilityCallback();
        return configuration;
    }

    std::function<void(bool)> PresentationVisibilityCallback() {
        return [this, probe = callbackProbe_](
                const bool shown) {
            if (probe) probe->Record();
            ++presentationApplications_;
            presentationVisible_ = shown;
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            const HRESULT result = composition_.CommitExternalContentPresentation(
                {0, 0, 800, 520}, shown, timing);
            if (FAILED(result)) presentationCommitFailed_ = true;
        };
    }
    HWND window_{};
    ComPtr<ID2D1Factory1> factory_;
    widgetrail::OverlayCompositionSurface composition_;
    widgetrail::richmedia::RichMediaSurfaceCoordinator coordinator_;
    std::filesystem::path profileRoot_;
    std::wstring adapterIdentity_;
    std::vector<widgetrail::richmedia::Configuration::Resource> adapterResources_;
    bool sessionOpen_{};
    bool presentationVisible_{};
    std::uint64_t presentationApplications_{};
    bool presentationCommitFailed_{};
    bool deferInitialPresentation_{};
    bool releasedBeforeDetach_{};
    bool detachWaited_{};
    std::vector<std::wstring> diagnostics_;
    std::vector<widgetrail::richmedia::PlaybackEvent> playbackEvents_;
    std::shared_ptr<FixtureCallbackProbe> callbackProbe_;
};

void RunContractCases() {
    using namespace widgetrail::richmedia;
    {
        RichMediaSurfaceCoordinator unbound;
        PresentationTransferFailureStage beginStage{};
        PresentationTransferFailureStage completeStage{};
        PresentationTarget invalidTarget;
        Require(unbound.BeginPresentationTransfer(&beginStage) == E_UNEXPECTED &&
                    beginStage == PresentationTransferFailureStage::Admission &&
                    unbound.CompletePresentationTransfer(
                        std::move(invalidTarget), &completeStage) == E_INVALIDARG &&
                    completeStage == PresentationTransferFailureStage::Admission,
                "presentation-transfer failure result did not remain caller-owned after fail-closed admission");
        Require(PresentationTransferFailureStageValue(
                    PresentationTransferFailureStage::VisibilityDetach) ==
                    L"controller-visibility-detach" &&
                    PresentationTransferFailureStageValue(
                    PresentationTransferFailureStage::RootTargetDetach) ==
                    L"controller-root-target-detach",
                "presentation-transfer detach stages lost their exact diagnostic classification");
    }
    const auto coordinatorSourcePath =
        std::filesystem::path{__FILE__}.parent_path() /
        "RichMediaSurfaceCoordinator.cpp";
    std::ifstream coordinatorStream(coordinatorSourcePath, std::ios::binary);
    Require(static_cast<bool>(coordinatorStream),
            "rich media coordinator source opens");
    const std::string coordinatorSource{
        std::istreambuf_iterator<char>(coordinatorStream), {}};
    const auto activationBegin = coordinatorSource.find(
        "bool RichMediaSurfaceCoordinator::SendFocusedSpatialActivation() noexcept {");
    const auto activationEnd = coordinatorSource.find(
        "bool RichMediaSurfaceCoordinator::FocusedActionPoint(", activationBegin);
    Require(activationBegin != std::string::npos &&
                activationEnd != std::string::npos &&
                activationBegin < activationEnd,
            "focused activation owner source section is bounded");
    const auto activation = coordinatorSource.substr(
        activationBegin, activationEnd - activationBegin);
    const auto move = activation.find("COREWEBVIEW2_MOUSE_EVENT_KIND_MOVE");
    const auto down = activation.find(
        "COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_DOWN", move);
    const auto up = activation.find(
        "COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_UP", down);
    const auto leave = activation.find(
        "COREWEBVIEW2_MOUSE_EVENT_KIND_LEAVE", up);
    Require(move != std::string::npos && down != std::string::npos &&
                up != std::string::npos && leave != std::string::npos &&
                move < down && down < up && up < leave &&
                activation.find(
                    "return SUCCEEDED(result) && SUCCEEDED(leaveResult);") !=
                    std::string::npos,
            "retained controller activation publishes one complete move/down/up/leave gesture and terminalizes either send failure");
    widgetrail::EmbeddedMediaSurfaceDeclaration initialSurface;
    initialSurface.id = L"neutral.primary";
    initialSurface.accessibleName = L"Neutral media";
    initialSurface.entryAsset = L"media/index.html";
    initialSurface.surface.mode = L"responsive";
    initialSurface.surface.widthMode = L"bounded";
    initialSurface.surface.heightMode = L"bounded";
    initialSurface.surface.preferredWidth = 760;
    initialSurface.surface.preferredHeight = 425;
    initialSurface.surface.minimumWidth = 320;
    initialSurface.surface.minimumHeight = 180;
    initialSurface.aspectRatio = 16.0 / 9.0;
    initialSurface.resources = {{L"media/index.html", L"text/html"}};
    initialSurface.commands = {L"activate", L"togglePlayback"};
    auto pendingSurface = initialSurface;
    pendingSurface.pendingCommand = widgetrail::EmbeddedMediaPlaybackCommand{
        3, L"play", L"neutral-tone", std::nullopt, std::nullopt};
    const auto admittedContract =
        widgetrail::EmbeddedMediaResourceContract(initialSurface);
    std::wstring sourceWidgetId = L"neutral-widget";
    std::wstring sourceSurfaceId = initialSurface.id;
    const std::wstring retainedWidgetId{std::wstring_view{sourceWidgetId}};
    const std::wstring retainedSurfaceId{std::wstring_view{sourceSurfaceId}};
    sourceWidgetId.assign(L"navigatePrevious");
    sourceSurfaceId.assign(L"retired-surface-with-different-storage");
    initialSurface.id.assign(L"retired.contract");
    initialSurface.resources.clear();
    Require(retainedWidgetId == L"neutral-widget" &&
                retainedSurfaceId == L"neutral.primary" &&
                admittedContract.id == L"neutral.primary" &&
                admittedContract.resources.size() == 1 &&
                admittedContract.resources[0].path == L"media/index.html",
            "retained media identity/resource contract borrowed snapshot storage");
    int controllerAdmissions = 1;
    const bool pendingRetainsSession = widgetrail::SameEmbeddedMediaResourceContract(
        admittedContract, pendingSurface);
    if (!pendingRetainsSession) ++controllerAdmissions;
    Require(pendingRetainsSession,
            "playback command snapshot churn replaced the sealed media session");
    pendingSurface.pendingCommand.reset();
    const bool acknowledgementRetainsSession =
        widgetrail::SameEmbeddedMediaResourceContract(
            admittedContract, pendingSurface);
    if (!acknowledgementRetainsSession) ++controllerAdmissions;
    Require(acknowledgementRetainsSession,
            "playback acknowledgement snapshot churn replaced the sealed media session");
    auto renamedSurface = pendingSurface;
    renamedSurface.accessibleName = L"Neutral media for the selected item";
    Require(widgetrail::SameEmbeddedMediaResourceContract(
                admittedContract, renamedSurface),
            "semantic accessible-name update replaced the sealed media session");
    auto replacementSurface = pendingSurface;
    replacementSurface.resources[0].path = L"media/replacement.html";
    Require(!widgetrail::SameEmbeddedMediaResourceContract(
                admittedContract, replacementSurface),
            "genuine sealed media resource replacement retained stale authority");
    if (!widgetrail::SameEmbeddedMediaResourceContract(
            admittedContract, replacementSurface)) ++controllerAdmissions;
    Require(controllerAdmissions == 2,
            "compatible updates or genuine replacement produced wrong controller count");
    Require(widgetrail::RetainEmbeddedMediaPresentation({
                true, true, true, true, 7, 8}) &&
            !widgetrail::RetainEmbeddedMediaPresentation({
                true, false, true, true, 7, 8}) &&
            !widgetrail::RetainEmbeddedMediaPresentation({
                true, true, false, true, 7, 8}) &&
            !widgetrail::RetainEmbeddedMediaPresentation({
                true, true, true, false, 7, 8}) &&
            !widgetrail::RetainEmbeddedMediaPresentation({
                true, true, true, true, 7, 7}),
            "compatible successor plane retention crossed an authority boundary");
    Require(widgetrail::OverlayCompositionSurface::ExternalContentCoordinates(
                widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Overlay) ==
                widgetrail::OverlayCompositionSurface::ExternalContentCoordinateSpace::
                    ContentLocal &&
            widgetrail::OverlayCompositionSurface::ExternalContentCoordinates(
                widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Pinned) ==
                widgetrail::OverlayCompositionSurface::ExternalContentCoordinateSpace::
                    EndpointLocal,
            "external media endpoint lost renderer-local coordinate ownership");
    State state;
    state.authority = {2, 3, 7, 11, 13, 3};
    State next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "exact current correlated event was rejected");
    Require(next.focusedElement == L"seek" && next.authority.eventSequence == 4 &&
            next.lastAcknowledgedCommandId == 17 &&
            next.focusedActionBoundsCurrent && next.focusedActionBounds.x == 100.0,
            "exact event authority was not retained");
    next = state;
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":6,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale session was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":2,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale surface was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":10,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale controller was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":12,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "stale document was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":18,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "wrong command correlation was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60},"script":"bad"})",
        state.authority, 3, 17, next), "unknown page field was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"seek","playing":false,"bounds":{"x":-1,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "invalid focused bounds were admitted");
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "valid document-local focus identity was rejected");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"invalid focus","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "invalid focus identity grammar was admitted");
    const std::wstring oversizedFocusJson =
        LR"({"type":"focus","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":")" +
        std::wstring(129, L'a') +
        LR"(","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})";
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        oversizedFocusJson, state.authority, 3, 17, next),
        "oversized document-local focus identity was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"script","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":false,"bounds":{"x":100,"y":40,"width":120,"height":60}})",
        state.authority, 3, 17, next), "unknown event kind was admitted");
    next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":true,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"aurora-tone-2","playbackState":"playing","positionSeconds":12,"durationSeconds":60,"volume":0.72})",
        state.authority, 3, 17, next),
        "typed provider-neutral playback event was rejected");
    Require(next.mediaKey == L"aurora-tone-2" && next.playing &&
            next.positionSeconds == 12.0 && next.durationSeconds == 60.0,
            "typed playback state was not retained exactly");
    next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":false,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"aurora-tone-2","playbackState":"paused","positionSeconds":12,"durationSeconds":60,"volume":0.72,"playbackRate":1.5,"muted":true,"loop":false})",
        state.authority, 3, 17, next),
        "typed playback preference terminal state was rejected");
    Require(next.playbackRate == 1.5 && next.muted && !next.loop,
            "typed playback preference state was not retained exactly");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":false,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"aurora-tone-2","playbackState":"paused","positionSeconds":12,"durationSeconds":60,"volume":0.72,"playbackRate":2.1,"muted":true,"loop":false})",
        state.authority, 3, 17, next),
        "out-of-range playback preference was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":17,"commandSequence":17,"focus":"aurora.primary","playing":false,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"aurora-tone-2","playbackState":"paused","positionSeconds":12,"durationSeconds":60,"volume":0.72,"playbackRate":1.5,"muted":true})",
        state.authority, 3, 17, next),
        "partial playback preference terminal state was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"media","environmentGeneration":2,"surfaceGeneration":3,"sessionGeneration":7,"controllerGeneration":11,"documentGeneration":13,"eventSequence":4,"commandId":18,"commandSequence":17,"focus":"cedar.primary","playing":true,"bounds":{"x":0,"y":0,"width":640,"height":360},"mediaKey":"cedar-tone","playbackState":"playing","positionSeconds":12,"durationSeconds":60,"volume":0.72})",
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
    const std::vector<std::wstring> allowedFrameFamilies{L"example.com"};
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
    for (const auto uri : {
            L"https://example.com/frame", L"https://media.example.com/frame",
            L"https://deep.media.example.com/asset"}) {
        Require(RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                    uri, {}, allowedFrameFamilies),
                "registrable family root or dot-boundary subdomain was denied");
    }
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameNavigation(
                L"https://media.example.com/frame", {}) &&
            RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameNavigation(
                L"https://frames.aurora.invalid/embed/index.html",
                allowedFrameOrigins),
            "resource-family authority leaked into exact-origin frame navigation");
    for (const auto uri : {
            L"http://media.example.com/frame",
            L"https://example.com.evil.test/frame",
            L"https://notexample.com/frame",
            L"https://user@example.com/frame",
            L"https://example.com:443/frame"}) {
        Require(!RichMediaSurfaceCoordinatorTestPeer::IsAllowedFrameResource(
                    uri, {}, allowedFrameFamilies),
                "scheme, authority, port, or label-confused family traffic was admitted");
    }
    const auto suffixAuthority = widgetrail::PublicSuffixDomainAuthority::LoadDefault();
    Require(suffixAuthority.available() &&
                suffixAuthority.IsRegistrableDomain(L"example.com") &&
                suffixAuthority.IsRegistrableDomain(L"example.co.uk") &&
                !suffixAuthority.IsRegistrableDomain(L"com") &&
                !suffixAuthority.IsRegistrableDomain(L"co.uk") &&
                !suffixAuthority.IsRegistrableDomain(L"deep.example.com") &&
                !suffixAuthority.IsRegistrableDomain(L"Example.com") &&
                !suffixAuthority.IsRegistrableDomain(L"éxample.com"),
            "PSL registrable-domain authority or canonical grammar drifted");
    const auto corruptPath = std::filesystem::temp_directory_path() /
        L"wrail-corrupt-public-suffix-list.dat";
    {
        std::ofstream corrupt(corruptPath, std::ios::binary | std::ios::trunc);
        corrupt << "// VERSION: corrupted\ncom\n";
    }
    const auto corruptAuthority = widgetrail::PublicSuffixDomainAuthority::Load(corruptPath);
    std::error_code ignored;
    std::filesystem::remove(corruptPath, ignored);
    Require(!corruptAuthority.available() &&
                !corruptAuthority.IsRegistrableDomain(L"example.com"),
            "missing or corrupt PSL authority did not fail closed");
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
    const auto manifestIdentity = RichMediaSurfaceCoordinatorTestPeer::
        ManifestAssemblyIdentity(
            LR"(<?xml version="1.0"?><assembly manifestVersion="1.0"><assemblyIdentity version="99.4.3.2" processorArchitecture="amd64" name="WidgetRail.OverlayHost" publicKeyToken="ignored" language="neutral"/></assembly>)");
    Require(manifestIdentity && *manifestIdentity == L"WidgetRail.OverlayHost",
            "trusted host manifest identity derivation included mutable metadata");
    const auto hostReferer = RichMediaSurfaceCoordinatorTestPeer::
        FormatInstalledAppReferer(*manifestIdentity);
    Require(hostReferer && *hostReferer == L"https://widgetrail.overlayhost/",
            "installed app identity did not produce the stable lowercase HTTPS referer");
    Require(RichMediaSurfaceCoordinatorTestPeer::SelectInstalledAppReferer(
                true, L"Store.WidgetRail", *manifestIdentity) ==
                std::optional<std::wstring>{L"https://store.widgetrail/"} &&
            RichMediaSurfaceCoordinatorTestPeer::SelectInstalledAppReferer(
                false, L"Store.WidgetRail", *manifestIdentity) == hostReferer &&
            !RichMediaSurfaceCoordinatorTestPeer::SelectInstalledAppReferer(
                true, L"invalid_package", *manifestIdentity),
            "package identity precedence or fail-closed selection drifted");
    for (const std::wstring_view invalidIdentity : {
            L"", L"WidgetRail_OverlayHost", L"WidgetRail..OverlayHost",
            L"-WidgetRail.OverlayHost", L"WidgetRail.OverlayHost-",
            L"WidgetRail.OverlayHost:443", L"WidgetRail/OverlayHost",
            L"WidgetRail@OverlayHost", L"*.WidgetRail"}) {
        Require(!RichMediaSurfaceCoordinatorTestPeer::FormatInstalledAppReferer(
                    invalidIdentity),
                "invalid installed app identity produced a referer");
    }
    std::wstring callerReferer{L"https://caller-controlled.invalid/"};
    unsigned int headerWrites{};
    const auto setHeader = [&](const wchar_t* name, const wchar_t* value) {
        Require(std::wstring_view{name} == L"Referer",
                "host referer policy mutated an unexpected header");
        callerReferer = value;
        ++headerWrites;
        return S_OK;
    };
    Require(RichMediaSurfaceCoordinatorTestPeer::ApplyInstalledAppReferer(
                L"https://frames.aurora.invalid/embed/index.html",
                allowedFrameOrigins, *hostReferer, setHeader) == 1 &&
            headerWrites == 1 && callerReferer == *hostReferer,
            "declared frame request did not replace caller referer with host identity");
    Require(RichMediaSurfaceCoordinatorTestPeer::ApplyInstalledAppReferer(
                L"https://video.example.com/embed", {}, *hostReferer,
                setHeader, allowedFrameFamilies) == 1 && headerWrites == 2 &&
            callerReferer == *hostReferer,
            "admitted domain-family traffic did not receive the host referer");
    for (const std::wstring_view uri : {
            L"https://wrail-media-aurora.invalid/media/index.html",
            L"https://undeclared.invalid/embed/index.html"}) {
        Require(RichMediaSurfaceCoordinatorTestPeer::ApplyInstalledAppReferer(
                    uri, allowedFrameOrigins, *hostReferer, setHeader) == 0 &&
                headerWrites == 2,
                "host referer leaked to local or undeclared resource traffic");
    }
    Require(RichMediaSurfaceCoordinatorTestPeer::ApplyInstalledAppReferer(
                L"https://frames.aurora.invalid/embed/index.html",
                allowedFrameOrigins, L"", setHeader) == 2 &&
            RichMediaSurfaceCoordinatorTestPeer::ApplyInstalledAppReferer(
                L"https://frames.aurora.invalid/embed/index.html",
                allowedFrameOrigins, *hostReferer,
                [](const wchar_t*, const wchar_t*) { return E_ACCESSDENIED; }) == 2,
            "missing identity or header mutation failure did not fail closed");
    Require(RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                7, L"aurora-tone", 7, L"aurora-tone") &&
            RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                0, L"aurora-tone", std::nullopt, L"") &&
            !RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                8, L"aurora-tone", 7, L"aurora-tone") &&
            !RichMediaSurfaceCoordinatorTestPeer::IsPlaybackCommandCorrelated(
                7, L"cedar-tone", 7, L"aurora-tone"),
            "typed playback command sequence/media-key correlation drifted");
    Require(RichMediaSurfaceCoordinatorTestPeer::ClassifyPlaybackCommandDispatch(
                Lifecycle::Absent) == PlaybackCommandDispatchResult::Rejected &&
            RichMediaSurfaceCoordinatorTestPeer::ClassifyPlaybackCommandDispatch(
                Lifecycle::Faulted) == PlaybackCommandDispatchResult::Rejected &&
            RichMediaSurfaceCoordinatorTestPeer::ClassifyPlaybackCommandDispatch(
                Lifecycle::EnvironmentCreating) ==
                PlaybackCommandDispatchResult::Deferred &&
            RichMediaSurfaceCoordinatorTestPeer::ClassifyPlaybackCommandDispatch(
                Lifecycle::Visible) == PlaybackCommandDispatchResult::Deferred &&
            RichMediaSurfaceCoordinatorTestPeer::ClassifyPlaybackCommandDispatch(
                Lifecycle::ReadyHidden) == PlaybackCommandDispatchResult::Deferred,
            "typed playback command terminal/deferred classification drifted");
    using Stage = PlaybackCommandStage;
    using Source = PlaybackTerminalSource;
    Require(CanPublishPlaybackTerminal(
                Stage::Accepted, Source::HostDispatchRejection) &&
            !CanPublishPlaybackTerminal(
                Stage::Dispatched, Source::HostDispatchRejection) &&
            CanPublishPlaybackTerminal(Stage::Dispatched, Source::Page) &&
            !CanPublishPlaybackTerminal(Stage::Accepted, Source::Page) &&
            CanPublishPlaybackTerminal(
                Stage::Accepted, Source::AuthorityRetirement) &&
            CanPublishPlaybackTerminal(
                Stage::Dispatched, Source::AuthorityRetirement) &&
            !CanPublishPlaybackTerminal(
                Stage::Terminal, Source::HostDispatchRejection) &&
            !CanPublishPlaybackTerminal(Stage::Terminal, Source::Page) &&
            !CanPublishPlaybackTerminal(
                Stage::Terminal, Source::AuthorityRetirement),
            "trusted playback terminal source crossed its exact prior-stage authority");
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
    auto family = aurora;
    family.allowedFrameDomainFamilies = {L"example.com"};
    Require(RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(family),
            "valid registrable domain family was rejected by native admission");
    for (const auto invalidFamily : {
            L"com", L"co.uk", L"deep.example.com", L"Example.com",
            L"example.com:443", L"*.example.com", L"127.0.0.1"}) {
        auto invalid = aurora;
        invalid.allowedFrameDomainFamilies = {invalidFamily};
        Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(invalid),
                "invalid or public-suffix family crossed native configuration admission");
    }
    auto tooManyFamilies = aurora;
    tooManyFamilies.allowedFrameDomainFamilies = std::vector<std::wstring>(
        widgetrail::protocol_contract::MaximumEmbeddedMediaFrameDomainFamilyCount + 1,
        L"example.com");
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(
                tooManyFamilies),
            "domain-family count cap was not enforced at native admission");
    auto oversizedFamily = aurora;
    oversizedFamily.allowedFrameDomainFamilies = {
        std::wstring(widgetrail::protocol_contract::MaximumEmbeddedMediaFrameDomainFamilyLength + 1,
            L'a')};
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(
                oversizedFamily),
            "domain-family item length cap was not enforced at native admission");
    auto aggregateFamilies = aurora;
    aggregateFamilies.allowedFrameDomainFamilies = {
        std::wstring(130, L'a'), std::wstring(130, L'b'),
        std::wstring(130, L'c'), std::wstring(130, L'd')};
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(
                aggregateFamilies),
            "domain-family aggregate length cap was not enforced at native admission");
    auto unsafe = aurora;
    unsafe.entryAsset = L"../credential.txt";
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(unsafe),
            "unsafe adapter entry path was admitted");
    auto oversized = cedar;
    oversized.resources.front().content.resize(256 * 1024 + 1);
    Require(!RichMediaSurfaceCoordinatorTestPeer::IsValidAdapterConfiguration(oversized),
            "oversized adapter resource was admitted");
    const auto requireRange = [](const std::wstring_view range,
                                 const int expectedStatus,
                                 const std::size_t expectedOffset,
                                 const std::size_t expectedLength,
                                 const std::wstring_view expectedContentRange) {
        const auto [status, offset, length, headers] =
            RichMediaSurfaceCoordinatorTestPeer::SealedResourcePlan(
                range, L"video/mp4", 10);
        Require(status == expectedStatus && offset == expectedOffset &&
                    length == expectedLength &&
                    headers.find(L"Accept-Ranges: bytes") != std::wstring::npos &&
                    headers.find(L"Content-Length: " +
                        std::to_wstring(expectedLength)) != std::wstring::npos &&
                    headers.find(expectedContentRange) != std::wstring::npos,
                "sealed media byte-range plan drifted");
    };
    requireRange(L"bytes=2-5", 206, 2, 4, L"Content-Range: bytes 2-5/10");
    requireRange(L"bytes=6-", 206, 6, 4, L"Content-Range: bytes 6-9/10");
    requireRange(L"bytes=-3", 206, 7, 3, L"Content-Range: bytes 7-9/10");
    const auto [fullStatus, fullOffset, fullLength, fullHeaders] =
        RichMediaSurfaceCoordinatorTestPeer::SealedResourcePlan(
            L"", L"video/mp4", 10);
    Require(fullStatus == 200 && fullOffset == 0 && fullLength == 10 &&
                fullHeaders.find(L"Accept-Ranges: bytes") != std::wstring::npos &&
                fullHeaders.find(L"Content-Range:") == std::wstring::npos,
            "sealed media no-range request lost the full 200 response");
    for (const std::wstring_view invalid : {
            L"bytes=", L"bytes=1-2,4-5", L"bytes=9-8", L"bytes=10-",
            L"bytes=-0", L"bytes=-11", L"bytes=0-10", L"items=0-1",
            L"bytes=184467440737095516160-"}) {
        const auto [status, offset, length, headers] =
            RichMediaSurfaceCoordinatorTestPeer::SealedResourcePlan(
                invalid, L"video/mp4", 10);
        Require(status == 416 && offset == 0 && length == 0 &&
                    headers.find(L"Content-Range: bytes */10") != std::wstring::npos &&
                    headers.find(L"Content-Length: 0") != std::wstring::npos,
                "malformed or unsatisfiable sealed media range was admitted");
    }
    const auto [htmlStatus, htmlOffset, htmlLength, htmlHeaders] =
        RichMediaSurfaceCoordinatorTestPeer::SealedResourcePlan(
            L"bytes=2-5", L"text/html", 10, false);
    Require(htmlStatus == 200 && htmlOffset == 0 && htmlLength == 10 &&
                htmlHeaders.find(L"Accept-Ranges") == std::wstring::npos,
            "non-media sealed resource acquired byte-range behavior");
    const std::array<std::uint8_t, 10> sealedBytes{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    const auto [sliceStatus, sliceOffset, sliceLength, sliceHeaders] =
        RichMediaSurfaceCoordinatorTestPeer::SealedResourcePlan(
            L"bytes=2-5", L"video/mp4", sealedBytes.size());
    const std::array<std::uint8_t, 4> expectedSlice{2, 3, 4, 5};
    Require(sliceStatus == 206 &&
                std::equal(sealedBytes.begin() + sliceOffset,
                           sealedBytes.begin() + sliceOffset + sliceLength,
                           expectedSlice.begin()),
            "sealed media byte-range plan escaped the admitted byte slice");
    POINT activationPoint{};
    Require(RichMediaSurfaceCoordinatorTestPeer::FocusedActionPoint(
                {100.0, 40.0, 120.0, 60.0}, {12, 18, 712, 438},
                activationPoint) &&
                activationPoint.x == 160 && activationPoint.y == 70,
            "1.5x DPI client-space activation point was raster-scaled");
    std::cout << "RichMediaSurfaceCoordinator contract cases passed=48\n";
}

void RunSharedEnvironmentRecoveryOwnerCases() {
    const auto sourcePath = std::filesystem::path{__FILE__}.parent_path() /
        "RichMediaSurfaceCoordinator.cpp";
    std::ifstream stream(sourcePath, std::ios::binary);
    Require(static_cast<bool>(stream),
            "shared environment owner source opens");
    const std::string source{
        std::istreambuf_iterator<char>(stream), {}};
    const auto section = [&](const std::string_view begin,
                             const std::string_view end) {
        const auto beginOffset = source.find(begin);
        Require(beginOffset != std::string::npos,
                "shared environment owner section begins");
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Require(endOffset != std::string::npos,
                "shared environment owner section ends");
        return source.substr(beginOffset, endOffset - beginOffset);
    };

    const auto initialize = section(
        "HRESULT RichMediaSurfaceCoordinator::Initialize(Configuration configuration) noexcept {",
        "HRESULT RichMediaSurfaceCoordinator::RecoverExitedSharedEnvironment() noexcept {");
    Require(initialize.find(
                "const HRESULT recovery = RecoverExitedSharedEnvironment();") !=
                std::string::npos &&
            initialize.find(
                "Fault(L\"shared-environment-exited-with-live-controller\", recovery);") !=
                std::string::npos &&
            initialize.find("if (FAILED(recovery))") <
                initialize.find("return S_OK;", initialize.find("if (FAILED(recovery))")) &&
            initialize.find(
                "if (shared.lifecycle == EnvironmentLifecycle::Creating)\n"
                "        return AwaitSharedEnvironment();") != std::string::npos &&
            initialize.find(
                "if (shared.lifecycle != EnvironmentLifecycle::Cold) return E_UNEXPECTED;\n"
                "    return BeginEnvironment();") != std::string::npos,
        "exited shared environment admission is terminalized once or advances through one explicit recovery path");
    Require(initialize.find(
                "shared.lifecycle == EnvironmentLifecycle::Ready &&\n"
                "        !shared.faulted && shared.environment") !=
                std::string::npos &&
            initialize.find(
                "!shared.signal || !shared.signal->browserProcessExited.load") !=
                std::string::npos &&
            initialize.find("environment_ = shared.environment;") !=
                std::string::npos &&
            initialize.find("state_.authority.environmentGeneration = shared.generation;") !=
                std::string::npos &&
            initialize.find("return BeginController();") != std::string::npos,
        "healthy shared environment and peer generation remain reusable without replacement");

    const auto recover = section(
        "HRESULT RichMediaSurfaceCoordinator::RecoverExitedSharedEnvironment() noexcept {",
        "HRESULT RichMediaSurfaceCoordinator::Retry(Configuration configuration) noexcept {");
    const auto ownerGuard = recover.find(
        "if (shared.liveControllerOwners != 0 || !shared.waiters.empty())");
    const auto shuttingDown = recover.find(
        "shared.lifecycle = EnvironmentLifecycle::ShuttingDown;");
    const auto cold = recover.find(
        "shared.lifecycle = EnvironmentLifecycle::Cold;", shuttingDown);
    Require(recover.find("shared.lifecycle != EnvironmentLifecycle::Ready") !=
                std::string::npos &&
            recover.find("!shared.signal") != std::string::npos &&
            recover.find("!shared.signal->browserProcessExited.load") !=
                std::string::npos &&
            recover.find("return S_FALSE;") != std::string::npos &&
            ownerGuard != std::string::npos &&
            recover.find("return HRESULT_FROM_WIN32(ERROR_BUSY);", ownerGuard) !=
                std::string::npos,
        "recovery requires an exited Ready owner and refuses live controllers or waiters");
    Require(shuttingDown != std::string::npos && cold != std::string::npos &&
            shuttingDown < cold &&
            recover.find("environmentProfileDirectory_ = shared.profileDirectory;",
                         shuttingDown) < cold &&
            recover.find("shared.environment5.Reset();", shuttingDown) < cold &&
            recover.find("shared.environment.Reset();", shuttingDown) < cold &&
            recover.find("MarkCurrentProfileForDeferredCleanup();", shuttingDown) < cold &&
            recover.find("shared.signal.reset();", shuttingDown) < cold &&
            recover.find("shared.profileDirectory.clear();", shuttingDown) < cold &&
            recover.find("shared.faulted = false;", shuttingDown) < cold &&
            recover.find("return S_OK;", cold) != std::string::npos,
        "last-controller recovery retires the exited environment/profile and returns one clean Cold owner");

    const auto beginEnvironment = section(
        "HRESULT RichMediaSurfaceCoordinator::BeginEnvironment() noexcept {",
        "HRESULT RichMediaSurfaceCoordinator::AwaitSharedEnvironment() noexcept {");
    Require(beginEnvironment.find(
                "shared.lifecycle != EnvironmentLifecycle::Cold") !=
                std::string::npos &&
            beginEnvironment.find("shared.generation++;") != std::string::npos &&
            beginEnvironment.find(
                "L\"wrail-rich-media-\" + std::to_wstring(GetCurrentProcessId()) + L\"-\" +\n"
                "          std::to_wstring(shared.generation)") != std::string::npos &&
            beginEnvironment.find(
                "state_.authority.environmentGeneration = shared.generation;") !=
                std::string::npos &&
            beginEnvironment.find("CreateCoreWebView2EnvironmentWithOptions(") !=
                std::string::npos,
        "recovered Cold admission advances generation and authors one fresh profile/environment create");

    const auto controllerCreated = section(
        "HRESULT RichMediaSurfaceCoordinator::OnControllerCreated(",
        "HRESULT RichMediaSurfaceCoordinator::ConfigureCore() noexcept {");
    const auto teardown = section(
        "void RichMediaSurfaceCoordinator::BeginSessionTeardown() noexcept {",
        "void RichMediaSurfaceCoordinator::CompleteSessionTeardown() noexcept {");
    Require(controllerCreated.find("if (!ownsSharedController_)") !=
                std::string::npos &&
            controllerCreated.find("++sharedEnvironment_->liveControllerOwners;") !=
                std::string::npos &&
            controllerCreated.find("ownsSharedController_ = true;") !=
                std::string::npos &&
            teardown.find("if (ownsSharedController_)") != std::string::npos &&
            teardown.find("--sharedEnvironment_->liveControllerOwners;") !=
                std::string::npos &&
            teardown.find("ownsSharedController_ = false;") != std::string::npos,
        "controller ownership is counted once and released at exact session teardown");

    const auto retry = section(
        "HRESULT RichMediaSurfaceCoordinator::Retry(Configuration configuration) noexcept {",
        "std::shared_ptr<RichMediaSurfaceCoordinator::CallbackLease>");
    Require(retry.find("if (sharedEnvironment_->faulted ||") !=
                std::string::npos &&
            retry.find("sharedEnvironment_->signal->browserProcessExited.load") !=
                std::string::npos &&
            retry.find("return E_UNEXPECTED;") != std::string::npos,
        "Retry cannot relabel a faulted or exited shared browser as healthy");
    std::cout << "RichMedia shared-environment recovery owner cases passed=6\n";
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

void RunExternalPresentationCommitCases() {
    using Surface = widgetrail::OverlayCompositionSurface;
    using Endpoint = Surface::ExternalContentEndpoint;

    WNDCLASSW windowClass{};
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.lpfnWndProc = WindowProc;
    windowClass.lpszClassName = L"WidgetRail.ExternalPresentationTests";
    RegisterClassW(&windowClass);
    const auto createWindow = [&] {
        return CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP,
            windowClass.lpszClassName, L"", WS_POPUP,
            0, 0, 900, 640, nullptr, nullptr, windowClass.hInstance, nullptr);
    };
    const HWND overlayWindow = createWindow();
    const HWND pinnedWindow = createWindow();
    const HWND replacementPinnedWindow = createWindow();
    Require(overlayWindow && pinnedWindow && replacementPinnedWindow,
            "external presentation owner HWND creation failed");

    ComPtr<ID2D1Factory1> factory;
    Require(SUCCEEDED(D2D1CreateFactory(
                D2D1_FACTORY_TYPE_SINGLE_THREADED,
                factory.ReleaseAndGetAddressOf())),
            "external presentation D2D factory creation failed");
    Surface composition;
    std::wstring error;
    Require(composition.Initialize(overlayWindow, factory.Get(), error) &&
                composition.InitializePinnedExternalContentEndpoint(
                    pinnedWindow, error),
            "external presentation endpoints did not initialize");

    const RECT overlayBounds{24, 36, 664, 396};
    const RECT overlayClip{24, 36, 664, 396};
    ComPtr<IUnknown> target;
    Require(SUCCEEDED(composition.CreateExternalContentTarget(
                Endpoint::Overlay, &target)),
            "overlay external target creation failed");
    Surface::CommitTiming timing;
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, overlayBounds, overlayClip, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "initial overlay external presentation was not committed");
    auto overlayCounters = composition.externalContentCommitCounters(
        Endpoint::Overlay);
    Require(overlayCounters.requested == 1 && overlayCounters.committed == 1,
            "initial overlay commit counters were incorrect");
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, overlayBounds, overlayClip, true, timing) == S_FALSE &&
                !timing.externalPresentationCommitted,
            "identical overlay presentation mutated DirectComposition");
    overlayCounters = composition.externalContentCommitCounters(Endpoint::Overlay);
    Require(overlayCounters.requested == 2 && overlayCounters.committed == 1,
            "identical overlay presentation incremented the commit count");

    const RECT resizedBounds{24, 36, 704, 416};
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, resizedBounds, resizedBounds, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "genuine overlay resize did not permit a new commit");
    const RECT dpiResolvedBounds{30, 45, 880, 520};
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, dpiResolvedBounds, dpiResolvedBounds, true, timing) ==
                    S_OK && timing.externalPresentationCommitted,
            "genuine DPI-resolved overlay geometry did not permit a new commit");

    Require(SUCCEEDED(composition.DetachExternalContentTarget(
                Endpoint::Overlay, timing)) && timing.waitedForCompletion,
            "overlay endpoint did not detach before transfer");
    target.Reset();
    Require(SUCCEEDED(composition.CreateExternalContentTarget(
                Endpoint::Pinned, &target)),
            "pinned external target creation failed");
    const RECT pinnedBounds{16, 20, 656, 380};
    const auto pinnedBefore = composition.externalContentCommitCounters(
        Endpoint::Pinned);
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "overlay-to-pinned transfer did not commit the pinned endpoint");
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_FALSE &&
                !timing.externalPresentationCommitted,
            "identical pinned presentation mutated DirectComposition");
    auto pinnedAfter = composition.externalContentCommitCounters(Endpoint::Pinned);
    Require(pinnedAfter.requested == pinnedBefore.requested + 2 &&
                pinnedAfter.committed == pinnedBefore.committed + 1,
            "overlay-to-pinned transfer did not produce exactly one genuine commit");

    Surface::PinnedMediaChromePresentation chrome{
        pinnedBounds, true, true, false, 0.4,
        D2D1::ColorF(0.02F, 0.03F, 0.05F, 0.8F),
        D2D1::ColorF(0.20F, 0.24F, 0.30F, 1.0F),
        D2D1::ColorF(0.30F, 0.70F, 0.95F, 1.0F),
        D2D1::ColorF(0.55F, 0.80F, 1.0F, 1.0F)};
    Require(composition.CommitPinnedMediaChrome(chrome, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "compact pinned chrome was not committed above the retained media visual");
    Require(composition.CommitPinnedMediaChrome(chrome, timing) == S_FALSE &&
                !timing.externalPresentationCommitted,
            "identical compact chrome presentation was not a no-op");
    const auto pinnedBeforeChromeNoop = composition.externalContentCommitCounters(
        Endpoint::Pinned);
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_FALSE &&
                !timing.externalPresentationCommitted,
            "ordinary compact chrome update detached or recommitted the media visual");
    const auto pinnedAfterChromeNoop = composition.externalContentCommitCounters(
        Endpoint::Pinned);
    Require(pinnedAfterChromeNoop.requested == pinnedBeforeChromeNoop.requested + 1 &&
                pinnedAfterChromeNoop.committed == pinnedBeforeChromeNoop.committed,
            "ordinary compact chrome update caused external-media commit churn");

    Require(SUCCEEDED(composition.DetachExternalContentTarget(
                Endpoint::Pinned, timing)) && timing.waitedForCompletion,
            "pinned media visual did not detach for the reattachment case");
    target.Reset();
    Require(SUCCEEDED(composition.CreateExternalContentTarget(
                Endpoint::Pinned, &target)),
            "pinned media target did not recreate under retained compact chrome");
    const auto pinnedBeforeReattach = composition.externalContentCommitCounters(
        Endpoint::Pinned);
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "later pinned media reattachment did not commit behind retained chrome");
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_FALSE &&
                !timing.externalPresentationCommitted,
            "identical reattached media presentation was not a no-op");
    pinnedAfter = composition.externalContentCommitCounters(Endpoint::Pinned);
    Require(pinnedAfter.requested == pinnedBeforeReattach.requested + 2 &&
                pinnedAfter.committed == pinnedBeforeReattach.committed + 1,
            "pinned media reattachment produced more than one genuine commit");

    Require(SUCCEEDED(composition.DetachExternalContentTarget(
                Endpoint::Pinned, timing)) && timing.waitedForCompletion,
            "pinned endpoint did not detach before transfer back");
    target.Reset();
    Require(SUCCEEDED(composition.CreateExternalContentTarget(
                Endpoint::Overlay, &target)),
            "overlay target recreation failed");
    const auto overlayBeforeReturn = composition.externalContentCommitCounters(
        Endpoint::Overlay);
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, overlayBounds, overlayClip, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "pinned-to-overlay transfer did not commit the overlay endpoint");
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Overlay, overlayBounds, overlayClip, true, timing) == S_FALSE,
            "identical transferred overlay presentation was not a no-op");
    overlayCounters = composition.externalContentCommitCounters(Endpoint::Overlay);
    Require(overlayCounters.requested == overlayBeforeReturn.requested + 2 &&
                overlayCounters.committed == overlayBeforeReturn.committed + 1,
            "pinned-to-overlay transfer did not produce exactly one genuine commit");

    Require(SUCCEEDED(composition.DetachExternalContentTarget(
                Endpoint::Overlay, timing)) && timing.waitedForCompletion,
            "returned overlay endpoint did not detach");
    target.Reset();
    Require(SUCCEEDED(composition.ReleasePinnedExternalContentEndpoint(timing)) &&
                timing.waitedForCompletion &&
                composition.InitializePinnedExternalContentEndpoint(
                    replacementPinnedWindow, error),
            "pinned endpoint owner replacement failed");
    Require(SUCCEEDED(composition.CreateExternalContentTarget(
                Endpoint::Pinned, &target)),
            "replacement pinned external target creation failed");
    const auto pinnedBeforeReplacement = composition.externalContentCommitCounters(
        Endpoint::Pinned);
    Require(composition.CommitExternalContentPresentation(
                Endpoint::Pinned, pinnedBounds, pinnedBounds, true, timing) == S_OK &&
                timing.externalPresentationCommitted,
            "genuine pinned owner/target invalidation did not permit a new commit");
    pinnedAfter = composition.externalContentCommitCounters(Endpoint::Pinned);
    Require(pinnedAfter.committed == pinnedBeforeReplacement.committed + 1,
            "replacement pinned owner did not produce one genuine commit");

    Require(SUCCEEDED(composition.DetachExternalContentTarget(
                Endpoint::Pinned, timing)) && timing.waitedForCompletion,
            "replacement pinned endpoint did not detach");
    target.Reset();
    Require(SUCCEEDED(composition.ReleasePinnedExternalContentEndpoint(timing)) &&
                timing.waitedForCompletion,
            "replacement pinned endpoint did not release");
    composition.Reset();
    factory.Reset();
    DestroyWindow(replacementPinnedWindow);
    DestroyWindow(pinnedWindow);
    DestroyWindow(overlayWindow);
    std::cout << "Rich media external presentation cases passed=12\n";
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
        const auto presentationApplications = fixture.presentationApplications();
        Require(fixture.coordinator().SetVisible(true) == S_FALSE &&
                    fixture.presentationApplications() == presentationApplications,
                "identical visible state touched the WebView/DComp presentation path");
        Require(fixture.coordinator().UpdateGeometry(
                    {0, 0, 800, 520}, 1.0) == S_FALSE,
                "identical controller geometry mutated the WebView presentation");
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

        const auto localFaultAuthority = fixture.coordinator().state().authority;
        RichMediaSurfaceCoordinatorTestPeer::MarkEnvironmentFaulted(fixture.coordinator());
        RichMediaSurfaceCoordinatorTestPeer::Fault(
            fixture.coordinator(), L"proof-local-environment-fault");
        Require(SUCCEEDED(fixture.Retry()), "local-fault Retry submission failed");
        RequireReady(fixture, "local-fault Retry did not recreate a ready session");
        RequireRetainedEnvironment(initialEnvironment,
            fixture.coordinator().environmentState(),
            "local-fault Retry replaced the healthy shared environment");
        const auto localRetryAuthority = fixture.coordinator().state().authority;
        Require(localRetryAuthority.environmentGeneration ==
                    localFaultAuthority.environmentGeneration &&
                localRetryAuthority.surfaceGeneration ==
                    localFaultAuthority.surfaceGeneration &&
                localRetryAuthority.sessionGeneration >
                    localFaultAuthority.sessionGeneration &&
                localRetryAuthority.controllerGeneration >
                    localFaultAuthority.controllerGeneration &&
                localRetryAuthority.documentGeneration >
                    localFaultAuthority.documentGeneration,
                "local-fault Retry did not retain environment/surface and refresh session authority");
        finalProfile = initialEnvironment.profileDirectory;
    }
    const auto marker = finalProfile /
        (L".wrail-retired-owner-" + std::to_wstring(GetCurrentProcessId()));
    Require(std::filesystem::exists(marker),
            "final shutdown did not mark the process-lifetime profile for deferred cleanup");

    const auto failedRoot = root / L"shared-browser-failure";
    {
        Fixture failed(2, true, failedRoot, L"aurora-adapter");
        RequireReady(failed, "shared-browser failure fixture did not become ready",
                     L"aurora.primary");
        const auto failedEnvironment = failed.coordinator().environmentState();
        RichMediaSurfaceCoordinatorTestPeer::RecordLiveUnexpectedBrowserExit(
            failed.coordinator(), failedEnvironment.browserProcessId);
        RichMediaSurfaceCoordinatorTestPeer::Fault(
            failed.coordinator(), L"proof-shared-browser-exit");
        Require(failed.Retry() == E_UNEXPECTED,
                "shared browser-exit authority incorrectly permitted Retry");
    }
    {
        Fixture recreated(3, true, root / L"shared-browser-recreated",
                          L"aurora-adapter");
        RequireReady(recreated, "fresh owner did not recreate after shared browser failure",
                     L"aurora.primary");
        const auto environment = recreated.coordinator().environmentState();
        Require(environment.lifecycle == EnvironmentLifecycle::Ready &&
                    environment.browserProcessId > 0 && environment.observerActive &&
                    !environment.browserExitObserved && !environment.faulted,
                "fresh owner retained shared browser-failure authority");
    }
    std::cout << "RichMediaSurfaceCoordinator lifecycle cases passed=26\n";
}

void RunSharedEnvironmentAdmissionCases() {
    using namespace widgetrail::richmedia;
    const auto root = std::filesystem::temp_directory_path() /
        (L"wrail-rich-media-shared-environment-" +
         std::to_wstring(GetCurrentProcessId()));
    const auto retiredProbe = std::make_shared<FixtureCallbackProbe>();
    Fixture environmentOwner(29, true, root, L"aurora-adapter");
    RequireReady(environmentOwner, "shared environment owner did not become ready",
                 L"aurora.primary");
    const auto initialEnvironment = environmentOwner.coordinator().environmentState();
    const auto environment = RichMediaSurfaceCoordinatorTestPeer::SharedEnvironment(
        environmentOwner.coordinator());
    Require(SUCCEEDED(environmentOwner.CloseSession()) &&
                environmentOwner.finalDetachSucceededAndWaited(),
            "shared environment owner session did not retire cleanly");
    RichMediaSurfaceCoordinatorTestPeer::HoldSharedEnvironmentCreating(
        environmentOwner.coordinator());

    Fixture aurora(30, true, root, L"aurora-adapter", {}, {}, environment, true);
    Require(aurora.coordinator().state().lifecycle == Lifecycle::EnvironmentCreating &&
                aurora.sawDiagnostic(L"environment-waiting"),
            "first overlapping media session was not retained as an environment waiter");
    Fixture cedar(31, true, root, L"cedar-adapter", {}, {}, environment, true);
    Require(cedar.coordinator().state().lifecycle == Lifecycle::EnvironmentCreating &&
                cedar.sawDiagnostic(L"environment-waiting"),
            "overlapping media session was not retained as an environment waiter");
    Fixture retired(32, true, root, L"aurora-adapter", {}, retiredProbe, environment, true);
    Require(retired.coordinator().state().lifecycle == Lifecycle::EnvironmentCreating &&
                retired.sawDiagnostic(L"environment-waiting"),
            "retirement fixture was not admitted while the environment was creating");
    Require(SUCCEEDED(retired.CloseSession()) && retired.finalDetachSucceededAndWaited(),
            "waiting media session did not retire cleanly before environment readiness");
    retired.RetireCallbackProbe();
    RichMediaSurfaceCoordinatorTestPeer::ReleaseSharedEnvironmentReady(
        environmentOwner.coordinator());
    Require(SUCCEEDED(aurora.CommitInitialPresentation()) &&
                SUCCEEDED(cedar.CommitInitialPresentation()),
            "shared-environment fixtures did not commit their initial targets");

    RequireReady(aurora, "environment creator did not become ready", L"aurora.primary");
    RequireReady(cedar, "retained environment waiter did not resume", L"cedar.primary");
    const auto auroraEnvironment = aurora.coordinator().environmentState();
    const auto cedarEnvironment = cedar.coordinator().environmentState();
    Require(auroraEnvironment.lifecycle == EnvironmentLifecycle::Ready &&
                cedarEnvironment.lifecycle == EnvironmentLifecycle::Ready &&
                auroraEnvironment.generation == cedarEnvironment.generation &&
                auroraEnvironment.profileDirectory == cedarEnvironment.profileDirectory &&
                auroraEnvironment.browserProcessId != 0 &&
                auroraEnvironment.browserProcessId == cedarEnvironment.browserProcessId &&
                auroraEnvironment.generation == initialEnvironment.generation &&
                cedar.countDiagnostic(L"environment-resumed") == 1 &&
                aurora.countDiagnostic(L"environment-resumed") == 1 &&
                aurora.countDiagnostic(L"controller-creating") == 1 &&
                cedar.countDiagnostic(L"controller-creating") == 1,
            "overlapping sessions did not share one environment and create one controller each");
    Require(retired.callbacksAfterRetirement() == 0 &&
                retired.coordinator().state().lifecycle == Lifecycle::Absent,
            "retired environment waiter received a readiness callback");

    const auto auroraCommand = aurora.coordinator().state().lastAcknowledgedCommandId;
    const auto cedarCommand = cedar.coordinator().state().lastAcknowledgedCommandId;
    Require(aurora.coordinator().SendCommand(Command::NavigateNext) &&
                cedar.coordinator().SendCommand(Command::NavigateNext),
            "shared-environment sessions did not accept independent commands");
    Require(PumpUntil([&] {
        return aurora.coordinator().state().lastAcknowledgedCommandId > auroraCommand &&
            aurora.coordinator().state().focusedElement == L"aurora.secondary" &&
            cedar.coordinator().state().lastAcknowledgedCommandId > cedarCommand &&
            cedar.coordinator().state().focusedElement == L"cedar.secondary";
    }, 2s), "shared-environment sessions did not publish independent events");

    const auto cedarAuthority = cedar.coordinator().state().authority;
    RichMediaSurfaceCoordinatorTestPeer::MarkEnvironmentFaulted(aurora.coordinator());
    RichMediaSurfaceCoordinatorTestPeer::Fault(
        aurora.coordinator(), L"proof-local-environment-fault");
    Require(SUCCEEDED(aurora.Retry()),
            "locally faulted coordinator did not retry on the healthy shared environment");
    RequireReady(aurora, "locally faulted coordinator did not become ready after Retry",
                 L"aurora.primary");
    const auto cedarAfterRetry = cedar.coordinator().state();
    Require(cedarAfterRetry.lifecycle == Lifecycle::Visible &&
                cedarAfterRetry.authority.environmentGeneration ==
                    cedarAuthority.environmentGeneration &&
                cedarAfterRetry.authority.surfaceGeneration ==
                    cedarAuthority.surfaceGeneration &&
                cedarAfterRetry.authority.sessionGeneration ==
                    cedarAuthority.sessionGeneration &&
                cedarAfterRetry.authority.controllerGeneration ==
                    cedarAuthority.controllerGeneration &&
                cedarAfterRetry.authority.documentGeneration ==
                    cedarAuthority.documentGeneration,
            "local Retry mutated the separate live shared-environment coordinator");
    const auto cedarAfterRetryCommand = cedarAfterRetry.lastAcknowledgedCommandId;
    Require(cedar.coordinator().SendCommand(Command::NavigatePrevious) &&
                PumpUntil([&] {
                    return cedar.coordinator().state().lastAcknowledgedCommandId >
                            cedarAfterRetryCommand &&
                        cedar.coordinator().state().focusedElement == L"cedar.primary";
                }, 2s),
            "separate live coordinator stopped dispatching after peer-local Retry");
    std::cout << "RichMediaSurfaceCoordinator shared environment cases passed=17\n";
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

    wchar_t modulePath[MAX_PATH]{};
    Require(GetModuleFileNameW(nullptr, modulePath, MAX_PATH) != 0,
            "sample adapter test could not resolve its executable path");
    const auto samplePath = std::filesystem::path{modulePath}.parent_path() /
        L"runtime" / L"EmbeddedMediaSample" / L"payload" / L"media" /
        L"adapter.html";
    std::ifstream sampleStream(samplePath, std::ios::binary);
    Require(sampleStream.good(), "built sample adapter was unavailable");
    const std::vector<std::uint8_t> sampleBytes(
        std::istreambuf_iterator<char>{sampleStream},
        std::istreambuf_iterator<char>{});
    Require(!sampleBytes.empty(), "built sample adapter was empty");
    constexpr std::wstring_view primaryMediaKey = L"aurora-video-0";
    constexpr std::wstring_view secondaryMediaKey = L"horizon-video-1";
    const std::string sampleAdapter(sampleBytes.begin(), sampleBytes.end());
    const std::regex primaryMediaDeclaration{
        R"(\[\s*"aurora-video-0"\s*,\s*"sample\.mp4"\s*\])"};
    const std::regex secondaryMediaDeclaration{
        R"(\[\s*"horizon-video-1"\s*,\s*"horizon\.mp4"\s*\])"};
    Require(std::regex_search(sampleAdapter, primaryMediaDeclaration) &&
                std::regex_search(sampleAdapter, secondaryMediaDeclaration),
            "built sample adapter did not declare the expected media keys");
    const auto adapterRuntimePath = samplePath.parent_path() /
        L"adapter-runtime.js";
    std::ifstream adapterRuntimeStream(adapterRuntimePath, std::ios::binary);
    Require(adapterRuntimeStream.good(),
            "built sample adapter runtime was unavailable");
    const std::vector<std::uint8_t> adapterRuntimeBytes(
        std::istreambuf_iterator<char>{adapterRuntimeStream},
        std::istreambuf_iterator<char>{});
    Require(!adapterRuntimeBytes.empty(),
            "built sample adapter runtime was empty");
    const auto videoPath = samplePath.parent_path() / L"sample.mp4";
    std::ifstream videoStream(videoPath, std::ios::binary);
    Require(videoStream.good(), "built sample sealed video asset was unavailable");
    const std::vector<std::uint8_t> videoBytes(
        std::istreambuf_iterator<char>{videoStream},
        std::istreambuf_iterator<char>{});
    Require(!videoBytes.empty(), "built sample sealed video asset was empty");
    const auto horizonVideoPath = samplePath.parent_path() / L"horizon.mp4";
    std::ifstream horizonVideoStream(horizonVideoPath, std::ios::binary);
    Require(horizonVideoStream.good(),
            "built sample secondary sealed video asset was unavailable");
    const std::vector<std::uint8_t> horizonVideoBytes(
        std::istreambuf_iterator<char>{horizonVideoStream},
        std::istreambuf_iterator<char>{});
    Require(!horizonVideoBytes.empty(),
            "built sample secondary sealed video asset was empty");
    std::string forcedProgressAdapter(sampleBytes.begin(), sampleBytes.end());
    std::string forcedProgressRuntime(
        adapterRuntimeBytes.begin(), adapterRuntimeBytes.end());
    const auto inject = [&](std::string& target,
                            const std::string_view marker,
                            const std::string_view replacement) {
        const auto offset = target.find(marker);
        Require(offset != std::string::npos,
                "built adapter progress-boundary seam was unavailable");
        target.replace(offset, marker.size(), replacement);
    };
    inject(forcedProgressRuntime,
           "      inFlight = operation;",
           "      inFlight = operation;\n"
           "      global.document.querySelector('video')?.dispatchEvent("
           "new Event('timeupdate'));");
    inject(forcedProgressAdapter,
           "  void playMedia(activation.signal).then(activation.resolve, "
           "activation.reject);",
           "  media.dispatchEvent(new Event('timeupdate'));\n"
           "  void playMedia(activation.signal).then(activation.resolve, "
           "activation.reject);");
    const std::vector<std::uint8_t> forcedProgressBytes(
        forcedProgressAdapter.begin(), forcedProgressAdapter.end());
    const std::vector<std::uint8_t> forcedProgressRuntimeBytes(
        forcedProgressRuntime.begin(), forcedProgressRuntime.end());
    const auto forcedCallbackProbe = std::make_shared<FixtureCallbackProbe>();
    {
        Fixture forcedProgress(ordinal++, true, {}, {}, {
            {L"payload/media/adapter.html", L"text/html", forcedProgressBytes},
            {L"payload/media/adapter-runtime.js", L"application/javascript",
             forcedProgressRuntimeBytes},
            {L"payload/media/sample.mp4", L"video/mp4", videoBytes},
            {L"payload/media/horizon.mp4", L"video/mp4", horizonVideoBytes},
        }, forcedCallbackProbe);
        RequireReady(forcedProgress,
                     "forced-progress adapter did not become ready", L"media-plane");
        const auto zeroEventsBefore = std::count_if(
            forcedProgress.playbackEvents().begin(), forcedProgress.playbackEvents().end(),
            [](const PlaybackEvent& event) { return event.commandSequence == 0; });
        Require(forcedProgress.coordinator().SendPlaybackCommand({
                    71, PlaybackCommandKind::Play, L"aurora-video-0",
                    std::nullopt, std::nullopt}),
                "forced-progress Play command was rejected");
        Require(PumpUntil([&] {
            return std::any_of(
                forcedProgress.playbackEvents().begin(),
                forcedProgress.playbackEvents().end(),
                [](const PlaybackEvent& event) {
                    return event.commandSequence == 71 && event.state == L"playing";
                });
        }, 5s), "forced progress escaped the arm-to-Play command lifetime");
        const auto zeroEventsAfter = std::count_if(
            forcedProgress.playbackEvents().begin(), forcedProgress.playbackEvents().end(),
            [](const PlaybackEvent& event) { return event.commandSequence == 0; });
        Require(zeroEventsAfter == zeroEventsBefore &&
                    forcedProgress.coordinator().state().lifecycle == Lifecycle::Visible,
                "zero-ID playback publication escaped an in-flight command");
        Require(SUCCEEDED(forcedProgress.CloseSession()) &&
                    forcedProgress.finalDetachSucceededAndWaited(),
                "forced-progress controller did not detach cleanly");
        const auto teardown = forcedProgress.coordinator().sessionTeardownResult();
        Require(teardown.sessionOwnersEmpty && !teardown.callbackDeadlineExpired,
                "forced-progress controller or callback stream remained active");
        forcedProgress.RetireCallbackProbe();
    }
    Fixture sample(ordinal++, true, {}, {}, {
        {L"payload/media/adapter.html", L"text/html", sampleBytes},
        {L"payload/media/adapter-runtime.js", L"application/javascript",
         adapterRuntimeBytes},
        {L"payload/media/sample.mp4", L"video/mp4", videoBytes},
        {L"payload/media/horizon.mp4", L"video/mp4", horizonVideoBytes},
    });
    RequireReady(sample, "built sample adapter did not become ready", L"media-plane");
    Require(forcedCallbackProbe->callbacksAfterRetirement.load(
                std::memory_order_acquire) == 0,
            "destroyed forced-progress callback sink reached the ordinary fixture");
    auto requirePlayback = [&](const std::uint64_t sequence,
                               const std::wstring_view expectedKey,
                               const std::wstring_view expectedState) {
        Require(PumpUntil([&] {
            return std::any_of(
                sample.playbackEvents().begin(), sample.playbackEvents().end(),
                [&](const PlaybackEvent& event) {
                    return event.commandSequence == sequence;
                });
        }, 5s), "built sample did not acknowledge a typed playback command");
        const auto found = std::find_if(
            sample.playbackEvents().begin(), sample.playbackEvents().end(),
            [&](const PlaybackEvent& event) {
                return event.commandSequence == sequence;
            });
        if (found != sample.playbackEvents().end()) {
            std::wcout << L"built-sample-event command=" << found->commandSequence
                       << L" mediaKey=" << found->mediaKey
                       << L" state=" << found->state
                       << L" error=" << (found->errorCode.empty()
                            ? L"<none>" : found->errorCode)
                       << L" duration=" << found->durationSeconds
                       << L" position=" << found->positionSeconds
                       << L" volume=" << found->volume
                       << L" rate=" << found->playbackRate
                       << L" muted=" << found->muted
                       << L" loop=" << found->loop << L'\n' << std::flush;
        }
        Require(found != sample.playbackEvents().end() &&
                    found->mediaKey == expectedKey &&
                    found->state == expectedState && found->errorCode.empty() &&
                    found->volume > 0.0 && found->volume <= 1.0,
                "built sample acknowledged playback with false or muted state");
    };
    const auto sendPlayback = [&](const std::uint64_t sequence,
                                  const PlaybackCommandKind kind,
                                  const std::wstring_view key,
                                  const std::optional<double> position = std::nullopt) {
        Require(sample.coordinator().SendPlaybackCommand({
                    sequence, kind, std::wstring{key}, position, std::nullopt}),
                "built sample rejected a consecutive typed playback command");
        Require(sample.coordinator().DispatchPlaybackCommand({
                    sequence + 1000, PlaybackCommandKind::Pause,
                    std::wstring{key}}) == PlaybackCommandDispatchResult::Deferred,
                "in-flight typed command admitted a duplicate dispatch");
    };
    sendPlayback(1, PlaybackCommandKind::Play, primaryMediaKey);
    requirePlayback(1, primaryMediaKey, L"playing");
    Require(PumpUntil([&] {
        return RichMediaSurfaceCoordinatorTestPeer::DocumentAudioOutputActive(
            sample.coordinator());
    }, 5s), "built sample did not produce active unmuted document audio");
    const auto retainedHiddenAuthority = sample.coordinator().state().authority;
    Require(SUCCEEDED(sample.SuspendPresentation()) &&
                sample.finalDetachSucceededAndWaited() &&
                sample.coordinator().presentationTransferPending() &&
                sample.coordinator().state().lifecycle == Lifecycle::ReadyHidden &&
                !sample.coordinator().state().inputEnabled &&
                !sample.presentationVisible(),
            "retained-hidden suspension did not detach presentation authority");
    Require(!sample.coordinator().SendCommand(Command::NavigateNext) &&
                !sample.coordinator().ForwardKey(WM_KEYDOWN, VK_RETURN, 0),
            "retained-hidden session admitted raw controller input");
    sendPlayback(2, PlaybackCommandKind::Pause, primaryMediaKey);
    requirePlayback(2, primaryMediaKey, L"paused");
    const auto hiddenTerminalAuthority = sample.coordinator().state().authority;
    Require(hiddenTerminalAuthority.environmentGeneration ==
                retainedHiddenAuthority.environmentGeneration &&
            hiddenTerminalAuthority.surfaceGeneration ==
                retainedHiddenAuthority.surfaceGeneration &&
            hiddenTerminalAuthority.sessionGeneration ==
                retainedHiddenAuthority.sessionGeneration &&
            hiddenTerminalAuthority.controllerGeneration ==
                retainedHiddenAuthority.controllerGeneration &&
            hiddenTerminalAuthority.documentGeneration ==
                retainedHiddenAuthority.documentGeneration,
            "hidden typed command changed controller/document/session authority");
    Require(SUCCEEDED(sample.ResumePresentation()) &&
                !sample.coordinator().presentationTransferPending() &&
                sample.coordinator().state().lifecycle == Lifecycle::Visible &&
                sample.coordinator().state().inputEnabled &&
                sample.presentationVisible(),
            "exact MediaViewport return did not reattach the retained session");
    const auto reattachedAuthority = sample.coordinator().state().authority;
    Require(reattachedAuthority.environmentGeneration ==
                retainedHiddenAuthority.environmentGeneration &&
            reattachedAuthority.surfaceGeneration ==
                retainedHiddenAuthority.surfaceGeneration &&
            reattachedAuthority.sessionGeneration ==
                retainedHiddenAuthority.sessionGeneration &&
            reattachedAuthority.controllerGeneration ==
                retainedHiddenAuthority.controllerGeneration &&
            reattachedAuthority.documentGeneration ==
                retainedHiddenAuthority.documentGeneration,
            "exact MediaViewport return recreated the retained controller/document");
    sendPlayback(3, PlaybackCommandKind::Play, primaryMediaKey);
    requirePlayback(3, primaryMediaKey, L"playing");
    Require(PumpUntil([&] {
        return RichMediaSurfaceCoordinatorTestPeer::DocumentAudioOutputActive(
            sample.coordinator());
    }, 5s), "built sample resume did not restore active unmuted document audio");
    sendPlayback(4, PlaybackCommandKind::Seek, primaryMediaKey, 12.0);
    requirePlayback(4, primaryMediaKey, L"playing");
    sendPlayback(5, PlaybackCommandKind::Load, secondaryMediaKey);
    requirePlayback(5, secondaryMediaKey, L"ready");
    sendPlayback(6, PlaybackCommandKind::Load, primaryMediaKey);
    requirePlayback(6, primaryMediaKey, L"ready");
    sendPlayback(7, PlaybackCommandKind::Seek, primaryMediaKey, 5.0);
    requirePlayback(7, primaryMediaKey, L"paused");
    const auto finalSeek = std::find_if(
        sample.playbackEvents().begin(), sample.playbackEvents().end(),
        [](const PlaybackEvent& event) { return event.commandSequence == 7; });
    Require(finalSeek != sample.playbackEvents().end() &&
                std::abs(finalSeek->positionSeconds - 5.0) <= 0.15,
            "built sample final seek did not retain the exact in-duration position");
    const auto preferenceControllerGeneration =
        sample.coordinator().state().authority.controllerGeneration;
    Require(sample.coordinator().SendPlaybackCommand({
                8, PlaybackCommandKind::SetPlaybackRate, std::wstring{primaryMediaKey},
                std::nullopt, std::nullopt, 1.5, std::nullopt, std::nullopt}),
            "built sample rejected a playback-rate preference command");
    requirePlayback(8, primaryMediaKey, L"paused");
    const auto rateEvent = std::find_if(
        sample.playbackEvents().begin(), sample.playbackEvents().end(),
        [](const PlaybackEvent& event) { return event.commandSequence == 8; });
    Require(rateEvent != sample.playbackEvents().end() &&
                rateEvent->playbackRate == 1.5,
            "built sample did not report the applied playback rate");
    const double authoredVolume = rateEvent->volume;
    Require(sample.coordinator().SendPlaybackCommand({
                9, PlaybackCommandKind::SetMuted, std::wstring{primaryMediaKey},
                std::nullopt, std::nullopt, std::nullopt, true, std::nullopt}),
            "built sample rejected an exact mute preference command");
    requirePlayback(9, primaryMediaKey, L"paused");
    const auto mutedEvent = std::find_if(
        sample.playbackEvents().begin(), sample.playbackEvents().end(),
        [](const PlaybackEvent& event) { return event.commandSequence == 9; });
    Require(mutedEvent != sample.playbackEvents().end() && mutedEvent->muted &&
                mutedEvent->volume == authoredVolume,
            "mute changed authored volume or did not report applied state");
    Require(sample.coordinator().SendPlaybackCommand({
                10, PlaybackCommandKind::SetLoop, std::wstring{primaryMediaKey},
                std::nullopt, std::nullopt, std::nullopt, std::nullopt, false}),
            "built sample rejected an exact loop preference command");
    requirePlayback(10, primaryMediaKey, L"paused");
    const auto loopEvent = std::find_if(
        sample.playbackEvents().begin(), sample.playbackEvents().end(),
        [](const PlaybackEvent& event) { return event.commandSequence == 10; });
    Require(loopEvent != sample.playbackEvents().end() && !loopEvent->loop,
            "built sample did not report the applied loop state");
    Require(sample.coordinator().SendPlaybackCommand({
                11, PlaybackCommandKind::Load, std::wstring{secondaryMediaKey}}),
            "built sample rejected preference-retaining media load");
    requirePlayback(11, secondaryMediaKey, L"ready");
    const auto retainedEvent = std::find_if(
        sample.playbackEvents().begin(), sample.playbackEvents().end(),
        [](const PlaybackEvent& event) { return event.commandSequence == 11; });
    Require(retainedEvent != sample.playbackEvents().end() &&
                retainedEvent->playbackRate == 1.5 && retainedEvent->muted &&
                !retainedEvent->loop && retainedEvent->volume == authoredVolume,
            "Load did not deterministically retain document-owned preferences");
    Require(sample.coordinator().state().authority.controllerGeneration ==
                preferenceControllerGeneration,
            "ordinary preference changes recreated the media controller");
    const auto eventsBeforeReplacement = sample.playbackEvents().size();
    Require(SUCCEEDED(sample.Reopen(true)),
            "preference session replacement submission failed");
    RequireReady(sample, "replacement media document did not become ready",
                 L"media-plane");
    Require(PumpUntil([&] {
        return sample.playbackEvents().size() > eventsBeforeReplacement;
    }, 5s), "replacement media document did not publish its initial state");
    const auto& replacementEvent = sample.playbackEvents().back();
    Require(replacementEvent.commandSequence == 0 &&
                replacementEvent.playbackRate == 1.0 && !replacementEvent.muted &&
                replacementEvent.loop,
            "controller/document replacement did not reset preferences to adapter defaults");
    Require(sample.coordinator().state().lifecycle == Lifecycle::Visible &&
                sample.presentationVisible() && !sample.presentationCommitFailed(),
            "compatible playback updates replaced or hid the media controller");
    Require(forcedCallbackProbe->callbacksAfterRetirement.load(
                std::memory_order_acquire) == 0,
            "retired forced-progress callbacks interleaved ordinary observations");
    Require(SUCCEEDED(sample.CloseSession()) &&
                sample.finalDetachSucceededAndWaited() &&
                sample.coordinator().state().lifecycle == Lifecycle::Absent &&
                !sample.coordinator().presentationTransferPending() &&
                sample.coordinator().DispatchPlaybackCommand({
                    12, PlaybackCommandKind::Pause,
                    std::wstring{secondaryMediaKey}}) ==
                    PlaybackCommandDispatchResult::Rejected,
            "ordinary non-retained declaration removal preserved command authority");
    std::cout << "RichMedia provider-neutral adapter cases passed=5\n";
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

int wmain(int argc, wchar_t** argv) {
    const HRESULT initialize = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialize)) {
        std::cerr << "COM initialization failed hr=" << initialize << '\n';
        return 1;
    }
    try {
        RunContractCases();
        if (argc > 1 && std::wstring_view{argv[1]} == L"--contract-only") {
            CoUninitialize();
            return 0;
        }
        if (argc > 1 && std::wstring_view{argv[1]} == L"--provider-neutral-only") {
            RunProviderNeutralAdapterCases();
            CoUninitialize();
            return 0;
        }
        if (argc > 1 &&
            std::wstring_view{argv[1]} == L"--environment-recovery-owner-only") {
            RunSharedEnvironmentRecoveryOwnerCases();
            CoUninitialize();
            return 0;
        }
        RunProcessOwnershipCases();
        RunCpuBudgetCases();
        RunMemoryBudgetScopeCases();
        RunCpuWorkloadPolicyCases();
        RunExternalPresentationCommitCases();
        RunSharedEnvironmentAdmissionCases();
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
