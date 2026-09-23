#include "OverlayTargeting.h"

#include <cmath>
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

void CheckNear(const float actual, const float expected, const char* message) {
    Check(std::abs(actual - expected) <= 0.01F, message);
}

} // namespace

int main() {
    using widgetrail::DisplayEnvironmentChange;
    using widgetrail::DisplayRefreshPlan;
    using widgetrail::OverlayPresentationDirective;
    constexpr widgetrail::OverlayPresentationExtent dashboard{1180, 180};
    constexpr widgetrail::OverlayPresentationExtent compactWidget{540, 620};
    constexpr widgetrail::OverlayPresentationExtent tallWidget{540, 700};
    constexpr widgetrail::OverlayPresentationExtent wideWidget{980, 700};

    Check(widgetrail::DecideOverlayPresentation(false, false, {}, {}) ==
              OverlayPresentationDirective::None,
          "an already hidden overlay needs no presentation work");
    Check(widgetrail::DecideOverlayPresentation(true, false, dashboard, {}) ==
              OverlayPresentationDirective::Hide,
          "visible to hidden requests one hide");
    Check(widgetrail::DecideOverlayPresentation(false, true, {}, dashboard) ==
              OverlayPresentationDirective::Place,
          "opening resolves monitor and places the overlay");
    Check(widgetrail::DecideOverlayPresentation(true, true, dashboard, dashboard) ==
              OverlayPresentationDirective::Repaint,
          "dashboard selection is repaint-only");
    Check(widgetrail::DecideOverlayPresentation(true, true, compactWidget, compactWidget) ==
              OverlayPresentationDirective::Repaint,
          "same-extent snapshot refresh is repaint-only");
    Check(widgetrail::DecideOverlayPresentation(true, true, compactWidget, tallWidget) ==
              OverlayPresentationDirective::Place,
          "handled action height changes request placement");
    Check(widgetrail::DecideOverlayPresentation(true, true, tallWidget, wideWidget) ==
              OverlayPresentationDirective::Place,
          "handled action width changes request placement");
    Check(widgetrail::DecideOverlayPresentation(true, true, wideWidget, dashboard) ==
              OverlayPresentationDirective::Place,
          "catalog reconciliation returning to dashboard requests placement");
    Check(widgetrail::DecideOverlayPresentation(true, true, dashboard, dashboard, true) ==
              OverlayPresentationDirective::Place,
          "monitor target change requests placement even at the same extent");
    Check(widgetrail::ShouldCommitVisiblePlacementSynchronously(
              true, OverlayPresentationDirective::Place),
          "visible resize or retarget commits a complete frame synchronously");
    Check(!widgetrail::ShouldCommitVisiblePlacementSynchronously(
              false, OverlayPresentationDirective::Place),
          "initial show does not add a redundant synchronous frame");
    Check(!widgetrail::ShouldCommitVisiblePlacementSynchronously(
              true, OverlayPresentationDirective::Repaint),
          "same-extent widget switch remains repaint-only");

    Check(widgetrail::PlanRenderTargetResize(true, false, 952, 698) ==
              widgetrail::RenderTargetResizePlan{true, true},
          "visible extent changes resize the existing HWND target in place");
    Check(widgetrail::PlanRenderTargetResize(false, false, 952, 698) ==
              widgetrail::RenderTargetResizePlan{false, true},
          "first valid size invalidates for lazy target creation");
    Check(widgetrail::PlanRenderTargetResize(true, true, 952, 698) ==
              widgetrail::RenderTargetResizePlan{},
          "minimization never resizes or invalidates presentation resources");
    Check(widgetrail::PlanRenderTargetResize(true, false, 0, 698) ==
              widgetrail::RenderTargetResizePlan{} &&
              widgetrail::PlanRenderTargetResize(true, false, 952, 0) ==
              widgetrail::RenderTargetResizePlan{},
          "zero-area size messages cannot disturb the retained target");

    Check(widgetrail::PlanCompositionGeometry(0, 0, 952, 698) ==
              widgetrail::CompositionGeometryPlan{952, 698, false, true},
          "first composed frame is committed before its HWND is shown");
    Check(widgetrail::PlanCompositionGeometry(540, 620, 980, 700) ==
              widgetrail::CompositionGeometryPlan{540, 620, false, true},
          "growth retains the old clip until the larger surface commits");
    Check(widgetrail::PlanCompositionGeometry(980, 700, 540, 620) ==
              widgetrail::CompositionGeometryPlan{540, 620, true, true},
          "shrink clips the old surface before committing the smaller surface");
    Check(widgetrail::PlanCompositionGeometry(980, 620, 540, 700) ==
              widgetrail::CompositionGeometryPlan{540, 620, true, true},
          "mixed geometry clips only shrinking dimensions before commit");
    Check(widgetrail::PlanCompositionGeometry(540, 620, 540, 620) ==
              widgetrail::CompositionGeometryPlan{540, 620, false, false},
          "same-size repaint does not schedule HWND geometry work");

    constexpr auto growStart = widgetrail::PlanCompositionMotion(
        540, 620, 980, 700, 540.0F, 620.0F);
    Check(growStart.containerWidth == 980 && growStart.containerHeight == 700 &&
              growStart.scaleX > 0.55F && growStart.scaleX < 0.56F &&
              growStart.offsetX == 220.0F && growStart.offsetY == 40.0F &&
              !growStart.retainsTransparentContainer,
          "growth starts centered in the final transparent client without a later resize");
    constexpr auto shrinkStart = widgetrail::PlanCompositionMotion(
        980, 700, 540, 620, 980.0F, 700.0F);
    Check(shrinkStart.containerWidth == 980 && shrinkStart.containerHeight == 700 &&
              shrinkStart.scaleX > 1.81F && shrinkStart.scaleX < 1.82F &&
              shrinkStart.offsetX == 0.0F && shrinkStart.offsetY == 0.0F &&
              shrinkStart.retainsTransparentContainer,
          "shrink retains source-sized transparent container without per-tick HWND work");
    constexpr auto shrinkEnd = widgetrail::PlanCompositionMotion(
        980, 700, 540, 620, 540.0F, 620.0F);
    Check(shrinkEnd.scaleX == 1.0F && shrinkEnd.scaleY == 1.0F &&
              shrinkEnd.offsetX == 220.0F && shrinkEnd.offsetY == 40.0F,
          "destination reaches identity scale while remaining centered in the union client");
    constexpr auto fixedContainer = widgetrail::PlanCompositionMotion(
        1180, 700, 540, 620, 540.0F, 620.0F);
    Check(fixedContainer.containerWidth == 1180 &&
              fixedContainer.containerHeight == 700 &&
              fixedContainer.offsetX == 320.0F &&
              fixedContainer.offsetY == 40.0F &&
              fixedContainer.retainsTransparentContainer,
          "content-only union centers the panel without adding chrome reservation");
    constexpr auto panelMotionStart = widgetrail::PlanCompositionMotion(
        760, 645, 760, 385, 560.0F, 645.0F,
        widgetrail::CompositionVerticalAnchor::Bottom);
    Check(panelMotionStart.containerWidth == 760 &&
              panelMotionStart.containerHeight == 645 &&
              panelMotionStart.scaleX > 0.73F &&
              panelMotionStart.scaleX < 0.74F &&
              panelMotionStart.scaleY > 1.67F &&
              panelMotionStart.scaleY < 1.68F &&
              panelMotionStart.offsetY == 0.0F,
          "panel-local destination starts from the retained panel envelope");
    constexpr auto panelMotionEnd = widgetrail::PlanCompositionMotion(
        760, 645, 760, 385, 760.0F, 385.0F,
        widgetrail::CompositionVerticalAnchor::Bottom);
    Check(panelMotionEnd.scaleX == 1.0F && panelMotionEnd.scaleY == 1.0F &&
              panelMotionEnd.offsetX == 0.0F && panelMotionEnd.offsetY == 260.0F,
          "panel-local destination settles at the same visible bottom edge");
    Check(widgetrail::PlanCompositionMotion(0, 700, 540, 620, 540.0F, 620.0F) ==
              widgetrail::CompositionMotionPlan{},
          "invalid source geometry cannot start compositor motion");

    constexpr widgetrail::CompositionPoint contentPoint{100.0F, 80.0F};
    constexpr widgetrail::CompositionPoint guidePoint{200.0F, 30.0F};
    constexpr widgetrail::CompositionPoint trayPoint{500.0F, 60.0F};
    constexpr widgetrail::CompositionPoint fixedGuideOffset{-120.0F, 900.0F};
    constexpr widgetrail::CompositionPoint fixedTrayOffset{-200.0F, 980.0F};
    constexpr auto childStart = widgetrail::ApplyFixedChromeChildOffsets(
        widgetrail::PlanCompositionChildCoordinates(
        widgetrail::PlanCompositionMotion(
            632, 878, 632, 878, 592.0F, 698.0F,
            widgetrail::CompositionVerticalAnchor::Bottom),
        632, 878), fixedGuideOffset, fixedTrayOffset);
    constexpr auto childMid = widgetrail::ApplyFixedChromeChildOffsets(
        widgetrail::PlanCompositionChildCoordinates(
        widgetrail::PlanCompositionMotion(
            632, 878, 632, 878, 612.0F, 788.0F,
            widgetrail::CompositionVerticalAnchor::Bottom),
        632, 878), fixedGuideOffset, fixedTrayOffset);
    constexpr auto childEnd = widgetrail::ApplyFixedChromeChildOffsets(
        widgetrail::PlanCompositionChildCoordinates(
        widgetrail::PlanCompositionMotion(
            632, 878, 632, 878, 632.0F, 878.0F,
            widgetrail::CompositionVerticalAnchor::Bottom),
        632, 878), fixedGuideOffset, fixedTrayOffset);
    constexpr auto trayStart = widgetrail::ProjectTrayPoint(childStart, trayPoint);
    constexpr auto trayMid = widgetrail::ProjectTrayPoint(childMid, trayPoint);
    constexpr auto trayEnd = widgetrail::ProjectTrayPoint(childEnd, trayPoint);
    constexpr auto guideStart = widgetrail::ProjectGuidePoint(childStart, guidePoint);
    constexpr auto guideMid = widgetrail::ProjectGuidePoint(childMid, guidePoint);
    constexpr auto guideEnd = widgetrail::ProjectGuidePoint(childEnd, guidePoint);
    Check(trayStart == trayMid && trayMid == trayEnd &&
              guideStart == guideMid && guideMid == guideEnd,
          "companion-HWND guide and tray stay fixed through content motion");
    constexpr auto contentStart = widgetrail::ProjectContentPoint(childStart, contentPoint);
    constexpr auto contentMid = widgetrail::ProjectContentPoint(childMid, contentPoint);
    constexpr auto contentEnd = widgetrail::ProjectContentPoint(childEnd, contentPoint);
    Check(contentStart != contentMid && contentMid != contentEnd,
          "only the destination content coordinate space advances during motion");
    constexpr auto inverseMid = widgetrail::InverseContentPoint(childMid, contentMid);
    CheckNear(inverseMid.x, contentPoint.x,
              "midpoint pointer inverse agrees with animated content x");
    CheckNear(inverseMid.y, contentPoint.y,
              "midpoint pointer inverse agrees with animated content y");
    constexpr auto trayInverse = widgetrail::InverseTrayPoint(childMid, trayMid);
    CheckNear(trayInverse.x, trayPoint.x,
              "midpoint tray pointer remains in fixed child x space");
    CheckNear(trayInverse.y, trayPoint.y,
              "midpoint tray pointer remains in fixed child y space");
    constexpr auto oddContainerChrome = widgetrail::ApplyFixedChromeChildOffsets(
        widgetrail::PlanCompositionChildCoordinates(widgetrail::PlanCompositionMotion(
            1181, 878, 632, 878, 612.0F, 788.0F,
            widgetrail::CompositionVerticalAnchor::Bottom),
        632, 878, 275.0F, 0.0F),
        fixedGuideOffset, {275.0F, 980.0F});
    CheckNear(oddContainerChrome.content.offsetX, 284.5F,
              "odd-width content union retains its fractional visual center");
    CheckNear(widgetrail::ProjectTrayPoint(oddContainerChrome, trayPoint).x, 775.0F,
              "fixed tray uses the exact companion-HWND offset at odd parity");

    Check(widgetrail::ResolveWidgetExtentAuthority(true, true) ==
              widgetrail::WidgetExtentAuthority::AdmittedSnapshot,
          "admitted snapshot owns extent even when a prior surface exists");
    Check(widgetrail::ResolveWidgetExtentAuthority(true, false) ==
              widgetrail::WidgetExtentAuthority::AdmittedSnapshot,
          "admitted snapshot does not require retained geometry");
    Check(widgetrail::ResolveWidgetExtentAuthority(false, true) ==
              widgetrail::WidgetExtentAuthority::RetainedCommittedSurface,
          "worker-start copy retains the previously committed widget extent");
    Check(widgetrail::ResolveWidgetExtentAuthority(false, false) ==
              widgetrail::WidgetExtentAuthority::CompactStartupFallback,
          "compact fallback is reserved for the first widget open");

    Check(widgetrail::ResolveWidgetContentAuthority(true, true, true) ==
              widgetrail::WidgetContentAuthority::AdmittedSnapshot,
          "a current destination checkpoint owns visible widget content");
    Check(widgetrail::ResolveWidgetContentAuthority(true, true, false) ==
              widgetrail::WidgetContentAuthority::AdmittedSnapshot,
          "first-open current content uses its admitted checkpoint directly");
    Check(widgetrail::ResolveWidgetContentAuthority(true, false, true) ==
              widgetrail::WidgetContentAuthority::InertRetainedSnapshot,
          "failed content retains only its own inert checkpoint");
    Check(widgetrail::ResolveWidgetContentAuthority(false, false, true) ==
              widgetrail::WidgetContentAuthority::RetainedCommittedSnapshot,
          "cold worker startup keeps the prior transition checkpoint painted");
    Check(widgetrail::ResolveWidgetContentAuthority(false, false, false) ==
              widgetrail::WidgetContentAuthority::StableStartupStatus,
          "startup status is reserved for an open with no committed content");

    constexpr widgetrail::WidgetContentFocusSources focusSources{
        L"current.focus", L"refresh.focus", L"committed.focus"};
    static_assert(widgetrail::WidgetContentAuthorityCount == 4);
    Check(widgetrail::ResolveWidgetContentFocusId(
              widgetrail::WidgetContentAuthority::AdmittedSnapshot, focusSources) ==
              L"current.focus",
          "current content uses the live widget focus identifier");
    Check(widgetrail::ResolveWidgetContentFocusId(
              widgetrail::WidgetContentAuthority::InertRetainedSnapshot, focusSources) ==
              L"refresh.focus",
          "RefreshRetained content uses only its retained focus identifier");
    Check(widgetrail::ResolveWidgetContentFocusId(
              widgetrail::WidgetContentAuthority::RetainedCommittedSnapshot, focusSources) ==
              L"committed.focus",
          "transition fallback content uses its committed focus identifier");
    Check(widgetrail::ResolveWidgetContentFocusId(
              widgetrail::WidgetContentAuthority::StableStartupStatus, focusSources) ==
              L"current.focus",
          "startup fallback preserves the existing live-focus source");
    bool unknownContentAuthorityRejected = false;
    try {
        static_cast<void>(widgetrail::ResolveWidgetContentFocusId(
            widgetrail::WidgetContentAuthority::Count, focusSources));
    } catch (const std::invalid_argument&) {
        unknownContentAuthorityRejected = true;
    }
    Check(unknownContentAuthorityRejected,
          "an unknown content authority cannot silently select a focus source");

    Check(widgetrail::DecideDisplayRefresh(false, DisplayEnvironmentChange::Dpi) ==
              DisplayRefreshPlan{},
          "hidden DPI changes defer work until the next authoritative show");
    Check(widgetrail::DecideDisplayRefresh(true, DisplayEnvironmentChange::Dpi) ==
              DisplayRefreshPlan{true, false, true},
          "visible DPI changes recreate resources and reposition both windows");
    Check(widgetrail::DecideDisplayRefresh(true, DisplayEnvironmentChange::Topology) ==
              DisplayRefreshPlan{true, false, true},
          "topology changes re-resolve monitor bounds without appearance churn");
    Check(widgetrail::DecideDisplayRefresh(true, DisplayEnvironmentChange::SystemSettings) ==
              DisplayRefreshPlan{true, true, true},
          "work-area settings reapply appearance and reposition both windows");

    widgetrail::DisplayRefreshAccumulator displayRefresh;
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

    widgetrail::ForegroundTargetTracker tracker;
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

    widgetrail::PlacementRefreshGate gate;
    Check(gate.TryEnter(), "first placement enters gate");
    Check(!gate.TryEnter(), "synchronous DPI placement is coalesced instead of recursing");
    Check(!gate.TryEnter(), "multiple nested display messages remain one pending refresh");
    Check(gate.Complete(), "gate requests one deferred refresh after nested message");
    Check(gate.TryEnter(), "deferred placement enters after original completes");
    Check(!gate.Complete(), "settled placement does not schedule another refresh");

    for (const auto position : {widgetrail::OverlayPosition::BottomLeft, widgetrail::OverlayPosition::BottomRight}) {
        for (const float width : {320.0F, 639.5F, 961.0F}) {
            const auto motion = widgetrail::PlanCompositionMotion(961, 701, 640, 400,
                width, 500, widgetrail::CompositionVerticalAnchor::Bottom, position);
            const bool left = position == widgetrail::OverlayPosition::BottomLeft;
            Check(left ? motion.offsetX == 0 : motion.offsetX + width == 961,
                "corner resize holds the outer edge across fractional animation frames");
            Check(motion.offsetY + 500 == 701, "corner resize holds the bottom edge");
            const auto spaces = widgetrail::PlanCompositionChildCoordinates(motion, 640, 400);
            const widgetrail::CompositionPoint local{90, 70};
            const auto roundTrip = widgetrail::InverseContentPoint(spaces, widgetrail::ProjectContentPoint(spaces, local));
            Check(std::abs(roundTrip.x - local.x) < 0.01F && std::abs(roundTrip.y - local.y) < 0.01F,
                "corner animation preserves pointer coordinate round trips");
            Check(left ? spaces.chromeOffsetX == 0 : spaces.chromeOffsetX + 640 == 961,
                "destination chrome uses the same fixed edge");
        }
    }
    std::cout << "OverlayTargetingTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
