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

widgetrail::RenderAccessibilityRegion Region(const wchar_t* id, const float top) {
    return {id, {10.0F, top, 120.0F, 32.0F}};
}

widgetrail::WidgetNode Text(const wchar_t* id, const wchar_t* value) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"text";
    node.text = value;
    return node;
}

} // namespace

int main() {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 9;
    snapshot.instanceId = L"music.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";

    snapshot.root.children.push_back(Text(L"heading", L"Now playing"));
    widgetrail::WidgetNode button;
    button.id = L"next";
    button.kind = L"button";
    button.accessibilityLabel = L"Next track";
    button.actionId = L"next";
    snapshot.root.children.push_back(button);

    widgetrail::WidgetNode slider;
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

    widgetrail::WidgetNode tile;
    tile.id = L"album";
    tile.kind = L"actionSurface";
    tile.accessibilityLabel = L"Open album";
    tile.actionId = L"open-album";
    tile.children.push_back(Text(L"album-title", L"Duplicate visual title"));
    snapshot.root.children.push_back(tile);

    widgetrail::WidgetNode textEntry;
    textEntry.id = L"search";
    textEntry.kind = L"textEntry";
    textEntry.accessibilityLabel = L"Search installed games";
    textEntry.actionId = L"search.commit";
    snapshot.root.children.push_back(textEntry);

    widgetrail::WidgetNode modal;
    modal.id = L"modal";
    modal.kind = L"stack";
    modal.inputScopeId = L"modal";
    modal.children.push_back(Text(L"modal-text", L"Hidden modal"));
    snapshot.root.children.push_back(modal);

    widgetrail::RenderResult render;
    render.accessibilityRegions = {
        Region(L"heading", 10), Region(L"next", 50), Region(L"progress", 90),
        Region(L"album", 130), Region(L"album-title", 135), Region(L"search", 170),
        Region(L"modal-text", 210),
    };

    auto tree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"next");
    Check(tree.widgetId == L"music" && tree.runtimeGeneration == L"generation-1",
          "tree retains exact widget runtime identity");
    Check(tree.snapshotSequence == 9, "tree retains snapshot authority");
    Check(tree.activeInputScopeId == L"root", "tree retains active input scope authority");
    Check(tree.nodes.size() == 5, "active semantic nodes are exposed exactly once");
    Check(widgetrail::accessibility::HasUniqueElementKeys(tree) &&
          tree.nodes[0].domain == widgetrail::accessibility::ElementDomain::Widget &&
          widgetrail::accessibility::AutomationId(tree.nodes[1]) == L"widget:next",
          "widget projection owns a unique namespaced UIA identity");
    Check(tree.nodes[0].role == widgetrail::accessibility::Role::Text &&
          tree.nodes[0].name == L"Now playing", "visible text has a static-text name");
    Check(tree.nodes[1].role == widgetrail::accessibility::Role::Button &&
          tree.nodes[1].actionId == L"next", "button exposes Invoke authority metadata");
    Check(tree.focusedNode == 1 && tree.nodes[1].focused,
          "logical controller focus is represented");
    Check(tree.nodes[2].role == widgetrail::accessibility::Role::Slider &&
          tree.nodes[2].value == L"one minute" && !tree.nodes[2].enabled,
          "slider exposes value and busy state");
    Check(tree.nodes[2].rangeValue == 60 && tree.nodes[2].rangeMaximum == 180 &&
          tree.nodes[2].rangeStep == 5, "slider exposes a closed numeric range");
    const std::map<std::wstring, double, std::less<>> presentedValues{
        {L"progress", 75},
    };
    tree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"next", presentedValues);
    Check(tree.nodes[2].rangeValue == 75,
          "accessibility range uses the same optimistic value as rendered pixels");
    Check(tree.nodes[3].name == L"Open album" && tree.nodes[3].children.empty(),
          "action-surface descendants remain presentation-only");
    Check(tree.nodes[4].role == widgetrail::accessibility::Role::Button &&
          tree.nodes[4].id == L"search" &&
          tree.nodes[4].actionId == L"search.commit",
          "text entry exposes one exact modal-opening Invoke action");
    Check(std::none_of(tree.nodes.begin(), tree.nodes.end(), [](const auto& node) {
        return node.id == L"modal-text";
    }), "inactive input scopes are absent from the accessibility tree");

    snapshot.activeInputScopeId = L"modal";
    tree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"modal-text");
    Check(tree.nodes.size() == 1 && tree.nodes[0].name == L"Hidden modal",
          "modal scope replaces rather than merges the root accessibility surface");
    Check(tree.focusedNode == 0, "modal focus is independently represented");

    widgetrail::WidgetSnapshot virtualSnapshot;
    virtualSnapshot.protocolVersion = 19;
    virtualSnapshot.sequence = 10;
    virtualSnapshot.instanceId = L"virtual.instance";
    virtualSnapshot.activeInputScopeId = L"virtual.list";
    virtualSnapshot.root.id = L"virtual.list";
    virtualSnapshot.root.kind = L"scroll";
    virtualSnapshot.root.inputScopeId = L"virtual.list";
    virtualSnapshot.root.scrollAxis = L"vertical";
    virtualSnapshot.root.collectionAnchorKey = L"key.5000";
    virtualSnapshot.root.virtualCollectionWindow = widgetrail::VirtualCollectionWindow{
        3,
        widgetrail::VirtualCollectionWindowChange::Replace,
        5000,
        10'000,
        true,
        true,
        52.0,
    };
    for (int index = 5000; index < 5002; ++index) {
        widgetrail::WidgetNode item;
        item.id = L"virtual.item." + std::to_wstring(index);
        item.kind = L"button";
        item.accessibilityLabel = L"Virtual item";
        item.actionId = L"select";
        item.collectionItemKey = L"key." + std::to_wstring(index);
        virtualSnapshot.root.children.push_back(std::move(item));
    }
    widgetrail::RenderResult virtualRender;
    virtualRender.accessibilityRegions = {
        Region(L"virtual.item.5000", 10),
        Region(L"virtual.item.5001", 50),
    };
    auto virtualTree = widgetrail::accessibility::BuildWidgetTree(
        L"virtual", L"generation-1", virtualSnapshot, virtualRender,
        L"virtual.item.5000");
    Check(virtualTree.nodes.size() == 2 &&
          virtualTree.nodes[0].positionInSet == 5001 &&
          virtualTree.nodes[1].positionInSet == 5002 &&
          virtualTree.nodes[0].sizeOfSet == 10'000 &&
          virtualTree.nodes[1].sizeOfSet == 10'000,
          "realized virtual items expose accurate logical set positions");

    widgetrail::WidgetSnapshot mediaSnapshot;
    mediaSnapshot.protocolVersion = 23;
    mediaSnapshot.sequence = 11;
    mediaSnapshot.instanceId = L"cedar.instance";
    mediaSnapshot.activeInputScopeId = L"cedar.root";
    mediaSnapshot.root.id = L"cedar.root";
    mediaSnapshot.root.kind = L"stack";
    widgetrail::WidgetNode mediaViewport;
    mediaViewport.id = L"cedar.viewport";
    mediaViewport.kind = L"mediaViewport";
    mediaViewport.mediaSurfaceId = L"cedar.media";
    mediaViewport.accessibilityLabel = L"Cedar local media";
    mediaSnapshot.root.children.push_back(mediaViewport);
    widgetrail::RenderResult mediaRender;
    mediaRender.accessibilityRegions = {Region(L"cedar.viewport", 20)};
    const auto mediaTree = widgetrail::accessibility::BuildWidgetTree(
        L"cedar", L"generation-2", mediaSnapshot, mediaRender, L"");
    Check(mediaTree.nodes.size() == 1 &&
          mediaTree.nodes[0].role == widgetrail::accessibility::Role::Image &&
          mediaTree.nodes[0].name == L"Cedar local media" &&
          mediaTree.nodes[0].actionId.empty() && !mediaTree.focusedNode,
          "MediaViewport exposes one named native non-interactive media semantic");

    render.accessibilityRegions.clear();
    tree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"modal-text");
    Check(tree.nodes.empty() && !tree.focusedNode,
          "unpainted semantic nodes are never exposed to assistive technology");

    std::cout << "AccessibilityTreeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
