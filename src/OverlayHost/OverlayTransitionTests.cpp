#include "OverlayTransition.h"
#include "OverlayPresentationTransaction.h"

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(const float actual, const float expected, const char* message) {
    Check(std::abs(actual - expected) <= 0.015F, message);
}

void OpensAndClosesWithinBounds() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(10, false);
    auto frame = timeline.Sample(10, false);
    Near(frame.shellOpacity, 0.0F, "open starts transparent");
    Check(frame.shellActive, "open requests bounded follow-up work");

    frame = timeline.Sample(
        10 + widgetrail::OverlayTransitionTimeline::OpenDurationMilliseconds, false);
    Near(frame.shellOpacity, 1.0F, "open reaches opaque terminal state");
    Check(!frame.shellActive, "settled open owns no idle frames");
    Check(!timeline.TakeHideCompletion(), "open cannot request a physical hide");

    timeline.BeginClose(200, false);
    frame = timeline.Sample(200, false);
    Near(frame.shellOpacity, 1.0F, "close starts from visible alpha");
    Check(frame.shellActive, "close requests bounded follow-up work");
    frame = timeline.Sample(
        200 + widgetrail::OverlayTransitionTimeline::CloseDurationMilliseconds, false);
    Near(frame.shellOpacity, 0.0F, "close reaches transparent terminal state");
    Check(!frame.active, "settled close owns no idle frames");
    Check(timeline.TakeHideCompletion(), "close completes one physical hide");
    Check(!timeline.TakeHideCompletion(), "hide completion is one-shot");
}

void ReversesWithoutOpacityDiscontinuity() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, false);
    const auto opening = timeline.Sample(70, false);
    timeline.BeginClose(70, false);
    const auto closingStart = timeline.Sample(70, false);
    Near(closingStart.shellOpacity, opening.shellOpacity,
         "close retarget starts at the presented open opacity");
    const auto closing = timeline.Sample(100, false);
    timeline.BeginOpen(100, false);
    const auto reopened = timeline.Sample(100, false);
    Near(reopened.shellOpacity, closing.shellOpacity,
         "reopen retarget starts at the presented close opacity");
    Check(!timeline.TakeHideCompletion(),
          "reopen clears a pending physical hide");
}

void InitialOpenWaitsForCommittedPaint() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, false);
    (void)timeline.Sample(50, false);
    timeline.BeginClose(50, false);
    (void)timeline.Sample(80, false);

    timeline.PrepareInitialOpen();
    auto frame = timeline.Sample(500, false);
    Near(frame.shellOpacity, 0.0F,
         "prepared initial open stays transparent while paint is pending");
    Check(!frame.shellActive,
          "prepared initial open owns no animation work before paint");
    Check(!timeline.TakeHideCompletion(),
          "prepared initial open cancels stale close completion");

    timeline.BeginOpen(500, false);
    frame = timeline.Sample(500, false);
    Near(frame.shellOpacity, 0.0F,
         "successful paint begins open from transparent");
    frame = timeline.Sample(
        500 + widgetrail::OverlayTransitionTimeline::OpenDurationMilliseconds, false);
    Near(frame.shellOpacity, 1.0F,
         "paint-primed initial open reaches full opacity");
}

void ResidentShowRecoveryIsIdempotent() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, true);
    auto frame = timeline.Sample(0, true);
    Near(frame.shellOpacity, 1.0F,
         "resident surface starts from a fully committed presentation");

    timeline.BeginOpen(10, false);
    frame = timeline.Sample(10, false);
    Near(frame.shellOpacity, 1.0F,
         "Show on an already-presented surface is an opacity no-op");
    Check(!frame.shellActive,
          "idempotent visible Show schedules no unnecessary transition frames");
    Check(!timeline.TakeHideCompletion(),
          "idempotent visible Show cannot acknowledge a stale hide");

    timeline.BeginClose(20, false);
    const auto closing = timeline.Sample(70, false);
    Check(closing.shellOpacity > 0.0F && closing.shellOpacity < 1.0F,
          "resident fixture reaches a physically incomplete close");
    timeline.BeginOpen(70, false);
    frame = timeline.Sample(70, false);
    Near(frame.shellOpacity, closing.shellOpacity,
         "real resident recovery resumes from the physically presented opacity");
    Check(frame.shellActive,
          "partially closed resident Show performs bounded recovery work");
    Check(!timeline.TakeHideCompletion(),
          "resident recovery revokes pending hide completion before settlement");
    frame = timeline.Sample(
        70 + widgetrail::OverlayTransitionTimeline::OpenDurationMilliseconds,
        false);
    Near(frame.shellOpacity, 1.0F,
         "resident recovery settles at a fully visible presentation");
    Check(!frame.shellActive,
          "settled resident recovery owns no polling or watchdog cadence");

    timeline.BeginClose(300, true);
    Check(timeline.TakeHideCompletion(),
          "fixture proves a completed close would normally hide the HWNDs");
    timeline.BeginClose(310, true);
    timeline.BeginOpen(310, true);
    frame = timeline.Sample(310, true);
    Near(frame.shellOpacity, 1.0F,
         "resident Show restores a zero-opacity completed close immediately");
    Check(!timeline.TakeHideCompletion(),
          "recovery clears a ready hide acknowledgement before it can run");
}

void ReducedMotionSnapsAtStartAndMidFlight() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, true);
    auto frame = timeline.Sample(0, true);
    Near(frame.shellOpacity, 1.0F, "reduced-motion open snaps visible");
    Check(!frame.active, "reduced-motion open owns no frame loop");

    timeline.BeginContentReveal(10, false);
    frame = timeline.Sample(30, false);
    Check(frame.contentActive && frame.contentOpacity < 1.0F,
          "normal content reveal can animate");
    frame = timeline.Sample(31, true);
    Near(frame.contentOpacity, 1.0F,
         "enabling reduced motion snaps active content reveal");
    Check(!frame.active, "mid-flight reduced motion cancels future frames");

    timeline.BeginClose(40, true);
    frame = timeline.Sample(40, true);
    Near(frame.shellOpacity, 0.0F, "reduced-motion close snaps hidden");
    Check(timeline.TakeHideCompletion(),
          "reduced-motion close still requests physical finalization");
}

void ContentRevealIsBoundedAndSnappable() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, true);
    timeline.BeginContentReveal(10, false);
    auto frame = timeline.Sample(10, false);
    Near(frame.contentOpacity,
         widgetrail::OverlayTransitionTimeline::ContentRevealStartOpacity,
         "replacement reveal starts from subtle nonzero opacity");
    Check(frame.contentActive, "replacement reveal requests a frame");
    timeline.SnapContentVisible();
    frame = timeline.Sample(11, false);
    Near(frame.contentOpacity, 1.0F, "entering widget focus snaps content visible");
    Check(!frame.contentActive, "snapped content owns no idle work");
}

void RepeatedContentRevealIsContinuous() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, true);
    timeline.BeginContentReveal(10, false);
    const auto first = timeline.Sample(55, false);
    Check(first.contentActive, "first content reveal remains active mid-flight");

    timeline.BeginContentReveal(55, false);
    const auto retargeted = timeline.Sample(55, false);
    Near(retargeted.contentOpacity, first.contentOpacity,
         "mid-flight content replacement preserves presented opacity");
    Check(retargeted.contentActive,
          "retargeted content reveal remains bounded animation work");
}

void ExtentTransitionIsBoundedAndRetargetable() {
    widgetrail::OverlayExtentTransitionTimeline timeline;
    timeline.Begin(10, 620.0F, 520.0F, 980.0F, 560.0F, false);
    auto frame = timeline.Sample(10, false);
    Near(frame.widthDip, 620.0F, "extent transition starts at committed width");
    Near(frame.heightDip, 520.0F, "extent transition starts at committed height");
    Check(frame.active, "extent transition requests bounded visible frames");

    frame = timeline.Sample(80, false);
    Check(frame.widthDip > 620.0F && frame.widthDip < 980.0F,
          "extent transition interpolates between committed and target widths");
    const auto presentedWidth = frame.widthDip;
    const auto presentedHeight = frame.heightDip;
    timeline.Begin(
        80, presentedWidth, presentedHeight, 820.0F, 430.0F, false);
    frame = timeline.Sample(80, false);
    Near(frame.widthDip, presentedWidth,
         "rapid reversal retargets from the presented width");
    Near(frame.heightDip, presentedHeight,
         "rapid reversal retargets from the presented height");

    frame = timeline.Sample(
        80 + widgetrail::OverlayExtentTransitionTimeline::DurationMilliseconds, false);
    Near(frame.widthDip, 820.0F, "retargeted extent reaches final width");
    Near(frame.heightDip, 430.0F, "retargeted extent reaches final height");
    Check(!frame.active, "settled extent owns no idle frames");
}

void ReducedMotionExtentSnapsImmediately() {
    widgetrail::OverlayExtentTransitionTimeline timeline;
    timeline.Begin(0, 520.0F, 520.0F, 980.0F, 560.0F, true);
    auto frame = timeline.Sample(0, true);
    Near(frame.widthDip, 980.0F, "reduced motion snaps destination width");
    Near(frame.heightDip, 560.0F, "reduced motion snaps destination height");
    Check(!frame.active, "reduced extent owns no frame loop");

    timeline.Begin(10, 980.0F, 560.0F, 820.0F, 430.0F, false);
    frame = timeline.Sample(45, false);
    Check(frame.active, "full-motion extent can be active before preference change");
    frame = timeline.Sample(46, true);
    Near(frame.widthDip, 820.0F, "mid-flight reduced motion snaps final width");
    Near(frame.heightDip, 430.0F, "mid-flight reduced motion snaps final height");
    Check(!frame.active, "mid-flight reduced preference cancels extent work");
}

void HiddenExtentRetiresWithoutTerminalFrameWork() {
    widgetrail::OverlayExtentTransitionTimeline timeline;
    timeline.Begin(10, 980.0F, 700.0F, 540.0F, 620.0F, false);
    const auto presented = timeline.Sample(55, false);
    Check(presented.active,
          "visible extent is active before the host becomes hidden");

    timeline.Cancel();
    Check(!timeline.active(),
          "hidden extent retirement owns no future controller cadence");
    const auto hidden = timeline.Sample(5000, false);
    Near(hidden.widthDip, presented.widthDip,
         "hidden retirement does not synthesize a terminal width frame");
    Near(hidden.heightDip, presented.heightDip,
         "hidden retirement does not synthesize a terminal height frame");
    Check(!hidden.active,
          "sampling retired hidden state cannot restart frame work");

    timeline.Begin(6000, 540.0F, 620.0F, 820.0F, 430.0F, false);
    const auto reopened = timeline.Sample(6000, false);
    Near(reopened.widthDip, 540.0F,
         "reopened extent begins from current authoritative width");
    Near(reopened.heightDip, 620.0F,
         "reopened extent begins from current authoritative height");
}

void DestinationAdmissionOwnsLayoutBeforeEnvelopeSettlement() {
    widgetrail::OverlayPresentationTransaction transaction;
    widgetrail::WidgetSnapshot audioSnapshot;
    audioSnapshot.instanceId = L"audio-mixer.default";
    widgetrail::WidgetSurfaceRequest audioRequest;
    audioRequest.mode = widgetrail::WidgetSurfaceMode::Compact;
    audioRequest.preferredWidthDip = 520.0F;
    audioRequest.preferredHeightDip = 520.0F;
    transaction.RetainAdmittedWidget(
        L"audio-mixer", audioSnapshot, L"audio-master", audioRequest);
    Check(transaction.retainedPresentation() &&
              transaction.retainedPresentation()->widgetId == L"audio-mixer" &&
              transaction.retainedSurfaceRequest(),
          "transaction exclusively retains the admitted source presentation");

    constexpr widgetrail::OverlayPresentationExtent audioExtent{592, 698};
    constexpr widgetrail::OverlayPresentationExtent networkExtent{632, 878};
    constexpr widgetrail::OverlayPlacement networkPlacement{484, 22, 632, 878};
    constexpr widgetrail::OverlayPlacement sharedContainer{484, 22, 632, 878};
    transaction.BeginExtentTransition(
        audioExtent, networkExtent, 100, false, true);
    const auto directive = transaction.PrepareCompositionAdmission(
        592, 698, networkPlacement, sharedContainer, networkExtent,
        100, false, true);
    Check(directive.animateMotion &&
              directive.destinationExtentDip == networkExtent &&
              directive.destinationPlacement.x == networkPlacement.x &&
              directive.destinationPlacement.y == networkPlacement.y &&
              directive.destinationPlacement.width == networkPlacement.width &&
              directive.destinationPlacement.height == networkPlacement.height,
          "admission directive carries destination layout independently of motion");
    Near(directive.initialPresentation.scaleX, 592.0F / 632.0F,
         "complete Network frame starts inside Audio width envelope");
    Near(directive.initialPresentation.scaleY, 698.0F / 878.0F,
         "complete Network frame starts inside Audio height envelope");
    transaction.AcceptCompositionAdmission(directive);
    Check(transaction.PresentedExtent(networkExtent, true) == audioExtent,
          "source extent remains only the visual envelope before settlement");

    const auto final = transaction.PrepareCompositionStep(
        100 + widgetrail::OverlayExtentTransitionTimeline::DurationMilliseconds,
        false);
    Check(final && final->finalFrame &&
              final->presentedExtentDip == networkExtent &&
              final->destinationPlacement.x == networkPlacement.x &&
              final->destinationPlacement.y == networkPlacement.y &&
              final->destinationPlacement.width == networkPlacement.width &&
              final->destinationPlacement.height == networkPlacement.height,
          "motion ends at explicit destination geometry");
    transaction.AcceptCompositionStep(*final);
    Check(transaction.PresentedExtent(networkExtent, true) == networkExtent &&
              !transaction.extentTransitionActive(),
          "settled destination retires all retained extent authority");
    const auto settled = transaction.CurrentMotionPlan(
        632, 878, networkExtent);
    Check(settled && settled->scaleX == 1.0F && settled->scaleY == 1.0F &&
              settled->offsetX == 0.0F && settled->offsetY == 0.0F,
          "settled viewport is authored Network geometry without stale scaling");
}

void CommittedDestinationDrivesLateAdmissionAndStableRefresh() {
    widgetrail::OverlayPresentationTransaction transaction;
    constexpr widgetrail::OverlayPresentationExtent retainedNetwork{560, 645};
    constexpr widgetrail::OverlayPresentationExtent admittedYtMusic{760, 385};
    constexpr widgetrail::OverlayPlacement networkPlacement{680, 250, 560, 645};
    constexpr widgetrail::OverlayPlacement networkContainer = networkPlacement;
    const auto network = transaction.PrepareCompositionAdmission(
        0, 0, networkPlacement, networkContainer, retainedNetwork,
        10, false, false);
    transaction.AcceptCompositionAdmission(network, L"network-controls");
    Check(transaction.committedDestination() &&
              transaction.committedDestination()->widgetId == L"network-controls" &&
              transaction.CommittedDestinationExtent(admittedYtMusic) == retainedNetwork &&
              transaction.PresentedExtent(admittedYtMusic, true) == retainedNetwork,
          "late provider model changes cannot replace committed destination authority");
    Check(widgetrail::DecideOverlayPresentation(
              true, true,
              transaction.CommittedDestinationExtent(admittedYtMusic),
              admittedYtMusic) == widgetrail::OverlayPresentationDirective::Place,
          "YT admission requests placement against the retained Network destination");

    constexpr int guideTop = 900;
    constexpr int authoredGap = 5;
    constexpr widgetrail::OverlayPlacement ytPlacement{
        580, guideTop - authoredGap - admittedYtMusic.heightDip,
        admittedYtMusic.widthDip, admittedYtMusic.heightDip};
    constexpr widgetrail::OverlayPlacement motionContainer{
        580, guideTop - authoredGap - retainedNetwork.heightDip,
        admittedYtMusic.widthDip, retainedNetwork.heightDip};
    transaction.BeginExtentTransition(
        retainedNetwork, admittedYtMusic, 100, false, true);
    const auto ytAdmission = transaction.PrepareCompositionAdmission(
        retainedNetwork.widthDip, retainedNetwork.heightDip,
        ytPlacement, motionContainer, admittedYtMusic,
        100, false, true);
    const auto visibleBottom = [&](const widgetrail::CompositionMotionPlan& motion) {
        return static_cast<float>(motionContainer.y) + motion.offsetY +
            static_cast<float>(ytPlacement.height) * motion.scaleY;
    };
    Near(visibleBottom(ytAdmission.initialPresentation),
         static_cast<float>(guideTop - authoredGap),
         "transition start keeps panel above the fixed guide gap");
    transaction.AcceptCompositionAdmission(ytAdmission, L"widgetrail.samples.ytmusic");
    Check(transaction.committedDestination() &&
              transaction.committedDestination()->widgetId ==
                  L"widgetrail.samples.ytmusic" &&
              transaction.CommittedDestinationExtent(retainedNetwork) == admittedYtMusic,
          "successful admission atomically advances identity and destination extent");

    const auto midpoint = transaction.PrepareCompositionStep(170, false);
    Check(midpoint && !midpoint->finalFrame,
          "accepted destination retains one in-flight midpoint");
    Near(visibleBottom(midpoint->presentation),
         static_cast<float>(guideTop - authoredGap),
         "transition midpoint keeps panel above the fixed guide gap");
    transaction.AcceptCompositionStep(*midpoint);

    const auto presentedMidpoint = transaction.PresentedExtent(
        admittedYtMusic, true);
    Check(presentedMidpoint != admittedYtMusic &&
              widgetrail::DecideOverlayPresentation(
                  true, true,
                  transaction.CommittedDestinationExtent(admittedYtMusic),
                  admittedYtMusic) == widgetrail::OverlayPresentationDirective::Repaint,
          "same-destination lifecycle refresh repaints without restarting motion");
    transaction.AcceptCompositionRepaint(L"widgetrail.samples.ytmusic");
    const auto final = transaction.PrepareCompositionStep(240, false);
    Check(final && final->finalFrame && final->index == midpoint->index + 1,
          "same-destination refresh preserves the original motion sequence");
    Near(visibleBottom(final->presentation),
         static_cast<float>(guideTop - authoredGap),
         "transition settlement keeps panel above the fixed guide gap");
    transaction.AcceptCompositionStep(*final);
    Check(transaction.PresentedExtent(admittedYtMusic, true) == admittedYtMusic,
          "settled YT destination owns its authored extent");
}

void ReducedMotionAdmissionCommitsDestinationDirectly() {
    widgetrail::OverlayPresentationTransaction transaction;
    constexpr widgetrail::OverlayPresentationExtent compact{592, 698};
    constexpr widgetrail::OverlayPresentationExtent wide{1052, 878};
    constexpr widgetrail::OverlayPlacement placement{274, 22, 1052, 878};
    transaction.BeginExtentTransition(compact, wide, 400, true, true);
    const auto directive = transaction.PrepareCompositionAdmission(
        592, 698, placement, placement, wide, 400, true, true);
    Check(!directive.animateMotion &&
              directive.initialPresentation.scaleX == 1.0F &&
              directive.initialPresentation.scaleY == 1.0F,
          "reduced motion admits the complete destination at final geometry");
    transaction.AcceptCompositionAdmission(directive);
    Check(transaction.PresentedExtent(wide, true) == wide,
          "reduced motion retains no source extent after admission");
}

void AnimationPreferencePathsPreserveContainerGeometry() {
    constexpr widgetrail::OverlayPresentationExtent sourceExtent{880, 445};
    constexpr widgetrail::OverlayPresentationExtent destinationExtent{580, 345};
    constexpr widgetrail::OverlayPlacement destinationPlacement{450, 300, 580, 345};
    constexpr widgetrail::OverlayPlacement retainedContainer{300, 200, 880, 445};

    widgetrail::OverlayPresentationTransaction snapTransaction;
    const auto snap = snapTransaction.PrepareCompositionAdmission(
        sourceExtent.widthDip, sourceExtent.heightDip,
        destinationPlacement, retainedContainer, destinationExtent,
        100, false, true);
    Check(!snap.animateMotion,
          "animation-off admission bypasses the extent motion path");
    Near(snap.initialPresentation.scaleX, 1.0F,
         "animation-off admission keeps destination scale one");
    Near(snap.initialPresentation.scaleY, 1.0F,
         "animation-off admission keeps destination scale one vertically");
    Near(snap.initialPresentation.offsetX,
         static_cast<float>(destinationPlacement.x - retainedContainer.x),
         "animation-off admission uses exact destination-to-container x offset");
    Near(snap.initialPresentation.offsetY,
         static_cast<float>(destinationPlacement.y - retainedContainer.y),
         "animation-off admission uses exact destination-to-container y offset");
    snapTransaction.AcceptCompositionAdmission(snap, L"settings");
    const auto snapped = snapTransaction.CurrentMotionPlan(
        retainedContainer.width, retainedContainer.height, destinationExtent);
    Check(snapped.has_value(),
          "accepted animation-off admission retains a settled presentation");
    Near(snapped->offsetX, snap.initialPresentation.offsetX,
         "settled animation-off x offset matches admission");
    Near(snapped->offsetY, snap.initialPresentation.offsetY,
         "settled animation-off y offset matches admission");

    widgetrail::OverlayPresentationTransaction animatedTransaction;
    animatedTransaction.BeginExtentTransition(
        sourceExtent, destinationExtent, 200, false, true);
    const auto animated = animatedTransaction.PrepareCompositionAdmission(
        sourceExtent.widthDip, sourceExtent.heightDip,
        destinationPlacement, retainedContainer, destinationExtent,
        200, false, true);
    Check(animated.animateMotion,
          "animation-on admission retains the existing extent motion path");
    const auto expected = widgetrail::PlanCompositionMotion(
        static_cast<unsigned int>(retainedContainer.width),
        static_cast<unsigned int>(retainedContainer.height),
        static_cast<unsigned int>(destinationPlacement.width),
        static_cast<unsigned int>(destinationPlacement.height),
        static_cast<float>(sourceExtent.widthDip),
        static_cast<float>(sourceExtent.heightDip),
        widgetrail::CompositionVerticalAnchor::Bottom);
    Near(animated.initialPresentation.scaleX, expected.scaleX,
         "animation-on admission preserves existing x scale");
    Near(animated.initialPresentation.scaleY, expected.scaleY,
         "animation-on admission preserves existing y scale");
    Near(animated.initialPresentation.offsetX, expected.offsetX,
         "animation-on admission preserves existing x motion offset");
    Near(animated.initialPresentation.offsetY, expected.offsetY,
         "animation-on admission preserves existing y motion offset");
}

void FullscreenExitSettlesBeforeCompositionAdmission() {
    widgetrail::OverlayPresentationTransaction transaction;
    constexpr widgetrail::OverlayPresentationExtent fullscreen{5072, 1384};
    constexpr widgetrail::OverlayPresentationExtent ordinary{760, 555};
    constexpr widgetrail::OverlayPlacement ordinaryPlacement{820, 325, 760, 555};

    transaction.BeginExtentTransition(fullscreen, ordinary, 100, false, true);
    Check(transaction.extentTransitionActive() && transaction.hasActiveExtent(),
          "fullscreen exit begins with pending extent authority");

    // The accepted host exit derives this decision from the last committed
    // fullscreen frame, even when the mutable successor snapshot is already
    // non-fullscreen, and settles before preparing composition admission.
    transaction.SettleExtent(ordinary, 101, true);
    const auto directive = transaction.PrepareCompositionAdmission(
        fullscreen.widthDip, fullscreen.heightDip,
        ordinaryPlacement, ordinaryPlacement, ordinary,
        101, false, true);
    Check(!transaction.extentTransitionActive() && !directive.animateMotion,
          "committed fullscreen exit cannot admit extent composition motion");
    Near(directive.initialPresentation.scaleX, 1.0F,
         "fullscreen exit directly restores ordinary viewport scale x");
    Near(directive.initialPresentation.scaleY, 1.0F,
         "fullscreen exit directly restores ordinary viewport scale y");
    Near(directive.initialPresentation.offsetX, 0.0F,
         "fullscreen exit directly restores ordinary viewport offset x");
    Near(directive.initialPresentation.offsetY, 0.0F,
         "fullscreen exit directly restores ordinary viewport offset y");

    transaction.AcceptCompositionAdmission(directive, L"neutral-media");
    Check(!transaction.hasActiveExtent() &&
              transaction.PresentedExtent(ordinary, true) == ordinary &&
              transaction.contentPlacement() &&
              transaction.contentPlacement()->x == ordinaryPlacement.x &&
              transaction.contentPlacement()->y == ordinaryPlacement.y &&
              transaction.contentPlacement()->width == ordinaryPlacement.width &&
              transaction.contentPlacement()->height == ordinaryPlacement.height,
          "fullscreen exit clears pending placement and commits exact ordinary viewport");
    Check(!transaction.PrepareCompositionStep(102, false),
          "fullscreen exit schedules no later composition motion frame");
}

void ClockAndDecisionsAreStable() {
    widgetrail::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(100, false);
    const auto forward = timeline.Sample(120, false);
    const auto reversedClock = timeline.Sample(90, false);
    Near(reversedClock.shellOpacity, forward.shellOpacity,
         "reversed clocks cannot rewind a transition");

    Check(widgetrail::ShouldRevealWidgetContent(
              false, {}, true, L"music"),
          "dashboard to widget starts content reveal");
    Check(widgetrail::ShouldRevealWidgetContent(
              true, L"audio", true, L"network"),
          "active widget identity change starts content reveal");
    Check(widgetrail::ShouldRevealWidgetContent(
              true, L"audio", true, L"audio", true),
          "same public ID with replaced runtime starts content reveal");
    Check(!widgetrail::ShouldRevealWidgetContent(
              true, L"audio", true, L"audio", false),
          "same identity snapshot update never flashes content");
    Check(widgetrail::ShouldSnapWidgetContentVisible(true, false, true),
          "entering widget focus snaps content visible");
    Check(!widgetrail::ShouldSnapWidgetContentVisible(true, true, true),
          "ordinary focused snapshot update does not alter transition");
}

} // namespace

int main() {
    OpensAndClosesWithinBounds();
    ReversesWithoutOpacityDiscontinuity();
    InitialOpenWaitsForCommittedPaint();
    ResidentShowRecoveryIsIdempotent();
    ReducedMotionSnapsAtStartAndMidFlight();
    ContentRevealIsBoundedAndSnappable();
    RepeatedContentRevealIsContinuous();
    ExtentTransitionIsBoundedAndRetargetable();
    ReducedMotionExtentSnapsImmediately();
    HiddenExtentRetiresWithoutTerminalFrameWork();
    DestinationAdmissionOwnsLayoutBeforeEnvelopeSettlement();
    CommittedDestinationDrivesLateAdmissionAndStableRefresh();
    ReducedMotionAdmissionCommitsDestinationDirectly();
    AnimationPreferencePathsPreserveContainerGeometry();
    FullscreenExitSettlesBeforeCompositionAdmission();
    ClockAndDecisionsAreStable();
    std::cout << "OverlayTransitionTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
