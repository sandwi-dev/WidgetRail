#include "OverlayChrome.h"

#include "OverlayCompositionSurface.h"

#include <algorithm>

namespace gba::shell {

RECT ComputeFixedChromeWindowBounds(
    const RECT& workArea, const LONG width, const LONG height) noexcept {
    if (width <= 0 || height <= 0 || workArea.right <= workArea.left ||
        workArea.bottom <= workArea.top) return {};
    const LONG availableWidth = workArea.right - workArea.left;
    const LONG left = workArea.left + (availableWidth - width) / 2;
    return {left, workArea.bottom - height, left + width, workArea.bottom};
}

std::optional<RECT> ComputeContentWindowBoundsAboveGuide(
    const RECT& workArea,
    const LONG guideTop,
    const LONG width,
    const LONG height,
    const LONG panelToGuideGap) noexcept {
    if (width <= 0 || height <= 0 || panelToGuideGap < 0 ||
        workArea.right <= workArea.left ||
        workArea.bottom <= workArea.top) return std::nullopt;
    const LONG maximumX = workArea.right - width;
    const LONG maximumY = workArea.bottom - height;
    if (maximumX < workArea.left || maximumY < workArea.top)
        return std::nullopt;
    const LONG left = workArea.left +
        ((workArea.right - workArea.left) - width) / 2;
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

bool InitializeFixedChromeComposition(
    OverlayCompositionSurface& surface,
    const HWND content,
    const HWND chrome,
    ID2D1Factory1* factory,
    std::wstring& error,
    FixedChromeTargetInitializer initializeChrome) {
    if (!surface.Initialize(content, factory, error)) {
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

} // namespace gba::shell
