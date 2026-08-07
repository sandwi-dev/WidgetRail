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

    gba::input::StickNavigator phased;
    phased.Prime(0, 0, 0);
    const auto pressedDirection = phased.UpdateEvent(20'000, 0, 10);
    Check(pressedDirection && pressedDirection->phase ==
              gba::input::NavigationEventPhase::Pressed,
          "new stick direction is a pressed event");
    const auto repeatedDirection = phased.UpdateEvent(20'000, 0, 370);
    Check(repeatedDirection && repeatedDirection->phase ==
              gba::input::NavigationEventPhase::Repeated,
          "held stick direction preserves repeat phase");

    constexpr std::uint16_t left = 0x0001;
    constexpr std::uint16_t right = 0x0002;
    Check(gba::input::DigitalNavigationAxis(left, left, right) < 0,
          "digital negative button maps to a negative axis");
    Check(gba::input::DigitalNavigationAxis(right, left, right) > 0,
          "digital positive button maps to a positive axis");
    Check(gba::input::DigitalNavigationAxis(left | right, left, right) == 0,
          "opposing digital directions fail neutral");
    gba::input::StickNavigator dpad{
        gba::input::StickNavigationOptions{1, 0, 360, 125}};
    dpad.Prime(0, 0, 0);
    const auto dpadPressed = dpad.UpdateEvent(
        gba::input::DigitalNavigationAxis(right, left, right), 0, 10);
    Check(dpadPressed && dpadPressed->phase == gba::input::NavigationEventPhase::Pressed,
          "D-pad direction emits one pressed event");
    Check(!dpad.UpdateEvent(
              gba::input::DigitalNavigationAxis(right, left, right), 0, 369),
          "D-pad hold waits for bounded initial repeat delay");
    const auto dpadRepeated = dpad.UpdateEvent(
        gba::input::DigitalNavigationAxis(right, left, right), 0, 370);
    Check(dpadRepeated && dpadRepeated->phase == gba::input::NavigationEventPhase::Repeated,
          "D-pad hold shares the bounded navigation repeat cadence");

    using gba::input::FocusedDirectionRoute;
    using gba::input::RouteFocusedDirection;
    Check(RouteFocusedDirection(L"slider", false, false, NavigationDirection::Left) ==
              FocusedDirectionRoute::SliderAdjustment,
          "focused slider owns Left adjustment");
    Check(RouteFocusedDirection(L"slider", false, false, NavigationDirection::Up) ==
              FocusedDirectionRoute::FocusNavigation,
          "focused slider leaves Up for focus navigation");
    Check(RouteFocusedDirection(L"slider", true, false, NavigationDirection::Right) ==
              FocusedDirectionRoute::Consume,
          "disabled slider consumes adjustment without activating");
    Check(RouteFocusedDirection(L"slider", false, true, NavigationDirection::Right) ==
              FocusedDirectionRoute::Consume,
          "busy slider consumes adjustment while retaining focus");
    Check(RouteFocusedDirection(L"button", true, true, NavigationDirection::Right) ==
              FocusedDirectionRoute::FocusNavigation,
          "button direction remains focus navigation regardless of activation state");

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
