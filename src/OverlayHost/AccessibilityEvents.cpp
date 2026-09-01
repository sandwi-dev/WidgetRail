#include "AccessibilityEvents.h"

#include <UIAutomation.h>

#include <algorithm>

namespace widgetrail::accessibility {
namespace {

bool SameRect(const declarative::Rect& left, const declarative::Rect& right) noexcept {
    return left.x == right.x && left.y == right.y &&
        left.width == right.width && left.height == right.height;
}

std::optional<ElementKey> FocusedKey(const Tree* tree) {
    if (!tree || !tree->focusedNode || *tree->focusedNode >= tree->nodes.size())
        return std::nullopt;
    return KeyFor(tree->nodes[*tree->focusedNode]);
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
        if (before.domain != after.domain || before.id != after.id ||
            before.role != after.role ||
            before.parent != after.parent || before.children != after.children ||
            SupportsInvoke(before) != SupportsInvoke(after) ||
            SupportsRangeValue(before) != SupportsRangeValue(after)) return false;
    }
    return true;
}

template<typename Value>
void AddIfChanged(
    EventPlan& plan,
    const Node& node,
    const PropertyKind kind,
    const Value& before,
    const Value& after) {
    if (before != after)
        plan.properties.push_back({node.id, kind, before, after, node.domain});
}

} // namespace

EventPlan PlanEvents(const Tree* previous, const Tree* current) {
    EventPlan plan;
    plan.structureChanged = !previous || !current || !SameStructure(*previous, *current);
    const auto oldFocus = FocusedKey(previous);
    const auto newFocus = FocusedKey(current);
    plan.focusChanged = oldFocus != newFocus;
    plan.focusedElement = newFocus;
    if (!previous || !current ||
        previous->widgetId != current->widgetId ||
        previous->runtimeGeneration != current->runtimeGeneration) return plan;

    for (const auto& after : current->nodes) {
        const auto before = std::find_if(
            previous->nodes.begin(), previous->nodes.end(),
            [&](const Node& candidate) { return KeyFor(candidate) == KeyFor(after); });
        if (before == previous->nodes.end()) {
            if (after.role == Role::ListItem && after.selected)
                plan.selectedElements.push_back(KeyFor(after));
            if (after.liveSetting != LiveSetting::Off && !after.name.empty())
                plan.liveRegionChangedElements.push_back(KeyFor(after));
            continue;
        }
        if (before->role != after.role) continue;
        AddIfChanged(plan, after, PropertyKind::Name, before->name, after.name);
        AddIfChanged(
            plan, after,
            after.role == Role::ComboBox
                ? PropertyKind::Value : PropertyKind::HelpText,
            before->value, after.value);
        AddIfChanged(plan, after, PropertyKind::Enabled, before->enabled, after.enabled);
        AddIfChanged(plan, after, PropertyKind::Selected, before->selected, after.selected);
        AddIfChanged(
            plan, after, PropertyKind::Offscreen,
            before->offscreen, after.offscreen);
        if (after.role == Role::ListItem && after.selected && !before->selected)
            plan.selectedElements.push_back(KeyFor(after));
        if (after.role == Role::ComboBox)
            AddIfChanged(
                plan, after, PropertyKind::Expanded,
                before->expanded
                    ? static_cast<int>(ExpandCollapseState_Expanded)
                    : static_cast<int>(ExpandCollapseState_Collapsed),
                after.expanded
                    ? static_cast<int>(ExpandCollapseState_Expanded)
                    : static_cast<int>(ExpandCollapseState_Collapsed));
        AddIfChanged(
            plan, after, PropertyKind::HeadingLevel,
            static_cast<int>(before->headingLevel), static_cast<int>(after.headingLevel));
        AddIfChanged(
            plan, after, PropertyKind::LiveSetting,
            static_cast<int>(before->liveSetting), static_cast<int>(after.liveSetting));
        if (before->name != after.name && after.liveSetting != LiveSetting::Off)
            plan.liveRegionChangedElements.push_back(KeyFor(after));
        if (after.role == Role::Slider || after.role == Role::Progress) {
            AddIfChanged(
                plan, after, PropertyKind::RangeValue,
                before->rangeValue, after.rangeValue);
            AddIfChanged(
                plan, after, PropertyKind::RangeMinimum,
                before->rangeMinimum, after.rangeMinimum);
            AddIfChanged(
                plan, after, PropertyKind::RangeMaximum,
                before->rangeMaximum, after.rangeMaximum);
            AddIfChanged(
                plan, after, PropertyKind::RangeSmallChange,
                before->rangeStep, after.rangeStep);
            AddIfChanged(
                plan, after, PropertyKind::RangeLargeChange,
                RangeLargeChange(*before), RangeLargeChange(after));
            AddIfChanged(
                plan, after, PropertyKind::RangeReadOnly,
                RangeReadOnly(*before), RangeReadOnly(after));
        }
        if (!SameRect(before->bounds, after.bounds))
            plan.properties.push_back({
                after.id, PropertyKind::Bounds, before->bounds, after.bounds, after.domain,
            });
    }
    return plan;
}

} // namespace widgetrail::accessibility
