#pragma once

#include <Windows.h>
#include <Unknwn.h>
#include <WebView2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <cstdint>
#include <functional>
#include <string>
#include <string_view>

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
    std::uint64_t sessionGeneration{};
    std::uint64_t commandSequence{};
};

struct State final {
    Lifecycle lifecycle{Lifecycle::Absent};
    Authority authority;
    bool inputEnabled{};
    bool playing{};
    std::wstring focusedElement;
    std::wstring failureCode;
};

struct Configuration final {
    HWND ownerWindow{};
    Microsoft::WRL::ComPtr<IUnknown> compositionTarget;
    RECT bounds{};
    double rasterScale{1.0};
    bool initiallyVisible{};
    std::wstring ephemeralProfileDirectory;
    std::function<void(std::wstring_view)> diagnostic;
    std::function<void()> invalidate;
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
    [[nodiscard]] HRESULT Retry() noexcept;
    [[nodiscard]] HRESULT SetVisible(bool visible) noexcept;
    [[nodiscard]] HRESULT UpdateGeometry(const RECT& bounds, double rasterScale) noexcept;
    [[nodiscard]] bool SendCommand(Command command) noexcept;
    [[nodiscard]] bool ForwardMouse(UINT message, WPARAM wParam, LPARAM lParam) noexcept;
    [[nodiscard]] bool ForwardKey(UINT message, WPARAM wParam, LPARAM lParam) noexcept;
    [[nodiscard]] HRESULT GetAutomationProvider(
        IRawElementProviderSimple** provider) const noexcept;
    void Shutdown() noexcept;

    [[nodiscard]] State state() const noexcept { return state_; }
    [[nodiscard]] static bool ValidatePageEvent(
        std::wstring_view json, std::uint64_t expectedGeneration,
        std::uint64_t lastSequence, State& next) noexcept;
    [[nodiscard]] static std::wstring CommandJson(
        Command command, const Authority& authority);

private:
    [[nodiscard]] HRESULT BeginEnvironment() noexcept;
    [[nodiscard]] HRESULT OnEnvironmentCreated(
        HRESULT result, ICoreWebView2Environment* environment) noexcept;
    [[nodiscard]] HRESULT OnControllerCreated(
        HRESULT result, ICoreWebView2CompositionController* controller) noexcept;
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
    void Fault(std::wstring_view code, HRESULT result = S_OK) noexcept;
    void Emit(std::wstring message) const;
    void RemoveEvents() noexcept;

    Configuration configuration_;
    State state_;
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
    EventRegistrationToken browserProcessExitedToken_{};
    bool browserProcessExited_{};
    bool pageReady_{};
};

} // namespace widgetrail::richmedia
