#pragma once

namespace widgetrail {

enum class OverlayPosition { Center, BottomLeft, BottomRight };

// Shared by placement, presentation transforms, and fixed shell chrome.
[[nodiscard]] constexpr float HorizontalAnchor(const OverlayPosition position) noexcept {
    switch (position) {
    case OverlayPosition::BottomLeft: return 0.0F;
    case OverlayPosition::BottomRight: return 1.0F;
    default: return 0.5F;
    }
}

} // namespace widgetrail
