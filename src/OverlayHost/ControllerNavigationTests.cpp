#include "ControllerNavigation.h"

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
    using gba::input::NavigationDirection;
    gba::input::StickNavigator navigator;
    navigator.Prime(0, 0, 0);
    Check(!navigator.Update(8'000, 7'000, 10), "dead zone is quiet");
    Check(navigator.Update(0, -20'000, 20) == NavigationDirection::Down,
          "negative Y navigates down");
    Check(!navigator.Update(4'000, -18'000, 100), "held direction does not chatter");
    Check(navigator.Update(20'000, -10'000, 110) == NavigationDirection::Right,
          "clear dominant-axis gesture changes direction");
    Check(!navigator.Update(18'000, -14'500, 200), "diagonal noise keeps engaged axis");
    Check(navigator.Update(18'000, -14'500, 470) == NavigationDirection::Right,
          "held direction repeats after initial delay");
    Check(navigator.Update(18'000, -14'500, 595) == NavigationDirection::Right,
          "repeat cadence is stable");
    Check(!navigator.Update(0, 0, 600), "release is quiet");
    Check(navigator.Update(0, 20'000, 610) == NavigationDirection::Up,
          "positive Y navigates up after release");
    navigator.Reset();
    navigator.Prime(-20'000, 0, 700);
    Check(!navigator.Update(-20'000, 0, 710), "priming prevents an opening ghost move");

    std::cout << "ControllerNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
