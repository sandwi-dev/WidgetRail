#pragma once

#include "AccessibilityTree.h"

#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace gba::accessibility {

enum class PropertyKind {
    Name,
    HelpText,
    Enabled,
    Selected,
    RangeValue,
    RangeMinimum,
    RangeMaximum,
    RangeSmallChange,
    RangeLargeChange,
    RangeReadOnly,
    HeadingLevel,
    LiveSetting,
    Bounds,
};

using PropertyValue = std::variant<std::wstring, bool, double, int, declarative::Rect>;

struct PropertyChange final {
    std::wstring nodeId;
    PropertyKind kind{PropertyKind::Name};
    PropertyValue oldValue;
    PropertyValue newValue;
};

struct EventPlan final {
    bool structureChanged{};
    bool focusChanged{};
    std::optional<std::wstring> focusedNodeId;
    std::vector<std::wstring> liveRegionChangedNodeIds;
    std::vector<PropertyChange> properties;
};

[[nodiscard]] EventPlan PlanEvents(
    const Tree* previous,
    const Tree* current);

} // namespace gba::accessibility
