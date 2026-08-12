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

    Check(gba::PlanRenderTargetResize(true, false, 952, 698) ==
              gba::RenderTargetResizePlan{true, true},
          "visible extent changes resize the existing HWND target in place");
    Check(gba::PlanRenderTargetResize(false, false, 952, 698) ==
              gba::RenderTargetResizePlan{false, true},
          "first valid size invalidates for lazy target creation");
    Check(gba::PlanRenderTargetResize(true, true, 952, 698) ==
              gba::RenderTargetResizePlan{},
          "minimization never resizes or invalidates presentation resources");
    Check(gba::PlanRenderTargetResize(true, false, 0, 698) ==
              gba::RenderTargetResizePlan{} &&
              gba::PlanRenderTargetResize(true, false, 952, 0) ==
              gba::RenderTargetResizePlan{},
          "zero-area size messages cannot disturb the retained target");

    Check(gba::PlanCompositionGeometry(0, 0, 952, 698) ==
              gba::CompositionGeometryPlan{952, 698, false, true},
          "first composed frame is committed before its HWND is shown");
    Check(gba::PlanCompositionGeometry(540, 620, 980, 700) ==
              gba::CompositionGeometryPlan{540, 620, false, true},
          "growth retains the old clip until the larger surface commits");
    Check(gba::PlanCompositionGeometry(980, 700, 540, 620) ==
              gba::CompositionGeometryPlan{540, 620, true, true},
          "shrink clips the old surface before committing the smaller surface");
    Check(gba::PlanCompositionGeometry(980, 620, 540, 700) ==
              gba::CompositionGeometryPlan{540, 620, true, true},
          "mixed geometry clips only shrinking dimensions before commit");
    Check(gba::PlanCompositionGeometry(540, 620, 540, 620) ==
              gba::CompositionGeometryPlan{540, 620, false, false},
          "same-size repaint does not schedule HWND geometry work");

    constexpr auto growStart = gba::PlanCompositionMotion(
        540, 620, 980, 700, 540.0F, 620.0F);
    Check(growStart.containerWidth == 980 && growStart.containerHeight == 700 &&
              growStart.scaleX > 0.55F && growStart.scaleX < 0.56F &&
              growStart.offsetX == 220.0F && growStart.offsetY == 40.0F &&
              !growStart.retainsTransparentContainer,
          "growth starts centered in the final transparent client without a later resize");
    constexpr auto shrinkStart = gba::PlanCompositionMotion(
        980, 700, 540, 620, 980.0F, 700.0F);
    Check(shrinkStart.containerWidth == 980 && shrinkStart.containerHeight == 700 &&
              shrinkStart.scaleX > 1.81F && shrinkStart.scaleX < 1.82F &&
              shrinkStart.offsetX == 0.0F && shrinkStart.offsetY == 0.0F &&
              shrinkStart.retainsTransparentContainer,
          "shrink retains source-sized transparent container without per-tick HWND work");
    constexpr auto shrinkEnd = gba::PlanCompositionMotion(
        980, 700, 540, 620, 540.0F, 620.0F);
    Check(shrinkEnd.scaleX == 1.0F && shrinkEnd.scaleY == 1.0F &&
              shrinkEnd.offsetX == 220.0F && shrinkEnd.offsetY == 40.0F,
          "destination reaches identity scale while remaining centered in the union client");
    constexpr auto fixedContainer = gba::PlanCompositionMotion(
        1180, 700, 540, 620, 540.0F, 620.0F);
    Check(fixedContainer.containerWidth == 1180 &&
              fixedContainer.containerHeight == 700 &&
              fixedContainer.offsetX == 320.0F &&
              fixedContainer.offsetY == 40.0F &&
              fixedContainer.retainsTransparentContainer,
          "fixed shell container centers settled content and retains transparent unused client");
    Check(gba::PlanCompositionMotion(0, 700, 540, 620, 540.0F, 620.0F) ==
              gba::CompositionMotionPlan{},
          "invalid source geometry cannot start compositor motion");

    Check(gba::ResolveWidgetExtentAuthority(true, true) ==
              gba::WidgetExtentAuthority::AdmittedSnapshot,
          "admitted snapshot owns extent even when a prior surface exists");
    Check(gba::ResolveWidgetExtentAuthority(true, false) ==
              gba::WidgetExtentAuthority::AdmittedSnapshot,
          "admitted snapshot does not require retained geometry");
    Check(gba::ResolveWidgetExtentAuthority(false, true) ==
              gba::WidgetExtentAuthority::RetainedCommittedSurface,
          "worker-start copy retains the previously committed widget extent");
    Check(gba::ResolveWidgetExtentAuthority(false, false) ==
              gba::WidgetExtentAuthority::CompactStartupFallback,
          "compact fallback is reserved for the first widget open");

    Check(gba::ResolveWidgetContentAuthority(true, true) ==
              gba::WidgetContentAuthority::AdmittedSnapshot,
          "an admitted destination snapshot owns visible widget content");
    Check(gba::ResolveWidgetContentAuthority(true, false) ==
              gba::WidgetContentAuthority::AdmittedSnapshot,
          "first-open content uses its admitted snapshot directly");
    Check(gba::ResolveWidgetContentAuthority(false, true) ==
              gba::WidgetContentAuthority::RetainedCommittedSnapshot,
          "worker startup keeps one previously admitted snapshot painted");
    Check(gba::ResolveWidgetContentAuthority(false, false) ==
              gba::WidgetContentAuthority::StableStartupStatus,
          "startup status is reserved for an open with no committed content");

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

    gba::DisplayRefreshAccumulator displayRefresh;
    Check(!displayRefresh.Enqueue(false, DisplayEnvironmentChange::Dpi),
          "hidden display changes do not schedule work");
    Check(displayRefresh.Take() == DisplayRefreshPlan{},
          "hidden display changes leave no latent work");
    Check(displayRefresh.Enqueue(true, DisplayEnvironmentChange::Dpi),
          "first visible display change schedules one posted refresh");
    Check(!displayRefresh.Enqueue(true, DisplayEnvironmentChange::Topology),
          "topology burst merges into the scheduled refresh");
    Check(!displayRefresh.Enqueue(true, DisplayEnvironmentChange::SystemSettings),
          "work-area burst merges without posting another refresh");
    Check(displayRefresh.Take() == DisplayRefreshPlan{true, true, true},
          "coalesced burst retains its strongest appearance and placement work");
    Check(displayRefresh.Enqueue(true, DisplayEnvironmentChange::Topology),
          "consuming a burst permits a later display transition");
    Check(displayRefresh.Take() == DisplayRefreshPlan{true, false, true},
          "later topology transition remains independent");

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
