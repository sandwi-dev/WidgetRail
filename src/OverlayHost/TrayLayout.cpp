#include "TrayLayout.h"

#include <algorithm>
#include <cmath>

namespace widgetrail::shell {

std::optional<TrayLayout> ComputeTrayLayout(
    const float width,
    const float height,
    const std::size_t widgetCount,
    const std::size_t selectedSlot,
    const std::optional<TrayBand> band) {
    if (widgetCount == 0 || !std::isfinite(width) || !std::isfinite(height) ||
        width <= 0.0F || height <= 0.0F) return std::nullopt;

    constexpr float preferredTileSize = 64.0F;
    constexpr float minimumFullCatalogTileSize = 44.0F;
    constexpr float gap = 14.0F;
    constexpr float preferredOverflowSize = 36.0F;
    // The tray is host chrome, so its coordinate envelope must not expand and
    // contract with each widget's preferred content width. Wider surfaces
    // center this bounded band; genuinely constrained work areas still use the
    // established overflow path.
    constexpr float stableTrayWidth = 560.0F;
    const float layoutWidth = std::min(width, stableTrayWidth);
    const float layoutOffsetX = (width - layoutWidth) * 0.5F;
    const float stripTop = band
        ? band->top
        : std::max(0.0F, height - 112.0F);
    const float stripBottom = band
        ? band->bottom
        : std::max(stripTop, height - 14.0F);
    const float stripHeight = stripBottom - stripTop;
    if (!std::isfinite(stripTop) || !std::isfinite(stripBottom) ||
        stripHeight <= 0.0F) return std::nullopt;

    const float verticalPadding = std::min(
        16.0F, std::max(0.0F, (stripHeight - preferredTileSize) * 0.5F));
    const float horizontalPadding = std::min(14.0F, layoutWidth * 0.15F);
    const float preferredBoundedTileSize = std::max(
        1.0F, std::min({preferredTileSize,
                        layoutWidth - horizontalPadding * 2.0F,
                        stripHeight - verticalPadding * 2.0F}));
    const float allCatalogTileSize =
        (layoutWidth - horizontalPadding * 2.0F -
         gap * static_cast<float>(widgetCount > 0 ? widgetCount - 1 : 0)) /
        static_cast<float>(widgetCount);
    const bool stableCatalogFits = width >= stableTrayWidth &&
        allCatalogTileSize >= minimumFullCatalogTileSize &&
        stripHeight - verticalPadding * 2.0F >= minimumFullCatalogTileSize;
    const float tileSize = stableCatalogFits
        ? std::min(preferredBoundedTileSize, allCatalogTileSize)
        : preferredBoundedTileSize;
    const float stripPadding = std::min(
        14.0F, std::max(0.0F, (layoutWidth - tileSize) * 0.5F));
    const auto unreservedMaximum = stableCatalogFits
        ? widgetCount
        : static_cast<std::size_t>(std::max(
            1.0F, std::floor((layoutWidth - horizontalPadding * 2.0F + gap) /
                             (preferredTileSize + gap))));
    const bool showOverflow = widgetCount > unreservedMaximum && width >= 192.0F;
    const float overflowSize = showOverflow
        ? std::min(preferredOverflowSize, tileSize)
        : 0.0F;
    const float overflowReserve = showOverflow
        ? 2.0F * (overflowSize + gap)
        : 0.0F;
    const auto maximumVisible = showOverflow
        ? static_cast<std::size_t>(std::max(
            1.0F, std::floor(
                (layoutWidth - horizontalPadding * 2.0F - overflowReserve + gap) /
                (preferredTileSize + gap))))
        : unreservedMaximum;
    const std::size_t visibleCount = std::min(widgetCount, maximumVisible);
    const std::size_t boundedSelected = std::min(selectedSlot, widgetCount - 1);
    const std::size_t half = visibleCount / 2;
    const std::size_t maximumFirst = widgetCount - visibleCount;
    const std::size_t firstSlot = std::min(
        boundedSelected > half ? boundedSelected - half : 0U,
        maximumFirst);
    const float stripWidth = tileSize * static_cast<float>(visibleCount) +
        gap * static_cast<float>(visibleCount - 1) + stripPadding * 2.0F +
        overflowReserve;
    const float stripLeft = layoutOffsetX + (layoutWidth - stripWidth) * 0.5F;
    const float tileLeft = stripLeft + stripPadding +
        (showOverflow ? overflowSize + gap : 0.0F);

    TrayLayout result;
    result.stripBounds = {stripLeft, stripTop, stripWidth, stripHeight};
    result.totalCount = widgetCount;
    result.tiles.reserve(visibleCount);
    for (std::size_t visibleIndex = 0; visibleIndex < visibleCount; ++visibleIndex) {
        result.tiles.push_back({
            firstSlot + visibleIndex,
            {
                tileLeft +
                    static_cast<float>(visibleIndex) * (tileSize + gap),
                stripTop + verticalPadding,
                tileSize,
                tileSize,
            },
        });
    }
    if (showOverflow && firstSlot > 0) {
        result.previousOverflow = TrayOverflowLayout{
            TrayOverflowDirection::Previous,
            firstSlot,
            firstSlot - 1,
            {
                stripLeft + stripPadding,
                stripTop + (stripHeight - overflowSize) * 0.5F,
                overflowSize,
                overflowSize,
            },
        };
    }
    const std::size_t firstHiddenAfter = firstSlot + visibleCount;
    if (showOverflow && firstHiddenAfter < widgetCount) {
        result.nextOverflow = TrayOverflowLayout{
            TrayOverflowDirection::Next,
            widgetCount - firstHiddenAfter,
            firstHiddenAfter,
            {
                tileLeft + tileSize * static_cast<float>(visibleCount) +
                    gap * static_cast<float>(visibleCount),
                stripTop + (stripHeight - overflowSize) * 0.5F,
                overflowSize,
                overflowSize,
            },
        };
    }
    return result;
}

const TrayTileLayout* HitTestTray(
    const TrayLayout& layout, const float x, const float y) noexcept {
    const auto found = std::find_if(
        layout.tiles.begin(), layout.tiles.end(),
        [&](const TrayTileLayout& tile) {
            return x >= tile.bounds.x && y >= tile.bounds.y &&
                x < tile.bounds.x + tile.bounds.width &&
                y < tile.bounds.y + tile.bounds.height;
        });
    return found == layout.tiles.end() ? nullptr : &*found;
}

const TrayOverflowLayout* HitTestTrayOverflow(
    const TrayLayout& layout, const float x, const float y) noexcept {
    const auto contains = [&](const std::optional<TrayOverflowLayout>& overflow) {
        return overflow && x >= overflow->bounds.x && y >= overflow->bounds.y &&
            x < overflow->bounds.x + overflow->bounds.width &&
            y < overflow->bounds.y + overflow->bounds.height;
    };
    if (contains(layout.previousOverflow)) return &*layout.previousOverflow;
    if (contains(layout.nextOverflow)) return &*layout.nextOverflow;
    return nullptr;
}

} // namespace widgetrail::shell
