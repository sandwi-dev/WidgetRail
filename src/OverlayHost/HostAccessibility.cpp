#include "HostAccessibility.h"

#include <algorithm>
#include <cstdint>

namespace widgetrail::accessibility {
namespace {

const WidgetNode* FindDeclaredScopeRoot(
    const WidgetNode& node,
    const std::wstring_view scopeId) noexcept {
    if (node.inputScopeId == scopeId) return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindDeclaredScopeRoot(child, scopeId)) return found;
    }
    return nullptr;
}

bool HasPressedBackShortcut(const WidgetNode& node) noexcept {
    return std::any_of(
        node.shortcuts.begin(), node.shortcuts.end(),
        [](const WidgetShortcut& shortcut) {
            return shortcut.button == L"b" && shortcut.phase == L"pressed" &&
                !shortcut.actionId.empty();
        });
}

enum class FocusedBackResolution {
    NotFound,
    NoShortcut,
    Handled,
    Suppressed,
};

FocusedBackResolution ResolveFocusedBackInScope(
    const WidgetNode& node,
    const std::wstring_view nodeId,
    const bool isScopeRoot) noexcept {
    if (!isScopeRoot && !node.inputScopeId.empty())
        return FocusedBackResolution::NotFound;
    if (node.id == nodeId) {
        if (!HasPressedBackShortcut(node))
            return FocusedBackResolution::NoShortcut;
        return node.isDisabled || node.isBusy
            ? FocusedBackResolution::Suppressed
            : FocusedBackResolution::Handled;
    }
    for (const auto& child : node.children) {
        const auto nested = ResolveFocusedBackInScope(child, nodeId, false);
        if (nested == FocusedBackResolution::NotFound) continue;
        if (nested != FocusedBackResolution::NoShortcut) return nested;
        return HasPressedBackShortcut(node)
            ? FocusedBackResolution::Handled
            : FocusedBackResolution::NoShortcut;
    }
    return FocusedBackResolution::NotFound;
}

void AppendTrayOverflow(
    Tree& tree,
    const std::vector<TrayItem>& items,
    const std::optional<shell::TrayOverflowLayout>& overflow) {
    if (!overflow || overflow->targetSlot >= items.size()) return;
    const auto& target = items[overflow->targetSlot];
    if (target.widgetId.empty()) return;
    Node node;
    node.id = overflow->direction == shell::TrayOverflowDirection::Previous
        ? L"overflow.previous"
        : L"overflow.next";
    node.domain = ElementDomain::Tray;
    node.name = std::to_wstring(overflow->hiddenCount) +
        (overflow->direction == shell::TrayOverflowDirection::Previous
            ? L" previous widgets"
            : L" more widgets");
    node.hostTargetId = target.widgetId;
    node.bounds = overflow->bounds;
    node.role = Role::Button;
    node.hostAction = HostAction::SelectTrayOverflow;
    node.keyboardFocusable = false;
    tree.nodes.push_back(std::move(node));
}

} // namespace

bool HasActiveScopeBackShortcut(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) noexcept {
    if (snapshot.activeInputScopeId.empty()) return false;
    const auto* scopeRoot = FindDeclaredScopeRoot(
        snapshot.root, snapshot.activeInputScopeId);
    if (!scopeRoot) return false;
    if (focusedElementId.empty()) {
        return !scopeRoot->isDisabled && !scopeRoot->isBusy &&
            HasPressedBackShortcut(*scopeRoot);
    }

    return ResolveFocusedBackInScope(*scopeRoot, focusedElementId, true) ==
        FocusedBackResolution::Handled;
}

bool IsCurrentBackAction(
    const HostAction action,
    const std::wstring_view targetScopeId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) noexcept {
    if (targetScopeId.empty() || targetScopeId != snapshot.activeInputScopeId)
        return false;
    const std::wstring_view rootScope = snapshot.root.inputScopeId.empty()
        ? std::wstring_view(snapshot.root.id)
        : std::wstring_view(snapshot.root.inputScopeId);
    if (action == HostAction::BackToTray)
        return targetScopeId == rootScope;
    if (action == HostAction::BackWithinWidget)
        return targetScopeId != rootScope &&
            HasActiveScopeBackShortcut(snapshot, focusedElementId);
    return false;
}

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
        hashText(dashboard->help);
        hashText(dashboard->status);
    }
    return static_cast<long long>(hash & 0x7fffffffffffffffULL);
}

long long ComputeOpenWidgetSemanticRevision(
    const std::vector<TrayItem>& items,
    const OpenWidgetSemantics& semantics) noexcept {
    DashboardSemantics semanticText{
        semantics.title, {}, semantics.help, {}, semantics.status, {},
    };
    auto revision = static_cast<std::uint64_t>(
        ComputeTraySemanticRevision(items, &semanticText));
    revision ^= static_cast<std::uint64_t>(semantics.backAction) +
        0x9e3779b97f4a7c15ULL;
    revision *= 1099511628211ULL;
    for (const wchar_t codeUnit : semantics.backTargetId) {
        revision ^= static_cast<std::uint16_t>(codeUnit);
        revision *= 1099511628211ULL;
    }
    return static_cast<long long>(revision & 0x7fffffffffffffffULL);
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
    tree.name = L"WidgetRail";
    tree.nodes.reserve(
        layout.tiles.size() + (layout.previousOverflow ? 1U : 0U) +
        (layout.nextOverflow ? 1U : 0U) + (dashboard ? 3U : 0U));
    if (dashboard && !dashboard->title.empty()) {
        Node title;
        title.id = L"host.dashboard.title";
        title.domain = ElementDomain::HostShell;
        title.name = dashboard->title;
        title.bounds = dashboard->titleBounds;
        title.role = Role::Heading;
        title.headingLevel = HeadingLevel::Level1;
        tree.nodes.push_back(std::move(title));
    }
    AppendTrayOverflow(tree, items, layout.previousOverflow);
    for (const auto& tile : layout.tiles) {
        if (tile.slot >= items.size()) continue;
        const auto& item = items[tile.slot];
        if (item.widgetId.empty() || item.name.empty()) continue;
        Node node;
        node.id = L"tray." + item.widgetId;
        node.domain = ElementDomain::Tray;
        node.name = item.name;
        node.hostTargetId = item.widgetId;
        node.bounds = tile.bounds;
        node.role = Role::ListItem;
        node.hostAction = HostAction::ActivateTrayItem;
        node.enabled = item.enabled;
        node.selected = tile.slot == selectedSlot;
        node.focused = node.selected;
        node.positionInSet = static_cast<int>(tile.slot + 1);
        node.sizeOfSet = static_cast<int>(items.size());
        const auto index = tree.nodes.size();
        tree.nodes.push_back(std::move(node));
        if (tree.nodes[index].focused) tree.focusedNode = index;
    }
    AppendTrayOverflow(tree, items, layout.nextOverflow);
    if (dashboard && !dashboard->help.empty()) {
        Node help;
        help.id = L"host.dashboard.help";
        help.domain = ElementDomain::HostShell;
        help.name = dashboard->help;
        help.bounds = dashboard->helpBounds;
        help.role = Role::Text;
        tree.nodes.push_back(std::move(help));
    }
    if (dashboard && !dashboard->status.empty()) {
        Node status;
        status.id = L"host.dashboard.status";
        status.domain = ElementDomain::HostShell;
        status.name = dashboard->status;
        status.bounds = dashboard->statusBounds;
        status.role = Role::Status;
        status.liveSetting = LiveSetting::Polite;
        tree.nodes.push_back(std::move(status));
    }
    return tree;
}

Tree BuildOpenWidgetTree(
    Tree widgetTree,
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    const std::size_t selectedSlot,
    const bool trayFocused,
    const OpenWidgetSemantics& semantics) {
    widgetTree.name = semantics.title.empty()
        ? L"WidgetRail"
        : semantics.title + L" · WidgetRail";
    if (trayFocused) {
        for (auto& node : widgetTree.nodes) node.focused = false;
        widgetTree.focusedNode.reset();
    }

    if (semantics.backAction != HostAction::None) {
        Node back;
        back.id = L"host.open.back";
        back.domain = ElementDomain::HostShell;
        back.name = semantics.backAction == HostAction::BackToTray
            ? L"Back to widget tray"
            : L"Back";
        back.bounds = semantics.backBounds;
        back.role = Role::Button;
        back.hostAction = semantics.backAction;
        back.hostTargetId = semantics.backTargetId;
        back.keyboardFocusable = false;
        widgetTree.nodes.push_back(std::move(back));
    }

    Node close;
    close.id = L"host.open.close";
    close.domain = ElementDomain::HostShell;
    close.name = L"Close overlay";
    close.bounds = semantics.closeBounds;
    close.role = Role::Button;
    close.hostAction = HostAction::CloseOverlay;
    close.keyboardFocusable = false;
    widgetTree.nodes.push_back(std::move(close));

    if (!semantics.help.empty()) {
        Node help;
        help.id = L"host.open.help";
        help.domain = ElementDomain::HostShell;
        help.name = semantics.help;
        help.bounds = semantics.helpBounds;
        help.role = Role::Text;
        help.keyboardFocusable = false;
        widgetTree.nodes.push_back(std::move(help));
    }
    if (!semantics.status.empty()) {
        Node status;
        status.id = L"host.open.status";
        status.domain = ElementDomain::HostShell;
        status.name = semantics.status;
        status.bounds = semantics.statusBounds;
        status.role = Role::Status;
        status.liveSetting = LiveSetting::Polite;
        status.keyboardFocusable = false;
        widgetTree.nodes.push_back(std::move(status));
    }

    AppendTrayOverflow(widgetTree, items, layout.previousOverflow);
    for (const auto& tile : layout.tiles) {
        if (tile.slot >= items.size()) continue;
        const auto& item = items[tile.slot];
        if (item.widgetId.empty() || item.name.empty()) continue;
        Node node;
        node.id = L"tray." + item.widgetId;
        node.domain = ElementDomain::Tray;
        node.name = item.name;
        node.hostTargetId = item.widgetId;
        node.bounds = tile.bounds;
        node.role = Role::ListItem;
        node.hostAction = HostAction::ActivateTrayItem;
        node.enabled = item.enabled;
        node.selected = tile.slot == selectedSlot;
        node.focused = trayFocused && node.selected;
        node.positionInSet = static_cast<int>(tile.slot + 1);
        node.sizeOfSet = static_cast<int>(items.size());
        const auto index = widgetTree.nodes.size();
        widgetTree.nodes.push_back(std::move(node));
        if (widgetTree.nodes[index].focused) widgetTree.focusedNode = index;
    }
    AppendTrayOverflow(widgetTree, items, layout.nextOverflow);
    return widgetTree;
}

} // namespace widgetrail::accessibility
