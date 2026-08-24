#include "WidgetSurfaceCoordinator.h"

#include "AccessibilityTree.h"
#include "FocusNavigation.h"
#include "WidgetSurfaceFocus.h"

#include <ShellScalingApi.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <utility>

namespace widgetrail::pinned {
namespace {

constexpr wchar_t kWindowClass[] = L"WidgetRail.PinnedSurface";
constexpr wchar_t kWindowTitle[] = L"WidgetRail pinned surface";
constexpr UINT kAccessibilityActionMessage = WM_APP + 0x316;
constexpr float kChromeHeightDip = 36.0F;
constexpr float kSideInsetDip = 8.0F;
constexpr float kBottomInsetDip = 8.0F;
constexpr float kPinnedBorderDip = 1.0F;
constexpr float kAdjustBorderDip = 3.0F;
constexpr unsigned int kMinimumOpacityPercent = 30;
constexpr unsigned int kMaximumOpacityPercent = 100;
constexpr std::size_t kMaximumPendingInputRequests = 16;
constexpr std::size_t kMaximumFeedbackCharacters = 160;

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
    placementLimits_ = admission.placementLimits;
    placementLimits_.maximumWidthDip = std::max(
        placementLimits_.maximumWidthDip,
        admission.initialContentWidthDip + kSideInsetDip * 2.0F);
    placementLimits_.maximumHeightDip = std::max(
        placementLimits_.maximumHeightDip,
        admission.initialContentHeightDip + kChromeHeightDip + kBottomInsetDip);
    admission_ = std::move(admission);
    focusedElementId_ = admission_->snapshot.initialFocusId;
    inputRequests_.clear();
    actionFeedback_.clear();
    actionFeedbackFailure_ = false;
    controllerFocused_ = false;
    opacityPercent_ = 100;
    opacityPreviewOriginal_.reset();
    if (!CreateWindowForAdmission(error)) {
        admission_.reset();
        policy_.Stop(StopReason::Unpin);
        return false;
    }
    PublishAccessibility();
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::UpdateSnapshot(
    const std::wstring_view widgetIdValue,
    const std::wstring_view runtimeGenerationValue,
    const WidgetSnapshot& snapshot) {
    if (!pinned() || widgetIdValue != admission_->widgetId ||
        runtimeGenerationValue != admission_->runtimeGeneration ||
        snapshot.instanceId != admission_->instanceId) {
        return false;
    }
    admission_->snapshot = snapshot;
    inputRequests_.clear();
    actionFeedback_.clear();
    actionFeedbackFailure_ = false;
    if (focusedElementId_.empty()) focusedElementId_ = snapshot.initialFocusId;
    if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
    if (window_) InvalidateRect(window_, nullptr, FALSE);
    return true;
}

bool WidgetSurfaceCoordinator::SetInteractionMode(const InteractionMode mode) {
    if (!pinned() || policy_.interactionMode() == mode) return false;
    if (mode == InteractionMode::ClickThrough) (void)ExitControllerFocus();
    policy_.SetInteractionMode(mode);
    ApplyWindowPolicy();
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
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
    if (const auto visible = input::ResolveVisibleFocusTarget(
            focusedElementId_, admission_->snapshot.activeInputScopeId,
            lastRenderResult_)) {
        focusedElementId_ = *visible;
    } else if (focusedElementId_.empty()) {
        focusedElementId_ = admission_->snapshot.initialFocusId;
    }
    controllerFocused_ = true;
    if (window_) {
        (void)SetActiveWindow(window_);
        (void)SetFocus(window_);
    }
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::ExitControllerFocus() noexcept {
    if (!controllerFocused_) return false;
    if (placementSession_) (void)CancelPlacement();
    if (opacityPreviewOriginal_) (void)CancelOpacity();
    controllerFocused_ = false;
    if (overlayVisible_ && notificationWindow_ && IsWindow(notificationWindow_))
        (void)SetFocus(notificationWindow_);
    PublishAccessibility();
    if (window_) InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::MoveControllerFocus(
    const input::NavigationDirection direction) {
    if (!controllerFocused_ || direction == input::NavigationDirection::None ||
        !pinned()) return false;
    const auto activeScope = std::wstring_view(admission_->snapshot.activeInputScopeId);
    const auto visible = input::ResolveVisibleFocusTarget(
        focusedElementId_, activeScope, lastRenderResult_);
    if (!visible) return false;
    if (*visible != focusedElementId_) {
        focusedElementId_ = *visible;
        PublishAccessibility();
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }
    const auto* focused = input::FindNodeInInputScope(
        admission_->snapshot, focusedElementId_, activeScope);
    if (!focused) return false;
    const std::wstring* authored{};
    switch (direction) {
    case input::NavigationDirection::Left: authored = &focused->focusLeft; break;
    case input::NavigationDirection::Right: authored = &focused->focusRight; break;
    case input::NavigationDirection::Up: authored = &focused->focusUp; break;
    case input::NavigationDirection::Down: authored = &focused->focusDown; break;
    case input::NavigationDirection::None: break;
    }
    const auto* explicitTarget = authored && !authored->empty()
        ? input::FindNodeInInputScope(admission_->snapshot, *authored, activeScope)
        : nullptr;
    if (explicitTarget && input::IsDistinctFocusMove(
            focusedElementId_, explicitTarget->id,
            input::IsEnabledFocusTarget(explicitTarget->id, lastRenderResult_))) {
        focusedElementId_ = explicitTarget->id;
    } else if (const auto geometric = input::FindGeometricFocusTarget(
                   focusedElementId_, direction, lastRenderResult_)) {
        focusedElementId_ = *geometric;
    } else {
        return false;
    }
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    return true;
}

void WidgetSurfaceCoordinator::QueueResolvedInput(
    std::wstring nodeId,
    std::wstring protocolButton,
    const ControllerInputOrigin origin,
    const std::optional<double> requestedValue) {
    if (!pinned() || policy_.interactionMode() != InteractionMode::Focusable ||
        nodeId.empty() || protocolButton.empty()) return;
    if (inputRequests_.size() >= kMaximumPendingInputRequests) {
        SetActionFeedback(L"Pinned input queue is busy. Try again.", true);
        return;
    }
    inputRequests_.push_back({
        admission_->widgetId,
        admission_->runtimeGeneration,
        admission_->snapshot.sequence,
        admission_->snapshot.activeInputScopeId,
        std::move(nodeId),
        std::move(protocolButton),
        requestedValue,
        origin,
    });
    NotifyOwner();
}

bool WidgetSurfaceCoordinator::QueueFocusedInput(
    const std::wstring_view protocolButton,
    const ControllerInputOrigin origin,
    const std::optional<double> requestedValue) {
    if (!controllerFocused_ || focusedElementId_.empty()) return false;
    const auto* node = input::FindNodeInInputScope(
        admission_->snapshot, focusedElementId_,
        admission_->snapshot.activeInputScopeId);
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

void WidgetSurfaceCoordinator::SetActionFeedback(
    std::wstring message, const bool failure) {
    if (message.size() > kMaximumFeedbackCharacters)
        message.resize(kMaximumFeedbackCharacters);
    actionFeedback_ = std::move(message);
    actionFeedbackFailure_ = failure;
    PublishAccessibility();
    if (window_) InvalidateRect(window_, nullptr, FALSE);
}

bool WidgetSurfaceCoordinator::EmergencyHideAll() noexcept {
    return Unpin(WidgetSurfaceStopReason::EmergencyHide);
}

bool WidgetSurfaceCoordinator::BeginPlacement(const PlacementMode mode) {
    if (!pinned() || (mode != PlacementMode::Move && mode != PlacementMode::Resize &&
                      mode != PlacementMode::Adjust))
        return false;
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
        ApplyWindowPolicy();
    }
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    return true;
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
    if (!placementStore_ ||
        !placementStore_->Save(admission_->widgetId, *committed, error)) {
        (void)CancelPlacement();
        return false;
    }
    committedPlacement_ = *committed;
    placementSession_.reset();
    pointerPlacement_ = false;
    if (GetCapture() == window_) ReleaseCapture();
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    return true;
}

bool WidgetSurfaceCoordinator::CancelPlacement() noexcept {
    if (!placementSession_) return false;
    const auto original = placementSession_->original;
    placementSession_.reset();
    pointerPlacement_ = false;
    if (GetCapture() == window_) ReleaseCapture();
    ApplyPlacementBounds(original);
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::ApplyOpacity() noexcept {
    if (!window_) return;
    const BYTE alpha = static_cast<BYTE>(std::lround(
        static_cast<double>(opacityPercent_) * 255.0 / 100.0));
    (void)SetLayeredWindowAttributes(window_, 0, alpha, LWA_ALPHA);
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
    if (!placementStore_->Save(admission_->widgetId, *current, error)) return false;
    committedPlacement_ = std::move(current);
    return true;
}

bool WidgetSurfaceCoordinator::BeginOpacityAdjustment() {
    if (!pinned() || opacityPreviewOriginal_) return false;
    if (placementSession_) (void)CancelPlacement();
    opacityPreviewOriginal_ = opacityPercent_;
    if (policy_.interactionMode() != InteractionMode::Focusable) {
        policy_.SetInteractionMode(InteractionMode::Focusable);
        ApplyWindowPolicy();
    }
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
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
    InvalidateRect(window_, nullptr, FALSE);
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
        ApplyOpacity();
        opacityPreviewOriginal_.reset();
        PublishAccessibility();
        InvalidateRect(window_, nullptr, FALSE);
        NotifyOwner();
        return false;
    }
    opacityPreviewOriginal_.reset();
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
    NotifyOwner();
    error.clear();
    return true;
}

bool WidgetSurfaceCoordinator::CancelOpacity() noexcept {
    if (!opacityPreviewOriginal_) return false;
    opacityPercent_ = *opacityPreviewOriginal_;
    opacityPreviewOriginal_.reset();
    ApplyOpacity();
    PublishAccessibility();
    InvalidateRect(window_, nullptr, FALSE);
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
    if (!pinned() && !window_) return false;
    lastStopReason_ = reason;
    placementSession_.reset();
    opacityPreviewOriginal_.reset();
    opacityPercent_ = 100;
    controllerFocused_ = false;
    overlayVisible_ = false;
    pointerPlacement_ = false;
    pointerActionNode_.clear();
    inputRequests_.clear();
    actionFeedback_.clear();
    if (GetCapture() == window_) ReleaseCapture();
    const HWND retiring = window_;
    tearingDown_ = true;
    accessibilityProvider_.Clear();
    accessibilityProvider_.Detach();
    if (retiring && IsWindow(retiring)) DestroyWindow(retiring);
    window_ = nullptr;
    tearingDown_ = false;
    ReleaseGraphicsResources();
    admission_.reset();
    committedPlacement_.reset();
    focusedElementId_.clear();
    lastRenderResult_ = {};
    policy_.Stop(reason == WidgetSurfaceStopReason::HostExit ||
                         reason == WidgetSurfaceStopReason::CoordinatorDisposed
                     ? StopReason::HostExit
                     : StopReason::Unpin);
    ++teardownCount_;
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::OnOverlayHidden() noexcept {
    overlayVisible_ = false;
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
    return admission_.has_value() && policy_.state() == LifecycleState::Pinned &&
        window_ && IsWindow(window_);
}

std::wstring_view WidgetSurfaceCoordinator::widgetId() const noexcept {
    return admission_ ? std::wstring_view{admission_->widgetId} : std::wstring_view{};
}

std::wstring_view WidgetSurfaceCoordinator::runtimeGeneration() const noexcept {
    return admission_ ? std::wstring_view{admission_->runtimeGeneration} : std::wstring_view{};
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
    case WM_SETFOCUS:
        accessibilityProvider_.SetWindowFocused(true);
        PublishAccessibility();
        return 0;
    case WM_KILLFOCUS:
        accessibilityProvider_.SetWindowFocused(false);
        if (pointerPlacement_) (void)CancelPlacement();
        if (opacityPreviewOriginal_) (void)CancelOpacity();
        pointerActionNode_.clear();
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
            if (mode != PlacementMode::None && BeginPlacement(mode)) {
                pointerPlacement_ = true;
                GetCursorPos(&pointerStart_);
                pointerStartBounds_ = placementSession_->current;
                SetCapture(window_);
                return 0;
            }
            const float scale =
                static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
            if (const auto hit = input::FindPointerHitTarget(
                    static_cast<float>(x) / scale,
                    static_cast<float>(y) / scale,
                    admission_->snapshot.activeInputScopeId,
                    lastRenderResult_)) {
                focusedElementId_ = hit->id;
                pointerActionNode_ = hit->enabled ? hit->id : std::wstring{};
                SetCapture(window_);
                PublishAccessibility();
                InvalidateRect(window_, nullptr, FALSE);
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
            if (placementSession_->mode == PlacementMode::Move) {
                proposed.left += dx;
                proposed.right += dx;
                proposed.top += dy;
                proposed.bottom += dy;
            } else {
                proposed.right += dx;
                proposed.bottom += dy;
            }
            const auto monitor = CurrentWindowMonitor();
            if (monitor && SetPlacementSessionBounds(
                    *placementSession_, proposed, *monitor, placementLimits_)) {
                ApplyPlacementBounds(placementSession_->current);
            }
        }
        return 0;
    case WM_LBUTTONUP:
        if (pointerPlacement_) {
            pointerPlacement_ = false;
            if (GetCapture() == window_) ReleaseCapture();
            std::wstring ignored;
            if (!CommitPlacement(ignored)) (void)CancelPlacement();
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
                admission_->snapshot.activeInputScopeId,
                lastRenderResult_);
            if (hit && hit->enabled && hit->id == pressed)
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
        if (pointerPlacement_) (void)CancelPlacement();
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
            (void)QueueFocusedInput(
                L"a", ControllerInputOrigin::AccessibilityAutomation);
        } else if (wParam == VK_ESCAPE && controllerFocused_) {
            (void)ExitControllerFocus();
            (void)SetInteractionMode(InteractionMode::ClickThrough);
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
        ReleaseGraphicsResources();
        ReconcileDisplayEnvironment();
        return 0;
    case WM_DISPLAYCHANGE:
    case WM_SETTINGCHANGE:
        ReconcileDisplayEnvironment();
        return 0;
    case WM_SIZE:
        if (renderTarget_ && wParam != SIZE_MINIMIZED) {
            renderTarget_->Resize(D2D1::SizeU(LOWORD(lParam), HIWORD(lParam)));
            if (renderer_) renderer_->DiscardTargetResources();
        }
        return 0;
    case WM_ERASEBKGND:
        return 1;
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
    for (const auto& request : accessibilityProvider_.TakeActions()) {
        if (request.widgetId != admission_->widgetId ||
            request.runtimeGeneration != admission_->runtimeGeneration ||
            request.snapshotSequence != admission_->snapshot.sequence ||
            request.activeInputScopeId != admission_->snapshot.activeInputScopeId)
            continue;
        if (request.domain == accessibility::ElementDomain::Widget) {
            const auto resolved = accessibility::ResolveActionRequest(
                request, admission_->widgetId, admission_->runtimeGeneration,
                admission_->snapshot);
            if (!resolved || policy_.interactionMode() != InteractionMode::Focusable)
                continue;
            if (resolved->kind == accessibility::ActionKind::Focus) {
                focusedElementId_ = resolved->nodeId;
                PublishAccessibility();
                InvalidateRect(window_, nullptr, FALSE);
            } else {
                QueueResolvedInput(
                    resolved->nodeId, resolved->protocolButton,
                    ControllerInputOrigin::AccessibilityAutomation,
                    resolved->requestedValue);
            }
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
            else (void)CommitPlacement(ignored);
        } else if (request.actionId == L"pinned.cancel") {
            if (opacityPreviewOriginal_) (void)CancelOpacity();
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
    const auto placement = ResolveDurablePlacement(
        monitors, persisted, placementLimits_,
        std::pair{
            admission_->initialContentWidthDip + kSideInsetDip * 2.0F,
            admission_->initialContentHeightDip + kChromeHeightDip + kBottomInsetDip});
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
    InvalidateRect(window_, nullptr, FALSE);
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
    renderTarget_->BeginDraw();
    renderTarget_->Clear(D2D1::ColorF(0x16212E));
    renderTarget_->FillRectangle(D2D1::RectF(0, 0, widthDip, kChromeHeightDip), chromeBrush_.Get());
    renderTarget_->DrawTextW(
        admission_->name.c_str(), static_cast<UINT32>(admission_->name.size()),
        titleFormat_.Get(), D2D1::RectF(
            kSideInsetDip, 7.0F, std::max(kSideInsetDip + 1.0F, widthDip * 0.40F), 31.0F),
        textBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
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
            chrome = L"Adjust — left stick/D-pad move · right stick resize · A commit · B cancel · " +
                std::to_wstring(bounds.right - bounds.left) + L"x" +
                std::to_wstring(bounds.bottom - bounds.top);
        }
    } else {
        chrome = policy_.interactionMode() == InteractionMode::Focusable
            ? L"Interactive · D-pad navigate · A activate · B return"
            : L"Click-through · View enters · Menu options";
    }
    renderTarget_->DrawTextW(
        chrome.c_str(), static_cast<UINT32>(chrome.size()), chromeFormat_.Get(),
        D2D1::RectF(std::max(kSideInsetDip, widthDip * 0.42F), 9.0F,
                    widthDip - kSideInsetDip, 31.0F),
        secondaryBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    DeclarativeRenderOptions options;
    options.pixelScale = dpiScale;
    options.collectAccessibility = ResolveSurfacePresentationPolicy(
        policy_.interactionMode()).exposeInteractiveSemantics;
    options.responsiveViewport = {widthDip, heightDip};
    options.surfaceBackground = NativeColor{22.0F / 255.0F, 33.0F / 255.0F, 46.0F / 255.0F, 1.0F};
    options.accessibility.reducedMotion = true;
    const declarative::Rect viewport{
        kSideInsetDip, kChromeHeightDip,
        std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
        std::max(1.0F, heightDip - kChromeHeightDip - kBottomInsetDip),
    };
    lastRenderResult_ = renderer_->Render(
        renderTarget_.Get(), admission_->snapshot,
        controllerFocused_
            ? std::wstring_view{focusedElementId_}
            : std::wstring_view{},
        viewport, options);
    renderTarget_->DrawRectangle(
        D2D1::RectF(0.5F, 0.5F, std::max(0.5F, widthDip - 0.5F),
                    std::max(0.5F, heightDip - 0.5F)),
        chromeBrush_.Get(),
        placementSession_ && placementSession_->mode == PlacementMode::Adjust
            ? kAdjustBorderDip : kPinnedBorderDip);
    const HRESULT result = renderTarget_->EndDraw();
    if (result == D2DERR_RECREATE_TARGET) ReleaseGraphicsResources();
    else PublishAccessibility();
    EndPaint(window_, &paint);
}

void WidgetSurfaceCoordinator::PublishAccessibility() {
    if (!pinned()) return;
    RECT client{};
    GetClientRect(window_, &client);
    const float scale =
        static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F;
    const float widthDip = static_cast<float>(client.right - client.left) / scale;
    accessibility::Tree tree{
        admission_->widgetId,
        admission_->runtimeGeneration,
        admission_->snapshot.sequence,
        admission_->snapshot.activeInputScopeId,
    };
    tree.name = admission_->name + L" pinned surface";
    accessibility::Node heading;
    heading.id = L"pinned.heading";
    heading.name = tree.name;
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
            state.value = L"Adjust mode. Left stick or D-pad moves. Right stick resizes. Commit or cancel.";
    } else {
        state.value = policy_.interactionMode() == InteractionMode::Focusable
            ? L"Interactive. D-pad navigates. A activates. B returns to the tray. Menu opens options."
            : L"Click-through. From the tray, View enters this pin and Menu opens options.";
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

    if (policy_.interactionMode() == InteractionMode::Focusable &&
        lastRenderResult_.succeeded) {
        auto widgetTree = accessibility::BuildWidgetTree(
            admission_->widgetId, admission_->runtimeGeneration,
            admission_->snapshot, lastRenderResult_,
            controllerFocused_ ? std::wstring_view{focusedElementId_}
                               : std::wstring_view{});
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

void WidgetSurfaceCoordinator::NotifyOwner() const noexcept {
    if (notificationWindow_ && IsWindow(notificationWindow_))
        (void)PostMessageW(notificationWindow_, notificationMessage_, 0, 0);
}

void WidgetSurfaceCoordinator::ReleaseGraphicsResources() noexcept {
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
    if (!tearingDown_ && admission_) {
        accessibilityProvider_.Detach();
        controllerFocused_ = false;
        inputRequests_.clear();
        focusedElementId_.clear();
        admission_.reset();
        policy_.Stop(StopReason::Unpin);
        ++teardownCount_;
        NotifyOwner();
    }
    window_ = nullptr;
}

} // namespace widgetrail::pinned
