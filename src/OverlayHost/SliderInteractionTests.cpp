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
gba::input::SliderInputDescriptor Slider(double value = 0.0) {
    return {L"runtime-1", L"root", L"volume", L"volume.changed", 1,
            -1.0, 1.0, value, 0.3, false, false};
}
}

int main() {
    using namespace gba::input;
    SliderInteractionState state;
    auto slider = Slider();
    auto presentationRevision = state.presentationRevision();
    auto first = state.Adjust(slider, NavigationDirection::Right, 10);
    Check(first.consumed && first.requestedValue.has_value(), "right is consumed and dispatched");
    Check(state.presentationRevision() > presentationRevision,
          "first optimistic target advances the presentation revision");
    presentationRevision = state.presentationRevision();
    Near(*first.requestedValue, 0.2, "arbitrary range snaps to next minimum-anchored step");
    auto repeated = state.Adjust(slider, NavigationDirection::Right, 20);
    Check(state.presentationRevision() > presentationRevision,
          "repeated optimistic target advances the presentation revision");
    Near(*repeated.requestedValue, 0.5, "repeat advances transient target, not stale snapshot");
    Near(*state.PresentationValue(slider, 25), 0.5, "pending target drives optimistic paint");

    auto cancellationSlider = slider;
    cancellationSlider.nodeId = L"cancellation";
    cancellationSlider.minimum = 0.0;
    cancellationSlider.maximum = 1.0;
    cancellationSlider.value = 0.0;
    cancellationSlider.step = 0.1;
    auto away = state.Adjust(cancellationSlider, NavigationDirection::Right, 26);
    Near(*away.requestedValue, 0.1, "first adjustment leaves authoritative value");
    auto back = state.Adjust(cancellationSlider, NavigationDirection::Left, 27);
    Near(*back.requestedValue, 0.0, "reverse adjustment can cancel to authoritative value");
    Near(*state.PresentationValue(cancellationSlider, 28), 0.0,
         "same-sequence render cannot falsely acknowledge a pending cancellation");
    cancellationSlider.snapshotSequence = 2;
    Check(!state.PresentationValue(cancellationSlider, 29),
          "newer matching snapshot acknowledges pending cancellation");

    slider.value = 0.5;
    slider.snapshotSequence = 2;
    presentationRevision = state.presentationRevision();
    Check(!state.PresentationValue(slider, 30), "authoritative acknowledgement clears override");
    Check(state.presentationRevision() > presentationRevision,
          "authoritative acknowledgement advances the presentation revision");
    slider.value = 1.0;
    slider.snapshotSequence = 3;
    auto saturated = state.Adjust(slider, NavigationDirection::Right, 40);
    Check(saturated.consumed && !saturated.requestedValue, "maximum consumes without dispatch");

    slider.busy = true;
    slider.value = 0.5;
    slider.snapshotSequence = 4;
    Check(state.Adjust(slider, NavigationDirection::Left, 50).consumed,
          "busy slider owns horizontal input");
    Check(!state.Adjust(slider, NavigationDirection::Left, 50).requestedValue,
          "busy slider cannot adjust");
    slider.busy = false;
    slider.disabled = true;
    Check(!state.Adjust(slider, NavigationDirection::Left, 60).requestedValue,
          "disabled slider cannot adjust");

    auto invalid = slider;
    invalid.disabled = false;
    invalid.step = std::numeric_limits<double>::infinity();
    Check(state.Adjust(invalid, NavigationDirection::Right, 70).consumed,
          "invalid slider fails closed");
    invalid.step = 1.0;
    invalid.minimum = -std::numeric_limits<double>::max();
    invalid.maximum = std::numeric_limits<double>::max();
    invalid.value = 0.0;
    Check(!state.Adjust(invalid, NavigationDirection::Right, 71).requestedValue,
          "finite endpoints with an overflowing range fail closed");

    slider.disabled = false;
    slider.value = 0.0;
    slider.snapshotSequence = 5;
    (void)state.Adjust(slider, NavigationDirection::Right, 100);
    presentationRevision = state.presentationRevision();
    Check(!state.PresentationValue(slider, 2'101), "stale optimistic value expires");
    Check(state.presentationRevision() > presentationRevision,
          "optimistic timeout advances the presentation revision");

    slider.snapshotSequence = 6;
    (void)state.Adjust(slider, NavigationDirection::Right, 2'200);
    auto restarted = slider;
    restarted.snapshotSequence = 1;
    restarted.value = -0.4;
    const auto afterRestart = state.Adjust(restarted, NavigationDirection::Right, 2'210);
    Check(afterRestart.requestedValue.has_value(),
          "worker sequence reset starts a fresh slider session instead of wedging");
    Near(*afterRestart.requestedValue, -0.1,
         "worker sequence reset uses the restarted authoritative value");

    SliderInteractionState lookupOnly;
    for (std::size_t index = 0; index < 2'048; ++index) {
        auto unadjusted = slider;
        const auto id = L"unadjusted-" + std::to_wstring(index);
        unadjusted.nodeId = id;
        Check(!lookupOnly.PresentationValue(unadjusted, 2'300 + index),
              "unadjusted slider has no optimistic presentation value");
    }
    Check(lookupOnly.size() == 0,
          "presentation lookup never populates state for a maximum-size slider tree");
    auto active = slider;
    active.nodeId = L"active";
    const auto activeAdjustment = lookupOnly.Adjust(active, NavigationDirection::Right, 5'000);
    Check(activeAdjustment.requestedValue.has_value(), "active slider creates transient state");
    for (std::size_t index = 0; index < 2'048; ++index) {
        auto unadjusted = slider;
        const auto id = L"later-unadjusted-" + std::to_wstring(index);
        unadjusted.nodeId = id;
        (void)lookupOnly.PresentationValue(unadjusted, 5'001 + index);
    }
    Check(lookupOnly.size() == 1 && lookupOnly.PresentationValue(active, 5'100),
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
    Check(!inactiveAdjustment.consumed && !inactiveAdjustment.requestedValue,
          "inactive activation-first slider does not consume navigation");
    Check(state.EnterAdjustmentMode(activationFirst, 4'001),
          "A-equivalent host action enters adjustment mode");
    Check(state.AdjustmentModeActive(activationFirst, 4'002),
          "entered slider reports active adjustment mode");
    auto activeModeAdjustment = state.Adjust(
        activationFirst, NavigationDirection::Right, 4'003);
    Check(activeModeAdjustment.consumed && activeModeAdjustment.requestedValue,
          "active activation-first slider adjusts normally");
    Check(state.ExitAdjustmentMode(activationFirst, 4'004),
          "A/B-equivalent host action exits adjustment mode");
    Check(!state.AdjustmentModeActive(activationFirst, 4'005),
          "exited slider no longer reports active adjustment mode");
    Check(!state.ExitAdjustmentMode(activationFirst, 4'006),
          "inactive B leaves adjustment state untouched");
    Check(state.EnterAdjustmentMode(activationFirst, 4'007),
          "slider can re-enter adjustment mode");
    state.RetainAdjustmentMode(L"runtime-1", L"root", L"different-focus");
    Check(!state.AdjustmentModeActive(activationFirst, 4'008),
          "moving focus away clears adjustment mode");
    Check(state.EnterAdjustmentMode(activationFirst, 4'009),
          "slider can enter before global teardown");
    state.DeactivateAll();
    Check(!state.AdjustmentModeActive(activationFirst, 4'010),
          "overlay transition clears all adjustment modes");

    state.ForgetWidget(L"runtime-1");
    Check(state.size() == 0, "runtime teardown clears transient state");

    std::cout << "SliderInteractionTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
