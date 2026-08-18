#pragma once

#include "NativeStyle.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <unordered_map>

namespace widgetrail {

/// Bounded presentation values that change without invalidating declarative
/// layout. Translation is consumed by the renderer's presentation-geometry
/// pass so translated paint, clipping, pointer, and controller geometry remain
/// identical.
struct DeclarativeMotionValue final {
    float opacity{1.0F};
    float scale{1.0F};
    float translationX{};
    float translationY{};
    friend bool operator==(const DeclarativeMotionValue&,
                           const DeclarativeMotionValue&) = default;
};

struct DeclarativeMotionSample final {
    DeclarativeMotionValue value;
    bool active{};
};

/// A bounded, deterministic timeline for stable declarative node identities.
/// The host supplies monotonic frame timestamps; this class never owns a
/// thread, timer, or callback and therefore cannot create idle work.
class DeclarativeMotionTimeline final {
public:
    static constexpr std::size_t MaximumTrackedNodes = 1024;
    static constexpr std::uint64_t MaximumDurationMilliseconds = 2000;
    static constexpr float MaximumTranslationDips = 4096.0F;

    /// Starts a complete tree observation at the supplied monotonic time.
    void BeginFrame(std::uint64_t timestampMilliseconds) noexcept;

    /// Resolves the presentation value for one stable node. A node's first
    /// observation snaps to its authored target; later target changes retarget
    /// from the value visible at this frame, avoiding discontinuities.
    [[nodiscard]] DeclarativeMotionSample Resolve(
        std::wstring_view stableNodeKey,
        DeclarativeMotionValue target,
        float durationMilliseconds,
        NativeTransitionEasing easing,
        bool reducedMotion = false);

    /// Completes the tree observation, removing nodes that disappeared. The
    /// result is true only while at least one seen node needs another frame.
    [[nodiscard]] bool EndFrame() noexcept;

    /// Drops entries for a removed/replaced widget instance.
    void ForgetPrefix(std::wstring_view stablePrefix) noexcept;
    void Clear() noexcept;

    [[nodiscard]] std::size_t trackedNodeCount() const noexcept {
        return entries_.size();
    }

private:
    struct Entry final {
        DeclarativeMotionValue from;
        DeclarativeMotionValue target;
        std::uint64_t startedAt{};
        std::uint64_t duration{};
        std::uint64_t seenGeneration{};
        NativeTransitionEasing easing{NativeTransitionEasing::EaseOut};
        bool active{};
    };

    [[nodiscard]] static DeclarativeMotionValue Sanitize(
        DeclarativeMotionValue value) noexcept;
    [[nodiscard]] static DeclarativeMotionSample Sample(
        Entry& entry,
        std::uint64_t timestampMilliseconds) noexcept;

    std::unordered_map<std::wstring, Entry> entries_;
    std::uint64_t timestampMilliseconds_{};
    std::uint64_t lastTimestampMilliseconds_{};
    std::uint64_t generation_{};
    bool frameActive_{};
};

} // namespace widgetrail
