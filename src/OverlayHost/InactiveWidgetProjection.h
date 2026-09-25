#pragma once

#include "WidgetBridgeClient.h"

#include <optional>

namespace widgetrail {

// Retained pixels do not authorize carrying a transient dialog across an
// inactive lifetime. Show its unchanged parent until the worker admits its
// current view. This never mutates the protocol checkpoint or dismisses a
// modal in an active widget.
[[nodiscard]] inline std::optional<WidgetSnapshot> ProjectInactiveWidgetParent(
    const WidgetSnapshot& snapshot, bool inactive) {
    if (!inactive || snapshot.root.kind != L"modalLayer" ||
        snapshot.root.children.size() != 2U) return std::nullopt;
    auto parent = snapshot;
    parent.root = snapshot.root.children.front();
    parent.activeInputScopeId = parent.root.inputScopeId;
    parent.initialFocusId.clear();
    parent.focusGroupEntryRequest.reset();
    parent.quickActions.clear();
    return parent;
}

} // namespace widgetrail
