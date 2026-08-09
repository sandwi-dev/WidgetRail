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
          tree.nodes[0].role == gba::accessibility::Role::ListItem,
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
    play.id = L"music.play";
    play.name = L"Play";
    play.actionId = L"music.play";
    play.bounds = {20, 80, 120, 44};
    play.role = gba::accessibility::Role::Button;
    play.focused = true;
    widgetTree.nodes.push_back(play);
    widgetTree.focusedNode = 0;
    const gba::accessibility::OpenWidgetSemantics open{
        L"YT Music", true,
        {320, 440, 80, 30}, {400, 440, 100, 30},
        L"X Play  LB Previous  RB Next", {20, 440, 286, 30},
        L"", {20, 440, 286, 30},
    };
    const auto openTree = gba::accessibility::BuildOpenWidgetTree(
        widgetTree, items, *layout, 1, false, open);
    Check(openTree.widgetId == L"music" &&
          openTree.runtimeGeneration == L"music-v1" &&
          openTree.snapshotSequence == 42 &&
          openTree.name == L"YT Music · Game Bar Alternative",
          "open shell retains widget authority and publishes page context on the root");
    Check(openTree.focusedNode == 0 && openTree.nodes[0].focused,
          "widget focus remains the singular composite focus owner");
    Check(openTree.nodes[1].id == L"host.open.back" &&
          openTree.nodes[1].hostAction == gba::accessibility::HostAction::BackToTray &&
          !openTree.nodes[1].keyboardFocusable &&
          openTree.nodes[2].id == L"host.open.close" &&
          openTree.nodes[2].hostAction == gba::accessibility::HostAction::CloseOverlay &&
          !openTree.nodes[2].keyboardFocusable,
          "open shell exposes closed non-focus-stealing Back and Close commands");
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

    std::cout << "HostAccessibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
