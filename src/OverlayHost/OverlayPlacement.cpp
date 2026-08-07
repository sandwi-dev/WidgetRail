#include "OverlayPlacement.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace gba {
namespace {

[[nodiscard]] std::optional<int> ScaleDip(float value, unsigned int dpi) noexcept {
    if (!std::isfinite(value) || value < 0.0F || dpi == 0 || dpi > 1'000'000U) {
        return std::nullopt;
    }
    const double scaled = static_cast<double>(value) * static_cast<double>(dpi) / 96.0;
    if (!std::isfinite(scaled) || scaled > static_cast<double>(std::numeric_limits<int>::max())) {
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
    const long long workWidth = static_cast<long long>(workArea.right) - workArea.left;
    const long long workHeight = static_cast<long long>(workArea.bottom) - workArea.top;
    if (workWidth <= 0 || workHeight <= 0 || workWidth > std::numeric_limits<int>::max() ||
        workHeight > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }

    const auto desiredWidth = ScaleDip(desiredWidthDip, dpi);
    const auto desiredHeight = ScaleDip(desiredHeightDip, dpi);
    const auto side = ScaleDip(margins.side, dpi);
    const auto top = ScaleDip(margins.top, dpi);
    const auto bottom = ScaleDip(margins.bottom, dpi);
    if (!desiredWidth || !desiredHeight || !side || !top || !bottom ||
        *desiredWidth <= 0 || *desiredHeight <= 0) {
        return std::nullopt;
    }

    const long long horizontalMargins = static_cast<long long>(*side) * 2;
    const long long verticalMargins = static_cast<long long>(*top) + *bottom;
    const int availableWidth = static_cast<int>(std::max(1LL, workWidth - horizontalMargins));
    const int availableHeight = static_cast<int>(std::max(1LL, workHeight - verticalMargins));
    const int width = std::min(*desiredWidth, availableWidth);
    const int height = std::min(*desiredHeight, availableHeight);
    const long long x = static_cast<long long>(workArea.left) +
                        (workWidth - static_cast<long long>(width)) / 2;
    const long long preferredY = static_cast<long long>(workArea.bottom) - *bottom - height;
    const long long maximumY = static_cast<long long>(workArea.bottom) - height;
    const long long y = std::clamp(
        preferredY, static_cast<long long>(workArea.top), maximumY);
    if (x < std::numeric_limits<int>::min() || x > std::numeric_limits<int>::max() ||
        y < std::numeric_limits<int>::min() || y > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }
    return OverlayPlacement{static_cast<int>(x), static_cast<int>(y), width, height};
}

std::optional<OverlayRenderMetrics> ComputeOverlayRenderMetrics(
    const int clientWidthPx,
    const int clientHeightPx,
    const unsigned int dpi,
    const float interfaceScale) noexcept {
    if (clientWidthPx <= 0 || clientHeightPx <= 0 || dpi == 0 || dpi > 1'000'000U ||
        !std::isfinite(interfaceScale) || interfaceScale <= 0.0F || interfaceScale > 100.0F) {
        return std::nullopt;
    }

    const double logicalWidth = static_cast<double>(clientWidthPx) * 96.0 /
                                static_cast<double>(dpi);
    const double logicalHeight = static_cast<double>(clientHeightPx) * 96.0 /
                                 static_cast<double>(dpi);
    const double viewportWidth = logicalWidth / interfaceScale;
    const double viewportHeight = logicalHeight / interfaceScale;
    const double physicalPixelsPerDip = static_cast<double>(dpi) * interfaceScale / 96.0;
    if (!std::isfinite(viewportWidth) || !std::isfinite(viewportHeight) ||
        !std::isfinite(physicalPixelsPerDip) || viewportWidth <= 0.0 || viewportHeight <= 0.0 ||
        viewportWidth > std::numeric_limits<float>::max() ||
        viewportHeight > std::numeric_limits<float>::max() ||
        physicalPixelsPerDip > std::numeric_limits<float>::max()) {
        return std::nullopt;
    }

    return OverlayRenderMetrics{
        static_cast<float>(viewportWidth),
        static_cast<float>(viewportHeight),
        interfaceScale,
        static_cast<float>(physicalPixelsPerDip),
    };
}

std::optional<OverlaySurfaceGeometry> ComputeOverlaySurfaceGeometry(
    const float viewportWidthDip,
    const float viewportHeightDip,
    const float preferredPanelWidthDip) noexcept {
    if (!std::isfinite(viewportWidthDip) || !std::isfinite(viewportHeightDip) ||
        !std::isfinite(preferredPanelWidthDip) || viewportWidthDip <= 0.0F ||
        viewportHeightDip <= 0.0F || preferredPanelWidthDip <= 0.0F) {
        return std::nullopt;
    }

    const float preferredSideInset = std::clamp(viewportWidthDip * 0.04F, 8.0F, 36.0F);
    const float sideInset = std::min(preferredSideInset, viewportWidthDip * 0.1F);
    const float panelWidth = std::min(
        preferredPanelWidthDip, std::max(0.0F, viewportWidthDip - sideInset * 2.0F));
    const float panelX = (viewportWidthDip - panelWidth) * 0.5F;
    const float trayY = std::max(0.0F, viewportHeightDip - 112.0F);
    const float trayBottom = std::max(trayY, viewportHeightDip - 14.0F);
    const float panelY = std::min(
        std::min(20.0F, viewportHeightDip * 0.05F), trayY);
    const float trayReservation = std::min(158.0F, viewportHeightDip * 0.45F);
    const float preferredPanelBottom = std::max(
        panelY, viewportHeightDip - trayReservation);
    const float panelBottom = std::min(preferredPanelBottom, trayY);
    const float panelHeight = panelBottom - panelY;

    const float contentInset = std::min(
        1.0F, std::min(panelWidth, panelHeight) * 0.1F);
    const float footerReservation = std::min(55.0F, panelHeight * 0.3F);
    const float widgetViewportWidth = std::max(0.0F, panelWidth - contentInset * 2.0F);
    const float widgetViewportHeight = std::max(
        0.0F, panelHeight - footerReservation - contentInset);
    const float footerY = panelY + panelHeight - footerReservation;
    return OverlaySurfaceGeometry{
        panelX,
        panelY,
        panelWidth,
        panelHeight,
        trayY,
        trayBottom - trayY,
        panelX + contentInset,
        panelY + contentInset,
        widgetViewportWidth,
        widgetViewportHeight,
        footerY,
        footerReservation,
    };
}

} // namespace gba
