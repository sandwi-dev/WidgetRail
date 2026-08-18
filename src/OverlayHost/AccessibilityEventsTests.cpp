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

widgetrail::accessibility::Tree Tree() {
    widgetrail::accessibility::Tree tree;
    tree.widgetId = L"music";
    tree.runtimeGeneration = L"generation-1";
    tree.activeInputScopeId = L"root";
    widgetrail::accessibility::Node first;
    first.id = L"play";
    first.name = L"Play";
    first.value = L"Stopped";
    first.bounds = {10, 20, 80, 40};
    first.role = widgetrail::accessibility::Role::Button;
    first.focused = true;
    tree.nodes.push_back(first);
    tree.focusedNode = 0;
    widgetrail::accessibility::Node second;
    second.id = L"progress";
    second.name = L"Progress";
    second.bounds = {10, 80, 160, 24};
    second.role = widgetrail::accessibility::Role::Slider;
    second.rangeValue = 10;
    second.valueChangedActionId = L"seek";
    tree.nodes.push_back(second);
    widgetrail::accessibility::Node status;
    status.id = L"dashboard-status";
    status.name = L"Ready";
    status.role = widgetrail::accessibility::Role::Status;
    status.liveSetting = widgetrail::accessibility::LiveSetting::Polite;
    tree.nodes.push_back(status);
    return tree;
}

bool Has(
    const widgetrail::accessibility::EventPlan& plan,
    const widgetrail::accessibility::PropertyKind kind) {
    return std::any_of(plan.properties.begin(), plan.properties.end(),
        [&](const auto& change) { return change.kind == kind; });
}

} // namespace

int main() {
    auto before = Tree();
    auto plan = widgetrail::accessibility::PlanEvents(nullptr, &before);
    Check(plan.structureChanged && plan.focusChanged &&
          plan.focusedElement == widgetrail::accessibility::ElementKey{
              widgetrail::accessibility::ElementDomain::Widget, L"play"} &&
          plan.properties.empty(),
          "initial publication raises structure and focus only");
    plan = widgetrail::accessibility::PlanEvents(&before, &before);
    Check(!plan.structureChanged && !plan.focusChanged && plan.properties.empty(),
          "identical publication raises nothing");

    auto focused = before;
    focused.nodes[0].focused = false;
    focused.nodes[1].focused = true;
    focused.focusedNode = 1;
    plan = widgetrail::accessibility::PlanEvents(&before, &focused);
    Check(!plan.structureChanged && plan.focusChanged &&
          plan.focusedElement == widgetrail::accessibility::ElementKey{
              widgetrail::accessibility::ElementDomain::Widget, L"progress"},
          "focus move is independent of structure");

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
    after.nodes[2].name = L"Playback failed";
    plan = widgetrail::accessibility::PlanEvents(&focused, &after);
    Check(plan.structureChanged && !plan.focusChanged && plan.properties.size() == 11 &&
          Has(plan, widgetrail::accessibility::PropertyKind::Name) &&
          Has(plan, widgetrail::accessibility::PropertyKind::HelpText) &&
          Has(plan, widgetrail::accessibility::PropertyKind::Enabled) &&
          Has(plan, widgetrail::accessibility::PropertyKind::Selected) &&
          Has(plan, widgetrail::accessibility::PropertyKind::RangeValue) &&
          Has(plan, widgetrail::accessibility::PropertyKind::RangeMaximum) &&
          Has(plan, widgetrail::accessibility::PropertyKind::RangeSmallChange) &&
          Has(plan, widgetrail::accessibility::PropertyKind::RangeLargeChange) &&
          Has(plan, widgetrail::accessibility::PropertyKind::RangeReadOnly) &&
          Has(plan, widgetrail::accessibility::PropertyKind::Bounds),
          "closed semantic and range-pattern changes are classified exactly");
    Check(plan.liveRegionChangedElements.size() == 1 &&
          plan.liveRegionChangedElements[0] == widgetrail::accessibility::ElementKey{
              widgetrail::accessibility::ElementDomain::Widget, L"dashboard-status"},
          "polite status-name changes request one live-region event");

    auto staticHelp = before;
    staticHelp.nodes[2].id = L"dashboard-help";
    staticHelp.nodes[2].role = widgetrail::accessibility::Role::Text;
    staticHelp.nodes[2].liveSetting = widgetrail::accessibility::LiveSetting::Off;
    auto changedHelp = staticHelp;
    changedHelp.nodes[2].name = L"A Select, B Close, Y Reorder";
    plan = widgetrail::accessibility::PlanEvents(&staticHelp, &changedHelp);
    Check(plan.liveRegionChangedElements.empty() &&
          Has(plan, widgetrail::accessibility::PropertyKind::Name),
          "routine dashboard guidance updates without a live announcement");
    auto insertedStatus = changedHelp;
    insertedStatus.nodes.pop_back();
    insertedStatus.nodes.push_back(after.nodes[2]);
    plan = widgetrail::accessibility::PlanEvents(&changedHelp, &insertedStatus);
    Check(plan.structureChanged && plan.liveRegionChangedElements.size() == 1 &&
          plan.liveRegionChangedElements[0] == widgetrail::accessibility::ElementKey{
              widgetrail::accessibility::ElementDomain::Widget, L"dashboard-status"},
          "new transient feedback is announced once when it replaces static help");

    auto structural = after;
    structural.nodes.pop_back();
    structural.focusedNode.reset();
    plan = widgetrail::accessibility::PlanEvents(&after, &structural);
    Check(plan.structureChanged && plan.focusChanged && !plan.focusedElement,
          "removal raises structure and focus-clear state");
    auto replacement = before;
    replacement.runtimeGeneration = L"generation-2";
    plan = widgetrail::accessibility::PlanEvents(&before, &replacement);
    Check(plan.structureChanged && plan.properties.empty(),
          "runtime replacement never emits cross-generation properties");
    auto roleReplacement = before;
    roleReplacement.nodes[0].role = widgetrail::accessibility::Role::Slider;
    roleReplacement.nodes[0].valueChangedActionId = L"seek";
    plan = widgetrail::accessibility::PlanEvents(&before, &roleReplacement);
    Check(plan.structureChanged && plan.properties.empty(),
          "control-type replacement relies on structure invalidation");

    auto collisionBefore = before;
    widgetrail::accessibility::Node hostCollision;
    hostCollision.domain = widgetrail::accessibility::ElementDomain::HostShell;
    hostCollision.id = L"play";
    hostCollision.name = L"Back";
    hostCollision.role = widgetrail::accessibility::Role::Button;
    collisionBefore.nodes.push_back(hostCollision);
    auto collisionAfter = collisionBefore;
    collisionAfter.nodes[0].name = L"Pause";
    plan = widgetrail::accessibility::PlanEvents(&collisionBefore, &collisionAfter);
    Check(!plan.structureChanged && plan.properties.size() == 1 &&
          plan.properties[0].domain == widgetrail::accessibility::ElementDomain::Widget &&
          plan.properties[0].nodeId == L"play",
          "event diffing keeps identical raw IDs isolated by owner domain");

    std::cout << "AccessibilityEventsTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
