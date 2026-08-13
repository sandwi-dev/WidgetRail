#include "ControllerNavigation.h"

namespace gba::input {

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

} // namespace gba::input
