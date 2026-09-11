#pragma once

#include <algorithm>
#include <cmath>

namespace widgetrail {

inline constexpr float OverlayMinimumZoomScale = 0.88F;

[[nodiscard]] inline float OverlayEntranceZoomScale(
    const float visibility, const bool reducedMotion) noexcept {
    return reducedMotion ? 1.0F : OverlayMinimumZoomScale + (1.0F - OverlayMinimumZoomScale) *
        (std::isfinite(visibility) ? std::clamp(visibility, 0.0F, 1.0F) : 1.0F);
}

} // namespace widgetrail
