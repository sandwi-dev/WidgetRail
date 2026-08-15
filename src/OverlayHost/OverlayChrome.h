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
