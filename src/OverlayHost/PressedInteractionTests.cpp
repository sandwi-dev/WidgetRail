#include "PressedInteraction.h"

#include <cstdlib>
#include <iostream>
#include <string_view>
#include <utility>

namespace {

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

gba::WidgetNode Button(
    const wchar_t* id,
    const wchar_t* action = L"activate") {
    gba::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.actionId = action;
    return node;
}

gba::WidgetSnapshot Snapshot(const long long sequence = 1) {
    gba::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"player.runtime.v1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"play"));

    auto shortcut = Button(L"previous", L"");
    shortcut.shortcuts.push_back({L"leftBumper", L"previous-track", L"pressed"});
    snapshot.root.children.push_back(std::move(shortcut));

    auto slider = Button(L"volume", L"toggle-mute");
    slider.kind = L"slider";
    slider.valueChangedActionId = L"volume.changed";
    slider.hasSliderRange = true;
    snapshot.root.children.push_back(std::move(slider));

    auto dialog = gba::WidgetNode{};
    dialog.id = L"dialog";
    dialog.kind = L"stack";
    dialog.inputScopeId = L"dialog";
    dialog.children.push_back(Button(L"confirm"));
    snapshot.root.children.push_back(std::move(dialog));
    return snapshot;
}

void ExactPhysicalPressLifecycle() {
    auto snapshot = Snapshot();
    gba::input::PressedInteractionState state;
    Check(state.Begin(snapshot, L"play", L"a"), "A begins focused button state");
    Check(state.ActiveElementId(snapshot, L"play") == L"play",
          "exact snapshot and focus expose pressed element");
    Check(!state.Release(L"rightBumper"), "unrelated release does not clear press");
    Check(state.Release(L"a"), "matching release clears press");
    Check(!state.active(), "release cannot leave state stuck");
}

void ActionabilityAndScopeFailClosed() {
    auto snapshot = Snapshot();
    gba::input::PressedInteractionState state;
    snapshot.root.children[0].isDisabled = true;
    Check(!state.Begin(snapshot, L"play", L"a"), "disabled button cannot press");
    snapshot.root.children[0].isDisabled = false;
    snapshot.root.children[0].isBusy = true;
    Check(!state.Begin(snapshot, L"play", L"a"), "busy button cannot press");
    snapshot.root.children[0].isBusy = false;
    Check(!state.Begin(snapshot, L"play", L"leftBumper"),
          "shortcut on a different element cannot press focused button");
    Check(!state.Begin(snapshot, L"confirm", L"a"),
          "element outside active input scope cannot press");
    snapshot.activeInputScopeId = L"dialog";
    Check(state.Begin(snapshot, L"confirm", L"a"),
          "nested active scope resolves its own focused action");
}

void ShortcutsAndSlidersAreNativePressedComponents() {
    auto snapshot = Snapshot();
    gba::input::PressedInteractionState state;
    Check(state.Begin(snapshot, L"previous", L"leftBumper"),
          "focused exact shortcut begins press");
    Check(state.Cancel(L"leftBumper"), "dispatch failure cancels exact shortcut press");
    Check(state.Begin(snapshot, L"volume", L"a"),
          "slider activation participates in pressed styling");
    Check(state.Clear(), "navigation/overlay close clears slider activation");
    Check(state.Begin(snapshot, L"volume", L"dPadRight"),
          "slider adjustment participates in pressed styling");
}

void ActionSurfacesUseTheNativePressLifecycle() {
    auto snapshot = Snapshot();
    auto tile = Button(L"album", L"open-album");
    tile.kind = L"actionSurface";
    auto title = gba::WidgetNode{};
    title.id = L"album.title";
    title.kind = L"text";
    title.text = L"Album title";
    tile.children.push_back(std::move(title));
    snapshot.root.children.insert(snapshot.root.children.begin(), std::move(tile));

    gba::input::PressedInteractionState state;
    Check(state.Begin(snapshot, L"album", L"a"),
          "A begins the full ActionSurface press target");
    Check(state.ActiveElementId(snapshot, L"album") == L"album",
          "ActionSurface exposes its exact full-tile pressed identity");
    Check(state.Release(L"a"), "ActionSurface releases with the matching A button");

    snapshot.root.children[0].isDisabled = true;
    Check(!state.Begin(snapshot, L"album", L"a"),
          "disabled ActionSurface rejects activation");
    snapshot.root.children[0].isDisabled = false;
    snapshot.root.children[0].isBusy = true;
    Check(!state.Begin(snapshot, L"album", L"a"),
          "busy ActionSurface rejects activation");
    snapshot.root.children[0].isBusy = false;
    Check(!state.Begin(snapshot, L"album.title", L"a"),
          "presentational ActionSurface child cannot begin an independent press");
}

void SnapshotAndFocusReplacementClearState() {
    auto snapshot = Snapshot();
    gba::input::PressedInteractionState state;
    Check(state.Begin(snapshot, L"play", L"a"), "precondition press begins");
    auto replacement = Snapshot(2);
    Check(state.Reconcile(replacement, L"play"), "new snapshot clears transient press");
    Check(state.ActiveElementId(replacement, L"play").empty(),
          "replacement cannot inherit press state");

    Check(state.Begin(replacement, L"play", L"a"), "press restarts on replacement");
    Check(state.Reconcile(replacement, L"previous"), "focus navigation clears press");
    Check(!state.active(), "focus reconciliation cannot leave stale state");

    Check(state.Begin(replacement, L"play", L"a"), "press restarts before worker replacement");
    replacement.instanceId = L"player.runtime.v2";
    Check(state.Reconcile(replacement, L"play"), "worker replacement clears press");
}

} // namespace

int main() {
    ExactPhysicalPressLifecycle();
    ActionabilityAndScopeFailClosed();
    ShortcutsAndSlidersAreNativePressedComponents();
    ActionSurfacesUseTheNativePressLifecycle();
    SnapshotAndFocusReplacementClearState();
    std::cout << "Pressed interaction checks passed: " << checks << '\n';
    return EXIT_SUCCESS;
}
