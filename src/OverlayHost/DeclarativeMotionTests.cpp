#include "DeclarativeMotion.h"

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string>
#include <string_view>

namespace {

int checks = 0;

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(const float actual, const float expected, const std::string_view message) {
    Check(std::abs(actual - expected) <= 0.015F, message);
}

gba::DeclarativeMotionSample Resolve(
    gba::DeclarativeMotionTimeline& timeline,
    const std::uint64_t now,
    const std::wstring_view key,
    const gba::DeclarativeMotionValue target,
    const float duration = 100.0F,
    const gba::NativeTransitionEasing easing = gba::NativeTransitionEasing::Linear,
    const bool reducedMotion = false) {
    timeline.BeginFrame(now);
    const auto sample = timeline.Resolve(
        key, target, duration, easing, reducedMotion);
    (void)timeline.EndFrame();
    return sample;
}

void InterpolatesAndSettlesWithoutIdleWork() {
    gba::DeclarativeMotionTimeline timeline;
    auto sample = Resolve(timeline, 0, L"widget\x1fplay", {0.5F, 1.0F});
    Near(sample.value.opacity, 0.5F, "first observation snaps to target");
    Check(!sample.active, "first observation does not manufacture an animation");

    sample = Resolve(timeline, 10, L"widget\x1fplay", {1.0F, 1.2F});
    Near(sample.value.opacity, 0.5F, "transition starts at previous opacity");
    Near(sample.value.scale, 1.0F, "transition starts at previous scale");
    Check(sample.active, "changed focus target starts animation");

    sample = Resolve(timeline, 60, L"widget\x1fplay", {1.0F, 1.2F});
    Near(sample.value.opacity, 0.75F, "linear opacity interpolates at midpoint");
    Near(sample.value.scale, 1.1F, "linear scale interpolates at midpoint");
    Check(sample.active, "midpoint requests another frame");

    sample = Resolve(timeline, 110, L"widget\x1fplay", {1.0F, 1.2F});
    Near(sample.value.opacity, 1.0F, "completed opacity reaches target");
    Near(sample.value.scale, 1.2F, "completed scale reaches target");
    Check(!sample.active, "settled timeline does not request an animation loop");
}

void RetargetsFromPresentedValue() {
    gba::DeclarativeMotionTimeline timeline;
    (void)Resolve(timeline, 0, L"widget\x1fitem", {0.0F, 1.0F});
    (void)Resolve(timeline, 10, L"widget\x1fitem", {1.0F, 1.2F});
    auto sample = Resolve(timeline, 60, L"widget\x1fitem", {0.0F, 0.8F});
    Near(sample.value.opacity, 0.5F, "retarget begins at visible opacity");
    Near(sample.value.scale, 1.1F, "retarget begins at visible scale");
    Check(sample.active, "interrupted transition remains active");

    sample = Resolve(timeline, 110, L"widget\x1fitem", {0.0F, 0.8F});
    Near(sample.value.opacity, 0.25F, "retargeted opacity has no discontinuity");
    Near(sample.value.scale, 0.95F, "retargeted scale has no discontinuity");
}

void RemovesMissingNodesAtFrameBoundary() {
    gba::DeclarativeMotionTimeline timeline;
    timeline.BeginFrame(0);
    (void)timeline.Resolve(L"widget\x1fone", {1.0F, 1.0F}, 100.0F,
                           gba::NativeTransitionEasing::Linear);
    (void)timeline.Resolve(L"widget\x1ftwo", {1.0F, 1.0F}, 100.0F,
                           gba::NativeTransitionEasing::Linear);
    (void)timeline.EndFrame();
    Check(timeline.trackedNodeCount() == 2, "complete tree is tracked");

    timeline.BeginFrame(16);
    (void)timeline.Resolve(L"widget\x1ftwo", {1.0F, 1.0F}, 100.0F,
                           gba::NativeTransitionEasing::Linear);
    (void)timeline.EndFrame();
    Check(timeline.trackedNodeCount() == 1, "removed node is swept after frame");

    timeline.ForgetPrefix(L"widget\x1f");
    Check(timeline.trackedNodeCount() == 0, "widget replacement clears its timeline");
}

void ReducedMotionSnapsAndCancels() {
    gba::DeclarativeMotionTimeline timeline;
    (void)Resolve(timeline, 0, L"widget\x1fbutton", {0.4F, 1.0F});
    auto sample = Resolve(
        timeline, 10, L"widget\x1fbutton", {1.0F, 1.1F}, 2000.0F,
        gba::NativeTransitionEasing::Spring, true);
    Near(sample.value.opacity, 1.0F, "reduced motion snaps opacity");
    Near(sample.value.scale, 1.1F, "reduced motion snaps scale");
    Check(!sample.active, "reduced motion never schedules another frame");

    sample = Resolve(
        timeline, 20, L"widget\x1fbutton", {0.2F, 0.9F}, 100.0F,
        gba::NativeTransitionEasing::Linear, false);
    Check(sample.active, "normal motion can restart after preference change");
    sample = Resolve(
        timeline, 30, L"widget\x1fbutton", {0.2F, 0.9F}, 100.0F,
        gba::NativeTransitionEasing::Linear, true);
    Near(sample.value.opacity, 0.2F, "enabling reduced motion cancels active opacity");
    Near(sample.value.scale, 0.9F, "enabling reduced motion cancels active scale");
    Check(!sample.active, "cancelled motion is settled");
}

void BoundsUntrustedInputsAndCapacity() {
    gba::DeclarativeMotionTimeline timeline;
    timeline.BeginFrame(0);
    for (std::size_t index = 0;
         index < gba::DeclarativeMotionTimeline::MaximumTrackedNodes + 20;
         ++index) {
        (void)timeline.Resolve(
            L"widget\x1f" + std::to_wstring(index),
            {NAN, INFINITY}, 999999.0F,
            gba::NativeTransitionEasing::Spring);
    }
    (void)timeline.EndFrame();
    Check(timeline.trackedNodeCount() ==
              gba::DeclarativeMotionTimeline::MaximumTrackedNodes,
          "untrusted tree cannot grow timeline beyond capacity");
}

} // namespace

int main() {
    InterpolatesAndSettlesWithoutIdleWork();
    RetargetsFromPresentedValue();
    RemovesMissingNodesAtFrameBoundary();
    ReducedMotionSnapsAndCancels();
    BoundsUntrustedInputsAndCapacity();
    std::cout << "DeclarativeMotionTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
