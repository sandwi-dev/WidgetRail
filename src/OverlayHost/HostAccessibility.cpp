#include "HostAccessibility.h"

#include <algorithm>

namespace gba::accessibility {

Tree BuildTrayTree(
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    const std::size_t selectedSlot,
    const long long sequence) {
    Tree tree;
    tree.widgetId = L"host.tray";
    tree.runtimeGeneration = L"host";
    tree.snapshotSequence = sequence;
    tree.activeInputScopeId = L"host.tray";
    tree.nodes.reserve(layout.tiles.size());
    for (const auto& tile : layout.tiles) {
        if (tile.slot >= items.size()) continue;
        const auto& item = items[tile.slot];
        if (item.widgetId.empty() || item.name.empty()) continue;
        Node node;
        node.id = L"tray." + item.widgetId;
        node.name = item.name;
        node.hostTargetId = item.widgetId;
        node.bounds = tile.bounds;
        node.role = Role::ListItem;
        node.hostAction = HostAction::ActivateTrayItem;
        node.enabled = item.enabled;
        node.selected = tile.slot == selectedSlot;
        node.focused = node.selected;
        const auto index = tree.nodes.size();
        tree.nodes.push_back(std::move(node));
        if (tree.nodes[index].focused) tree.focusedNode = index;
    }
    return tree;
}

} // namespace gba::accessibility
