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

void TrayYGesture::Press(
    const std::wstring_view selectedWidget,
    const bool restartEligible,
    const std::uint64_t now) noexcept {
    if (state_ != State::Idle || selectedWidget.empty()) return;
    selectedWidget_ = selectedWidget;
    pressedAt_ = now;
    state_ = restartEligible ? State::PendingRestart : State::PendingTap;
}

TrayYGestureAction TrayYGesture::Update(
    const std::wstring_view selectedWidget,
    const bool restartEligible,
    const std::uint64_t now) noexcept {
    if (state_ == State::PendingRestart) {
        if (!TargetIsCurrent(selectedWidget, restartEligible)) {
            Cancel();
            return TrayYGestureAction::None;
        }
        return CrossThreshold(now);
    }
    if (state_ == State::PendingTap && selectedWidget != selectedWidget_) Cancel();
    return TrayYGestureAction::None;
}

TrayYGestureAction TrayYGesture::Release(
    const std::wstring_view selectedWidget,
    const bool restartEligible,
    const std::uint64_t now) noexcept {
    if (state_ == State::PendingRestart) {
        if (!TargetIsCurrent(selectedWidget, restartEligible)) {
            Reset();
            return TrayYGestureAction::None;
        }
        if (CrossThreshold(now) == TrayYGestureAction::RestartSelectedWidget) {
            Reset();
            return TrayYGestureAction::RestartSelectedWidget;
        }
    }
    if (state_ == State::PendingTap && selectedWidget == selectedWidget_) {
        Reset();
        return TrayYGestureAction::ToggleReorder;
    }
    if (state_ == State::PendingRestart) {
        Reset();
        return TrayYGestureAction::ToggleReorder;
    }
    Reset();
    return TrayYGestureAction::None;
}

void TrayYGesture::Cancel() noexcept {
    if (state_ != State::Idle) state_ = State::Canceled;
}

void TrayYGesture::Reset() noexcept {
    state_ = State::Idle;
    selectedWidget_.clear();
    pressedAt_ = 0;
}

bool TrayYGesture::capturing() const noexcept {
    return state_ != State::Idle;
}

bool TrayYGesture::pendingRestart() const noexcept {
    return state_ == State::PendingRestart;
}

std::wstring_view TrayYGesture::selectedWidget() const noexcept {
    return selectedWidget_;
}

unsigned int TrayYGesture::progressPercent(const std::uint64_t now) const noexcept {
    if (state_ != State::PendingRestart) return 0;
    if (now <= pressedAt_) return 0;
    const auto elapsed = now - pressedAt_;
    if (elapsed >= kTrayWidgetRestartHoldMilliseconds) return 100;
    return static_cast<unsigned int>(
        elapsed * 100 / kTrayWidgetRestartHoldMilliseconds);
}

bool TrayYGesture::TargetIsCurrent(
    const std::wstring_view selectedWidget,
    const bool restartEligible) const noexcept {
    return restartEligible && !selectedWidget.empty() &&
           selectedWidget == selectedWidget_;
}

TrayYGestureAction TrayYGesture::CrossThreshold(const std::uint64_t now) noexcept {
    if (state_ != State::PendingRestart || now < pressedAt_ ||
        now - pressedAt_ < kTrayWidgetRestartHoldMilliseconds) {
        return TrayYGestureAction::None;
    }
    state_ = State::RestartWon;
    return TrayYGestureAction::RestartSelectedWidget;
}

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
    const auto event = UpdateEvent(x, y, now);
    return event ? std::optional{event->direction} : std::nullopt;
}

std::optional<StickNavigationEvent> StickNavigator::UpdateEvent(
    const short x, const short y, const std::uint64_t now) noexcept {
    const auto resolved = Resolve(x, y);
    if (resolved == NavigationDirection::None) {
        Reset();
        return std::nullopt;
    }
    if (resolved != direction_) {
        direction_ = resolved;
        nextRepeat_ = now + options_.initialRepeatMilliseconds;
        return StickNavigationEvent{resolved, NavigationEventPhase::Pressed};
    }
    if (nextRepeat_ != 0 && now >= nextRepeat_) {
        nextRepeat_ = now + options_.repeatMilliseconds;
        return StickNavigationEvent{resolved, NavigationEventPhase::Repeated};
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
