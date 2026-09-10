#include "OverlayPlatformPolicy.h"

#include <Windows.h>
#include <Xinput.h>

#include <optional>

namespace {

constexpr std::uint32_t ToAbiBoolean(const bool value) noexcept {
    return value
        ? WRAIL_OVERLAY_PLATFORM_TRUE
        : WRAIL_OVERLAY_PLATFORM_FALSE;
}

WidgetRailOverlayPlatformNavigationDirection ConvertDirection(
    const widgetrail::input::NavigationDirection direction) noexcept {
    switch (direction) {
    case widgetrail::input::NavigationDirection::Left:
        return WidgetRailOverlayPlatformNavigationDirection::Left;
    case widgetrail::input::NavigationDirection::Right:
        return WidgetRailOverlayPlatformNavigationDirection::Right;
    case widgetrail::input::NavigationDirection::Up:
        return WidgetRailOverlayPlatformNavigationDirection::Up;
    case widgetrail::input::NavigationDirection::Down:
        return WidgetRailOverlayPlatformNavigationDirection::Down;
    case widgetrail::input::NavigationDirection::None:
    default:
        return WidgetRailOverlayPlatformNavigationDirection::None;
    }
}

WidgetRailOverlayPlatformNavigationPhase ConvertPhase(
    const widgetrail::input::NavigationEventPhase phase) noexcept {
    return phase == widgetrail::input::NavigationEventPhase::Repeated
        ? WidgetRailOverlayPlatformNavigationPhase::Repeated
        : WidgetRailOverlayPlatformNavigationPhase::Pressed;
}

WidgetRailOverlayPlatformNavigationEvent ConvertNavigation(
    const std::optional<widgetrail::input::StickNavigationEvent>& event) noexcept {
    return event
        ? WidgetRailOverlayPlatformNavigationEvent{
              ConvertDirection(event->direction), ConvertPhase(event->phase)}
        : WidgetRailOverlayPlatformNavigationEvent{};
}

} // namespace

namespace widgetrail::platform {

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
    const WidgetRailOverlayPlatformRawControllerState& state,
    const std::uint64_t nowMilliseconds) noexcept {
    const auto effective = connected
        ? state
        : WidgetRailOverlayPlatformRawControllerState{};
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
        widgetrail::input::DigitalNavigationAxis(
            effective.buttons,
            XINPUT_GAMEPAD_DPAD_LEFT,
            XINPUT_GAMEPAD_DPAD_RIGHT),
        widgetrail::input::DigitalNavigationAxis(
            effective.buttons,
            XINPUT_GAMEPAD_DPAD_DOWN,
            XINPUT_GAMEPAD_DPAD_UP),
        nowMilliseconds);
    primed_ = true;
}

WidgetRailOverlayPlatformControllerFrame ControllerFrameTracker::Update(
    const bool connected,
    const WidgetRailOverlayPlatformRawControllerState& state,
    const std::uint64_t nowMilliseconds,
    const bool allowNavigationRepeat) noexcept {
    WidgetRailOverlayPlatformControllerFrame frame;
    frame.connected = ToAbiBoolean(connected);
    frame.state = connected ? state : WidgetRailOverlayPlatformRawControllerState{};
    if (!primed_) {
        Prime(connected, state, nowMilliseconds);
        frame.primed = WRAIL_OVERLAY_PLATFORM_TRUE;
        return frame;
    }

    frame.pressedButtons = static_cast<std::uint16_t>(
        frame.state.buttons & ~previousButtons_);
    frame.releasedButtons = static_cast<std::uint16_t>(
        previousButtons_ & ~frame.state.buttons);
    previousButtons_ = frame.state.buttons;

    const bool leftTrigger = connected && frame.state.leftTrigger >= 30;
    const bool rightTrigger = connected && frame.state.rightTrigger >= 30;
    frame.leftTriggerPressed = ToAbiBoolean(
        leftTrigger && !leftTriggerPressed_);
    frame.leftTriggerReleased = ToAbiBoolean(
        !leftTrigger && leftTriggerPressed_);
    frame.rightTriggerPressed = ToAbiBoolean(
        rightTrigger && !rightTriggerPressed_);
    frame.rightTriggerReleased = ToAbiBoolean(
        !rightTrigger && rightTriggerPressed_);
    leftTriggerPressed_ = leftTrigger;
    rightTriggerPressed_ = rightTrigger;

    constexpr std::uint16_t recoveryChord =
        XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
    const bool recoveryChordDown = connected &&
        (frame.state.buttons & recoveryChord) == recoveryChord;
    frame.recoveryChordPressed = ToAbiBoolean(
        recoveryChordDown && !recoveryChordHeld_);
    recoveryChordHeld_ = recoveryChordDown;

    frame.stickNavigation = ConvertNavigation(stickNavigator_.UpdateEvent(
        frame.state.leftThumbX, frame.state.leftThumbY, nowMilliseconds,
        allowNavigationRepeat));
    frame.dpadNavigation = ConvertNavigation(dpadNavigator_.UpdateEvent(
        widgetrail::input::DigitalNavigationAxis(
            frame.state.buttons,
            XINPUT_GAMEPAD_DPAD_LEFT,
            XINPUT_GAMEPAD_DPAD_RIGHT),
        widgetrail::input::DigitalNavigationAxis(
            frame.state.buttons,
            XINPUT_GAMEPAD_DPAD_DOWN,
            XINPUT_GAMEPAD_DPAD_UP),
        nowMilliseconds, allowNavigationRepeat));
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

} // namespace widgetrail::platform
