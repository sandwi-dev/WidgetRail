#pragma once

#include <Windows.h>
#include <Unknwn.h>
#include <WebView2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <cstdint>
#include <atomic>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::richmedia {

enum class Lifecycle {
    Absent,
    EnvironmentCreating,
    ControllerCreating,
    ReadyHidden,
    Visible,
    Faulted,
    Closing,
};

enum class EnvironmentLifecycle {
    Cold,
    Creating,
    Ready,
    ShuttingDown,
};

enum class Command {
    NavigatePrevious,
    NavigateNext,
    Activate,
    Back,
    TogglePlayback,
    SeekBackward,
    SeekForward,
};

struct Authority final {
    std::uint64_t environmentGeneration{};
    std::uint64_t surfaceGeneration{};
    std::uint64_t sessionGeneration{};
    std::uint64_t controllerGeneration{};
    std::uint64_t documentGeneration{};
    std::uint64_t eventSequence{};
};

struct ActionBounds final {
    double x{};
    double y{};
    double width{};
    double height{};
};

struct State final {
    Lifecycle lifecycle{Lifecycle::Absent};
    Authority authority;
    bool inputEnabled{};
    bool playing{};
    std::uint64_t lastAcknowledgedCommandId{};
    ActionBounds focusedActionBounds;
    bool focusedActionBoundsCurrent{};
    std::wstring focusedElement;
    std::wstring failureCode;
};

struct SessionTeardownResult final {
    DWORD browserProcessId{};
    bool callbackDeadlineExpired{};
    HRESULT visibilityResult{E_UNEXPECTED};
    HRESULT rootVisualResult{E_UNEXPECTED};
    HRESULT controllerCloseResult{E_UNEXPECTED};
    bool sessionOwnersEmpty{};
    bool environmentRetained{};
};

struct EnvironmentState final {
    EnvironmentLifecycle lifecycle{EnvironmentLifecycle::Cold};
    std::uint64_t generation{};
    DWORD browserProcessId{};
    bool browserExitObserved{};
    bool faulted{};
    bool observerActive{};
    std::wstring profileDirectory;
};

struct Configuration final {
    struct Resource final {
        std::wstring path;
        std::wstring contentType;
        std::vector<std::uint8_t> content;
    };
    HWND ownerWindow{};
    Microsoft::WRL::ComPtr<IUnknown> compositionTarget;
    RECT bounds{};
    double rasterScale{1.0};
    bool initiallyVisible{};
    std::wstring profileRootDirectory;
    // Empty values select the embedded provider-neutral WIDGE-20 proof. A
    // public session supplies one host-generated origin plus an exact bounded
    // package-local resource bundle admitted by WidgetBridge.
    std::wstring origin;
    std::wstring entryAsset;
    std::vector<Resource> resources;
    std::function<void(std::wstring_view)> diagnostic;
    std::function<void()> invalidate;
    std::function<void(bool)> setPresentationVisible;
};

struct PresentationTarget final {
    HWND ownerWindow{};
    Microsoft::WRL::ComPtr<IUnknown> compositionTarget;
    RECT bounds{};
    double rasterScale{1.0};
    std::function<void(bool)> setPresentationVisible;
};

// Owns the bounded rich-media session only. The OverlayApp remains the sole
// HWND/input/focus/UIA/geometry/presentation/teardown authority and explicitly
// forwards admitted operations to this coordinator.
class RichMediaSurfaceCoordinator final {
public:
    RichMediaSurfaceCoordinator();
    ~RichMediaSurfaceCoordinator();
    RichMediaSurfaceCoordinator(const RichMediaSurfaceCoordinator&) = delete;
    RichMediaSurfaceCoordinator& operator=(const RichMediaSurfaceCoordinator&) = delete;

    [[nodiscard]] HRESULT Initialize(Configuration configuration) noexcept;
    [[nodiscard]] HRESULT Retry(Configuration configuration) noexcept;
    [[nodiscard]] HRESULT SetVisible(bool visible) noexcept;
    [[nodiscard]] HRESULT UpdateGeometry(const RECT& bounds, double rasterScale) noexcept;
    [[nodiscard]] HRESULT BeginPresentationTransfer() noexcept;
    [[nodiscard]] HRESULT CompletePresentationTransfer(
        PresentationTarget target) noexcept;
    [[nodiscard]] bool SendCommand(Command command) noexcept;
    [[nodiscard]] bool ForwardMouse(UINT message, WPARAM wParam, LPARAM lParam) noexcept;
    [[nodiscard]] bool ForwardKey(UINT message, WPARAM wParam, LPARAM lParam) noexcept;
    [[nodiscard]] HRESULT GetAutomationProvider(
        IRawElementProviderSimple** provider) const noexcept;
    void BeginSessionTeardown() noexcept;
    void CompleteSessionTeardown() noexcept;
    void Shutdown() noexcept;

    [[nodiscard]] State state() const noexcept { return state_; }
    [[nodiscard]] SessionTeardownResult sessionTeardownResult() const noexcept {
        return sessionTeardownResult_;
    }
    [[nodiscard]] EnvironmentState environmentState() const noexcept;
    [[nodiscard]] static bool ValidatePageEvent(
        std::wstring_view json, const Authority& expectedAuthority,
        std::uint64_t lastSequence, std::optional<std::uint64_t> pendingCommandId,
        State& next) noexcept;
    [[nodiscard]] static std::wstring CommandJson(
        Command command, const Authority& authority, std::uint64_t commandId);

private:
    [[nodiscard]] HRESULT BeginEnvironment() noexcept;
    struct CallbackLease;
    struct FrameSubscription;
    struct EnvironmentSignal;
    static void RecordBrowserProcessExit(
        const std::shared_ptr<EnvironmentSignal>& signal, DWORD processId) noexcept;
    [[nodiscard]] bool BrowserProcessExitObserved() const noexcept;
    [[nodiscard]] bool BrowserProcessExitObserverActive() const noexcept;
    [[nodiscard]] std::shared_ptr<CallbackLease> CreateCallbackLease() noexcept;
    [[nodiscard]] bool IsCurrentCallback(
        const std::shared_ptr<CallbackLease>& lease, bool requireDocument) const noexcept;
    void RetireCallbacks() noexcept;
    [[nodiscard]] HRESULT BeginController() noexcept;
    [[nodiscard]] HRESULT OnEnvironmentCreated(
        const std::shared_ptr<CallbackLease>& lease, HRESULT result,
        ICoreWebView2Environment* environment) noexcept;
    [[nodiscard]] HRESULT OnControllerCreated(
        const std::shared_ptr<CallbackLease>& lease, HRESULT result,
        ICoreWebView2CompositionController* controller) noexcept;
    [[nodiscard]] HRESULT ConfigureCore() noexcept;
    [[nodiscard]] HRESULT ServeResource(
        ICoreWebView2WebResourceRequestedEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnWebMessage(
        ICoreWebView2WebMessageReceivedEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnNavigationStarting(
        ICoreWebView2NavigationStartingEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnNavigationCompleted(
        ICoreWebView2NavigationCompletedEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnProcessFailed(
        ICoreWebView2ProcessFailedEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnFrameCreated(
        ICoreWebView2FrameCreatedEventArgs* args) noexcept;
    [[nodiscard]] HRESULT OnFrameNavigationStarting(
        ICoreWebView2NavigationStartingEventArgs* args) noexcept;
    [[nodiscard]] static bool IsAllowedNavigation(std::wstring_view uri) noexcept;
    [[nodiscard]] static bool IsAllowedNavigation(
        std::wstring_view uri, std::wstring_view exactPageUri) noexcept;
    [[nodiscard]] static bool IsAllowedMessageSource(
        std::wstring_view source, std::wstring_view exactPageUri) noexcept;
    [[nodiscard]] static bool IsValidAdapterConfiguration(
        const Configuration& configuration) noexcept;
    [[nodiscard]] static bool SurfaceLocalPoint(
        HWND ownerWindow, const RECT& bounds, UINT message, LPARAM lParam,
        POINT& point) noexcept;
    [[nodiscard]] static bool FocusedActionPoint(
        const ActionBounds& actionBounds, const RECT& surfaceBounds,
        POINT& point) noexcept;
    [[nodiscard]] bool SendFocusedSpatialActivation() noexcept;
    void Fault(std::wstring_view code, HRESULT result = S_OK) noexcept;
    void Emit(std::wstring message) const;
    void RemoveEvents() noexcept;
    void ReleaseEnvironment(bool markForDeferredCleanup) noexcept;
    void CleanupMarkedPriorProfiles() const noexcept;
    void MarkCurrentProfileForDeferredCleanup() const noexcept;

    Configuration configuration_;
    State state_;
    std::uint64_t nextSurfaceGeneration_{};
    std::uint64_t nextEnvironmentGeneration_{};
    std::uint64_t nextSessionGeneration_{};
    std::uint64_t nextControllerGeneration_{};
    std::uint64_t nextDocumentGeneration_{};
    std::uint64_t nextCommandId_{};
    enum class PendingPhase { AwaitingEvent, AwaitingSpatialActivation };
    struct PendingCommand final {
        std::uint64_t id{};
        Command command{};
        PendingPhase phase{PendingPhase::AwaitingEvent};
    };
    std::optional<PendingCommand> pendingCommand_;
    bool desiredVisible_{};
    Microsoft::WRL::ComPtr<ICoreWebView2Environment> environment_;
    Microsoft::WRL::ComPtr<ICoreWebView2Environment5> environment5_;
    Microsoft::WRL::ComPtr<ICoreWebView2CompositionController> controller_;
    Microsoft::WRL::ComPtr<ICoreWebView2Controller> controllerBase_;
    Microsoft::WRL::ComPtr<ICoreWebView2> core_;
    EventRegistrationToken navigationStartingToken_{};
    EventRegistrationToken navigationCompletedToken_{};
    EventRegistrationToken webResourceRequestedToken_{};
    EventRegistrationToken webMessageReceivedToken_{};
    EventRegistrationToken processFailedToken_{};
    EventRegistrationToken newWindowRequestedToken_{};
    EventRegistrationToken permissionRequestedToken_{};
    EventRegistrationToken downloadStartingToken_{};
    EventRegistrationToken basicAuthenticationToken_{};
    EventRegistrationToken serverCertificateToken_{};
    EventRegistrationToken externalUriToken_{};
    EventRegistrationToken frameCreatedToken_{};
    EventRegistrationToken browserProcessExitedToken_{};
    std::shared_ptr<CallbackLease> callbackLease_;
    std::vector<std::shared_ptr<CallbackLease>> retiredLeases_;
    std::shared_ptr<EnvironmentSignal> environmentSignal_;
    std::vector<FrameSubscription> frameSubscriptions_;
    std::uint64_t pendingNavigationId_{};
    bool pageReady_{};
    bool mouseInside_{};
    POINT lastMousePoint_{};
    SessionTeardownResult sessionTeardownResult_;
    EnvironmentLifecycle environmentLifecycle_{EnvironmentLifecycle::Cold};
    std::wstring profileRootDirectory_;
    std::wstring environmentProfileDirectory_;
    bool environmentFaulted_{};
    std::uint64_t retrySurfaceGeneration_{};
    bool teardownBegun_{};
    bool presentationTransferPending_{};
    bool transferDesiredVisible_{};
    HRESULT browserEventRegistrationResult_{E_UNEXPECTED};
    std::wstring pageUri_;

    friend class RichMediaSurfaceCoordinatorTestPeer;
};

} // namespace widgetrail::richmedia
