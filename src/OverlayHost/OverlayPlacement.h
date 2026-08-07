#pragma once

#include <optional>

namespace gba {

struct PhysicalRect final {
    int left{};
    int top{};
    int right{};
    int bottom{};
};

struct OverlayMarginsDip final {
    float side{24.0F};
    float top{24.0F};
    float bottom{32.0F};
};

struct OverlayPlacement final {
    int x{};
    int y{};
    int width{};
    int height{};
};

/// Computes a bottom-centered physical-pixel window rectangle that is fully
/// contained by the monitor work area. Logical dimensions and margins are
/// device-independent pixels; dpi is the target monitor's effective DPI.
[[nodiscard]] std::optional<OverlayPlacement> ComputeOverlayPlacement(
    PhysicalRect workArea,
    unsigned int dpi,
    float desiredWidthDip,
    float desiredHeightDip,
    OverlayMarginsDip margins = {}) noexcept;

} // namespace gba
