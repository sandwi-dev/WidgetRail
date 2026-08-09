#pragma once

#include "DeclarativeLayout.h"

#include <cstddef>
#include <optional>
#include <vector>

namespace gba::shell {

struct TrayBand final {
    float top{};
    float bottom{};
};

struct TrayTileLayout final {
    std::size_t slot{};
    declarative::Rect bounds;
};

struct TrayLayout final {
    declarative::Rect stripBounds;
    std::vector<TrayTileLayout> tiles;
};

/// Computes the single authoritative tray geometry used by paint, pointer
/// hit-testing, and Windows accessibility projection.
[[nodiscard]] std::optional<TrayLayout> ComputeTrayLayout(
    float width,
    float height,
    std::size_t widgetCount,
    std::size_t selectedSlot,
    std::optional<TrayBand> band = std::nullopt);

[[nodiscard]] const TrayTileLayout* HitTestTray(
    const TrayLayout& layout, float x, float y) noexcept;

} // namespace gba::shell
