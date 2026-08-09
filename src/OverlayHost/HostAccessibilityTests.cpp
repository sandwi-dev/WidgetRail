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
          dashboardTree.nodes[3].bounds.width == dashboard.statusBounds.width &&
          dashboardTree.nodes[3].bounds.height == dashboard.statusBounds.height,
          "dashboard semantic text uses the exact paint rectangles");
    Check(dashboardTree.nodes[3].id == L"host.dashboard.status" &&
          dashboardTree.nodes[3].name == dashboard.status &&
          dashboardTree.nodes[3].role == gba::accessibility::Role::Status &&
          dashboardTree.nodes[3].liveSetting == gba::accessibility::LiveSetting::Polite,
          "dashboard hint and feedback are exposed as one polite status region");
    Check(dashboardTree.focusedNode == 2 && dashboardTree.nodes[2].hostTargetId == L"performance",
          "non-focusable dashboard text does not disturb tray focus identity");
    const auto revision = gba::accessibility::ComputeTraySemanticRevision(items, &dashboard);
    auto changedDashboard = dashboard;
    changedDashboard.status = L"Playback command failed";
    Check(revision != gba::accessibility::ComputeTraySemanticRevision(
              items, &changedDashboard),
          "dashboard status changes invalidate the host projection revision");
    auto renamedItems = items;
    renamedItems[1].name = L"YouTube Music";
    Check(revision != gba::accessibility::ComputeTraySemanticRevision(
              renamedItems, &dashboard),
          "catalog display-name changes invalidate the host projection revision");

    std::cout << "HostAccessibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
