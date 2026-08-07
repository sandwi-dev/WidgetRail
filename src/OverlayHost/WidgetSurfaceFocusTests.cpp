#include "WidgetSurfaceFocus.h"

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

gba::WidgetNode Button(const wchar_t* id, const bool disabled = false) {
    gba::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.isDisabled = disabled;
    return node;
}

gba::WidgetSnapshot Snapshot(const wchar_t* activeScope) {
    gba::WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"test.instance";
    snapshot.activeInputScopeId = activeScope;
    snapshot.initialFocusId = L"root-first";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"root-first"));
    snapshot.root.children.push_back(Button(L"root-second"));

    gba::WidgetNode modal;
    modal.id = L"modal-container";
    modal.kind = L"stack";
    modal.inputScopeId = L"modal";
    modal.children.push_back(Button(L"modal-first"));
    modal.children.push_back(Button(L"modal-second"));
    snapshot.root.children.push_back(std::move(modal));

    gba::WidgetNode empty;
    empty.id = L"empty-container";
    empty.kind = L"stack";
    empty.inputScopeId = L"empty";
    snapshot.root.children.push_back(std::move(empty));
    return snapshot;
}

gba::WidgetSnapshot SessionList(std::initializer_list<const wchar_t*> ids) {
    gba::WidgetSnapshot snapshot;
    snapshot.instanceId = L"audio.runtime.v1";
    snapshot.activeInputScopeId = L"audio-root";
    snapshot.initialFocusId = L"master-mute";
    snapshot.root.id = L"audio-root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"master-mute"));
    gba::WidgetNode sessions;
    sessions.id = L"sessions";
    sessions.kind = L"scroll";
    sessions.scrollAxis = L"vertical";
    for (const auto* id : ids) sessions.children.push_back(Button(id));
    snapshot.root.children.push_back(std::move(sessions));
    return snapshot;
}

} // namespace

int main() {
    gba::input::WidgetSurfaceFocusMemory memory;
    auto root = Snapshot(L"root");
    Check(memory.Restore(L"widget", root) == L"root-first",
          "root initial focus is restored");
    memory.Remember(L"widget", root, L"root-second");
    Check(memory.Restore(L"widget", root) == L"root-second",
          "root remembered focus wins");

    auto modal = Snapshot(L"modal");
    // Root initial focus is outside this active surface, so tree-order fallback
    // selects the modal's first enabled button.
    Check(memory.Restore(L"widget", modal) == L"modal-first",
          "modal never inherits root focus");
    memory.Remember(L"widget", modal, L"modal-second");
    Check(memory.Restore(L"widget", modal) == L"modal-second",
          "modal focus is remembered independently");
    Check(memory.Restore(L"widget", root) == L"root-second",
          "returning to root restores root focus");

    memory.Forget(L"widget");
    Check(memory.Restore(L"widget", root) == L"root-first",
          "runtime replacement clears all remembered widget surfaces");
    Check(memory.Restore(L"widget", modal) == L"modal-first",
          "runtime replacement clears remembered modal focus");

    auto empty = Snapshot(L"empty");
    Check(memory.Restore(L"widget", empty).empty(),
          "focusless active surfaces remain valid");
    Check(gba::input::FindNodeInInputScope(modal, L"root-first", L"modal") == nullptr,
          "sibling and parent controls are inert");
    Check(gba::input::FindNodeInInputScope(modal, L"modal-first", L"modal") != nullptr,
          "active-surface controls are discoverable");

    modal.root.children[2].children[1].isDisabled = true;
    Check(memory.Restore(L"widget", modal) == L"modal-first",
          "disabled remembered focus falls back safely");

    auto sessions = SessionList({
        L"session.game.mute", L"session.chat.mute", L"session.music.mute",
        L"session.browser.mute", L"session.voice.mute", L"session.capture.mute",
    });
    memory.Remember(L"audio", sessions, L"session.browser.mute");
    Check(memory.Restore(L"audio", sessions) == L"session.browser.mute",
          "stable dynamic-list child ID survives close and reopen");
    auto churned = SessionList({
        L"session.game.mute", L"session.chat.mute", L"session.music.mute",
        L"session.voice.mute", L"session.capture.mute",
    });
    Check(memory.Restore(L"audio", churned) == L"session.voice.mute",
          "removed focused app falls to the nearest controller row by tree position");

    std::cout << "WidgetSurfaceFocusTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
