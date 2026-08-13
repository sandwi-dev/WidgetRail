#include "../OverlayHost/OverlayPlacement.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <optional>

namespace gba {
namespace {

std::optional<int> ScalePlacementDip(
    const float value,
    const unsigned int dpi) noexcept {
    if (!std::isfinite(value) || value < 0.0F ||
        dpi == 0 || dpi > 1'000'000U) {
        return std::nullopt;
    }
    const double scaled = static_cast<double>(value) *
                          static_cast<double>(dpi) / 96.0;
    if (!std::isfinite(scaled) ||
        scaled > static_cast<double>(std::numeric_limits<int>::max())) {
        return std::nullopt;
    }
    return static_cast<int>(std::lround(scaled));
}

} // namespace

std::optional<OverlayPlacement> ComputeOverlayPlacement(
    const PhysicalRect workArea,
    const unsigned int dpi,
    const float desiredWidthDip,
    const float desiredHeightDip,
    const OverlayMarginsDip margins) noexcept {
    const long long workWidth =
        static_cast<long long>(workArea.right) - workArea.left;
    const long long workHeight =
        static_cast<long long>(workArea.bottom) - workArea.top;
    if (workWidth <= 0 || workHeight <= 0 ||
        workWidth > std::numeric_limits<int>::max() ||
        workHeight > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }

    const auto desiredWidth = ScalePlacementDip(desiredWidthDip, dpi);
    const auto desiredHeight = ScalePlacementDip(desiredHeightDip, dpi);
    const auto side = ScalePlacementDip(margins.side, dpi);
    const auto top = ScalePlacementDip(margins.top, dpi);
    const auto bottom = ScalePlacementDip(margins.bottom, dpi);
    if (!desiredWidth || !desiredHeight || !side || !top || !bottom ||
        *desiredWidth <= 0 || *desiredHeight <= 0) {
        return std::nullopt;
    }

    const long long horizontalMargins = static_cast<long long>(*side) * 2;
    const long long verticalMargins = static_cast<long long>(*top) + *bottom;
    const int availableWidth = static_cast<int>(
        std::max(1LL, workWidth - horizontalMargins));
    const int availableHeight = static_cast<int>(
        std::max(1LL, workHeight - verticalMargins));
    const int width = std::min(*desiredWidth, availableWidth);
    const int height = std::min(*desiredHeight, availableHeight);
    const long long x = static_cast<long long>(workArea.left) +
                        (workWidth - static_cast<long long>(width)) / 2;
    const long long preferredY =
        static_cast<long long>(workArea.bottom) - *bottom - height;
    const long long maximumY =
        static_cast<long long>(workArea.bottom) - height;
    const long long y = std::clamp(
        preferredY,
        static_cast<long long>(workArea.top),
        maximumY);
    if (x < std::numeric_limits<int>::min() ||
        x > std::numeric_limits<int>::max() ||
        y < std::numeric_limits<int>::min() ||
        y > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }
    return OverlayPlacement{
        static_cast<int>(x), static_cast<int>(y), width, height};
}

} // namespace gba
