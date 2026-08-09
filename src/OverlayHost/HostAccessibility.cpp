#include "HostAccessibility.h"

#include <algorithm>
#include <cstdint>

namespace gba::accessibility {

long long ComputeTraySemanticRevision(
    const std::vector<TrayItem>& items,
    const DashboardSemantics* dashboard) noexcept {
    std::uint64_t hash = 1469598103934665603ULL;
    const auto hashText = [&](const std::wstring_view value) {
        for (const wchar_t codeUnit : value) {
            hash ^= static_cast<std::uint16_t>(codeUnit);
            hash *= 1099511628211ULL;
        }
        hash ^= 0xffffU;
        hash *= 1099511628211ULL;
    };
    for (const auto& item : items) {
        hashText(item.widgetId);
        hashText(item.name);
        hash ^= item.enabled ? 1U : 0U;
        hash *= 1099511628211ULL;
    }
    if (dashboard) {
        hashText(dashboard->title);
        hashText(dashboard->status);
    }
    return static_cast<long long>(hash & 0x7fffffffffffffffULL);
}

Tree BuildTrayTree(
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    const std::size_t selectedSlot,
    const long long sequence,
    const DashboardSemantics* dashboard) {
    Tree tree;
    tree.widgetId = L"host.tray";
    tree.runtimeGeneration = L"host";
    tree.snapshotSequence = sequence;
    tree.activeInputScopeId = L"host.tray";
    tree.nodes.reserve(layout.tiles.size() + (dashboard ? 2U : 0U));
    if (dashboard && !dashboard->title.empty()) {
        Node title;
        title.id = L"host.dashboard.title";
        title.name = dashboard->title;
        title.bounds = dashboard->titleBounds;
        title.role = Role::Heading;
        title.headingLevel = HeadingLevel::Level1;
        tree.nodes.push_back(std::move(title));
    }
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
    if (dashboard && !dashboard->status.empty()) {
        Node status;
        status.id = L"host.dashboard.status";
        status.name = dashboard->status;
        status.bounds = dashboard->statusBounds;
        status.role = Role::Status;
        status.liveSetting = LiveSetting::Polite;
        tree.nodes.push_back(std::move(status));
    }
    return tree;
}

} // namespace gba::accessibility
