#pragma once
#include "ControllerNavigation.h"

namespace widgetrail::input {
// This gate is dormant for the rail. Crossing radial ownership with a held
// right stick requires neutral before either page-repeat or free scroll resumes.
class RadialRightStick final {
public:
    void UpdateOwner(bool radial, short x, short y) noexcept {
        const bool neutral = std::abs(static_cast<int>(x)) < 7849 && std::abs(static_cast<int>(y)) < 7849;
        if (radial != radial_) { radial_=radial; waitingNeutral_=!neutral; navigator_.Reset(); }
        if (neutral) waitingNeutral_=false;
    }
    [[nodiscard]] bool allowScroll() const noexcept { return !radial_ && !waitingNeutral_; }
    [[nodiscard]] std::optional<StickNavigationEvent> Page(short x, std::uint64_t now) noexcept {
        return radial_ && !waitingNeutral_ ? navigator_.UpdateEvent(x,0,now) : std::nullopt;
    }
private:
    bool radial_{};
    bool waitingNeutral_{};
    StickNavigator navigator_;
};
}
