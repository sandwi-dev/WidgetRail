#include "WidgetSurfaceFocus.h"

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

const WidgetNode* FirstEnabledButton(
    const WidgetNode& node,
    const std::wstring_view targetScope,
    const std::wstring_view inheritedScope) noexcept {
    const std::wstring_view currentScope = node.inputScopeId.empty()
        ? inheritedScope
        : std::wstring_view(node.inputScopeId);
    if (currentScope == targetScope && node.kind == L"button" &&
        !node.isDisabled && !node.isBusy) {
        return &node;
    }
    for (const auto& child : node.children) {
        if (const auto* match = FirstEnabledButton(child, targetScope, currentScope))
            return match;
    }
    return nullptr;
}

bool IsEnabledButton(const WidgetNode* node) noexcept {
    return node && node->kind == L"button" && !node->isDisabled && !node->isBusy;
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
    if (IsEnabledButton(node)) entries_[Key(widgetId, scope)] = focusedElementId;
}

std::wstring WidgetSurfaceFocusMemory::Restore(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const {
    const auto scope = std::wstring_view(snapshot.activeInputScopeId);
    const auto memory = entries_.find(Key(widgetId, scope));
    if (memory != entries_.end() &&
        IsEnabledButton(FindNodeInInputScope(snapshot, memory->second, scope))) {
        return memory->second;
    }
    if (!snapshot.initialFocusId.empty() &&
        IsEnabledButton(FindNodeInInputScope(snapshot, snapshot.initialFocusId, scope))) {
        return snapshot.initialFocusId;
    }
    if (const auto* first = FirstEnabledButton(
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
