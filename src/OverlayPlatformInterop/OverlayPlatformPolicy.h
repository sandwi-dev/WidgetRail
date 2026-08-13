#pragma once

#include "OverlayPlatformInterop.h"
#include "../OverlayHost/ControllerNavigation.h"

#include <cstdint>

namespace gba::platform {

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
        const GbaOverlayPlatformRawControllerState& state,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] GbaOverlayPlatformControllerFrame Update(
        bool connected,
        const GbaOverlayPlatformRawControllerState& state,
        std::uint64_t nowMilliseconds) noexcept;
    void Reset() noexcept;
    [[nodiscard]] bool primed() const noexcept { return primed_; }

private:
    bool primed_{};
    std::uint16_t previousButtons_{};
    bool leftTriggerPressed_{};
    bool rightTriggerPressed_{};
    bool recoveryChordHeld_{};
    gba::input::StickNavigator stickNavigator_;
    gba::input::StickNavigator dpadNavigator_{
        gba::input::StickNavigationOptions{1, 0, 360, 125}};
};

} // namespace gba::platform
