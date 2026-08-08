#include "DeclarativeMotion.h"

#include <algorithm>
#include <cmath>

namespace gba {
namespace {

constexpr float kTargetEpsilon = 0.0001F;

[[nodiscard]] bool Near(const float left, const float right) noexcept {
    return std::abs(left - right) <= kTargetEpsilon;
}

[[nodiscard]] bool Near(
    const DeclarativeMotionValue& left,
    const DeclarativeMotionValue& right) noexcept {
    return Near(left.opacity, right.opacity) && Near(left.scale, right.scale);
}

[[nodiscard]] float Ease(
    const float progress,
    const NativeTransitionEasing easing) noexcept {
    const float value = std::clamp(progress, 0.0F, 1.0F);
    switch (easing) {
    case NativeTransitionEasing::Linear:
        return value;
    case NativeTransitionEasing::EaseInOut:
        // Smoothstep has zero velocity at both ends and cannot overshoot.
        return value * value * (3.0F - 2.0F * value);
    case NativeTransitionEasing::Spring: {
        // A normalized critically damped response preserves the authored
        // spring character without overshoot, unbounded duration, or a hidden
        // settle tail that would keep the render loop alive.
        constexpr float response = 6.0F;
        const float raw = 1.0F - (1.0F + response * value) *
            std::exp(-response * value);
        const float end = 1.0F - (1.0F + response) * std::exp(-response);
        return end > 0.0F ? std::clamp(raw / end, 0.0F, 1.0F) : value;
    }
    case NativeTransitionEasing::EaseOut:
    default: {
        const float inverse = 1.0F - value;
        return 1.0F - inverse * inverse * inverse;
    }
    }
}

[[nodiscard]] float Interpolate(
    const float from,
    const float to,
    const float progress) noexcept {
    return from + (to - from) * progress;
}

} // namespace

void DeclarativeMotionTimeline::BeginFrame(
    const std::uint64_t timestampMilliseconds) noexcept {
    // A bad/test clock cannot reverse a live animation or underflow elapsed
    // time. Equal timestamps remain valid for atomic retargeting.
    timestampMilliseconds_ = std::max(
        timestampMilliseconds, lastTimestampMilliseconds_);
    lastTimestampMilliseconds_ = timestampMilliseconds_;
    ++generation_;
    if (generation_ == 0) {
        // The generation is only a mark-and-sweep token. Resetting after an
        // effectively unreachable wrap is safer than accidentally retaining
        // every historical node.
        generation_ = 1;
        for (auto& [_, entry] : entries_) entry.seenGeneration = 0;
    }
    frameActive_ = false;
}

DeclarativeMotionSample DeclarativeMotionTimeline::Resolve(
    const std::wstring_view stableNodeKey,
    DeclarativeMotionValue target,
    const float durationMilliseconds,
    const NativeTransitionEasing easing,
    const bool reducedMotion) {
    target = Sanitize(target);
    if (stableNodeKey.empty() || stableNodeKey.size() > 1024) return {target, false};

    auto found = entries_.find(std::wstring{stableNodeKey});
    if (found == entries_.end()) {
        if (entries_.size() >= MaximumTrackedNodes) return {target, false};
        Entry entry;
        entry.from = target;
        entry.target = target;
        entry.startedAt = timestampMilliseconds_;
        entry.seenGeneration = generation_;
        found = entries_.emplace(std::wstring{stableNodeKey}, entry).first;
        return {target, false};
    }

    auto& entry = found->second;
    entry.seenGeneration = generation_;
    auto current = Sample(entry, timestampMilliseconds_);
    if (!Near(entry.target, target)) {
        const auto boundedDuration = reducedMotion ||
                !std::isfinite(durationMilliseconds) || durationMilliseconds <= 0.0F
            ? std::uint64_t{0}
            : static_cast<std::uint64_t>(std::llround(std::clamp(
                durationMilliseconds,
                1.0F,
                static_cast<float>(MaximumDurationMilliseconds))));
        entry.from = current.value;
        entry.target = target;
        entry.startedAt = timestampMilliseconds_;
        entry.duration = boundedDuration;
        entry.easing = easing;
        entry.active = boundedDuration != 0 && !Near(entry.from, entry.target);
        if (!entry.active) entry.from = entry.target;
        current = {entry.active ? entry.from : entry.target, entry.active};
    } else if (reducedMotion && entry.active) {
        entry.from = entry.target;
        entry.startedAt = timestampMilliseconds_;
        entry.duration = 0;
        entry.active = false;
        current = {entry.target, false};
    }

    frameActive_ = frameActive_ || current.active;
    return current;
}

bool DeclarativeMotionTimeline::EndFrame() noexcept {
    std::erase_if(entries_, [&](const auto& item) {
        return item.second.seenGeneration != generation_;
    });
    return frameActive_;
}

void DeclarativeMotionTimeline::ForgetPrefix(
    const std::wstring_view stablePrefix) noexcept {
    if (stablePrefix.empty()) return;
    std::erase_if(entries_, [&](const auto& item) {
        return item.first.starts_with(stablePrefix);
    });
}

void DeclarativeMotionTimeline::Clear() noexcept {
    entries_.clear();
    frameActive_ = false;
}

DeclarativeMotionValue DeclarativeMotionTimeline::Sanitize(
    DeclarativeMotionValue value) noexcept {
    value.opacity = std::isfinite(value.opacity)
        ? std::clamp(value.opacity, 0.0F, 1.0F)
        : 1.0F;
    value.scale = std::isfinite(value.scale)
        ? std::clamp(value.scale, 0.5F, 2.0F)
        : 1.0F;
    return value;
}

DeclarativeMotionSample DeclarativeMotionTimeline::Sample(
    Entry& entry,
    const std::uint64_t timestampMilliseconds) noexcept {
    if (!entry.active || entry.duration == 0) return {entry.target, false};
    const auto elapsed = timestampMilliseconds >= entry.startedAt
        ? timestampMilliseconds - entry.startedAt
        : std::uint64_t{0};
    if (elapsed >= entry.duration) {
        entry.from = entry.target;
        entry.active = false;
        return {entry.target, false};
    }
    const float progress = Ease(
        static_cast<float>(elapsed) / static_cast<float>(entry.duration),
        entry.easing);
    return {{
        Interpolate(entry.from.opacity, entry.target.opacity, progress),
        Interpolate(entry.from.scale, entry.target.scale, progress),
    }, true};
}

} // namespace gba
