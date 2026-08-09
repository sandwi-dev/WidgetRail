#include "HostAccessibility.h"

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
    const std::vector<gba::accessibility::TrayItem> items{
        {L"audio", L"Audio Mixer"},
        {L"music", L"YT Music"},
        {L"performance", L"Performance", false},
        {L"gallery", L"SDK Gallery"},
    };
    const auto layout = gba::shell::ComputeTrayLayout(240, 500, items.size(), 2);
    Check(layout && layout->tiles.size() == 2, "fixture exposes a clipped tray window");
    const auto tree = gba::accessibility::BuildTrayTree(items, *layout, 2, 17);
    Check(tree.widgetId == L"host.tray" && tree.runtimeGeneration == L"host" &&
          tree.snapshotSequence == 17 && tree.activeInputScopeId == L"host.tray",
          "host tree retains closed shell authority");
    Check(tree.nodes.size() == 2 && tree.nodes[0].hostTargetId == L"music" &&
          tree.nodes[1].hostTargetId == L"performance",
          "only final visible tray items are published in catalog order");
    Check(tree.nodes[0].name == L"YT Music" &&
          tree.nodes[0].role == gba::accessibility::Role::ListItem &&
          tree.nodes[0].domain == gba::accessibility::ElementDomain::Tray,
          "tray item exposes its accessible list-item name");
    Check(tree.nodes[1].hostAction == gba::accessibility::HostAction::ActivateTrayItem,
          "tray activation is a closed typed action");
    Check(tree.nodes[1].selected && tree.nodes[1].focused && !tree.nodes[1].enabled &&
          tree.focusedNode == 1,
          "selected, focused, and enabled states remain independent");
    Check(tree.nodes[0].bounds.x == layout->tiles[0].bounds.x &&
          tree.nodes[0].bounds.y == layout->tiles[0].bounds.y &&
          tree.nodes[0].bounds.width == layout->tiles[0].bounds.width &&
          tree.nodes[0].bounds.height == layout->tiles[0].bounds.height,
          "accessibility uses the exact shared paint/hit-test rectangle");

    const gba::accessibility::DashboardSemantics dashboard{
        L"Reorder widgets", {24, 10, 192, 34},
        L"A Select  B Done", {24, 44, 192, 22},
        L"", {24, 44, 192, 22},
    };
    const auto dashboardTree = gba::accessibility::BuildTrayTree(
        items, *layout, 2, 18, &dashboard);
    Check(dashboardTree.nodes.size() == 4 &&
          dashboardTree.nodes[0].id == L"host.dashboard.title" &&
          dashboardTree.nodes[0].domain == gba::accessibility::ElementDomain::HostShell &&
          dashboardTree.nodes[0].role == gba::accessibility::Role::Heading &&
          dashboardTree.nodes[0].headingLevel == gba::accessibility::HeadingLevel::Level1,
          "dashboard title is a level-one heading with exact host-owned text");
    Check(dashboardTree.nodes[0].bounds.x == dashboard.titleBounds.x &&
          dashboardTree.nodes[0].bounds.y == dashboard.titleBounds.y &&
          dashboardTree.nodes[3].bounds.width == dashboard.helpBounds.width &&
          dashboardTree.nodes[3].bounds.height == dashboard.helpBounds.height,
          "dashboard semantic text uses the exact paint rectangles");
    Check(dashboardTree.nodes[3].id == L"host.dashboard.help" &&
          dashboardTree.nodes[3].name == dashboard.help &&
          dashboardTree.nodes[3].role == gba::accessibility::Role::Text &&
          dashboardTree.nodes[3].liveSetting == gba::accessibility::LiveSetting::Off,
          "routine dashboard guidance is discoverable without becoming live");
    Check(dashboardTree.focusedNode == 2 && dashboardTree.nodes[2].hostTargetId == L"performance",
          "non-focusable dashboard text does not disturb tray focus identity");
    const auto revision = gba::accessibility::ComputeTraySemanticRevision(items, &dashboard);
    auto changedDashboard = dashboard;
    changedDashboard.help.clear();
    changedDashboard.status = L"Playback command failed";
    Check(revision != gba::accessibility::ComputeTraySemanticRevision(
              items, &changedDashboard),
          "dashboard status changes invalidate the host projection revision");
    const auto statusTree = gba::accessibility::BuildTrayTree(
        items, *layout, 2, 19, &changedDashboard);
    Check(statusTree.nodes.size() == 4 &&
          statusTree.nodes[3].id == L"host.dashboard.status" &&
          statusTree.nodes[3].role == gba::accessibility::Role::Status &&
          statusTree.nodes[3].liveSetting == gba::accessibility::LiveSetting::Polite,
          "transient dashboard feedback replaces help with one polite status region");
    auto renamedItems = items;
    renamedItems[1].name = L"YouTube Music";
    Check(revision != gba::accessibility::ComputeTraySemanticRevision(
              renamedItems, &dashboard),
          "catalog display-name changes invalidate the host projection revision");

    gba::accessibility::Tree widgetTree;
    widgetTree.widgetId = L"music";
    widgetTree.runtimeGeneration = L"music-v1";
    widgetTree.snapshotSequence = 42;
    widgetTree.activeInputScopeId = L"music.root";
    gba::accessibility::Node play;
    play.id = L"host.open.back";
    play.name = L"Play";
    play.actionId = L"music.play";
    play.bounds = {20, 80, 120, 44};
    play.role = gba::accessibility::Role::Button;
    play.focused = true;
    widgetTree.nodes.push_back(play);
    widgetTree.focusedNode = 0;
    const gba::accessibility::OpenWidgetSemantics open{
        L"YT Music", gba::accessibility::HostAction::BackToTray, L"music.root",
        {320, 440, 80, 30}, {400, 440, 100, 30},
        L"X Play  LB Previous  RB Next", {20, 440, 286, 30},
        L"", {20, 440, 286, 30},
    };
    const auto openTree = gba::accessibility::BuildOpenWidgetTree(
        widgetTree, items, *layout, 1, false, open);
    auto nestedOpen = open;
    nestedOpen.backAction = gba::accessibility::HostAction::BackWithinWidget;
    nestedOpen.backTargetId = L"music.sheet";
    Check(gba::accessibility::ComputeOpenWidgetSemanticRevision(items, open) !=
          gba::accessibility::ComputeOpenWidgetSemanticRevision(items, nestedOpen),
          "Back action and target participate in composite projection revision");
    Check(openTree.widgetId == L"music" &&
          openTree.runtimeGeneration == L"music-v1" &&
          openTree.snapshotSequence == 42 &&
          openTree.name == L"YT Music · Game Bar Alternative",
          "open shell retains widget authority and publishes page context on the root");
    Check(openTree.focusedNode == 0 && openTree.nodes[0].focused,
          "widget focus remains the singular composite focus owner");
    Check(openTree.nodes[1].id == L"host.open.back" &&
          openTree.nodes[0].domain == gba::accessibility::ElementDomain::Widget &&
          openTree.nodes[1].domain == gba::accessibility::ElementDomain::HostShell &&
          openTree.nodes[1].hostAction == gba::accessibility::HostAction::BackToTray &&
          !openTree.nodes[1].keyboardFocusable &&
          openTree.nodes[2].id == L"host.open.close" &&
          openTree.nodes[2].hostAction == gba::accessibility::HostAction::CloseOverlay &&
          !openTree.nodes[2].keyboardFocusable,
          "open shell exposes closed non-focus-stealing Back and Close commands");
    Check(gba::accessibility::HasUniqueElementKeys(openTree) &&
          gba::accessibility::AutomationId(openTree.nodes[0]) ==
              L"widget:host.open.back" &&
          gba::accessibility::AutomationId(openTree.nodes[1]) ==
              L"host:host.open.back",
          "widget and host elements may share raw IDs without identity collision");
    Check(openTree.nodes[3].id == L"host.open.help" &&
          openTree.nodes[3].liveSetting == gba::accessibility::LiveSetting::Off &&
          openTree.nodes[3].bounds.width == open.helpBounds.width,
          "visible widget shortcut guidance is readable non-live text with exact bounds");
    Check(openTree.nodes.size() == 6 &&
          openTree.nodes[4].id == L"tray.music" &&
          openTree.nodes[5].id == L"tray.performance" &&
          !openTree.nodes[4].focused && !openTree.nodes[5].focused,
          "open shell appends only the visible tray window without stealing widget focus");

    auto statusOpen = open;
    statusOpen.help.clear();
    statusOpen.status = L"Playback command failed";
    const auto trayFocusedTree = gba::accessibility::BuildOpenWidgetTree(
        widgetTree, items, *layout, 2, true, statusOpen);
    Check(trayFocusedTree.focusedNode == 5 &&
          !trayFocusedTree.nodes[0].focused && trayFocusedTree.nodes[5].focused,
          "tray focus clears widget focus and selects exactly one visible tray item");
    Check(trayFocusedTree.nodes[3].id == L"host.open.status" &&
          trayFocusedTree.nodes[3].role == gba::accessibility::Role::Status &&
          trayFocusedTree.nodes[3].liveSetting == gba::accessibility::LiveSetting::Polite,
          "transient open-widget feedback replaces static help with one polite status");

    gba::WidgetSnapshot nested;
    nested.sequence = 43;
    nested.activeInputScopeId = L"music.sheet";
    nested.root.id = L"music.root";
    nested.root.inputScopeId = L"music.root";
    gba::WidgetNode sheet;
    sheet.id = L"sheet";
    sheet.inputScopeId = L"music.sheet";
    sheet.shortcuts.push_back({L"b", L"sheet.back", L"pressed"});
    nested.root.children.push_back(sheet);
    Check(gba::accessibility::HasActiveScopeBackShortcut(nested) &&
          gba::accessibility::IsCurrentBackAction(
              gba::accessibility::HostAction::BackWithinWidget,
              L"music.sheet", nested) &&
          !gba::accessibility::IsCurrentBackAction(
              gba::accessibility::HostAction::BackToTray,
              L"music.sheet", nested) &&
          !gba::accessibility::IsCurrentBackAction(
              gba::accessibility::HostAction::BackWithinWidget,
              L"music.stale", nested),
          "nested Back resolves only to the active scope's explicit B shortcut");
    nested.root.children[0].shortcuts[0].phase = L"released";
    Check(!gba::accessibility::HasActiveScopeBackShortcut(nested) &&
          !gba::accessibility::IsCurrentBackAction(
              gba::accessibility::HostAction::BackWithinWidget,
              L"music.sheet", nested),
          "missing pressed-B authority suppresses nested Back without tray fallback");

    std::cout << "HostAccessibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
