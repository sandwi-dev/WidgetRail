#include "OverlayPlacement.h"

#include <cstdlib>
#include <cmath>
#include <iostream>
#include <limits>

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
            for (const float interfaceScale : {0.85F, 1.0F, 1.125F, 1.5F}) {
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
        Check(geometry->panelX >= 0 && geometry->panelY >= 0,
              "panel origin remains in viewport");
        Check(geometry->panelWidth >= 0 && geometry->panelHeight >= 0,
              "panel dimensions never invert");
        Check(geometry->panelX + geometry->panelWidth <= width + 0.001F &&
              geometry->panelY + geometry->panelHeight <= height + 0.001F,
              "panel remains fully contained");
        Check(geometry->trayY >= 0 && geometry->trayHeight >= 0 &&
              geometry->trayY + geometry->trayHeight <= height + 0.001F,
              "persistent tray band remains fully contained");
        Check(geometry->panelY + geometry->panelHeight <=
                  geometry->trayY + 0.001F,
              "floating widget panel never overlaps persistent tray band");
        Check(geometry->widgetViewportX >= geometry->panelX &&
              geometry->widgetViewportY >= geometry->panelY,
              "widget viewport begins inside panel");
        Check(geometry->widgetViewportX + geometry->widgetViewportWidth <=
                  geometry->panelX + geometry->panelWidth + 0.001F &&
              geometry->widgetViewportY + geometry->widgetViewportHeight <=
                  geometry->panelY + geometry->panelHeight + 0.001F,
              "widget viewport remains contained by panel");
    }

    const auto standardSurface = ComputeOverlaySurfaceGeometry(1180, 700, 880);
    Check(standardSurface.has_value(), "standard widget surface produces geometry");
    Check(std::abs(standardSurface->trayY - 588.0F) < 0.001F &&
          std::abs(standardSurface->trayHeight - 98.0F) < 0.001F,
          "standard widget surface preserves the persistent tray band");
    Check(standardSurface->panelY + standardSurface->panelHeight <
              standardSurface->trayY,
          "standard widget panel floats above the persistent tray");
    Check(!ComputeOverlaySurfaceGeometry(0, 700, 880),
          "zero surface width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(1180, 700, -1),
          "negative preferred panel width fails closed");

    std::cout << "OverlayPlacementTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
