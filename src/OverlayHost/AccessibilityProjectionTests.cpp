#include "AccessibilityProjection.h"

#include <cstdlib>
#include <iostream>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

gba::accessibility::ProjectionKey Key() {
    return {
        L"music", L"generation-1", L"root", L"play", 9, 0, 3,
        20, 30, 600, 400, 720, 540, 1.5F, 1.0F, 400, false, false,
    };
}

} // namespace

int main() {
    gba::accessibility::ProjectionTracker tracker;
    auto key = Key();
    Check(tracker.ShouldCollect(key), "first UIA frame collects");
    tracker.Published(key);
    Check(!tracker.ShouldCollect(key), "stable paints retain the immutable projection");
    Check(!tracker.ObserveFrame(true) && !tracker.ShouldCollect(key),
          "paint-only animation frames do not collect");
    Check(!tracker.ObserveFrame(true) && !tracker.ShouldCollect(key),
          "repeated animation frames remain allocation-dormant");
    Check(tracker.ObserveFrame(false) && tracker.ShouldCollect(key),
          "animation completion requests one final-geometry collection");
    tracker.Published(key);
    Check(!tracker.ObserveFrame(false) && !tracker.ShouldCollect(key),
          "stable final frames do not request another collection");
    auto focusChanged = key;
    focusChanged.focusedElementId = L"next";
    Check(tracker.ShouldCollect(focusChanged), "focus changes collect immediately");
    tracker.Published(focusChanged);
    auto resized = focusChanged;
    resized.viewportWidth = 480;
    Check(tracker.ShouldCollect(resized), "viewport changes collect immediately");
    tracker.Published(resized);
    auto sliderChanged = resized;
    ++sliderChanged.sliderPresentationRevision;
    Check(tracker.ShouldCollect(sliderChanged),
          "optimistic slider presentation changes collect immediately");
    tracker.Clear();
    Check(tracker.ShouldCollect(focusChanged), "surface clear retires published authority");

    std::cout << "AccessibilityProjectionTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
