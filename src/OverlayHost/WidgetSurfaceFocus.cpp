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

std::wstring_view RootInputScope(const WidgetSnapshot& snapshot) noexcept {
    return snapshot.root.inputScopeId.empty()
        ? std::wstring_view(snapshot.root.id)
        : std::wstring_view(snapshot.root.inputScopeId);
}

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
        entries_[Key(widgetId, scope)] = {
            std::wstring(focusedElementId),
            static_cast<std::size_t>(position - focusNodes.begin()),
            snapshot.initialFocusId,
        };
    }
}

std::wstring WidgetSurfaceFocusMemory::Restore(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const {
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    const auto memory = entries_.find(Key(widgetId, scope));
    if (memory != entries_.end() &&
        memory->second.initialFocusId != snapshot.initialFocusId &&
        !snapshot.initialFocusId.empty() &&
        IsEnabledFocusNode(FindNodeInInputScope(snapshot, snapshot.initialFocusId, scope))) {
        return snapshot.initialFocusId;
    }
    if (memory != entries_.end() &&
        IsEnabledFocusNode(FindNodeInInputScope(snapshot, memory->second.elementId, scope))) {
        return memory->second.elementId;
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
    for (const auto* ancestor : path) {
        if (!ancestor->initialChildFocusId.empty() &&
            ancestor->id != focusedElementId) {
            entries_[Key(widgetId, scope, ancestor->id)] = focusedElementId;
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
    if (memory != entries_.end() && eligible(memory->second))
        return memory->second;
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
