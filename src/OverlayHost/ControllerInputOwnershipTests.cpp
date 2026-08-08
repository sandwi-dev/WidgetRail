#include "ControllerInputOwnership.h"

#include <cstdlib>
#include <iostream>

namespace {

int checks = 0;

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(1);
    }
}

} // namespace

int main() {
    using namespace gba::input;

    Check(DecideControllerInputOwnership(false, false, true) ==
              ControllerInputOwnershipDecision{},
          "hidden overlay never polls ordinary controller input");

    const auto exclusive = DecideControllerInputOwnership(true, true, true);
    Check(exclusive.readPath == ControllerReadPath::GameInputForegroundExclusive &&
              exclusive.ordinaryInputExclusive && !exclusive.shouldAcquireForeground,
          "focused visible overlay uses foreground-exclusive GameInput");

    const auto lostFocus = DecideControllerInputOwnership(true, false, true);
    Check(lostFocus.readPath == ControllerReadPath::None &&
              !lostFocus.ordinaryInputExclusive && lostFocus.shouldAcquireForeground,
          "GameInput path fails closed until foreground ownership is confirmed");

    const auto compatibility = DecideControllerInputOwnership(true, true, false);
    Check(compatibility.readPath == ControllerReadPath::XInputCompatibility &&
              !compatibility.ordinaryInputExclusive,
          "XInput fallback is explicitly non-exclusive");

    Check(PlanForegroundAcquisition(true, 10, 20) == ForegroundAcquisitionPlan{},
          "existing foreground ownership does not manipulate input queues");
    Check(PlanForegroundAcquisition(false, 10, 20) ==
              ForegroundAcquisitionPlan{true, true},
          "different valid threads permit one bounded attachment fallback");
    Check(PlanForegroundAcquisition(false, 10, 10) ==
              ForegroundAcquisitionPlan{true, false},
          "same-thread foreground acquisition never self-attaches");
    Check(PlanForegroundAcquisition(false, 10, 0) ==
              ForegroundAcquisitionPlan{true, false},
          "missing foreground thread only receives the direct attempt");

    Check(DecideVisibleForegroundTransition(true, true, false) ==
              VisibleForegroundTransition::CloseOverlay,
          "valid external foreground closes a visible overlay");
    Check(DecideVisibleForegroundTransition(true, true, true) ==
              VisibleForegroundTransition::Ignore,
          "our own overlay or backdrop activation is ignored");
    Check(DecideVisibleForegroundTransition(true, false, false) ==
              VisibleForegroundTransition::Ignore,
          "transient missing foreground evidence does not close");
    Check(DecideVisibleForegroundTransition(false, true, false) ==
              VisibleForegroundTransition::Ignore,
          "hidden overlay ignores foreground changes");

    Check(ResolveBasicKeyboardAction(0x25) == BasicKeyboardAction::NavigateLeft &&
              ResolveBasicKeyboardAction(0x27) == BasicKeyboardAction::NavigateRight &&
              ResolveBasicKeyboardAction(0x26) == BasicKeyboardAction::NavigateUp &&
              ResolveBasicKeyboardAction(0x28) == BasicKeyboardAction::NavigateDown,
          "arrow keys resolve to navigation only");
    Check(ResolveBasicKeyboardAction(0x0D) == BasicKeyboardAction::Activate,
          "Enter resolves to A/select");
    Check(ResolveBasicKeyboardAction(0x1B) == BasicKeyboardAction::Back,
          "Escape resolves to B/back");
    Check(ResolveBasicKeyboardAction('A') == BasicKeyboardAction::None &&
              ResolveBasicKeyboardAction('B') == BasicKeyboardAction::None &&
              ResolveBasicKeyboardAction('X') == BasicKeyboardAction::None &&
              ResolveBasicKeyboardAction('Y') == BasicKeyboardAction::None,
          "letter keys do not become hidden controller shortcuts");

    std::cout << "ControllerInputOwnershipTests passed (" << checks << " checks)\n";
    return 0;
}
