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
    using gba::DisplayEnvironmentChange;
    using gba::DisplayRefreshPlan;
    using gba::OverlayPresentationDirective;
    constexpr gba::OverlayPresentationExtent dashboard{1180, 180};
    constexpr gba::OverlayPresentationExtent compactWidget{540, 620};
    constexpr gba::OverlayPresentationExtent tallWidget{540, 700};
    constexpr gba::OverlayPresentationExtent wideWidget{980, 700};

    Check(gba::DecideOverlayPresentation(false, false, {}, {}) ==
              OverlayPresentationDirective::None,
          "an already hidden overlay needs no presentation work");
    Check(gba::DecideOverlayPresentation(true, false, dashboard, {}) ==
              OverlayPresentationDirective::Hide,
          "visible to hidden requests one hide");
    Check(gba::DecideOverlayPresentation(false, true, {}, dashboard) ==
              OverlayPresentationDirective::Place,
          "opening resolves monitor and places the overlay");
    Check(gba::DecideOverlayPresentation(true, true, dashboard, dashboard) ==
              OverlayPresentationDirective::Repaint,
          "dashboard selection is repaint-only");
    Check(gba::DecideOverlayPresentation(true, true, compactWidget, compactWidget) ==
              OverlayPresentationDirective::Repaint,
          "same-extent snapshot refresh is repaint-only");
    Check(gba::DecideOverlayPresentation(true, true, compactWidget, tallWidget) ==
              OverlayPresentationDirective::Place,
          "handled action height changes request placement");
    Check(gba::DecideOverlayPresentation(true, true, tallWidget, wideWidget) ==
              OverlayPresentationDirective::Place,
          "handled action width changes request placement");
    Check(gba::DecideOverlayPresentation(true, true, wideWidget, dashboard) ==
              OverlayPresentationDirective::Place,
          "catalog reconciliation returning to dashboard requests placement");
    Check(gba::DecideOverlayPresentation(true, true, dashboard, dashboard, true) ==
              OverlayPresentationDirective::Place,
          "monitor target change requests placement even at the same extent");
    Check(gba::ShouldCommitVisiblePlacementSynchronously(
              true, OverlayPresentationDirective::Place),
          "visible resize or retarget commits a complete frame synchronously");
    Check(!gba::ShouldCommitVisiblePlacementSynchronously(
              false, OverlayPresentationDirective::Place),
          "initial show does not add a redundant synchronous frame");
    Check(!gba::ShouldCommitVisiblePlacementSynchronously(
              true, OverlayPresentationDirective::Repaint),
          "same-extent widget switch remains repaint-only");

    Check(gba::DecideDisplayRefresh(false, DisplayEnvironmentChange::Dpi) ==
              DisplayRefreshPlan{},
          "hidden DPI changes defer work until the next authoritative show");
    Check(gba::DecideDisplayRefresh(true, DisplayEnvironmentChange::Dpi) ==
              DisplayRefreshPlan{true, false, true},
          "visible DPI changes recreate resources and reposition both windows");
    Check(gba::DecideDisplayRefresh(true, DisplayEnvironmentChange::Topology) ==
              DisplayRefreshPlan{true, false, true},
          "topology changes re-resolve monitor bounds without appearance churn");
    Check(gba::DecideDisplayRefresh(true, DisplayEnvironmentChange::SystemSettings) ==
              DisplayRefreshPlan{true, true, true},
          "work-area settings reapply appearance and reposition both windows");

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
