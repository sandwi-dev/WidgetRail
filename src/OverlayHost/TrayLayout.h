#pragma once

#include "DeclarativeLayout.h"
#include "OverlayPosition.h"

#include <cstddef>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail::shell {

struct TrayBand final {
    float top{};
    float bottom{};
};

enum class TrayWidthBasis {
    MonitorUsableWidth,
    ExactCapacity,
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
    // Tight visual envelope of the visible tiles and overflow controls. It is
    // not an interactive panel; pointer authority remains on the controls.
    declarative::Rect stripBounds;
    std::vector<TrayTileLayout> tiles;
    std::optional<TrayOverflowLayout> previousOverflow;
    std::optional<TrayOverflowLayout> nextOverflow;
    std::optional<declarative::Rect> statusBounds;
    std::wstring statusDescription;
    std::size_t totalCount{};
    std::optional<declarative::Rect> radialBounds;
    std::size_t page{};
    std::size_t pageCount{1};
};

inline constexpr std::size_t kRadialPageSize = 8;
[[nodiscard]] std::optional<TrayLayout> ComputeRadialTrayLayout(
    const declarative::Rect& bounds, std::size_t widgetCount, std::size_t selectedSlot,
    std::optional<std::size_t> browsedPage = std::nullopt);
[[nodiscard]] std::size_t RadialPageTarget(std::size_t count, std::size_t slot, int delta) noexcept;
[[nodiscard]] std::optional<std::size_t> RadialSector(float x, float y, float deadZone = 0.0F) noexcept;

/// Computes the single authoritative tray geometry used by paint, pointer
/// hit-testing, and Windows accessibility projection.
[[nodiscard]] std::optional<TrayLayout> ComputeTrayLayout(
    float width,
    float height,
    std::size_t widgetCount,
    std::size_t selectedSlot,
    std::optional<TrayBand> band = std::nullopt,
    TrayWidthBasis widthBasis = TrayWidthBasis::MonitorUsableWidth,
    OverlayPosition position = OverlayPosition::Center);

/// Resolves the tray's bounded capacity from the active monitor's usable width.
/// Exact-capacity child surfaces must not apply this policy a second time.
[[nodiscard]] float ComputeTrayCapacityWidth(float monitorUsableWidth) noexcept;

/// Extends the centered tray surface for passive status without reducing icon capacity.
[[nodiscard]] float ComputeTrayStatusSurfaceWidth(float monitorUsableWidth) noexcept;

/// Adds passive status in spare space without reducing icon capacity.
/// Corner layouts keep status at the outside edge of the rail.
/// Narrow surfaces compact or omit status before sacrificing icon space.
/// Exact-capacity child surfaces may supply their original icon capacity separately.
[[nodiscard]] std::optional<TrayLayout> ComputeTrayStatusLayout(
    float width, float height, std::size_t widgetCount, std::size_t selectedSlot,
    std::optional<TrayBand> band = std::nullopt,
    TrayWidthBasis widthBasis = TrayWidthBasis::MonitorUsableWidth,
    std::optional<float> iconCapacityWidth = std::nullopt,
    OverlayPosition position = OverlayPosition::Center);

[[nodiscard]] const TrayTileLayout* HitTestTray(
    const TrayLayout& layout, float x, float y) noexcept;

[[nodiscard]] const TrayOverflowLayout* HitTestTrayOverflow(
    const TrayLayout& layout, float x, float y) noexcept;

} // namespace widgetrail::shell
