#pragma once

#include "OverlayPlatformInterop.h"
#include "ControllerNavigation.h"

#include <cstdint>

namespace widgetrail::platform {

inline constexpr std::uint64_t kGuideDebounceMilliseconds = 150;
// Compatibility polling allows a 250 ms driver call and a result up to 250 ms
// old. A second API can therefore report the same press after the ordinary
// debounce window. Keep rapid presses from one source responsive.
inline constexpr std::uint64_t kGuideCrossSourceDebounceMilliseconds = 500;

class GuideToggleDebouncer final {
public:
    [[nodiscard]] bool Accept(std::uint64_t nowMilliseconds,
        WidgetRailOverlayPlatformGuideSource source = WidgetRailOverlayPlatformGuideSource::None) noexcept;
    void Reset() noexcept;

private:
    std::uint64_t lastAcceptedAt_{};
    bool hasAccepted_{};
    WidgetRailOverlayPlatformGuideSource lastSource_{WidgetRailOverlayPlatformGuideSource::None};
};

class ControllerFrameTracker final {
public:
    // State publication is idempotent. Only a real hide/focus-loss transition
    // retires edges; a visible background read lease must keep tracking input.
    void ApplyWindowStateTransition(bool wasVisible, bool wasFocused,
        bool visible, bool focused) noexcept;
    void Prime(
        bool connected,
        const WidgetRailOverlayPlatformRawControllerState& state,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] WidgetRailOverlayPlatformControllerFrame Update(
        bool connected,
        const WidgetRailOverlayPlatformRawControllerState& state,
        std::uint64_t nowMilliseconds,
        bool allowNavigationRepeat = true) noexcept;
    void Reset() noexcept;
    [[nodiscard]] bool primed() const noexcept { return primed_; }

private:
    bool primed_{};
    std::uint16_t previousButtons_{};
    bool leftTriggerPressed_{};
    bool rightTriggerPressed_{};
    bool recoveryChordHeld_{};
    widgetrail::input::StickNavigator stickNavigator_;
    widgetrail::input::StickNavigator dpadNavigator_{
        widgetrail::input::StickNavigationOptions{1, 0, 360, 125}};
};

} // namespace widgetrail::platform
