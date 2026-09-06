#include "HostAccessibility.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>

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
    const std::vector<widgetrail::accessibility::TrayItem> items{
        {L"audio", L"Audio Mixer"},
        {L"music", L"YT Music"},
        {L"performance", L"Performance", false},
        {L"gallery", L"SDK Gallery"},
    };
    const auto layout = widgetrail::shell::ComputeTrayLayout(240, 500, items.size(), 2);
    Check(layout && layout->tiles.size() == 1 && layout->previousOverflow &&
          layout->nextOverflow, "fixture exposes one tile between explicit overflow controls");
    const auto tree = widgetrail::accessibility::BuildTrayTree(items, *layout, 2, 17);
    Check(tree.widgetId == L"host.tray" && tree.runtimeGeneration == L"host" &&
          tree.snapshotSequence == 17 && tree.activeInputScopeId == L"host.tray",
          "host tree retains closed shell authority");
    Check(tree.nodes.size() == 3 && tree.nodes[0].hostTargetId == L"music" &&
          tree.nodes[1].hostTargetId == L"performance" &&
          tree.nodes[2].hostTargetId == L"gallery",
          "overflow controls and visible tile preserve catalog reachability order");
    Check(tree.nodes[0].hostAction ==
              widgetrail::accessibility::HostAction::SelectTrayOverflow &&
          tree.nodes[0].name == L"2 previous widgets" &&
          tree.nodes[2].name == L"1 more widgets",
          "overflow controls announce direction and hidden count without activation");
    Check(tree.nodes[1].name == L"Performance" &&
          tree.nodes[1].role == widgetrail::accessibility::Role::ListItem &&
          tree.nodes[1].domain == widgetrail::accessibility::ElementDomain::Tray,
          "tray item exposes its accessible list-item name");
    Check(tree.nodes[1].hostAction == widgetrail::accessibility::HostAction::ActivateTrayItem,
          "tray activation is a closed typed action");
    Check(tree.nodes[1].selected && tree.nodes[1].focused && !tree.nodes[1].enabled &&
          tree.focusedNode == 1 && tree.nodes[1].positionInSet == 3 &&
          tree.nodes[1].sizeOfSet == 4,
          "selected, focused, and enabled states remain independent");
    Check(tree.nodes[1].bounds.x == layout->tiles[0].bounds.x &&
          tree.nodes[1].bounds.y == layout->tiles[0].bounds.y &&
          tree.nodes[1].bounds.width == layout->tiles[0].bounds.width &&
          tree.nodes[1].bounds.height == layout->tiles[0].bounds.height,
          "accessibility uses the exact shared paint/hit-test rectangle");
    Check(std::none_of(
              tree.nodes.begin(), tree.nodes.end(), [&](const auto& node) {
                  return node.bounds.x == layout->stripBounds.x &&
                      node.bounds.y == layout->stripBounds.y &&
                      node.bounds.width == layout->stripBounds.width &&
                      node.bounds.height == layout->stripBounds.height;
              }),
          "removed outer tray background contributes no accessibility element or bounds");

    const widgetrail::accessibility::DashboardSemantics dashboard{
        L"Reorder widgets", {24, 10, 192, 34},
        L"A Select  B Done", {24, 44, 192, 22},
        L"", {24, 44, 192, 22},
    };
    const auto dashboardTree = widgetrail::accessibility::BuildTrayTree(
        items, *layout, 2, 18, &dashboard);
    Check(dashboardTree.nodes.size() == 5 &&
          dashboardTree.nodes[0].id == L"host.dashboard.title" &&
          dashboardTree.nodes[0].domain == widgetrail::accessibility::ElementDomain::HostShell &&
          dashboardTree.nodes[0].role == widgetrail::accessibility::Role::Heading &&
          dashboardTree.nodes[0].headingLevel == widgetrail::accessibility::HeadingLevel::Level1,
          "dashboard title is a level-one heading with exact host-owned text");
    Check(dashboardTree.nodes[0].bounds.x == dashboard.titleBounds.x &&
          dashboardTree.nodes[0].bounds.y == dashboard.titleBounds.y &&
          dashboardTree.nodes[4].bounds.width == dashboard.helpBounds.width &&
          dashboardTree.nodes[4].bounds.height == dashboard.helpBounds.height,
          "dashboard semantic text uses the exact paint rectangles");
    Check(dashboardTree.nodes[4].id == L"host.dashboard.help" &&
          dashboardTree.nodes[4].name == dashboard.help &&
          dashboardTree.nodes[4].role == widgetrail::accessibility::Role::Text &&
          dashboardTree.nodes[4].liveSetting == widgetrail::accessibility::LiveSetting::Off,
          "routine dashboard guidance is discoverable without becoming live");
    Check(dashboardTree.focusedNode == 2 && dashboardTree.nodes[2].hostTargetId == L"performance",
          "non-focusable dashboard text does not disturb tray focus identity");
    const auto revision = widgetrail::accessibility::ComputeTraySemanticRevision(items, &dashboard);
    auto changedDashboard = dashboard;
    changedDashboard.help.clear();
    changedDashboard.status = L"Playback command failed";
    Check(revision != widgetrail::accessibility::ComputeTraySemanticRevision(
              items, &changedDashboard),
          "dashboard status changes invalidate the host projection revision");
    const auto statusTree = widgetrail::accessibility::BuildTrayTree(
        items, *layout, 2, 19, &changedDashboard);
    Check(statusTree.nodes.size() == 5 &&
          statusTree.nodes[4].id == L"host.dashboard.status" &&
          statusTree.nodes[4].role == widgetrail::accessibility::Role::Status &&
          statusTree.nodes[4].liveSetting == widgetrail::accessibility::LiveSetting::Polite,
          "transient dashboard feedback replaces help with one polite status region");
    auto renamedItems = items;
    renamedItems[1].name = L"YouTube Music";
    Check(revision != widgetrail::accessibility::ComputeTraySemanticRevision(
              renamedItems, &dashboard),
          "catalog display-name changes invalidate the host projection revision");

    auto pinDashboard = dashboard;
    pinDashboard.contextMenu.targetId = L"music";
    pinDashboard.contextMenu.items = {{
        L"host.tray.context.primary", L"Pin YT Music",
        L"Not pinned; creates a Click-through surface", L"music",
        {12, 100, 216, 48},
        widgetrail::accessibility::HostAction::PinTrayWidget, true, true,
    }};
    const auto pinMenuTree = widgetrail::accessibility::BuildTrayTree(
        items, *layout, 2, 20, &pinDashboard);
    const auto pinNode = std::find_if(
        pinMenuTree.nodes.begin(), pinMenuTree.nodes.end(), [](const auto& node) {
            return node.id == L"host.tray.context.primary";
        });
    Check(pinNode != pinMenuTree.nodes.end() &&
              pinNode->hostAction == widgetrail::accessibility::HostAction::PinTrayWidget &&
              pinNode->hostTargetId == L"music" && pinNode->enabled &&
              pinNode->focused && pinNode->bounds.x == 12 &&
              pinNode->bounds.y == 100 && pinNode->bounds.width == 216 &&
              pinNode->bounds.height == 48,
          "visible Pin exposes one enabled focused host action with exact shared row geometry");
    Check(pinMenuTree.focusedNode &&
              pinMenuTree.nodes[*pinMenuTree.focusedNode].id ==
                  L"host.tray.context.primary" &&
              std::none_of(pinMenuTree.nodes.begin(), pinMenuTree.nodes.end(),
                           [](const auto& node) {
                               return node.domain ==
                                          widgetrail::accessibility::ElementDomain::Tray &&
                                   node.focused;
                           }),
          "open host menu owns one focus identity without leaving tray focus duplicated");

    auto pinnedDashboard = dashboard;
    pinnedDashboard.contextMenu.targetId = L"music";
    pinnedDashboard.contextMenu.items = {
        {L"host.tray.context.primary", L"Adjust pinned widget",
         L"Move with left stick or D-pad; resize with right stick", L"music",
         {12, 8, 216, 48},
         widgetrail::accessibility::HostAction::AdjustPinnedSurface, true, false},
        {L"host.tray.context.opacity", L"Opacity — 70%",
         L"Adjust whole pinned surface opacity from 30 to 100 percent", L"music",
         {12, 56, 216, 48},
         widgetrail::accessibility::HostAction::AdjustPinnedOpacity, true, true},
        {L"host.tray.context.unpin", L"Unpin", L"Remove the pinned surface", L"music",
         {12, 104, 216, 48},
         widgetrail::accessibility::HostAction::UnpinSurface, true, false},
    };
    const auto pinnedMenuTree = widgetrail::accessibility::BuildTrayTree(
        items, *layout, 2, 21, &pinnedDashboard);
    const auto firstMenuNode = std::find_if(
        pinnedMenuTree.nodes.begin(), pinnedMenuTree.nodes.end(), [](const auto& node) {
            return node.id == L"host.tray.context.primary";
        });
    Check(firstMenuNode != pinnedMenuTree.nodes.end() &&
              std::distance(firstMenuNode, pinnedMenuTree.nodes.end()) >= 3 &&
              firstMenuNode[0].hostAction ==
                  widgetrail::accessibility::HostAction::AdjustPinnedSurface &&
              firstMenuNode[1].hostAction ==
                  widgetrail::accessibility::HostAction::AdjustPinnedOpacity &&
              firstMenuNode[2].hostAction ==
                  widgetrail::accessibility::HostAction::UnpinSurface &&
              firstMenuNode[0].bounds.y + firstMenuNode[0].bounds.height ==
                  firstMenuNode[1].bounds.y &&
              firstMenuNode[1].bounds.y + firstMenuNode[1].bounds.height ==
                  firstMenuNode[2].bounds.y,
          "Adjust, Opacity, and Unpin retain top-to-bottom action and shared-row order");

    widgetrail::accessibility::Tree widgetTree;
    widgetTree.widgetId = L"music";
    widgetTree.runtimeGeneration = L"music-v1";
    widgetTree.snapshotSequence = 42;
    widgetTree.activeInputScopeId = L"music.root";
    widgetrail::accessibility::Node play;
    play.id = L"host.open.back";
    play.name = L"Play";
    play.actionId = L"music.play";
    play.bounds = {20, 80, 120, 44};
    play.role = widgetrail::accessibility::Role::Button;
    play.focused = true;
    widgetTree.nodes.push_back(play);
    widgetTree.focusedNode = 0;
    const widgetrail::accessibility::OpenWidgetSemantics open{
        L"YT Music", widgetrail::accessibility::HostAction::BackToTray, L"music.root",
        {320, 440, 80, 30}, {400, 440, 100, 30},
        L"X Play  LB Previous  RB Next", {20, 440, 286, 30},
        L"", {20, 440, 286, 30},
    };
    const auto openLayout = widgetrail::shell::ComputeTrayLayout(240, 500, items.size(), 1);
    Check(openLayout && openLayout->tiles.front().slot == 1,
          "open fixture keeps its selected tray identity visible");
    const auto openTree = widgetrail::accessibility::BuildOpenWidgetTree(
        widgetTree, items, *openLayout, 1, false, open);
    auto nestedOpen = open;
    nestedOpen.backAction = widgetrail::accessibility::HostAction::BackWithinWidget;
    nestedOpen.backTargetId = L"music.sheet";
    Check(widgetrail::accessibility::ComputeOpenWidgetSemanticRevision(items, open) !=
          widgetrail::accessibility::ComputeOpenWidgetSemanticRevision(items, nestedOpen),
          "Back action and target participate in composite projection revision");
    Check(openTree.widgetId == L"music" &&
          openTree.runtimeGeneration == L"music-v1" &&
          openTree.snapshotSequence == 42 &&
          openTree.name == L"YT Music · WidgetRail",
          "open shell retains widget authority and publishes page context on the root");
    Check(openTree.focusedNode == 0 && openTree.nodes[0].focused,
          "widget focus remains the singular composite focus owner");
    Check(openTree.nodes[1].id == L"host.open.back" &&
          openTree.nodes[0].domain == widgetrail::accessibility::ElementDomain::Widget &&
          openTree.nodes[1].domain == widgetrail::accessibility::ElementDomain::HostShell &&
          openTree.nodes[1].hostAction == widgetrail::accessibility::HostAction::BackToTray &&
          !openTree.nodes[1].keyboardFocusable &&
          openTree.nodes[2].id == L"host.open.close" &&
          openTree.nodes[2].hostAction == widgetrail::accessibility::HostAction::CloseOverlay &&
          !openTree.nodes[2].keyboardFocusable,
          "open shell exposes closed non-focus-stealing Back and Close commands");
    Check(widgetrail::accessibility::HasUniqueElementKeys(openTree) &&
          widgetrail::accessibility::AutomationId(openTree.nodes[0]) ==
              L"widget:host.open.back" &&
          widgetrail::accessibility::AutomationId(openTree.nodes[1]) ==
              L"host:host.open.back",
          "widget and host elements may share raw IDs without identity collision");
    Check(openTree.nodes[3].id == L"host.open.help" &&
          openTree.nodes[3].liveSetting == widgetrail::accessibility::LiveSetting::Off &&
          openTree.nodes[3].bounds.width == open.helpBounds.width,
          "visible widget shortcut guidance is readable non-live text with exact bounds");
    Check(openTree.nodes.size() == 7 &&
          openTree.nodes[4].id == L"overflow.previous" &&
          openTree.nodes[5].id == L"tray.music" &&
          openTree.nodes[6].id == L"overflow.next" &&
          !openTree.nodes[4].focused && !openTree.nodes[5].focused &&
          !openTree.nodes[6].focused,
          "open shell appends overflow and visible tray semantics without stealing widget focus");

    auto statusOpen = open;
    statusOpen.help.clear();
    statusOpen.status = L"Playback command failed";
    const auto trayFocusedTree = widgetrail::accessibility::BuildOpenWidgetTree(
        widgetTree, items, *layout, 2, true, statusOpen);
    Check(trayFocusedTree.focusedNode == 5 &&
          !trayFocusedTree.nodes[0].focused && trayFocusedTree.nodes[5].focused,
          "tray focus clears widget focus and selects exactly one visible tray item");
    Check(trayFocusedTree.nodes[3].id == L"host.open.status" &&
          trayFocusedTree.nodes[3].role == widgetrail::accessibility::Role::Status &&
          trayFocusedTree.nodes[3].liveSetting == widgetrail::accessibility::LiveSetting::Polite,
          "transient open-widget feedback replaces static help with one polite status");

    widgetrail::WidgetSnapshot nested;
    nested.sequence = 43;
    nested.activeInputScopeId = L"music.sheet";
    nested.root.id = L"music.root";
    nested.root.inputScopeId = L"music.root";
    widgetrail::WidgetNode sheet;
    sheet.id = L"sheet";
    sheet.inputScopeId = L"music.sheet";
    sheet.shortcuts.push_back({L"b", L"sheet.back", L"pressed"});
    widgetrail::WidgetNode sheetAction;
    sheetAction.id = L"sheet.action";
    sheetAction.kind = L"button";
    sheet.children.push_back(sheetAction);
    nested.root.children.push_back(sheet);
    Check(widgetrail::accessibility::HasActiveScopeBackShortcut(nested, {}) &&
          widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action") &&
          widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackWithinWidget,
              L"music.sheet", nested, L"sheet.action") &&
          !widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackToTray,
              L"music.sheet", nested, L"sheet.action") &&
          !widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackWithinWidget,
              L"music.stale", nested, L"sheet.action"),
          "focusless and descendant focus resolve the active scope's B shortcut");

    nested.root.children[0].children[0].shortcuts.push_back(
        {L"b", L"action.back", L"pressed"});
    Check(widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "an enabled focused B shortcut publishes nested Back");
    nested.root.children[0].children[0].isDisabled = true;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "a disabled focused B shortcut suppresses ancestor fallback");
    nested.root.children[0].children[0].isDisabled = false;
    nested.root.children[0].children[0].isBusy = true;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "a busy focused B shortcut suppresses ancestor fallback");
    nested.root.children[0].children[0].isBusy = false;
    nested.root.children[0].children[0].shortcuts.clear();
    nested.root.children[0].children[0].isDisabled = true;
    Check(widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "a disabled focus without its own B still resolves the ancestor shortcut");
    nested.root.children[0].children[0].isDisabled = false;
    nested.root.children[0].isDisabled = true;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "a disabled ancestor B owner is not published to accessibility");
    nested.root.children[0].isDisabled = false;
    nested.root.children[0].isBusy = true;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.action"),
          "a busy ancestor B owner is not published to accessibility");
    nested.root.children[0].isBusy = false;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"sheet.missing"),
          "stale focused identity suppresses nested Back publication");

    widgetrail::WidgetNode innerScope;
    innerScope.id = L"inner";
    innerScope.inputScopeId = L"music.inner";
    widgetrail::WidgetNode innerAction;
    innerAction.id = L"inner.action";
    innerAction.kind = L"button";
    innerScope.children.push_back(innerAction);
    nested.root.children[0].children.push_back(innerScope);
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(
              nested, L"inner.action"),
          "focus in another input scope cannot authorize the active scope's Back");

    nested.root.children[0].isBusy = true;
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(nested, {}),
          "a focusless busy scope root does not publish Back");
    nested.root.children[0].isBusy = false;
    nested.root.children[0].shortcuts[0].phase = L"released";
    Check(!widgetrail::accessibility::HasActiveScopeBackShortcut(nested, {}) &&
          !widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackWithinWidget,
              L"music.sheet", nested, {}),
          "missing pressed-B authority suppresses nested Back without tray fallback");

    widgetrail::WidgetSnapshot presentedRoot;
    presentedRoot.activeInputScopeId = L"music.root";
    presentedRoot.root.id = L"music.background";
    presentedRoot.root.kind = L"backgroundSurface";
    widgetrail::WidgetNode focusPresentation;
    focusPresentation.id = L"music.presentation";
    focusPresentation.kind = L"focusPresentationSurface";
    widgetrail::WidgetNode presentedContent;
    presentedContent.id = L"music.content";
    presentedContent.kind = L"stack";
    presentedContent.inputScopeId = L"music.root";
    presentedContent.children.push_back(sheetAction);
    focusPresentation.children.push_back(std::move(presentedContent));
    presentedRoot.root.children.push_back(std::move(focusPresentation));
    Check(widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackToTray,
              L"music.root", presentedRoot, L"sheet.action") &&
          !widgetrail::accessibility::IsCurrentBackAction(
              widgetrail::accessibility::HostAction::BackWithinWidget,
              L"music.root", presentedRoot, L"sheet.action"),
          "presentation-only root wrappers preserve accessible host Back authority");

    std::cout << "HostAccessibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
