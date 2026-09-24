#pragma once
#include "WidgetBridgeClient.h"
#include <algorithm>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace widgetrail::input {
inline bool HasAvailableContextMenuActions(const std::vector<WidgetContextAction>& actions) {
    return std::ranges::any_of(actions, [](const WidgetContextAction& action) {
        return !action.isDisabled && !action.isBusy;
    });
}

// Retain the source identity, not a whole snapshot. Progress, artwork and
// unrelated layout updates do not change the authority of an open menu.
struct WidgetContextMenuSource final {
    std::wstring nodeId, scopeId, kind, actionId, itemKey, menuButton;
    std::vector<std::pair<std::wstring, std::optional<std::uint64_t>>> collections;
    std::vector<WidgetContextAction> actions;
};

inline std::optional<WidgetContextMenuSource> CaptureContextMenuSource(
    const WidgetSnapshot& snapshot, const std::wstring_view nodeId) {
    std::optional<WidgetContextMenuSource> found;
    std::vector<std::pair<std::wstring, std::optional<std::uint64_t>>> collections;
    const auto visit = [&](const auto& self, const WidgetNode& node,
                           std::wstring scope, const bool blocked) -> void {
        if (!node.inputScopeId.empty()) scope = node.inputScopeId;
        const bool unavailable = blocked || node.isDisabled || node.isBusy;
        const bool collection = node.kind == L"scroll" || node.collectionResetGeneration.has_value();
        if (collection) collections.emplace_back(node.id, node.collectionResetGeneration);
        if (node.id == nodeId && scope == snapshot.activeInputScopeId && !unavailable &&
            (node.kind == L"actionSurface" || !node.contextMenuButton.empty()) &&
            !node.contextActions.empty())
            found = WidgetContextMenuSource{node.id, scope, node.kind, node.actionId,
                node.collectionItemKey, node.contextMenuButton, collections, node.contextActions};
        for (const auto& child : node.children) self(self, child, scope, unavailable);
        if (collection) collections.pop_back();
    };
    visit(visit, snapshot.root, snapshot.root.id, false);
    return found;
}

inline bool ContextMenuSourceCurrent(const WidgetSnapshot& snapshot,
                                     const WidgetContextMenuSource& origin) {
    const auto current = CaptureContextMenuSource(snapshot, origin.nodeId);
    if (!current || current->scopeId != origin.scopeId || current->kind != origin.kind ||
        current->actionId != origin.actionId || current->itemKey != origin.itemKey ||
        current->menuButton != origin.menuButton || current->collections != origin.collections ||
        current->actions.size() != origin.actions.size()) return false;
    for (std::size_t index = 0; index < origin.actions.size(); ++index) {
        const auto& before = origin.actions[index];
        const auto& after = current->actions[index];
        if (before.actionId != after.actionId || before.label != after.label ||
            before.style != after.style || before.isDisabled != after.isDisabled ||
            before.isBusy != after.isBusy) return false;
    }
    return true;
}
} // namespace widgetrail::input
