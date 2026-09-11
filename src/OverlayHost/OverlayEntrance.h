#pragma once

#include <algorithm>
#include <cmath>
#include <cstddef>

namespace widgetrail {

inline constexpr float OverlayMinimumZoomScale = 0.92F;
inline constexpr float WidgetEntranceDistanceDip = 32.0F;
inline constexpr unsigned long long WidgetEntranceDurationMilliseconds = 180;

enum class WidgetEntranceDirection { None = 0, FromLeft = -1, FromRight = 1 };

// Explicit navigation wins at the ends of the rail. Pointer selection uses
// the relative slot, while first open and same-widget refresh have no direction.
[[nodiscard]] inline WidgetEntranceDirection ResolveWidgetEntranceDirection(
    const std::size_t previous, const std::size_t next, const std::size_t count,
    const int navigationDirection = 0) noexcept {
    if (previous >= count || next >= count || previous == next)
        return WidgetEntranceDirection::None;
    if (navigationDirection < 0) return WidgetEntranceDirection::FromLeft;
    if (navigationDirection > 0) return WidgetEntranceDirection::FromRight;
    return next > previous ? WidgetEntranceDirection::FromRight : WidgetEntranceDirection::FromLeft;
}

[[nodiscard]] inline float OverlayEntranceZoomScale(
    const float visibility, const bool reducedMotion) noexcept {
    return reducedMotion ? 1.0F : OverlayMinimumZoomScale + (1.0F - OverlayMinimumZoomScale) *
        (std::isfinite(visibility) ? std::clamp(visibility, 0.0F, 1.0F) : 1.0F);
}

[[nodiscard]] inline float WidgetEntranceOffset(
    const WidgetEntranceDirection direction, const float physicalPixelsPerDip,
    const bool reducedMotion) noexcept {
    if (reducedMotion || !std::isfinite(physicalPixelsPerDip) || physicalPixelsPerDip <= 0)
        return 0.0F;
    return static_cast<float>(direction) * WidgetEntranceDistanceDip * std::min(physicalPixelsPerDip, 8.0F);
}

[[nodiscard]] inline float SampleWidgetEntranceOffset(
    const float from, const unsigned long long elapsedMilliseconds) noexcept {
    if (!std::isfinite(from) || elapsedMilliseconds >= WidgetEntranceDurationMilliseconds) return 0.0F;
    const float remaining = 1.0F - static_cast<float>(elapsedMilliseconds) /
        static_cast<float>(WidgetEntranceDurationMilliseconds);
    return from * remaining * remaining * remaining;
}

} // namespace widgetrail
