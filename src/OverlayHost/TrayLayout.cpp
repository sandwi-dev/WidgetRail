#include "TrayLayout.h"

#include <algorithm>
#include <cmath>

namespace widgetrail::shell {

namespace {

constexpr float kPreferredTileSize = 64.0F;
constexpr float kMinimumFullCatalogTileSize = 44.0F;
constexpr float kGap = 14.0F;
constexpr float kPreferredOverflowSize = 36.0F;
constexpr float kMaximumHorizontalPadding = 14.0F;
constexpr float kMonitorTrayWidthFraction = 0.60F;
constexpr float kMinimumOverflowCapacity =
    kPreferredTileSize + 2.0F * (kPreferredOverflowSize + kGap) +
    2.0F * kMaximumHorizontalPadding;

} // namespace

float ComputeTrayCapacityWidth(const float monitorUsableWidth) noexcept {
    if (!std::isfinite(monitorUsableWidth) || monitorUsableWidth <= 0.0F)
        return 0.0F;
    return std::min(
        monitorUsableWidth,
        std::max(
            monitorUsableWidth * kMonitorTrayWidthFraction,
            kMinimumOverflowCapacity));
}

std::optional<TrayLayout> ComputeTrayLayout(
    const float width,
    const float height,
    const std::size_t widgetCount,
    const std::size_t selectedSlot,
    const std::optional<TrayBand> band,
    const TrayWidthBasis widthBasis) {
    if (widgetCount == 0 || !std::isfinite(width) || !std::isfinite(height) ||
        width <= 0.0F || height <= 0.0F) return std::nullopt;

    const float layoutWidth = widthBasis == TrayWidthBasis::ExactCapacity
        ? width
        : ComputeTrayCapacityWidth(width);
    if (layoutWidth <= 0.0F) return std::nullopt;
    const float layoutOffsetX = (width - layoutWidth) * 0.5F;
    const float bandTop = band
        ? band->top
        : std::max(0.0F, height - 112.0F);
    const float bandBottom = band
        ? band->bottom
        : std::max(bandTop, height - 14.0F);
    const float bandHeight = bandBottom - bandTop;
    if (!std::isfinite(bandTop) || !std::isfinite(bandBottom) ||
        bandHeight <= 0.0F) return std::nullopt;

    const float verticalPadding = std::min(
        16.0F, std::max(0.0F, (bandHeight - kPreferredTileSize) * 0.5F));
    const float horizontalPadding = std::min(
        kMaximumHorizontalPadding, layoutWidth * 0.15F);
    const float preferredBoundedTileSize = std::max(
        1.0F, std::min({kPreferredTileSize,
                        layoutWidth - horizontalPadding * 2.0F,
                        bandHeight - verticalPadding * 2.0F}));
    const bool fullCatalogNeedsSpare = widgetCount > 1 && widgetCount % 2 == 0;
    const std::size_t fullCatalogPositionCount =
        widgetCount + (fullCatalogNeedsSpare ? 1U : 0U);
    const float allCatalogTileSize =
        (layoutWidth - horizontalPadding * 2.0F -
         kGap * static_cast<float>(fullCatalogPositionCount - 1)) /
        static_cast<float>(fullCatalogPositionCount);
    const bool stableCatalogFits =
        allCatalogTileSize >= kMinimumFullCatalogTileSize &&
        bandHeight - verticalPadding * 2.0F >= kMinimumFullCatalogTileSize;
    const float tileSize = stableCatalogFits
        ? std::min(preferredBoundedTileSize, allCatalogTileSize)
        : preferredBoundedTileSize;
    const auto unreservedMaximum = stableCatalogFits
        ? widgetCount
        : static_cast<std::size_t>(std::max(
            1.0F, std::floor((layoutWidth - horizontalPadding * 2.0F + kGap) /
                             (kPreferredTileSize + kGap))));
    const bool showOverflow = widgetCount > unreservedMaximum &&
        layoutWidth >= kMinimumOverflowCapacity;
    const float overflowSize = showOverflow
        ? std::min(kPreferredOverflowSize, tileSize)
        : 0.0F;
    const float overflowReserve = showOverflow
        ? 2.0F * (overflowSize + kGap)
        : 0.0F;
    auto maximumVisible = showOverflow
        ? static_cast<std::size_t>(std::max(
            1.0F, std::floor(
                (layoutWidth - horizontalPadding * 2.0F - overflowReserve + kGap) /
                (kPreferredTileSize + kGap))))
        : unreservedMaximum;
    if (showOverflow && maximumVisible > 1 && maximumVisible % 2 == 0)
        --maximumVisible;
    const std::size_t visibleCount = std::min(widgetCount, maximumVisible);
    const std::size_t boundedSelected = std::min(selectedSlot, widgetCount - 1);
    const std::size_t half = visibleCount / 2;
    // Selection already wraps in OverlayState. Project that same cyclic order
    // through a fixed center slot without duplicating catalog identities.
    const std::size_t firstSlot =
        (boundedSelected + widgetCount - half) % widgetCount;
    // A complete even catalog cannot surround one selected tile symmetrically.
    // Reserve one ordinary tile position on the trailing side instead of
    // stretching gaps or cloning an item.
    const std::size_t visualPositionCount = visibleCount +
        (!showOverflow && visibleCount == widgetCount && fullCatalogNeedsSpare
            ? 1U : 0U);
    const float stripWidth = tileSize * static_cast<float>(visualPositionCount) +
        kGap * static_cast<float>(visualPositionCount - 1) + overflowReserve;
    const float stripLeft = layoutOffsetX + (layoutWidth - stripWidth) * 0.5F;
    const float stripTop = bandTop + (bandHeight - tileSize) * 0.5F;
    const float tileLeft = stripLeft +
        (showOverflow ? overflowSize + kGap : 0.0F);

    TrayLayout result;
    result.stripBounds = {stripLeft, stripTop, stripWidth, tileSize};
    result.totalCount = widgetCount;
    result.tiles.reserve(visibleCount);
    for (std::size_t visibleIndex = 0; visibleIndex < visibleCount; ++visibleIndex) {
        result.tiles.push_back({
            (firstSlot + visibleIndex) % widgetCount,
            {
                tileLeft +
                    static_cast<float>(visibleIndex) * (tileSize + kGap),
                stripTop,
                tileSize,
                tileSize,
            },
        });
    }
    if (showOverflow) {
        result.previousOverflow = TrayOverflowLayout{
            TrayOverflowDirection::Previous,
            widgetCount - visibleCount,
            (firstSlot + widgetCount - 1) % widgetCount,
            {
                stripLeft,
                stripTop + (tileSize - overflowSize) * 0.5F,
                overflowSize,
                overflowSize,
            },
        };
    }
    if (showOverflow) {
        result.nextOverflow = TrayOverflowLayout{
            TrayOverflowDirection::Next,
            widgetCount - visibleCount,
            (firstSlot + visibleCount) % widgetCount,
            {
                tileLeft + tileSize * static_cast<float>(visibleCount) +
                    kGap * static_cast<float>(visibleCount),
                stripTop + (tileSize - overflowSize) * 0.5F,
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
