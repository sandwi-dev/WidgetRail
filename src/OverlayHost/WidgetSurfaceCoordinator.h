#pragma once

#include "AccessibilityProvider.h"
#include "ControllerNavigation.h"
#include "DeclarativeRenderer.h"
#include "PinnedSurfacePolicy.h"
#include "PinnedSurfacePlacement.h"
#include "WidgetBridgeClient.h"
#include "WidgetInteractionSession.h"
#include "WidgetSurfaceAppearance.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::pinned {

inline constexpr WPARAM kBackgroundSurfaceDiagnosticNotification = 1;
inline constexpr WPARAM kPinnedResizeDiagnosticNotification = 2;

[[nodiscard]] std::optional<double> ResolveBoundedMediaSeekTarget(
    double currentPositionSeconds,
    double durationSeconds,
    double seekStepSeconds,
    input::NavigationDirection direction) noexcept;

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
    std::wstring selectedLayoutId;
    std::wstring activeInputScopeId;
    std::wstring nodeId;
    std::wstring protocolButton;
    std::wstring activationActionId;
    std::optional<double> requestedValue;
    std::optional<input::WidgetInteractionActionRequest> sliderActionRequest;
    std::optional<input::WidgetInteractionActionRequest> selectActionRequest;
    ControllerInputOrigin origin{ControllerInputOrigin::PhysicalController};
};

struct WidgetSurfacePaginationRequest final {
    std::wstring selectedLayoutId;
    input::ScrollPaginationPrefetchRequest request;
};

struct WidgetSurfacePaginationBatch final {
    std::vector<WidgetSurfacePaginationRequest> requests;
    std::vector<input::ScrollPaginationDiagnostic> diagnostics;
};

struct PinnedLayoutOption final {
    enum class Kind {
        Authored,
        CompactMedia,
    };

    std::wstring id;
    std::wstring name;
    float contentWidthDip{};
    float contentHeightDip{};
    std::optional<WidgetSnapshot> projection;
    Kind kind{Kind::Authored};
};

struct PinnedLayoutSelectionNotification final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::wstring layoutId;
    bool selected{};
};

struct CommittedMediaViewportPresentation final {
    RenderMediaViewportRegion region;
    std::uint64_t frameGeneration{};
};

struct PinnedResizeCommitDiagnostic final {
    std::optional<SIZE> previousClientExtent;
    SIZE currentClientExtent{};
    std::size_t coalescedResizeCount{};
    CommittedMediaViewportPresentation committedViewport;
};

struct WidgetSurfaceWorkCounters final {
    std::uint64_t snapshots{};
    std::uint64_t invalidations{};
    std::uint64_t coalescedInvalidations{};
    std::uint64_t paintMessages{};
    std::uint64_t rasterDraws{};
    std::uint64_t mediaViewportReconciliations{};
    std::uint64_t ownerNotifications{};
};

struct CompactPinnedMediaState final {
    double positionSeconds{};
    double durationSeconds{};
    double previewPositionSeconds{};
    double seekStepSeconds{};
    bool playing{};
    bool scrubActive{};
    bool seekBarVisible{};
};

struct CompactMediaSeekRequest final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring sessionId;
    std::wstring selectedLayoutId;
    long long snapshotSequence{};
    double targetSeconds{};
};

struct WidgetSurfaceAdmission final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring name;
    bool pinningSupported{};
    WidgetSnapshot snapshot;
    float initialContentWidthDip{480.0F};
    float initialContentHeightDip{270.0F};
    // Host-injected policy. Public manifests cannot set physical geometry.
    PlacementLimits placementLimits{};
    std::vector<PinnedLayoutOption> pinnedLayouts;
    surface_appearance::Policy surfaceAppearancePolicy;
    bool compactMediaSessionAvailable{true};
};

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
struct WidgetSurfacePaintTrace final {
    long long snapshotSequence{};
    ContentPresentation contentPresentation{ContentPresentation::AdmittedWidget};
    bool declarativeRenderSucceeded{}, admittedContentPresented{};
    std::size_t navigationNodeCount{};
    std::optional<RenderDiagnostic> currentFirstRenderDiagnostic;
    std::wstring artworkRuntimeGeneration;
    std::wstring artworkPresentationGeneration;
    bool hostCanvasTransparent{};
    bool hostChromeVisible{};
    bool hostBorderVisible{};
    bool hostBorderUsesFocusColor{};
    float hostBorderDip{};
    declarative::Rect contentViewport{};
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
        const WidgetSnapshot& snapshot,
        std::vector<PinnedLayoutOption> layouts = {},
        bool compactMediaSessionAvailable = true);
    void SetSurfaceAppearancePolicy(surface_appearance::Policy policy);
    [[nodiscard]] bool SetInteractionMode(InteractionMode mode);
    [[nodiscard]] bool ToggleInteractionMode();
    [[nodiscard]] ImageBitmapCacheStats GetImageBitmapCacheStats() const noexcept;
    [[nodiscard]] bool EnterControllerFocus();
    [[nodiscard]] bool ExitControllerFocus() noexcept;
    [[nodiscard]] bool MoveControllerFocus(
        input::NavigationDirection direction,
        bool sliderAdjustmentEligible = true);
    [[nodiscard]] bool HandleFocusedSliderModeButton(
        std::wstring_view protocolButton,
        std::uint64_t now);
    [[nodiscard]] bool PumpSliderInteraction(
        std::uint64_t now,
        bool forceDispatch = false);
    [[nodiscard]] bool FlushSliderBeforeFocusDeparture(
        input::NavigationDirection direction,
        std::uint64_t now);
    [[nodiscard]] bool HandleFocusedSelectButton(
        std::wstring_view protocolButton,
        ControllerInputOrigin origin = ControllerInputOrigin::PhysicalController);
    [[nodiscard]] bool selectPopupOpen() const noexcept {
        return sliderInteraction_.selectPopup().has_value();
    }
    [[nodiscard]] bool MoveSelectPopupWheel(short wheelDelta);
    [[nodiscard]] bool ScrollFocusedProjection(
        short rightThumbX,
        short rightThumbY,
        std::uint64_t now);
    [[nodiscard]] bool QueueFocusedInput(
        std::wstring_view protocolButton,
        ControllerInputOrigin origin = ControllerInputOrigin::PhysicalController,
        std::optional<double> requestedValue = std::nullopt);
    [[nodiscard]] std::vector<WidgetSurfaceInputRequest> TakeInputRequests() noexcept;
    [[nodiscard]] WidgetSurfacePaginationBatch TakePaginationRequests(
        std::uint64_t now);
    [[nodiscard]] bool IsCurrentPaginationRequest(
        const WidgetSurfacePaginationRequest& request) const noexcept;
    void CompletePaginationRequest(
        input::ScrollPaginationDispatchOutcome outcome);
    [[nodiscard]] bool IsCurrentInputRequest(
        const WidgetSurfaceInputRequest& request) const noexcept;
    void RejectInputRequest(
        const WidgetSurfaceInputRequest& request,
        std::uint64_t now) noexcept;
    [[nodiscard]] std::vector<PinnedLayoutSelectionNotification>
        TakeLayoutSelectionNotifications() noexcept;
    [[nodiscard]] std::vector<std::wstring>
        TakeBackgroundSurfaceDiagnostics() noexcept;
    [[nodiscard]] std::vector<PinnedResizeCommitDiagnostic>
        TakePinnedResizeCommitDiagnostics() noexcept;
    void SetActionFeedback(std::wstring message, bool failure);
    void SetBeforeWindowRetirement(
        std::function<void(WidgetSurfaceStopReason)> callback);
    [[nodiscard]] bool EmergencyHideAll() noexcept;
    [[nodiscard]] bool BeginPlacement(PlacementMode mode);
    [[nodiscard]] bool BeginSetup(bool newPin);
    [[nodiscard]] bool CycleLayout(int delta);
    [[nodiscard]] bool CommitSetup(std::wstring& error);
    [[nodiscard]] bool CancelSetup() noexcept;
    [[nodiscard]] bool StepPlacement(
        PlacementDirection direction,
        float stepDip = surface_geometry::kPlacementAdjustmentStepDip);
    [[nodiscard]] bool StepPlacement(
        PlacementMode operation,
        PlacementDirection direction,
        float stepDip = surface_geometry::kPlacementAdjustmentStepDip);
    [[nodiscard]] bool CommitPlacement(std::wstring& error);
    [[nodiscard]] bool CancelPlacement() noexcept;
    [[nodiscard]] bool BeginOpacityAdjustment();
    [[nodiscard]] bool StepOpacity(PlacementDirection direction);
    [[nodiscard]] bool CommitOpacity(std::wstring& error);
    [[nodiscard]] bool CancelOpacity() noexcept;
    void ReconcileDisplayEnvironment() noexcept;
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    void ReconcileDisplayEnvironmentForTesting(
        const std::vector<MonitorWorkArea>& monitors) noexcept;
    [[nodiscard]] std::optional<POINT> PointerPointForTesting(
        std::wstring_view nodeId) const noexcept;
    [[nodiscard]] WidgetSurfacePaintTrace PaintTraceForTesting() const {
        return {admission_ ? admission_->snapshot.sequence : 0,
                ResolveSurfacePresentationPolicy(policy_.interactionMode()).content,
                lastRenderResult_.succeeded, pinned() && lastRenderResult_.succeeded,
                lastRenderResult_.navigationRects.size(),
                lastRenderResult_.diagnostics.empty()
                    ? std::nullopt
                    : std::optional<RenderDiagnostic>{
                          lastRenderResult_.diagnostics.front()},
                lastArtworkRuntimeGenerationForTesting_,
                lastArtworkPresentationGenerationForTesting_,
                lastHostCanvasTransparentForTesting_,
                lastHostChromeVisibleForTesting_,
                lastHostBorderVisibleForTesting_,
                lastHostBorderUsesFocusColorForTesting_,
                lastHostBorderDipForTesting_,
                lastContentViewportForTesting_};
    }
    [[nodiscard]] std::optional<float> ScrollOffsetForTesting(
        const std::wstring_view scrollId) const noexcept {
        const auto found = lastRenderResult_.scrollOffsets.find(scrollId);
        return found == lastRenderResult_.scrollOffsets.end()
            ? std::nullopt : std::optional<float>{found->second};
    }
    [[nodiscard]] bool FreeScrollBindingForTesting() const noexcept {
        return freeScroll_.binding().has_value();
    }
    [[nodiscard]] bool SliderAdjustmentActiveForTesting(
        std::wstring_view nodeId, std::uint64_t now);
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
    [[nodiscard]] std::optional<CommittedMediaViewportPresentation>
        CurrentMediaViewport(std::wstring_view sessionId) const noexcept;
    [[nodiscard]] bool compactMediaPresentation() const noexcept;
    [[nodiscard]] CompactPinnedMediaState compactMediaState() const noexcept;
    void UpdateCompactMediaPlayback(
        double positionSeconds, double durationSeconds, bool playing) noexcept;
    [[nodiscard]] bool BeginCompactMediaScrub() noexcept;
    [[nodiscard]] bool StepCompactMediaScrub(
        input::NavigationDirection direction) noexcept;
    [[nodiscard]] std::optional<double> CompactMediaSeekTarget(
        input::NavigationDirection direction) const noexcept;
    [[nodiscard]] std::optional<double> CommitCompactMediaScrub() noexcept;
    [[nodiscard]] bool CancelCompactMediaScrub() noexcept;
    [[nodiscard]] std::optional<CompactMediaSeekRequest>
        TakeCompactMediaSeekRequest() noexcept;
    [[nodiscard]] bool IsCurrentCompactMediaSeekRequest(
        const CompactMediaSeekRequest& request) const noexcept;
    [[nodiscard]] WidgetSurfacePresentationState presentationState() const noexcept;
    [[nodiscard]] PlacementMode placementMode() const noexcept {
        return placementSession_ ? placementSession_->mode : PlacementMode::None;
    }
    [[nodiscard]] bool opacityAdjustmentActive() const noexcept {
        return opacityPreviewOriginal_.has_value();
    }
    [[nodiscard]] bool setupActive() const noexcept { return setupNewPin_.has_value(); }
    [[nodiscard]] std::wstring_view selectedLayoutName() const noexcept;
    [[nodiscard]] std::size_t selectedLayoutIndex() const noexcept { return selectedLayoutIndex_; }
    [[nodiscard]] std::size_t layoutCount() const noexcept { return layoutOptions_.size(); }
    [[nodiscard]] unsigned int opacityPercent() const noexcept {
        return opacityPercent_;
    }
    [[nodiscard]] std::size_t teardownCount() const noexcept { return teardownCount_; }
    [[nodiscard]] WidgetSurfaceWorkCounters workCounters() const noexcept {
        return workCounters_;
    }
    [[nodiscard]] WidgetSurfaceStopReason lastStopReason() const noexcept {
        return lastStopReason_;
    }

private:
    static LRESULT CALLBACK WindowProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    [[nodiscard]] bool CreateWindowForAdmission(std::wstring& error);
    [[nodiscard]] static std::optional<std::vector<PinnedLayoutOption>>
        BuildLayoutOptions(
            float fullWidthDip,
            float fullHeightDip,
            const std::vector<PinnedLayoutOption>& authored,
            const WidgetSnapshot& snapshot,
            bool compactMediaSessionAvailable);
    [[nodiscard]] bool EnsureGraphicsResources();
    void Paint();
    void RequestPaint(const RECT* update = nullptr) noexcept;
    void PublishAccessibility();
    void ApplyWindowPolicy();
    void NotifyOwner(WPARAM notification = 0) const noexcept;
    void ReleaseGraphicsResources() noexcept;
    void OnWindowDestroyed() noexcept;
    [[nodiscard]] std::optional<MonitorWorkArea> CurrentWindowMonitor() const noexcept;
    void ReconcileDisplayEnvironment(
        const std::vector<MonitorWorkArea>& monitors) noexcept;
    void ApplyPlacementBounds(const PhysicalRect& bounds) noexcept;
    void ApplyOpacity() noexcept;
    [[nodiscard]] surface_appearance::Mode EffectiveSurfaceAppearance() const noexcept;
    [[nodiscard]] bool SaveCurrentState(std::wstring& error);
    void HandleAccessibilityActions();
    bool QueueResolvedInput(
        std::wstring nodeId,
        std::wstring protocolButton,
        ControllerInputOrigin origin,
        std::optional<double> requestedValue,
        std::optional<input::WidgetInteractionActionRequest> sliderActionRequest =
            std::nullopt,
        std::optional<input::WidgetInteractionActionRequest> selectActionRequest =
            std::nullopt);
    [[nodiscard]] const WidgetSnapshot& SelectedSnapshot() const noexcept;
    [[nodiscard]] std::wstring_view SelectedLayoutId() const noexcept;
    void ClearFreeScroll() noexcept;
    void RetireSliderInteraction() noexcept;
    void TransitionPinnedFocus(std::wstring_view target);
    [[nodiscard]] bool RecordPaginationOutcome(
        input::ScrollPaginationSessionOutcome outcome);
    void QueueLayoutSelection(std::wstring_view layoutId, bool selected);

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
    std::vector<PinnedLayoutOption> layoutOptions_;
    std::size_t selectedLayoutIndex_{};
    std::optional<bool> setupNewPin_;
    std::wstring setupOriginalLayoutId_;
    unsigned int opacityPercent_{kMaximumOpacityPercent};
    std::optional<unsigned int> opacityPreviewOriginal_;
    RenderResult lastRenderResult_;
    std::optional<CommittedMediaViewportPresentation> committedMediaViewport_;
    std::uint64_t nextCommittedFrameGeneration_{1};
    bool mediaViewportGeometryDirty_{true};
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    bool lastHostCanvasTransparentForTesting_{};
    bool lastHostChromeVisibleForTesting_{};
    bool lastHostBorderVisibleForTesting_{};
    bool lastHostBorderUsesFocusColorForTesting_{};
    float lastHostBorderDipForTesting_{};
    declarative::Rect lastContentViewportForTesting_{};
    std::wstring lastArtworkRuntimeGenerationForTesting_;
    std::wstring lastArtworkPresentationGenerationForTesting_;
#endif
    struct PendingPinnedResizeDiagnostic final {
        std::optional<SIZE> previousClientExtent;
        SIZE currentClientExtent{};
        std::size_t coalescedResizeCount{1};
    };
    std::optional<SIZE> lastPinnedClientExtent_;
    std::optional<PendingPinnedResizeDiagnostic> pendingPinnedResizeDiagnostic_;
    std::vector<PinnedResizeCommitDiagnostic> pinnedResizeCommitDiagnostics_;
    mutable WidgetSurfaceWorkCounters workCounters_;
    std::wstring focusedElementId_;
    input::WidgetFocusGroupMemory focusGroupMemory_;
    input::FreeScrollInteractionState freeScroll_;
    input::WidgetInteractionSession sliderInteraction_;
    std::vector<WidgetSurfaceInputRequest> inputRequests_;
    std::vector<input::ScrollPaginationDiagnostic> paginationDiagnostics_;
    std::vector<PinnedLayoutSelectionNotification> layoutSelectionNotifications_;
    std::vector<std::wstring> backgroundSurfaceDiagnostics_;
    std::wstring actionFeedback_;
    std::function<void(WidgetSurfaceStopReason)> beforeWindowRetirement_;
    bool actionFeedbackFailure_{};
    bool overlayVisible_{};
    bool controllerFocused_{};
    double compactMediaPositionSeconds_{};
    double compactMediaDurationSeconds_{};
    double compactMediaPreviewSeconds_{};
    bool compactMediaPlaying_{};
    bool compactMediaScrubActive_{};
    std::optional<CompactMediaSeekRequest> compactMediaSeekRequest_;
    bool pointerPlacement_{};
    PlacementMode pointerPlacementMode_{PlacementMode::None};
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
