#include "ControllerNavigation.h"

#include <algorithm>
#include <cmath>

namespace widgetrail::input {

namespace {

constexpr int kRightStickScrollDeadZone = 8'000;
constexpr float kRightStickMaximumScrollRateDipPerSecond = 2'200.0F;
constexpr std::uint64_t kRightStickInitialSampleMilliseconds = 16;
constexpr std::uint64_t kRightStickMaximumSampleMilliseconds = 50;
constexpr float kRightStickAxisSwitchRatio = 1.25F;

} // namespace

RightStickScrollUpdate RightStickScrollKinetics::Update(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    const int horizontal = std::abs(static_cast<int>(x));
    const int vertical = std::abs(static_cast<int>(y));
    if (horizontal <= kRightStickScrollDeadZone &&
        vertical <= kRightStickScrollDeadZone) {
        const bool returnedToDeadZone = moving_;
        moving_ = false;
        axis_ = FreeScrollAxis::None;
        lastSampleAt_ = now;
        return {FreeScrollAxis::None, 0.0F, false, returnedToDeadZone};
    }

    FreeScrollAxis candidate = horizontal >= vertical
        ? FreeScrollAxis::Horizontal
        : FreeScrollAxis::Vertical;
    if (axis_ == FreeScrollAxis::Horizontal &&
        static_cast<float>(vertical) <
            static_cast<float>(horizontal) * kRightStickAxisSwitchRatio) {
        candidate = axis_;
    } else if (axis_ == FreeScrollAxis::Vertical &&
               static_cast<float>(horizontal) <
                   static_cast<float>(vertical) * kRightStickAxisSwitchRatio) {
        candidate = axis_;
    }
    axis_ = candidate;

    const int magnitude = axis_ == FreeScrollAxis::Horizontal
        ? horizontal
        : vertical;
    const float strength = std::clamp(
        static_cast<float>(magnitude - kRightStickScrollDeadZone) /
            static_cast<float>(32'767 - kRightStickScrollDeadZone),
        0.0F,
        1.0F);
    const std::uint64_t elapsed = !moving_
        ? kRightStickInitialSampleMilliseconds
        : now > lastSampleAt_
            ? std::min(now - lastSampleAt_, kRightStickMaximumSampleMilliseconds)
            : 0;
    lastSampleAt_ = std::max(lastSampleAt_, now);
    moving_ = true;

    float direction = 1.0F;
    if ((axis_ == FreeScrollAxis::Horizontal && x < 0) ||
        (axis_ == FreeScrollAxis::Vertical && y > 0)) {
        direction = -1.0F;
    }
    return {
        axis_,
        direction * strength * kRightStickMaximumScrollRateDipPerSecond *
            static_cast<float>(elapsed) / 1000.0F,
        true,
        false,
    };
}

void RightStickScrollKinetics::Reset() noexcept {
    axis_ = FreeScrollAxis::None;
    lastSampleAt_ = 0;
    moving_ = false;
}

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

} // namespace widgetrail::input
