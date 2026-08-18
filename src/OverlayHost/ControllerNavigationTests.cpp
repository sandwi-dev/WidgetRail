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
    using widgetrail::input::NavigationDirection;

    using widgetrail::input::TrayYGesture;
    using widgetrail::input::TrayYGestureAction;
    constexpr auto hold = widgetrail::input::kTrayWidgetRestartHoldMilliseconds;
    TrayYGesture trayY;
    trayY.Press(L"spotify", true, 1'000);
    Check(trayY.capturing() && trayY.pendingRestart(),
          "eligible tray Y begins one captured restart gesture");
    Check(trayY.progressPercent(1'000) == 0 &&
              trayY.progressPercent(1'000 + hold / 2) == 50,
          "hold progress is deterministic and bounded");
    Check(trayY.Update(L"spotify", true, 1'000 + hold - 1) ==
              TrayYGestureAction::None,
          "hold remains pending immediately before the threshold");
    Check(trayY.Release(L"spotify", true, 1'000 + hold - 1) ==
              TrayYGestureAction::ToggleReorder,
          "release immediately before threshold preserves tap reorder");
    Check(!trayY.capturing(), "tap release retires gesture state");

    trayY.Press(L"spotify", true, 2'000);
    Check(trayY.Release(L"spotify", true, 2'000 + hold) ==
              TrayYGestureAction::RestartSelectedWidget,
          "release exactly at threshold restarts instead of reordering");
    trayY.Press(L"spotify", true, 2'800);
    Check(trayY.Release(L"spotify", true, 2'800 + hold + 1) ==
              TrayYGestureAction::RestartSelectedWidget,
          "release after threshold restarts without requiring an earlier timer tick");
    trayY.Press(L"spotify", true, 3'000);
    trayY.Press(L"ytmusic", true, 3'100);
    Check(trayY.Update(L"spotify", true, 3'000 + hold) ==
              TrayYGestureAction::RestartSelectedWidget,
          "held Y activates at threshold and repeated press cannot replace its target");
    Check(trayY.Update(L"spotify", true, 3'000 + hold + 1) ==
              TrayYGestureAction::None,
          "timer repeats cannot activate restart twice");
    Check(trayY.Release(L"spotify", true, 3'000 + hold + 2) ==
              TrayYGestureAction::None,
          "release after hold wins suppresses reorder and widget Y");

    trayY.Press(L"spotify", true, 4'000);
    Check(trayY.Update(L"ytmusic", true, 4'100) == TrayYGestureAction::None,
          "stale selected ID cancels restart");
    Check(trayY.Release(L"ytmusic", true, 4'200) == TrayYGestureAction::None,
          "stale-selection release cannot become reorder");
    trayY.Press(L"spotify", true, 5'000);
    Check(trayY.Update(L"spotify", false, 5'100) == TrayYGestureAction::None,
          "focus or lifecycle ineligibility cancels hold");
    Check(trayY.Release(L"spotify", false, 5'200) == TrayYGestureAction::None,
          "canceled context consumes the eventual physical release");
    trayY.Press(L"spotify", true, 5'300);
    Check(trayY.Update(L"spotify", false, 5'400) == TrayYGestureAction::None &&
              trayY.Release(L"spotify", false, 5'500) == TrayYGestureAction::None,
          "opening widget controls cancels hold and suppresses release");
    trayY.Press(L"spotify", true, 5'600);
    Check(trayY.Update(L"spotify", false, 5'700) == TrayYGestureAction::None &&
              trayY.Release(L"spotify", false, 5'800) == TrayYGestureAction::None,
          "entering reorder cancels an already pending restart");
    trayY.Press(L"spotify", true, 6'000);
    trayY.Cancel();
    Check(trayY.Update(L"spotify", true, 6'000 + hold) == TrayYGestureAction::None,
          "overlay hide cancels before threshold work");
    Check(trayY.Release(L"spotify", true, 6'000 + hold + 1) ==
              TrayYGestureAction::None,
          "overlay cancellation suppresses stale release");
    trayY.Press(L"spotify", true, 6'800);
    trayY.Cancel();
    Check(trayY.Update(L"spotify", true, 6'800 + hold) == TrayYGestureAction::None &&
              trayY.Release(L"spotify", true, 6'800 + hold + 1) ==
                  TrayYGestureAction::None,
          "device loss cancels and blocks reconnect/repeat work until release");

    trayY.Press(L"spotify", false, 7'000);
    Check(!trayY.pendingRestart() && trayY.Release(L"spotify", false, 8'000) ==
              TrayYGestureAction::ToggleReorder,
          "Y in reorder mode remains a tap-only done action");
    trayY.Press(L"spotify", true, 9'000);
    Check(trayY.Update(L"spotify", true, 9'000 + hold) ==
              TrayYGestureAction::RestartSelectedWidget,
          "restart authority is requested once even when the host reload fails");
    Check(trayY.Update(L"spotify", true, 10'000) == TrayYGestureAction::None,
          "failed reload result cannot retrigger the recognized hold");
    trayY.Reset();
    Check(!trayY.capturing(), "lifecycle reset releases all gesture state");

    using widgetrail::input::ResolveCurrentWidgetReloadTarget;
    Check(ResolveCurrentWidgetReloadTarget(
              true, true, L"selected", L"active") == L"selected",
          "tray F5 resolves the selected widget");
    Check(ResolveCurrentWidgetReloadTarget(
              true, false, L"selected", L"active") == L"active",
          "open-widget F5 and recovery resolve the active widget");
    Check(ResolveCurrentWidgetReloadTarget(
              false, true, L"selected", L"active").empty(),
          "hidden overlay has no reload target");

    widgetrail::input::StickNavigator navigator;
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

    widgetrail::input::StickNavigator phased;
    phased.Prime(0, 0, 0);
    const auto pressedDirection = phased.UpdateEvent(20'000, 0, 10);
    Check(pressedDirection && pressedDirection->phase ==
              widgetrail::input::NavigationEventPhase::Pressed,
          "new stick direction is a pressed event");
    const auto repeatedDirection = phased.UpdateEvent(20'000, 0, 370);
    Check(repeatedDirection && repeatedDirection->phase ==
              widgetrail::input::NavigationEventPhase::Repeated,
          "held stick direction preserves repeat phase");

    constexpr std::uint16_t left = 0x0001;
    constexpr std::uint16_t right = 0x0002;
    Check(widgetrail::input::DigitalNavigationAxis(left, left, right) < 0,
          "digital negative button maps to a negative axis");
    Check(widgetrail::input::DigitalNavigationAxis(right, left, right) > 0,
          "digital positive button maps to a positive axis");
    Check(widgetrail::input::DigitalNavigationAxis(left | right, left, right) == 0,
          "opposing digital directions fail neutral");
    widgetrail::input::StickNavigator dpad{
        widgetrail::input::StickNavigationOptions{1, 0, 360, 125}};
    dpad.Prime(0, 0, 0);
    const auto dpadPressed = dpad.UpdateEvent(
        widgetrail::input::DigitalNavigationAxis(right, left, right), 0, 10);
    Check(dpadPressed && dpadPressed->phase == widgetrail::input::NavigationEventPhase::Pressed,
          "D-pad direction emits one pressed event");
    Check(!dpad.UpdateEvent(
              widgetrail::input::DigitalNavigationAxis(right, left, right), 0, 369),
          "D-pad hold waits for bounded initial repeat delay");
    const auto dpadRepeated = dpad.UpdateEvent(
        widgetrail::input::DigitalNavigationAxis(right, left, right), 0, 370);
    Check(dpadRepeated && dpadRepeated->phase == widgetrail::input::NavigationEventPhase::Repeated,
          "D-pad hold shares the bounded navigation repeat cadence");

    using widgetrail::input::FocusedDirectionRoute;
    using widgetrail::input::RouteFocusedDirection;
    Check(RouteFocusedDirection(
              L"slider", false, false, false, false, NavigationDirection::Left) ==
              FocusedDirectionRoute::SliderAdjustment,
          "focused slider owns Left adjustment");
    Check(RouteFocusedDirection(
              L"slider", false, false, false, false, NavigationDirection::Up) ==
              FocusedDirectionRoute::FocusNavigation,
          "focused slider leaves Up for focus navigation");
    Check(RouteFocusedDirection(
              L"slider", true, false, false, false, NavigationDirection::Right) ==
              FocusedDirectionRoute::Consume,
          "disabled slider consumes adjustment without activating");
    Check(RouteFocusedDirection(
              L"slider", false, true, false, false, NavigationDirection::Right) ==
              FocusedDirectionRoute::Consume,
          "busy slider consumes adjustment while retaining focus");
    Check(RouteFocusedDirection(
              L"button", true, true, false, false, NavigationDirection::Right) ==
              FocusedDirectionRoute::FocusNavigation,
          "button direction remains focus navigation regardless of activation state");
    Check(RouteFocusedDirection(
              L"slider", false, false, true, false, NavigationDirection::Left) ==
              FocusedDirectionRoute::FocusNavigation,
          "inactive activation-first slider leaves Left for focus navigation");
    Check(RouteFocusedDirection(
              L"slider", false, false, true, true, NavigationDirection::Left) ==
              FocusedDirectionRoute::SliderAdjustment,
          "active activation-first slider owns Left adjustment");
    Check(RouteFocusedDirection(
              L"slider", false, false, true, true, NavigationDirection::Down) ==
              FocusedDirectionRoute::Consume,
          "active activation-first slider contains vertical navigation");

    using widgetrail::input::FocusedSliderButtonRoute;
    using widgetrail::input::RouteFocusedSliderButton;
    Check(RouteFocusedSliderButton(L"slider", true, false, L"A") ==
              FocusedSliderButtonRoute::EnterAdjustment,
          "inactive activation-first slider uses A to enter adjustment");
    Check(RouteFocusedSliderButton(L"slider", true, true, L"A") ==
              FocusedSliderButtonRoute::ExitAdjustment,
          "active activation-first slider uses A to exit adjustment");
    Check(RouteFocusedSliderButton(L"slider", true, true, L"B") ==
              FocusedSliderButtonRoute::ExitAdjustment,
          "active activation-first slider uses B to exit adjustment");
    Check(RouteFocusedSliderButton(L"slider", true, false, L"B") ==
              FocusedSliderButtonRoute::Widget,
          "inactive activation-first slider leaves B to widget Back");
    Check(RouteFocusedSliderButton(L"slider", false, false, L"A") ==
              FocusedSliderButtonRoute::Widget,
          "direct slider preserves A activation actions");

    using widgetrail::input::ControllerActionContext;
    using widgetrail::input::ControllerActionRoute;
    using widgetrail::input::FailedWidgetActionRoute;
    using widgetrail::input::RouteControllerAction;
    using widgetrail::input::RouteFailedWidgetAction;
    using widgetrail::input::RouteUnhandledControllerAction;
    Check(RouteControllerAction(ControllerActionContext::Tray, L"A") ==
              ControllerActionRoute::HostActivate,
          "dashboard A remains the host open action");
    Check(RouteControllerAction(ControllerActionContext::Tray, L"Y") ==
              ControllerActionRoute::HostToggleReorder,
          "dashboard Y remains the host reorder action");
    Check(RouteControllerAction(ControllerActionContext::Tray, L"B") ==
              ControllerActionRoute::HostCloseOverlay,
          "dashboard B is browser-like Back and closes the overlay");
    constexpr std::array<std::wstring_view, 9> widgetOwnedDashboardButtons{
        L"X", L"LB", L"RB", L"LT", L"RT", L"LS", L"RS", L"View", L"Menu"};
    for (const auto button : widgetOwnedDashboardButtons) {
        Check(RouteControllerAction(ControllerActionContext::Tray, button) ==
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
    Check(RouteFailedWidgetAction(
              true, widgetrail::input::NavigationEventPhase::Pressed, L"A") ==
              FailedWidgetActionRoute::Retry,
          "failed open-widget A retains the explicit recovery action");
    Check(RouteFailedWidgetAction(
              true, widgetrail::input::NavigationEventPhase::Pressed, L"B") ==
              FailedWidgetActionRoute::HostBackToDashboard,
          "failed open-widget B remains host-owned Back navigation");
    Check(RouteFailedWidgetAction(
              false, widgetrail::input::NavigationEventPhase::Pressed, L"B") ==
              FailedWidgetActionRoute::Inert,
          "failed dashboard B is not redirected through widget input authority");
    Check(RouteFailedWidgetAction(
              true, widgetrail::input::NavigationEventPhase::Repeated, L"A") ==
              FailedWidgetActionRoute::Inert,
          "a held recovery button cannot start a second worker generation");
    Check(RouteFailedWidgetAction(
              true, widgetrail::input::NavigationEventPhase::Pressed, L"X") ==
              FailedWidgetActionRoute::Inert,
          "all other failed-widget actions remain inert");

    using widgetrail::input::ShouldTransferFocusToTray;
    Check(ShouldTransferFocusToTray(NavigationDirection::Down, true, false, false),
          "Down after the last root control transfers focus to the tray");
    Check(!ShouldTransferFocusToTray(NavigationDirection::Up, true, false, false),
          "only the downward root boundary enters the tray");
    Check(!ShouldTransferFocusToTray(NavigationDirection::Down, false, false, false),
          "nested scopes remain contained at their downward boundary");
    Check(!ShouldTransferFocusToTray(NavigationDirection::Down, true, true, false),
          "an explicit Down edge wins before the tray");
    Check(!ShouldTransferFocusToTray(NavigationDirection::Down, true, false, true),
          "a geometric Down target wins before the tray");
    using widgetrail::input::ShouldEnterWidgetFromTray;
    Check(ShouldEnterWidgetFromTray(
              NavigationDirection::Up,
              widgetrail::input::NavigationEventPhase::Pressed),
          "tray Up enters the already visible widget controls");
    Check(!ShouldEnterWidgetFromTray(
              NavigationDirection::Up,
              widgetrail::input::NavigationEventPhase::Repeated),
          "held tray Up cannot spill a repeated move into widget controls");
    Check(!ShouldEnterWidgetFromTray(
              NavigationDirection::Down,
              widgetrail::input::NavigationEventPhase::Pressed),
          "tray Down does not invent a second entry direction");
    using widgetrail::input::IsDistinctFocusMove;
    Check(IsDistinctFocusMove(L"current", L"next", true),
          "a distinct enabled explicit target is a real focus move");
    Check(!IsDistinctFocusMove(L"current", L"current", true),
          "a root self-loop is an exhausted boundary rather than movement");
    Check(!IsDistinctFocusMove(L"current", L"next", false),
          "an unavailable explicit target is not a focus move");

    std::cout << "ControllerNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
