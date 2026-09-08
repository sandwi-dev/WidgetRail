#include "SliderInteraction.h"

#include <cmath>
#include <cstdlib>
#include <iostream>

namespace {
int checks{};
void Check(bool condition, const char* message) {
    ++checks;
    if (!condition) { std::cerr << "FAIL: " << message << '\n'; std::exit(EXIT_FAILURE); }
}
void Near(double actual, double expected, const char* message) {
    Check(std::abs(actual - expected) < 1e-9, message);
}
widgetrail::input::SliderInputDescriptor Slider(double value = 0.0) {
    return {L"runtime-1", L"root", L"volume", L"volume.changed", 1,
            -1.0, 1.0, value, 0.3, false, false};
}
}

int main() {
    using namespace widgetrail::input;
    SliderInteractionState state;
    auto slider = Slider();
    auto presentationRevision = state.presentationRevision();
    auto first = state.Adjust(slider, NavigationDirection::Right, 10);
    Check(first.consumed, "right is consumed into a local pending preview");
    Check(state.presentationRevision() > presentationRevision,
          "first optimistic target advances the presentation revision");
    presentationRevision = state.presentationRevision();
    const auto firstPreview = state.PresentationValue(slider);
    Check(firstPreview.has_value(), "first adjustment publishes a local preview");
    Near(*firstPreview, 0.2,
         "arbitrary range snaps to next minimum-anchored step");
    auto repeated = state.Adjust(slider, NavigationDirection::Right, 20);
    Check(state.presentationRevision() > presentationRevision,
          "repeated optimistic target advances the presentation revision");
    Check(repeated.consumed, "repeat remains local during its trailing window");
    const auto repeatedPreview = state.PresentationValue(slider);
    Check(repeatedPreview.has_value(), "repeated adjustment updates the local preview");
    Near(*repeatedPreview, 0.5,
         "repeat advances transient target, not stale snapshot");
    Check(!state.TakePendingDispatch(slider, 169),
          "latest target waits for the complete 150 ms trailing window");
    const auto trailingDeadline = state.NextReconcileDeadline();
    Check(trailingDeadline && *trailingDeadline == 170,
          "trailing dispatch deadline belongs to the latest actual change");
    Check(state.SetRequestedValue(slider, 0.5, 169) &&
              state.NextReconcileDeadline() == trailingDeadline,
          "a no-op absolute request does not extend the trailing deadline");
    const auto firstDispatch = state.TakePendingDispatch(slider, 170);
    Check(firstDispatch && firstDispatch->intentGeneration != 0,
          "trailing settlement dispatches the latest target exactly once");
    Near(firstDispatch->requestedValue, 0.5,
         "trailing settlement carries the latest computed target");
    Check(!state.TakePendingDispatch(slider, 171),
          "an already dispatched target is not emitted per poll");

    const auto unitSlider = [](const std::wstring_view nodeId, const double value) {
        auto result = Slider(value);
        result.nodeId = nodeId;
        result.minimum = 0.0;
        result.maximum = 1.0;
        result.step = 0.01;
        return result;
    };
    SliderInteractionState gridState;
    auto floatLeft = gridState.Adjust(
        unitSlider(L"float-left", static_cast<double>(0.32F)),
        NavigationDirection::Left, 26);
    Check(floatLeft.consumed,
          "float-derived near-grid value previews on first Left");
    const auto floatLeftPreview = gridState.PresentationValue(
        unitSlider(L"float-left", static_cast<double>(0.32F)));
    Check(floatLeftPreview.has_value(), "float-derived Left publishes a preview");
    Near(*floatLeftPreview, 0.31,
         "float-derived near-grid value steps left instead of snapping in place");
    auto floatRight = gridState.Adjust(
        unitSlider(L"float-right", static_cast<double>(0.32F)),
        NavigationDirection::Right, 27);
    Check(floatRight.consumed,
          "float-derived near-grid value previews on first Right");
    const auto floatRightPreview = gridState.PresentationValue(
        unitSlider(L"float-right", static_cast<double>(0.32F)));
    Check(floatRightPreview.has_value(), "float-derived Right publishes a preview");
    Near(*floatRightPreview, 0.33,
         "float-derived near-grid value steps right instead of snapping in place");
    auto offGridLeft = gridState.Adjust(
        unitSlider(L"off-grid-left", 0.325), NavigationDirection::Left, 28);
    Check(offGridLeft.consumed,
          "genuine off-grid value previews Left");
    const auto offGridLeftPreview = gridState.PresentationValue(
        unitSlider(L"off-grid-left", 0.325));
    Check(offGridLeftPreview.has_value(), "off-grid Left publishes a preview");
    Near(*offGridLeftPreview, 0.32,
         "genuine off-grid Left retains directional snap semantics");
    auto offGridRight = gridState.Adjust(
        unitSlider(L"off-grid-right", 0.325), NavigationDirection::Right, 29);
    Check(offGridRight.consumed,
          "genuine off-grid value previews Right");
    const auto offGridRightPreview = gridState.PresentationValue(
        unitSlider(L"off-grid-right", 0.325));
    Check(offGridRightPreview.has_value(), "off-grid Right publishes a preview");
    Near(*offGridRightPreview, 0.33,
         "genuine off-grid Right retains directional snap semantics");
    auto atMinimum = gridState.Adjust(
        unitSlider(L"minimum", 0.0), NavigationDirection::Left, 30);
    Check(atMinimum.consumed &&
              !gridState.PresentationValue(unitSlider(L"minimum", 0.0)),
          "minimum consumes Left without previewing below the range");
    auto atMaximum = gridState.Adjust(
        unitSlider(L"maximum", 1.0), NavigationDirection::Right, 31);
    Check(atMaximum.consumed &&
              !gridState.PresentationValue(unitSlider(L"maximum", 1.0)),
          "maximum consumes Right without previewing above the range");

    auto cancellationSlider = slider;
    cancellationSlider.nodeId = L"cancellation";
    cancellationSlider.minimum = 0.0;
    cancellationSlider.maximum = 1.0;
    cancellationSlider.value = 0.0;
    cancellationSlider.step = 0.1;
    auto away = state.Adjust(cancellationSlider, NavigationDirection::Right, 26);
    Check(away.consumed, "first adjustment leaves authoritative value");
    const auto awayPreview = state.PresentationValue(cancellationSlider);
    Check(awayPreview.has_value(), "unsent adjustment publishes a preview");
    Near(*awayPreview, 0.1,
         "unsent adjustment owns a local preview");
    auto back = state.Adjust(cancellationSlider, NavigationDirection::Left, 27);
    Check(back.consumed && !state.PresentationValue(cancellationSlider),
          "unsent reverse adjustment cancels back to authoritative value");
    Check(!state.TakePendingDispatch(cancellationSlider, 1'000, true),
          "unsent away/back cancellation leaves no dispatchable work");
    cancellationSlider.snapshotSequence = 2;
    const auto cancelledReconciliation = state.Reconcile(cancellationSlider, 29);
    Check(!cancelledReconciliation.visualChanged &&
              !state.PresentationValue(cancellationSlider),
          "newer snapshot keeps an unsent cancellation authoritative");

    auto dispatchedSlider = cancellationSlider;
    dispatchedSlider.nodeId = L"two-dispatched";
    dispatchedSlider.snapshotSequence = 1;
    dispatchedSlider.value = 0.0;
    Check(state.Adjust(dispatchedSlider, NavigationDirection::Right, 30).consumed,
          "first held segment creates a pending value");
    const auto dispatchedFirst = state.TakePendingDispatch(
        dispatchedSlider, 30, true);
    Check(dispatchedFirst.has_value(),
          "first held segment dispatches its exact value");
    Near(dispatchedFirst->requestedValue, 0.1,
         "first dispatched value preserves computed step math");
    Check(state.Adjust(dispatchedSlider, NavigationDirection::Right, 31).consumed,
          "continued hold advances beyond the first dispatched value");
    const auto dispatchedSecond = state.TakePendingDispatch(
        dispatchedSlider, 31, true);
    Check(dispatchedSecond && dispatchedSecond->intentGeneration !=
              dispatchedFirst->intentGeneration,
          "second held segment dispatches a distinct latest value");
    Near(dispatchedSecond->requestedValue, 0.2,
         "second dispatched value preserves computed step math");
    dispatchedSlider.snapshotSequence = 2;
    dispatchedSlider.value = 0.1;
    const auto olderAcknowledgement = state.Reconcile(dispatchedSlider, 32);
    const auto newerPreview = state.PresentationValue(dispatchedSlider);
    Check(!olderAcknowledgement.visualChanged && newerPreview.has_value(),
          "older dispatched acknowledgement retains the newer optimistic value");
    Near(*newerPreview, 0.2,
         "older acknowledgement preserves the exact newer computed target");
    dispatchedSlider.snapshotSequence = 3;
    dispatchedSlider.value = 0.2;
    const auto latestAcknowledgement = state.Reconcile(dispatchedSlider, 33);
    Check(!latestAcknowledgement.visualChanged &&
              !state.PresentationValue(dispatchedSlider),
          "latest dispatched acknowledgement settles the optimistic value");

    slider.value = 0.5;
    slider.snapshotSequence = 2;
    presentationRevision = state.presentationRevision();
    const auto acknowledgement = state.Reconcile(slider, 172);
    Check(!state.PresentationValue(slider),
          "authoritative acknowledgement clears override");
    Check(acknowledgement.stateChanged && !acknowledgement.visualChanged,
          "matching publication settles state without repainting matching pixels");
    Check(state.presentationRevision() == presentationRevision,
          "matching acknowledgement keeps already-presented pixels unchanged");
    Check(state.Adjust(slider, NavigationDirection::Right, 200).consumed,
          "a second hold after settlement starts from the admitted value");
    const auto secondHoldPreview = state.PresentationValue(slider);
    Check(secondHoldPreview.has_value(), "second hold publishes a fresh preview");
    Near(*secondHoldPreview, 0.8,
         "second hold starts from the admitted value");
    Check(!state.TakePendingDispatch(slider, 349),
          "second hold retains the same bounded trailing delay");
    const auto secondHold = state.TakePendingDispatch(slider, 350);
    Check(secondHold.has_value(),
          "second hold dispatches after its independent settlement window");
    Near(secondHold->requestedValue, 0.8,
         "second hold dispatch preserves minimum-anchored step math");
    slider.value = 0.8;
    slider.snapshotSequence = 3;
    Check(!state.Reconcile(slider, 351).visualChanged &&
              !state.PresentationValue(slider),
          "second hold matching publication settles without a duplicate repaint");
    slider.value = 1.0;
    slider.snapshotSequence = 4;
    auto saturated = state.Adjust(slider, NavigationDirection::Right, 360);
    Check(saturated.consumed && !state.PresentationValue(slider),
          "maximum consumes without creating pending work");

    slider.busy = true;
    slider.value = 0.5;
    slider.snapshotSequence = 5;
    Check(state.Adjust(slider, NavigationDirection::Left, 370).consumed,
          "busy slider owns horizontal input");
    Check(!state.PresentationValue(slider), "busy slider cannot adjust");
    slider.busy = false;
    slider.disabled = true;
    Check(state.Adjust(slider, NavigationDirection::Left, 380).consumed &&
              !state.PresentationValue(slider),
          "disabled slider consumes without adjusting");

    auto invalid = slider;
    invalid.disabled = false;
    invalid.step = std::numeric_limits<double>::infinity();
    Check(state.Adjust(invalid, NavigationDirection::Right, 390).consumed,
          "invalid slider fails closed");
    invalid.step = 1.0;
    invalid.minimum = -std::numeric_limits<double>::max();
    invalid.maximum = std::numeric_limits<double>::max();
    invalid.value = 0.0;
    Check(state.Adjust(invalid, NavigationDirection::Right, 391).consumed &&
              !state.PresentationValue(invalid),
          "finite endpoints with an overflowing range fail closed");

    slider.disabled = false;
    slider.value = 0.0;
    slider.snapshotSequence = 6;
    (void)state.Adjust(slider, NavigationDirection::Right, 500);
    const auto expiringDispatch = state.TakePendingDispatch(slider, 650);
    Check(expiringDispatch.has_value(),
          "sent-request expiry begins only after an actual dispatch");
    Near(expiringDispatch->requestedValue, 0.2,
         "expiring dispatch carries its exact computed target");
    presentationRevision = state.presentationRevision();
    const auto expired = state.Reconcile(slider, 2'651);
    Check(expired.visualChanged && !state.PresentationValue(slider),
          "stale dispatched optimistic value expires through reconciliation");
    Check(state.presentationRevision() > presentationRevision,
          "optimistic timeout advances the presentation revision");

    slider.snapshotSequence = 7;
    (void)state.Adjust(slider, NavigationDirection::Right, 2'700);
    auto restarted = slider;
    restarted.snapshotSequence = 1;
    restarted.value = -0.4;
    const auto afterRestart = state.Adjust(restarted, NavigationDirection::Right, 2'710);
    const auto restartPreview = state.PresentationValue(restarted);
    Check(afterRestart.consumed && restartPreview.has_value(),
          "worker sequence reset starts a fresh slider session instead of wedging");
    Near(*restartPreview, -0.1,
         "worker sequence reset uses the restarted authoritative value");

    SliderInteractionState lookupOnly;
    for (std::size_t index = 0; index < 2'048; ++index) {
        auto unadjusted = slider;
        const auto id = L"unadjusted-" + std::to_wstring(index);
        unadjusted.nodeId = id;
        Check(!lookupOnly.PresentationValue(unadjusted),
              "unadjusted slider has no optimistic presentation value");
    }
    Check(lookupOnly.size() == 0,
          "presentation lookup never populates state for a maximum-size slider tree");
    auto active = slider;
    active.nodeId = L"active";
    const auto activeAdjustment = lookupOnly.Adjust(active, NavigationDirection::Right, 5'000);
    Check(activeAdjustment.consumed && lookupOnly.PresentationValue(active),
          "active slider creates transient state");
    for (std::size_t index = 0; index < 2'048; ++index) {
        auto unadjusted = slider;
        const auto id = L"later-unadjusted-" + std::to_wstring(index);
        unadjusted.nodeId = id;
        (void)lookupOnly.PresentationValue(unadjusted);
    }
    Check(lookupOnly.size() == 1 && lookupOnly.PresentationValue(active),
          "render traversal cannot evict the focused pending slider");

    for (std::size_t index = 0;
         index < SliderInteractionState::MaximumEntries + 32; ++index) {
        auto bounded = slider;
        const auto id = L"volume-" + std::to_wstring(index);
        bounded.nodeId = id;
        (void)state.Adjust(bounded, NavigationDirection::Right, 3'000 + index);
    }
    Check(state.size() == SliderInteractionState::MaximumEntries,
          "transient Slider state is hard LRU bounded");

    auto activationFirst = Slider(0.0);
    activationFirst.nodeId = L"activation-first";
    activationFirst.activationRequired = true;
    auto inactiveAdjustment = state.Adjust(
        activationFirst, NavigationDirection::Right, 4'000);
    Check(!inactiveAdjustment.consumed &&
              !state.PresentationValue(activationFirst),
          "inactive activation-first slider does not consume navigation");
    Check(state.EnterAdjustmentMode(activationFirst, 4'001),
          "A-equivalent host action enters adjustment mode");
    Check(state.AdjustmentModeActive(activationFirst),
          "entered slider reports active adjustment mode");
    auto activeModeAdjustment = state.Adjust(
        activationFirst, NavigationDirection::Right, 4'003);
    Check(activeModeAdjustment.consumed &&
              state.PresentationValue(activationFirst),
          "active activation-first slider adjusts normally");
    Check(state.ExitAdjustmentMode(activationFirst, 4'004),
          "A/B-equivalent host action exits adjustment mode");
    Check(!state.AdjustmentModeActive(activationFirst),
          "exited slider no longer reports active adjustment mode");
    Check(!state.ExitAdjustmentMode(activationFirst, 4'006),
          "inactive B leaves adjustment state untouched");
    Check(state.EnterAdjustmentMode(activationFirst, 4'007),
          "slider can re-enter adjustment mode");
    state.RetainAdjustmentMode(L"runtime-1", L"root", L"different-focus");
    Check(!state.AdjustmentModeActive(activationFirst),
          "moving focus away clears adjustment mode");
    Check(state.EnterAdjustmentMode(activationFirst, 4'009),
          "slider can enter before global teardown");
    (void)state.DeactivateAll();
    Check(!state.AdjustmentModeActive(activationFirst),
          "overlay transition clears all adjustment modes");

    state.ForgetWidget(L"runtime-1");
    Check(state.size() == 0, "runtime teardown clears transient state");

    std::cout << "SliderInteractionTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
