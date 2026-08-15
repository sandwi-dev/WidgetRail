#pragma once

#include <d2d1.h>

#include <cstdint>
#include <string>
#include <vector>

namespace gba::shell {

enum class OuterChromeBoundary {
    ColorKeyAliased,
    PremultipliedAlpha,
};

struct RetainedTrayItem final {
    RECT bounds{};
    std::wstring identity;
    bool selected{};
    bool focused{};

    friend bool operator==(const RetainedTrayItem&, const RetainedTrayItem&) = default;
};

struct RetainedTrayState final {
    unsigned int width{};
    unsigned int height{};
    std::uint64_t appearanceRevision{};
    std::vector<RetainedTrayItem> items;
};

struct TrayInvalidationPlan final {
    bool full{};
    std::vector<RECT> dirtyRects;

    [[nodiscard]] bool empty() const noexcept {
        return !full && dirtyRects.empty();
    }
};

/// Retains the tray surface across content-only commits. A stable layout may
/// update only tiles whose identity/selection/focus changed; size, appearance,
/// or item-count changes rebuild the bounded tray surface.
[[nodiscard]] TrayInvalidationPlan PlanTrayInvalidation(
    const RetainedTrayState* retained,
    const RetainedTrayState& next);

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

} // namespace gba::shell
