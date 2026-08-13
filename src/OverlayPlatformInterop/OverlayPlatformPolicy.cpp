#include "OverlayPlatformPolicy.h"

#include <Windows.h>
#include <Xinput.h>

#include <algorithm>
#include <cmath>
#include <optional>

namespace {

int Magnitude(const short value) noexcept {
    return std::abs(static_cast<int>(value));
}

bool SameDirection(
    const gba::input::NavigationDirection direction,
    const short x,
    const short y) noexcept {
    switch (direction) {
    case gba::input::NavigationDirection::Left: return x < 0;
    case gba::input::NavigationDirection::Right: return x > 0;
    case gba::input::NavigationDirection::Up: return y > 0;
    case gba::input::NavigationDirection::Down: return y < 0;
    default: return false;
    }
}

bool Horizontal(const gba::input::NavigationDirection direction) noexcept {
    return direction == gba::input::NavigationDirection::Left ||
           direction == gba::input::NavigationDirection::Right;
}

GbaOverlayPlatformNavigationDirection ConvertDirection(
    const gba::input::NavigationDirection direction) noexcept {
    switch (direction) {
    case gba::input::NavigationDirection::Left:
        return GbaOverlayPlatformNavigationDirection::Left;
    case gba::input::NavigationDirection::Right:
        return GbaOverlayPlatformNavigationDirection::Right;
    case gba::input::NavigationDirection::Up:
        return GbaOverlayPlatformNavigationDirection::Up;
    case gba::input::NavigationDirection::Down:
        return GbaOverlayPlatformNavigationDirection::Down;
    case gba::input::NavigationDirection::None:
    default:
        return GbaOverlayPlatformNavigationDirection::None;
    }
}

GbaOverlayPlatformNavigationPhase ConvertPhase(
    const gba::input::NavigationEventPhase phase) noexcept {
    return phase == gba::input::NavigationEventPhase::Repeated
        ? GbaOverlayPlatformNavigationPhase::Repeated
        : GbaOverlayPlatformNavigationPhase::Pressed;
}

GbaOverlayPlatformNavigationEvent ConvertNavigation(
    const std::optional<gba::input::StickNavigationEvent>& event) noexcept {
    return event
        ? GbaOverlayPlatformNavigationEvent{
              ConvertDirection(event->direction), ConvertPhase(event->phase)}
        : GbaOverlayPlatformNavigationEvent{};
}

} // namespace

namespace gba::input {

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

void StickNavigator::Prime(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    direction_ = Resolve(x, y);
    nextRepeat_ = direction_ == NavigationDirection::None
        ? 0
        : now + options_.initialRepeatMilliseconds;
}

std::optional<NavigationDirection> StickNavigator::Update(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    const auto event = UpdateEvent(x, y, now);
    return event ? std::optional{event->direction} : std::nullopt;
}

std::optional<StickNavigationEvent> StickNavigator::UpdateEvent(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
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

NavigationDirection StickNavigator::Resolve(
    const short x,
    const short y) const noexcept {
    const auto absX = Magnitude(x);
    const auto absY = Magnitude(y);
    if (direction_ != NavigationDirection::None &&
        SameDirection(direction_, x, y)) {
        const auto primary = Horizontal(direction_) ? absX : absY;
        const auto perpendicular = Horizontal(direction_) ? absY : absX;
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

namespace gba::platform {

bool GuideToggleDebouncer::Accept(
    const std::uint64_t nowMilliseconds) noexcept {
    if (hasAccepted_ && nowMilliseconds >= lastAcceptedAt_ &&
        nowMilliseconds - lastAcceptedAt_ < kGuideDebounceMilliseconds) {
        return false;
    }
    lastAcceptedAt_ = nowMilliseconds;
    hasAccepted_ = true;
    return true;
}

void GuideToggleDebouncer::Reset() noexcept {
    lastAcceptedAt_ = 0;
    hasAccepted_ = false;
}

void ControllerFrameTracker::Prime(
    const bool connected,
    const GbaOverlayPlatformRawControllerState& state,
    const std::uint64_t nowMilliseconds) noexcept {
    const auto effective = connected
        ? state
        : GbaOverlayPlatformRawControllerState{};
    previousButtons_ = effective.buttons;
    leftTriggerPressed_ = connected && effective.leftTrigger >= 30;
    rightTriggerPressed_ = connected && effective.rightTrigger >= 30;
    constexpr std::uint16_t recoveryChord =
        XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
    recoveryChordHeld_ = connected &&
        (effective.buttons & recoveryChord) == recoveryChord;
    stickNavigator_.Prime(
        effective.leftThumbX, effective.leftThumbY, nowMilliseconds);
    dpadNavigator_.Prime(
        gba::input::DigitalNavigationAxis(
            effective.buttons,
            XINPUT_GAMEPAD_DPAD_LEFT,
            XINPUT_GAMEPAD_DPAD_RIGHT),
        gba::input::DigitalNavigationAxis(
            effective.buttons,
            XINPUT_GAMEPAD_DPAD_DOWN,
            XINPUT_GAMEPAD_DPAD_UP),
        nowMilliseconds);
    primed_ = true;
}

GbaOverlayPlatformControllerFrame ControllerFrameTracker::Update(
    const bool connected,
    const GbaOverlayPlatformRawControllerState& state,
    const std::uint64_t nowMilliseconds) noexcept {
    GbaOverlayPlatformControllerFrame frame;
    frame.connected = connected;
    frame.state = connected ? state : GbaOverlayPlatformRawControllerState{};
    if (!primed_) {
        Prime(connected, state, nowMilliseconds);
        frame.primed = true;
        return frame;
    }

    frame.pressedButtons = static_cast<std::uint16_t>(
        frame.state.buttons & ~previousButtons_);
    frame.releasedButtons = static_cast<std::uint16_t>(
        previousButtons_ & ~frame.state.buttons);
    previousButtons_ = frame.state.buttons;

    const bool leftTrigger = connected && frame.state.leftTrigger >= 30;
    const bool rightTrigger = connected && frame.state.rightTrigger >= 30;
    frame.leftTriggerPressed = leftTrigger && !leftTriggerPressed_;
    frame.leftTriggerReleased = !leftTrigger && leftTriggerPressed_;
    frame.rightTriggerPressed = rightTrigger && !rightTriggerPressed_;
    frame.rightTriggerReleased = !rightTrigger && rightTriggerPressed_;
    leftTriggerPressed_ = leftTrigger;
    rightTriggerPressed_ = rightTrigger;

    constexpr std::uint16_t recoveryChord =
        XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
    const bool recoveryChordDown = connected &&
        (frame.state.buttons & recoveryChord) == recoveryChord;
    frame.recoveryChordPressed = recoveryChordDown && !recoveryChordHeld_;
    recoveryChordHeld_ = recoveryChordDown;

    frame.stickNavigation = ConvertNavigation(stickNavigator_.UpdateEvent(
        frame.state.leftThumbX, frame.state.leftThumbY, nowMilliseconds));
    frame.dpadNavigation = ConvertNavigation(dpadNavigator_.UpdateEvent(
        gba::input::DigitalNavigationAxis(
            frame.state.buttons,
            XINPUT_GAMEPAD_DPAD_LEFT,
            XINPUT_GAMEPAD_DPAD_RIGHT),
        gba::input::DigitalNavigationAxis(
            frame.state.buttons,
            XINPUT_GAMEPAD_DPAD_DOWN,
            XINPUT_GAMEPAD_DPAD_UP),
        nowMilliseconds));
    return frame;
}

void ControllerFrameTracker::Reset() noexcept {
    primed_ = false;
    previousButtons_ = 0;
    leftTriggerPressed_ = false;
    rightTriggerPressed_ = false;
    recoveryChordHeld_ = false;
    stickNavigator_.Reset();
    dpadNavigator_.Reset();
}

} // namespace gba::platform
