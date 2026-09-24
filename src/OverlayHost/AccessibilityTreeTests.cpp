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
    button.isDisabled = true;
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
    tile.isDisabled = true;
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
          tree.nodes[1].actionId == L"next" && !tree.nodes[1].enabled,
          "disabled button retains Invoke identity while exposing Unavailable");
    Check(tree.focusedNode == 1 && tree.nodes[1].focused,
          "logical controller focus is represented");
    const auto focuslessTree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"");
    Check(!focuslessTree.focusedNode &&
              std::none_of(
                  focuslessTree.nodes.begin(), focuslessTree.nodes.end(),
                  [](const auto& node) { return node.focused; }),
          "focusless current presentation exposes no synthetic UIA focus");
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
    Check(tree.nodes[3].name == L"Open album" && tree.nodes[3].children.empty() &&
          !tree.nodes[3].enabled,
          "disabled action surface is Unavailable and its descendants remain presentational");
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
    mediaViewport.mediaSessionId = L"cedar.media";
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

    widgetrail::WidgetSnapshot selectSnapshot;
    selectSnapshot.sequence = 41;
    selectSnapshot.instanceId = L"select.instance";
    selectSnapshot.activeInputScopeId = L"select.root";
    selectSnapshot.root.id = L"select.root";
    selectSnapshot.root.kind = L"stack";
    widgetrail::WidgetNode select;
    select.id = L"select.output";
    select.kind = L"button";
    select.isSelect = true;
    select.accessibilityLabel = L"Output device";
    select.accessibilityValue = L"Option 0";
    for (int index = 0; index < 12; ++index) {
        widgetrail::WidgetSelectOption option;
        option.id = L"option." + std::to_wstring(index);
        option.label = L"Option " + std::to_wstring(index);
        option.actionId = L"select.option-" + std::to_wstring(index);
        option.accessibilityLabel = L"Accessible option " + std::to_wstring(index);
        option.isSelected = index == 0;
        select.selectOptions.push_back(std::move(option));
    }
    selectSnapshot.root.children.push_back(select);
    widgetrail::RenderResult selectRender;
    selectRender.accessibilityRegions = {Region(L"select.output", 20)};
    widgetrail::accessibility::SelectPopupAccessibility popup;
    popup.openerElementId = L"select.output";
    popup.options = select.selectOptions;
    popup.highlightedOption = 9;
    for (std::size_t index = 8; index < 12; ++index)
    popup.items.push_back({
            index, {20.0F, 80.0F + 38.0F * static_cast<float>(index - 8),
                    220.0F, 38.0F}});
    const auto collapsedSelectTree = widgetrail::accessibility::BuildWidgetTree(
        L"select", L"generation-select", selectSnapshot, selectRender,
        L"select.output");
    Check(collapsedSelectTree.nodes.size() == 2 &&
              collapsedSelectTree.nodes.front().role ==
                  widgetrail::accessibility::Role::ComboBox &&
              !collapsedSelectTree.nodes.front().expanded &&
              collapsedSelectTree.nodes[1].selected &&
              collapsedSelectTree.nodes[1].offscreen &&
              collapsedSelectTree.nodes[1].domain ==
                  widgetrail::accessibility::ElementDomain::WidgetOption &&
              collapsedSelectTree.nodes[1].hostAction ==
                  widgetrail::accessibility::HostAction::None,
          "collapsed Select projects only its selected offscreen provider without an action");
    const auto selectTree = widgetrail::accessibility::BuildWidgetTree(
        L"select", L"generation-select", selectSnapshot, selectRender,
        L"select.output", {}, &popup);
    Check(selectTree.nodes.size() == 13 &&
              selectTree.nodes.front().role ==
                  widgetrail::accessibility::Role::ComboBox &&
              selectTree.nodes.front().expanded &&
              selectTree.nodes.front().children.size() == 12,
          "expanded Select exposes one ComboBox and the complete bounded option set");
    Check(selectTree.nodes[1].selected && selectTree.nodes[1].offscreen &&
              selectTree.nodes[1].positionInSet == 1 &&
              selectTree.nodes[1].sizeOfSet == 12,
          "selected off-page option remains available to SelectionProvider");
    Check(std::count_if(
              selectTree.nodes.begin(), selectTree.nodes.end(),
              [](const auto& node) { return node.focused; }) == 1 &&
              selectTree.focusedNode &&
              selectTree.nodes[*selectTree.focusedNode].id ==
                  L"select-option:13:select.output8:option.9",
          "expanded Select exposes exactly one highlighted UIA focus owner");
    auto collisionSnapshot = selectSnapshot;
    widgetrail::WidgetNode authoredCollision;
    authoredCollision.id = L"select.output.option.option.0";
    authoredCollision.kind = L"button";
    authoredCollision.text = L"Authored collision";
    authoredCollision.actionId = L"authored.collision";
    collisionSnapshot.root.children.push_back(authoredCollision);
    auto collisionRender = selectRender;
    collisionRender.accessibilityRegions.push_back(
        Region(L"select.output.option.option.0", 260));
    const auto collisionSelectTree = widgetrail::accessibility::BuildWidgetTree(
        L"select", L"generation-select", collisionSnapshot, collisionRender,
        L"select.output", {}, &popup);
    Check(widgetrail::accessibility::HasUniqueElementKeys(collisionSelectTree) &&
          std::count_if(
              collisionSelectTree.nodes.begin(), collisionSelectTree.nodes.end(),
              [](const auto& node) {
                  return node.id == L"select.output.option.option.0";
              }) == 1,
          "synthetic option identity cannot collide with an authored same-domain node ID");

    auto syntheticCollisionSnapshot = selectSnapshot;
    syntheticCollisionSnapshot.root.children.clear();
    widgetrail::WidgetNode firstSelect = select;
    firstSelect.id = L"alpha.option.beta";
    firstSelect.selectOptions.front().id = L"gamma";
    firstSelect.selectOptions.resize(1);
    widgetrail::WidgetNode secondSelect = select;
    secondSelect.id = L"alpha";
    secondSelect.selectOptions.front().id = L"beta.option.gamma";
    secondSelect.selectOptions.resize(1);
    syntheticCollisionSnapshot.root.children = {firstSelect, secondSelect};
    widgetrail::RenderResult syntheticCollisionRender;
    syntheticCollisionRender.accessibilityRegions = {
        Region(firstSelect.id.c_str(), 20),
        Region(secondSelect.id.c_str(), 80)};
    const auto syntheticCollisionTree = widgetrail::accessibility::BuildWidgetTree(
        L"select", L"generation-select", syntheticCollisionSnapshot,
        syntheticCollisionRender, L"");
    Check(widgetrail::accessibility::HasUniqueElementKeys(syntheticCollisionTree) &&
          syntheticCollisionTree.nodes.size() == 4 &&
          syntheticCollisionTree.nodes[1].id != syntheticCollisionTree.nodes[3].id,
          "length-prefixed synthetic option identities remain injective across two Select owners");

    auto unavailableSnapshot = selectSnapshot;
    for (auto& option : unavailableSnapshot.root.children[0].selectOptions)
        option.isDisabled = true;
    const auto unavailableTree = widgetrail::accessibility::BuildWidgetTree(
        L"select", L"generation-select", unavailableSnapshot, selectRender,
        L"select.output");
    Check(!unavailableTree.nodes.front().enabled &&
              unavailableTree.nodes.front().hostAction ==
                  widgetrail::accessibility::HostAction::None,
          "a Select with no available options is exposed as unavailable and unexpandable");

    widgetrail::WidgetSnapshot switchSnapshot;
    switchSnapshot.sequence = 42;
    switchSnapshot.instanceId = L"switch.instance";
    switchSnapshot.activeInputScopeId = L"switch.root";
    switchSnapshot.root.id = L"switch.root";
    switchSnapshot.root.kind = L"stack";
    switchSnapshot.root.inputScopeId = L"switch.root";
    widgetrail::WidgetNode switchOn;
    switchOn.id = L"switch.on";
    switchOn.kind = L"button";
    switchOn.text = L"Motion  On";
    switchOn.accessibilityLabel = L"Motion, On";
    switchOn.actionId = L"toggle-motion";
    switchOn.isSelected = true;
    switchOn.isDisabled = true;
    widgetrail::WidgetNode switchOff = switchOn;
    switchOff.id = L"switch.off";
    switchOff.text = L"Motion  Off";
    switchOff.accessibilityLabel = L"Motion, Off";
    switchOff.isSelected = false;
    switchOff.isDisabled = false;
    switchSnapshot.root.children = {switchOn, switchOff};
    widgetrail::RenderResult switchRender;
    switchRender.accessibilityRegions = {
        Region(L"switch.on", 20), Region(L"switch.off", 60)};
    const auto switchTree = widgetrail::accessibility::BuildWidgetTree(
        L"switch", L"generation-switch", switchSnapshot, switchRender,
        L"switch.on");
    Check(switchTree.nodes.size() == 2 &&
              switchTree.nodes[0].role == widgetrail::accessibility::Role::Button &&
              switchTree.nodes[0].selected && !switchTree.nodes[0].enabled &&
              switchTree.nodes[0].name == L"Motion, On" &&
              !switchTree.nodes[1].selected && switchTree.nodes[1].enabled &&
              switchTree.nodes[1].name == L"Motion, Off",
          "Switch On and Off retain exact selected and unavailable accessibility state independent of visual glyphs");

    render.accessibilityRegions.clear();
    tree = widgetrail::accessibility::BuildWidgetTree(
        L"music", L"generation-1", snapshot, render, L"modal-text");
    Check(tree.nodes.empty() && !tree.focusedNode,
          "unpainted semantic nodes are never exposed to assistive technology");

    {
        widgetrail::WidgetSnapshot prompts;
        prompts.instanceId = L"prompt.instance"; prompts.sequence = 1;
        prompts.activeInputScopeId = L"key";
        prompts.root.id = L"key"; prompts.root.kind = L"controllerGlyph";
        prompts.root.controllerPrompt = L"a";
        widgetrail::RenderResult glyphRender;
        glyphRender.accessibilityRegions = {Region(L"key", 0)};
        const auto xbox = widgetrail::accessibility::BuildWidgetTree(L"prompts", L"generation", prompts, glyphRender, L"");
        Check(xbox.nodes.size() == 1 && xbox.nodes[0].name == L"A button" && !xbox.focusedNode,
            "controller symbol has a noninteractive accessible name");
        glyphRender.playStationControls = true;
        const auto ps = widgetrail::accessibility::BuildWidgetTree(L"prompts", L"generation", prompts, glyphRender, L"");
        Check(ps.nodes.size() == 1 && ps.nodes[0].name == L"Cross button", "accessible name follows rendered controller family");
        prompts.root.accessibilityLabel = L"Previous section";
        const auto custom = widgetrail::accessibility::BuildWidgetTree(L"prompts", L"generation", prompts, glyphRender, L"");
        Check(custom.nodes[0].name == L"Previous section", "authored accessible context is retained");
    }
    std::cout << "AccessibilityTreeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
