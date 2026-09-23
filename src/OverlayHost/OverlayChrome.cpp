#include "OverlayChrome.h"

#include "OverlayCompositionSurface.h"

#include <algorithm>
#include <cmath>

namespace widgetrail::shell {

namespace {
LONG HorizontalOrigin(const RECT& workArea, const LONG width,
    const OverlayPosition position, const LONG sideMargin) noexcept {
    const LONG spare = std::max(0L, workArea.right - workArea.left - width);
    const LONG inset = std::clamp(sideMargin, 0L, spare / 2);
    switch (position) {
    case OverlayPosition::BottomLeft: return workArea.left + inset;
    case OverlayPosition::BottomRight: return workArea.right - width - inset;
    default: return workArea.left + spare / 2;
    }
}
} // namespace

RECT ComputeFixedChromeWindowBounds(
    const RECT& workArea, const LONG width, const LONG height,
    const OverlayPosition position, const LONG sideMargin) noexcept {
    if (width <= 0 || height <= 0 || workArea.right <= workArea.left ||
        workArea.bottom <= workArea.top) return {};
    const LONG left = HorizontalOrigin(workArea, width, position, sideMargin);
    return {left, workArea.bottom - height, left + width, workArea.bottom};
}

RadialChromePlacement ComputeRadialChromePlacement(
    const RECT& workArea, const RECT& windowBounds,
    const RECT& guideClientBounds, const RECT& trayClientBounds,
    const float pixelsPerDip) noexcept {
    RadialChromePlacement result{windowBounds, guideClientBounds, trayClientBounds};
    if (!std::isfinite(pixelsPerDip) || pixelsPerDip <= 0.0F) return result;
    const LONG gap = static_cast<LONG>(std::ceil(12.0F * pixelsPerDip));
    const LONG available = windowBounds.top + guideClientBounds.top - workArea.top - gap;
    result.wheelSize = std::max(0L, std::min({
        static_cast<LONG>(std::floor(400.0F * pixelsPerDip)),
        trayClientBounds.right - trayClientBounds.left - 2 * gap, available}));
    if (result.wheelSize == 0) return result;
    const LONG wheelTop = guideClientBounds.top - gap - result.wheelSize;
    result.expansion = std::max(0L, -wheelTop);
    result.windowBounds.top -= result.expansion;
    result.guideClientBounds.top += result.expansion;
    result.guideClientBounds.bottom += result.expansion;
    result.trayClientBounds.top = wheelTop + result.expansion;
    result.trayClientBounds.bottom += result.expansion;
    result.railOffset = trayClientBounds.top - wheelTop;
    return result;
}

std::optional<RECT> ComputeContentWindowBoundsAboveGuide(
    const RECT& workArea,
    const LONG guideTop,
    const LONG width,
    const LONG height,
    const LONG panelToGuideGap,
    const OverlayPosition position, const LONG sideMargin) noexcept {
    if (width <= 0 || height <= 0 || panelToGuideGap < 0 ||
        workArea.right <= workArea.left ||
        workArea.bottom <= workArea.top) return std::nullopt;
    const LONG maximumX = workArea.right - width;
    const LONG maximumY = workArea.bottom - height;
    if (maximumX < workArea.left || maximumY < workArea.top)
        return std::nullopt;
    const LONG left = HorizontalOrigin(workArea, width, position, sideMargin);
    const LONG top = std::clamp(
        guideTop - panelToGuideGap - height,
        workArea.top, maximumY);
    return RECT{left, top, left + width, top + height};
}

bool IsFixedChromeHit(
    const POINT screenPoint, const RECT& guideBounds,
    const RECT& trayBounds) noexcept {
    return PtInRect(&guideBounds, screenPoint) || PtInRect(&trayBounds, screenPoint);
}

bool ApplyFixedChromeWindow(
    const HWND owner, const HWND chrome, const RECT& bounds,
    const bool show) noexcept {
    if (!owner || !chrome || !IsWindow(owner) || !IsWindow(chrome) ||
        bounds.right <= bounds.left || bounds.bottom <= bounds.top) return false;
    SetLastError(ERROR_SUCCESS);
    const auto priorOwner = reinterpret_cast<HWND>(SetWindowLongPtrW(
        chrome, GWLP_HWNDPARENT, reinterpret_cast<LONG_PTR>(owner)));
    if (!priorOwner && GetLastError() != ERROR_SUCCESS) return false;
    return SetWindowPos(
        chrome, HWND_TOPMOST, bounds.left, bounds.top,
        bounds.right - bounds.left, bounds.bottom - bounds.top,
        SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)) != FALSE;
}

namespace {

bool InitializeProductionChromeTarget(
    OverlayCompositionSurface& surface,
    const HWND chrome,
    std::wstring& error) {
    return surface.InitializeChromeTarget(chrome, error);
}

} // namespace

bool SetContentCompositionMode(const HWND content, const bool composition) noexcept {
    if (!content || !IsWindow(content)) return false;
    const auto current = static_cast<DWORD>(GetWindowLongPtrW(content, GWL_EXSTYLE));
    if (!composition && (current & WS_EX_NOREDIRECTIONBITMAP)) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return false;
    }
    const auto desired = composition
        ? current & ~WS_EX_LAYERED : current | WS_EX_LAYERED;
    if (current != desired) {
        SetLastError(ERROR_SUCCESS);
        if (!SetWindowLongPtrW(content, GWL_EXSTYLE, static_cast<LONG_PTR>(desired)) &&
            GetLastError() != ERROR_SUCCESS) return false;
        if (!SetWindowPos(content, nullptr, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE |
                    SWP_FRAMECHANGED | SWP_NOREDRAW)) return false;
    }
    constexpr DWORD renderingStyles = WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP;
    return (static_cast<DWORD>(GetWindowLongPtrW(content, GWL_EXSTYLE)) & renderingStyles) ==
        (desired & renderingStyles);
}

bool InitializeFixedChromeComposition(
    OverlayCompositionSurface& surface,
    const HWND content,
    const HWND chrome,
    ID2D1Factory1* factory,
    std::wstring& error,
    FixedChromeTargetInitializer initializeChrome,
    const bool softwareDevice) {
    if (!surface.Initialize(content, factory, error, softwareDevice)) {
        ResetFixedChromeComposition(surface, chrome);
        return false;
    }
    if (!initializeChrome) initializeChrome = InitializeProductionChromeTarget;
    if (initializeChrome(surface, chrome, error)) return true;
    ResetFixedChromeComposition(surface, chrome);
    return false;
}

void ResetFixedChromeComposition(
    OverlayCompositionSurface& surface, const HWND chrome) noexcept {
    surface.Reset();
    if (chrome && IsWindow(chrome)) ShowWindow(chrome, SW_HIDE);
}

bool RouteFixedChromePointerRelease(
    const HWND chrome,
    const HWND content,
    const LPARAM position,
    void* const context,
    const FixedChromePointerActivation activate) noexcept {
    if (!chrome || !content || !activate ||
        !IsWindow(chrome) || !IsWindow(content)) return false;
    POINT point{
        static_cast<LONG>(static_cast<short>(LOWORD(position))),
        static_cast<LONG>(static_cast<short>(HIWORD(position))),
    };
    if (!ClientToScreen(chrome, &point) || !ScreenToClient(content, &point))
        return false;
    activate(context, static_cast<float>(point.x), static_cast<float>(point.y));
    return true;
}

bool SameFixedChromeSession(
    const FixedChromeSessionKey& left,
    const FixedChromeSessionKey& right) noexcept {
    return EqualRect(&left.workArea, &right.workArea) &&
        left.dpi == right.dpi &&
        left.interfaceScale == right.interfaceScale &&
        left.appearanceRevision == right.appearanceRevision &&
        left.catalogOrder == right.catalogOrder;
}

bool RequiresTrayRepaint(
    const RetainedTrayState* retained,
    const RetainedTrayState& next) noexcept {
    return !retained || *retained != next;
}

void FillColorKeyRoundedRectangle(
    ID2D1RenderTarget* target,
    const D2D1_ROUNDED_RECT& rectangle,
    ID2D1Brush* brush,
    const OuterChromeBoundary boundary) noexcept {
    if (!target || !brush) return;
    if (boundary == OuterChromeBoundary::PremultipliedAlpha) {
        target->FillRoundedRectangle(rectangle, brush);
        return;
    }
    const auto previous = target->GetAntialiasMode();
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
    target->FillRoundedRectangle(rectangle, brush);
    target->SetAntialiasMode(previous);
}

} // namespace widgetrail::shell
