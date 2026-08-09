#pragma once

#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <cstddef>
#include <optional>
#include <string>
#include <vector>

namespace gba::accessibility {

enum class Role {
    Button,
    Slider,
    Text,
    Image,
    Progress,
};

struct Node final {
    std::wstring id;
    std::wstring name;
    std::wstring value;
    std::wstring actionId;
    std::wstring valueChangedActionId;
    declarative::Rect bounds;
    Role role{Role::Text};
    std::optional<std::size_t> parent;
    std::vector<std::size_t> children;
    double rangeValue{};
    double rangeMinimum{};
    double rangeMaximum{};
    double rangeStep{};
    bool enabled{true};
    bool selected{};
    bool focused{};
};

struct Tree final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::vector<Node> nodes;
    std::optional<std::size_t> focusedNode;
};

/// Builds a closed immutable accessibility tree from the exact semantic
/// geometry produced by the completed render pass. Only the active input scope
/// is exposed, and ActionSurface descendants remain presentation-only.
[[nodiscard]] Tree BuildWidgetTree(
    std::wstring widgetId,
    std::wstring runtimeGeneration,
    const WidgetSnapshot& snapshot,
    const RenderResult& render,
    std::wstring_view focusedElementId);

} // namespace gba::accessibility
