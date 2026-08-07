#include "ControllerNavigation.h"

#include <array>
#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

int checks{};
void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

} // namespace

int main() {
    using gba::input::NavigationDirection;
    gba::input::StickNavigator navigator;
    navigator.Prime(0, 0, 0);
    Check(!navigator.Update(8'000, 7'000, 10), "dead zone is quiet");
    Check(navigator.Update(0, -20'000, 20) == NavigationDirection::Down,
          "negative Y navigates down");
    Check(!navigator.Update(4'000, -18'000, 100), "held direction does not chatter");
    Check(navigator.Update(20'000, -10'000, 110) == NavigationDirection::Right,
          "clear dominant-axis gesture changes direction");
    Check(!navigator.Update(18'000, -14'500, 200), "diagonal noise keeps engaged axis");
    Check(navigator.Update(18'000, -14'500, 470) == NavigationDirection::Right,
          "held direction repeats after initial delay");
    Check(navigator.Update(18'000, -14'500, 595) == NavigationDirection::Right,
          "repeat cadence is stable");
    Check(!navigator.Update(0, 0, 600), "release is quiet");
    Check(navigator.Update(0, 20'000, 610) == NavigationDirection::Up,
          "positive Y navigates up after release");
    navigator.Reset();
    navigator.Prime(-20'000, 0, 700);
    Check(!navigator.Update(-20'000, 0, 710), "priming prevents an opening ghost move");

    using gba::input::ControllerActionContext;
    using gba::input::ControllerActionRoute;
    using gba::input::RouteControllerAction;
    using gba::input::RouteUnhandledControllerAction;
    Check(RouteControllerAction(ControllerActionContext::Dashboard, L"A") ==
              ControllerActionRoute::HostActivate,
          "dashboard A remains the host open action");
    Check(RouteControllerAction(ControllerActionContext::Dashboard, L"Y") ==
              ControllerActionRoute::HostToggleReorder,
          "dashboard Y remains the host reorder action");
    Check(RouteControllerAction(ControllerActionContext::Dashboard, L"B") ==
              ControllerActionRoute::HostCloseOverlay,
          "dashboard B is browser-like Back and closes the overlay");
    constexpr std::array<std::wstring_view, 9> widgetOwnedDashboardButtons{
        L"X", L"LB", L"RB", L"LT", L"RT", L"LS", L"RS", L"View", L"Menu"};
    for (const auto button : widgetOwnedDashboardButtons) {
        Check(RouteControllerAction(ControllerActionContext::Dashboard, button) ==
                  ControllerActionRoute::Widget,
              "every other dashboard action is owned by the hovered widget");
    }
    constexpr std::array<std::wstring_view, 12> openWidgetButtons{
        L"A", L"B", L"X", L"Y", L"LB", L"RB", L"LT", L"RT", L"LS", L"RS", L"View", L"Menu"};
    for (const auto button : openWidgetButtons) {
        Check(RouteControllerAction(ControllerActionContext::RootWidgetScope, button) ==
                  ControllerActionRoute::Widget,
              "root widget receives every action before host fallback");
        Check(RouteControllerAction(ControllerActionContext::NestedWidgetScope, button) ==
                  ControllerActionRoute::Widget,
              "nested widget scope receives every action before host fallback");
    }
    Check(RouteUnhandledControllerAction(
              ControllerActionContext::RootWidgetScope, L"B") ==
              ControllerActionRoute::HostBackToDashboard,
          "unhandled root B returns to the icon tray");
    Check(RouteUnhandledControllerAction(
              ControllerActionContext::NestedWidgetScope, L"B") ==
              ControllerActionRoute::None,
          "unhandled nested B never collapses the widget hierarchy");
    Check(RouteUnhandledControllerAction(
              ControllerActionContext::RootWidgetScope, L"X") ==
              ControllerActionRoute::None,
          "unhandled non-Back actions never become host navigation");

    std::cout << "ControllerNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
