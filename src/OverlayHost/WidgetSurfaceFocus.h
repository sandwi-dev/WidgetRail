#pragma once

#include "WidgetBridgeClient.h"

#include <string>
#include <string_view>
#include <optional>
#include <unordered_map>

namespace widgetrail { struct RenderResult; }

namespace widgetrail::input {

/// Returns the semantic root input scope. Protocol-owned presentation surfaces
/// are transparent to input: their required sole content child owns focus,
/// shortcuts, and accessibility. Other containers remain scope boundaries so
/// an arbitrary nested scope cannot acquire host-owned root exits.
[[nodiscard]] inline std::wstring_view RootInputScope(
    const WidgetSnapshot& snapshot) noexcept {
    const WidgetNode* root = &snapshot.root;
    while (root->inputScopeId.empty() && root->children.size() == 1U &&
           (root->kind == L"backgroundSurface" ||
            root->kind == L"focusPresentationSurface")) {
        root = &root->children.front();
    }
    return root->inputScopeId.empty()
        ? std::wstring_view(root->id)
        : std::wstring_view(root->inputScopeId);
}

[[nodiscard]] const WidgetNode* FindNodeInInputScope(
    const WidgetSnapshot& snapshot,
    std::wstring_view nodeId,
    std::wstring_view scopeId) noexcept;

/// Remembers controller focus independently for every widget input surface.
/// This is deliberately host-owned: a modal may disappear and later return
/// without forcing widget authors to persist native focus bookkeeping.
class WidgetSurfaceFocusMemory final {
public:
    void Remember(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId);

    /// A changed valid initial focus is an explicit snapshot request and wins
    /// over prior memory. Otherwise returns exact remembered focus, its nearest
    /// ordinal fallback, valid initial focus, then the first navigable control.
    /// Disabled/busy controls retain focus but suppress actions. An empty string
    /// is valid for focusless surfaces whose container-level shortcuts accept input.
    [[nodiscard]] std::wstring Restore(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot) const;

    /// Drops all remembered surfaces for a removed or runtime-replaced widget.
    void Forget(std::wstring_view widgetId);

private:
    struct Entry final {
        std::wstring elementId;
        std::size_t ordinal{};
        std::wstring initialFocusId;
        std::wstring collectionScrollId;
    };

    [[nodiscard]] static std::wstring Key(
        std::wstring_view widgetId,
        std::wstring_view scopeId);

    std::unordered_map<std::wstring, Entry> entries_;
    std::unordered_map<std::wstring, std::uint64_t> collectionNavigation_;
};

/// Remembers the last valid focused descendant of each explicitly authored
/// focus-entry container without making the container a focus or UIA node.
class WidgetFocusGroupMemory final {
public:
    void Remember(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId);
    [[nodiscard]] std::optional<std::wstring> Resolve(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view groupId,
        const RenderResult& renderResult) const;
    void Forget(std::wstring_view widgetId);

private:
    [[nodiscard]] static std::wstring Key(
        std::wstring_view widgetId,
        std::wstring_view scopeId,
        std::wstring_view groupId);
    std::unordered_map<std::wstring, std::wstring> entries_;
};

} // namespace widgetrail::input
