#include "OverlayTransition.h"

#include <algorithm>
#include <cmath>
#include <utility>

namespace gba {
namespace {

[[nodiscard]] float SanitizeOpacity(const float value) noexcept {
    return std::isfinite(value) ? std::clamp(value, 0.0F, 1.0F) : 1.0F;
}

} // namespace

void OverlayTransitionTimeline::BeginOpen(
    const std::uint64_t timestampMilliseconds,
    const bool reducedMotion) noexcept {
    (void)Sample(timestampMilliseconds, reducedMotion);
    closeRequested_ = false;
    hideCompletionReady_ = false;
    Start(shell_, shell_.value, 1.0F, OpenDurationMilliseconds,
          Easing::EaseOut, reducedMotion);
}

void OverlayTransitionTimeline::PrepareInitialOpen() noexcept {
    shell_.from = 0.0F;
    shell_.target = 0.0F;
    shell_.value = 0.0F;
    shell_.duration = 0;
    shell_.active = false;
    closeRequested_ = false;
    hideCompletionReady_ = false;
}

void OverlayTransitionTimeline::BeginClose(
    const std::uint64_t timestampMilliseconds,
    const bool reducedMotion) noexcept {
    (void)Sample(timestampMilliseconds, reducedMotion);
    closeRequested_ = true;
    hideCompletionReady_ = false;
    SnapContentVisible();
    Start(shell_, shell_.value, 0.0F, CloseDurationMilliseconds,
          Easing::EaseIn, reducedMotion);
    CompleteCloseIfReady();
}

void OverlayTransitionTimeline::BeginContentReveal(
    const std::uint64_t timestampMilliseconds,
    const bool reducedMotion) noexcept {
    (void)Sample(timestampMilliseconds, reducedMotion);
    // A genuinely new reveal starts subtly below full opacity. If another
    // identity arrives while that reveal is already running, preserve the
    // presented value rather than visibly jumping backwards to the constant.
    const float from = content_.active
        ? content_.value
        : ContentRevealStartOpacity;
    Start(content_, from, 1.0F,
           ContentDurationMilliseconds, Easing::EaseOut, reducedMotion);
}

void OverlayTransitionTimeline::SnapContentVisible() noexcept {
    content_.from = 1.0F;
    content_.target = 1.0F;
    content_.value = 1.0F;
    content_.duration = 0;
    content_.active = false;
}

OverlayTransitionSample OverlayTransitionTimeline::Sample(
    const std::uint64_t timestampMilliseconds,
    const bool reducedMotion) noexcept {
    timestampMilliseconds_ = std::max(timestampMilliseconds_, timestampMilliseconds);
    if (reducedMotion) {
        shell_.value = shell_.target;
        shell_.from = shell_.target;
        shell_.duration = 0;
        shell_.active = false;
        content_.value = content_.target;
        content_.from = content_.target;
        content_.duration = 0;
        content_.active = false;
    } else {
        (void)Advance(shell_);
        (void)Advance(content_);
    }
    CompleteCloseIfReady();
    return {
        SanitizeOpacity(shell_.value),
        SanitizeOpacity(content_.value),
        shell_.active || content_.active,
        shell_.active,
        content_.active,
    };
}

bool OverlayTransitionTimeline::TakeHideCompletion() noexcept {
    return std::exchange(hideCompletionReady_, false);
}

void OverlayTransitionTimeline::Start(
    Track& track,
    const float from,
    const float target,
    const std::uint64_t durationMilliseconds,
    const Easing easing,
    const bool reducedMotion) noexcept {
    track.from = SanitizeOpacity(from);
    track.target = SanitizeOpacity(target);
    track.value = reducedMotion ? track.target : track.from;
    track.startedAt = timestampMilliseconds_;
    track.duration = reducedMotion
        ? 0
        : std::clamp<std::uint64_t>(
            durationMilliseconds, 1, MaximumDurationMilliseconds);
    track.easing = easing;
    track.active = track.duration != 0 &&
        std::abs(track.from - track.target) > 0.0001F;
    if (!track.active) track.value = track.target;
}

float OverlayTransitionTimeline::Advance(Track& track) noexcept {
    if (!track.active || track.duration == 0) return track.value;
    const auto elapsed = timestampMilliseconds_ >= track.startedAt
        ? timestampMilliseconds_ - track.startedAt
        : std::uint64_t{0};
    if (elapsed >= track.duration) {
        track.value = track.target;
        track.from = track.target;
        track.active = false;
        return track.value;
    }
    const auto linear = std::clamp(
        static_cast<float>(elapsed) / static_cast<float>(track.duration),
        0.0F, 1.0F);
    const auto inverse = 1.0F - linear;
    const auto progress = track.easing == Easing::EaseIn
        ? linear * linear * linear
        : 1.0F - inverse * inverse * inverse;
    track.value = track.from + (track.target - track.from) * progress;
    return track.value;
}

void OverlayTransitionTimeline::CompleteCloseIfReady() noexcept {
    if (!closeRequested_ || shell_.active || shell_.target != 0.0F) return;
    shell_.value = 0.0F;
    closeRequested_ = false;
    hideCompletionReady_ = true;
}

bool ShouldRevealWidgetContent(
    const bool wasWidgetSurface,
    const std::wstring_view previousWidgetId,
    const bool isWidgetSurface,
    const std::wstring_view currentWidgetId,
    const bool runtimeReplaced) noexcept {
    return isWidgetSurface && !currentWidgetId.empty() &&
        (!wasWidgetSurface || previousWidgetId != currentWidgetId || runtimeReplaced);
}

bool ShouldSnapWidgetContentVisible(
    const bool isWidgetSurface,
    const bool wasWidgetFocused,
    const bool isWidgetFocused) noexcept {
    return isWidgetSurface && !wasWidgetFocused && isWidgetFocused;
}

} // namespace gba
