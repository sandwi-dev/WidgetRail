#include "WidgetSurfaceCoordinator.h"

#include "EmbeddedMediaResourceContract.h"

#include "AccessibilityTree.h"
#include "FocusNavigation.h"
#include "NativeIcons.h"
#include "WidgetSurfaceFocus.h"
#include "WidgetSurfaceGeometry.h"

#include <ShellScalingApi.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <iterator>
#include <utility>

namespace widgetrail::pinned {
namespace {

constexpr wchar_t kWindowClass[] = L"WidgetRail.PinnedSurface";
constexpr wchar_t kWindowTitle[] = L"WidgetRail pinned surface";
constexpr UINT kAccessibilityActionMessage = WM_APP + 0x316;
constexpr UINT_PTR kBackgroundSurfaceAnimationTimer = 1;
constexpr UINT kBackgroundSurfaceAnimationTimerMilliseconds = 15;
constexpr float kChromeHeightDip = surface_geometry::kPinnedChromeHeightDip;
constexpr float kSideInsetDip = surface_geometry::kPinnedSideInsetDip;
constexpr float kBottomInsetDip = surface_geometry::kPinnedBottomInsetDip;
constexpr float kPinnedBorderDip = 1.0F;
constexpr float kAdjustBorderDip = 3.0F;
constexpr std::size_t kMaximumPendingInputRequests = 16;
constexpr std::size_t kMaximumPendingLayoutSelectionNotifications = 16;
constexpr std::size_t kMaximumFeedbackCharacters = 160;
constexpr std::wstring_view kCompactMediaLayoutId = L"host.compact-media";
constexpr COLORREF kTransparentSurfaceColorKey = RGB(1, 2, 3);

[[nodiscard]] bool SameCompactMediaGeometryContract(
    const std::optional<EmbeddedMediaSessionDeclaration>& left,
    const std::optional<EmbeddedMediaSessionDeclaration>& right) noexcept {
    if (left.has_value() != right.has_value()) return false;
    if (!left) return true;
    const auto& a = left->surface;
    const auto& b = right->surface;
    return left->id == right->id && left->aspectRatio == right->aspectRatio &&
        a.mode == b.mode && a.widthMode == b.widthMode &&
        a.heightMode == b.heightMode &&
        a.preferredWidth == b.preferredWidth &&
        a.preferredHeight == b.preferredHeight &&
        a.minimumWidth == b.minimumWidth &&
        a.minimumHeight == b.minimumHeight;
}

[[nodiscard]] std::filesystem::path DefaultPlacementPath() {
    std::array<wchar_t, 32768> localAppData{};
    const DWORD length = GetEnvironmentVariableW(
        L"LOCALAPPDATA", localAppData.data(), static_cast<DWORD>(localAppData.size()));
    if (length == 0 || length >= localAppData.size()) return {};
    return std::filesystem::path(
               std::wstring_view(localAppData.data(), length)) /
        L"WidgetRail" / L"pinned-surface-placement.ini";
}

[[nodiscard]] DWORD ExtendedStyle(const InteractionMode mode) noexcept {
    DWORD style = WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED;
    if (mode == InteractionMode::ClickThrough)
        style |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
    return style;
}

[[nodiscard]] std::vector<MonitorWorkArea> CurrentMonitorWorkAreas() {
    struct Context final {
        std::vector<MonitorWorkArea> monitors;
    } context;
    EnumDisplayMonitors(nullptr, nullptr,
        [](const HMONITOR monitor, HDC, LPRECT, const LPARAM data) -> BOOL {
            auto& destination = reinterpret_cast<Context*>(data)->monitors;
            MONITORINFOEXW info{sizeof(info)};
            if (!GetMonitorInfoW(monitor, &info)) return TRUE;
            UINT dpiX = 96;
            UINT dpiY = 96;
            if (FAILED(GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &dpiX, &dpiY)) ||
                dpiX == 0 || dpiY == 0) {
                dpiX = 96;
            }
            destination.push_back({
                info.szDevice,
                {info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom},
                dpiX,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            });
            return TRUE;
        }, reinterpret_cast<LPARAM>(&context));
    return context.monitors;
}

} // namespace

std::optional<double> ResolveBoundedMediaSeekTarget(
    const double currentPositionSeconds,
    const double durationSeconds,
    const double seekStepSeconds,
    const input::NavigationDirection direction) noexcept {
    if (!std::isfinite(currentPositionSeconds) ||
        !std::isfinite(durationSeconds) ||
        !std::isfinite(seekStepSeconds) ||
        currentPositionSeconds < 0.0 || durationSeconds <= 0.0 ||
        seekStepSeconds <= 0.0 ||
        (direction != input::NavigationDirection::Left &&
         direction != input::NavigationDirection::Right)) return std::nullopt;
    const double position = std::clamp(
        currentPositionSeconds, 0.0, durationSeconds);
    const double target = std::clamp(
        position + seekStepSeconds *
            (direction == input::NavigationDirection::Left ? -1.0 : 1.0),
        0.0, durationSeconds);
    return target == position ? std::nullopt
                              : std::optional<double>{target};
}

WidgetSurfaceCoordinator::WidgetSurfaceCoordinator() = default;

WidgetSurfaceCoordinator::~WidgetSurfaceCoordinator() {
    Dispose();
}

bool WidgetSurfaceCoordinator::Initialize(
    const HINSTANCE instance,
    const HWND notificationWindow,
    const UINT notificationMessage,
    ID2D1Factory* const d2dFactory,
    IDWriteFactory* const writeFactory,
    RemoteImageCache* const imageCache,
    std::wstring& error,
    std::optional<std::filesystem::path> placementPath) {
    if (initialized_ || disposed_ || !instance || !d2dFactory || !writeFactory ||
        notificationMessage < WM_APP) {
        error = L"Pinned surface coordinator initialization is invalid.";
        return false;
    }
    WNDCLASSEXW windowClass{sizeof(windowClass)};
    windowClass.lpfnWndProc = WindowProc;
    windowClass.hInstance = instance;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
    windowClass.lpszClassName = kWindowClass;
    if (!RegisterClassExW(&windowClass) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        error = L"Pinned surface window class could not be registered.";
        return false;
    }
    instance_ = instance;
    notificationWindow_ = notificationWindow;
    notificationMessage_ = notificationMessage;
    d2dFactory_ = d2dFactory;
    writeFactory_ = writeFactory;
    imageCache_ = imageCache;
    if (!placementPath) placementPath = DefaultPlacementPath();
    if (placementPath->empty()) {
        error = L"Pinned surface placement storage is unavailable.";
        return false;
    }
    placementStore_ = std::make_unique<PinnedPlacementStore>(std::move(*placementPath));
    renderer_ = std::make_unique<DeclarativeRenderer>(
        d2dFactory_.Get(), writeFactory_.Get(), imageCache_);
    initialized_ = true;
    error.clear();
    return true;
}

std::optional<std::vector<PinnedLayoutOption>>
WidgetSurfaceCoordinator::BuildLayoutOptions(
    const float fullWidthDip,
    const float fullHeightDip,
    const std::vector<PinnedLayoutOption>& authored,
    const WidgetSnapshot& snapshot,
    const bool compactMediaSessionAvailable) {
    std::vector<PinnedLayoutOption> result;
    result.push_back({
        std::wstring{kFullWidgetLayoutId}, L"Full widget",
        fullWidthDip, fullHeightDip});
    for (const auto& layout : authored) {
        if (layout.id.empty() || layout.name.empty() ||
            layout.id == kCompactMediaLayoutId ||
            layout.kind != PinnedLayoutOption::Kind::Authored ||
            !std::isfinite(layout.contentWidthDip) ||
            !std::isfinite(layout.contentHeightDip) ||
            layout.contentWidthDip <= 0.0F || layout.contentHeightDip <= 0.0F ||
            std::ranges::any_of(result, [&](const auto& existing) {
                return existing.id == layout.id;
            }))
            return std::nullopt;
        result.push_back(layout);
    }
    if (compactMediaSessionAvailable && snapshot.embeddedMediaSession &&
        SupportsMediaPresentation(
            *snapshot.embeddedMediaSession,
            MediaPresentationKind::CompactPinned)) {
        const auto& media = *snapshot.embeddedMediaSession;
        result.push_back({
            std::wstring{kCompactMediaLayoutId},
            L"Compact media",
            media.surface.preferredWidth
                ? static_cast<float>(*media.surface.preferredWidth)
                : fullWidthDip,
            media.surface.preferredHeight
                ? static_cast<float>(*media.surface.preferredHeight)
                : fullHeightDip,
            std::nullopt,
            PinnedLayoutOption::Kind::CompactMedia,
        });
    }
    return result;
}

bool WidgetSurfaceCoordinator::Pin(
    WidgetSurfaceAdmission admission,
    std::wstring& error) {
    if (!initialized_ || disposed_) {
        error = L"Pinned surfaces are unavailable.";
        return false;
    }
    if (pinned()) {
        error = admission.widgetId == widgetId()
            ? L"This widget is already pinned."
            : L"Only one pinned surface is currently supported.";
        return false;
    }
    if (!admission.pinningSupported || admission.widgetId.empty() ||
        admission.instanceId.empty() || admission.runtimeGeneration.empty() ||
        admission.presentationGeneration.empty() || admission.name.empty() ||
        admission.snapshot.instanceId != admission.instanceId ||
        !std::isfinite(admission.initialContentWidthDip) ||
        !std::isfinite(admission.initialContentHeightDip) ||
        admission.initialContentWidthDip <= 0.0F ||
        admission.initialContentHeightDip <= 0.0F) {
        error = L"The current widget does not expose an admitted pinnable surface.";
        return false;
    }
    if (!policy_.Pin({
            L"pinned:" + admission.widgetId,
            admission.name + L" pinned surface",
            0xFF16212E,
            true})) {
        error = L"The pinned surface descriptor was rejected.";
        return false;
    }
    auto layouts = BuildLayoutOptions(
        admission.initialContentWidthDip, admission.initialContentHeightDip,
        admission.pinnedLayouts, admission.snapshot,
        admission.compactMediaSessionAvailable);
    if (!layouts) {
        error = L"The current widget exposes an invalid pinned layout catalog.";
        policy_.Stop(StopReason::Unpin);
        return false;
    }
    placementLimits_ = admission.placementLimits;
    placementLimits_.maximumWidthDip = std::max(
        placementLimits_.maximumWidthDip,
        admission.initialContentWidthDip + kSideInsetDip * 2.0F);
    placementLimits_.maximumHeightDip = std::max(
        placementLimits_.maximumHeightDip,
        admission.initialContentHeightDip + kChromeHeightDip + kBottomInsetDip);
    workCounters_ = {};
    admission_ = std::move(admission);
    layoutOptions_ = std::move(*layouts);
    selectedLayoutIndex_ = 0;
    focusedElementId_ = SelectedSnapshot().initialFocusId;
    focusGroupMemory_.Remember(
        admission_->widgetId, SelectedSnapshot(), focusedElementId_);
    RetireSliderInteraction();
    inputRequests_.clear();
    paginationDiagnostics_.clear();
    actionFeedback_.clear();
    actionFeedbackFailure_ = false;
    controllerFocused_ = false;
    opacityPercent_ = kMaximumOpacityPercent;
    opacityPreviewOriginal_.reset();
    lastPinnedClientExtent_.reset();
    pendingPinnedResizeDiagnostic_.reset();
    pinnedResizeCommitDiagnostics_.clear();
    if (!CreateWindowForAdmission(error)) {
        admission_.reset();
        layoutOptions_.clear();
        policy_.Stop(StopReason::Unpin);
        return false;
    }
    if (!BeginSetup(true)) {
        (void)Unpin(WidgetSurfaceStopReason::Unpin);
        error = L"The pinned layout setup could not be started.";
        return false;
    }
    if (selectedLayoutIndex_ != 0)
        QueueLayoutSelection(layoutOptions_[selectedLayoutIndex_].id, true);
    PublishAccessibility();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::UpdateSnapshot(
    const std::wstring_view widgetIdValue,
    const std::wstring_view runtimeGenerationValue,
    const WidgetSnapshot& snapshot,
    std::vector<PinnedLayoutOption> layouts,
    const bool compactMediaSessionAvailable) {
    if (!pinned() || widgetIdValue != admission_->widgetId ||
        runtimeGenerationValue != admission_->runtimeGeneration ||
        snapshot.instanceId != admission_->instanceId) {
        return false;
    }
    auto nextLayouts = BuildLayoutOptions(
        admission_->initialContentWidthDip,
        admission_->initialContentHeightDip, layouts, snapshot,
        compactMediaSessionAvailable);
    if (!nextLayouts) return false;
    ++workCounters_.snapshots;
    const auto priorSurfaceAppearance = EffectiveSurfaceAppearance();
    const std::wstring priorLayoutId{SelectedLayoutId()};
    const bool priorCompactMedia = compactMediaPresentation();
    const auto priorMediaGeometry = SelectedSnapshot().embeddedMediaSession;
    const std::wstring priorInputScopeId{
        SelectedSnapshot().activeInputScopeId};
    const std::optional<widgetrail::EmbeddedMediaDocumentIdentity>
        priorMediaContract = SelectedSnapshot().embeddedMediaSession
            ? std::optional{widgetrail::MakeEmbeddedMediaDocumentIdentity(
                  *SelectedSnapshot().embeddedMediaSession)}
            : std::nullopt;
    const std::wstring selectedId = selectedLayoutIndex_ < layoutOptions_.size()
        ? layoutOptions_[selectedLayoutIndex_].id : std::wstring(kFullWidgetLayoutId);
    const auto nextSelected = std::ranges::find_if(
        *nextLayouts, [&](const auto& layout) { return layout.id == selectedId; });
    if (priorCompactMedia && nextSelected == nextLayouts->end()) {
        // The compact endpoint cannot silently fall back to Full Widget in the
        // old compact HWND extent. Retire the pin while its exact old geometry
        // is still available to the before-window-retirement transfer owner.
        (void)Unpin(WidgetSurfaceStopReason::Unpin);
        return false;
    }
    admission_->snapshot = snapshot;
    admission_->pinnedLayouts = std::move(layouts);
    admission_->compactMediaSessionAvailable = compactMediaSessionAvailable;
    layoutOptions_ = std::move(*nextLayouts);
    const auto selected = std::ranges::find_if(layoutOptions_, [&](const auto& layout) {
        return layout.id == selectedId;
    });
    selectedLayoutIndex_ = selected == layoutOptions_.end()
        ? 0 : static_cast<std::size_t>(selected - layoutOptions_.begin());
    if (priorSurfaceAppearance != EffectiveSurfaceAppearance()) ApplyOpacity();
    if (selected == layoutOptions_.end() && selectedId != kFullWidgetLayoutId)
        QueueLayoutSelection(selectedId, false);
    actionFeedback_.clear();
    actionFeedbackFailure_ = false;
    const auto& selectedSnapshot = SelectedSnapshot();
    const std::wstring priorFocus = focusedElementId_;
    if (focusedElementId_.empty() ||
        !input::FindNodeInInputScope(
            selectedSnapshot, focusedElementId_, selectedSnapshot.activeInputScopeId))
        focusedElementId_ = selectedSnapshot.initialFocusId;
    focusGroupMemory_.Remember(
        admission_->widgetId, selectedSnapshot, focusedElementId_);
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId,
        &selectedSnapshot,
        admission_->runtimeGeneration,
        admission_->presentationGeneration,
        false,
    };
    const auto decision = freeScroll_.Evaluate(authority, focusedElementId_);
    const auto& binding = freeScroll_.binding();
    const bool selectedLayoutReplaced = priorLayoutId != SelectedLayoutId();
    const std::optional<widgetrail::EmbeddedMediaDocumentIdentity>
        nextMediaContract = selectedSnapshot.embeddedMediaSession
            ? std::optional{widgetrail::MakeEmbeddedMediaDocumentIdentity(
                  *selectedSnapshot.embeddedMediaSession)}
            : std::nullopt;
    const bool mediaContractReplaced =
        priorMediaContract.has_value() != nextMediaContract.has_value() ||
        (priorMediaContract && nextMediaContract &&
         !widgetrail::SameEmbeddedMediaDocumentIdentity(
             *priorMediaContract, *nextMediaContract));
    const bool compactGeometryChanged =
        !SameCompactMediaGeometryContract(
            priorMediaGeometry, selectedSnapshot.embeddedMediaSession);
    const bool preserveFreeScrollBinding = binding &&
        !selectedLayoutReplaced && priorFocus == focusedElementId_ &&
        decision.disposition == input::FreeScrollAuthorityDisposition::Current &&
        input::IsExactScrollAuthorityCurrent(
            selectedSnapshot.root, binding->scrollId, binding->axis,
            lastRenderResult_);
    if (!preserveFreeScrollBinding) {
        ClearFreeScroll();
    }
    const auto* retainedFocus = input::FindNodeInInputScope(
        selectedSnapshot, focusedElementId_, selectedSnapshot.activeInputScopeId);
    // Disabled and busy are transient states an adjustment itself provokes: a
    // widget marks its slider busy while the value change it just accepted is
    // in flight. Retiring on them would cancel the mode after every step, so
    // they only suppress new adjustments through the shared directional route
    // and the slider interaction state, exactly as the full widget does.
    if (selectedLayoutReplaced || priorFocus != focusedElementId_ ||
        priorInputScopeId != selectedSnapshot.activeInputScopeId ||
        !retainedFocus ||
        (retainedFocus->kind != L"slider" && !retainedFocus->isSelect)) {
        RetireSliderInteraction();
    } else {
        (void)sliderInteraction_.ReconcileAdmission(
            selectedSnapshot, GetTickCount64());
    }
    std::vector<WidgetSurfaceInputRequest> retainedInputRequests;
    retainedInputRequests.reserve(inputRequests_.size());
    const auto inputReconciliationTime = GetTickCount64();
    // A publish can land between the queue and its posted drain. Every request
    // is re-tested against the same exact current authority instead of being
    // dropped for arriving one frame early; only slider requests own an
    // optimistic presentation that a rejection must roll back.
    for (auto& request : inputRequests_) {
        if (IsCurrentInputRequest(request)) {
            retainedInputRequests.push_back(std::move(request));
        } else if (request.sliderActionRequest) {
            RejectInputRequest(request, inputReconciliationTime);
        }
    }
    inputRequests_.swap(retainedInputRequests);
    if (selectedLayoutReplaced) {
        if (RecordPaginationOutcome(
                sliderInteraction_.RetireScrollPagination(
                    admission_->widgetId, L"pinned-layout-replaced"))) {
            NotifyOwner();
        }
        if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
    }
    if (selectedLayoutReplaced || mediaContractReplaced ||
        (compactMediaPresentation() && compactGeometryChanged))
        mediaViewportGeometryDirty_ = true;
    if (mediaContractReplaced) {
        compactMediaPositionSeconds_ = 0.0;
        compactMediaDurationSeconds_ = 0.0;
        compactMediaPreviewSeconds_ = 0.0;
        compactMediaPlaying_ = false;
        compactMediaScrubActive_ = false;
        compactMediaSeekRequest_.reset();
        if (compactMediaPresentation())
            focusedElementId_ = L"host.compact-media.seek";
    }
    RequestPaint();
    return true;
}

std::wstring_view WidgetSurfaceCoordinator::selectedLayoutName() const noexcept {
    return selectedLayoutIndex_ < layoutOptions_.size()
        ? std::wstring_view(layoutOptions_[selectedLayoutIndex_].name)
        : std::wstring_view{};
}

bool WidgetSurfaceCoordinator::SetInteractionMode(const InteractionMode mode) {
    if (!pinned() || policy_.interactionMode() == mode) return false;
    if (mode == InteractionMode::ClickThrough) (void)ExitControllerFocus();
    policy_.SetInteractionMode(mode);
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::ToggleInteractionMode() {
    return SetInteractionMode(policy_.interactionMode() == InteractionMode::Focusable
        ? InteractionMode::ClickThrough
        : InteractionMode::Focusable);
}

bool WidgetSurfaceCoordinator::EnterControllerFocus() {
    if (!pinned() || !overlayVisible_ ||
        policy_.interactionMode() != InteractionMode::Focusable) return false;
    const auto& snapshot = SelectedSnapshot();
    if (compactMediaPresentation()) {
        RetireSliderInteraction();
        focusedElementId_ = L"host.compact-media.seek";
        controllerFocused_ = true;
        if (window_) {
            (void)SetActiveWindow(window_);
            (void)SetFocus(window_);
        }
        PublishAccessibility();
        RequestPaint();
        NotifyOwner();
        return true;
    }
    const std::wstring priorFocus = focusedElementId_;
    if (const auto visible = input::ResolveVisibleFocusTarget(
            focusedElementId_, snapshot.activeInputScopeId,
            lastRenderResult_)) {
        focusedElementId_ = *visible;
    } else if (focusedElementId_.empty()) {
        focusedElementId_ = snapshot.initialFocusId;
    }
    if (focusedElementId_ != priorFocus) {
        ClearFreeScroll();
        RetireSliderInteraction();
    }
    controllerFocused_ = true;
    if (window_) {
        (void)SetActiveWindow(window_);
        (void)SetFocus(window_);
    }
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::ExitControllerFocus() noexcept {
    if (!controllerFocused_) return false;
    if (placementSession_) (void)CancelPlacement();
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    (void)CancelCompactMediaScrub();
    controllerFocused_ = false;
    ClearFreeScroll();
    RetireSliderInteraction();
    if (admission_) {
        (void)RecordPaginationOutcome(
            sliderInteraction_.RetireScrollPagination(
                admission_->widgetId, L"pinned-focus-left"));
    }
    if (overlayVisible_ && notificationWindow_ && IsWindow(notificationWindow_))
        (void)SetFocus(notificationWindow_);
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::MoveControllerFocus(
    const input::NavigationDirection direction,
    const bool sliderAdjustmentEligible) {
    if (!controllerFocused_ || direction == input::NavigationDirection::None ||
        !pinned()) return false;
    if (compactMediaPresentation()) return true;
    const auto& snapshot = SelectedSnapshot();
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    const auto* focused = input::FindNodeInInputScope(
        snapshot, focusedElementId_, snapshot.activeInputScopeId);
    if (sliderInteraction_.selectPopup()) {
        if (!focused || !focused->isSelect ||
            !sliderInteraction_.SelectPopupCurrent(authority, *focused)) {
            (void)sliderInteraction_.CloseSelectPopup();
            RequestPaint();
            return true;
        }
        if (sliderInteraction_.MoveSelectPopup(authority, *focused, direction))
            RequestPaint();
        return true;
    }
    if (focused) {
        const bool activationRequired =
            focused->sliderInteractionMode == L"activateToAdjust";
        const bool adjustmentActive = activationRequired &&
            sliderInteraction_.SliderAdjustmentModeActive(
                authority, *focused, GetTickCount64());
        const auto route = input::RouteFocusedDirection(
            focused->kind, focused->isDisabled, focused->isBusy,
            activationRequired, adjustmentActive, direction);
        if (route == input::FocusedDirectionRoute::Consume) return true;
        if (route == input::FocusedDirectionRoute::SliderAdjustment) {
            if (!sliderAdjustmentEligible) return true;
            const auto adjustment = sliderInteraction_.AdjustSlider(
                authority, *focused, direction, GetTickCount64());
            if (adjustment.actionRequest) {
                QueueResolvedInput(
                    focused->id,
                    direction == input::NavigationDirection::Left
                        ? L"dPadLeft" : L"dPadRight",
                    ControllerInputOrigin::PhysicalController,
                    adjustment.actionRequest->requestedValue,
                    adjustment.actionRequest);
            }
            if (adjustment.visualChanged) RequestPaint();
            return adjustment.consumed;
        }
    }
    const auto applyFocus = [&](const std::wstring_view target) {
        const auto mutation = input::SurfaceInteractionTransactions::MoveFocus(
            focusedElementId_, target);
        if (!mutation.changed) return true;
        focusGroupMemory_.Remember(
            admission_->widgetId, snapshot, focusedElementId_);
        RetireSliderInteraction();
        RECT client{};
        if (!renderer_ || !window_ || !GetClientRect(window_, &client)) {
            PublishAccessibility();
            RequestPaint();
            return true;
        }
        const float scale =
            static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
        const float widthDip = static_cast<float>(client.right - client.left) / scale;
        const float heightDip = static_cast<float>(client.bottom - client.top) / scale;
        const declarative::Rect viewport{
            kSideInsetDip, kChromeHeightDip,
            std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
            std::max(1.0F, heightDip - kChromeHeightDip - kBottomInsetDip)};
        const auto plan = input::SurfaceInteractionTransactions::PlanFocusUpdate(
            *renderer_, snapshot, mutation.priorFocus,
            mutation.focusedElementId, viewport);
        if (!plan) {
            renderer_->CancelPresentationUpdatePlan();
            PublishAccessibility();
            RequestPaint();
            return true;
        }
        const auto& damage = plan->damage;
        RECT update{
            static_cast<LONG>(std::floor(damage.x * scale)),
            static_cast<LONG>(std::floor(damage.y * scale)),
            static_cast<LONG>(std::ceil((damage.x + damage.width) * scale)),
            static_cast<LONG>(std::ceil((damage.y + damage.height) * scale))};
        update.left = std::clamp<LONG>(update.left, client.left, client.right);
        update.top = std::clamp<LONG>(update.top, client.top, client.bottom);
        update.right = std::clamp<LONG>(update.right, update.left, client.right);
        update.bottom = std::clamp<LONG>(update.bottom, update.top, client.bottom);
        PublishAccessibility();
        if (update.right > update.left && update.bottom > update.top) {
            RequestPaint(&update);
        } else {
            renderer_->CancelPresentationUpdatePlan();
            RequestPaint();
        }
        return true;
    };
    if (freeScroll_.binding()) {
        auto reentry = input::SurfaceInteractionTransactions::ResolveFreeScrollReentry(
            freeScroll_, authority, focusedElementId_, lastRenderResult_);
        switch (reentry.disposition) {
        case input::FreeScrollReentryDisposition::RecoveryConsumed:
            if (reentry.target) (void)applyFocus(*reentry.target);
            return true;
        case input::FreeScrollReentryDisposition::ResumeDirectionalInput:
            break;
        case input::FreeScrollReentryDisposition::None:
            ClearFreeScroll();
            break;
        }
    }
    auto resolution = input::SurfaceInteractionTransactions::ResolveDirectionalFocus(
        snapshot, focusedElementId_, direction, lastRenderResult_,
        &focusGroupMemory_, admission_->widgetId);
    auto focusAdmission = input::AdmitDirectionalFocusResolution(
        sliderInteraction_, authority, lastRenderResult_, focusedElementId_,
        direction, std::move(resolution),
        input::ScrollPaginationIntentSource::DirectionalNavigation,
        GetTickCount64());
    if (RecordPaginationOutcome(std::move(focusAdmission.pagination)))
        NotifyOwner();
    if (focusAdmission.retainFocus) return true;
    return focusAdmission.resolution.target
        ? applyFocus(*focusAdmission.resolution.target)
        : false;
}

bool WidgetSurfaceCoordinator::HandleFocusedSliderModeButton(
    const std::wstring_view protocolButton,
    const std::uint64_t now) {
    if (!controllerFocused_ || !pinned() ||
        (protocolButton != L"a" && protocolButton != L"b")) return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* focused = input::FindNodeInInputScope(
        snapshot, focusedElementId_, snapshot.activeInputScopeId);
    if (!focused) return false;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    const bool activationRequired =
        focused->sliderInteractionMode == L"activateToAdjust";
    const bool adjustmentActive = activationRequired &&
        sliderInteraction_.SliderAdjustmentModeActive(authority, *focused, now);
    using input::FocusedSliderButtonRoute;
    switch (input::RouteFocusedSliderButton(
        focused->kind, activationRequired, adjustmentActive,
        protocolButton == L"a" ? L"A" : L"B")) {
    case FocusedSliderButtonRoute::EnterAdjustment:
        (void)sliderInteraction_.TransitionSliderAdjustmentMode(
            authority, *focused,
            input::SliderAdjustmentModeTransition::Enter, now);
        RequestPaint();
        return true;
    case FocusedSliderButtonRoute::ExitAdjustment:
        (void)sliderInteraction_.TransitionSliderAdjustmentMode(
            authority, *focused,
            input::SliderAdjustmentModeTransition::Exit, now);
        RequestPaint();
        return true;
    case FocusedSliderButtonRoute::Widget:
        return false;
    }
    return false;
}

bool WidgetSurfaceCoordinator::HandleFocusedSelectButton(
    const std::wstring_view protocolButton,
    const ControllerInputOrigin origin) {
    if (!pinned() || policy_.interactionMode() != InteractionMode::Focusable ||
        (protocolButton != L"a" && protocolButton != L"b")) return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* focused = input::FindNodeInInputScope(
        snapshot, focusedElementId_, snapshot.activeInputScopeId);
    if (!focused) return false;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    if (sliderInteraction_.selectPopup()) {
        if (protocolButton == L"b") {
            (void)sliderInteraction_.CloseSelectPopup();
            PublishAccessibility();
            RequestPaint();
            return true;
        }
        auto action = sliderInteraction_.CommitSelectPopup(authority, *focused);
        PublishAccessibility();
        RequestPaint();
        if (action) {
            QueueResolvedInput(
                focused->id, L"a", origin,
                std::nullopt, std::nullopt, action->request);
        }
        return true;
    }
    if (protocolButton == L"a") {
        const auto activation = sliderInteraction_.OpenSelectPopup(
            authority, *focused);
        if (activation == input::SelectActivationResult::Opened) {
            PublishAccessibility();
            RequestPaint();
        }
        if (activation != input::SelectActivationResult::NotSelect) return true;
    }
    return false;
}

bool WidgetSurfaceCoordinator::MoveSelectPopupWheel(const short wheelDelta) {
    if (!pinned() || policy_.interactionMode() != InteractionMode::Focusable ||
        !sliderInteraction_.selectPopup() ||
        wheelDelta == 0) return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* focused = input::FindNodeInInputScope(
        snapshot, focusedElementId_, snapshot.activeInputScopeId);
    if (!focused || !focused->isSelect) return false;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    const auto direction = wheelDelta > 0
        ? input::NavigationDirection::Up : input::NavigationDirection::Down;
    const auto steps = std::max(
        1, std::abs(static_cast<int>(wheelDelta)) / WHEEL_DELTA);
    bool changed{};
    for (int step = 0; step < steps; ++step)
        changed = sliderInteraction_.MoveSelectPopup(
            authority, *focused, direction) || changed;
    if (changed) {
        PublishAccessibility();
        RequestPaint();
    }
    return true;
}

bool WidgetSurfaceCoordinator::ScrollFocusedProjection(
    const short rightThumbX,
    const short rightThumbY,
    const std::uint64_t now) {
    const auto sample = freeScroll_.SampleRightStick(
        rightThumbX, rightThumbY, now);
    if (!pinned() || !controllerFocused_ ||
        policy_.interactionMode() != InteractionMode::Focusable ||
        !renderer_ || !window_ || focusedElementId_.empty()) {
        ClearFreeScroll();
        return false;
    }

    RECT pendingPaint{};
    RECT client{};
    if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE ||
        !GetClientRect(window_, &client)) {
        return false;
    }

    const auto& snapshot = SelectedSnapshot();
    if (snapshot.sequence <= 0 || snapshot.instanceId != admission_->instanceId ||
        snapshot.activeInputScopeId.empty()) {
        ClearFreeScroll();
        return false;
    }
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId,
        &snapshot,
        admission_->runtimeGeneration,
        admission_->presentationGeneration,
        false,
    };
    if (!sample.moving) return false;
    const float scale =
        static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
    const float widthDip = static_cast<float>(client.right - client.left) / scale;
    const float heightDip = static_cast<float>(client.bottom - client.top) / scale;
    const declarative::Rect viewport{
        kSideInsetDip,
        kChromeHeightDip,
        std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
        std::max(1.0F, heightDip - kChromeHeightDip - kBottomInsetDip),
    };
    const auto axis = sample.axis == input::FreeScrollAxis::Horizontal
        ? declarative::ScrollAxis::Horizontal
        : declarative::ScrollAxis::Vertical;
    const auto plan = input::SurfaceInteractionTransactions::PlanFreeScroll(
        freeScroll_, *renderer_, authority, focusedElementId_,
        lastRenderResult_, axis, sample.deltaDip, viewport);
    if (!plan) return false;

    const auto& damage = plan->render.damage;
    RECT update{
        static_cast<LONG>(std::floor(damage.x * scale)),
        static_cast<LONG>(std::floor(damage.y * scale)),
        static_cast<LONG>(std::ceil((damage.x + damage.width) * scale)),
        static_cast<LONG>(std::ceil((damage.y + damage.height) * scale)),
    };
    update.left = std::clamp<LONG>(update.left, client.left, client.right);
    update.top = std::clamp<LONG>(update.top, client.top, client.bottom);
    update.right = std::clamp<LONG>(update.right, update.left, client.right);
    update.bottom = std::clamp<LONG>(update.bottom, update.top, client.bottom);
    if (update.right <= update.left || update.bottom <= update.top) {
        renderer_->CancelPresentationUpdatePlan();
        return false;
    }
    RequestPaint(&update);
    (void)input::SurfaceInteractionTransactions::CommitFreeScroll(
        freeScroll_, authority, focusedElementId_, *plan);
    return true;
}

void WidgetSurfaceCoordinator::ClearFreeScroll() noexcept {
    (void)freeScroll_.Clear();
}

void WidgetSurfaceCoordinator::RetireSliderInteraction() noexcept {
    (void)sliderInteraction_.RetirePresentations();
}

void WidgetSurfaceCoordinator::TransitionPinnedFocus(
    const std::wstring_view target) {
    ClearFreeScroll();
    if (focusedElementId_ != target) RetireSliderInteraction();
    focusedElementId_ = target;
    if (admission_)
        focusGroupMemory_.Remember(
            admission_->widgetId, SelectedSnapshot(), focusedElementId_);
}

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
bool WidgetSurfaceCoordinator::SliderAdjustmentActiveForTesting(
    const std::wstring_view nodeId, const std::uint64_t now) {
    if (!admission_) return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* node = input::FindNodeInInputScope(
        snapshot, nodeId, snapshot.activeInputScopeId);
    if (!node) return false;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    return sliderInteraction_.SliderAdjustmentModeActive(authority, *node, now);
}
#endif

bool WidgetSurfaceCoordinator::RecordPaginationOutcome(
    input::ScrollPaginationSessionOutcome outcome) {
    const bool notify = outcome.dispatchReady || !outcome.diagnostics.empty();
    for (auto& diagnostic : outcome.diagnostics) {
        if (paginationDiagnostics_.size() >= kMaximumPendingInputRequests)
            paginationDiagnostics_.erase(paginationDiagnostics_.begin());
        paginationDiagnostics_.push_back(std::move(diagnostic));
    }
    return notify;
}

void WidgetSurfaceCoordinator::QueueResolvedInput(
    std::wstring nodeId,
    std::wstring protocolButton,
    const ControllerInputOrigin origin,
    const std::optional<double> requestedValue,
    std::optional<input::WidgetInteractionActionRequest> sliderActionRequest,
    std::optional<input::WidgetInteractionActionRequest> selectActionRequest) {
    if (!pinned() || policy_.interactionMode() != InteractionMode::Focusable ||
        nodeId.empty() || protocolButton.empty()) return;
    if (inputRequests_.size() >= kMaximumPendingInputRequests) {
        SetActionFeedback(L"Pinned input queue is busy. Try again.", true);
        return;
    }
    const auto& snapshot = SelectedSnapshot();
    const auto* node = input::FindNodeInInputScope(
        snapshot, nodeId, snapshot.activeInputScopeId);
    if (!node || node->isDisabled || node->isBusy) return;
    if (sliderActionRequest &&
        (node->kind != L"slider" ||
         (protocolButton != L"dPadLeft" && protocolButton != L"dPadRight") ||
         sliderActionRequest->widgetId != admission_->widgetId ||
         sliderActionRequest->widgetInstanceId != snapshot.instanceId ||
         sliderActionRequest->runtimeGeneration != admission_->runtimeGeneration ||
         sliderActionRequest->presentationGeneration !=
             admission_->presentationGeneration ||
         sliderActionRequest->inputScopeId != snapshot.activeInputScopeId ||
         sliderActionRequest->sourceElementId != node->id ||
         sliderActionRequest->actionId != node->valueChangedActionId ||
         sliderActionRequest->snapshotSequence != snapshot.sequence ||
         sliderActionRequest->requestedValue != requestedValue)) {
        if (sliderInteraction_.CancelSliderAction(
                *sliderActionRequest, GetTickCount64()).visualChanged && window_) {
            RequestPaint();
        }
        return;
    }
    if (selectActionRequest &&
        (!node->isSelect || protocolButton != L"a" ||
         selectActionRequest->widgetId != admission_->widgetId ||
         selectActionRequest->widgetInstanceId != snapshot.instanceId ||
         selectActionRequest->runtimeGeneration != admission_->runtimeGeneration ||
         selectActionRequest->presentationGeneration != admission_->presentationGeneration ||
         selectActionRequest->inputScopeId != snapshot.activeInputScopeId ||
         selectActionRequest->sourceElementId != node->id ||
         selectActionRequest->snapshotSequence != snapshot.sequence ||
         std::none_of(node->selectOptions.begin(), node->selectOptions.end(),
             [&](const WidgetSelectOption& option) {
                 return option.actionId == selectActionRequest->actionId &&
                     !option.isDisabled && !option.isBusy;
             }))) return;
    inputRequests_.push_back({
        admission_->widgetId,
        admission_->runtimeGeneration,
        snapshot.sequence,
        std::wstring(SelectedLayoutId()),
        snapshot.activeInputScopeId,
        std::move(nodeId),
        protocolButton,
        protocolButton == L"a" ? node->actionId : std::wstring{},
        requestedValue,
        std::move(sliderActionRequest),
        std::move(selectActionRequest),
        origin,
    });
    NotifyOwner();
}

bool WidgetSurfaceCoordinator::QueueFocusedInput(
    const std::wstring_view protocolButton,
    const ControllerInputOrigin origin,
    const std::optional<double> requestedValue) {
    if (!controllerFocused_ || focusedElementId_.empty()) return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* node = input::FindNodeInInputScope(
        snapshot, focusedElementId_, snapshot.activeInputScopeId);
    if (!node || node->isDisabled || node->isBusy) return false;
    QueueResolvedInput(focusedElementId_, std::wstring(protocolButton), origin,
                       requestedValue);
    return true;
}

std::vector<WidgetSurfaceInputRequest>
WidgetSurfaceCoordinator::TakeInputRequests() noexcept {
    std::vector<WidgetSurfaceInputRequest> result;
    result.swap(inputRequests_);
    return result;
}

WidgetSurfacePaginationBatch WidgetSurfaceCoordinator::TakePaginationRequests(
    const std::uint64_t now) {
    WidgetSurfacePaginationBatch batch;
    batch.diagnostics.swap(paginationDiagnostics_);
    if (!pinned()) return batch;
    const auto& snapshot = SelectedSnapshot();
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId,
        &snapshot,
        admission_->runtimeGeneration,
        admission_->presentationGeneration,
        false,
    };
    for (std::size_t index = 0;
         index < kMaximumPendingInputRequests; ++index) {
        auto [request, outcome] =
            sliderInteraction_.AcquireScrollPaginationDispatch(
                authority, lastRenderResult_, now);
        batch.diagnostics.insert(
            batch.diagnostics.end(),
            std::make_move_iterator(outcome.diagnostics.begin()),
            std::make_move_iterator(outcome.diagnostics.end()));
        if (!request) break;
        batch.requests.push_back({
            std::wstring{SelectedLayoutId()}, std::move(*request)});
    }
    return batch;
}

bool WidgetSurfaceCoordinator::IsCurrentPaginationRequest(
    const WidgetSurfacePaginationRequest& pending) const noexcept {
    if (!pinned() || pending.selectedLayoutId != SelectedLayoutId())
        return false;
    const auto& request = pending.request;
    const auto& snapshot = SelectedSnapshot();
    if (request.widgetId != admission_->widgetId ||
        request.widgetInstanceId != snapshot.instanceId ||
        request.runtimeGeneration != admission_->runtimeGeneration ||
        request.presentationGeneration != admission_->presentationGeneration ||
        request.inputScopeId != snapshot.activeInputScopeId) {
        return false;
    }
    const auto actions = input::FindScrollPaginationActions(
        snapshot.root, snapshot.activeInputScopeId, lastRenderResult_);
    return std::ranges::any_of(actions, [&](const auto& action) {
        return action.scrollId == request.action.scrollId &&
            action.actionId == request.action.actionId &&
            action.sourceElementId == request.action.sourceElementId &&
            action.edge == request.action.edge &&
            action.edgeKey == request.action.edgeKey;
    });
}

void WidgetSurfaceCoordinator::CompletePaginationRequest(
    input::ScrollPaginationDispatchOutcome outcome) {
    if (RecordPaginationOutcome(
            sliderInteraction_.CompleteScrollPaginationDispatch(outcome))) {
        NotifyOwner();
    }
}

bool WidgetSurfaceCoordinator::IsCurrentInputRequest(
    const WidgetSurfaceInputRequest& request) const noexcept {
    if (!pinned() || request.widgetId != admission_->widgetId ||
        request.runtimeGeneration != admission_->runtimeGeneration ||
        std::wstring_view(request.selectedLayoutId) != SelectedLayoutId())
        return false;
    const auto& snapshot = SelectedSnapshot();
    const auto* node = input::FindNodeInInputScope(
        snapshot, request.nodeId, request.activeInputScopeId);
    const bool sliderActionCurrent = !request.sliderActionRequest ||
        (node && node->kind == L"slider" &&
         request.sliderActionRequest->widgetId == admission_->widgetId &&
         request.sliderActionRequest->widgetInstanceId == snapshot.instanceId &&
         request.sliderActionRequest->runtimeGeneration == admission_->runtimeGeneration &&
         request.sliderActionRequest->presentationGeneration ==
             admission_->presentationGeneration &&
         request.sliderActionRequest->inputScopeId == snapshot.activeInputScopeId &&
         request.sliderActionRequest->sourceElementId == node->id &&
         request.sliderActionRequest->actionId == node->valueChangedActionId &&
         request.sliderActionRequest->snapshotSequence == request.snapshotSequence &&
         request.sliderActionRequest->requestedValue == request.requestedValue &&
         (request.protocolButton == L"dPadLeft" ||
          request.protocolButton == L"dPadRight"));
    const bool selectActionCurrent = !request.selectActionRequest ||
        (node && node->isSelect && request.protocolButton == L"a" &&
         request.selectActionRequest->widgetId == admission_->widgetId &&
         request.selectActionRequest->widgetInstanceId == snapshot.instanceId &&
         request.selectActionRequest->runtimeGeneration == admission_->runtimeGeneration &&
         request.selectActionRequest->presentationGeneration ==
             admission_->presentationGeneration &&
         request.selectActionRequest->inputScopeId == snapshot.activeInputScopeId &&
         request.selectActionRequest->sourceElementId == node->id &&
         request.selectActionRequest->snapshotSequence == request.snapshotSequence &&
         std::any_of(node->selectOptions.begin(), node->selectOptions.end(),
             [&](const WidgetSelectOption& option) {
                 return option.actionId == request.selectActionRequest->actionId &&
                     !option.isDisabled && !option.isBusy;
             }));
    return node && !node->isDisabled && !node->isBusy && sliderActionCurrent &&
        selectActionCurrent &&
        request.nodeId == focusedElementId_ &&
        request.snapshotSequence <= snapshot.sequence &&
        request.activeInputScopeId == snapshot.activeInputScopeId;
}

void WidgetSurfaceCoordinator::RejectInputRequest(
    const WidgetSurfaceInputRequest& request,
    const std::uint64_t now) noexcept {
    if (!request.requestedValue || !request.sliderActionRequest || !pinned() ||
        request.widgetId != admission_->widgetId ||
        request.runtimeGeneration != admission_->runtimeGeneration ||
        request.selectedLayoutId != SelectedLayoutId()) return;
    const auto& snapshot = SelectedSnapshot();
    if (request.activeInputScopeId != snapshot.activeInputScopeId) return;
    const auto* node = input::FindNodeInInputScope(
        snapshot, request.nodeId, snapshot.activeInputScopeId);
    if (!node || node->kind != L"slider" ||
        node->valueChangedActionId != request.sliderActionRequest->actionId ||
        snapshot.instanceId != request.sliderActionRequest->widgetInstanceId ||
        admission_->presentationGeneration !=
            request.sliderActionRequest->presentationGeneration) return;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId, &snapshot, admission_->runtimeGeneration,
        admission_->presentationGeneration, false};
    if (sliderInteraction_.CancelSliderAction(
            authority, *node, now).visualChanged && window_) {
        RequestPaint();
    }
}

std::vector<PinnedLayoutSelectionNotification>
WidgetSurfaceCoordinator::TakeLayoutSelectionNotifications() noexcept {
    std::vector<PinnedLayoutSelectionNotification> result;
    result.swap(layoutSelectionNotifications_);
    return result;
}

std::vector<std::wstring>
WidgetSurfaceCoordinator::TakeBackgroundSurfaceDiagnostics() noexcept {
    std::vector<std::wstring> result;
    result.swap(backgroundSurfaceDiagnostics_);
    return result;
}

std::vector<PinnedResizeCommitDiagnostic>
WidgetSurfaceCoordinator::TakePinnedResizeCommitDiagnostics() noexcept {
    std::vector<PinnedResizeCommitDiagnostic> result;
    result.swap(pinnedResizeCommitDiagnostics_);
    return result;
}

const WidgetSnapshot& WidgetSurfaceCoordinator::SelectedSnapshot() const noexcept {
    if (selectedLayoutIndex_ < layoutOptions_.size() &&
        layoutOptions_[selectedLayoutIndex_].projection)
        return *layoutOptions_[selectedLayoutIndex_].projection;
    return admission_->snapshot;
}

std::wstring_view WidgetSurfaceCoordinator::SelectedLayoutId() const noexcept {
    if (selectedLayoutIndex_ < layoutOptions_.size())
        return layoutOptions_[selectedLayoutIndex_].id;
    return kFullWidgetLayoutId;
}

void WidgetSurfaceCoordinator::QueueLayoutSelection(
    const std::wstring_view layoutId,
    const bool selected) {
    if (!admission_ || layoutId.empty() ||
        layoutId == kFullWidgetLayoutId ||
        layoutId == kCompactMediaLayoutId) return;
    if (layoutSelectionNotifications_.size() >=
        kMaximumPendingLayoutSelectionNotifications)
        layoutSelectionNotifications_.erase(layoutSelectionNotifications_.begin());
    layoutSelectionNotifications_.push_back({
        admission_->widgetId,
        admission_->runtimeGeneration,
        admission_->snapshot.sequence,
        std::wstring(layoutId),
        selected,
    });
    NotifyOwner();
}

void WidgetSurfaceCoordinator::SetActionFeedback(
    std::wstring message, const bool failure) {
    if (message.size() > kMaximumFeedbackCharacters)
        message.resize(kMaximumFeedbackCharacters);
    actionFeedback_ = std::move(message);
    actionFeedbackFailure_ = failure;
    PublishAccessibility();
    RequestPaint();
}

bool WidgetSurfaceCoordinator::EmergencyHideAll() noexcept {
    return Unpin(WidgetSurfaceStopReason::EmergencyHide);
}

bool WidgetSurfaceCoordinator::BeginPlacement(const PlacementMode mode) {
    if (mode == PlacementMode::Adjust) return BeginSetup(false);
    if (!pinned() || (mode != PlacementMode::Move && mode != PlacementMode::Resize &&
                      mode != PlacementMode::Adjust))
        return false;
    ClearFreeScroll();
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    RECT bounds{};
    if (!GetWindowRect(window_, &bounds)) return false;
    if (placementSession_) (void)CancelPlacement();
    placementSession_ = BeginPlacementSession(
        mode, {bounds.left, bounds.top, bounds.right, bounds.bottom},
        admission_->runtimeGeneration, admission_->presentationGeneration);
    if (!placementSession_) return false;
    if (policy_.interactionMode() != InteractionMode::Focusable) {
        policy_.SetInteractionMode(InteractionMode::Focusable);
    }
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::BeginSetup(const bool newPin) {
    if (!pinned() || layoutOptions_.empty()) return false;
    ClearFreeScroll();
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    RECT bounds{};
    if (!GetWindowRect(window_, &bounds)) return false;
    if (placementSession_) (void)CancelPlacement();
    placementSession_ = BeginPlacementSession(
        PlacementMode::Adjust, {bounds.left, bounds.top, bounds.right, bounds.bottom},
        admission_->runtimeGeneration, admission_->presentationGeneration);
    if (!placementSession_) return false;
    setupNewPin_ = newPin;
    setupOriginalLayoutId_ = std::wstring(kFullWidgetLayoutId);
    if (committedPlacement_) setupOriginalLayoutId_ = committedPlacement_->selectedLayoutId;
    if (policy_.interactionMode() != InteractionMode::Focusable) {
        policy_.SetInteractionMode(InteractionMode::Focusable);
    }
    ApplyWindowPolicy();
    (void)SetFocus(window_);
    (void)EnterControllerFocus();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::CycleLayout(const int delta) {
    if (!setupNewPin_ || !placementSession_ || layoutOptions_.empty() || delta == 0)
        return false;
    const auto count = static_cast<long long>(layoutOptions_.size());
    const auto current = static_cast<long long>(selectedLayoutIndex_);
    const auto priorId = layoutOptions_[selectedLayoutIndex_].id;
    selectedLayoutIndex_ = static_cast<std::size_t>((current + delta % count + count) % count);
    const auto& layout = layoutOptions_[selectedLayoutIndex_];
    if (priorId != layout.id) {
        mediaViewportGeometryDirty_ = true;
        ClearFreeScroll();
        RetireSliderInteraction();
        QueueLayoutSelection(priorId, false);
        QueueLayoutSelection(layout.id, true);
        focusedElementId_ = SelectedSnapshot().initialFocusId;
        if (compactMediaPresentation())
            focusedElementId_ = L"host.compact-media.seek";
        focusGroupMemory_.Remember(
            admission_->widgetId, SelectedSnapshot(), focusedElementId_);
        inputRequests_.clear();
        if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
        ApplyOpacity();
    }
    const auto monitor = CurrentWindowMonitor();
    if (!monitor) return false;
    const int width = std::max(1, static_cast<int>(std::lround(
        (layout.contentWidthDip + kSideInsetDip * 2.0F) * monitor->dpi / 96.0F)));
    const int height = std::max(1, static_cast<int>(std::lround(
        (layout.contentHeightDip + kChromeHeightDip + kBottomInsetDip) * monitor->dpi / 96.0F)));
    const auto ownerMode = placementSession_->mode;
    placementSession_->mode = PlacementMode::Resize;
    const bool changed = SetPlacementSessionBounds(
        *placementSession_,
        {placementSession_->current.left, placementSession_->current.top,
         placementSession_->current.left + width,
         placementSession_->current.top + height},
        *monitor, placementLimits_);
    placementSession_->mode = ownerMode;
    if (changed) ApplyPlacementBounds(placementSession_->current);
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::CommitSetup(std::wstring& error) {
    if (!setupNewPin_) {
        error = L"No pinned layout setup is active.";
        return false;
    }
    if (!CommitPlacement(error)) return false;
    setupNewPin_.reset();
    setupOriginalLayoutId_.clear();
    (void)ExitControllerFocus();
    (void)SetInteractionMode(InteractionMode::ClickThrough);
    error.clear();
    return true;
}

bool WidgetSurfaceCoordinator::CancelSetup() noexcept {
    if (!setupNewPin_) return false;
    const bool newPin = *setupNewPin_;
    setupNewPin_.reset();
    if (newPin) return Unpin(WidgetSurfaceStopReason::Unpin);
    const auto original = std::ranges::find_if(layoutOptions_, [&](const auto& layout) {
        return layout.id == setupOriginalLayoutId_;
    });
    const auto selectedId = layoutOptions_[selectedLayoutIndex_].id;
    if (original != layoutOptions_.end())
        selectedLayoutIndex_ = static_cast<std::size_t>(original - layoutOptions_.begin());
    const auto restoredId = layoutOptions_[selectedLayoutIndex_].id;
    if (selectedId != restoredId) {
        ClearFreeScroll();
        RetireSliderInteraction();
        QueueLayoutSelection(selectedId, false);
        QueueLayoutSelection(restoredId, true);
        focusedElementId_ = SelectedSnapshot().initialFocusId;
        focusGroupMemory_.Remember(
            admission_->widgetId, SelectedSnapshot(), focusedElementId_);
        if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
        ApplyOpacity();
    }
    setupOriginalLayoutId_.clear();
    const bool canceled = CancelPlacement();
    (void)ExitControllerFocus();
    (void)SetInteractionMode(InteractionMode::ClickThrough);
    return canceled;
}

std::optional<MonitorWorkArea> WidgetSurfaceCoordinator::CurrentWindowMonitor() const noexcept {
    if (!window_) return std::nullopt;
    const HMONITOR selected = MonitorFromWindow(window_, MONITOR_DEFAULTTONEAREST);
    MONITORINFOEXW info{sizeof(info)};
    if (!selected || !GetMonitorInfoW(selected, &info)) return std::nullopt;
    UINT dpiX = 96;
    UINT dpiY = 96;
    if (FAILED(GetDpiForMonitor(selected, MDT_EFFECTIVE_DPI, &dpiX, &dpiY)) ||
        dpiX == 0 || dpiY == 0) dpiX = 96;
    return MonitorWorkArea{
        info.szDevice,
        {info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom},
        dpiX,
        (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
    };
}

void WidgetSurfaceCoordinator::ApplyPlacementBounds(
    const PhysicalRect& bounds) noexcept {
    if (!window_) return;
    mediaViewportGeometryDirty_ = true;
    SetWindowPos(
        window_, HWND_TOPMOST, bounds.left, bounds.top,
        bounds.right - bounds.left, bounds.bottom - bounds.top,
        SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

bool WidgetSurfaceCoordinator::StepPlacement(
    const PlacementDirection direction, const float stepDip) {
    const auto monitor = CurrentWindowMonitor();
    if (!placementSession_ || !monitor ||
        !StepPlacementSession(
            *placementSession_, direction, *monitor, placementLimits_, stepDip)) return false;
    ApplyPlacementBounds(placementSession_->current);
    PublishAccessibility();
    return true;
}

bool WidgetSurfaceCoordinator::StepPlacement(
    const PlacementMode operation,
    const PlacementDirection direction,
    const float stepDip) {
    if (!placementSession_ || placementSession_->mode != PlacementMode::Adjust ||
        (operation != PlacementMode::Move && operation != PlacementMode::Resize))
        return false;
    const auto monitor = CurrentWindowMonitor();
    if (!monitor) return false;
    const auto ownerMode = placementSession_->mode;
    placementSession_->mode = operation;
    const bool changed = StepPlacementSession(
        *placementSession_, direction, *monitor, placementLimits_, stepDip);
    placementSession_->mode = ownerMode;
    if (!changed) return false;
    ApplyPlacementBounds(placementSession_->current);
    PublishAccessibility();
    return true;
}

bool WidgetSurfaceCoordinator::CommitPlacement(std::wstring& error) {
    const auto monitor = CurrentWindowMonitor();
    if (!pinned() || !placementSession_ || !monitor) {
        error = L"No pinned placement gesture is active.";
        return false;
    }
    auto committed = CommitPlacementSession(
        *placementSession_, admission_->runtimeGeneration,
        admission_->presentationGeneration, *monitor, placementLimits_);
    if (!committed) {
        (void)CancelPlacement();
        error = L"The pinned surface changed before placement could be committed.";
        return false;
    }
    committed->opacityPercent = opacityPercent_;
    if (selectedLayoutIndex_ < layoutOptions_.size())
        committed->selectedLayoutId = layoutOptions_[selectedLayoutIndex_].id;
    if (!placementStore_ ||
        !placementStore_->Save(admission_->widgetId, *committed, error)) {
        (void)CancelPlacement();
        return false;
    }
    committedPlacement_ = *committed;
    placementSession_.reset();
    setupNewPin_.reset();
    setupOriginalLayoutId_.clear();
    pointerPlacement_ = false;
    pointerPlacementMode_ = PlacementMode::None;
    if (GetCapture() == window_) ReleaseCapture();
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::CancelPlacement() noexcept {
    if (!placementSession_) return false;
    const auto original = placementSession_->original;
    placementSession_.reset();
    pointerPlacement_ = false;
    pointerPlacementMode_ = PlacementMode::None;
    if (GetCapture() == window_) ReleaseCapture();
    ApplyPlacementBounds(original);
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::ApplyOpacity() noexcept {
    if (!window_) return;
    const BYTE alpha = static_cast<BYTE>(std::lround(
        static_cast<double>(opacityPercent_) * 255.0 / 100.0));
    const bool transparent = admission_ &&
        !placementSession_ && !opacityPreviewOriginal_;
    (void)SetLayeredWindowAttributes(
        window_, transparent ? kTransparentSurfaceColorKey : 0, alpha,
        transparent ? LWA_ALPHA | LWA_COLORKEY : LWA_ALPHA);
}

surface_appearance::Mode WidgetSurfaceCoordinator::EffectiveSurfaceAppearance() const noexcept {
    if (!admission_) return surface_appearance::Mode::Solid;
    return surface_appearance::Resolve(
        SelectedSnapshot().surface ? SelectedSnapshot().surface->appearance : L"theme",
        admission_->surfaceAppearancePolicy,
        admission_->widgetId,
        true).effective;
}

void WidgetSurfaceCoordinator::SetSurfaceAppearancePolicy(
    surface_appearance::Policy policy) {
    if (!admission_) return;
    const auto prior = EffectiveSurfaceAppearance();
    admission_->surfaceAppearancePolicy = std::move(policy);
    if (prior == EffectiveSurfaceAppearance()) return;
    ApplyOpacity();
    RequestPaint();
}

bool WidgetSurfaceCoordinator::SaveCurrentState(std::wstring& error) {
    if (!pinned() || !placementStore_) {
        error = L"Pinned surface state storage is unavailable.";
        return false;
    }
    RECT bounds{};
    const auto monitor = CurrentWindowMonitor();
    if (!monitor || !GetWindowRect(window_, &bounds)) {
        error = L"Pinned surface geometry is unavailable.";
        return false;
    }
    auto current = CaptureDurablePlacement(
        *monitor, {bounds.left, bounds.top, bounds.right, bounds.bottom},
        placementLimits_);
    if (!current) {
        error = L"Pinned surface geometry is invalid.";
        return false;
    }
    current->opacityPercent = opacityPercent_;
    if (selectedLayoutIndex_ < layoutOptions_.size())
        current->selectedLayoutId = layoutOptions_[selectedLayoutIndex_].id;
    if (!placementStore_->Save(admission_->widgetId, *current, error)) return false;
    committedPlacement_ = std::move(current);
    return true;
}

bool WidgetSurfaceCoordinator::BeginOpacityAdjustment() {
    if (!pinned() || opacityPreviewOriginal_) return false;
    ClearFreeScroll();
    if (placementSession_) (void)CancelPlacement();
    opacityPreviewOriginal_ = opacityPercent_;
    if (policy_.interactionMode() != InteractionMode::Focusable) {
        policy_.SetInteractionMode(InteractionMode::Focusable);
    }
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::StepOpacity(const PlacementDirection direction) {
    if (!opacityPreviewOriginal_ ||
        (direction != PlacementDirection::Left &&
         direction != PlacementDirection::Right)) return false;
    const int delta = direction == PlacementDirection::Right ? 10 : -10;
    const auto next = static_cast<unsigned int>(std::clamp(
        static_cast<int>(opacityPercent_) + delta,
        static_cast<int>(kMinimumOpacityPercent),
        static_cast<int>(kMaximumOpacityPercent)));
    if (next == opacityPercent_) return false;
    opacityPercent_ = next;
    ApplyOpacity();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::CommitOpacity(std::wstring& error) {
    if (!opacityPreviewOriginal_) {
        error = L"No pinned opacity adjustment is active.";
        return false;
    }
    const auto original = *opacityPreviewOriginal_;
    if (!SaveCurrentState(error)) {
        opacityPercent_ = original;
        opacityPreviewOriginal_.reset();
        ApplyWindowPolicy();
        PublishAccessibility();
        RequestPaint();
        NotifyOwner();
        return false;
    }
    opacityPreviewOriginal_.reset();
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    error.clear();
    return true;
}

bool WidgetSurfaceCoordinator::CancelOpacity() noexcept {
    if (!opacityPreviewOriginal_) return false;
    opacityPercent_ = *opacityPreviewOriginal_;
    opacityPreviewOriginal_.reset();
    ApplyWindowPolicy();
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::ReconcileDisplayEnvironment() noexcept {
    ReconcileDisplayEnvironment(CurrentMonitorWorkAreas());
}

void WidgetSurfaceCoordinator::ReconcileDisplayEnvironment(
    const std::vector<MonitorWorkArea>& monitors) noexcept {
    if (!pinned()) return;
    if (placementSession_) (void)CancelPlacement();
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    const auto resolved = ResolveDurablePlacement(
        monitors, committedPlacement_, placementLimits_);
    if (!resolved) {
        (void)Unpin(WidgetSurfaceStopReason::DisplayUnavailable);
        return;
    }
    ApplyPlacementBounds(resolved->bounds);
    const auto monitor = CurrentWindowMonitor();
    if (monitor) {
        committedPlacement_ = CaptureDurablePlacement(
            *monitor, resolved->bounds, placementLimits_);
        if (committedPlacement_) committedPlacement_->opacityPercent = opacityPercent_;
        if (resolved->usedFallback && committedPlacement_ && placementStore_) {
            std::wstring ignored;
            (void)placementStore_->Save(
                admission_->widgetId, *committedPlacement_, ignored);
        }
    }
    PublishAccessibility();
}

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
void WidgetSurfaceCoordinator::ReconcileDisplayEnvironmentForTesting(
    const std::vector<MonitorWorkArea>& monitors) noexcept {
    ReconcileDisplayEnvironment(monitors);
}

std::optional<POINT> WidgetSurfaceCoordinator::PointerPointForTesting(
    const std::wstring_view nodeId) const noexcept {
    const auto region = std::ranges::find_if(
        lastRenderResult_.hitRegions,
        [&](const auto& candidate) { return candidate.nodeId == nodeId; });
    if (region == lastRenderResult_.hitRegions.end()) return std::nullopt;
    const float scale = window_
        ? static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F
        : 1.0F;
    return POINT{
        static_cast<LONG>(std::lround(
            (region->rect.x + region->rect.width * 0.5F) * scale)),
        static_cast<LONG>(std::lround(
            (region->rect.y + region->rect.height * 0.5F) * scale)),
    };
}
#endif

bool WidgetSurfaceCoordinator::Unpin(const WidgetSurfaceStopReason reason) noexcept {
    if (tearingDown_) return false;
    if (!pinned() && !window_) return false;
    // Window retirement owns pinned presentation authority from this point
    // forward. Callbacks may detach or retarget external content, so every
    // reentrant reconciliation must observe pinned() == false before that work
    // begins rather than rediscovering the retiring HWND as a valid endpoint.
    tearingDown_ = true;
    lastStopReason_ = reason;
    if (beforeWindowRetirement_) beforeWindowRetirement_(reason);
    if (selectedLayoutIndex_ < layoutOptions_.size())
        QueueLayoutSelection(layoutOptions_[selectedLayoutIndex_].id, false);
    placementSession_.reset();
    setupNewPin_.reset();
    setupOriginalLayoutId_.clear();
    opacityPreviewOriginal_.reset();
    opacityPercent_ = kMaximumOpacityPercent;
    controllerFocused_ = false;
    compactMediaPositionSeconds_ = 0.0;
    compactMediaDurationSeconds_ = 0.0;
    compactMediaPreviewSeconds_ = 0.0;
    compactMediaPlaying_ = false;
    compactMediaScrubActive_ = false;
    compactMediaSeekRequest_.reset();
    ClearFreeScroll();
    if (admission_)
        sliderInteraction_.ForgetRuntime(admission_->instanceId);
    if (admission_) focusGroupMemory_.Forget(admission_->widgetId);
    overlayVisible_ = false;
    pointerPlacement_ = false;
    pointerPlacementMode_ = PlacementMode::None;
    pointerActionNode_.clear();
    inputRequests_.clear();
    (void)sliderInteraction_.RetireScrollPagination(
        admission_ ? admission_->widgetId : std::wstring_view{},
        L"pinned-surface-retired");
    paginationDiagnostics_.clear();
    actionFeedback_.clear();
    if (renderer_ && admission_)
        renderer_->ForgetWidgetState(admission_->instanceId);
    if (GetCapture() == window_) ReleaseCapture();
    const HWND retiring = window_;
    accessibilityProvider_.Clear();
    accessibilityProvider_.Detach();
    if (retiring && IsWindow(retiring)) DestroyWindow(retiring);
    window_ = nullptr;
    ReleaseGraphicsResources();
    admission_.reset();
    layoutOptions_.clear();
    selectedLayoutIndex_ = 0;
    committedPlacement_.reset();
    focusedElementId_.clear();
    lastRenderResult_ = {};
    committedMediaViewport_.reset();
    mediaViewportGeometryDirty_ = true;
    lastPinnedClientExtent_.reset();
    pendingPinnedResizeDiagnostic_.reset();
    pinnedResizeCommitDiagnostics_.clear();
    policy_.Stop(reason == WidgetSurfaceStopReason::HostExit ||
                         reason == WidgetSurfaceStopReason::CoordinatorDisposed
                     ? StopReason::HostExit
                     : StopReason::Unpin);
    ++teardownCount_;
    tearingDown_ = false;
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::SetBeforeWindowRetirement(
    std::function<void(WidgetSurfaceStopReason)> callback) {
    beforeWindowRetirement_ = std::move(callback);
}

void WidgetSurfaceCoordinator::OnOverlayHidden() noexcept {
    overlayVisible_ = false;
    if (setupNewPin_) {
        (void)CancelSetup();
        return;
    }
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    (void)ExitControllerFocus();
    policy_.OnMainOverlayHidden();
    if (pinned() && policy_.interactionMode() == InteractionMode::Focusable)
        (void)SetInteractionMode(InteractionMode::ClickThrough);
}

void WidgetSurfaceCoordinator::OnOverlayShown() noexcept {
    overlayVisible_ = true;
    if (pinned()) NotifyOwner();
}

void WidgetSurfaceCoordinator::ReconcileCatalog(
    const std::vector<WidgetDescriptor>& descriptors) noexcept {
    if (!pinned()) return;
    const auto descriptor = std::ranges::find_if(descriptors, [&](const auto& candidate) {
        return candidate.id == admission_->widgetId;
    });
    if (descriptor == descriptors.end() || !descriptor->pinningSupported) {
        (void)Unpin(WidgetSurfaceStopReason::WidgetRemoved);
        return;
    }
    if (descriptor->instanceId != admission_->instanceId ||
        descriptor->runtimeGeneration != admission_->runtimeGeneration ||
        descriptor->presentationGeneration != admission_->presentationGeneration) {
        (void)Unpin(WidgetSurfaceStopReason::RuntimeReplaced);
    }
}

void WidgetSurfaceCoordinator::Dispose() noexcept {
    if (disposed_) return;
    if (pinned() || window_)
        (void)Unpin(WidgetSurfaceStopReason::CoordinatorDisposed);
    renderer_.reset();
    placementStore_.reset();
    d2dFactory_.Reset();
    writeFactory_.Reset();
    imageCache_ = nullptr;
    disposed_ = true;
}

bool WidgetSurfaceCoordinator::pinned() const noexcept {
    return !tearingDown_ && admission_.has_value() &&
        policy_.state() == LifecycleState::Pinned &&
        window_ && IsWindow(window_);
}

std::wstring_view WidgetSurfaceCoordinator::widgetId() const noexcept {
    return admission_ ? std::wstring_view{admission_->widgetId} : std::wstring_view{};
}

std::wstring_view WidgetSurfaceCoordinator::runtimeGeneration() const noexcept {
    return admission_ ? std::wstring_view{admission_->runtimeGeneration} : std::wstring_view{};
}

std::optional<CommittedMediaViewportPresentation>
WidgetSurfaceCoordinator::CurrentMediaViewport(
    const std::wstring_view sessionId) const noexcept {
    if (!pinned() || !committedMediaViewport_)
        return std::nullopt;
    const auto& region = committedMediaViewport_->region;
    if (region.mediaSessionId != sessionId) return std::nullopt;
    const auto& snapshot = SelectedSnapshot();
    if (!snapshot.embeddedMediaSession || snapshot.embeddedMediaSession->id != sessionId)
        return std::nullopt;
    return committedMediaViewport_;
}

bool WidgetSurfaceCoordinator::compactMediaPresentation() const noexcept {
    if (!pinned() || selectedLayoutIndex_ >= layoutOptions_.size() ||
        layoutOptions_[selectedLayoutIndex_].kind !=
            PinnedLayoutOption::Kind::CompactMedia)
        return false;
    const auto& media = SelectedSnapshot().embeddedMediaSession;
    return media && SupportsMediaPresentation(
        *media, MediaPresentationKind::CompactPinned);
}

CompactPinnedMediaState WidgetSurfaceCoordinator::compactMediaState() const noexcept {
    const auto& media = SelectedSnapshot().embeddedMediaSession;
    const double step = media && media->mediaSeekStepSeconds
        ? *media->mediaSeekStepSeconds
        : protocol_contract::DefaultMediaSeekStepSeconds;
    return {
        compactMediaPositionSeconds_, compactMediaDurationSeconds_,
        compactMediaScrubActive_ ? compactMediaPreviewSeconds_
                                 : compactMediaPositionSeconds_,
        step, compactMediaPlaying_, compactMediaScrubActive_,
        compactMediaPresentation() &&
            (controllerFocused_ || compactMediaScrubActive_ || !compactMediaPlaying_),
    };
}

void WidgetSurfaceCoordinator::UpdateCompactMediaPlayback(
    const double positionSeconds, const double durationSeconds,
    const bool playing) noexcept {
    if (!std::isfinite(positionSeconds) || !std::isfinite(durationSeconds) ||
        positionSeconds < 0.0 || durationSeconds < 0.0) return;
    compactMediaPositionSeconds_ = durationSeconds > 0.0
        ? std::clamp(positionSeconds, 0.0, durationSeconds) : 0.0;
    compactMediaDurationSeconds_ = durationSeconds;
    compactMediaPlaying_ = playing;
    if (!compactMediaScrubActive_)
        compactMediaPreviewSeconds_ = compactMediaPositionSeconds_;
    PublishAccessibility();
}

bool WidgetSurfaceCoordinator::BeginCompactMediaScrub() noexcept {
    if (!compactMediaPresentation() || !controllerFocused_ ||
        compactMediaDurationSeconds_ <= 0.0) return false;
    compactMediaPreviewSeconds_ = compactMediaPositionSeconds_;
    compactMediaScrubActive_ = true;
    PublishAccessibility();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::StepCompactMediaScrub(
    const input::NavigationDirection direction) noexcept {
    if (!compactMediaScrubActive_ || compactMediaDurationSeconds_ <= 0.0 ||
        (direction != input::NavigationDirection::Left &&
         direction != input::NavigationDirection::Right)) return false;
    const double delta = compactMediaState().seekStepSeconds *
        (direction == input::NavigationDirection::Left ? -1.0 : 1.0);
    compactMediaPreviewSeconds_ = std::clamp(
        compactMediaPreviewSeconds_ + delta, 0.0, compactMediaDurationSeconds_);
    PublishAccessibility();
    RequestPaint();
    NotifyOwner();
    return true;
}

std::optional<double> WidgetSurfaceCoordinator::CompactMediaSeekTarget(
    const input::NavigationDirection direction) const noexcept {
    if (!compactMediaPresentation() || !controllerFocused_ ||
        compactMediaDurationSeconds_ <= 0.0 ||
        (direction != input::NavigationDirection::Left &&
         direction != input::NavigationDirection::Right)) return std::nullopt;
    return ResolveBoundedMediaSeekTarget(
        compactMediaPositionSeconds_, compactMediaDurationSeconds_,
        compactMediaState().seekStepSeconds, direction);
}

std::optional<double> WidgetSurfaceCoordinator::CommitCompactMediaScrub() noexcept {
    if (!compactMediaScrubActive_) return std::nullopt;
    compactMediaScrubActive_ = false;
    const double target = compactMediaPreviewSeconds_;
    PublishAccessibility();
    NotifyOwner();
    return target;
}

bool WidgetSurfaceCoordinator::CancelCompactMediaScrub() noexcept {
    if (!compactMediaScrubActive_) return false;
    compactMediaScrubActive_ = false;
    compactMediaPreviewSeconds_ = compactMediaPositionSeconds_;
    PublishAccessibility();
    NotifyOwner();
    return true;
}

std::optional<CompactMediaSeekRequest>
WidgetSurfaceCoordinator::TakeCompactMediaSeekRequest() noexcept {
    return std::exchange(compactMediaSeekRequest_, std::nullopt);
}

bool WidgetSurfaceCoordinator::IsCurrentCompactMediaSeekRequest(
    const CompactMediaSeekRequest& request) const noexcept {
    if (!admission_ || !compactMediaPresentation() ||
        request.widgetId != admission_->widgetId ||
        request.instanceId != admission_->instanceId ||
        request.runtimeGeneration != admission_->runtimeGeneration ||
        request.presentationGeneration != admission_->presentationGeneration ||
        request.selectedLayoutId != SelectedLayoutId()) return false;
    const auto& snapshot = SelectedSnapshot();
    return snapshot.sequence == request.snapshotSequence &&
        snapshot.embeddedMediaSession &&
        snapshot.embeddedMediaSession->id == request.sessionId;
}

InteractionMode WidgetSurfaceCoordinator::interactionMode() const noexcept {
    return policy_.interactionMode();
}

WidgetSurfacePresentationState WidgetSurfaceCoordinator::presentationState() const noexcept {
    if (!pinned()) return WidgetSurfacePresentationState::Hidden;
    return policy_.interactionMode() == InteractionMode::Focusable
        ? WidgetSurfacePresentationState::PinnedInteractive
        : WidgetSurfacePresentationState::PinnedClickThrough;
}

LRESULT CALLBACK WidgetSurfaceCoordinator::WindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* coordinator = reinterpret_cast<WidgetSurfaceCoordinator*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        coordinator = static_cast<WidgetSurfaceCoordinator*>(create->lpCreateParams);
        coordinator->window_ = window;
        SetWindowLongPtrW(window, GWLP_USERDATA,
                          reinterpret_cast<LONG_PTR>(coordinator));
    }
    if (!coordinator) return DefWindowProcW(window, message, wParam, lParam);
    if (message == WM_NCDESTROY) {
        const LRESULT result = DefWindowProcW(window, message, wParam, lParam);
        coordinator->OnWindowDestroyed();
        return result;
    }
    return coordinator->HandleMessage(message, wParam, lParam);
}

LRESULT WidgetSurfaceCoordinator::HandleMessage(
    const UINT message, const WPARAM wParam, const LPARAM lParam) {
    switch (message) {
    case WM_NCHITTEST:
        return policy_.interactionMode() == InteractionMode::ClickThrough
            ? HTTRANSPARENT
            : HTCLIENT;
    case WM_MOUSEACTIVATE:
        return policy_.interactionMode() == InteractionMode::ClickThrough
            ? MA_NOACTIVATE
            : MA_ACTIVATE;
    case WM_GETOBJECT:
        return accessibilityProvider_.HandleWmGetObject(wParam, lParam);
    case WM_MOUSEWHEEL:
        if (MoveSelectPopupWheel(static_cast<short>(HIWORD(wParam)))) return 0;
        return DefWindowProcW(window_, message, wParam, lParam);
    case WM_RBUTTONUP:
        if (sliderInteraction_.selectPopup()) {
            (void)sliderInteraction_.CloseSelectPopup();
            PublishAccessibility();
            RequestPaint();
            return 0;
        }
        return DefWindowProcW(window_, message, wParam, lParam);
    case WM_SETFOCUS:
        accessibilityProvider_.SetWindowFocused(true);
        PublishAccessibility();
        return 0;
    case WM_KILLFOCUS:
        accessibilityProvider_.SetWindowFocused(false);
        if (pointerPlacement_) {
            if (setupNewPin_) (void)CancelSetup();
            else (void)CancelPlacement();
        }
        if (opacityPreviewOriginal_) (void)CancelOpacity();
        pointerActionNode_.clear();
        // Win32 keyboard focus owns pointer and keyboard input, not this
        // surface's controller-input authority. A topmost tool window loses it
        // routinely while the controller still owns the projection, so only a
        // surface without controller focus retires shared interaction state.
        // Real controller authority loss runs through ExitControllerFocus.
        if (!controllerFocused_) RetireSliderInteraction();
        if (GetCapture() == window_) ReleaseCapture();
        return 0;
    case kAccessibilityActionMessage:
        HandleAccessibilityActions();
        return 0;
    case WM_LBUTTONDOWN:
        if (policy_.interactionMode() == InteractionMode::Focusable) {
            RECT client{};
            GetClientRect(window_, &client);
            const int x = static_cast<short>(LOWORD(lParam));
            const int y = static_cast<short>(HIWORD(lParam));
            const int dpi = static_cast<int>(GetDpiForWindow(window_));
            const float scale = static_cast<float>(std::max(1, dpi)) / 96.0F;
            if (sliderInteraction_.selectPopup()) {
                const auto& snapshot = SelectedSnapshot();
                const auto* node = input::FindNodeInInputScope(
                    snapshot, focusedElementId_, snapshot.activeInputScopeId);
                const input::WidgetInteractionAuthority authority{
                    admission_->widgetId, &snapshot, admission_->runtimeGeneration,
                    admission_->presentationGeneration, false};
                const auto anchor = lastRenderResult_.focusRects.find(focusedElementId_);
                const declarative::Rect viewport{
                    kSideInsetDip, kChromeHeightDip,
                    std::max(1.0F, static_cast<float>(client.right) / scale -
                        kSideInsetDip * 2.0F),
                    std::max(1.0F, static_cast<float>(client.bottom) / scale -
                        kChromeHeightDip - kBottomInsetDip)};
                const auto layout = anchor == lastRenderResult_.focusRects.end()
                    ? input::SelectPopupLayout{}
                    : input::ComputeSelectPopupLayout(
                        anchor->second, viewport, *sliderInteraction_.selectPopup());
                const auto option = input::HitTestSelectPopup(
                    layout, static_cast<float>(x) / scale,
                    static_cast<float>(y) / scale);
                if (node && option && sliderInteraction_.HighlightSelectPopupOption(
                        authority, *node, *option)) {
                    (void)HandleFocusedSelectButton(
                        L"a", ControllerInputOrigin::PhysicalController);
                } else {
                    (void)sliderInteraction_.CloseSelectPopup();
                    PublishAccessibility();
                    RequestPaint();
                }
                return 0;
            }
            const int chrome = MulDiv(
                static_cast<int>(kChromeHeightDip), dpi, 96);
            const int fromRight = client.right - x;
            PlacementMode mode = PlacementMode::None;
            if (y >= 0 && y <= chrome) {
                if (fromRight >= MulDiv(208, dpi, 96) &&
                    fromRight < MulDiv(276, dpi, 96)) mode = PlacementMode::Move;
                else if (fromRight >= MulDiv(132, dpi, 96) &&
                         fromRight < MulDiv(208, dpi, 96)) mode = PlacementMode::Resize;
            }
            const bool placementReady = mode != PlacementMode::None &&
                (setupNewPin_ ? placementSession_.has_value() : BeginPlacement(mode));
            if (placementReady) {
                pointerPlacement_ = true;
                pointerPlacementMode_ = mode;
                GetCursorPos(&pointerStart_);
                pointerStartBounds_ = placementSession_->current;
                SetCapture(window_);
                return 0;
            }
            if (const auto hit = input::FindPointerHitTarget(
                    static_cast<float>(x) / scale,
                    static_cast<float>(y) / scale,
                    SelectedSnapshot().activeInputScopeId,
                    lastRenderResult_)) {
                TransitionPinnedFocus(hit->id);
                pointerActionNode_ = hit->enabled ? hit->id : std::wstring{};
                SetCapture(window_);
                PublishAccessibility();
                RequestPaint();
            }
        }
        return 0;
    case WM_MOUSEMOVE:
        if (pointerPlacement_ && placementSession_ && (wParam & MK_LBUTTON) != 0) {
            POINT current{};
            GetCursorPos(&current);
            const int dx = current.x - pointerStart_.x;
            const int dy = current.y - pointerStart_.y;
            auto proposed = pointerStartBounds_;
            if (pointerPlacementMode_ == PlacementMode::Move) {
                proposed.left += dx;
                proposed.right += dx;
                proposed.top += dy;
                proposed.bottom += dy;
            } else {
                proposed.right += dx;
                proposed.bottom += dy;
            }
            const auto monitor = CurrentWindowMonitor();
            const auto ownerMode = placementSession_->mode;
            placementSession_->mode = pointerPlacementMode_;
            const bool changed = monitor && SetPlacementSessionBounds(
                    *placementSession_, proposed, *monitor, placementLimits_);
            placementSession_->mode = ownerMode;
            if (changed) {
                ApplyPlacementBounds(placementSession_->current);
            }
        }
        return 0;
    case WM_LBUTTONUP:
        if (pointerPlacement_) {
            pointerPlacement_ = false;
            pointerPlacementMode_ = PlacementMode::None;
            if (GetCapture() == window_) ReleaseCapture();
            std::wstring ignored;
            if (setupNewPin_) {
                if (!CommitSetup(ignored)) (void)CancelSetup();
            } else if (!CommitPlacement(ignored)) (void)CancelPlacement();
            return 0;
        }
        if (!pointerActionNode_.empty()) {
            const std::wstring pressed = std::move(pointerActionNode_);
            pointerActionNode_.clear();
            if (GetCapture() == window_) ReleaseCapture();
            const float scale =
                static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
            const auto hit = input::FindPointerHitTarget(
                static_cast<float>(static_cast<short>(LOWORD(lParam))) / scale,
                static_cast<float>(static_cast<short>(HIWORD(lParam))) / scale,
                SelectedSnapshot().activeInputScopeId,
                lastRenderResult_);
            if (hit && hit->enabled && hit->id == pressed)
                if (!HandleFocusedSelectButton(
                        L"a", ControllerInputOrigin::PhysicalController))
                    QueueResolvedInput(
                        std::move(pressed), L"a",
                        ControllerInputOrigin::PhysicalController, std::nullopt);
            return 0;
        }
        if (policy_.interactionMode() == InteractionMode::Focusable) {
            RECT client{};
            GetClientRect(window_, &client);
            const int x = static_cast<short>(LOWORD(lParam));
            const int y = static_cast<short>(HIWORD(lParam));
            const int chrome = MulDiv(
                static_cast<int>(kChromeHeightDip),
                static_cast<int>(GetDpiForWindow(window_)), 96);
            if (y >= 0 && y <= chrome) {
                const int dpi = static_cast<int>(GetDpiForWindow(window_));
                const int fromRight = client.right - x;
                if (fromRight < MulDiv(64, dpi, 96))
                    (void)Unpin(WidgetSurfaceStopReason::Close);
                else if (fromRight < MulDiv(132, dpi, 96))
                    (void)Unpin(WidgetSurfaceStopReason::Unpin);
                else if (fromRight >= MulDiv(276, dpi, 96) &&
                         fromRight < MulDiv(390, dpi, 96))
                    (void)SetInteractionMode(InteractionMode::ClickThrough);
            }
        }
        return 0;
    case WM_CAPTURECHANGED:
        if (pointerPlacement_) {
            if (setupNewPin_) (void)CancelSetup();
            else (void)CancelPlacement();
        }
        pointerActionNode_.clear();
        return 0;
    case WM_KEYDOWN:
        if (opacityPreviewOriginal_) {
            if (wParam == VK_LEFT)
                (void)StepOpacity(PlacementDirection::Left);
            else if (wParam == VK_RIGHT)
                (void)StepOpacity(PlacementDirection::Right);
            else if (wParam == VK_RETURN) {
                std::wstring ignored;
                (void)CommitOpacity(ignored);
            } else if (wParam == VK_ESCAPE) (void)CancelOpacity();
        } else if (placementSession_) {
            const auto step = [&](const PlacementDirection direction) {
                if (placementSession_->mode == PlacementMode::Adjust)
                    (void)StepPlacement(PlacementMode::Move, direction);
                else
                    (void)StepPlacement(direction);
            };
            if (wParam == VK_LEFT) step(PlacementDirection::Left);
            else if (wParam == VK_RIGHT) step(PlacementDirection::Right);
            else if (wParam == VK_UP) step(PlacementDirection::Up);
            else if (wParam == VK_DOWN) step(PlacementDirection::Down);
            else if (wParam == VK_RETURN) {
                std::wstring ignored;
                (void)CommitPlacement(ignored);
            } else if (wParam == VK_ESCAPE) (void)CancelPlacement();
        } else if (controllerFocused_ &&
                   (wParam == VK_LEFT || wParam == VK_RIGHT ||
                    wParam == VK_UP || wParam == VK_DOWN)) {
            input::NavigationDirection direction = input::NavigationDirection::None;
            if (wParam == VK_LEFT) direction = input::NavigationDirection::Left;
            else if (wParam == VK_RIGHT) direction = input::NavigationDirection::Right;
            else if (wParam == VK_UP) direction = input::NavigationDirection::Up;
            else if (wParam == VK_DOWN) direction = input::NavigationDirection::Down;
            (void)MoveControllerFocus(direction);
        } else if (controllerFocused_ && wParam == VK_RETURN) {
            if (!HandleFocusedSelectButton(
                    L"a", ControllerInputOrigin::AccessibilityAutomation))
                (void)QueueFocusedInput(
                    L"a", ControllerInputOrigin::AccessibilityAutomation);
        } else if (wParam == VK_ESCAPE && controllerFocused_) {
            if (!HandleFocusedSelectButton(
                    L"b", ControllerInputOrigin::AccessibilityAutomation)) {
                (void)ExitControllerFocus();
                (void)SetInteractionMode(InteractionMode::ClickThrough);
            }
        } else if (wParam == 'M') (void)BeginPlacement(PlacementMode::Move);
        else if (wParam == 'R') (void)BeginPlacement(PlacementMode::Resize);
        else if (wParam == 'P') {
            if (ToggleInteractionMode() &&
                policy_.interactionMode() == InteractionMode::Focusable)
                (void)EnterControllerFocus();
        }
        else if (wParam == 'U') (void)Unpin(WidgetSurfaceStopReason::Unpin);
        return 0;
    case WM_DPICHANGED:
        mediaViewportGeometryDirty_ = true;
        ReleaseGraphicsResources();
        ReconcileDisplayEnvironment();
        return 0;
    case WM_DISPLAYCHANGE:
    case WM_SETTINGCHANGE:
        ReconcileDisplayEnvironment();
        return 0;
    case WM_SIZE:
        if (wParam != SIZE_MINIMIZED) {
            mediaViewportGeometryDirty_ = true;
            const SIZE currentClientExtent{
                static_cast<LONG>(LOWORD(lParam)), static_cast<LONG>(HIWORD(lParam))};
            if (pendingPinnedResizeDiagnostic_) {
                pendingPinnedResizeDiagnostic_->currentClientExtent = currentClientExtent;
                ++pendingPinnedResizeDiagnostic_->coalescedResizeCount;
            } else {
                pendingPinnedResizeDiagnostic_ = PendingPinnedResizeDiagnostic{
                    lastPinnedClientExtent_, currentClientExtent};
            }
            lastPinnedClientExtent_ = currentClientExtent;
        }
        if (renderTarget_ && wParam != SIZE_MINIMIZED) {
            renderTarget_->Resize(D2D1::SizeU(LOWORD(lParam), HIWORD(lParam)));
            if (renderer_) renderer_->DiscardTargetResources();
        }
        if (wParam != SIZE_MINIMIZED) RequestPaint();
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_TIMER:
        if (wParam == kBackgroundSurfaceAnimationTimer) {
            if (!renderer_ || !lastRenderResult_.succeeded) {
                KillTimer(window_, kBackgroundSurfaceAnimationTimer);
                return 0;
            }
            std::optional<declarative::Rect> damage =
                lastRenderResult_.backgroundSurfaceAnimationDamage;
            if (!damage && lastRenderResult_.backgroundSurfaceSettleWake) {
                const auto now = GetTickCount64();
                const auto deadline = lastRenderResult_
                    .backgroundSurfaceSettleWake->deadlineMilliseconds;
                if (now < deadline) {
                    const auto remaining = std::min<std::uint64_t>(
                        deadline - now,
                        std::numeric_limits<UINT>::max());
                    if (SetTimer(
                            window_, kBackgroundSurfaceAnimationTimer,
                            std::max<UINT>(
                                1U, static_cast<UINT>(remaining)), nullptr) == 0) {
                        // Retain the one-shot wake for the next externally
                        // caused paint; an immediate fallback would spin while
                        // the proposal is intentionally static.
                        KillTimer(window_, kBackgroundSurfaceAnimationTimer);
                    }
                    return 0;
                }
                KillTimer(window_, kBackgroundSurfaceAnimationTimer);
                renderer_->CancelPresentationUpdatePlan();
                RequestPaint();
                return 0;
            }
            if (!damage) {
                if (lastRenderResult_.animationActive) {
                    renderer_->CancelPresentationUpdatePlan();
                    RequestPaint();
                    return 0;
                }
                KillTimer(window_, kBackgroundSurfaceAnimationTimer);
                return 0;
            }
            RECT pendingPaint{};
            if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE) {
                renderer_->CancelPresentationUpdatePlan();
                RequestPaint();
                return 0;
            }
            const auto plan = renderer_->PlanBackgroundSurfaceAnimationFrame(
                *damage);
            if (!plan) {
                renderer_->CancelPresentationUpdatePlan();
                RequestPaint();
                return 0;
            }
            const float scale = static_cast<float>(
                std::max(1U, GetDpiForWindow(window_))) / 96.0F;
            RECT update{
                static_cast<LONG>(std::floor(plan->damage.x * scale)),
                static_cast<LONG>(std::floor(plan->damage.y * scale)),
                static_cast<LONG>(std::ceil(
                    (plan->damage.x + plan->damage.width) * scale)),
                static_cast<LONG>(std::ceil(
                    (plan->damage.y + plan->damage.height) * scale)),
            };
            RequestPaint(&update);
            return 0;
        }
        return DefWindowProcW(window_, message, wParam, lParam);
    case WM_PAINT:
        Paint();
        return 0;
    case WM_CLOSE:
        (void)Unpin(WidgetSurfaceStopReason::Close);
        return 0;
    default:
        return DefWindowProcW(window_, message, wParam, lParam);
    }
}

void WidgetSurfaceCoordinator::HandleAccessibilityActions() {
    if (!pinned()) return;
    const auto& snapshot = SelectedSnapshot();
    for (const auto& request : accessibilityProvider_.TakeActions()) {
        if (request.widgetId != admission_->widgetId ||
            request.runtimeGeneration != admission_->runtimeGeneration ||
            request.snapshotSequence != snapshot.sequence ||
            request.activeInputScopeId != snapshot.activeInputScopeId)
            continue;
        if (request.domain == accessibility::ElementDomain::Widget ||
            request.domain == accessibility::ElementDomain::WidgetOption) {
            if (request.hostAction == accessibility::HostAction::ExpandSelect) {
                if (request.kind != accessibility::ActionKind::Invoke ||
                    request.hostTargetId != request.nodeId ||
                    policy_.interactionMode() != InteractionMode::Focusable)
                    continue;
                TransitionPinnedFocus(request.nodeId);
                (void)HandleFocusedSelectButton(
                    L"a", ControllerInputOrigin::AccessibilityAutomation);
                continue;
            }
            if (request.hostAction == accessibility::HostAction::CollapseSelect) {
                if (request.kind != accessibility::ActionKind::Invoke ||
                    !sliderInteraction_.selectPopup() ||
                    sliderInteraction_.selectPopup()->openerElementId !=
                        request.hostTargetId)
                    continue;
                (void)HandleFocusedSelectButton(
                    L"b", ControllerInputOrigin::AccessibilityAutomation);
                continue;
            }
            if (request.hostAction ==
                accessibility::HostAction::CommitSelectOption) {
                if ((request.kind != accessibility::ActionKind::Invoke &&
                     request.kind != accessibility::ActionKind::Focus) ||
                    !sliderInteraction_.selectPopup() ||
                    sliderInteraction_.selectPopup()->openerElementId !=
                        request.hostTargetId)
                    continue;
                const auto* select = input::FindNodeInInputScope(
                    snapshot, request.hostTargetId, snapshot.activeInputScopeId);
                if (!select || !select->isSelect) continue;
                const auto option = std::find_if(
                    select->selectOptions.begin(), select->selectOptions.end(),
                    [&](const auto& candidate) {
                        return candidate.actionId == request.actionId;
                    });
                if (option == select->selectOptions.end()) continue;
                const input::WidgetInteractionAuthority authority{
                    admission_->widgetId, &snapshot, admission_->runtimeGeneration,
                    admission_->presentationGeneration, false};
                const auto index = static_cast<std::size_t>(
                    std::distance(select->selectOptions.begin(), option));
                if (request.kind == accessibility::ActionKind::Focus) {
                    if (!EnterControllerFocus() || GetFocus() != window_) continue;
                }
                if (!sliderInteraction_.HighlightSelectPopupOption(
                        authority, *select, index)) continue;
                if (request.kind == accessibility::ActionKind::Invoke)
                    (void)HandleFocusedSelectButton(
                        L"a", ControllerInputOrigin::AccessibilityAutomation);
                else {
                    PublishAccessibility();
                    RequestPaint();
                }
                continue;
            }
            const auto resolved = accessibility::ResolveActionRequest(
                request, admission_->widgetId, admission_->runtimeGeneration,
                snapshot);
            if (!resolved || policy_.interactionMode() != InteractionMode::Focusable)
                continue;
            if (resolved->kind == accessibility::ActionKind::Focus) {
                if (!EnterControllerFocus() || GetFocus() != window_) continue;
                TransitionPinnedFocus(resolved->nodeId);
                PublishAccessibility();
                RequestPaint();
            } else {
                QueueResolvedInput(
                    resolved->nodeId, resolved->protocolButton,
                    ControllerInputOrigin::AccessibilityAutomation,
                    resolved->requestedValue);
            }
            continue;
        }
        if (compactMediaPresentation() &&
            request.domain == accessibility::ElementDomain::HostShell &&
            request.kind == accessibility::ActionKind::SetValue &&
            request.actionId == L"host.compact-media.seek" &&
            request.requestedValue && std::isfinite(*request.requestedValue) &&
            *request.requestedValue >= 0.0 &&
            *request.requestedValue <= compactMediaDurationSeconds_) {
            compactMediaPreviewSeconds_ = *request.requestedValue;
            const auto& media = *SelectedSnapshot().embeddedMediaSession;
            compactMediaSeekRequest_ = CompactMediaSeekRequest{
                admission_->widgetId,
                admission_->instanceId,
                admission_->runtimeGeneration,
                admission_->presentationGeneration,
                media.id,
                std::wstring{SelectedLayoutId()},
                SelectedSnapshot().sequence,
                *request.requestedValue,
            };
            NotifyOwner();
            continue;
        }
        if (request.kind != accessibility::ActionKind::Invoke ||
            request.domain != accessibility::ElementDomain::HostShell)
            continue;
        if (request.actionId == L"pinned.enter") {
            (void)EnterControllerFocus();
        } else if (request.actionId == L"pinned.exit") {
            (void)ExitControllerFocus();
        } else if (request.actionId == L"pinned.move")
            (void)BeginPlacement(PlacementMode::Move);
        else if (request.actionId == L"pinned.resize")
            (void)BeginPlacement(PlacementMode::Resize);
        else if (request.actionId == L"pinned.adjust")
            (void)BeginPlacement(PlacementMode::Adjust);
        else if (request.actionId == L"pinned.opacity")
            (void)BeginOpacityAdjustment();
        else if (request.actionId == L"pinned.commit") {
            std::wstring ignored;
            if (opacityPreviewOriginal_) (void)CommitOpacity(ignored);
            else if (setupNewPin_) (void)CommitSetup(ignored);
            else (void)CommitPlacement(ignored);
        } else if (request.actionId == L"pinned.cancel") {
            if (opacityPreviewOriginal_) (void)CancelOpacity();
            else if (setupNewPin_) (void)CancelSetup();
            else (void)CancelPlacement();
        }
        else if (request.actionId == L"pinned.clickthrough")
            (void)SetInteractionMode(InteractionMode::ClickThrough);
        else if (request.actionId == L"pinned.unpin")
            (void)Unpin(WidgetSurfaceStopReason::Unpin);
        else if (request.actionId == L"pinned.close")
            (void)Unpin(WidgetSurfaceStopReason::Close);
        else if (request.actionId == L"pinned.emergency")
            (void)EmergencyHideAll();
        if (!pinned()) break;
    }
    accessibilityProvider_.RaisePendingEvents();
}

bool WidgetSurfaceCoordinator::CreateWindowForAdmission(std::wstring& error) {
    const auto monitors = CurrentMonitorWorkAreas();
    const auto persisted = placementStore_
        ? placementStore_->Load(admission_->widgetId)
        : std::optional<DurablePinnedPlacement>{};
    if (persisted) {
        const auto selected = std::ranges::find_if(layoutOptions_, [&](const auto& layout) {
            return layout.id == persisted->selectedLayoutId;
        });
        if (selected != layoutOptions_.end())
            selectedLayoutIndex_ = static_cast<std::size_t>(selected - layoutOptions_.begin());
    }
    const auto& initialLayout = layoutOptions_[selectedLayoutIndex_];
    const auto placement = ResolveDurablePlacement(
        monitors, persisted, placementLimits_,
        std::pair{
            initialLayout.contentWidthDip + kSideInsetDip * 2.0F,
            initialLayout.contentHeightDip + kChromeHeightDip + kBottomInsetDip});
    if (!placement) {
        error = L"No valid monitor work area is available for a pinned surface.";
        return false;
    }
    window_ = CreateWindowExW(
        ExtendedStyle(policy_.interactionMode()), kWindowClass, kWindowTitle, WS_POPUP,
        placement->bounds.left, placement->bounds.top,
        placement->bounds.right - placement->bounds.left,
        placement->bounds.bottom - placement->bounds.top,
        nullptr, nullptr, instance_, this);
    if (!window_) {
        error = L"The host could not create the pinned surface window (Win32 " +
            std::to_wstring(GetLastError()) + L").";
        return false;
    }
    accessibilityProvider_.Bind(window_, kAccessibilityActionMessage);
    opacityPercent_ = persisted
        ? std::clamp(persisted->opacityPercent,
                     kMinimumOpacityPercent, kMaximumOpacityPercent)
        : kMaximumOpacityPercent;
    ApplyOpacity();
    ShowWindow(window_, SW_SHOWNORMAL);
    SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    const auto monitor = std::ranges::find_if(monitors, [&](const auto& candidate) {
        return candidate.stableId == placement->monitorId;
    });
    if (monitor != monitors.end())
        committedPlacement_ = CaptureDurablePlacement(
            *monitor, placement->bounds, placementLimits_);
    if (committedPlacement_) committedPlacement_->opacityPercent = opacityPercent_;
    if (committedPlacement_)
        committedPlacement_->selectedLayoutId = layoutOptions_[selectedLayoutIndex_].id;
    RequestPaint();
    error.clear();
    return true;
}

bool WidgetSurfaceCoordinator::EnsureGraphicsResources() {
    if (renderTarget_ && backgroundBrush_ && chromeBrush_ && textBrush_ &&
        secondaryBrush_ && titleFormat_ && chromeFormat_) return true;
    RECT client{};
    if (!window_ || !GetClientRect(window_, &client)) return false;
    const auto size = D2D1::SizeU(
        static_cast<UINT32>(std::max(1L, client.right - client.left)),
        static_cast<UINT32>(std::max(1L, client.bottom - client.top)));
    if (FAILED(d2dFactory_->CreateHwndRenderTarget(
            D2D1::RenderTargetProperties(),
            D2D1::HwndRenderTargetProperties(window_, size),
            renderTarget_.ReleaseAndGetAddressOf()))) return false;
    HIGHCONTRASTW highContrast{sizeof(highContrast)};
    const bool systemHighContrast =
        SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(highContrast),
                              &highContrast, 0) &&
        (highContrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
    const auto fromSystem = [](const int index) {
        const COLORREF color = GetSysColor(index);
        return D2D1::ColorF(
            static_cast<float>(GetRValue(color)) / 255.0F,
            static_cast<float>(GetGValue(color)) / 255.0F,
            static_cast<float>(GetBValue(color)) / 255.0F);
    };
    const auto background = systemHighContrast
        ? fromSystem(COLOR_WINDOW)
        : D2D1::ColorF(0x16212E);
    const auto chrome = systemHighContrast
        ? fromSystem(COLOR_HIGHLIGHT)
        : D2D1::ColorF(0x24384D);
    const auto text = systemHighContrast
        ? fromSystem(COLOR_WINDOWTEXT)
        : D2D1::ColorF(0xFFFFFF);
    const auto secondary = systemHighContrast
        ? fromSystem(COLOR_HIGHLIGHTTEXT)
        : D2D1::ColorF(0xAFC4D8);
    if (FAILED(renderTarget_->CreateSolidColorBrush(
            background, backgroundBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            chrome, chromeBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            text, textBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            secondary, secondaryBrush_.ReleaseAndGetAddressOf()))) return false;
    if (FAILED(writeFactory_->CreateTextFormat(
            L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 16.0F, L"en-us",
            titleFormat_.ReleaseAndGetAddressOf())) ||
        FAILED(writeFactory_->CreateTextFormat(
            L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 12.0F, L"en-us",
            chromeFormat_.ReleaseAndGetAddressOf()))) return false;
    return true;
}

void WidgetSurfaceCoordinator::Paint() {
    ++workCounters_.paintMessages;
    PAINTSTRUCT paint{};
    BeginPaint(window_, &paint);
    if (!pinned() || !EnsureGraphicsResources()) {
        EndPaint(window_, &paint);
        return;
    }
    RECT client{};
    GetClientRect(window_, &client);
    const float dpiScale = static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
    const float widthDip = static_cast<float>(client.right - client.left) / dpiScale;
    const float heightDip = static_cast<float>(client.bottom - client.top) / dpiScale;
    renderTarget_->SetDpi(96.0F * dpiScale, 96.0F * dpiScale);
    ++workCounters_.rasterDraws;
    renderTarget_->BeginDraw();
    const bool adjustmentActive =
        placementSession_.has_value() || opacityPreviewOriginal_.has_value();
    const bool transparent = !adjustmentActive;
    renderTarget_->Clear(transparent
        ? D2D1::ColorF(1.0F / 255.0F, 2.0F / 255.0F, 3.0F / 255.0F, 1.0F)
        : D2D1::ColorF(0x16212E));
    const bool compactMedia = compactMediaPresentation();
    const bool showHostSetupChrome = compactMedia && adjustmentActive;
    const bool showChrome = adjustmentActive;
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    lastHostCanvasTransparentForTesting_ = transparent;
    lastHostChromeVisibleForTesting_ = showChrome;
    lastHostBorderVisibleForTesting_ = adjustmentActive || controllerFocused_;
    lastHostBorderUsesFocusColorForTesting_ =
        !adjustmentActive && controllerFocused_;
    lastHostBorderDipForTesting_ = adjustmentActive
        ? kAdjustBorderDip
        : controllerFocused_ ? kPinnedBorderDip : 0.0F;
#endif
    if (showChrome) {
        renderTarget_->FillRectangle(
            D2D1::RectF(0, 0, widthDip, kChromeHeightDip), chromeBrush_.Get());
        const std::wstring title = showHostSetupChrome
            ? std::wstring(selectedLayoutName()) : admission_->name;
        renderTarget_->DrawTextW(
            title.c_str(), static_cast<UINT32>(title.size()),
            titleFormat_.Get(), D2D1::RectF(
                kSideInsetDip, 7.0F,
                std::max(kSideInsetDip + 1.0F, widthDip * 0.40F), 31.0F),
            textBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }
    std::wstring chrome;
    if (opacityPreviewOriginal_) {
        chrome = L"Opacity " + std::to_wstring(opacityPercent_) +
            L"% · Left/Right adjust · A save · B cancel";
    } else if (placementSession_) {
        if (placementSession_->mode == PlacementMode::Move) {
            chrome = L"Moving · D-pad move · A save · B cancel";
        } else if (placementSession_->mode == PlacementMode::Resize) {
            chrome = L"Resizing · D-pad resize · A save · B cancel";
        } else {
            const auto& bounds = placementSession_->current;
            chrome = selectedLayoutName().empty() ? L"Full widget" : std::wstring(selectedLayoutName());
            chrome += L" " + std::to_wstring(selectedLayoutIndex_ + 1) + L"/" +
                std::to_wstring(layoutOptions_.size()) +
                L" · LT/RT layout · left stick/D-pad move · right stick resize · A commit · B cancel · " +
                std::to_wstring(bounds.right - bounds.left) + L"x" +
                std::to_wstring(bounds.bottom - bounds.top);
        }
    } else {
        chrome = policy_.interactionMode() == InteractionMode::Focusable
            ? L"Interactive · A activate · B widget back · View tray"
            : L"Click-through · View enters · Menu options";
    }
    if (showChrome) {
        renderTarget_->DrawTextW(
            chrome.c_str(), static_cast<UINT32>(chrome.size()), chromeFormat_.Get(),
            D2D1::RectF(std::max(kSideInsetDip, widthDip * 0.42F), 9.0F,
                        widthDip - kSideInsetDip, 31.0F),
            secondaryBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }
    DeclarativeRenderOptions options;
    options.pixelScale = dpiScale;
    options.collectAccessibility = ResolveSurfacePresentationPolicy(
        policy_.interactionMode()).exposeInteractiveSemantics;
    options.responsiveViewport = {widthDip, heightDip};
    options.surfaceBackground = NativeColor{22.0F / 255.0F, 33.0F / 255.0F, 46.0F / 255.0F, 1.0F};
    options.accessibility.reducedMotion = true;
    options.animationTimestampMilliseconds = GetTickCount64();
    const auto& selectedSnapshot = SelectedSnapshot();
    auto sliderPresentation = sliderInteraction_.PrepareRenderPresentation(
        selectedSnapshot,
        controllerFocused_ ? std::wstring_view{focusedElementId_}
                           : std::wstring_view{},
        GetTickCount64(), controllerFocused_, controllerFocused_,
        controllerFocused_);
    options.sliderValueOverrides =
        std::move(sliderPresentation.sliderValueOverrides);
    options.pressedElementId =
        std::move(sliderPresentation.pressedElementId);
    options.artworkWidgetId = admission_->widgetId;
    options.artworkAuthorityId = admission_->widgetId + L"\x1f" +
        admission_->runtimeGeneration + L"\x1f" +
        admission_->presentationGeneration;
    const input::WidgetInteractionAuthority authority{
        admission_->widgetId,
        &selectedSnapshot,
        admission_->runtimeGeneration,
        admission_->presentationGeneration,
        false,
    };
    const auto freeScrollDecision =
        input::SurfaceInteractionTransactions::EvaluateFreeScroll(
            freeScroll_, authority, focusedElementId_, lastRenderResult_);
    if (freeScroll_.binding() &&
        freeScrollDecision.disposition !=
            input::FreeScrollAuthorityDisposition::Current) {
        ClearFreeScroll();
    }
    options.suppressFocusedDescendantFollow =
        freeScrollDecision.followSuppressed;
    const declarative::Rect viewport = compactMedia && !showHostSetupChrome
        ? declarative::Rect{
              kPinnedBorderDip, kPinnedBorderDip,
              std::max(1.0F, widthDip - kPinnedBorderDip * 2.0F),
              std::max(1.0F, heightDip - kPinnedBorderDip * 2.0F)}
        : declarative::Rect{
              kSideInsetDip, kChromeHeightDip,
              std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
              std::max(1.0F, heightDip - kChromeHeightDip - kBottomInsetDip)};
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    lastContentViewportForTesting_ = viewport;
#endif
    RenderResult renderResult;
    if (compactMedia && selectedSnapshot.embeddedMediaSession) {
        const float aspect = static_cast<float>(selectedSnapshot.embeddedMediaSession->aspectRatio);
        float mediaWidth = viewport.width;
        float mediaHeight = mediaWidth / aspect;
        if (mediaHeight > viewport.height) {
            mediaHeight = viewport.height;
            mediaWidth = mediaHeight * aspect;
        }
        const declarative::Rect mediaBounds{
            viewport.x + (viewport.width - mediaWidth) * 0.5F,
            viewport.y + (viewport.height - mediaHeight) * 0.5F,
            mediaWidth, mediaHeight};
        renderResult.succeeded = true;
        renderResult.responsiveSurface = ResponsiveSurfacePresentation{
            {viewport.width, viewport.height}, ResponsiveSurfaceMode::Compact};
        renderResult.mediaViewportRegions.push_back({
            L"host.compact-media.viewport", selectedSnapshot.embeddedMediaSession->id,
            mediaBounds, mediaBounds});
    } else {
        renderResult = renderer_->Render(
            renderTarget_.Get(), selectedSnapshot,
            controllerFocused_
                ? std::wstring_view{focusedElementId_}
                : std::wstring_view{},
            viewport, options);
    }
    if (!compactMedia && sliderInteraction_.selectPopup()) {
        const auto* node = input::FindNodeInInputScope(
            selectedSnapshot, focusedElementId_, selectedSnapshot.activeInputScopeId);
        const auto anchor = renderResult.focusRects.find(focusedElementId_);
        if (node && node->isSelect && anchor != renderResult.focusRects.end() &&
            sliderInteraction_.SelectPopupCurrent(authority, *node)) {
            const auto popup = input::ComputeSelectPopupLayout(
                anchor->second, viewport, *sliderInteraction_.selectPopup());
            const auto& binding = *sliderInteraction_.selectPopup();
            const D2D1_ROUNDED_RECT panel{
                D2D1::RectF(
                    popup.bounds.x, popup.bounds.y,
                    popup.bounds.x + popup.bounds.width,
                    popup.bounds.y + popup.bounds.height),
                8.0F, 8.0F};
            renderTarget_->FillRoundedRectangle(panel, backgroundBrush_.Get());
            renderTarget_->DrawRoundedRectangle(panel, textBrush_.Get(), 1.5F);
            const auto priorParagraphAlignment =
                chromeFormat_->GetParagraphAlignment();
            const bool popupTextCentered = SUCCEEDED(
                chromeFormat_->SetParagraphAlignment(
                    DWRITE_PARAGRAPH_ALIGNMENT_CENTER));
            for (const auto& item : popup.items) {
                if (item.optionIndex >= binding.options.size()) continue;
                const auto& option = binding.options[item.optionIndex];
                if (item.optionIndex == binding.highlightedOption) {
                    renderTarget_->FillRoundedRectangle(
                        D2D1::RoundedRect(
                            D2D1::RectF(
                                item.bounds.x + 3.0F, item.bounds.y + 3.0F,
                                item.bounds.x + item.bounds.width - 3.0F,
                                item.bounds.y + item.bounds.height - 3.0F),
                            5.0F, 5.0F),
                        chromeBrush_.Get());
                }
                widgetrail::icons::NativeIcon icon{};
                const bool hasIcon = !option.glyph.empty() &&
                    widgetrail::icons::TryParseNativeIcon(option.glyph, icon);
                const auto content = input::ComputeSelectPopupContentLayout(
                    item.bounds, option.isSelected, hasIcon, 10.0F, 8.0F);
                if (content.checkmarkBounds) {
                    const std::wstring check = L"✓";
                    const auto& bounds = *content.checkmarkBounds;
                    renderTarget_->DrawTextW(
                        check.c_str(), static_cast<UINT32>(check.size()),
                        chromeFormat_.Get(),
                        D2D1::RectF(
                            bounds.x, bounds.y,
                            bounds.x + bounds.width, bounds.y + bounds.height),
                        option.isDisabled || option.isBusy
                            ? secondaryBrush_.Get() : textBrush_.Get(),
                        D2D1_DRAW_TEXT_OPTIONS_CLIP);
                }
                if (content.glyphBounds) {
                    const auto& bounds = *content.glyphBounds;
                    (void)widgetrail::icons::DrawNativeIcon(
                        renderTarget_.Get(), icon,
                        D2D1::RectF(
                            bounds.x, bounds.y,
                            bounds.x + bounds.width, bounds.y + bounds.height),
                        option.isDisabled || option.isBusy
                            ? secondaryBrush_.Get() : textBrush_.Get(),
                        1.7F);
                }
                const auto& labelBounds = content.labelBounds;
                renderTarget_->DrawTextW(
                    option.label.c_str(),
                    static_cast<UINT32>(option.label.size()),
                    chromeFormat_.Get(),
                    D2D1::RectF(
                        labelBounds.x, labelBounds.y,
                        labelBounds.x + labelBounds.width,
                        labelBounds.y + labelBounds.height),
                    option.isDisabled || option.isBusy
                        ? secondaryBrush_.Get() : textBrush_.Get(),
                    D2D1_DRAW_TEXT_OPTIONS_CLIP);
            }
            if (popupTextCentered) {
                (void)chromeFormat_->SetParagraphAlignment(
                    priorParagraphAlignment);
            }
        } else {
            (void)sliderInteraction_.CloseSelectPopup();
        }
    }
    if (!compactMedia && options.suppressFocusedDescendantFollow &&
        !input::SurfaceInteractionTransactions::EvaluateFreeScroll(
            freeScroll_, authority, focusedElementId_, renderResult)
             .followSuppressed) {
        ClearFreeScroll();
    }
    if (adjustmentActive || controllerFocused_) {
        renderTarget_->DrawRectangle(
            D2D1::RectF(0.5F, 0.5F, std::max(0.5F, widthDip - 0.5F),
                        std::max(0.5F, heightDip - 0.5F)),
            adjustmentActive ? chromeBrush_.Get() : textBrush_.Get(),
            adjustmentActive ? kAdjustBorderDip : kPinnedBorderDip);
    }
    const HRESULT result = renderTarget_->EndDraw();
    bool mediaViewportReconciled{};
    bool backgroundSurfaceDiagnosticsQueued{};
    bool pinnedResizeDiagnosticsQueued{};
    input::ScrollPaginationSessionOutcome paginationOutcome;
    if (result == D2DERR_RECREATE_TARGET) ReleaseGraphicsResources();
    else if (SUCCEEDED(result)) {
        constexpr std::size_t maximumPendingDiagnostics = 16;
        for (const auto& diagnostic : renderResult.diagnostics) {
            if (!diagnostic.code.starts_with(L"background_crossfade_")) continue;
            if (backgroundSurfaceDiagnostics_.size() >= maximumPendingDiagnostics)
                backgroundSurfaceDiagnostics_.erase(
                    backgroundSurfaceDiagnostics_.begin());
            backgroundSurfaceDiagnostics_.push_back(
                diagnostic.code + L" [" + diagnostic.nodeId + L"] " +
                diagnostic.message);
            backgroundSurfaceDiagnosticsQueued = true;
        }
        if (!renderResult.succeeded) {
            KillTimer(window_, kBackgroundSurfaceAnimationTimer);
        } else if (renderResult.backgroundSurfaceAnimationDamage) {
            if (SetTimer(
                    window_, kBackgroundSurfaceAnimationTimer,
                    kBackgroundSurfaceAnimationTimerMilliseconds, nullptr) == 0) {
                renderer_->CancelPresentationUpdatePlan();
                RequestPaint();
            }
        } else if (renderResult.backgroundSurfaceSettleWake) {
            const auto now = GetTickCount64();
            const auto deadline =
                renderResult.backgroundSurfaceSettleWake->deadlineMilliseconds;
            const auto remaining = deadline > now
                ? std::min<std::uint64_t>(
                    deadline - now, std::numeric_limits<UINT>::max())
                : std::uint64_t{1};
            if (SetTimer(
                    window_, kBackgroundSurfaceAnimationTimer,
                    std::max<UINT>(
                        1U, static_cast<UINT>(remaining)), nullptr) == 0) {
                // A later input/image-ready paint can republish or consume the
                // retained wake without a tight pre-deadline repaint loop.
                KillTimer(window_, kBackgroundSurfaceAnimationTimer);
            }
        } else if (renderResult.animationActive) {
            if (SetTimer(
                    window_, kBackgroundSurfaceAnimationTimer,
                    kBackgroundSurfaceAnimationTimerMilliseconds, nullptr) == 0) {
                renderer_->CancelPresentationUpdatePlan();
                RequestPaint();
            }
        } else {
            KillTimer(window_, kBackgroundSurfaceAnimationTimer);
        }
        lastRenderResult_ = std::move(renderResult);
        paginationOutcome = sliderInteraction_.ReconcileScrollPagination(
            authority, lastRenderResult_, GetTickCount64());
        if (mediaViewportGeometryDirty_) {
            if (lastRenderResult_.succeeded &&
                lastRenderResult_.mediaViewportRegions.size() == 1) {
                committedMediaViewport_ = CommittedMediaViewportPresentation{
                    lastRenderResult_.mediaViewportRegions.front(),
                    nextCommittedFrameGeneration_++,
                };
            } else {
                committedMediaViewport_.reset();
            }
            mediaViewportGeometryDirty_ = false;
            mediaViewportReconciled = true;
            ++workCounters_.mediaViewportReconciliations;
            if (pendingPinnedResizeDiagnostic_) {
                if (committedMediaViewport_) {
                    constexpr std::size_t maximumPendingResizeDiagnostics = 16;
                    if (pinnedResizeCommitDiagnostics_.size() >=
                        maximumPendingResizeDiagnostics) {
                        pinnedResizeCommitDiagnostics_.erase(
                            pinnedResizeCommitDiagnostics_.begin());
                    }
                    pinnedResizeCommitDiagnostics_.push_back({
                        pendingPinnedResizeDiagnostic_->previousClientExtent,
                        pendingPinnedResizeDiagnostic_->currentClientExtent,
                        pendingPinnedResizeDiagnostic_->coalescedResizeCount,
                        *committedMediaViewport_,
                    });
                    pinnedResizeDiagnosticsQueued = true;
                }
                pendingPinnedResizeDiagnostic_.reset();
            }
        }
        PublishAccessibility();
    } else {
        KillTimer(window_, kBackgroundSurfaceAnimationTimer);
    }
    EndPaint(window_, &paint);
    const bool paginationNotification =
        RecordPaginationOutcome(std::move(paginationOutcome));
    if (SUCCEEDED(result) &&
        (mediaViewportReconciled || paginationNotification)) NotifyOwner();
    if (SUCCEEDED(result) && backgroundSurfaceDiagnosticsQueued)
        NotifyOwner(kBackgroundSurfaceDiagnosticNotification);
    if (SUCCEEDED(result) && pinnedResizeDiagnosticsQueued)
        NotifyOwner(kPinnedResizeDiagnosticNotification);
}

void WidgetSurfaceCoordinator::PublishAccessibility() {
    if (!pinned()) return;
    const auto& snapshot = SelectedSnapshot();
    RECT client{};
    GetClientRect(window_, &client);
    const float scale =
        static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
    const float widthDip = static_cast<float>(client.right - client.left) / scale;
    const float heightDip = static_cast<float>(client.bottom - client.top) / scale;
    accessibility::Tree tree{
        admission_->widgetId,
        admission_->runtimeGeneration,
        snapshot.sequence,
        snapshot.activeInputScopeId,
    };
    tree.name = admission_->name + L" pinned surface";
    const bool compactMedia = compactMediaPresentation();
    accessibility::Node heading;
    heading.id = L"pinned.heading";
    heading.name = compactMedia && snapshot.embeddedMediaSession
        ? snapshot.embeddedMediaSession->accessibleName : tree.name;
    heading.domain = accessibility::ElementDomain::HostShell;
    heading.role = accessibility::Role::Heading;
    heading.keyboardFocusable = false;
    heading.bounds = {
        kSideInsetDip, 6.0F,
        std::max(1.0F, widthDip * 0.28F - kSideInsetDip), 26.0F};
    tree.nodes.push_back(std::move(heading));
    accessibility::Node state;
    state.id = L"pinned.mode";
    state.name = L"Pinned surface mode";
    if (opacityPreviewOriginal_) {
        state.value = L"Opacity adjustment. Left or Right changes opacity by 10 percent. A saves. B cancels.";
    } else if (placementSession_) {
        if (placementSession_->mode == PlacementMode::Move)
            state.value = L"Move mode. Direction changes position. Commit or cancel.";
        else if (placementSession_->mode == PlacementMode::Resize)
            state.value = L"Resize mode. Direction changes size. Commit or cancel.";
        else
            state.value = std::wstring(selectedLayoutName()) + L", layout " +
                std::to_wstring(selectedLayoutIndex_ + 1) + L" of " +
                std::to_wstring(layoutOptions_.size()) +
                L". Left or right trigger changes layout. Left stick or D-pad moves. Right stick resizes. Commit or cancel.";
    } else if (compactMedia) {
        const auto& commands = snapshot.embeddedMediaSession->commands;
        const auto supports = [&](const std::wstring_view command) {
            return std::ranges::find(commands, command) != commands.end();
        };
        state.value = L"Compact media.";
        if (supports(L"seekBackward"))
            state.value += L" Left trigger rewinds by the configured seek interval.";
        if (supports(L"seekForward"))
            state.value += L" Right trigger forwards by the configured seek interval.";
        if (supports(L"togglePlayback"))
            state.value += L" X plays or pauses.";
        if (supports(L"navigatePrevious"))
            state.value += L" Left bumper selects previous media.";
        if (supports(L"navigateNext"))
            state.value += L" Right bumper selects next media.";
        state.value += L" B exits to click-through. View returns to the prior overlay location.";
    } else {
        state.value = policy_.interactionMode() == InteractionMode::Focusable
            ? L"Interactive. D-pad navigates. A activates. B is widget Back. View returns to the prior overlay location. Menu opens options."
            : L"Click-through. From the tray or open overlay widget, View enters this pin. Menu opens options from the tray.";
    }
    state.domain = accessibility::ElementDomain::HostShell;
    state.role = accessibility::Role::Status;
    state.keyboardFocusable = false;
    state.bounds = {
        widthDip * 0.30F, 6.0F,
        std::max(1.0F, widthDip * 0.31F), 26.0F};
    tree.nodes.push_back(std::move(state));
    if (!actionFeedback_.empty()) {
        accessibility::Node feedback;
        feedback.id = L"pinned.feedback";
        feedback.name = actionFeedbackFailure_
            ? L"Pinned action failed"
            : L"Pinned action status";
        feedback.value = actionFeedback_;
        feedback.domain = accessibility::ElementDomain::HostShell;
        feedback.role = accessibility::Role::Status;
        feedback.liveSetting = actionFeedbackFailure_
            ? accessibility::LiveSetting::Assertive
            : accessibility::LiveSetting::Polite;
        feedback.keyboardFocusable = false;
        feedback.bounds = {
            widthDip * 0.63F, 6.0F,
            std::max(1.0F, widthDip * 0.37F - kSideInsetDip), 26.0F};
        tree.nodes.push_back(std::move(feedback));
    }

    struct HostActionSpec final {
        std::wstring id;
        std::wstring name;
        std::wstring actionId;
    };
    std::vector<HostActionSpec> actions;
    if (policy_.interactionMode() == InteractionMode::Focusable) {
        if (opacityPreviewOriginal_) {
            actions.push_back({L"pinned.commit", L"Save opacity", L"pinned.commit"});
            actions.push_back({L"pinned.cancel", L"Cancel opacity", L"pinned.cancel"});
        } else if (placementSession_) {
            actions.push_back({L"pinned.commit", L"Commit placement", L"pinned.commit"});
            actions.push_back({L"pinned.cancel", L"Cancel placement", L"pinned.cancel"});
        } else {
            actions.push_back({controllerFocused_ ? L"pinned.exit" : L"pinned.enter",
                               controllerFocused_ ? L"Return focus to overlay"
                                                  : L"Enter pinned surface",
                               controllerFocused_ ? L"pinned.exit" : L"pinned.enter"});
            actions.push_back({L"pinned.move", L"Move pinned surface", L"pinned.move"});
            actions.push_back({L"pinned.resize", L"Resize pinned surface", L"pinned.resize"});
            actions.push_back({L"pinned.adjust", L"Adjust pinned surface", L"pinned.adjust"});
            actions.push_back({L"pinned.opacity", L"Adjust pinned opacity", L"pinned.opacity"});
        }
        actions.push_back({L"pinned.clickthrough", L"Make click-through",
                           L"pinned.clickthrough"});
        actions.push_back({L"pinned.unpin", L"Unpin surface", L"pinned.unpin"});
        actions.push_back({L"pinned.close", L"Close pinned surface", L"pinned.close"});
        actions.push_back({L"pinned.emergency", L"Emergency hide all pinned surfaces",
                           L"pinned.emergency"});
    }
    const float actionWidth = actions.empty()
        ? 0.0F
        : std::max(28.0F, (widthDip - kSideInsetDip * 2.0F) /
                              static_cast<float>(actions.size()));
    const auto addAction = [&](HostActionSpec spec, const std::size_t index) {
        accessibility::Node action;
        action.id = std::move(spec.id);
        action.name = std::move(spec.name);
        action.actionId = std::move(spec.actionId);
        action.domain = accessibility::ElementDomain::HostShell;
        action.role = accessibility::Role::Button;
        action.enabled = true;
        action.keyboardFocusable = true;
        action.bounds = {
            kSideInsetDip + actionWidth * static_cast<float>(index),
            38.0F,
            actionWidth,
            26.0F,
        };
        tree.nodes.push_back(std::move(action));
    };
    for (std::size_t index = 0; index < actions.size(); ++index)
        addAction(std::move(actions[index]), index);

    const auto committedCompactMedia = compactMedia &&
            snapshot.embeddedMediaSession
        ? CurrentMediaViewport(snapshot.embeddedMediaSession->id)
        : std::nullopt;
    if (compactMedia && committedCompactMedia &&
        policy_.interactionMode() == InteractionMode::Focusable) {
        const auto media = compactMediaState();
        accessibility::Node seek;
        seek.id = L"host.compact-media.seek";
        seek.name = L"Media position";
        seek.value = std::to_wstring(static_cast<int>(std::lround(media.previewPositionSeconds))) +
            L" of " + std::to_wstring(static_cast<int>(std::lround(media.durationSeconds))) +
            L" seconds";
        seek.valueChangedActionId = L"host.compact-media.seek";
        seek.domain = accessibility::ElementDomain::HostShell;
        seek.role = accessibility::Role::Slider;
        seek.enabled = media.durationSeconds > 0.0;
        seek.rangeValue = media.previewPositionSeconds;
        seek.rangeMinimum = 0.0;
        seek.rangeMaximum = media.durationSeconds;
        seek.rangeStep = media.seekStepSeconds;
        seek.keyboardFocusable = true;
        const auto& mediaBounds = committedCompactMedia->region.bounds;
        const float seekHeight = std::min(28.0F, mediaBounds.height);
        seek.bounds = {
            mediaBounds.x,
            mediaBounds.y + mediaBounds.height - seekHeight,
            mediaBounds.width,
            seekHeight,
        };
        tree.nodes.push_back(std::move(seek));
        if (controllerFocused_) tree.focusedNode = tree.nodes.size() - 1;
    } else if (policy_.interactionMode() == InteractionMode::Focusable &&
        lastRenderResult_.succeeded) {
        std::optional<accessibility::SelectPopupAccessibility> selectPopup;
        if (sliderInteraction_.selectPopup()) {
            const auto& binding = *sliderInteraction_.selectPopup();
            const auto anchor = lastRenderResult_.focusRects.find(
                binding.openerElementId);
            if (anchor != lastRenderResult_.focusRects.end()) {
                const declarative::Rect viewport{
                    kSideInsetDip, kChromeHeightDip,
                    std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
                    std::max(1.0F, heightDip - kChromeHeightDip - kBottomInsetDip)};
                const auto layout = input::ComputeSelectPopupLayout(
                    anchor->second, viewport, binding);
                selectPopup.emplace();
                selectPopup->openerElementId = binding.openerElementId;
                selectPopup->options = binding.options;
                selectPopup->highlightedOption = binding.highlightedOption;
                selectPopup->items.reserve(layout.items.size());
                for (const auto& item : layout.items)
                    selectPopup->items.push_back({item.optionIndex, item.bounds});
            }
        }
        auto widgetTree = accessibility::BuildWidgetTree(
            admission_->widgetId, admission_->runtimeGeneration,
            snapshot, lastRenderResult_,
            controllerFocused_ ? std::wstring_view{focusedElementId_}
                               : std::wstring_view{}, {},
            selectPopup ? &*selectPopup : nullptr);
        const std::size_t offset = tree.nodes.size();
        for (auto& node : widgetTree.nodes) {
            if (node.parent) *node.parent += offset;
            for (auto& child : node.children) child += offset;
            tree.nodes.push_back(std::move(node));
        }
        if (widgetTree.focusedNode) tree.focusedNode = *widgetTree.focusedNode + offset;
    }
    RECT bounds{};
    GetWindowRect(window_, &bounds);
    const double screenScale =
        static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0;
    accessibilityProvider_.Publish(
        std::move(tree),
        {static_cast<double>(bounds.left), static_cast<double>(bounds.top), screenScale,
         static_cast<double>(bounds.right - bounds.left),
         static_cast<double>(bounds.bottom - bounds.top)});
    accessibilityProvider_.SetWindowVisible(true);
    accessibilityProvider_.SetWindowFocused(
        policy_.interactionMode() == InteractionMode::Focusable && GetFocus() == window_);
    accessibilityProvider_.RaisePendingEvents();
}

void WidgetSurfaceCoordinator::ApplyWindowPolicy() {
    if (!window_) return;
    SetWindowLongPtrW(window_, GWL_EXSTYLE,
                      static_cast<LONG_PTR>(ExtendedStyle(policy_.interactionMode())));
    SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW |
                     SWP_NOACTIVATE);
    ApplyOpacity();
}

void WidgetSurfaceCoordinator::NotifyOwner(const WPARAM notification) const noexcept {
    if (notificationWindow_ && IsWindow(notificationWindow_)) {
        ++workCounters_.ownerNotifications;
        (void)PostMessageW(
            notificationWindow_, notificationMessage_, notification, 0);
    }
}

void WidgetSurfaceCoordinator::RequestPaint(const RECT* update) noexcept {
    if (!window_) return;
    ++workCounters_.invalidations;
    RECT pending{};
    if (GetUpdateRect(window_, &pending, FALSE) != FALSE)
        ++workCounters_.coalescedInvalidations;
    InvalidateRect(window_, update, FALSE);
}

void WidgetSurfaceCoordinator::ReleaseGraphicsResources() noexcept {
    if (window_) KillTimer(window_, kBackgroundSurfaceAnimationTimer);
    if (renderer_) renderer_->DiscardTargetResources();
    chromeFormat_.Reset();
    titleFormat_.Reset();
    secondaryBrush_.Reset();
    textBrush_.Reset();
    chromeBrush_.Reset();
    backgroundBrush_.Reset();
    renderTarget_.Reset();
}

void WidgetSurfaceCoordinator::OnWindowDestroyed() noexcept {
    ReleaseGraphicsResources();
    ClearFreeScroll();
    if (!tearingDown_ && admission_) {
        if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
        accessibilityProvider_.Detach();
        controllerFocused_ = false;
        inputRequests_.clear();
        (void)sliderInteraction_.RetireScrollPagination(
            admission_->widgetId, L"pinned-window-destroyed");
        paginationDiagnostics_.clear();
        focusedElementId_.clear();
        lastPinnedClientExtent_.reset();
        pendingPinnedResizeDiagnostic_.reset();
        pinnedResizeCommitDiagnostics_.clear();
        admission_.reset();
        policy_.Stop(StopReason::Unpin);
        ++teardownCount_;
        NotifyOwner();
    }
    window_ = nullptr;
}

} // namespace widgetrail::pinned
