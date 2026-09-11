#include "DeclarativeRenderer.h"
#include "ControllerNavigation.h"
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

widgetrail::WidgetNode Button(const wchar_t* id, const bool disabled = false) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.isDisabled = disabled;
    return node;
}

widgetrail::WidgetNode Slider(const wchar_t* id, const bool disabled = false, const bool busy = false) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"slider";
    node.isDisabled = disabled;
    node.isBusy = busy;
    return node;
}

widgetrail::WidgetNode ActionSurface(
    const wchar_t* id,
    const bool disabled = false,
    const bool busy = false) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"actionSurface";
    node.actionId = L"open";
    node.isDisabled = disabled;
    node.isBusy = busy;
    widgetrail::WidgetNode title;
    title.id = std::wstring{id} + L".title";
    title.kind = L"text";
    title.text = L"Tile title";
    node.children.push_back(std::move(title));
    return node;
}

widgetrail::WidgetSnapshot Snapshot(const wchar_t* activeScope) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"test.instance";
    snapshot.activeInputScopeId = activeScope;
    snapshot.initialFocusId = L"root-first";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"root-first"));
    snapshot.root.children.push_back(Button(L"root-second"));

    widgetrail::WidgetNode modal;
    modal.id = L"modal-container";
    modal.kind = L"stack";
    modal.inputScopeId = L"modal";
    modal.children.push_back(Button(L"modal-first"));
    modal.children.push_back(Button(L"modal-second"));
    snapshot.root.children.push_back(std::move(modal));

    widgetrail::WidgetNode empty;
    empty.id = L"empty-container";
    empty.kind = L"stack";
    empty.inputScopeId = L"empty";
    snapshot.root.children.push_back(std::move(empty));
    return snapshot;
}

widgetrail::WidgetSnapshot WrappedRootSnapshot(
    std::initializer_list<const wchar_t*> wrappers,
    const wchar_t* activeScope = L"content.scope") {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"wrapped.runtime.v1";
    snapshot.activeInputScopeId = activeScope;
    snapshot.initialFocusId = L"content.action";

    widgetrail::WidgetNode content;
    content.id = L"content";
    content.kind = L"stack";
    content.inputScopeId = L"content.scope";
    content.children.push_back(Button(L"content.action"));

    for (auto wrapper = wrappers.end(); wrapper != wrappers.begin();) {
        --wrapper;
        widgetrail::WidgetNode presentation;
        presentation.id = std::wstring{L"presentation."} + *wrapper;
        presentation.kind = *wrapper;
        presentation.children.push_back(std::move(content));
        content = std::move(presentation);
    }
    snapshot.root = std::move(content);
    return snapshot;
}

widgetrail::WidgetSnapshot SessionList(std::initializer_list<const wchar_t*> ids) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.instanceId = L"audio.runtime.v1";
    snapshot.activeInputScopeId = L"audio-root";
    snapshot.initialFocusId = L"master-mute";
    snapshot.root.id = L"audio-root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"master-mute"));
    widgetrail::WidgetNode sessions;
    sessions.id = L"sessions";
    sessions.kind = L"scroll";
    sessions.scrollAxis = L"vertical";
    for (const auto* id : ids) sessions.children.push_back(Button(id));
    snapshot.root.children.push_back(std::move(sessions));
    return snapshot;
}

widgetrail::WidgetSnapshot EmbeddedMediaSnapshot(
    const wchar_t* activeScope = L"media.root",
    const long long sequence = 1) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"media.runtime.v1";
    snapshot.activeInputScopeId = activeScope;
    snapshot.initialFocusId = L"media.play";
    snapshot.root.id = L"media.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"media.root";

    widgetrail::WidgetNode viewport;
    viewport.id = L"media.viewport";
    viewport.kind = L"mediaViewport";
    snapshot.root.children.push_back(std::move(viewport));
    snapshot.root.children.push_back(Button(L"media.previous"));
    snapshot.root.children.push_back(Button(L"media.play"));
    snapshot.root.children.push_back(Button(L"media.seek-forward"));

    widgetrail::WidgetNode options;
    options.id = L"media.options";
    options.kind = L"stack";
    options.inputScopeId = L"media.options";
    options.children.push_back(Button(L"media.options.loop"));
    options.children.push_back(Button(L"media.options.mute"));
    snapshot.root.children.push_back(std::move(options));
    return snapshot;
}

widgetrail::WidgetSnapshot FocusGroupSnapshot(const long long sequence = 1) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"groups.runtime.v1";
    snapshot.activeInputScopeId = L"groups.root";
    snapshot.initialFocusId = L"groups.entry";
    snapshot.root.id = L"groups.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"groups.root";

    auto entry = Button(L"groups.entry");
    entry.focusDown = L"groups.outer";

    widgetrail::WidgetNode inner;
    inner.id = L"groups.inner";
    inner.kind = L"row";
    inner.initialChildFocusId = L"groups.inner.first";
    inner.children.push_back(Button(L"groups.inner.first"));
    inner.children.push_back(Button(L"groups.inner.second"));

    widgetrail::WidgetNode outer;
    outer.id = L"groups.outer";
    outer.kind = L"stack";
    outer.initialChildFocusId = L"groups.outer.first";
    outer.children.push_back(Button(L"groups.outer.first"));
    outer.children.push_back(std::move(inner));
    outer.children.push_back(Button(L"groups.outer.last"));

    widgetrail::WidgetNode modal;
    modal.id = L"groups.modal";
    modal.kind = L"stack";
    modal.inputScopeId = L"groups.modal";
    modal.initialChildFocusId = L"groups.modal.first";
    modal.children.push_back(Button(L"groups.modal.first"));
    modal.children.push_back(Button(L"groups.modal.second"));

    snapshot.root.children = {std::move(entry), std::move(outer), std::move(modal)};
    return snapshot;
}

widgetrail::RenderResult FocusGroupRender(
    std::initializer_list<const wchar_t*> visible) {
    widgetrail::RenderResult result;
    float y{};
    for (const auto* id : visible) {
        const widgetrail::declarative::Rect rect{0.0F, y, 160.0F, 32.0F};
        result.focusRects[id] = rect;
        result.navigationRects[id] = rect;
        result.navigationEnabled[id] = true;
        y += 40.0F;
    }
    return result;
}


void CursorRequestsAreExplicitAndOneShot() {
    using widgetrail::input::WidgetSurfaceFocusMemory;
    const auto window = [](int first, int count, int initial) {
        widgetrail::WidgetSnapshot s; s.instanceId=L"cursor"; s.activeInputScopeId=L"items";
        s.root.id=L"items"; s.root.kind=L"scroll"; s.root.scrollAxis=L"vertical";
        s.root.collectionStartIndex=first; s.root.collectionAnchorKey=L"key."+std::to_wstring(first);
        s.initialFocusId=L"item."+std::to_wstring(initial);
        for(int i=first;i<first+count;++i) {
            auto item=Button((L"item."+std::to_wstring(i)).c_str());
            item.collectionItemKey=L"key."+std::to_wstring(i); s.root.children.push_back(item);
        }
        return s;
    };
    WidgetSurfaceFocusMemory memory;
    auto before=window(0,64,0); memory.Remember(L"widget",before,L"item.60");
    auto after=window(0,128,64);
    Check(memory.Restore(L"widget",after)==L"item.60", "prefetch defaults never replace retained focus");
    after.root.collectionNavigation=widgetrail::WidgetNode::CollectionNavigationRequest{1,L"item.60",L"item.64"};
    Check(memory.Restore(L"widget",after)==L"item.64", "explicit page request can enter the next page");
    memory.Remember(L"widget",after,L"item.64");
    memory.Remember(L"widget",after,L"item.65");
    Check(memory.Restore(L"widget",after)==L"item.65", "consumed navigation does not replay on refresh");
    after.root.collectionNavigation=widgetrail::WidgetNode::CollectionNavigationRequest{2,L"item.60",L"item.64"};
    Check(memory.Restore(L"widget",after)==L"item.65", "late completion cannot override newer focus");
    memory.Remember(L"widget",before,L"item.20");
    auto evicted=window(64,64,64);
    Check(memory.Restore(L"widget",evicted)==L"item.64", "evicted focus recovers inside the same collection");
    before.root.collectionResetGeneration = 1;
    memory.Remember(L"widget", before, L"item.20");
    auto refreshed = window(0, 128, 20);
    refreshed.root.collectionResetGeneration = 5;
    refreshed.root.collectionGeneration = 6; // refresh and adjacent page coalesced
    Check(memory.Restore(L"widget", refreshed) == L"item.0", "refresh resets remembered item focus despite overlapping keys");
    memory.Remember(L"widget", refreshed, L"item.3");
    refreshed.root.collectionGeneration = 7;
    Check(memory.Restore(L"widget", refreshed) == L"item.3", "later pages do not replay collection reset");
    auto outer = refreshed;
    outer.root = Button(L"root"); outer.root.kind = L"stack"; outer.root.inputScopeId = L"items";
    outer.root.children = {Button(L"header"), refreshed.root};
    memory.Remember(L"widget", outer, L"header");
    outer.root.children[1].collectionResetGeneration = 8;
    Check(memory.Restore(L"widget", outer) == L"header", "collection reset preserves external control focus");
    widgetrail::input::WidgetFocusGroupMemory groups;
    outer.root.initialChildFocusId = L"header";
    outer.root.children[1].initialChildFocusId = L"item.20";
    groups.Remember(L"widget", outer, L"item.20");
    outer.root.children[1].collectionResetGeneration = 9;
    const auto render = FocusGroupRender({L"header", L"item.0", L"item.20"});
    Check(groups.Resolve(L"widget", outer, L"items", render) == L"item.0", "collection focus group discards its stale child on refresh");
    Check(groups.Resolve(L"widget", outer, L"root", render) == L"item.0", "outer group discards remembered child of refreshed collection");
    groups.Remember(L"widget", outer, L"header");
    outer.root.children[1].collectionResetGeneration = 10;
    Check(groups.Resolve(L"widget", outer, L"root", render) == L"header", "group focus outside refreshed collection remains remembered");


}

} // namespace

int main() {
    CursorRequestsAreExplicitAndOneShot();
    using widgetrail::input::ControllerActionContext;
    using widgetrail::input::ControllerActionRoute;
    using widgetrail::input::NavigationDirection;
    using widgetrail::input::RootInputScope;
    using widgetrail::input::RouteUnhandledControllerAction;
    using widgetrail::input::ShouldTransferFocusToTray;

    auto directRoot = Snapshot(L"root");
    Check(RootInputScope(directRoot) == L"root",
          "an ordinary direct root retains its exact input scope");

    for (auto wrappers : {
             std::initializer_list<const wchar_t*>{L"backgroundSurface"},
             std::initializer_list<const wchar_t*>{L"focusPresentationSurface"},
             std::initializer_list<const wchar_t*>{
                 L"backgroundSurface", L"focusPresentationSurface"}}) {
        auto wrapped = WrappedRootSnapshot(wrappers);
        Check(RootInputScope(wrapped) == L"content.scope" &&
              RouteUnhandledControllerAction(
                  wrapped.activeInputScopeId == RootInputScope(wrapped)
                      ? ControllerActionContext::RootWidgetScope
                      : ControllerActionContext::NestedWidgetScope,
                  L"B") == ControllerActionRoute::HostBackToDashboard &&
              ShouldTransferFocusToTray(
                  NavigationDirection::Down,
                  wrapped.activeInputScopeId == RootInputScope(wrapped),
                  false,
                  false),
              "a presentation-only root preserves default B and final-Down exits");
    }

    auto responsiveWrapped = WrappedRootSnapshot({L"backgroundSurface"});
    responsiveWrapped.root.children.front().visibleWhen = L"compactOnly";
    Check(RootInputScope(responsiveWrapped) == L"content.scope",
          "responsive visibility does not change the semantic root scope");

    auto nestedWithoutBack = WrappedRootSnapshot({L"backgroundSurface"}, L"nested.scope");
    widgetrail::WidgetNode nestedScope;
    nestedScope.id = L"nested";
    nestedScope.kind = L"stack";
    nestedScope.inputScopeId = L"nested.scope";
    nestedScope.children.push_back(Button(L"nested.action"));
    nestedWithoutBack.root.children.front().children.push_back(std::move(nestedScope));
    Check(RootInputScope(nestedWithoutBack) == L"content.scope" &&
          RouteUnhandledControllerAction(
              ControllerActionContext::NestedWidgetScope,
              L"B") == ControllerActionRoute::None &&
          !ShouldTransferFocusToTray(
              NavigationDirection::Down,
              nestedWithoutBack.activeInputScopeId == RootInputScope(nestedWithoutBack),
              false,
              false),
          "an authored nested scope without B never falls through to host exits");

    auto malformedWrapper = WrappedRootSnapshot({L"backgroundSurface"});
    malformedWrapper.root.children.push_back(Button(L"unexpected.sibling"));
    Check(RootInputScope(malformedWrapper) == malformedWrapper.root.id,
          "a malformed presentation wrapper fails closed at its own scope");

    widgetrail::input::WidgetSurfaceFocusMemory memory;
    auto root = Snapshot(L"root");
    Check(memory.Restore(L"widget", root) == L"root-first",
          "root initial focus is restored");
    memory.Remember(L"widget", root, L"root-second");
    Check(memory.Restore(L"widget", root) == L"root-second",
          "root remembered focus wins");
    auto reopenedRoot = root;
    reopenedRoot.sequence++;
    Check(memory.Restore(L"widget", reopenedRoot) == L"root-second",
          "compatible same-session reopen restores the exact remembered control");
    auto changedInitial = reopenedRoot;
    changedInitial.root.children.insert(
        changedInitial.root.children.begin() + 2, Button(L"root-third"));
    changedInitial.initialFocusId = L"root-third";
    Check(memory.Restore(L"widget", changedInitial) == L"root-third",
          "a changed valid initial focus request wins on reopen");
    auto invalidInitial = reopenedRoot;
    invalidInitial.initialFocusId = L"missing-initial";
    Check(memory.Restore(L"widget", invalidInitial) == L"root-second",
          "an invalid changed initial focus cannot displace exact compatible memory");
    auto removedRemembered = reopenedRoot;
    removedRemembered.root.children.erase(
        removedRemembered.root.children.begin() + 1);
    Check(memory.Restore(L"widget", removedRemembered) == L"root-first",
          "a removed remembered control uses the bounded nearest ordinal fallback");

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

    auto media = EmbeddedMediaSnapshot();
    Check(memory.Restore(L"media-widget", media) == L"media.play",
          "fresh single-page media enters its declared default control");
    memory.Remember(L"media-widget", media, L"media.seek-forward");
    auto mediaReadmitted = EmbeddedMediaSnapshot(L"media.root", 2);
    Check(memory.Restore(L"media-widget", mediaReadmitted) == L"media.seek-forward",
          "fresh compatible media admission preserves the exact non-default control");
    auto mediaOptions = EmbeddedMediaSnapshot(L"media.options", 3);
    Check(memory.Restore(L"media-widget", mediaOptions) == L"media.options.loop",
          "a distinct media input scope starts from its own first control");
    memory.Remember(L"media-widget", mediaOptions, L"media.options.mute");
    Check(memory.Restore(L"media-widget", mediaReadmitted) == L"media.seek-forward",
          "media root memory is independent from its alternate input scope");
    Check(memory.Restore(L"media-widget", mediaOptions) == L"media.options.mute",
          "returning to an alternate media scope restores its exact memory");

    auto mediaControlRemoved = mediaReadmitted;
    mediaControlRemoved.root.children.erase(
        mediaControlRemoved.root.children.begin() + 3);
    Check(memory.Restore(L"media-widget", mediaControlRemoved) == L"media.play",
          "removed media memory falls back to the nearest valid controller control");

    widgetrail::input::WidgetFocusGroupMemory groups;
    auto groupSnapshot = FocusGroupSnapshot();
    const auto groupRender = FocusGroupRender({
        L"groups.entry", L"groups.outer.first", L"groups.inner.first",
        L"groups.inner.second", L"groups.outer.last"});
    Check(groups.Resolve(
              L"widget-a", groupSnapshot, L"groups.outer", groupRender) ==
              L"groups.outer.first",
          "fresh group entry uses its authored initial child");
    groups.Remember(L"widget-a", groupSnapshot, L"groups.inner.second");
    Check(groups.Resolve(
              L"widget-a", groupSnapshot, L"groups.inner", groupRender) ==
              L"groups.inner.second" &&
          groups.Resolve(
              L"widget-a", groupSnapshot, L"groups.outer", groupRender) ==
              L"groups.inner.second",
          "nested descendant updates inner and outer ancestor memories");
    groups.Remember(L"widget-a", groupSnapshot, L"groups.outer.last");
    Check(groups.Resolve(
              L"widget-a", groupSnapshot, L"groups.outer", groupRender) ==
              L"groups.outer.last" &&
          groups.Resolve(
              L"widget-a", groupSnapshot, L"groups.inner", groupRender) ==
              L"groups.inner.second",
          "nested groups resolve their independently retained descendants");
    Check(groups.Resolve(
              L"widget-b", groupSnapshot, L"groups.outer", groupRender) ==
              L"groups.outer.first",
          "group memory is isolated by widget");
    auto compatibleGroup = FocusGroupSnapshot(2);
    Check(groups.Resolve(
              L"widget-a", compatibleGroup, L"groups.outer", groupRender) ==
              L"groups.outer.last",
          "compatible successor and reopen retain exact group memory");
    const auto withoutRemembered = FocusGroupRender({
        L"groups.entry", L"groups.outer.first", L"groups.inner.first"});
    Check(groups.Resolve(
              L"widget-a", compatibleGroup, L"groups.outer", withoutRemembered) ==
              L"groups.outer.first",
          "disabled or hidden remembered child falls back to the initial child");
    auto removedGroupChild = compatibleGroup;
    removedGroupChild.root.children[1].children.pop_back();
    Check(groups.Resolve(
              L"widget-a", removedGroupChild, L"groups.outer", groupRender) ==
              L"groups.outer.first",
          "removed remembered child falls back without crossing group authority");
    auto modalGroups = FocusGroupSnapshot(3);
    modalGroups.activeInputScopeId = L"groups.modal";
    const auto modalRender = FocusGroupRender({
        L"groups.modal.first", L"groups.modal.second"});
    groups.Remember(L"widget-a", modalGroups, L"groups.modal.second");
    Check(groups.Resolve(
              L"widget-a", modalGroups, L"groups.modal", modalRender) ==
              L"groups.modal.second" &&
          groups.Resolve(
              L"widget-a", compatibleGroup, L"groups.outer", groupRender) ==
              L"groups.outer.last",
          "modal scope memory is isolated from the ordinary root scope");
    groups.Forget(L"widget-a");
    Check(groups.Resolve(
              L"widget-a", compatibleGroup, L"groups.outer", groupRender) ==
              L"groups.outer.first" &&
          groups.Resolve(
              L"widget-b", compatibleGroup, L"groups.outer", groupRender) ==
              L"groups.outer.first",
          "runtime or package retirement clears only the retired widget memory");

    memory.Forget(L"widget");
    Check(memory.Restore(L"widget", root) == L"root-first",
          "package or runtime retirement clears all remembered widget surfaces");
    Check(memory.Restore(L"widget", modal) == L"modal-first",
          "runtime replacement clears remembered modal focus");

    auto empty = Snapshot(L"empty");
    Check(memory.Restore(L"widget", empty).empty(),
          "focusless active surfaces remain valid");
    Check(widgetrail::input::FindNodeInInputScope(modal, L"root-first", L"modal") == nullptr,
          "sibling and parent controls are inert");
    Check(widgetrail::input::FindNodeInInputScope(modal, L"modal-first", L"modal") != nullptr,
          "active-surface controls are discoverable");

    memory.Remember(L"widget", modal, L"modal-second");
    modal.root.children[2].children[1].isDisabled = true;
    Check(memory.Restore(L"widget", modal) == L"modal-second",
          "disabled remembered button retains exact controller focus");
    modal.root.children[2].children[1].isBusy = true;
    Check(memory.Restore(L"widget", modal) == L"modal-second",
          "busy disabled button remains a stable navigation target");

    auto sliderSurface = Snapshot(L"root");
    sliderSurface.root.children.insert(
        sliderSurface.root.children.begin() + 1,
        Slider(L"volume", false, false));
    memory.Remember(L"slider-widget", sliderSurface, L"volume");
    sliderSurface.root.children[1].isBusy = true;
    Check(memory.Restore(L"slider-widget", sliderSurface) == L"volume",
          "busy slider retains exact focus across snapshots");
    sliderSurface.root.children[1].isDisabled = true;
    Check(memory.Restore(L"slider-widget", sliderSurface) == L"volume",
          "disabled slider remains navigable and retains exact focus");

    auto tileSurface = Snapshot(L"root");
    tileSurface.root.children.insert(
        tileSurface.root.children.begin(),
        ActionSurface(L"library.tile"));
    tileSurface.initialFocusId = L"library.tile";
    Check(memory.Restore(L"tile-widget", tileSurface) == L"library.tile",
          "ActionSurface participates in initial focus restoration");
    memory.Remember(L"tile-widget", tileSurface, L"library.tile");
    tileSurface.root.children[0].isDisabled = true;
    Check(memory.Restore(L"tile-widget", tileSurface) == L"library.tile",
          "disabled ActionSurface retains exact controller focus");
    tileSurface.root.children[0].isBusy = true;
    Check(memory.Restore(L"tile-widget", tileSurface) == L"library.tile",
          "busy disabled ActionSurface remains a stable navigation target");
    tileSurface.initialFocusId = L"library.tile.title";
    memory.Forget(L"tile-widget");
    Check(memory.Restore(L"tile-widget", tileSurface) == L"library.tile",
          "presentational ActionSurface descendants never become focus targets");

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

    auto previousPage = SessionList({
        L"session.page.12", L"session.page.13", L"session.page.14",
    });
    previousPage.initialFocusId = L"session.page.14";
    memory.Remember(L"paged", previousPage, L"session.page.12");
    auto replacementPage = SessionList({
        L"session.page.0", L"session.page.1", L"session.page.2",
    });
    replacementPage.initialFocusId = L"session.page.2";
    Check(memory.Restore(L"paged", replacementPage) == L"session.page.2",
          "a replacement page entering-edge request outranks stale ordinal memory");
    memory.Remember(L"paged", replacementPage, L"session.page.1");
    auto unrelatedRefresh = replacementPage;
    unrelatedRefresh.sequence++;
    Check(memory.Restore(L"paged", unrelatedRefresh) == L"session.page.1",
          "the consumed page-entry request cannot steal focus on an unrelated refresh");

    auto finalPage = SessionList({
        L"session.page.24", L"session.page.25", L"session.page.26",
        L"session.page.27", L"session.page.28",
    });
    finalPage.initialFocusId = L"session.page.24";
    memory.Remember(L"paged", unrelatedRefresh, L"session.page.2");
    Check(memory.Restore(L"paged", finalPage) == L"session.page.24",
          "a five-row final page focuses its entering edge instead of stale page ordinal");
    memory.Remember(L"paged", finalPage, L"session.page.24");
    auto cachedMiddlePage = SessionList({
        L"session.page.12", L"session.page.13", L"session.page.14",
        L"session.page.15", L"session.page.16", L"session.page.17",
        L"session.page.18", L"session.page.19", L"session.page.20",
        L"session.page.21", L"session.page.22", L"session.page.23",
    });
    cachedMiddlePage.initialFocusId = L"session.page.23";
    Check(memory.Restore(L"paged", cachedMiddlePage) == L"session.page.23",
          "reverse paging restores the cached page's leaving edge");

    std::cout << "WidgetSurfaceFocusTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
