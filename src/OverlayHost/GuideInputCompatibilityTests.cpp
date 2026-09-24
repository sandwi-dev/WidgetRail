#include "GuideInputCompatibility.h"
#include "ControllerGuide.h"
#include "DeclarativeRenderer.h"

#include <array>
#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <utility>

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
    widgetrail::input::GuideEdgeTracker tracker;
    std::array<bool, XUSER_MAX_COUNT> state{};
    Check(tracker.Update(state) == 0, "initial neutral state is only a baseline");
    state[0] = true;
    Check(tracker.Update(state) == 0x01, "slot zero rising edge is reported");
    Check(tracker.Update(state) == 0, "held Guide does not repeat");
    state[0] = false;
    Check(tracker.Update(state) == 0, "release does not toggle");
    state[1] = true;
    state[3] = true;
    Check(tracker.Update(state) == 0x0A, "simultaneous slots retain identity");
    state = {};
    Check(tracker.Update(state) == 0, "multi-slot release is quiet");

    widgetrail::input::GuideCompatibilityActivation activation;
    widgetrail::input::GuideCompatibilityActivation::DeviceId first{};
    widgetrail::input::GuideCompatibilityActivation::DeviceId second{};
    first[0] = 1;
    second[0] = 2;
    Check(!activation.active() && activation.deviceCount() == 0,
          "compatibility polling starts dormant");
    Check(activation.Update(first, true) && activation.active(),
          "first legacy connection activates compatibility polling");
    Check(!activation.Update(first, true) && activation.deviceCount() == 1,
          "duplicate connection notification is idempotent");
    Check(!activation.Update(second, true) && activation.deviceCount() == 2,
          "additional legacy devices retain one active timer");
    Check(!activation.Update(first, false) && activation.active(),
          "disconnecting one of two devices retains polling");
    Check(activation.Update(second, false) && !activation.active(),
          "last legacy disconnect disables polling");
    Check(!activation.Update(first, false),
          "duplicate disconnect notification is idempotent");
    Check(activation.Update(first, true),
          "compatibility polling can reactivate after reconnect");
    activation.Reset();
    Check(!activation.active() && activation.deviceCount() == 0,
          "reset clears tracked devices");

    widgetrail::WidgetSnapshot snapshot;
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    widgetrail::WidgetNode select;
    select.id = L"density";
    select.kind = L"button";
    select.isSelect = true;
    select.text = L"Density";
    widgetrail::WidgetSelectOption compact;
    compact.id = L"compact";
    compact.label = L"Compact";
    compact.actionId = L"density.compact";
    compact.isSelected = true;
    select.selectOptions.push_back(compact);
    snapshot.root.children.push_back(std::move(select));
    const auto selectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(selectAuthority.focusedActivation,
          "focused Select publishes one exact activation guide authority");
    const auto selectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, selectAuthority, true, 500.0F,
        [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(selectLine.contextual == L"A Select" &&
              selectLine.host == L"B Back   Guide Close" &&
              selectLine.accessible == L"A Select   B Back   Guide Close",
          "focused Select guide and accessibility text retain direct A activation");

    snapshot.root.children[0].selectOptions[0].isDisabled = true;
    const auto disabledSelectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    const auto disabledSelectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, disabledSelectAuthority, true,
        500.0F, [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(!disabledSelectAuthority.focusedActivation &&
              disabledSelectLine.contextual.empty() &&
              disabledSelectLine.accessible == L"B Back   Guide Close",
          "all-disabled Select options publish no A Select guide authority");

    snapshot.root.children[0].selectOptions[0].isDisabled = false;
    snapshot.root.children[0].selectOptions[0].isBusy = true;
    const auto busySelectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    const auto busySelectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, busySelectAuthority, true,
        500.0F, [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(!busySelectAuthority.focusedActivation &&
              busySelectLine.contextual.empty() &&
              busySelectLine.accessible == L"B Back   Guide Close",
          "all-busy Select options publish no A Select guide authority");

    snapshot.root.children[0].selectOptions[0].isBusy = false;
    snapshot.root.text = L"Root command";
    snapshot.root.shortcuts.push_back({L"x", L"root.command", L"pressed"});
    snapshot.root.isDisabled = true;
    Check(widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density")
              .actions.empty(),
          "a disabled ancestor owner is not advertised by the guide");
    snapshot.root.isDisabled = false;
    snapshot.root.isBusy = true;
    Check(widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density")
              .actions.empty(),
          "a busy ancestor owner is not advertised by the guide");
    snapshot.root.isBusy = false;
    snapshot.quickActions.push_back(
        {L"x", L"root.command", L"Dashboard label", L"none"});
    const auto enabledAncestor =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(enabledAncestor.actions.size() == 1 &&
              enabledAncestor.actions[0].button == L"x" &&
              enabledAncestor.actions[0].label == L"Root command",
          "owner text wins without borrowing the dashboard QuickAction label");

    snapshot.root.shortcuts[0].label = L"Explicit command";
    const auto explicitlyLabeled =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(explicitlyLabeled.actions.size() == 1 &&
              explicitlyLabeled.actions[0].label == L"Explicit command",
          "an explicit shortcut label wins over owner and QuickAction text");

    snapshot.root.shortcuts[0].label.clear();
    snapshot.root.text.clear();
    snapshot.root.accessibilityLabel = L"Accessible owner";
    const auto accessibleOwner =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(accessibleOwner.actions.size() == 1 &&
              accessibleOwner.actions[0].label == L"Accessible owner",
          "direct owner accessibility text is the final guide fallback");

    snapshot.root.accessibilityLabel.clear();
    Check(widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density")
              .actions.empty(),
          "an unlabeled owner never borrows dashboard QuickAction text");

    const auto chordLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, {}, true, 500.0F,
        [](std::wstring_view text) { return std::optional<float>(static_cast<float>(text.size()) * 6); }, true);
    Check(chordLine.host == L"B Back   View + Menu Close" &&
          chordLine.accessible.find(L"View + Menu Close") != std::wstring::npos,
          "selected chord owns both the visible and accessible close hint");
    {
        using namespace widgetrail;
        guide::OpenWidgetAuthority authority;
        authority.focusedActivation=true;
        authority.actions={{L"leftBumper",L"Previous"},{L"rightTrigger",L"  Next\npage "},{L"view",L"Details"}};
        const auto measure=[](std::wstring_view text)->std::optional<float> {return static_cast<float>(text.size());};
        const auto cards=[](std::span<const ControllerGuideHint> hints)->std::optional<float> {return hints.size()*100.0F;};
        const auto fitted=guide::BuildOpenWidgetLine(ControllerGuideDensity::Full,authority,true,400,measure,true,cards);
        Check(fitted.hints.size()==4,"card measurement retains only whole contextual hints plus host escapes");
        Check(fitted.hints[0].button==L"leftBumper" && fitted.hints[1].button==L"rightTrigger",
            "structured hints preserve exact resolved button priority");
        Check(fitted.hints[1].label==L"Next page","structured hints sanitize labels consistently");
        Check(fitted.hints[2].button==L"B" && fitted.hints[3].button==L"View + Menu",
            "host Back and configured Close chord remain separate structured hints");
        const auto changed=guide::BuildOpenWidgetLine(ControllerGuideDensity::Full,{},false,400,measure,false,cards);
        Check(changed.hints.size()==1 && changed.hints[0].button==L"Guide",
            "changing focused authority cannot retain a stale contextual hint");
    }
    {
        using namespace widgetrail;
        WidgetSnapshot menus;
        menus.activeInputScopeId = L"root";
        menus.root.id = L"root"; menus.root.kind = L"stack"; menus.root.inputScopeId = L"root";
        menus.root.shortcuts = {{L"x", L"play", L"pressed", L"none", L"Play"},
            {L"leftTrigger", L"prev", L"pressed", L"none", L"Previous tab"},
            {L"rightTrigger", L"next", L"pressed", L"none", L"Next tab"},
            {L"y", L"setup", L"pressed", L"none", L"Settings"}};
        WidgetNode tile; tile.id = L"tile"; tile.kind = L"actionSurface"; tile.actionId = L"open";
        tile.contextActions = {{L"queue", L"Play next"}};
        menus.root.children = {tile};
        RenderResult rendered;
        rendered.focusRects[L"tile"] = {0, 0, 150, 150};
        const auto authority = [&] { return guide::ResolveOpenWidgetAuthority(menus, L"tile", &rendered); };
        auto resolved = authority();
        Check(!resolved.actions.empty() && resolved.actions.front().button == L"menu" && resolved.actions.front().label == L"Options" && resolved.actions.front().contextMenu,
            "legacy tile Menu binding is advertised ahead of ordinary shortcuts");
        const auto measure = [](std::wstring_view text) -> std::optional<float> { return static_cast<float>(text.size()); };
        const auto cards = [](std::span<const ControllerGuideHint> hints) -> std::optional<float> { return hints.size() * 100.0F; };
        const auto line = guide::BuildOpenWidgetLine(ControllerGuideDensity::Compact, resolved, true, 300, measure, false, cards);
        Check(line.hints.size() == 3 && line.hints[0].button == L"menu" && line.hints[0].label == L"Options" &&
            line.hints[1].button == L"B" && line.hints[2].button == L"Guide", "Options survives a one-context-hint budget with both host escapes");
        Check(line.accessible.find(L"Menu Options") != std::wstring::npos, "Options is included in guide accessibility text");
        menus.root.children[0].contextMenuButton = L"x";
        resolved = authority();
        Check(resolved.actions.front().button == L"x" && resolved.actions.front().label == L"Options" &&
            std::count_if(resolved.actions.begin(), resolved.actions.end(), [](const auto& a) { return a.button == L"x"; }) == 1,
            "X context menu replaces conflicting playback hint");
        menus.root.children[0].contextActions[0].isDisabled = true;
        resolved = authority();
        Check(std::none_of(resolved.actions.begin(), resolved.actions.end(), [](const auto& a) { return a.button == L"x"; }),
            "all-disabled menu suppresses both Options and the shortcut it consumes");
        menus.root.children[0].contextActions[0].isDisabled = false;
        menus.root.children[0].contextActions[0].isBusy = true;
        Check(!authority().actions.front().contextMenu, "all-busy menu is not advertised");
        menus.root.children[0].contextActions[0].isBusy = false;
        rendered.focusRects.clear();
        Check(!authority().actions.front().contextMenu, "offscreen tile does not advertise a menu");
        WidgetNode container; container.id = L"library.options"; container.kind = L"row";
        container.contextMenuButton = L"y"; container.contextActions = {{L"library", L"Library"}};
        menus.root.children.push_back(container);
        rendered.contextMenuRects[container.id] = {160, 0, 50, 30};
        resolved = authority();
        Check(resolved.actions.front().button == L"y" && resolved.actions.front().contextMenu,
            "visible non-focusable container menu is advertised with its actual binding");
        auto otherMenu = container; otherMenu.id = L"other.options";
        menus.root.children.push_back(otherMenu);
        rendered.contextMenuRects[otherMenu.id] = {220, 0, 50, 30};
        Check(!authority().actions.front().contextMenu, "ambiguous container menus are not advertised");
        menus.root.children.pop_back();
        menus.root.children[1].inputScopeId = L"other";
        Check(!authority().actions.front().contextMenu, "container menu cannot cross input scopes");
        menus.root.children[1].inputScopeId.clear();
        menus.root.isDisabled = true;
        Check(authority().actions.empty(), "disabled ancestor cannot advertise context or ordinary actions");
    }
    std::cout << "GuideInputCompatibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
