#include "WidgetSurfaceFocus.h"
#include "FocusNavigation.h"

#include <algorithm>
#include <vector>

namespace widgetrail::input {
namespace {

const WidgetNode* FindNode(
    const WidgetNode& node,
    const std::wstring_view nodeId,
    const std::wstring_view targetScope,
    const std::wstring_view inheritedScope) noexcept {
    const std::wstring_view currentScope = node.inputScopeId.empty()
        ? inheritedScope
        : std::wstring_view(node.inputScopeId);
    if (node.id == nodeId && currentScope == targetScope) return &node;
    for (const auto& child : node.children) {
        if (const auto* match = FindNode(child, nodeId, targetScope, currentScope))
            return match;
    }
    return nullptr;
}

bool IsEnabledFocusNode(const WidgetNode* node) noexcept {
    return node && (node->kind == L"slider" || node->kind == L"button" ||
                    node->kind == L"actionSurface");
}

const WidgetNode* FirstEnabledFocusNode(
    const WidgetNode& node,
    const std::wstring_view targetScope,
    const std::wstring_view inheritedScope) noexcept {
    const std::wstring_view currentScope = node.inputScopeId.empty()
        ? inheritedScope
        : std::wstring_view(node.inputScopeId);
    if (currentScope == targetScope && IsEnabledFocusNode(&node)) {
        return &node;
    }
    for (const auto& child : node.children) {
        if (const auto* match = FirstEnabledFocusNode(child, targetScope, currentScope))
            return match;
    }
    return nullptr;
}

void CollectEnabledFocusNodes(
    const WidgetNode& node,
    const std::wstring_view targetScope,
    const std::wstring_view inheritedScope,
    std::vector<const WidgetNode*>& result) {
    const std::wstring_view currentScope = node.inputScopeId.empty()
        ? inheritedScope
        : std::wstring_view(node.inputScopeId);
    if (currentScope == targetScope && IsEnabledFocusNode(&node)) {
        result.push_back(&node);
    }
    for (const auto& child : node.children)
        CollectEnabledFocusNodes(child, targetScope, currentScope, result);
}

bool FindNodePath(
    const WidgetNode& node,
    const std::wstring_view nodeId,
    const std::wstring_view targetScope,
    const std::wstring_view inheritedScope,
    std::vector<const WidgetNode*>& path) {
    const std::wstring_view currentScope = node.inputScopeId.empty()
        ? inheritedScope : std::wstring_view(node.inputScopeId);
    path.push_back(&node);
    if (node.id == nodeId && currentScope == targetScope) return true;
    for (const auto& child : node.children) {
        if (FindNodePath(child, nodeId, targetScope, currentScope, path))
            return true;
    }
    path.pop_back();
    return false;
}

bool ContainsDescendant(
    const WidgetNode& group,
    const std::wstring_view nodeId) noexcept {
    for (const auto& child : group.children) {
        if (child.id == nodeId || ContainsDescendant(child, nodeId)) return true;
    }
    return false;
}

const WidgetNode* FirstVisibleFocusDescendant(
    const WidgetNode& group,
    const std::wstring_view scope,
    const std::wstring_view inheritedScope,
    const RenderResult& renderResult) noexcept {
    for (const auto& child : group.children) {
        const std::wstring_view childScope = child.inputScopeId.empty()
            ? inheritedScope : std::wstring_view(child.inputScopeId);
        if (childScope == scope && IsEnabledFocusNode(&child) &&
            IsEnabledFocusTarget(child.id, renderResult)) return &child;
        if (const auto* candidate = FirstVisibleFocusDescendant(
                child, scope, childScope, renderResult)) return candidate;
    }
    return nullptr;
}

} // namespace

const WidgetNode* FindNodeInInputScope(
    const WidgetSnapshot& snapshot,
    const std::wstring_view nodeId,
    const std::wstring_view scopeId) noexcept {
    return FindNode(snapshot.root, nodeId, scopeId, RootInputScope(snapshot));
}

void WidgetSurfaceFocusMemory::Remember(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) {
    if (widgetId.empty() || focusedElementId.empty()) return;
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    const auto* node = FindNodeInInputScope(snapshot, focusedElementId, scope);
    if (!IsEnabledFocusNode(node)) return;
    std::vector<const WidgetNode*> focusNodes;
    CollectEnabledFocusNodes(snapshot.root, scope, RootInputScope(snapshot), focusNodes);
    const auto position = std::find(focusNodes.begin(), focusNodes.end(), node);
    if (position != focusNodes.end()) {
        std::vector<const WidgetNode*> path;
        (void)FindNodePath(snapshot.root, focusedElementId, scope, RootInputScope(snapshot), path);
        std::wstring collectionScroll;
        std::uint64_t resetGeneration{};
        for (auto it = path.rbegin(); it != path.rend(); ++it) {
            if ((*it)->kind == L"scroll" && (*it)->collectionStartIndex) {
                collectionScroll = (*it)->id;
                resetGeneration = (*it)->collectionResetGeneration.value_or(0);
                break;
            }
        }
        const auto observe = [&](const auto& self, const WidgetNode& current) -> void {
            if (current.collectionNavigation) {
                const auto& request = *current.collectionNavigation;
                if ((!request.targetFocusId.empty() && request.targetFocusId == focusedElementId) ||
                    (!request.originFocusId.empty() && request.originFocusId != focusedElementId)) {
                    auto& consumed = collectionNavigation_[Key(widgetId, scope) + L"\x1f" + current.id];
                    consumed = std::max(consumed, request.requestId);
                }
            }
            for (const auto& child : current.children) self(self, child);
        };
        observe(observe, snapshot.root);
        entries_[Key(widgetId, scope)] = {
            std::wstring(focusedElementId),
            static_cast<std::size_t>(position - focusNodes.begin()),
            snapshot.initialFocusId,
            std::move(collectionScroll),
            resetGeneration,
        };
    }
}

std::wstring WidgetSurfaceFocusMemory::Restore(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const {
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    const auto memory = entries_.find(Key(widgetId, scope));
    // A fresh collection invalidates only remembered focus inside that collection.
    if (memory != entries_.end() && !memory->second.collectionScrollId.empty()) {
        const auto* collection = FindNodeInInputScope(snapshot, memory->second.collectionScrollId, scope);
        if (collection && collection->collectionResetGeneration &&
            *collection->collectionResetGeneration != memory->second.collectionResetGeneration) {
            if (const auto* first = FirstEnabledFocusNode(*collection, scope, scope)) return first->id;
        }
    }
    std::vector<const WidgetNode*> initialPath;
    (void)FindNodePath(snapshot.root, snapshot.initialFocusId, scope, RootInputScope(snapshot), initialPath);
    const WidgetNode* initialCollection{};
    for (auto it = initialPath.rbegin(); it != initialPath.rend(); ++it) {
        if ((*it)->kind == L"scroll" && (*it)->collectionStartIndex) { initialCollection = *it; break; }
    }
    // Cursor defaults are entry defaults, not navigation commands. Only a fresh
    // explicitly authored request can replace existing focus after a load.
    if (initialCollection && initialCollection->collectionNavigation) {
        const auto& request = *initialCollection->collectionNavigation;
        const auto consumed = collectionNavigation_.find(Key(widgetId, scope) + L"\x1f" + initialCollection->id);
        if (!request.targetFocusId.empty() && request.targetFocusId == snapshot.initialFocusId &&
            (consumed == collectionNavigation_.end() || consumed->second < request.requestId) &&
            (request.originFocusId.empty() || (memory != entries_.end() && memory->second.elementId == request.originFocusId)) &&
            IsEnabledFocusNode(FindNodeInInputScope(snapshot, request.targetFocusId, scope)))
            return request.targetFocusId;
    }
    if (!initialCollection && memory != entries_.end() &&
        memory->second.initialFocusId != snapshot.initialFocusId &&
        !snapshot.initialFocusId.empty() &&
        IsEnabledFocusNode(FindNodeInInputScope(snapshot, snapshot.initialFocusId, scope))) {
        return snapshot.initialFocusId;
    }
    if (memory != entries_.end() &&
        IsEnabledFocusNode(FindNodeInInputScope(snapshot, memory->second.elementId, scope))) {
        return memory->second.elementId;
    }
    if (memory != entries_.end() && !memory->second.collectionScrollId.empty()) {
        const auto* collection = FindNodeInInputScope(snapshot, memory->second.collectionScrollId, scope);
        if (collection && collection->kind == L"scroll") {
            // Eviction must not reinterpret a collection-local position as a
            // whole-widget ordinal and move into another section.
            if (const auto* first = FirstEnabledFocusNode(*collection, scope, scope)) return first->id;
        }
    }
    if (memory != entries_.end()) {
        std::vector<const WidgetNode*> focusNodes;
        CollectEnabledFocusNodes(snapshot.root, scope, RootInputScope(snapshot), focusNodes);
        if (!focusNodes.empty()) {
            const auto nearest = std::min(memory->second.ordinal, focusNodes.size() - 1);
            return focusNodes[nearest]->id;
        }
    }
    if (!snapshot.initialFocusId.empty() &&
        IsEnabledFocusNode(FindNodeInInputScope(snapshot, snapshot.initialFocusId, scope))) {
        return snapshot.initialFocusId;
    }
    if (const auto* first = FirstEnabledFocusNode(
            snapshot.root, scope, RootInputScope(snapshot))) {
        return first->id;
    }
    return {};
}

void WidgetSurfaceFocusMemory::Forget(const std::wstring_view widgetId) {
    std::erase_if(collectionNavigation_, [&](const auto& entry) {
        return entry.first.starts_with(std::wstring{widgetId} + L"\x1f");
    });
    if (widgetId.empty()) return;
    std::wstring prefix(widgetId);
    prefix.push_back(L'\x1f');
    std::erase_if(entries_, [&](const auto& entry) {
        return entry.first.starts_with(prefix);
    });
}

std::wstring WidgetSurfaceFocusMemory::Key(
    const std::wstring_view widgetId,
    const std::wstring_view scopeId) {
    std::wstring key(widgetId);
    key.push_back(L'\x1f');
    key.append(scopeId);
    return key;
}

void WidgetFocusGroupMemory::Remember(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) {
    if (widgetId.empty() || focusedElementId.empty()) return;
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    std::vector<const WidgetNode*> path;
    if (!FindNodePath(snapshot.root, focusedElementId, scope,
            RootInputScope(snapshot), path)) return;
    Entry entry{std::wstring(focusedElementId), {}, 0};
    for (auto it = path.rbegin(); it != path.rend(); ++it) {
        if ((*it)->kind == L"scroll" && (*it)->collectionStartIndex) {
            entry.collectionScrollId = (*it)->id;
            entry.collectionResetGeneration = (*it)->collectionResetGeneration.value_or(0);
            break;
        }
    }
    for (const auto* ancestor : path) {
        if (!ancestor->initialChildFocusId.empty() &&
            ancestor->id != focusedElementId) {
            entries_[Key(widgetId, scope, ancestor->id)] = entry;
        }
    }
}

std::optional<std::wstring> WidgetFocusGroupMemory::Resolve(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view groupId,
    const RenderResult& renderResult) const {
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    const auto* group = FindNodeInInputScope(snapshot, groupId, scope);
    if (!group || group->initialChildFocusId.empty()) return std::nullopt;
    const auto eligible = [&](const std::wstring_view id) {
        return !id.empty() && ContainsDescendant(*group, id) &&
            IsEnabledFocusTarget(id, renderResult);
    };
    const auto memory = entries_.find(Key(widgetId, scope, groupId));
    bool reset = false;
    if (memory != entries_.end() && !memory->second.collectionScrollId.empty()) {
        const auto* collection = FindNodeInInputScope(snapshot, memory->second.collectionScrollId, scope);
        reset = collection && collection->collectionResetGeneration &&
            *collection->collectionResetGeneration != memory->second.collectionResetGeneration;
        if (reset && ContainsDescendant(*group, collection->id)) {
            if (const auto* first = FirstVisibleFocusDescendant(*collection, scope, scope, renderResult))
                return first->id;
        }
    }
    if (reset) {
        if (const auto* first = FirstVisibleFocusDescendant(*group, scope, scope, renderResult)) return first->id;
    }
    if (!reset && memory != entries_.end() && eligible(memory->second.elementId))
        return memory->second.elementId;
    if (eligible(group->initialChildFocusId)) return group->initialChildFocusId;
    if (const auto* first = FirstVisibleFocusDescendant(
            *group, scope, scope, renderResult)) return first->id;
    return std::nullopt;
}

void WidgetFocusGroupMemory::Forget(const std::wstring_view widgetId) {
    if (widgetId.empty()) return;
    std::wstring prefix(widgetId);
    prefix.push_back(L'\x1f');
    std::erase_if(entries_, [&](const auto& entry) {
        return entry.first.starts_with(prefix);
    });
}

std::wstring WidgetFocusGroupMemory::Key(
    const std::wstring_view widgetId,
    const std::wstring_view scopeId,
    const std::wstring_view groupId) {
    std::wstring key(widgetId);
    key.push_back(L'\x1f');
    key.append(scopeId);
    key.push_back(L'\x1f');
    key.append(groupId);
    return key;
}

} // namespace widgetrail::input
