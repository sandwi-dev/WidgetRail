#pragma once

#include "DeclarativeLayout.h"

#include <cstddef>
#include <optional>
#include <vector>

namespace widgetrail::shell {

struct TrayBand final {
    float top{};
    float bottom{};
};

struct TrayTileLayout final {
    std::size_t slot{};
    declarative::Rect bounds;
};

enum class TrayOverflowDirection {
    Previous,
    Next,
};

struct TrayOverflowLayout final {
    TrayOverflowDirection direction{TrayOverflowDirection::Previous};
    std::size_t hiddenCount{};
    std::size_t targetSlot{};
    declarative::Rect bounds;
};

struct TrayLayout final {
    declarative::Rect stripBounds;
    std::vector<TrayTileLayout> tiles;
    std::optional<TrayOverflowLayout> previousOverflow;
    std::optional<TrayOverflowLayout> nextOverflow;
    std::size_t totalCount{};
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

[[nodiscard]] const TrayOverflowLayout* HitTestTrayOverflow(
    const TrayLayout& layout, float x, float y) noexcept;

} // namespace widgetrail::shell
