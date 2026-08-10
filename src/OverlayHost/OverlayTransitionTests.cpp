#include "OverlayTransition.h"

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
    gba::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(10, false);
    auto frame = timeline.Sample(10, false);
    Near(frame.shellOpacity, 0.0F, "open starts transparent");
    Check(frame.shellActive, "open requests bounded follow-up work");

    frame = timeline.Sample(
        10 + gba::OverlayTransitionTimeline::OpenDurationMilliseconds, false);
    Near(frame.shellOpacity, 1.0F, "open reaches opaque terminal state");
    Check(!frame.shellActive, "settled open owns no idle frames");
    Check(!timeline.TakeHideCompletion(), "open cannot request a physical hide");

    timeline.BeginClose(200, false);
    frame = timeline.Sample(200, false);
    Near(frame.shellOpacity, 1.0F, "close starts from visible alpha");
    Check(frame.shellActive, "close requests bounded follow-up work");
    frame = timeline.Sample(
        200 + gba::OverlayTransitionTimeline::CloseDurationMilliseconds, false);
    Near(frame.shellOpacity, 0.0F, "close reaches transparent terminal state");
    Check(!frame.active, "settled close owns no idle frames");
    Check(timeline.TakeHideCompletion(), "close completes one physical hide");
    Check(!timeline.TakeHideCompletion(), "hide completion is one-shot");
}

void ReversesWithoutOpacityDiscontinuity() {
    gba::OverlayTransitionTimeline timeline;
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
    gba::OverlayTransitionTimeline timeline;
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
        500 + gba::OverlayTransitionTimeline::OpenDurationMilliseconds, false);
    Near(frame.shellOpacity, 1.0F,
         "paint-primed initial open reaches full opacity");
}

void ReducedMotionSnapsAtStartAndMidFlight() {
    gba::OverlayTransitionTimeline timeline;
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
    gba::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(0, true);
    timeline.BeginContentReveal(10, false);
    auto frame = timeline.Sample(10, false);
    Near(frame.contentOpacity,
         gba::OverlayTransitionTimeline::ContentRevealStartOpacity,
         "replacement reveal starts from subtle nonzero opacity");
    Check(frame.contentActive, "replacement reveal requests a frame");
    timeline.SnapContentVisible();
    frame = timeline.Sample(11, false);
    Near(frame.contentOpacity, 1.0F, "entering widget focus snaps content visible");
    Check(!frame.contentActive, "snapped content owns no idle work");
}

void RepeatedContentRevealIsContinuous() {
    gba::OverlayTransitionTimeline timeline;
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
    gba::OverlayExtentTransitionTimeline timeline;
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
        80 + gba::OverlayExtentTransitionTimeline::DurationMilliseconds, false);
    Near(frame.widthDip, 820.0F, "retargeted extent reaches final width");
    Near(frame.heightDip, 430.0F, "retargeted extent reaches final height");
    Check(!frame.active, "settled extent owns no idle frames");
}

void ReducedMotionExtentSnapsImmediately() {
    gba::OverlayExtentTransitionTimeline timeline;
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

void ClockAndDecisionsAreStable() {
    gba::OverlayTransitionTimeline timeline;
    timeline.BeginOpen(100, false);
    const auto forward = timeline.Sample(120, false);
    const auto reversedClock = timeline.Sample(90, false);
    Near(reversedClock.shellOpacity, forward.shellOpacity,
         "reversed clocks cannot rewind a transition");

    Check(gba::ShouldRevealWidgetContent(
              false, {}, true, L"music"),
          "dashboard to widget starts content reveal");
    Check(gba::ShouldRevealWidgetContent(
              true, L"audio", true, L"network"),
          "active widget identity change starts content reveal");
    Check(gba::ShouldRevealWidgetContent(
              true, L"audio", true, L"audio", true),
          "same public ID with replaced runtime starts content reveal");
    Check(!gba::ShouldRevealWidgetContent(
              true, L"audio", true, L"audio", false),
          "same identity snapshot update never flashes content");
    Check(gba::ShouldSnapWidgetContentVisible(true, false, true),
          "entering widget focus snaps content visible");
    Check(!gba::ShouldSnapWidgetContentVisible(true, true, true),
          "ordinary focused snapshot update does not alter transition");
}

} // namespace

int main() {
    OpensAndClosesWithinBounds();
    ReversesWithoutOpacityDiscontinuity();
    InitialOpenWaitsForCommittedPaint();
    ReducedMotionSnapsAtStartAndMidFlight();
    ContentRevealIsBoundedAndSnappable();
    RepeatedContentRevealIsContinuous();
    ExtentTransitionIsBoundedAndRetargetable();
    ReducedMotionExtentSnapsImmediately();
    ClockAndDecisionsAreStable();
    std::cout << "OverlayTransitionTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
