#include "WidgetSurfaceCoordinator.h"

#include <ShellScalingApi.h>

#include <algorithm>
#include <array>
#include <utility>

namespace gba::pinned {
namespace {

constexpr wchar_t kWindowClass[] = L"GameBarAlternativePinnedSurface";
constexpr wchar_t kWindowTitle[] = L"Game Bar Alternative pinned surface";
constexpr UINT kAccessibilityActionMessage = WM_APP + 0x316;
constexpr float kChromeHeightDip = 44.0F;
constexpr float kSideInsetDip = 14.0F;

[[nodiscard]] std::filesystem::path DefaultPlacementPath() {
    std::array<wchar_t, 32768> localAppData{};
    const DWORD length = GetEnvironmentVariableW(
        L"LOCALAPPDATA", localAppData.data(), static_cast<DWORD>(localAppData.size()));
    if (length == 0 || length >= localAppData.size()) return {};
    return std::filesystem::path(
               std::wstring_view(localAppData.data(), length)) /
        L"GameBarAlternative" / L"pinned-surface-placement.ini";
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
        admission.snapshot.instanceId != admission.instanceId) {
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
    admission_ = std::move(admission);
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
    if (renderer_) renderer_->ForgetWidgetState(admission_->instanceId);
    if (window_) InvalidateRect(window_, nullptr, FALSE);
    return true;
}

bool WidgetSurfaceCoordinator::SetInteractionMode(const InteractionMode mode) {
    if (!pinned() || policy_.interactionMode() == mode) return false;
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

bool WidgetSurfaceCoordinator::BeginPlacement(const PlacementMode mode) {
    if (!pinned() || (mode != PlacementMode::Move && mode != PlacementMode::Resize))
        return false;
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

bool WidgetSurfaceCoordinator::CommitPlacement(std::wstring& error) {
    const auto monitor = CurrentWindowMonitor();
    if (!pinned() || !placementSession_ || !monitor) {
        error = L"No pinned placement gesture is active.";
        return false;
    }
    const auto committed = CommitPlacementSession(
        *placementSession_, admission_->runtimeGeneration,
        admission_->presentationGeneration, *monitor, placementLimits_);
    if (!committed) {
        (void)CancelPlacement();
        error = L"The pinned surface changed before placement could be committed.";
        return false;
    }
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

void WidgetSurfaceCoordinator::ReconcileDisplayEnvironment() noexcept {
    if (!pinned()) return;
    if (placementSession_) (void)CancelPlacement();
    const auto resolved = ResolveDurablePlacement(
        CurrentMonitorWorkAreas(), committedPlacement_, placementLimits_);
    if (!resolved) {
        (void)Unpin(WidgetSurfaceStopReason::DisplayUnavailable);
        return;
    }
    ApplyPlacementBounds(resolved->bounds);
    const auto monitor = CurrentWindowMonitor();
    if (monitor) {
        committedPlacement_ = CaptureDurablePlacement(
            *monitor, resolved->bounds, placementLimits_);
        if (resolved->usedFallback && committedPlacement_ && placementStore_) {
            std::wstring ignored;
            (void)placementStore_->Save(
                admission_->widgetId, *committedPlacement_, ignored);
        }
    }
    PublishAccessibility();
}

bool WidgetSurfaceCoordinator::Unpin(const WidgetSurfaceStopReason reason) noexcept {
    if (!pinned() && !window_) return false;
    lastStopReason_ = reason;
    placementSession_.reset();
    pointerPlacement_ = false;
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
    policy_.Stop(reason == WidgetSurfaceStopReason::HostExit ||
                         reason == WidgetSurfaceStopReason::CoordinatorDisposed
                     ? StopReason::HostExit
                     : StopReason::Unpin);
    ++teardownCount_;
    NotifyOwner();
    return true;
}

void WidgetSurfaceCoordinator::OnOverlayHidden() noexcept {
    policy_.OnMainOverlayHidden();
    if (pinned() && policy_.interactionMode() == InteractionMode::Focusable)
        (void)SetInteractionMode(InteractionMode::ClickThrough);
}

void WidgetSurfaceCoordinator::OnOverlayShown() noexcept {
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
            const int chrome = MulDiv(44, dpi, 96);
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
        if (policy_.interactionMode() == InteractionMode::Focusable) {
            RECT client{};
            GetClientRect(window_, &client);
            const int x = static_cast<short>(LOWORD(lParam));
            const int y = static_cast<short>(HIWORD(lParam));
            const int chrome = MulDiv(44, static_cast<int>(GetDpiForWindow(window_)), 96);
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
        return 0;
    case WM_KEYDOWN:
        if (placementSession_) {
            if (wParam == VK_LEFT) (void)StepPlacement(PlacementDirection::Left);
            else if (wParam == VK_RIGHT) (void)StepPlacement(PlacementDirection::Right);
            else if (wParam == VK_UP) (void)StepPlacement(PlacementDirection::Up);
            else if (wParam == VK_DOWN) (void)StepPlacement(PlacementDirection::Down);
            else if (wParam == VK_RETURN) {
                std::wstring ignored;
                (void)CommitPlacement(ignored);
            } else if (wParam == VK_ESCAPE) (void)CancelPlacement();
        } else if (wParam == 'M') (void)BeginPlacement(PlacementMode::Move);
        else if (wParam == 'R') (void)BeginPlacement(PlacementMode::Resize);
        else if (wParam == 'P') (void)ToggleInteractionMode();
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
        if (request.kind != accessibility::ActionKind::Invoke ||
            request.domain != accessibility::ElementDomain::HostShell ||
            request.widgetId != admission_->widgetId ||
            request.runtimeGeneration != admission_->runtimeGeneration ||
            request.snapshotSequence != admission_->snapshot.sequence) continue;
        if (request.actionId == L"pinned.move")
            (void)BeginPlacement(PlacementMode::Move);
        else if (request.actionId == L"pinned.resize")
            (void)BeginPlacement(PlacementMode::Resize);
        else if (request.actionId == L"pinned.commit") {
            std::wstring ignored;
            (void)CommitPlacement(ignored);
        } else if (request.actionId == L"pinned.cancel")
            (void)CancelPlacement();
    }
}

bool WidgetSurfaceCoordinator::CreateWindowForAdmission(std::wstring& error) {
    const auto monitors = CurrentMonitorWorkAreas();
    const auto persisted = placementStore_
        ? placementStore_->Load(admission_->widgetId)
        : std::optional<DurablePinnedPlacement>{};
    const auto placement = ResolveDurablePlacement(
        monitors, persisted, placementLimits_);
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
    SetLayeredWindowAttributes(window_, 0, 255, LWA_ALPHA);
    ShowWindow(window_, SW_SHOWNORMAL);
    SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    const auto monitor = std::ranges::find_if(monitors, [&](const auto& candidate) {
        return candidate.stableId == placement->monitorId;
    });
    if (monitor != monitors.end())
        committedPlacement_ = CaptureDurablePlacement(
            *monitor, placement->bounds, placementLimits_);
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
    if (FAILED(renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x16212E), backgroundBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x24384D), chromeBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(D2D1::ColorF::White), textBrush_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0xAFC4D8), secondaryBrush_.ReleaseAndGetAddressOf()))) return false;
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
        titleFormat_.Get(), D2D1::RectF(kSideInsetDip, 10.0F, widthDip - 410.0F, 36.0F),
        textBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    std::wstring chrome;
    if (placementSession_) {
        chrome = placementSession_->mode == PlacementMode::Move
            ? L"Moving — arrows/D-pad · Enter/A commit · Esc/B cancel"
            : L"Resizing — arrows/D-pad · Enter/A commit · Esc/B cancel";
    } else {
        chrome = policy_.interactionMode() == InteractionMode::Focusable
            ? L"P Mode · M Move · R Resize · U Unpin · Close"
            : L"Click-through — reopen the overlay and press P to interact";
    }
    renderTarget_->DrawTextW(
        chrome.c_str(), static_cast<UINT32>(chrome.size()), chromeFormat_.Get(),
        D2D1::RectF(std::max(kSideInsetDip, widthDip - 400.0F), 13.0F,
                    widthDip - kSideInsetDip, 36.0F),
        secondaryBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    DeclarativeRenderOptions options;
    options.pixelScale = dpiScale;
    options.collectAccessibility = false;
    options.responsiveViewport = {widthDip, heightDip};
    options.surfaceBackground = NativeColor{22.0F / 255.0F, 33.0F / 255.0F, 46.0F / 255.0F, 1.0F};
    options.accessibility.reducedMotion = true;
    const declarative::Rect viewport{
        kSideInsetDip, kChromeHeightDip + 10.0F,
        std::max(1.0F, widthDip - kSideInsetDip * 2.0F),
        std::max(1.0F, heightDip - kChromeHeightDip - 24.0F),
    };
    (void)renderer_->Render(
        renderTarget_.Get(), admission_->snapshot,
        policy_.interactionMode() == InteractionMode::Focusable
            ? std::wstring_view{admission_->snapshot.initialFocusId}
            : std::wstring_view{},
        viewport, options);
    const HRESULT result = renderTarget_->EndDraw();
    if (result == D2DERR_RECREATE_TARGET) ReleaseGraphicsResources();
    EndPaint(window_, &paint);
}

void WidgetSurfaceCoordinator::PublishAccessibility() {
    if (!pinned()) return;
    accessibility::Tree tree;
    tree.widgetId = admission_->widgetId;
    tree.runtimeGeneration = admission_->runtimeGeneration;
    tree.snapshotSequence = admission_->snapshot.sequence;
    tree.activeInputScopeId = L"pinned.host";
    tree.name = admission_->name + L" pinned surface";
    accessibility::Node heading;
    heading.id = L"pinned.heading";
    heading.name = tree.name;
    heading.domain = accessibility::ElementDomain::HostShell;
    heading.role = accessibility::Role::Heading;
    heading.keyboardFocusable = false;
    heading.bounds = {kSideInsetDip, 8.0F, 125.0F, 28.0F};
    tree.nodes.push_back(std::move(heading));
    accessibility::Node state;
    state.id = L"pinned.mode";
    state.name = L"Pinned surface mode";
    if (placementSession_) {
        state.value = placementSession_->mode == PlacementMode::Move
            ? L"Move mode. Direction changes position. Commit or cancel."
            : L"Resize mode. Direction changes size. Commit or cancel.";
    } else {
        state.value = policy_.interactionMode() == InteractionMode::Focusable
            ? L"Interactive. Move and Resize are available. P switches to click-through. U unpins."
            : L"Click-through. Reopen the overlay and press P to interact.";
    }
    state.domain = accessibility::ElementDomain::HostShell;
    state.role = accessibility::Role::Status;
    state.keyboardFocusable = false;
    state.bounds = {145.0F, 8.0F, 145.0F, 28.0F};
    tree.nodes.push_back(std::move(state));
    const auto addAction = [&](std::wstring id, std::wstring name,
                               std::wstring actionId, const float x) {
        accessibility::Node action;
        action.id = std::move(id);
        action.name = std::move(name);
        action.actionId = std::move(actionId);
        action.domain = accessibility::ElementDomain::HostShell;
        action.role = accessibility::Role::Button;
        action.enabled = policy_.interactionMode() == InteractionMode::Focusable;
        action.keyboardFocusable = action.enabled;
        action.bounds = {x, 8.0F, 70.0F, 28.0F};
        tree.nodes.push_back(std::move(action));
    };
    if (policy_.interactionMode() == InteractionMode::Focusable) {
        if (placementSession_) {
            addAction(L"pinned.commit", L"Commit placement", L"pinned.commit", 300.0F);
            addAction(L"pinned.cancel", L"Cancel placement", L"pinned.cancel", 375.0F);
        } else {
            addAction(L"pinned.move", L"Move pinned surface", L"pinned.move", 300.0F);
            addAction(L"pinned.resize", L"Resize pinned surface", L"pinned.resize", 375.0F);
        }
    }
    RECT bounds{};
    GetWindowRect(window_, &bounds);
    const double scale = static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0;
    accessibilityProvider_.Publish(
        std::move(tree),
        {static_cast<double>(bounds.left), static_cast<double>(bounds.top), scale,
         static_cast<double>(bounds.right - bounds.left),
         static_cast<double>(bounds.bottom - bounds.top)});
    accessibilityProvider_.SetWindowVisible(true);
    accessibilityProvider_.SetWindowFocused(
        policy_.interactionMode() == InteractionMode::Focusable && GetFocus() == window_);
}

void WidgetSurfaceCoordinator::ApplyWindowPolicy() {
    if (!window_) return;
    SetWindowLongPtrW(window_, GWL_EXSTYLE,
                      static_cast<LONG_PTR>(ExtendedStyle(policy_.interactionMode())));
    SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW |
                     SWP_NOACTIVATE);
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
        admission_.reset();
        policy_.Stop(StopReason::Unpin);
        ++teardownCount_;
        NotifyOwner();
    }
    window_ = nullptr;
}

} // namespace gba::pinned
