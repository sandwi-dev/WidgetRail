#pragma once

#include "AccessibilityProvider.h"
#include "ControllerNavigation.h"
#include "DeclarativeRenderer.h"
#include "PinnedSurfacePolicy.h"
#include "PinnedSurfacePlacement.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cstddef>
#include <filesystem>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::pinned {

enum class WidgetSurfacePresentationState {
    Hidden,
    Overlay,
    PinnedInteractive,
    PinnedClickThrough,
};

enum class WidgetSurfaceStopReason {
    Unpin,
    Close,
    WidgetRemoved,
    RuntimeReplaced,
    WorkerUnavailable,
    DisplayUnavailable,
    EmergencyHide,
    HostExit,
    CoordinatorDisposed,
};

struct WidgetSurfaceInputRequest final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::wstring activeInputScopeId;
    std::wstring nodeId;
    std::wstring protocolButton;
    std::optional<double> requestedValue;
    ControllerInputOrigin origin{ControllerInputOrigin::PhysicalController};
};

struct WidgetSurfaceAdmission final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring name;
    bool pinningSupported{};
    WidgetSnapshot snapshot;
    // Host-injected policy. Public manifests cannot set physical geometry.
    PlacementLimits placementLimits{};
};

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
struct WidgetSurfacePaintTrace final {
    long long snapshotSequence{};
    ContentPresentation contentPresentation{ContentPresentation::AdmittedWidget};
    bool declarativeRenderSucceeded{}, admittedContentPresented{};
    std::size_t navigationNodeCount{};
};
#endif

/// Sole native owner for the first generic pinned HWND. Widget input is data
/// only: catalog identity, immutable generations, and a validated declarative
/// snapshot. No public or worker-facing type can supply a window or z-order.
class WidgetSurfaceCoordinator final {
public:
    static constexpr std::size_t MaximumPinnedSurfaces = 1;

    WidgetSurfaceCoordinator();
    ~WidgetSurfaceCoordinator();
    WidgetSurfaceCoordinator(const WidgetSurfaceCoordinator&) = delete;
    WidgetSurfaceCoordinator& operator=(const WidgetSurfaceCoordinator&) = delete;

    [[nodiscard]] bool Initialize(
        HINSTANCE instance,
        HWND notificationWindow,
        UINT notificationMessage,
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache,
        std::wstring& error,
        std::optional<std::filesystem::path> placementPath = std::nullopt);
    [[nodiscard]] bool Pin(WidgetSurfaceAdmission admission, std::wstring& error);
    [[nodiscard]] bool UpdateSnapshot(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        const WidgetSnapshot& snapshot);
    [[nodiscard]] bool SetInteractionMode(InteractionMode mode);
    [[nodiscard]] bool ToggleInteractionMode();
    [[nodiscard]] bool EnterControllerFocus();
    [[nodiscard]] bool ExitControllerFocus() noexcept;
    [[nodiscard]] bool MoveControllerFocus(input::NavigationDirection direction);
    [[nodiscard]] bool QueueFocusedInput(
        std::wstring_view protocolButton,
        ControllerInputOrigin origin = ControllerInputOrigin::PhysicalController,
        std::optional<double> requestedValue = std::nullopt);
    [[nodiscard]] std::vector<WidgetSurfaceInputRequest> TakeInputRequests() noexcept;
    void SetActionFeedback(std::wstring message, bool failure);
    [[nodiscard]] bool EmergencyHideAll() noexcept;
    [[nodiscard]] bool BeginPlacement(PlacementMode mode);
    [[nodiscard]] bool StepPlacement(PlacementDirection direction, float stepDip = 16.0F);
    [[nodiscard]] bool StepPlacement(
        PlacementMode operation,
        PlacementDirection direction,
        float stepDip = 16.0F);
    [[nodiscard]] bool CommitPlacement(std::wstring& error);
    [[nodiscard]] bool CancelPlacement() noexcept;
    void ReconcileDisplayEnvironment() noexcept;
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    void ReconcileDisplayEnvironmentForTesting(
        const std::vector<MonitorWorkArea>& monitors) noexcept;
    [[nodiscard]] std::optional<POINT> PointerPointForTesting(
        std::wstring_view nodeId) const noexcept;
    [[nodiscard]] WidgetSurfacePaintTrace PaintTraceForTesting() const noexcept {
        return {admission_ ? admission_->snapshot.sequence : 0,
                ResolveSurfacePresentationPolicy(policy_.interactionMode()).content,
                lastRenderResult_.succeeded, pinned() && lastRenderResult_.succeeded,
                lastRenderResult_.navigationRects.size()};
    }
#endif
    [[nodiscard]] bool Unpin(WidgetSurfaceStopReason reason) noexcept;
    void OnOverlayHidden() noexcept;
    void OnOverlayShown() noexcept;
    void ReconcileCatalog(const std::vector<WidgetDescriptor>& descriptors) noexcept;
    void Dispose() noexcept;

    [[nodiscard]] bool pinned() const noexcept;
    [[nodiscard]] HWND window() const noexcept { return window_; }
    [[nodiscard]] std::wstring_view widgetId() const noexcept;
    [[nodiscard]] std::wstring_view runtimeGeneration() const noexcept;
    [[nodiscard]] InteractionMode interactionMode() const noexcept;
    [[nodiscard]] bool controllerFocused() const noexcept { return controllerFocused_; }
    [[nodiscard]] std::wstring_view focusedElementId() const noexcept {
        return focusedElementId_;
    }
    [[nodiscard]] WidgetSurfacePresentationState presentationState() const noexcept;
    [[nodiscard]] PlacementMode placementMode() const noexcept {
        return placementSession_ ? placementSession_->mode : PlacementMode::None;
    }
    [[nodiscard]] std::size_t teardownCount() const noexcept { return teardownCount_; }
    [[nodiscard]] WidgetSurfaceStopReason lastStopReason() const noexcept {
        return lastStopReason_;
    }

private:
    static LRESULT CALLBACK WindowProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    [[nodiscard]] bool CreateWindowForAdmission(std::wstring& error);
    [[nodiscard]] bool EnsureGraphicsResources();
    void Paint();
    void PublishAccessibility();
    void ApplyWindowPolicy();
    void NotifyOwner() const noexcept;
    void ReleaseGraphicsResources() noexcept;
    void OnWindowDestroyed() noexcept;
    [[nodiscard]] std::optional<MonitorWorkArea> CurrentWindowMonitor() const noexcept;
    void ReconcileDisplayEnvironment(
        const std::vector<MonitorWorkArea>& monitors) noexcept;
    void ApplyPlacementBounds(const PhysicalRect& bounds) noexcept;
    void HandleAccessibilityActions();
    void QueueResolvedInput(
        std::wstring nodeId,
        std::wstring protocolButton,
        ControllerInputOrigin origin,
        std::optional<double> requestedValue);

    HINSTANCE instance_{};
    HWND notificationWindow_{};
    UINT notificationMessage_{};
    HWND window_{};
    bool initialized_{};
    bool disposed_{};
    bool tearingDown_{};
    std::optional<WidgetSurfaceAdmission> admission_;
    SurfacePolicy policy_;
    std::size_t teardownCount_{};
    WidgetSurfaceStopReason lastStopReason_{WidgetSurfaceStopReason::Unpin};
    accessibility::ProviderHost accessibilityProvider_;
    Microsoft::WRL::ComPtr<ID2D1Factory> d2dFactory_;
    Microsoft::WRL::ComPtr<IDWriteFactory> writeFactory_;
    std::unique_ptr<DeclarativeRenderer> renderer_;
    RemoteImageCache* imageCache_{};
    Microsoft::WRL::ComPtr<ID2D1HwndRenderTarget> renderTarget_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> chromeBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> textBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> titleFormat_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> chromeFormat_;
    PlacementLimits placementLimits_{};
    std::unique_ptr<PinnedPlacementStore> placementStore_;
    std::optional<DurablePinnedPlacement> committedPlacement_;
    std::optional<PlacementSession> placementSession_;
    RenderResult lastRenderResult_;
    std::wstring focusedElementId_;
    std::vector<WidgetSurfaceInputRequest> inputRequests_;
    std::wstring actionFeedback_;
    bool actionFeedbackFailure_{};
    bool overlayVisible_{};
    bool controllerFocused_{};
    bool pointerPlacement_{};
    std::wstring pointerActionNode_;
    POINT pointerStart_{};
    PhysicalRect pointerStartBounds_{};
};

[[nodiscard]] constexpr std::wstring_view WidgetSurfacePresentationStateValue(
    const WidgetSurfacePresentationState state) noexcept {
    switch (state) {
    case WidgetSurfacePresentationState::Overlay: return L"overlay";
    case WidgetSurfacePresentationState::PinnedInteractive: return L"pinnedInteractive";
    case WidgetSurfacePresentationState::PinnedClickThrough: return L"pinnedClickThrough";
    case WidgetSurfacePresentationState::Hidden: return L"hidden";
    }
    return L"hidden";
}

} // namespace widgetrail::pinned
