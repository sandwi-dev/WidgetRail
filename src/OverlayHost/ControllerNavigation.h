#pragma once

#include <cstdint>
#include <optional>
#include <string_view>

namespace gba::input {

enum class NavigationDirection { None, Left, Right, Up, Down };

enum class NavigationEventPhase { Pressed, Repeated };

struct StickNavigationEvent final {
    NavigationDirection direction{NavigationDirection::None};
    NavigationEventPhase phase{NavigationEventPhase::Pressed};
};

/// Converts an opposing pair of digital buttons to a signed navigation axis.
/// Simultaneous opposites are neutral so malformed hardware state cannot pick
/// an arbitrary direction.
[[nodiscard]] constexpr short DigitalNavigationAxis(
    const std::uint16_t buttons,
    const std::uint16_t negativeButton,
    const std::uint16_t positiveButton) noexcept {
    const bool negative = (buttons & negativeButton) != 0;
    const bool positive = (buttons & positiveButton) != 0;
    if (negative == positive) return 0;
    return negative ? static_cast<short>(-32'767) : static_cast<short>(32'767);
}

enum class FocusedDirectionRoute {
    FocusNavigation,
    SliderAdjustment,
    Consume,
};

/// A focused slider owns horizontal direction. Busy/disabled slider input is
/// consumed rather than leaking into spatial focus; Up/Down always navigate.
[[nodiscard]] constexpr FocusedDirectionRoute RouteFocusedDirection(
    const std::wstring_view focusedKind,
    const bool disabled,
    const bool busy,
    const NavigationDirection direction) noexcept {
    const bool horizontal = direction == NavigationDirection::Left ||
                            direction == NavigationDirection::Right;
    if (focusedKind != L"slider" || !horizontal)
        return FocusedDirectionRoute::FocusNavigation;
    return disabled || busy
        ? FocusedDirectionRoute::Consume
        : FocusedDirectionRoute::SliderAdjustment;
}

enum class ControllerActionContext {
    Tray,
    RootWidgetScope,
    NestedWidgetScope,
};

enum class ControllerActionRoute {
    HostActivate,
    HostToggleReorder,
    HostCloseOverlay,
    HostBackToDashboard,
    Widget,
    None,
};

/// Decides who receives a non-navigation button first. Tray A/Y remain shell
/// navigation, tray B is browser-like Back (close), and all interactive
/// widget buttons first reach the active SDK input scope. Guide is a system
/// button and is intentionally outside this policy.
[[nodiscard]] constexpr ControllerActionRoute RouteControllerAction(
    const ControllerActionContext context,
    const std::wstring_view button) noexcept {
    if (context != ControllerActionContext::Tray) {
        return ControllerActionRoute::Widget;
    }
    if (button == L"A") return ControllerActionRoute::HostActivate;
    if (button == L"Y") return ControllerActionRoute::HostToggleReorder;
    if (button == L"B") return ControllerActionRoute::HostCloseOverlay;
    return ControllerActionRoute::Widget;
}

/// Down exits a root widget only after both author-specified and geometric
/// focus navigation are exhausted. Nested input scopes keep focus contained so
/// their own Back hierarchy remains authoritative.
[[nodiscard]] constexpr bool ShouldTransferFocusToTray(
    const NavigationDirection direction,
    const bool rootInputScope,
    const bool hasExplicitTarget,
    const bool hasGeometricTarget) noexcept {
    return direction == NavigationDirection::Down && rootInputScope &&
           !hasExplicitTarget && !hasGeometricTarget;
}

/// Up is the spatial inverse of leaving a root widget through its lower
/// boundary: from the tray it enters the already visible widget without
/// activating the focused widget control. Repeats are ignored so a held stick
/// cannot immediately move again inside the widget after the region changes.
[[nodiscard]] constexpr bool ShouldEnterWidgetFromTray(
    const NavigationDirection direction,
    const NavigationEventPhase phase) noexcept {
    return direction == NavigationDirection::Up &&
           phase == NavigationEventPhase::Pressed;
}

/// A self-loop is useful for containing focus in a nested scope, but it is not
/// movement at a root boundary. Treat only a distinct enabled destination as
/// an explicit move so Down on a last-row self-loop can enter the shell tray.
[[nodiscard]] constexpr bool IsDistinctFocusMove(
    const std::wstring_view current,
    const std::wstring_view target,
    const bool targetNavigable) noexcept {
    return targetNavigable && !target.empty() && target != current;
}

/// Resolves only an explicitly unhandled widget result. B at the root scope
/// returns to the icon tray. A nested scope keeps Back widget-owned so its
/// explicit shortcut can perform local hierarchy navigation without the host
/// guessing or collapsing the entire widget.
[[nodiscard]] constexpr ControllerActionRoute RouteUnhandledControllerAction(
    const ControllerActionContext context,
    const std::wstring_view button) noexcept {
    return context == ControllerActionContext::RootWidgetScope && button == L"B"
        ? ControllerActionRoute::HostBackToDashboard
        : ControllerActionRoute::None;
}

struct StickNavigationOptions final {
    int engageThreshold{15'000};
    int releaseThreshold{9'000};
    std::uint64_t initialRepeatMilliseconds{360};
    std::uint64_t repeatMilliseconds{125};
};

/// Converts a noisy two-axis stick into stable four-way navigation with
/// hysteresis, dominant-axis switching, and deterministic repeat timing.
class StickNavigator final {
public:
    explicit StickNavigator(StickNavigationOptions options = {}) noexcept;

    void Prime(short x, short y, std::uint64_t now) noexcept;
    [[nodiscard]] std::optional<NavigationDirection> Update(
        short x, short y, std::uint64_t now) noexcept;
    [[nodiscard]] std::optional<StickNavigationEvent> UpdateEvent(
        short x, short y, std::uint64_t now) noexcept;
    void Reset() noexcept;

private:
    [[nodiscard]] NavigationDirection Resolve(short x, short y) const noexcept;

    StickNavigationOptions options_;
    NavigationDirection direction_{NavigationDirection::None};
    std::uint64_t nextRepeat_{};
};

} // namespace gba::input
