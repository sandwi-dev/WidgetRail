#pragma once

#include "WidgetBridgeClient.h"

#include <string>
#include <string_view>
#include <unordered_map>

namespace gba::input {

[[nodiscard]] std::wstring_view RootInputScope(const WidgetSnapshot& snapshot) noexcept;

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
    };

    [[nodiscard]] static std::wstring Key(
        std::wstring_view widgetId,
        std::wstring_view scopeId);

    std::unordered_map<std::wstring, Entry> entries_;
};

} // namespace gba::input
