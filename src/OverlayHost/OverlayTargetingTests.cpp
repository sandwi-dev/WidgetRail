#include "OverlayTargeting.h"

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
    gba::ForegroundTargetTracker tracker;
    tracker.SetOwnedWindows(100, 101);

    Check(tracker.Resolve(100, false) == 100, "host is initial fallback");
    Check(!tracker.Observe(0, false), "invalid foreground is ignored");
    Check(!tracker.Observe(100, true), "overlay cannot target itself");
    Check(!tracker.Observe(101, true), "backdrop cannot become target");
    Check(tracker.Observe(200, true), "opening captures external game window");
    Check(tracker.Resolve(100, true) == 200, "valid remembered game remains target");
    Check(!tracker.Observe(200, true), "duplicate foreground event avoids placement churn");
    Check(tracker.Observe(300, true), "alt-tab retargets to the new external window");
    Check(tracker.Resolve(100, true) == 300, "new foreground drives monitor selection");
    Check(tracker.Resolve(100, false) == 100, "destroyed target safely falls back to host");
    Check(tracker.remembered() == 300, "temporary invalidity does not invent a target");

    tracker.SetOwnedWindows(300, 301);
    Check(tracker.remembered() == 0, "recreated owned HWND cannot remain external target");

    gba::PlacementRefreshGate gate;
    Check(gate.TryEnter(), "first placement enters gate");
    Check(!gate.TryEnter(), "synchronous DPI placement is coalesced instead of recursing");
    Check(!gate.TryEnter(), "multiple nested display messages remain one pending refresh");
    Check(gate.Complete(), "gate requests one deferred refresh after nested message");
    Check(gate.TryEnter(), "deferred placement enters after original completes");
    Check(!gate.Complete(), "settled placement does not schedule another refresh");

    std::cout << "OverlayTargetingTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
