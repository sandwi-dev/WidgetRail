#pragma once

#include <cstdint>
#include <optional>
#include <string_view>

namespace gba::input {

enum class NavigationDirection { None, Left, Right, Up, Down };

enum class ControllerActionContext {
    Dashboard,
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

/// Decides who receives a non-navigation button first. Dashboard A/Y remain
/// shell navigation, dashboard B is browser-like Back (close), and all open
/// widget buttons first reach the active SDK input scope. Guide is a system
/// button and is intentionally outside this policy.
[[nodiscard]] constexpr ControllerActionRoute RouteControllerAction(
    const ControllerActionContext context,
    const std::wstring_view button) noexcept {
    if (context != ControllerActionContext::Dashboard) {
        return ControllerActionRoute::Widget;
    }
    if (button == L"A") return ControllerActionRoute::HostActivate;
    if (button == L"Y") return ControllerActionRoute::HostToggleReorder;
    if (button == L"B") return ControllerActionRoute::HostCloseOverlay;
    return ControllerActionRoute::Widget;
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
    void Reset() noexcept;

private:
    [[nodiscard]] NavigationDirection Resolve(short x, short y) const noexcept;

    StickNavigationOptions options_;
    NavigationDirection direction_{NavigationDirection::None};
    std::uint64_t nextRepeat_{};
};

} // namespace gba::input
