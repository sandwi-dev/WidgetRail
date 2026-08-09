#include "WidgetSurfaceFocus.h"

#include <algorithm>
#include <vector>

namespace gba::input {
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

} // namespace gba::input
