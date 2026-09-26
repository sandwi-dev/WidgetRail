#pragma once

#include "WidgetAnimationPolicy.h"
#include "NativeStyle.h"
#include <optional>
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

inline bool HasStableFocusTarget(const FocusTarget &previous,
                                std::span<const FocusTarget> currentTargets) noexcept {
    const auto old = std::find_if(currentTargets.begin(), currentTargets.end(), [&](const auto &target) {
        return target.key == previous.key && target.scope == previous.scope;
    });
    return old != currentTargets.end() && SameFocusRect(old->bounds, previous.bounds) &&
           SameFocusRect(old->clip, previous.clip);
}

inline bool InPlaceFocus(FocusStyle style) noexcept {
    return FocusPresetFor(style).inPlace;
}

inline unsigned FocusDuration(FocusStyle style) noexcept {
    return FocusPresetFor(style).milliseconds;
}

inline Recipe FocusFade(Rect bounds, Rect clip, float from, float to, unsigned duration = FocusDuration(FocusStyle::Fade)) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.from.opacity = from;
    recipe.to.opacity = to;
    recipe.milliseconds = from == to ? 0 : duration;
    return recipe;
}

inline Rect FocusSettleBounds(Rect bounds) noexcept {
    // Stay within the control even when a grid/row clips exactly to its height.
    // A fixed inset is visible on wide buttons without touching widget layout.
    const float inset = std::clamp(std::min(bounds.width, bounds.height) * .15F, 0.0F, 6.0F);
    return {bounds.x + inset, bounds.y + inset, bounds.width - inset * 2, bounds.height - inset * 2};
}

inline Recipe FocusSettle(Rect bounds, Rect clip, std::optional<Pose> previous = {}) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.from = previous.value_or(Pose{FocusSettleBounds(bounds), .65F, clip});
    recipe.from.clip = clip;
    recipe.milliseconds = SameFocusRect(recipe.from.bounds, bounds) && recipe.from.opacity == 1
        ? 0 : FocusDuration(FocusStyle::Settle);
    return recipe;
}

inline Recipe FocusEnter(FocusStyle style, Rect bounds, Rect clip, std::optional<Pose> previous = {}) noexcept {
    if (style == FocusStyle::Settle) return FocusSettle(bounds, clip, previous);
    if (style == FocusStyle::Fade)
        return FocusFade(bounds, clip, previous ? previous->opacity : 0, 1, FocusDuration(style));
    return Stationary(bounds, clip);
}

inline bool HasFocusSurfaceChange(const NativeRenderStyle &base, const NativeRenderStyle &focus) noexcept {
    return base.background() != focus.background() || base.surfaceShading() != focus.surfaceShading() ||
           base.shadowColor() != focus.shadowColor() || base.shadowBlurPx() != focus.shadowBlurPx() ||
           base.shadowOffsetXPx() != focus.shadowOffsetXPx() || base.shadowOffsetYPx() != focus.shadowOffsetYPx() ||
           base.borderEdges() != focus.borderEdges() ||
           base.cornerRadiusPx() != focus.cornerRadiusPx() || base.shape() != focus.shape();
}

inline bool CanSeparateFocusSurface(const NativeRenderStyle &base, const NativeRenderStyle &focus,
                                    bool compositorScale = false) noexcept {
    const auto &before = base.borderEdges(), &after = focus.borderEdges();
    return before.top.widthPx == after.top.widthPx && before.right.widthPx == after.right.widthPx &&
        before.bottom.widthPx == after.bottom.widthPx && before.left.widthPx == after.left.widthPx &&
        base.backgroundBlurPx() == 0 && focus.backgroundBlurPx() == 0 &&
        base.opacity() == focus.opacity() && (compositorScale || (base.scale() == 1 && focus.scale() == 1)) &&
        base.translateXPx() == focus.translateXPx() && base.translateYPx() == focus.translateYPx() &&
        base.widthPx() == focus.widthPx() && base.heightPx() == focus.heightPx() &&
        base.minWidthPx() == focus.minWidthPx() && base.minHeightPx() == focus.minHeightPx() &&
        base.maxWidthPx() == focus.maxWidthPx() && base.maxHeightPx() == focus.maxHeightPx() &&
        base.paddingPx() == focus.paddingPx() && base.marginPx() == focus.marginPx();
}

inline Rect ScaleControl(Rect bounds, float scale) noexcept {
    scale = std::clamp(scale, .5F, 2.0F);
    const float x = bounds.width * (scale - 1) * .5F, y = bounds.height * (scale - 1) * .5F;
    return {bounds.x - x, bounds.y - y, bounds.width * scale, bounds.height * scale};
}

inline Recipe ControlScale(Rect bounds, Rect clip, Pose previous, float scale, unsigned duration,
                           Curve curve = Smooth) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.from = previous;
    recipe.from.clip = clip;
    recipe.to.bounds = ScaleControl(bounds, scale);
    recipe.milliseconds = duration;
    recipe.curve = curve;
    return recipe;
}

} // namespace widgetrail::animation
