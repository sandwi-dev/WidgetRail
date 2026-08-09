#include "AccessibilityEvents.h"

#include <algorithm>

namespace gba::accessibility {
namespace {

bool SameRect(const declarative::Rect& left, const declarative::Rect& right) noexcept {
    return left.x == right.x && left.y == right.y &&
        left.width == right.width && left.height == right.height;
}

std::optional<std::wstring> FocusedId(const Tree* tree) {
    if (!tree || !tree->focusedNode || *tree->focusedNode >= tree->nodes.size())
        return std::nullopt;
    return tree->nodes[*tree->focusedNode].id;
}

bool SupportsInvoke(const Node& node) noexcept {
    return (node.role == Role::Button || node.role == Role::ListItem) &&
        (!node.actionId.empty() || node.hostAction != HostAction::None);
}

bool SupportsRangeValue(const Node& node) noexcept {
    return (node.role == Role::Slider && !node.valueChangedActionId.empty()) ||
        (node.role == Role::Progress && node.rangeMaximum > node.rangeMinimum);
}

bool RangeReadOnly(const Node& node) noexcept {
    return node.role != Role::Slider || !node.enabled ||
        node.valueChangedActionId.empty();
}

double RangeLargeChange(const Node& node) noexcept {
    return std::min(node.rangeMaximum - node.rangeMinimum, node.rangeStep * 10.0);
}

bool SameStructure(const Tree& left, const Tree& right) {
    if (left.widgetId != right.widgetId ||
        left.runtimeGeneration != right.runtimeGeneration ||
        left.activeInputScopeId != right.activeInputScopeId ||
        left.nodes.size() != right.nodes.size()) return false;
    for (std::size_t index = 0; index < left.nodes.size(); ++index) {
        const auto& before = left.nodes[index];
        const auto& after = right.nodes[index];
        if (before.id != after.id || before.role != after.role ||
            before.parent != after.parent || before.children != after.children ||
            SupportsInvoke(before) != SupportsInvoke(after) ||
            SupportsRangeValue(before) != SupportsRangeValue(after)) return false;
    }
    return true;
}

template<typename Value>
void AddIfChanged(
    EventPlan& plan,
    const std::wstring& nodeId,
    const PropertyKind kind,
    const Value& before,
    const Value& after) {
    if (before != after)
        plan.properties.push_back({nodeId, kind, before, after});
}

} // namespace

EventPlan PlanEvents(const Tree* previous, const Tree* current) {
    EventPlan plan;
    plan.structureChanged = !previous || !current || !SameStructure(*previous, *current);
    const auto oldFocus = FocusedId(previous);
    const auto newFocus = FocusedId(current);
    plan.focusChanged = oldFocus != newFocus;
    plan.focusedNodeId = newFocus;
    if (!previous || !current ||
        previous->widgetId != current->widgetId ||
        previous->runtimeGeneration != current->runtimeGeneration) return plan;

    for (const auto& after : current->nodes) {
        const auto before = std::find_if(
            previous->nodes.begin(), previous->nodes.end(),
            [&](const Node& candidate) { return candidate.id == after.id; });
        if (before == previous->nodes.end() || before->role != after.role) continue;
        AddIfChanged(plan, after.id, PropertyKind::Name, before->name, after.name);
        AddIfChanged(plan, after.id, PropertyKind::HelpText, before->value, after.value);
        AddIfChanged(plan, after.id, PropertyKind::Enabled, before->enabled, after.enabled);
        AddIfChanged(plan, after.id, PropertyKind::Selected, before->selected, after.selected);
        AddIfChanged(
            plan, after.id, PropertyKind::HeadingLevel,
            static_cast<int>(before->headingLevel), static_cast<int>(after.headingLevel));
        AddIfChanged(
            plan, after.id, PropertyKind::LiveSetting,
            static_cast<int>(before->liveSetting), static_cast<int>(after.liveSetting));
        if (before->name != after.name && after.liveSetting != LiveSetting::Off)
            plan.liveRegionChangedNodeIds.push_back(after.id);
        if (after.role == Role::Slider || after.role == Role::Progress) {
            AddIfChanged(
                plan, after.id, PropertyKind::RangeValue,
                before->rangeValue, after.rangeValue);
            AddIfChanged(
                plan, after.id, PropertyKind::RangeMinimum,
                before->rangeMinimum, after.rangeMinimum);
            AddIfChanged(
                plan, after.id, PropertyKind::RangeMaximum,
                before->rangeMaximum, after.rangeMaximum);
            AddIfChanged(
                plan, after.id, PropertyKind::RangeSmallChange,
                before->rangeStep, after.rangeStep);
            AddIfChanged(
                plan, after.id, PropertyKind::RangeLargeChange,
                RangeLargeChange(*before), RangeLargeChange(after));
            AddIfChanged(
                plan, after.id, PropertyKind::RangeReadOnly,
                RangeReadOnly(*before), RangeReadOnly(after));
        }
        if (!SameRect(before->bounds, after.bounds))
            plan.properties.push_back({
                after.id, PropertyKind::Bounds, before->bounds, after.bounds,
            });
    }
    return plan;
}

} // namespace gba::accessibility
