#pragma once

#include "WidgetBridgeClient.h"

#include <optional>
#include <string>
#include <string_view>

namespace gba::input {

// Immutable authority captured before the host enters the text modal's nested
// message loop. It contains no snapshot or node references.
struct TextEntryActionRequest final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::wstring nodeId;
    std::wstring actionId;
    std::wstring activeInputScopeId;
    std::wstring value;
    std::wstring placeholder;
    std::size_t maximumLength{};
};

struct TextEntryActionTarget final {
    std::wstring actionId;
    std::wstring sourceElementId;
    std::wstring activeInputScopeId;
};

[[nodiscard]] std::optional<TextEntryActionRequest> CaptureTextEntryActionRequest(
    std::wstring_view widgetId,
    std::wstring_view runtimeGeneration,
    const WidgetSnapshot& snapshot,
    std::wstring_view nodeId);

// Re-resolves every action-bearing value after the modal closes. A snapshot
// refresh is intentionally stale even when it happens to reproduce the same
// node, preventing authority from surviving an intervening generation.
[[nodiscard]] std::optional<TextEntryActionTarget> ResolveTextEntryActionTarget(
    const TextEntryActionRequest& request,
    bool currentInteractiveSurface,
    std::wstring_view activeWidgetId,
    std::wstring_view runtimeGeneration,
    const WidgetSnapshot& snapshot);

} // namespace gba::input
