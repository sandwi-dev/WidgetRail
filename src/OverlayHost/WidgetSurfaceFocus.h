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

    /// Returns remembered focus, then valid initial focus, then the first
    /// enabled button in tree order. An empty string is valid for focusless
    /// surfaces whose container-level shortcuts still accept input.
    [[nodiscard]] std::wstring Restore(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot) const;

private:
    [[nodiscard]] static std::wstring Key(
        std::wstring_view widgetId,
        std::wstring_view scopeId);

    std::unordered_map<std::wstring, std::wstring> entries_;
};

} // namespace gba::input
