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

    std::cout << "HostAccessibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
