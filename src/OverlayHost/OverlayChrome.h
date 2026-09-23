#pragma once

#include "OverlayPosition.h"

#include <d2d1_1.h>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail {
class OverlayCompositionSurface;
}

namespace widgetrail::shell {

constexpr DWORD FixedChromeWindowStyle() noexcept { return WS_POPUP; }
constexpr DWORD FixedChromeWindowExStyle() noexcept {
    return WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP |
        WS_EX_NOACTIVATE | WS_EX_TOPMOST;
}

[[nodiscard]] RECT ComputeFixedChromeWindowBounds(
    const RECT& workArea, LONG width, LONG height,
    OverlayPosition position = OverlayPosition::Center, LONG sideMargin = 0) noexcept;

struct RadialChromePlacement final {
    RECT windowBounds{};
    RECT guideClientBounds{};
    RECT trayClientBounds{};
    LONG expansion{};
    LONG railOffset{};
    LONG wheelSize{};
};

// Extends only the transparent chrome canvas. Existing guide and rail screen
// anchors remain fixed, so content placement and available height are unchanged.
[[nodiscard]] RadialChromePlacement ComputeRadialChromePlacement(
    const RECT& workArea, const RECT& windowBounds,
    const RECT& guideClientBounds, const RECT& trayClientBounds,
    float pixelsPerDip) noexcept;
[[nodiscard]] std::optional<RECT> ComputeContentWindowBoundsAboveGuide(
    const RECT& workArea,
    LONG guideTop,
    LONG width,
    LONG height,
    LONG panelToGuideGap,
    OverlayPosition position = OverlayPosition::Center, LONG sideMargin = 0) noexcept;
[[nodiscard]] bool IsFixedChromeHit(
    POINT screenPoint, const RECT& guideBounds, const RECT& trayBounds) noexcept;
[[nodiscard]] bool ApplyFixedChromeWindow(
    HWND owner, HWND chrome, const RECT& bounds, bool show) noexcept;

using FixedChromeTargetInitializer = bool (*)(
    OverlayCompositionSurface&, HWND, std::wstring&);

// A failed frame must not turn every subsequent paint into a device-creation
// loop. Retry outside drawing, then wait for another explicit open if exhausted.
class CompositionRecoverySchedule final {
public:
    void Request() noexcept { pending_ = true; }
    void Reopen() noexcept { if (pending_ && attempts_ == 3) attempts_ = 0; }
    [[nodiscard]] UINT delay() const noexcept {
        constexpr UINT delays[]{250, 1000, 3000};
        return pending_ && attempts_ < 3 ? delays[attempts_] : 0;
    }
    [[nodiscard]] bool BeginAttempt() noexcept {
        if (!delay()) return false;
        ++attempts_;
        return true;
    }
    void Complete() noexcept { pending_ = false; attempts_ = 0; }
    [[nodiscard]] bool softwareAttempt() const noexcept { return attempts_ == 3; }
private:
    bool pending_{};
    unsigned attempts_{};
};

// WS_EX_NOREDIRECTIONBITMAP is fixed at window creation. Such windows must
// recover their compositor, never enter the layered HWND render-target path.
// Redirected test/legacy windows may still transition to/from layered drawing.
[[nodiscard]] bool SetContentCompositionMode(HWND content, bool composition) noexcept;

/// Initializes the content and chrome targets as one recoverable endpoint pair.
/// The optional initializer is an internal policy seam used to deterministically
/// exercise second-target failure; production passes the default.
[[nodiscard]] bool InitializeFixedChromeComposition(
    OverlayCompositionSurface& surface,
    HWND content,
    HWND chrome,
    ID2D1Factory1* factory,
    std::wstring& error,
    FixedChromeTargetInitializer initializeChrome = nullptr,
    bool softwareDevice = false);

/// Atomically makes the paired composition endpoints unavailable to callers
/// before hiding the companion chrome HWND during fallback/device recovery.
void ResetFixedChromeComposition(
    OverlayCompositionSurface& surface, HWND chrome) noexcept;

using FixedChromePointerActivation = void (*)(
    void* context, float contentClientX, float contentClientY) noexcept;

/// Maps a real chrome-client pointer release into the content HWND coordinate
/// space and forwards it to the existing sole activation owner.
[[nodiscard]] bool RouteFixedChromePointerRelease(
    HWND chrome,
    HWND content,
    LPARAM position,
    void* context,
    FixedChromePointerActivation activate) noexcept;

struct FixedChromeSessionKey final {
    RECT workArea{};
    UINT dpi{96};
    double interfaceScale{1.0};
    std::uint64_t appearanceRevision{};
    std::vector<std::wstring> catalogOrder;
};

[[nodiscard]] bool SameFixedChromeSession(
    const FixedChromeSessionKey& left,
    const FixedChromeSessionKey& right) noexcept;

enum class OuterChromeBoundary {
    ColorKeyAliased,
    PremultipliedAlpha,
};

struct RetainedTrayItem final {
    RECT bounds{};
    std::wstring identity;
    bool selected{};
    bool focused{};

    friend bool operator==(
        const RetainedTrayItem& left,
        const RetainedTrayItem& right) noexcept {
        return left.bounds.left == right.bounds.left &&
            left.bounds.top == right.bounds.top &&
            left.bounds.right == right.bounds.right &&
            left.bounds.bottom == right.bounds.bottom &&
            left.identity == right.identity &&
            left.selected == right.selected && left.focused == right.focused;
    }
};

struct RetainedTrayState final {
    unsigned int width{};
    unsigned int height{};
    std::uint64_t appearanceRevision{};
    std::vector<RetainedTrayItem> items;
    std::wstring statusKey;

    friend bool operator==(
        const RetainedTrayState&,
        const RetainedTrayState&) = default;
};

/// The small retained tray surface is an all-or-nothing chrome frame. Content
/// changes leave it untouched; any tray-owned state change replaces the whole
/// surface once so no cleared tile or clipped focus stroke can remain.
[[nodiscard]] bool RequiresTrayRepaint(
    const RetainedTrayState* retained,
    const RetainedTrayState& next) noexcept;

[[nodiscard]] constexpr bool RequiresImageReadyRepaint(
    const bool currentTrayPackageIconCompleted,
    const bool compositorBackgroundAdvanced,
    const bool visibleContentImageCompleted = false) noexcept {
    return currentTrayPackageIconCompleted || visibleContentImageCompleted || !compositorBackgroundAdvanced;
}

/// A color-keyed layered HWND cannot represent partially transparent pixels at
/// its outer boundary: antialiasing blends authored chrome into the key color
/// and turns the blend into an opaque dark fringe. Paint only the outer shell
/// boundary aliased; inner widget content keeps its ordinary antialiased,
/// rounded clip.
void FillColorKeyRoundedRectangle(
    ID2D1RenderTarget* target,
    const D2D1_ROUNDED_RECT& rectangle,
    ID2D1Brush* brush,
    OuterChromeBoundary boundary = OuterChromeBoundary::ColorKeyAliased) noexcept;

} // namespace widgetrail::shell
