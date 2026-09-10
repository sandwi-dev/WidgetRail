#pragma once

#include "OverlayPlatformInterop.h"
#include "../OverlayHost/ControllerNavigation.h"

#include <cstdint>

namespace widgetrail::platform {

inline constexpr std::uint64_t kGuideDebounceMilliseconds = 150;

class GuideToggleDebouncer final {
public:
    [[nodiscard]] bool Accept(std::uint64_t nowMilliseconds) noexcept;
    void Reset() noexcept;

private:
    std::uint64_t lastAcceptedAt_{};
    bool hasAccepted_{};
};

class ControllerFrameTracker final {
public:
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
