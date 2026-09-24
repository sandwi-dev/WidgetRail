#pragma once

#include "DeclarativeLayout.h"
#include <algorithm>
#include <cstddef>
#include <cmath>

namespace widgetrail::shell {

inline constexpr float kPopupMenuShadowMargin = 6.0F;
inline constexpr float kPopupMenuPadding = 6.0F;
inline constexpr float kPopupMenuInset = kPopupMenuShadowMargin + kPopupMenuPadding;
inline constexpr float kPopupMenuRowHeight = 48.0F;
inline constexpr float kPopupMenuContentInset = 18.0F;
inline constexpr float kPopupMenuMinimumWidth = 220.0F;
inline constexpr float kPopupMenuMaximumWidth = 360.0F;
inline constexpr float kPopupMenuCheckmarkAdvance = 22.0F;
inline constexpr float kPopupMenuGlyphAdvance = 28.0F;

inline float PopupMenuWidth(float contentWidth, float availableWidth = kPopupMenuMaximumWidth) noexcept {
    if (!std::isfinite(availableWidth) || availableWidth <= 0) return 0;
    if (!std::isfinite(contentWidth) || contentWidth < 0) contentWidth = 0;
    const float desired = std::ceil(contentWidth + kPopupMenuInset * 2 + kPopupMenuContentInset + 12);
    return std::min(availableWidth, std::clamp(desired, kPopupMenuMinimumWidth, kPopupMenuMaximumWidth));
}

// Align visible panel edges, leaving the reserved shadow outside the anchor.
inline float PopupMenuLeft(const declarative::Rect& anchor, const declarative::Rect& viewport,
    float menuWidth) noexcept {
    menuWidth = std::clamp(menuWidth, 0.0F, std::max(0.0F, viewport.width));
    const float minimum = viewport.x;
    const float maximum = viewport.x + std::max(0.0F, viewport.width - menuWidth);
    const float leftAligned = anchor.x - kPopupMenuShadowMargin;
    if (leftAligned >= minimum && leftAligned <= maximum) return leftAligned;
    const float rightAligned = anchor.x + anchor.width + kPopupMenuShadowMargin - menuWidth;
    if (rightAligned >= minimum && rightAligned <= maximum) return rightAligned;
    return std::clamp(leftAligned, minimum, maximum);
}

inline constexpr float PopupMenuHeight(std::size_t count) noexcept {
    return kPopupMenuInset * 2 + kPopupMenuRowHeight * static_cast<float>(count);
}

inline declarative::Rect PopupMenuItemBounds(const declarative::Rect& bounds, std::size_t index,
    float rowHeight = kPopupMenuRowHeight, float inset = kPopupMenuInset) noexcept {
    return {bounds.x + inset,
        bounds.y + inset + rowHeight * static_cast<float>(index),
        std::max(0.0F, bounds.width - inset * 2), rowHeight};
}

} // namespace widgetrail::shell
