#include "OverlayPlacement.h"

#include <cstdlib>
#include <iostream>

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

    const auto small = ComputeOverlayPlacement({0, 0, 640, 360}, 96, 1180, 700);
    Check(small.has_value(), "small monitor is supported");
    FullyContained(*small, {0, 0, 640, 360});
    Check(!ComputeOverlayPlacement({0, 0, 0, 720}, 96, 1180, 700), "zero width fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 0, 1180, 700), "zero DPI fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 96, -1, 700), "negative size fails closed");

    std::cout << "OverlayPlacementTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
