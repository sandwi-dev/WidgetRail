#include "GuideInputCompatibility.h"

#include <array>
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

} // namespace

int main() {
    gba::input::GuideEdgeTracker tracker;
    std::array<bool, XUSER_MAX_COUNT> state{};
    Check(tracker.Update(state) == 0, "initial neutral state is only a baseline");
    state[0] = true;
    Check(tracker.Update(state) == 0x01, "slot zero rising edge is reported");
    Check(tracker.Update(state) == 0, "held Guide does not repeat");
    state[0] = false;
    Check(tracker.Update(state) == 0, "release does not toggle");
    state[1] = true;
    state[3] = true;
    Check(tracker.Update(state) == 0x0A, "simultaneous slots retain identity");
    state = {};
    Check(tracker.Update(state) == 0, "multi-slot release is quiet");

    std::cout << "GuideInputCompatibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
