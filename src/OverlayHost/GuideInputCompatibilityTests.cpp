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
    widgetrail::input::GuideEdgeTracker tracker;
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

    widgetrail::input::GuideCompatibilityActivation activation;
    widgetrail::input::GuideCompatibilityActivation::DeviceId first{};
    widgetrail::input::GuideCompatibilityActivation::DeviceId second{};
    first[0] = 1;
    second[0] = 2;
    Check(!activation.active() && activation.deviceCount() == 0,
          "compatibility polling starts dormant");
    Check(activation.Update(first, true) && activation.active(),
          "first legacy connection activates compatibility polling");
    Check(!activation.Update(first, true) && activation.deviceCount() == 1,
          "duplicate connection notification is idempotent");
    Check(!activation.Update(second, true) && activation.deviceCount() == 2,
          "additional legacy devices retain one active timer");
    Check(!activation.Update(first, false) && activation.active(),
          "disconnecting one of two devices retains polling");
    Check(activation.Update(second, false) && !activation.active(),
          "last legacy disconnect disables polling");
    Check(!activation.Update(first, false),
          "duplicate disconnect notification is idempotent");
    Check(activation.Update(first, true),
          "compatibility polling can reactivate after reconnect");
    activation.Reset();
    Check(!activation.active() && activation.deviceCount() == 0,
          "reset clears tracked devices");

    std::cout << "GuideInputCompatibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
