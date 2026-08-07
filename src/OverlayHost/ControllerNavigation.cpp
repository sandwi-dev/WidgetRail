#include "ControllerNavigation.h"

#include <algorithm>
#include <cmath>

namespace gba::input {
namespace {

int Magnitude(const short value) noexcept {
    return std::abs(static_cast<int>(value));
}

bool SameDirection(
    const NavigationDirection direction, const short x, const short y) noexcept {
    switch (direction) {
    case NavigationDirection::Left: return x < 0;
    case NavigationDirection::Right: return x > 0;
    case NavigationDirection::Up: return y > 0;
    case NavigationDirection::Down: return y < 0;
    default: return false;
    }
}

bool Horizontal(const NavigationDirection direction) noexcept {
    return direction == NavigationDirection::Left ||
           direction == NavigationDirection::Right;
}

} // namespace

StickNavigator::StickNavigator(StickNavigationOptions options) noexcept
    : options_(options) {
    options_.engageThreshold = std::clamp(options_.engageThreshold, 1, 32'767);
    options_.releaseThreshold = std::clamp(
        options_.releaseThreshold, 0, options_.engageThreshold);
    options_.initialRepeatMilliseconds = std::max<std::uint64_t>(
        1, options_.initialRepeatMilliseconds);
    options_.repeatMilliseconds = std::max<std::uint64_t>(
        1, options_.repeatMilliseconds);
}

void StickNavigator::Prime(const short x, const short y, const std::uint64_t now) noexcept {
    direction_ = Resolve(x, y);
    nextRepeat_ = direction_ == NavigationDirection::None
        ? 0
        : now + options_.initialRepeatMilliseconds;
}

std::optional<NavigationDirection> StickNavigator::Update(
    const short x, const short y, const std::uint64_t now) noexcept {
    const auto resolved = Resolve(x, y);
    if (resolved == NavigationDirection::None) {
        Reset();
        return std::nullopt;
    }
    if (resolved != direction_) {
        direction_ = resolved;
        nextRepeat_ = now + options_.initialRepeatMilliseconds;
        return resolved;
    }
    if (nextRepeat_ != 0 && now >= nextRepeat_) {
        nextRepeat_ = now + options_.repeatMilliseconds;
        return resolved;
    }
    return std::nullopt;
}

void StickNavigator::Reset() noexcept {
    direction_ = NavigationDirection::None;
    nextRepeat_ = 0;
}

NavigationDirection StickNavigator::Resolve(const short x, const short y) const noexcept {
    const auto absX = Magnitude(x);
    const auto absY = Magnitude(y);
    if (direction_ != NavigationDirection::None && SameDirection(direction_, x, y)) {
        const auto primary = Horizontal(direction_) ? absX : absY;
        const auto perpendicular = Horizontal(direction_) ? absY : absX;
        // Keep the engaged axis through small diagonal/noisy motion. A clear
        // 25% dominant perpendicular gesture intentionally changes direction.
        if (primary >= options_.releaseThreshold &&
            !(perpendicular >= options_.engageThreshold &&
              perpendicular * 4 > primary * 5)) {
            return direction_;
        }
    }
    if (std::max(absX, absY) < options_.engageThreshold) {
        return NavigationDirection::None;
    }
    if (absY > absX) {
        return y < 0 ? NavigationDirection::Down : NavigationDirection::Up;
    }
    return x < 0 ? NavigationDirection::Left : NavigationDirection::Right;
}

} // namespace gba::input
