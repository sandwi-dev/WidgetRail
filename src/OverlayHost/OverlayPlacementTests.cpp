#include "OverlayPlacement.h"

#include <cstdlib>
#include <cmath>
#include <iostream>
#include <limits>
#include <array>

namespace {

int checks{};

void Check(bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void FullyContained(const gba::OverlayPlacement& value, const gba::PhysicalRect& work) {
    Check(value.width > 0 && value.height > 0, "placement has positive size");
    Check(value.x >= work.left && value.y >= work.top, "placement starts inside work area");
    Check(value.x + value.width <= work.right, "placement right edge is contained");
    Check(value.y + value.height <= work.bottom, "placement bottom edge is contained");
}

void SurfaceContained(
    const gba::OverlaySurfaceGeometry& geometry,
    const float width,
    const float height) {
    constexpr float tolerance = 0.002F;
    for (const float value : {
             geometry.panelX, geometry.panelY, geometry.panelWidth,
             geometry.panelHeight, geometry.trayY, geometry.trayHeight,
             geometry.widgetViewportX, geometry.widgetViewportY,
             geometry.widgetViewportWidth, geometry.widgetViewportHeight,
             geometry.footerY, geometry.footerHeight,
         }) {
        Check(std::isfinite(value), "surface geometry remains finite");
        Check(value >= 0.0F, "surface geometry never becomes negative");
    }
    Check(geometry.panelX + geometry.panelWidth <= width + tolerance &&
          geometry.panelY + geometry.panelHeight <= height + tolerance,
          "panel remains fully contained");
    Check(std::abs(
              geometry.panelX + geometry.panelWidth * 0.5F - width * 0.5F) <= tolerance,
          "responsive panel remains horizontally centered");
    Check(geometry.trayY + geometry.trayHeight <= height + tolerance,
          "persistent tray band remains fully contained");
    Check(geometry.panelY + geometry.panelHeight <= geometry.trayY + tolerance,
          "floating widget panel never overlaps persistent tray band");
    Check(geometry.widgetViewportX >= geometry.panelX - tolerance &&
          geometry.widgetViewportY >= geometry.panelY - tolerance,
          "widget viewport begins inside panel");
    Check(geometry.widgetViewportX + geometry.widgetViewportWidth <=
              geometry.panelX + geometry.panelWidth + tolerance &&
          geometry.widgetViewportY + geometry.widgetViewportHeight <=
              geometry.panelY + geometry.panelHeight + tolerance,
          "widget viewport remains contained by panel");
    Check(geometry.footerY >= geometry.panelY - tolerance &&
          geometry.footerY + geometry.footerHeight <=
              geometry.panelY + geometry.panelHeight + tolerance,
          "adaptive footer remains inside panel");
    Check(geometry.widgetViewportY + geometry.widgetViewportHeight <=
              geometry.footerY + tolerance,
          "widget content never overlaps host footer chrome");
}

} // namespace

int main() {
    using namespace gba;
    for (const auto work : {
             PhysicalRect{0, 0, 1280, 720},
             PhysicalRect{0, 0, 1920, 1040},
             PhysicalRect{0, 0, 3840, 2120},
             PhysicalRect{0, 0, 1080, 1880},
             PhysicalRect{-3440, -200, 0, 1240},
             PhysicalRect{1920, 100, 7040, 1320},
         }) {
        for (const unsigned int dpi : {96U, 120U, 144U, 192U}) {
            const auto value = ComputeOverlayPlacement(work, dpi, 1180, 700);
            Check(value.has_value(), "common resolution produced placement");
            FullyContained(*value, work);
            Check(value->x + value->width / 2 == work.left + (work.right - work.left) / 2 ||
                      value->x + value->width / 2 + 1 == work.left + (work.right - work.left) / 2,
                  "placement is horizontally centered");
        }
    }

    for (const auto work : {
             PhysicalRect{0, 0, 640, 360},
             PhysicalRect{10, 20, 11, 21},
             PhysicalRect{-100, -100, -93, -97},
             PhysicalRect{-1'000'000'000, -20, 1'000'000'000, 20},
         }) {
        const auto small = ComputeOverlayPlacement(work, 96, 1180, 700);
        Check(small.has_value(), "arbitrary positive monitor extent is supported");
        FullyContained(*small, work);
    }

    const auto oversizedMargins = ComputeOverlayPlacement(
        {0, 0, 320, 180}, 96, 1180, 700,
        OverlayMarginsDip{1'000'000.0F, 1'000'000.0F, 1'000'000.0F});
    Check(oversizedMargins.has_value(), "oversized margins degrade to a contained viewport");
    FullyContained(*oversizedMargins, {0, 0, 320, 180});
    Check(!ComputeOverlayPlacement({0, 0, 0, 720}, 96, 1180, 700), "zero width fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 0, 1180, 700), "zero DPI fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 96, -1, 700), "negative size fails closed");

    for (const auto client : {
             PhysicalRect{0, 0, 1, 1},
             PhysicalRect{0, 0, 853, 479},
             PhysicalRect{0, 0, 3440, 1440},
             PhysicalRect{0, 0, 7680, 4320},
         }) {
        const int clientWidth = client.right - client.left;
        const int clientHeight = client.bottom - client.top;
        for (const unsigned int dpi : {72U, 96U, 120U, 144U, 192U, 288U, 480U}) {
            for (const float interfaceScale : {0.8F, 1.0F, 1.125F, 1.25F}) {
                const auto metrics = ComputeOverlayRenderMetrics(
                    clientWidth, clientHeight, dpi, interfaceScale);
                Check(metrics.has_value(), "arbitrary client DPI and zoom produce render metrics");
                Check(metrics->viewportWidthDip > 0 && metrics->viewportHeightDip > 0,
                      "responsive viewport remains positive");
                const float reconstructedWidth = metrics->viewportWidthDip *
                    metrics->physicalPixelsPerDip;
                const float reconstructedHeight = metrics->viewportHeightDip *
                    metrics->physicalPixelsPerDip;
                Check(std::abs(reconstructedWidth - clientWidth) < 0.01F,
                      "responsive viewport reconstructs physical width");
                Check(std::abs(reconstructedHeight - clientHeight) < 0.01F,
                      "responsive viewport reconstructs physical height");
            }
        }
    }
    Check(!ComputeOverlayRenderMetrics(0, 720, 96, 1), "zero client width fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 0, 1), "zero render DPI fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 96, 0), "zero UI scale fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 96,
          std::numeric_limits<float>::quiet_NaN()), "non-finite UI scale fails closed");

    for (const auto viewport : {
             PhysicalRect{0, 0, 1, 1},
             PhysicalRect{0, 0, 240, 135},
             PhysicalRect{0, 0, 280, 800},
             PhysicalRect{0, 0, 800, 280},
             PhysicalRect{0, 0, 853, 479},
             PhysicalRect{0, 0, 1180, 700},
             PhysicalRect{0, 0, 3840, 2160},
         }) {
        const float width = static_cast<float>(viewport.right);
        const float height = static_cast<float>(viewport.bottom);
        const auto geometry = ComputeOverlaySurfaceGeometry(width, height, 880.0F);
        Check(geometry.has_value(), "tiny portrait and ultrawide viewports produce geometry");
        SurfaceContained(*geometry, width, height);
    }

    // Exercise the shell math as one pipeline, matching the host call order:
    // physical work area -> scaled HWND placement -> logical render viewport ->
    // widget panel/tray/footer geometry. This includes legacy, handheld-sized,
    // portrait, ultrawide, 4K/5K/8K, offset, and negative-coordinate monitors.
    constexpr std::array physicalWorkAreas{
        PhysicalRect{0, 0, 320, 200},
        PhysicalRect{0, 0, 640, 360},
        PhysicalRect{0, 0, 800, 600},
        PhysicalRect{0, 0, 1024, 728},
        PhysicalRect{0, 0, 1280, 680},
        PhysicalRect{0, 0, 1366, 728},
        PhysicalRect{0, 0, 1920, 1040},
        PhysicalRect{0, 0, 2560, 1040},
        PhysicalRect{0, 0, 3440, 1400},
        PhysicalRect{0, 0, 3840, 1040},
        PhysicalRect{0, 0, 3840, 2120},
        PhysicalRect{0, 0, 5120, 1400},
        PhysicalRect{0, 0, 7680, 2120},
        PhysicalRect{0, 0, 7680, 4280},
        PhysicalRect{0, 0, 720, 1240},
        PhysicalRect{0, 0, 1080, 1880},
        PhysicalRect{0, 0, 1440, 3400},
        PhysicalRect{-5120, -200, -1680, 1240},
        PhysicalRect{1920, 80, 7040, 1480},
    };
    for (const auto work : physicalWorkAreas) {
        for (const unsigned int dpi : {72U, 96U, 120U, 144U, 168U, 192U, 240U, 288U, 384U, 480U}) {
            for (const float interfaceScale : {0.8F, 0.9F, 1.0F, 1.1F, 1.25F}) {
                for (const float desiredHeight : {180.0F, 540.0F, 700.0F}) {
                    const auto placement = ComputeOverlayPlacement(
                        work, dpi, 1180.0F * interfaceScale,
                        desiredHeight * interfaceScale);
                    Check(placement.has_value(),
                          "resolution matrix produces a physical placement");
                    FullyContained(*placement, work);
                    const auto metrics = ComputeOverlayRenderMetrics(
                        placement->width, placement->height, dpi, interfaceScale);
                    Check(metrics.has_value(),
                          "placed client produces responsive render metrics");
                    Check(std::abs(metrics->viewportWidthDip *
                                       metrics->physicalPixelsPerDip - placement->width) < 0.02F &&
                          std::abs(metrics->viewportHeightDip *
                                       metrics->physicalPixelsPerDip - placement->height) < 0.02F,
                          "DPI-derived logical viewport reconstructs placed client");
                    if (desiredHeight == 700.0F) {
                        const auto geometry = ComputeOverlaySurfaceGeometry(
                            metrics->viewportWidthDip, metrics->viewportHeightDip, 880.0F);
                        Check(geometry.has_value(),
                              "resolution matrix produces widget surface geometry");
                        SurfaceContained(
                            *geometry, metrics->viewportWidthDip,
                            metrics->viewportHeightDip);
                    }
                }
            }
        }
    }

    // Dense logical boundary coverage catches discontinuities around tile,
    // footer, tray, margin, and preferred-panel thresholds without encoding a
    // small list of familiar desktop resolutions.
    constexpr std::array logicalWidths{
        1.0F, 2.0F, 7.0F, 15.0F, 31.0F, 63.0F, 64.0F, 65.0F,
        127.0F, 239.0F, 240.0F, 319.0F, 320.0F, 479.0F, 640.0F,
        853.0F, 1180.0F, 1920.0F, 3440.0F, 7680.0F,
    };
    constexpr std::array logicalHeights{
        1.0F, 2.0F, 7.0F, 13.0F, 14.0F, 15.0F, 31.0F, 63.0F,
        111.0F, 112.0F, 113.0F, 179.0F, 180.0F, 239.0F, 240.0F,
        479.0F, 700.0F, 1080.0F, 2160.0F, 4320.0F,
    };
    for (const float preferredPanelWidth : {240.0F, 720.0F, 880.0F, 1600.0F}) {
        for (const float width : logicalWidths) {
            float priorPanelHeight = -1.0F;
            for (const float height : logicalHeights) {
                const auto geometry = ComputeOverlaySurfaceGeometry(
                    width, height, preferredPanelWidth);
                Check(geometry.has_value(),
                      "logical boundary matrix produces surface geometry");
                SurfaceContained(*geometry, width, height);
                Check(geometry->panelWidth <= preferredPanelWidth + 0.002F,
                      "responsive panel never exceeds author preference");
                Check(geometry->panelHeight + 0.002F >= priorPanelHeight,
                      "panel height grows monotonically with available height");
                priorPanelHeight = geometry->panelHeight;
            }
        }
    }

    const auto standardSurface = ComputeOverlaySurfaceGeometry(1180, 700, 880);
    Check(standardSurface.has_value(), "standard widget surface produces geometry");
    Check(std::abs(standardSurface->trayY - 588.0F) < 0.001F &&
          std::abs(standardSurface->trayHeight - 98.0F) < 0.001F,
          "standard widget surface preserves the persistent tray band");
    Check(standardSurface->panelY + standardSurface->panelHeight <
              standardSurface->trayY,
          "standard widget panel floats above the persistent tray");
    Check(std::abs(standardSurface->footerHeight - 55.0F) < 0.001F &&
          std::abs(standardSurface->footerY -
                   (standardSurface->panelY + standardSurface->panelHeight - 55.0F)) < 0.001F,
          "standard widget footer preserves established chrome height");
    Check(!ComputeOverlaySurfaceGeometry(0, 700, 880),
          "zero surface width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(1180, 700, -1),
          "negative preferred panel width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(
              std::numeric_limits<float>::infinity(), 700, 880),
          "non-finite surface width fails closed");

    std::cout << "OverlayPlacementTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
