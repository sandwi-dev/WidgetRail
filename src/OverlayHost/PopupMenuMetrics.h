#pragma once

#include "DeclarativeLayout.h"
#include <algorithm>
#include <cstddef>

namespace widgetrail::shell {

inline constexpr float kPopupMenuShadowMargin = 6.0F;
inline constexpr float kPopupMenuPadding = 6.0F;
inline constexpr float kPopupMenuInset = kPopupMenuShadowMargin + kPopupMenuPadding;
inline constexpr float kPopupMenuRowHeight = 48.0F;
inline constexpr float kPopupMenuContentInset = 18.0F;

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
