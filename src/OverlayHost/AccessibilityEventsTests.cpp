#include "AccessibilityEvents.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

gba::accessibility::Tree Tree() {
    gba::accessibility::Tree tree;
    tree.widgetId = L"music";
    tree.runtimeGeneration = L"generation-1";
    tree.activeInputScopeId = L"root";
    gba::accessibility::Node first;
    first.id = L"play";
    first.name = L"Play";
    first.value = L"Stopped";
    first.bounds = {10, 20, 80, 40};
    first.role = gba::accessibility::Role::Button;
    first.focused = true;
    tree.nodes.push_back(first);
    tree.focusedNode = 0;
    gba::accessibility::Node second;
    second.id = L"progress";
    second.name = L"Progress";
    second.bounds = {10, 80, 160, 24};
    second.role = gba::accessibility::Role::Slider;
    second.rangeValue = 10;
    second.valueChangedActionId = L"seek";
    tree.nodes.push_back(second);
    return tree;
}

bool Has(
    const gba::accessibility::EventPlan& plan,
    const gba::accessibility::PropertyKind kind) {
    return std::any_of(plan.properties.begin(), plan.properties.end(),
        [&](const auto& change) { return change.kind == kind; });
}

} // namespace

int main() {
    auto before = Tree();
    auto plan = gba::accessibility::PlanEvents(nullptr, &before);
    Check(plan.structureChanged && plan.focusChanged &&
          plan.focusedNodeId == L"play" && plan.properties.empty(),
          "initial publication raises structure and focus only");
    plan = gba::accessibility::PlanEvents(&before, &before);
    Check(!plan.structureChanged && !plan.focusChanged && plan.properties.empty(),
          "identical publication raises nothing");

    auto focused = before;
    focused.nodes[0].focused = false;
    focused.nodes[1].focused = true;
    focused.focusedNode = 1;
    plan = gba::accessibility::PlanEvents(&before, &focused);
    Check(!plan.structureChanged && plan.focusChanged &&
          plan.focusedNodeId == L"progress", "focus move is independent of structure");

    auto after = focused;
    after.nodes[0].name = L"Pause";
    after.nodes[0].value = L"Playing";
    after.nodes[0].enabled = false;
    after.nodes[0].selected = true;
    after.nodes[0].bounds.x = 30;
    after.nodes[1].rangeValue = 20;
    after.nodes[1].rangeMaximum = 240;
    after.nodes[1].rangeStep = 10;
    after.nodes[1].valueChangedActionId.clear();
    plan = gba::accessibility::PlanEvents(&focused, &after);
    Check(plan.structureChanged && !plan.focusChanged && plan.properties.size() == 10 &&
          Has(plan, gba::accessibility::PropertyKind::Name) &&
          Has(plan, gba::accessibility::PropertyKind::HelpText) &&
          Has(plan, gba::accessibility::PropertyKind::Enabled) &&
          Has(plan, gba::accessibility::PropertyKind::Selected) &&
          Has(plan, gba::accessibility::PropertyKind::RangeValue) &&
          Has(plan, gba::accessibility::PropertyKind::RangeMaximum) &&
          Has(plan, gba::accessibility::PropertyKind::RangeSmallChange) &&
          Has(plan, gba::accessibility::PropertyKind::RangeLargeChange) &&
          Has(plan, gba::accessibility::PropertyKind::RangeReadOnly) &&
          Has(plan, gba::accessibility::PropertyKind::Bounds),
          "closed semantic and range-pattern changes are classified exactly");

    auto structural = after;
    structural.nodes.pop_back();
    structural.focusedNode.reset();
    plan = gba::accessibility::PlanEvents(&after, &structural);
    Check(plan.structureChanged && plan.focusChanged && !plan.focusedNodeId,
          "removal raises structure and focus-clear state");
    auto replacement = before;
    replacement.runtimeGeneration = L"generation-2";
    plan = gba::accessibility::PlanEvents(&before, &replacement);
    Check(plan.structureChanged && plan.properties.empty(),
          "runtime replacement never emits cross-generation properties");
    auto roleReplacement = before;
    roleReplacement.nodes[0].role = gba::accessibility::Role::Slider;
    roleReplacement.nodes[0].valueChangedActionId = L"seek";
    plan = gba::accessibility::PlanEvents(&before, &roleReplacement);
    Check(plan.structureChanged && plan.properties.empty(),
          "control-type replacement relies on structure invalidation");

    std::cout << "AccessibilityEventsTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
