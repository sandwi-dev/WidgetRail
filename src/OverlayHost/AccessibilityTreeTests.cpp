#include "AccessibilityTree.h"

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

gba::RenderAccessibilityRegion Region(const wchar_t* id, const float top) {
    return {id, {10.0F, top, 120.0F, 32.0F}};
}

gba::WidgetNode Text(const wchar_t* id, const wchar_t* value) {
    gba::WidgetNode node;
    node.id = id;
    node.kind = L"text";
    node.text = value;
    return node;
}

} // namespace

int main() {
    gba::WidgetSnapshot snapshot;
    snapshot.sequence = 9;
    snapshot.instanceId = L"music.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";

    snapshot.root.children.push_back(Text(L"heading", L"Now playing"));
    gba::WidgetNode button;
    button.id = L"next";
    button.kind = L"button";
    button.accessibilityLabel = L"Next track";
    button.actionId = L"next";
    snapshot.root.children.push_back(button);

    gba::WidgetNode slider;
    slider.id = L"progress";
    slider.kind = L"slider";
    slider.accessibilityLabel = L"Playback position";
    slider.accessibilityValue = L"one minute";
    slider.valueChangedActionId = L"seek";
    slider.value = 60;
    slider.minimum = 0;
    slider.maximum = 180;
    slider.step = 5;
    slider.isBusy = true;
    snapshot.root.children.push_back(slider);

    gba::WidgetNode tile;
    tile.id = L"album";
    tile.kind = L"actionSurface";
    tile.accessibilityLabel = L"Open album";
    tile.actionId = L"open-album";
    tile.children.push_back(Text(L"album-title", L"Duplicate visual title"));
    snapshot.root.children.push_back(tile);

    gba::WidgetNode textEntry;
    textEntry.id = L"search";
    textEntry.kind = L"textEntry";
    textEntry.accessibilityLabel = L"Search installed games";
    textEntry.actionId = L"search.commit";
    snapshot.root.children.push_back(textEntry);

    gba::WidgetNode modal;
    modal.id = L"modal";
    modal.kind = L"stack";
    modal.inputScopeId = L"modal";
    modal.children.push_back(Text(L"modal-text", L"Hidden modal"));
    snapshot.root.children.push_back(modal);

    gba::RenderResult render;
    render.accessibilityRegions = {
        Region(L"heading", 10), Region(L"next", 50), Region(L"progress", 90),
        Region(L"album", 130), Region(L"album-title", 135), Region(L"search", 170),
        Region(L"modal-text", 210),
    };

    auto tree = gba::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"next");
    Check(tree.widgetId == L"music" && tree.runtimeGeneration == L"generation-1",
          "tree retains exact widget runtime identity");
    Check(tree.snapshotSequence == 9, "tree retains snapshot authority");
    Check(tree.activeInputScopeId == L"root", "tree retains active input scope authority");
    Check(tree.nodes.size() == 5, "active semantic nodes are exposed exactly once");
    Check(gba::accessibility::HasUniqueElementKeys(tree) &&
          tree.nodes[0].domain == gba::accessibility::ElementDomain::Widget &&
          gba::accessibility::AutomationId(tree.nodes[1]) == L"widget:next",
          "widget projection owns a unique namespaced UIA identity");
    Check(tree.nodes[0].role == gba::accessibility::Role::Text &&
          tree.nodes[0].name == L"Now playing", "visible text has a static-text name");
    Check(tree.nodes[1].role == gba::accessibility::Role::Button &&
          tree.nodes[1].actionId == L"next", "button exposes Invoke authority metadata");
    Check(tree.focusedNode == 1 && tree.nodes[1].focused,
          "logical controller focus is represented");
    Check(tree.nodes[2].role == gba::accessibility::Role::Slider &&
          tree.nodes[2].value == L"one minute" && !tree.nodes[2].enabled,
          "slider exposes value and busy state");
    Check(tree.nodes[2].rangeValue == 60 && tree.nodes[2].rangeMaximum == 180 &&
          tree.nodes[2].rangeStep == 5, "slider exposes a closed numeric range");
    const std::map<std::wstring, double, std::less<>> presentedValues{
        {L"progress", 75},
    };
    tree = gba::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"next", presentedValues);
    Check(tree.nodes[2].rangeValue == 75,
          "accessibility range uses the same optimistic value as rendered pixels");
    Check(tree.nodes[3].name == L"Open album" && tree.nodes[3].children.empty(),
          "action-surface descendants remain presentation-only");
    Check(tree.nodes[4].role == gba::accessibility::Role::Button &&
          tree.nodes[4].id == L"search" &&
          tree.nodes[4].actionId == L"search.commit",
          "text entry exposes one exact modal-opening Invoke action");
    Check(std::none_of(tree.nodes.begin(), tree.nodes.end(), [](const auto& node) {
        return node.id == L"modal-text";
    }), "inactive input scopes are absent from the accessibility tree");

    snapshot.activeInputScopeId = L"modal";
    tree = gba::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"modal-text");
    Check(tree.nodes.size() == 1 && tree.nodes[0].name == L"Hidden modal",
          "modal scope replaces rather than merges the root accessibility surface");
    Check(tree.focusedNode == 0, "modal focus is independently represented");

    render.accessibilityRegions.clear();
    tree = gba::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"modal-text");
    Check(tree.nodes.empty() && !tree.focusedNode,
          "unpainted semantic nodes are never exposed to assistive technology");

    std::cout << "AccessibilityTreeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
