#pragma once

#include "WidgetAnimationPolicy.h"
#include <span>
#include <string>
#include <string_view>

namespace widgetrail::animation {

// Small, renderer-independent identity/geometry records. They do not retain
// widget nodes, images, actions or cursor pages.
struct FocusTarget {
    std::wstring key, scope;
    Rect bounds, clip;
};

inline void AppendMotionIdentity(std::wstring &identity, std::wstring_view part) {
    identity += std::to_wstring(part.size()) + L":";
    identity.append(part);
}

inline bool SameFocusRect(Rect a, Rect b) noexcept {
    return std::abs(a.x - b.x) < .01F && std::abs(a.y - b.y) < .01F &&
           std::abs(a.width - b.width) < .01F && std::abs(a.height - b.height) < .01F;
}

inline bool FullyVisible(const FocusTarget &target) noexcept {
    const auto &a = target.bounds, &b = target.clip;
    return a.width > 0 && a.height > 0 && a.x >= b.x - .01F && a.y >= b.y - .01F &&
           a.x + a.width <= b.x + b.width + .01F && a.y + a.height <= b.y + b.height + .01F;
}

inline bool HasStableFocusTarget(const FocusTarget &previous,
                                std::span<const FocusTarget> currentTargets) noexcept {
    const auto old = std::find_if(currentTargets.begin(), currentTargets.end(), [&](const auto &target) {
        return target.key == previous.key && target.scope == previous.scope;
    });
    return old != currentTargets.end() && SameFocusRect(old->bounds, previous.bounds) &&
           SameFocusRect(old->clip, previous.clip);
}

inline bool CanMoveFocus(const FocusTarget &previous, const FocusTarget &next,
                         std::span<const FocusTarget> currentTargets) noexcept {
    if (previous.key == next.key || previous.scope != next.scope ||
        !SameFocusRect(previous.clip, next.clip) || !FullyVisible(previous) || !FullyVisible(next) ||
        !HasStableFocusTarget(previous, currentTargets))
        return false;
    const auto &a = previous.bounds, &b = next.bounds;
    const float gapX = std::max({b.x - a.x - a.width, a.x - b.x - b.width, 0.0F});
    const float gapY = std::max({b.y - a.y - a.height, a.y - b.y - b.height, 0.0F});
    return gapX <= std::max(a.width, b.width) && gapY <= std::max(a.height, b.height) &&
           b.width >= a.width * .5F && b.width <= a.width * 2 &&
           b.height >= a.height * .5F && b.height <= a.height * 2;
}

inline Recipe FocusMove(Rect bounds, Rect clip, Pose previous) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.from.bounds = previous.bounds;
    recipe.milliseconds = 140;
    return recipe;
}

} // namespace widgetrail::animation
