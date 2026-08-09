#pragma once

#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <cstddef>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <vector>

namespace gba::accessibility {

enum class Role {
    Button,
    Slider,
    Text,
    Heading,
    Status,
    Image,
    Progress,
    ListItem,
};

enum class HeadingLevel {
    None,
    Level1,
};

enum class LiveSetting {
    Off,
    Polite,
    Assertive,
};

enum class HostAction {
    None,
    ActivateTrayItem,
    BackToTray,
    BackWithinWidget,
    CloseOverlay,
};

enum class ElementDomain {
    Widget,
    HostShell,
    Tray,
};

struct ElementKey final {
    ElementDomain domain{ElementDomain::Widget};
    std::wstring id;

    friend bool operator==(const ElementKey&, const ElementKey&) = default;
};

struct Node final {
    std::wstring id;
    std::wstring name;
    std::wstring value;
    std::wstring actionId;
    std::wstring valueChangedActionId;
    std::wstring hostTargetId;
    declarative::Rect bounds;
    Role role{Role::Text};
    HeadingLevel headingLevel{HeadingLevel::None};
    LiveSetting liveSetting{LiveSetting::Off};
    HostAction hostAction{HostAction::None};
    ElementDomain domain{ElementDomain::Widget};
    std::optional<std::size_t> parent;
    std::vector<std::size_t> children;
    double rangeValue{};
    double rangeMinimum{};
    double rangeMaximum{};
    double rangeStep{};
    bool enabled{true};
    bool selected{};
    bool focused{};
    bool keyboardFocusable{true};
};

struct Tree final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::wstring activeInputScopeId;
    std::vector<Node> nodes;
    std::optional<std::size_t> focusedNode;
    std::wstring name;
};

[[nodiscard]] inline ElementKey KeyFor(const Node& node) {
    return {node.domain, node.id};
}
[[nodiscard]] inline std::wstring AutomationId(const Node& node) {
    std::wstring result;
    switch (node.domain) {
    case ElementDomain::Widget: result = L"widget:"; break;
    case ElementDomain::HostShell: result = L"host:"; break;
    case ElementDomain::Tray: result = L"tray:"; break;
    }
    result += node.id;
    return result;
}
[[nodiscard]] inline bool HasUniqueElementKeys(const Tree& tree) {
    std::set<std::pair<ElementDomain, std::wstring_view>> keys;
    for (const auto& node : tree.nodes) {
        if (node.id.empty() || !keys.emplace(node.domain, node.id).second) return false;
    }
    return true;
}

/// Builds a closed immutable accessibility tree from the exact semantic
/// geometry produced by the completed render pass. Only the active input scope
/// is exposed, and ActionSurface descendants remain presentation-only.
[[nodiscard]] Tree BuildWidgetTree(
    std::wstring widgetId,
    std::wstring runtimeGeneration,
    const WidgetSnapshot& snapshot,
    const RenderResult& render,
    std::wstring_view focusedElementId,
    const std::map<std::wstring, double, std::less<>>& presentedSliderValues = {});

} // namespace gba::accessibility
