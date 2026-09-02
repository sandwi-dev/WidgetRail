#include "ControllerNavigation.h"
#include "ControllerShortcutResolver.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>

namespace {

int checks{};
void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

struct ShortcutFixture final {
    std::wstring button;
    std::wstring actionId;
    std::wstring phase;
    std::wstring repeatPolicy;
};

struct ShortcutNodeFixture final {
    std::wstring id;
    std::wstring inputScopeId;
    bool isDisabled{};
    bool isBusy{};
    std::vector<ShortcutFixture> shortcuts;
    std::vector<ShortcutNodeFixture> children;
};

enum class Availability { Available, Disabled, Busy };

void SetAvailability(ShortcutNodeFixture& node, const Availability state) {
    node.isDisabled = state == Availability::Disabled;
    node.isBusy = state == Availability::Busy;
}

ShortcutFixture HeldShortcut(const std::wstring_view actionId = L"fixture.action") {
    return {L"x", std::wstring{actionId}, L"pressed", L"whileHeld"};
}

ShortcutNodeFixture ShortcutTree(
    const std::wstring_view ownerId,
    const Availability focusedState,
    const Availability ownerState) {
    ShortcutNodeFixture focused{L"focused"};
    SetAvailability(focused, focusedState);
    if (ownerId == L"focused") focused.shortcuts.push_back(HeldShortcut());

    ShortcutNodeFixture ancestor{L"ancestor"};
    if (ownerId == L"ancestor") {
        SetAvailability(ancestor, ownerState);
        ancestor.shortcuts.push_back(HeldShortcut());
    }
    ancestor.children.push_back(std::move(focused));

    ShortcutNodeFixture root{L"root", L"root"};
    if (ownerId == L"root") {
        SetAvailability(root, ownerState);
        root.shortcuts.push_back(HeldShortcut());
    }
    root.children.push_back(std::move(ancestor));
    return root;
}

void CheckShortcutResolutionContract() {
    using widgetrail::input::ControllerShortcutResolutionStatus;
    constexpr std::array levels{L"focused", L"ancestor", L"root"};
    constexpr std::array states{
        Availability::Available, Availability::Disabled, Availability::Busy};
    constexpr std::array phases{L"pressed", L"repeated"};
    for (const auto* phase : phases) {
        for (const auto* level : levels) {
            for (const auto focusedState : states) {
                for (const auto ownerState : states) {
                    if (std::wstring_view{level} == L"focused" &&
                        focusedState != ownerState) continue;
                    const auto root = ShortcutTree(level, focusedState, ownerState);
                    const auto resolved = widgetrail::input::ResolveControllerShortcut(
                        root, std::wstring_view{L"focused"}, L"x", phase);
                    Check(
                        resolved.status ==
                            (ownerState == Availability::Available
                                ? ControllerShortcutResolutionStatus::Resolved
                                : ControllerShortcutResolutionStatus::OwnerUnavailable),
                        "declaring owner availability governs every focus level and phase");
                    Check(resolved.owner && resolved.owner->id == level,
                          "shortcut resolution returns the exact declaring owner");
                    Check(resolved.actionId == L"fixture.action",
                          "shortcut resolution returns the exact action binding");
                }
            }
        }
    }

    const auto rootOnly = ShortcutTree(
        L"root", Availability::Available, Availability::Available);
    Check(widgetrail::input::ResolveControllerShortcut(
              rootOnly, std::nullopt, L"x", L"pressed").status ==
              ControllerShortcutResolutionStatus::Resolved,
          "no-focus resolution consults the scope root");

    auto nested = rootOnly;
    nested.children.clear();
    ShortcutNodeFixture nestedScope{L"nested", L"nested"};
    nestedScope.children.push_back(ShortcutNodeFixture{L"focused"});
    nested.children.push_back(std::move(nestedScope));
    Check(widgetrail::input::ResolveControllerShortcut(
              nested, std::wstring_view{L"focused"}, L"x", L"pressed").status ==
              ControllerShortcutResolutionStatus::FocusNotFound,
          "nested input scopes fail closed");

    auto nearestUnavailable = ShortcutTree(
        L"ancestor", Availability::Available, Availability::Disabled);
    nearestUnavailable.shortcuts.push_back(HeldShortcut(L"root.action"));
    const auto blocked = widgetrail::input::ResolveControllerShortcut(
        nearestUnavailable, std::wstring_view{L"focused"}, L"x", L"pressed");
    Check(blocked.status == ControllerShortcutResolutionStatus::OwnerUnavailable &&
              blocked.owner && blocked.owner->id == L"ancestor",
          "an unavailable nearest owner blocks ancestor fallback");

    auto focuslessDispatch = ShortcutTree(
        L"root", Availability::Available, Availability::Available);
    focuslessDispatch.children[0].children[0].shortcuts.push_back(
        HeldShortcut(L"alternative.action"));
    for (const auto* phase : phases) {
        const auto exact = widgetrail::input::ResolveHostControllerShortcut(
            focuslessDispatch, std::nullopt,
            std::optional<std::wstring_view>{L"focused"}, L"x", phase);
        Check(exact.status == ControllerShortcutResolutionStatus::Resolved &&
                  exact.owner && exact.owner->id == L"root" &&
                  exact.actionId == L"fixture.action",
              "legitimate empty focus keeps pressed and repeated dispatch on the scope-root binding");
    }

    for (const auto* phase : phases) {
        const auto stale = widgetrail::input::ResolveHostControllerShortcut(
            focuslessDispatch, std::optional<std::wstring_view>{L"missing"},
            std::optional<std::wstring_view>{L"focused"}, L"x", phase);
        Check(stale.status == ControllerShortcutResolutionStatus::FocusNotFound,
              "a stale raw focus cannot rebind pressed or repeated dispatch to an alternative visible target");
    }

    using widgetrail::input::AuthoredHeldActionBindingView;
    using widgetrail::input::AuthoredHeldActionDecisionPhase;
    using widgetrail::input::AuthoredHeldActionDisposition;
    const AuthoredHeldActionBindingView captured{
        {}, L"root", L"fixture.action"};
    const auto arm = widgetrail::input::DecideAuthoredHeldAction(
        AuthoredHeldActionDecisionPhase::Arm, false, captured);
    const auto initial = widgetrail::input::DecideAuthoredHeldAction(
        AuthoredHeldActionDecisionPhase::InitialDispatch, false, captured);
    const auto repeated = widgetrail::input::DecideAuthoredHeldAction(
        AuthoredHeldActionDecisionPhase::Repeat, false, captured, captured);
    Check(arm.disposition == AuthoredHeldActionDisposition::Dispatch &&
              initial.disposition == AuthoredHeldActionDisposition::Dispatch &&
              repeated.disposition == AuthoredHeldActionDisposition::Dispatch &&
              !initial.transportFocus && !initial.focusedNodeHandling &&
              !repeated.transportFocus && !repeated.focusedNodeHandling,
          "focusless arm, initial dispatch, and repeat retain empty transport focus without focused-node handling");
    Check(widgetrail::input::DecideAuthoredHeldAction(
              AuthoredHeldActionDecisionPhase::Repeat, false, captured,
              AuthoredHeldActionBindingView{L"focused", L"root", L"fixture.action"})
              .disposition == AuthoredHeldActionDisposition::Retire &&
          widgetrail::input::DecideAuthoredHeldAction(
              AuthoredHeldActionDecisionPhase::Repeat, false, captured,
              AuthoredHeldActionBindingView{{}, L"other", L"fixture.action"})
              .disposition == AuthoredHeldActionDisposition::Retire &&
          widgetrail::input::DecideAuthoredHeldAction(
              AuthoredHeldActionDecisionPhase::Repeat, false, captured,
              AuthoredHeldActionBindingView{{}, L"root", L"other.action"})
              .disposition == AuthoredHeldActionDisposition::Retire,
          "repeat retires when captured focus, source, or action authority changes");
    Check(widgetrail::input::DecideAuthoredHeldAction(
              AuthoredHeldActionDecisionPhase::Arm, true, captured).disposition ==
              AuthoredHeldActionDisposition::Retire &&
          widgetrail::input::DecideAuthoredHeldAction(
              AuthoredHeldActionDecisionPhase::Repeat, true, captured, captured)
              .disposition == AuthoredHeldActionDisposition::Retire,
          "an open Select popup blocks new arms and retires an existing hold");

    constexpr std::array inputPhases{L"pressed", L"released", L"repeated"};
    constexpr std::array shortcutPhases{L"pressed", L"released", L"repeated"};
    constexpr std::array policies{L"none", L"whileHeld"};
    for (const auto* inputPhase : inputPhases) {
        for (const auto* shortcutPhase : shortcutPhases) {
            for (const auto* policy : policies) {
                const bool expected = std::wstring_view{shortcutPhase} == inputPhase ||
                    (std::wstring_view{inputPhase} == L"repeated" &&
                     std::wstring_view{shortcutPhase} == L"pressed" &&
                     std::wstring_view{policy} == L"whileHeld");
                Check(widgetrail::protocol_contract::ControllerShortcutMatches(
                          L"x", shortcutPhase, policy, L"x", inputPhase) == expected,
                      "generated native match matrix equals the managed contract");
                Check(!widgetrail::protocol_contract::ControllerShortcutMatches(
                          L"x", shortcutPhase, policy, L"y", inputPhase),
                      "generated native match matrix requires the exact button");
            }
        }
    }
}

} // namespace

int main() {
    CheckShortcutResolutionContract();
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

    const auto ordinaryOptions = widgetrail::input::StickNavigationOptions{};
    Check(ordinaryOptions.initialRepeatMilliseconds == 360 &&
              ordinaryOptions.repeatMilliseconds == 125,
          "ordinary tray and widget navigation retains the 360/125 cadence");
    Check(widgetrail::input::kPinnedPlacementNavigationOptions.engageThreshold ==
              ordinaryOptions.engageThreshold &&
              widgetrail::input::kPinnedPlacementNavigationOptions.releaseThreshold ==
                  ordinaryOptions.releaseThreshold &&
              widgetrail::input::kPinnedPlacementNavigationOptions
                      .initialRepeatMilliseconds == 250 &&
              widgetrail::input::kPinnedPlacementNavigationOptions.repeatMilliseconds == 80,
          "pinned placement changes only the shared repeat cadence");
    std::array placementNavigators{
        widgetrail::input::StickNavigator{
            widgetrail::input::kPinnedPlacementNavigationOptions},
        widgetrail::input::StickNavigator{
            widgetrail::input::kPinnedPlacementNavigationOptions},
        widgetrail::input::StickNavigator{
            widgetrail::input::kPinnedPlacementNavigationOptions},
    };
    for (auto& placement : placementNavigators) {
        placement.Prime(0, 0, 0);
        const auto pressed = placement.UpdateEvent(20'000, 0, 10);
        Check(pressed && pressed->direction == NavigationDirection::Right &&
                  pressed->phase == widgetrail::input::NavigationEventPhase::Pressed,
              "each placement route emits its new direction immediately");
        Check(!placement.UpdateEvent(20'000, 0, 259),
              "each placement route waits the full 250-ms initial delay");
        const auto initialRepeat = placement.UpdateEvent(20'000, 0, 260);
        Check(initialRepeat && initialRepeat->phase ==
                  widgetrail::input::NavigationEventPhase::Repeated,
              "each placement route repeats at exactly 250 ms");
        Check(!placement.UpdateEvent(20'000, 0, 339),
              "each placement route waits the full 80-ms repeat interval");
        const auto steadyRepeat = placement.UpdateEvent(20'000, 0, 340);
        Check(steadyRepeat && steadyRepeat->phase ==
                  widgetrail::input::NavigationEventPhase::Repeated,
              "each placement route repeats every 80 ms");
    }
    auto& placementLifecycle = placementNavigators.front();
    placementLifecycle.Reset();
    placementLifecycle.Prime(-20'000, 0, 1'000);
    Check(!placementLifecycle.UpdateEvent(-20'000, 0, 1'001),
          "placement entry prime suppresses an inherited held direction");
    placementLifecycle.Reset();
    placementLifecycle.Prime(-20'000, 0, 1'100);
    Check(!placementLifecycle.UpdateEvent(-20'000, 0, 1'349),
          "placement reentry cannot inherit the prior repeat deadline");
    const auto reenteredRepeat =
        placementLifecycle.UpdateEvent(-20'000, 0, 1'350);
    Check(reenteredRepeat && reenteredRepeat->phase ==
              widgetrail::input::NavigationEventPhase::Repeated,
          "placement reentry starts one fresh bounded repeat cadence");
    placementLifecycle.Reset();
    placementLifecycle.Prime(0, 0, 1'400);
    const auto freshAfterExit =
        placementLifecycle.UpdateEvent(0, 20'000, 1'401);
    Check(freshAfterExit && freshAfterExit->phase ==
              widgetrail::input::NavigationEventPhase::Pressed,
          "placement exit reset permits exactly one later fresh direction");

    widgetrail::input::StickNavigator menuNavigation;
    menuNavigation.Prime(0, 0, 1'000);
    const auto menuDown = menuNavigation.UpdateEvent(0, -20'000, 1'010);
    const auto menuRepeat = menuNavigation.UpdateEvent(0, -20'000, 1'370);
    const auto menuReverse = menuNavigation.UpdateEvent(0, 20'000, 1'380);
    Check(menuDown && menuDown->direction == NavigationDirection::Down &&
              menuDown->phase == widgetrail::input::NavigationEventPhase::Pressed &&
              menuRepeat && menuRepeat->direction == NavigationDirection::Down &&
              menuRepeat->phase == widgetrail::input::NavigationEventPhase::Repeated &&
              menuReverse && menuReverse->direction == NavigationDirection::Up &&
              menuReverse->phase == widgetrail::input::NavigationEventPhase::Pressed,
          "vertical tray-menu navigation shares bounded press, repeat, and reversal cadence");
    menuNavigation.Reset();
    Check(!menuNavigation.UpdateEvent(0, 0, 1'390),
          "closing the tray menu retires its held navigation cadence");

    using widgetrail::input::FreeScrollAxis;
    using widgetrail::input::RightStickScrollKinetics;
    RightStickScrollKinetics freeScroll;
    const auto quiet = freeScroll.Update(8'000, -8'000, 1'000);
    Check(!quiet.moving && quiet.axis == FreeScrollAxis::None &&
              !quiet.returnedToDeadZone,
          "right-stick dead zone does not create free-scroll state");
    const auto verticalFull = freeScroll.Update(0, -32'767, 1'016);
    Check(verticalFull.moving && verticalFull.axis == FreeScrollAxis::Vertical &&
              std::abs(verticalFull.deltaDip - 35.2F) < 0.01F,
          "full vertical deflection uses the bounded initial sample rate");
    const auto verticalBounded = freeScroll.Update(0, -32'767, 1'200);
    Check(verticalBounded.moving && verticalBounded.deltaDip > verticalFull.deltaDip &&
              verticalBounded.deltaDip <= 110.01F,
          "long sampling gaps are capped at the bounded maximum delta");
    const auto verticalProportional = freeScroll.Update(0, -20'000, 1'216);
    Check(verticalProportional.moving && verticalProportional.deltaDip > 0.0F &&
              verticalProportional.deltaDip < verticalFull.deltaDip,
          "partial deflection produces a smaller proportional scroll delta");
    const auto verticalHysteresis = freeScroll.Update(18'000, -20'000, 1'232);
    Check(verticalHysteresis.axis == FreeScrollAxis::Vertical,
          "diagonal noise retains the engaged free-scroll axis");
    const auto horizontalSwitch = freeScroll.Update(26'000, -20'000, 1'248);
    Check(horizontalSwitch.axis == FreeScrollAxis::Horizontal &&
              horizontalSwitch.deltaDip > 0.0F,
          "a clearly dominant orthogonal deflection changes free-scroll axis");
    const auto horizontalReverse = freeScroll.Update(-26'000, -20'000, 1'264);
    Check(horizontalReverse.axis == FreeScrollAxis::Horizontal &&
              horizontalReverse.deltaDip < 0.0F,
          "horizontal free scroll preserves direction after axis selection");
    const auto releasedFreeScroll = freeScroll.Update(0, 0, 1'280);
    Check(!releasedFreeScroll.moving && releasedFreeScroll.returnedToDeadZone &&
              releasedFreeScroll.axis == FreeScrollAxis::None,
          "returning to the dead zone reports one pending re-entry boundary");
    const auto stillReleased = freeScroll.Update(0, 0, 1'296);
    Check(!stillReleased.returnedToDeadZone,
          "a held dead-zone sample cannot create a second re-entry boundary");
    freeScroll.Reset();
    const auto afterReset = freeScroll.Update(0, 32'767, 2'000);
    Check(afterReset.moving && afterReset.axis == FreeScrollAxis::Vertical &&
              afterReset.deltaDip < 0.0F &&
              std::abs(afterReset.deltaDip) <= 35.21F,
          "lifecycle reset retires prior timing and direction state");

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
    using widgetrail::input::OverlayMediaBackRoute;
    using widgetrail::input::RouteOverlayMediaBackButton;
    // Exhaustive over the three booleans that decide B for an overlay-projected
    // media widget. Fullscreen outranks widget Back, and both outrank the
    // widget's own bindings; changing that order fails here rather than
    // stranding a user in a presentation that paints no tray or guide.
    struct OverlayMediaBackCase final {
        bool fullscreenActive;
        bool authorityCurrent;
        bool mediaWidgetFocused;
        OverlayMediaBackRoute expected;
        const char* reason;
    };
    constexpr std::array<OverlayMediaBackCase, 8> overlayMediaBackCases{{
        {false, false, false, OverlayMediaBackRoute::Widget,
         "no media authority leaves B to the widget"},
        {false, false, true, OverlayMediaBackRoute::Widget,
         "focus alone never makes B host-owned"},
        {true, false, false, OverlayMediaBackRoute::Widget,
         "a stale media authority cannot exit fullscreen"},
        {true, false, true, OverlayMediaBackRoute::Widget,
         "a stale media authority never claims B"},
        {false, true, false, OverlayMediaBackRoute::Widget,
         "an unfocused media widget leaves B to the widget"},
        {false, true, true, OverlayMediaBackRoute::HostWidgetBack,
         "focused media widget B backs out to the dashboard"},
        {true, true, false, OverlayMediaBackRoute::ExitOverlayFullscreen,
         "fullscreen B exits the host-owned mode without widget focus"},
        {true, true, true, OverlayMediaBackRoute::ExitOverlayFullscreen,
         "fullscreen B exits the mode instead of backing out"},
    }};
    for (const auto& testCase : overlayMediaBackCases) {
        Check(RouteOverlayMediaBackButton(
                  L"B", testCase.fullscreenActive, testCase.authorityCurrent,
                  testCase.mediaWidgetFocused) == testCase.expected,
              testCase.reason);
    }
    constexpr std::array<std::wstring_view, 5> nonBackButtons{
        L"A", L"X", L"Y", L"LT", L"RT"};
    for (const auto button : nonBackButtons) {
        Check(RouteOverlayMediaBackButton(button, true, true, true) ==
                  OverlayMediaBackRoute::Widget,
              "fullscreen reserves B alone; every other button stays widget-owned");
    }
    using widgetrail::input::OverlayFullscreenMediaAuthority;
    using widgetrail::input::OverlayFullscreenMediaAuthorityCurrent;
    constexpr OverlayFullscreenMediaAuthority fullscreenAuthority{
        L"fixture", L"instance-1", L"runtime-1", L"presentation-1", L"media"};
    Check(OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority, fullscreenAuthority, true, true, false),
          "exact fullscreen authority remains current");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority,
              {L"other", L"instance-1", L"runtime-1", L"presentation-1", L"media"},
              true, true, false),
          "widget switch retires fullscreen authority");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority, fullscreenAuthority, false, true, false),
          "deactivation retires fullscreen authority");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority, fullscreenAuthority, true, false, false),
          "capability drop retires fullscreen authority");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority, fullscreenAuthority, true, true, true),
          "pinned takeover retires fullscreen authority");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority,
              {L"fixture", L"instance-1", L"runtime-2", L"presentation-1", L"media"},
              true, true, false),
          "worker restart retires fullscreen authority");
    Check(!OverlayFullscreenMediaAuthorityCurrent(
              fullscreenAuthority,
              {L"fixture", L"instance-1", L"runtime-1", L"presentation-2", L"media"},
              true, true, false),
          "presentation replacement retires fullscreen authority");
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

    using widgetrail::input::HeldButtonActionRepeat;
    using widgetrail::input::HeldButtonAuthorityState;
    constexpr std::uint32_t kLeftTrigger = 0x1'0000;
    constexpr std::uint32_t kRightTrigger = 0x2'0000;
    constexpr std::uint32_t kX = 0x4000;
    {
        // A tap is exactly one action: the initial press already dispatched,
        // and no repeat is due before the hold delay elapses.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kLeftTrigger, 1'000);
        Check(repeat.active(), "a begun hold owns its button");
        Check(repeat.button() == kLeftTrigger, "the hold reports its own button");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 1'100),
              "a held action emits nothing before the initial delay");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 1'359),
              "the initial delay is not short by a millisecond");
        Check(!repeat.Update(false, HeldButtonAuthorityState::Current, 1'200),
              "releasing before the delay leaves a tap as one action");
        Check(!repeat.active(), "release disarms the hold");
    }
    {
        // Hold repeats once at 360 ms and then every 125 ms.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kRightTrigger, 0);
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 360),
              "the first repeat lands exactly at the initial delay");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 484),
              "the steady cadence is not short by a millisecond");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 485),
              "the steady cadence repeats at 125 ms");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 610),
              "the steady cadence keeps repeating while held");
    }
    {
        // A refresh withholding presentation authority is this hold's own
        // consequence. It defers the emission and must not cancel the hold.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kLeftTrigger, 0);
        Check(!repeat.Update(true, HeldButtonAuthorityState::Deferred, 360),
              "a deferred tick emits nothing");
        Check(repeat.active(), "a deferred tick keeps the hold armed");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Deferred, 900),
              "a long deferral still emits nothing");
        Check(repeat.active(), "a long deferral still keeps the hold armed");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 901),
              "the first tick after a deferral emits once");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 1'000),
              "a deferral does not repay withheld ticks as a burst");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 1'026),
              "the cadence resumes from the emission that ended the deferral");
    }
    {
        // Backpressure: ticks that come due while the action is still owned
        // coalesce into one emission and never accumulate a backlog.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kX, 0);
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 10'000),
              "a long stall still emits once when it is next polled");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 10'001),
              "missed ticks coalesce instead of queueing a backlog");
    }
    {
        // Retirement is terminal: replaced authority cancels the hold, and a
        // still-physical press cannot revive it without a fresh edge.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kX, 0);
        Check(!repeat.Update(true, HeldButtonAuthorityState::Retired, 360),
              "retired authority emits nothing");
        Check(!repeat.active(), "retired authority disarms the hold");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 500),
              "a disarmed hold stays silent until a fresh press");
        repeat.Begin(kX, 500);
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 860),
              "a fresh press re-arms the hold from its full initial delay");
    }
    {
        // One owner, one button: a second held button replaces the first.
        HeldButtonActionRepeat repeat;
        repeat.Begin(kLeftTrigger, 0);
        repeat.Begin(kRightTrigger, 100);
        Check(repeat.button() == kRightTrigger,
              "the newest held button owns the single repeat slot");
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 400),
              "the replacing button restarts the initial delay");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 460),
              "the replacing button repeats on its own schedule");
        repeat.Reset();
        Check(!repeat.active(), "an explicit reset disarms the hold");
        Check(repeat.button() == 0, "a disarmed hold owns no button");
    }
    {
        // The cadence is fixed platform policy, but the owner still refuses a
        // degenerate zero interval that would emit on every tick.
        HeldButtonActionRepeat repeat{{0, 0}};
        repeat.Begin(kLeftTrigger, 0);
        Check(!repeat.Update(true, HeldButtonAuthorityState::Current, 0),
              "a zero initial delay is clamped to a real interval");
        Check(repeat.Update(true, HeldButtonAuthorityState::Current, 1),
              "the clamped interval still emits");
    }

    std::cout << "ControllerNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
