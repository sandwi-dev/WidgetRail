#pragma once

#include <cstdint>
#include <string_view>

namespace gba {

struct OverlayTransitionSample final {
    float shellOpacity{};
    float contentOpacity{1.0F};
    bool active{};
    bool shellActive{};
    bool contentActive{};
};

/// Pure, bounded shell/content transition state. The Win32 host supplies a
/// monotonic timestamp and owns the one existing visible-controller timer;
/// this type never owns a thread, timer, callback, HWND, or render resource.
class OverlayTransitionTimeline final {
public:
    static constexpr std::uint64_t MaximumDurationMilliseconds = 200;
    static constexpr std::uint64_t OpenDurationMilliseconds = 140;
    static constexpr std::uint64_t CloseDurationMilliseconds = 100;
    static constexpr std::uint64_t ContentDurationMilliseconds = 100;
    static constexpr float ContentRevealStartOpacity = 0.78F;
    static_assert(OpenDurationMilliseconds <= MaximumDurationMilliseconds);
    static_assert(CloseDurationMilliseconds <= MaximumDurationMilliseconds);
    static_assert(ContentDurationMilliseconds <= MaximumDurationMilliseconds);

    void BeginOpen(std::uint64_t timestampMilliseconds, bool reducedMotion) noexcept;
    /// Holds a newly shown HWND fully transparent until the host confirms that
    /// its first D2D frame was committed successfully. This also cancels a
    /// pending close completion so a delayed paint cannot hide a reopened HWND.
    void PrepareInitialOpen() noexcept;
    void BeginClose(std::uint64_t timestampMilliseconds, bool reducedMotion) noexcept;
    void BeginContentReveal(
        std::uint64_t timestampMilliseconds,
        bool reducedMotion) noexcept;
    void SnapContentVisible() noexcept;

    [[nodiscard]] OverlayTransitionSample Sample(
        std::uint64_t timestampMilliseconds,
        bool reducedMotion) noexcept;

    /// Becomes true exactly once after a requested close reaches zero opacity.
    /// Reopening clears a pending completion before it can hide the HWNDs.
    [[nodiscard]] bool TakeHideCompletion() noexcept;

    [[nodiscard]] bool active() const noexcept {
        return shell_.active || content_.active;
    }

private:
    enum class Easing {
        EaseIn,
        EaseOut,
    };

    struct Track final {
        float from{};
        float target{};
        float value{};
        std::uint64_t startedAt{};
        std::uint64_t duration{};
        Easing easing{Easing::EaseOut};
        bool active{};
    };

    void Start(
        Track& track,
        float from,
        float target,
        std::uint64_t durationMilliseconds,
        Easing easing,
        bool reducedMotion) noexcept;
    [[nodiscard]] float Advance(Track& track) noexcept;
    void CompleteCloseIfReady() noexcept;

    Track shell_{};
    Track content_{1.0F, 1.0F, 1.0F};
    std::uint64_t timestampMilliseconds_{};
    bool closeRequested_{};
    bool hideCompletionReady_{};
};

/// Widget content reveals are identity transitions, not ordinary immutable
/// snapshot updates. A sequence-only refresh must never flash the surface.
[[nodiscard]] bool ShouldRevealWidgetContent(
    bool wasWidgetSurface,
    std::wstring_view previousWidgetId,
    bool isWidgetSurface,
    std::wstring_view currentWidgetId,
    bool runtimeReplaced = false) noexcept;

[[nodiscard]] bool ShouldSnapWidgetContentVisible(
    bool isWidgetSurface,
    bool wasWidgetFocused,
    bool isWidgetFocused) noexcept;

} // namespace gba
