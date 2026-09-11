#include "WidgetInteractionSession.h"
#include "WidgetSessionCoordinator.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
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

void Near(const double actual, const double expected, const std::string_view message) {
    Check(std::abs(actual - expected) < 1e-9, message);
}

widgetrail::WidgetNode Button(
    const wchar_t* id,
    const wchar_t* action = L"activate") {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.actionId = action;
    return node;
}

widgetrail::WidgetNode Slider(
    const wchar_t* id,
    const double value,
    const wchar_t* action) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"slider";
    node.actionId = L"toggle-mute";
    node.valueChangedActionId = action;
    node.hasSliderRange = true;
    node.minimum = 0.0;
    node.maximum = 1.0;
    node.value = value;
    node.step = 0.1;
    return node;
}

widgetrail::WidgetNode Select(const wchar_t* id) {
    const auto option = [](
        const wchar_t* optionId,
        const wchar_t* label,
        const wchar_t* actionId,
        const wchar_t* glyph,
        const wchar_t* accessibilityLabel,
        const bool selected) {
        widgetrail::WidgetSelectOption result;
        result.id = optionId;
        result.label = label;
        result.actionId = actionId;
        result.glyph = glyph;
        result.accessibilityLabel = accessibilityLabel;
        result.isSelected = selected;
        result.isDisabled = false;
        result.isBusy = false;
        return result;
    };
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.isSelect = true;
    node.selectOptions = {
        option(L"compact", L"Compact", L"density.compact", L"settings",
               L"Compact density", true),
        option(L"comfortable", L"Comfortable", L"density.comfortable",
               L"connection", L"Comfortable density", false),
        option(L"spacious", L"Spacious", L"density.spacious", L"warning",
               L"Spacious density", false),
    };
    return node;
}

widgetrail::WidgetSnapshot Snapshot(
    const long long sequence = 41,
    const wchar_t* instanceId = L"fixture.instance",
    const wchar_t* scope = L"root",
    const wchar_t* scrollAxis = L"vertical") {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = instanceId;
    snapshot.activeInputScopeId = scope;
    snapshot.initialFocusId = L"first";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.children.push_back(Button(L"first"));
    snapshot.root.children.push_back(Slider(L"volume-a", 0.4, L"volume-a.changed"));
    snapshot.root.children.push_back(Slider(L"volume-b", 0.6, L"volume-b.changed"));

    widgetrail::WidgetNode scroll;
    scroll.id = L"items.scroll";
    scroll.kind = L"scroll";
    scroll.scrollAxis = scrollAxis;
    scroll.children.push_back(Button(L"row.partial"));
    scroll.children.push_back(Button(L"row.first"));
    scroll.children.push_back(Button(L"row.second"));
    snapshot.root.children.push_back(std::move(scroll));

    widgetrail::WidgetNode dialog;
    dialog.id = L"dialog";
    dialog.kind = L"stack";
    dialog.inputScopeId = L"dialog";
    dialog.children.push_back(Button(L"dialog.confirm"));
    snapshot.root.children.push_back(std::move(dialog));
    return snapshot;
}

widgetrail::WidgetSnapshot PagedSnapshot(
    const int firstKey = 0,
    const long long sequence = 100,
    const wchar_t* scope = L"root") {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"paged.instance";
    snapshot.activeInputScopeId = scope;
    snapshot.initialFocusId = L"page.row.2";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";

    widgetrail::WidgetNode scroll;
    scroll.id = L"page.scroll";
    scroll.kind = L"scroll";
    scroll.scrollAxis = L"vertical";
    scroll.scrollNearStartActionId = L"page.before";
    scroll.scrollNearEndActionId = L"page.after";
    scroll.scrollPaginationThreshold = 1;
    scroll.collectionAnchorKey = L"key.2";
    for (int index = firstKey; index < firstKey + 5; ++index) {
        auto row = Button(
            (L"page.row." + std::to_wstring(index)).c_str(),
            L"page.activate");
        row.collectionItemKey = L"key." + std::to_wstring(index);
        scroll.children.push_back(std::move(row));
    }
    snapshot.root.children.push_back(std::move(scroll));
    return snapshot;
}

widgetrail::RenderResult PagedRender(
    const int firstKey,
    const float viewportY) {
    widgetrail::RenderResult result;
    result.scrollViewports.emplace(
        L"page.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {0.0F, viewportY, 200.0F, 80.0F}, viewportY, 140.0F});
    for (int local = 0; local < 5; ++local) {
        auto id = L"page.row." + std::to_wstring(firstKey + local);
        const widgetrail::declarative::Rect rect{
            0.0F, static_cast<float>(local * 44), 200.0F, 40.0F};
        result.focusScopes[id] = L"root";
        result.focusRects[id] = rect;
        result.navigationRects[id] = rect;
        result.navigationEnabled[id] = true;
        result.hitRegions.push_back({std::move(id), rect, true});
    }
    return result;
}

const widgetrail::WidgetNode& Node(
    const widgetrail::WidgetSnapshot& snapshot,
    const std::wstring_view id) {
    const auto* node = widgetrail::input::FindNodeInInputScope(
        snapshot, id, snapshot.activeInputScopeId);
    Check(node != nullptr, "fixture node exists in the active input scope");
    return *node;
}

widgetrail::input::WidgetInteractionAuthority Authority(
    const widgetrail::WidgetSnapshot& snapshot,
    const std::wstring_view widgetId = L"fixture.widget",
    const std::wstring_view runtime = L"runtime-7",
    const std::wstring_view presentation = L"presentation-9",
    const bool retainedRefresh = false) {
    return {widgetId, &snapshot, runtime, presentation, retainedRefresh};
}

void AddRenderTarget(
    widgetrail::RenderResult& result,
    std::wstring id,
    const widgetrail::declarative::Rect rect,
    const bool enabled = true) {
    result.focusScopes[id] = L"root";
    result.focusRects[id] = rect;
    result.navigationRects[id] = rect;
    result.navigationEnabled[id] = enabled;
    result.hitRegions.push_back({std::move(id), rect, enabled});
}

void AddNavigationTarget(
    widgetrail::RenderResult& result,
    std::wstring id,
    const widgetrail::declarative::Rect rect,
    const bool visible,
    const bool enabled = true) {
    result.focusScopes[id] = L"root";
    result.navigationRects[id] = rect;
    result.navigationEnabled[id] = enabled;
    if (visible) {
        result.focusRects[id] = rect;
        result.hitRegions.push_back({std::move(id), rect, enabled});
    } else {
        result.revealableFocusIds.insert(std::move(id));
    }
}

struct ResponsiveGridScrollFixture final {
    widgetrail::WidgetSnapshot snapshot;
    widgetrail::RenderResult render;
};

ResponsiveGridScrollFixture ResponsiveGridScroll(
    const std::size_t columns,
    const float pixelScale) {
    ResponsiveGridScrollFixture fixture;
    auto& snapshot = fixture.snapshot;
    snapshot.sequence = 165;
    snapshot.instanceId = L"directional-grid.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.initialFocusId = L"tile.0";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    snapshot.root.children.push_back(Button(L"search"));
    snapshot.root.children.push_back(Button(L"filter"));

    widgetrail::WidgetNode scroll;
    scroll.id = L"results.scroll";
    scroll.kind = L"scroll";
    scroll.scrollAxis = L"vertical";
    scroll.scrollNearEndActionId = L"results.next";
    scroll.scrollPaginationThreshold = 2;

    widgetrail::WidgetNode grid;
    grid.id = L"results.grid";
    grid.kind = L"grid";
    grid.gridMinimumColumnWidth = 80.0;
    grid.gridMaximumColumns = columns;
    for (std::size_t index = 0; index < 12; ++index) {
        const auto id = L"tile." + std::to_wstring(index);
        grid.children.push_back(Button(id.c_str()));
    }
    scroll.children.push_back(std::move(grid));
    scroll.children.push_back(Button(L"scroll.action"));
    snapshot.root.children.push_back(std::move(scroll));
    snapshot.root.children.push_back(Button(L"back"));
    snapshot.root.children.push_back(Button(L"refresh"));
    snapshot.root.children.push_back(Button(L"footer"));

    const float cellWidth = 80.0F * pixelScale;
    const float cellHeight = 40.0F * pixelScale;
    const float rowPitch = 50.0F * pixelScale;
    const float gridTop = 40.0F * pixelScale;
    const float gridWidth = static_cast<float>(columns) * cellWidth;
    auto& render = fixture.render;
    AddNavigationTarget(
        render, L"search", {0.0F, 0.0F, gridWidth, 24.0F * pixelScale}, true);
    AddNavigationTarget(
        render, L"filter", {0.0F, 28.0F * pixelScale, gridWidth,
                             8.0F * pixelScale}, true);
    for (std::size_t index = 0; index < 12; ++index) {
        const auto row = index / columns;
        const auto column = index % columns;
        const widgetrail::declarative::Rect rect{
            static_cast<float>(column) * cellWidth,
            gridTop + static_cast<float>(row) * rowPitch,
            cellWidth - 8.0F * pixelScale,
            cellHeight,
        };
        AddNavigationTarget(
            render, L"tile." + std::to_wstring(index), rect, row < 2);
    }
    AddNavigationTarget(
        render, L"scroll.action",
        {0.0F, 220.0F * pixelScale, gridWidth, cellHeight}, false);
    AddNavigationTarget(
        render, L"back",
        {0.0F, 132.0F * pixelScale, gridWidth, 30.0F * pixelScale}, true);
    AddNavigationTarget(
        render, L"refresh",
        {0.0F, 180.0F * pixelScale, gridWidth, 30.0F * pixelScale}, true);
    AddNavigationTarget(
        render, L"footer",
        {0.0F, 300.0F * pixelScale, gridWidth, 30.0F * pixelScale}, true);
    render.scrollViewports.emplace(
        L"results.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {0.0F, gridTop, gridWidth, 75.0F * pixelScale},
            0.0F,
            260.0F * pixelScale,
        });
    return fixture;
}

void FocusAndSurfaceLifecycle() {
    using widgetrail::input::WidgetInteractionSession;
    auto snapshot = Snapshot();
    WidgetInteractionSession session;

    Check(session.RestoreFocus(L"fixture.widget", snapshot) == L"first",
          "the session restores the admitted surface's initial focus");
    session.SetFocus(L"fixture.widget", snapshot, L"volume-a");
    session.RememberFocus(L"fixture.widget", snapshot);
    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", snapshot) == L"volume-a",
          "the session owns remembered widget-surface focus");

    const auto authority = Authority(snapshot);
    const auto adjustment = session.AdjustSlider(
        authority, Node(snapshot, L"volume-a"),
        widgetrail::input::NavigationDirection::Right, 100);
    Check(adjustment.consumed && adjustment.visualChanged &&
              !adjustment.actionRequest,
          "the focused slider creates an optimistic visual before navigation");
    Check(session.TransitionPressedPresentation(
              widgetrail::input::PressedInputTransition::Begin,
              &snapshot, L"volume-a", L"a"),
          "the session begins the exact focused pressed presentation");

    const auto moved = session.MoveFocus(
        L"fixture.widget", snapshot, L"first");
    Check(moved.changed && moved.priorFocus == L"volume-a" &&
              moved.focusedElementId == L"first",
          "focus mutation reports exact old and new focus identities");
    Check(moved.sliderDamageNodeIds == std::vector<std::wstring>{L"volume-a"},
          "focus mutation returns only the optimistic slider rollback damage");
    Check(moved.pressedPresentationChanged,
          "focus mutation retires the exact pressed presentation");

    session.ForgetWidget(L"fixture.widget");
    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", snapshot) == L"first",
          "widget retirement clears only that widget's remembered focus");

    auto dialog = Snapshot(42, L"fixture.instance", L"dialog");
    Check(session.RestoreFocus(L"fixture.widget", dialog) == L"dialog.confirm",
          "a nested input scope restores its own focus surface");
}

void FocusRestorationPrecedesInteractiveAdmission() {
    using namespace widgetrail;
    using namespace widgetrail::input;

    auto snapshot = Snapshot();
    WidgetInteractionSession session;
    session.SetFocus(L"fixture.widget", snapshot, L"volume-b");
    session.RememberFocus(L"fixture.widget", snapshot);

    const WidgetSessionPresentation visible{
        &snapshot,
        WidgetPresentationAuthority::Current,
        WidgetLifecycleState::Visible,
    };
    Check(visible.HasCommittedViewAuthority(WidgetCommittedViewUse::Presentation) &&
              !visible.HasCommittedViewAuthority(WidgetCommittedViewUse::Interaction),
          "a delayed Interactive lifecycle keeps the committed presentation eligible but denies input");

    session.ClearLiveFocus();
    Check(session.RestoreFocus(L"fixture.widget", *visible.snapshot) == L"volume-b",
          "the focus owner restores a non-first remembered target before input admission");

    auto initial = Snapshot(42, L"initial.instance");
    initial.initialFocusId = L"volume-b";
    WidgetInteractionSession initialSession;
    const WidgetSessionPresentation initialVisible{
        &initial,
        WidgetPresentationAuthority::Current,
        WidgetLifecycleState::Visible,
    };
    Check(!initialVisible.HasCommittedViewAuthority(WidgetCommittedViewUse::Interaction) &&
              initialSession.RestoreFocus(
                  L"initial.widget", *initialVisible.snapshot) == L"volume-b",
          "a non-first authored initial target is selected while delayed Interactive still denies input");

    auto invalidRemembered = snapshot;
    std::erase_if(
        invalidRemembered.root.children,
        [](const widgetrail::WidgetNode& node) {
            return node.id == L"volume-b";
        });
    session.ClearLiveFocus();
    Check(session.RestoreFocus(L"fixture.widget", invalidRemembered) == L"row.partial",
          "a removed remembered target uses the bounded nearest-valid fallback");

    const WidgetSessionPresentation interactive{
        &snapshot,
        WidgetPresentationAuthority::Current,
        WidgetLifecycleState::Interactive,
    };
    Check(interactive.HasCommittedViewAuthority(WidgetCommittedViewUse::Interaction),
          "the same committed snapshot admits input only after Interactive completes");
}

void HostFocusRestorationUsesPresentationAuthority() {
    const auto path = std::filesystem::path{__FILE__}.parent_path() / "main.cpp";
    std::ifstream input(path, std::ios::binary);
    Check(input.is_open(), "the host focus-restoration source is available");
    std::ostringstream buffer;
    buffer << input.rdbuf();
    const auto source = buffer.str();

    const auto helperBegin = source.find(
        "const widgetrail::WidgetSnapshot* FocusRestorationSnapshotFor(");
    const auto helperEnd = source.find(
        "const widgetrail::WidgetSnapshot* DashboardActionSnapshotFor(",
        helperBegin);
    Check(helperBegin != std::string::npos && helperEnd != std::string::npos &&
              helperBegin < helperEnd,
          "the host owns one bounded focus-restoration snapshot selector");
    const auto helper = source.substr(helperBegin, helperEnd - helperBegin);
    Check(helper.find("WidgetCommittedViewUse::Presentation") != std::string::npos &&
              helper.find("WidgetCommittedViewUse::Interaction") == std::string::npos,
          "focus restoration selects committed presentation authority without granting interaction");

    const auto rememberBegin = source.find(
        "void RememberCurrentFocus(const std::wstring_view widgetId)");
    const auto restoreBegin = source.find(
        "void RestoreFocusForActiveSurface(const std::wstring_view widgetId)",
        rememberBegin);
    const auto restoreEnd = source.find(
        "void ReturnPinnedControllerFocusToOverlay(", restoreBegin);
    Check(rememberBegin != std::string::npos && restoreBegin != std::string::npos &&
              restoreEnd != std::string::npos && rememberBegin < restoreBegin &&
              restoreBegin < restoreEnd,
          "the host focus-memory and restoration owners remain bounded");
    const auto remember = source.substr(rememberBegin, restoreBegin - rememberBegin);
    const auto restore = source.substr(restoreBegin, restoreEnd - restoreBegin);
    Check(remember.find("InteractionSnapshotFor(widgetId)") != std::string::npos &&
              remember.find("FocusRestorationSnapshotFor(widgetId)") == std::string::npos,
          "remembering focus still requires exact Interactive input authority");
    Check(restore.find("FocusRestorationSnapshotFor(widgetId)") != std::string::npos &&
              restore.find("InteractionSnapshotFor(widgetId)") == std::string::npos,
          "the active-surface restore path consumes only the presentation-authority selector");

    const auto transitionBegin = source.find(
        "template <typename Mutation>\n    void ApplyStateTransition(");
    const auto transitionEnd = source.find(
        "void ApplyPresentation(", transitionBegin);
    Check(transitionBegin != std::string::npos && transitionEnd != std::string::npos &&
              transitionBegin < transitionEnd,
          "the host state-transition owner is available");
    const auto transition = source.substr(
        transitionBegin, transitionEnd - transitionBegin);
    const auto sync = transition.find("SyncWidgetActivity(");
    const auto restoreCall = transition.find(
        "RestoreFocusForActiveSurface(state_.activeWidget())", sync);
    Check(sync != std::string::npos && restoreCall != std::string::npos &&
              sync < restoreCall,
          "widget entry starts asynchronous lifecycle admission before selecting first-frame focus");
}

void PinnedViewReturnUsesExistingFocusMemory() {
    using widgetrail::input::WidgetInteractionSession;
    auto root = Snapshot();
    WidgetInteractionSession session;

    session.SetFocus(L"fixture.widget", root, L"volume-b");
    session.RememberFocus(L"fixture.widget", root);

    auto dialog = Snapshot(42, L"fixture.instance", L"dialog");
    session.SetFocus(L"fixture.widget", dialog, L"dialog.confirm");
    session.RememberFocus(L"fixture.widget", dialog);

    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", root) == L"volume-b",
          "returning from pinned focus restores the exact remembered root-scope target");
    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", dialog) == L"dialog.confirm",
          "returning from pinned focus preserves independent input-scope memory");

    auto compatible = root;
    ++compatible.sequence;
    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", compatible) == L"volume-b",
          "a compatible successor retains the exact remembered widget target");

    auto removed = compatible;
    std::erase_if(removed.root.children, [](const widgetrail::WidgetNode& node) {
        return node.id == L"volume-b";
    });
    session.ClearFocus();
    Check(session.RestoreFocus(L"fixture.widget", removed) == L"row.partial",
          "a removed remembered target uses the existing nearest-valid ordinal fallback");
}

void ResponsiveFocusHandoffUsesInteractionOwner() {
    using namespace widgetrail::input;
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 77;
    snapshot.instanceId = L"responsive.instance";
    snapshot.activeInputScopeId = L"responsive.root";
    snapshot.root.id = L"responsive.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;

    widgetrail::WidgetNode compact;
    compact.id = L"responsive.compact";
    compact.kind = L"stack";
    compact.visibleWhen = L"compactOnly";
    auto compactQueue = Button(L"responsive.compact.queue", L"queue.open");
    compactQueue.focusPersistenceId = L"navigation.queue";
    compact.children = {compactQueue};

    widgetrail::WidgetNode expanded;
    expanded.id = L"responsive.expanded";
    expanded.kind = L"stack";
    expanded.visibleWhen = L"expandedOnly";
    auto expandedQueue = Button(L"responsive.expanded.queue", L"queue.open");
    expandedQueue.focusPersistenceId = L"navigation.queue";
    expanded.children = {expandedQueue};
    snapshot.root.children = {compact, expanded};

    WidgetInteractionSession session;
    session.SetFocus(
        L"responsive.widget", snapshot, L"responsive.expanded.queue");
    Check(!ResolveResponsiveFocusPersistenceTarget(
              snapshot, session.focusedElementId(),
              snapshot.activeInputScopeId, false) &&
          session.focusedElementId() == L"responsive.expanded.queue",
          "same responsive geometry leaves interaction focus untouched");

    const auto compactTarget = ResolveResponsiveFocusPersistenceTarget(
        snapshot, session.focusedElementId(),
        snapshot.activeInputScopeId, true);
    Check(compactTarget.has_value(),
          "committed compact transition resolves one exact alias target");
    const auto compactMove = session.MoveFocus(
        L"responsive.widget", snapshot, *compactTarget);
    Check(compactMove.changed &&
              compactMove.priorFocus == L"responsive.expanded.queue" &&
              compactMove.focusedElementId == L"responsive.compact.queue" &&
              session.focusedElementId() == L"responsive.compact.queue",
          "interaction session atomically owns the responsive focus handoff");
    Check(!ResolveResponsiveFocusPersistenceTarget(
              snapshot, session.focusedElementId(),
              snapshot.activeInputScopeId, true),
          "repeated same-mode refresh cannot apply the handoff twice");

    auto refreshed = snapshot;
    ++refreshed.sequence;
    Check(!ResolveResponsiveFocusPersistenceTarget(
              refreshed, session.focusedElementId(),
              refreshed.activeInputScopeId, true) &&
          session.focusedElementId() == L"responsive.compact.queue",
          "new snapshot authority with unchanged geometry preserves exact focus");
}

void RememberedGroupsUseExplicitAndGeometricEntryOwners() {
    using namespace widgetrail::input;
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 91;
    snapshot.instanceId = L"groups.instance";
    snapshot.activeInputScopeId = L"groups.root";
    snapshot.initialFocusId = L"groups.entry";
    snapshot.root.id = L"groups.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"groups.root";

    auto entry = Button(L"groups.entry");
    entry.focusDown = L"groups.controls";
    widgetrail::WidgetNode controls;
    controls.id = L"groups.controls";
    controls.kind = L"row";
    controls.initialChildFocusId = L"groups.play";
    controls.children.push_back(Button(L"groups.play"));
    controls.children.push_back(Button(L"groups.seek"));
    widgetrail::WidgetNode routes;
    routes.id = L"groups.routes";
    routes.kind = L"row";
    routes.initialChildFocusId = L"groups.link";
    routes.children.push_back(Button(L"groups.link"));
    routes.children.push_back(Button(L"groups.setup"));
    snapshot.root.children = {
        std::move(entry), std::move(controls), std::move(routes)};

    widgetrail::RenderResult render;
    AddRenderTarget(render, L"groups.entry", {0.0F, 0.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.play", {0.0F, 50.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.seek", {120.0F, 50.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.link", {0.0F, 100.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.setup", {120.0F, 100.0F, 100.0F, 30.0F});
    render.focusScopes[L"groups.entry"] = L"groups.root";
    render.focusScopes[L"groups.play"] = L"groups.root";
    render.focusScopes[L"groups.seek"] = L"groups.root";
    render.focusScopes[L"groups.link"] = L"groups.root";
    render.focusScopes[L"groups.setup"] = L"groups.root";

    WidgetInteractionSession fresh;
    fresh.SetFocus(L"groups.widget", snapshot, L"groups.entry");
    auto initial = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Down, render);
    Check(initial.disposition == DirectionalFocusDisposition::Explicit &&
              initial.target == L"groups.play",
          "explicit group entry resolves the authored initial descendant");

    snapshot.root.children.front().focusDown.clear();
    initial = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Down, render);
    Check(initial.disposition == DirectionalFocusDisposition::Geometric &&
              initial.target == L"groups.play",
          "external geometric entry treats the group as one composite and resolves its initial child");

    fresh.SetFocus(L"groups.widget", snapshot, L"groups.seek");
    fresh.SetFocus(L"groups.widget", snapshot, L"groups.entry");
    auto remembered = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Down, render);
    Check(remembered.disposition == DirectionalFocusDisposition::Geometric &&
              remembered.target == L"groups.seek",
          "geometric group entry restores the exact remembered child");

    fresh.SetFocus(L"groups.widget", snapshot, L"groups.play");
    const auto internal = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Right, render);
    Check(internal.disposition == DirectionalFocusDisposition::Geometric &&
              internal.target == L"groups.seek",
          "navigation inside a remembered group remains ordinary leaf geometry");

    const auto siblingInitial = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Down, render);
    Check(siblingInitial.disposition == DirectionalFocusDisposition::Geometric &&
              siblingInitial.target == L"groups.link",
          "current-group leaf navigation retains a sibling composite group and resolves its initial child");

    fresh.SetFocus(L"groups.widget", snapshot, L"groups.setup");
    fresh.SetFocus(L"groups.widget", snapshot, L"groups.seek");
    const auto siblingRemembered = fresh.ResolveDirectionalFocus(
        L"groups.widget", snapshot, NavigationDirection::Down, render);
    Check(siblingRemembered.disposition == DirectionalFocusDisposition::Geometric &&
              siblingRemembered.target == L"groups.setup",
          "sibling composite entry restores its deliberately remembered child");

    auto successor = snapshot;
    successor.sequence++;
    fresh.ClearFocus();
    fresh.SetFocus(L"groups.widget", successor, L"groups.entry");
    auto reopened = fresh.ResolveDirectionalFocus(
        L"groups.widget", successor, NavigationDirection::Down, render);
    Check(reopened.target == L"groups.seek",
          "compatible refresh and overlay reopen retain group memory");

    render.navigationEnabled[L"groups.seek"] = false;
    auto disabled = fresh.ResolveDirectionalFocus(
        L"groups.widget", successor, NavigationDirection::Down, render);
    Check(disabled.target == L"groups.play",
          "disabled remembered child uses the bounded initial fallback");

    WidgetInteractionSession ordinary;
    auto plain = Snapshot(92);
    widgetrail::RenderResult plainRender;
    AddRenderTarget(plainRender, L"first", {0.0F, 0.0F, 100.0F, 30.0F});
    AddRenderTarget(plainRender, L"volume-a", {120.0F, 0.0F, 100.0F, 30.0F});
    ordinary.SetFocus(L"plain.widget", plain, L"first");
    const auto geometric = ordinary.ResolveDirectionalFocus(
        L"plain.widget", plain, NavigationDirection::Right, plainRender);
    Check(geometric.disposition == DirectionalFocusDisposition::Geometric &&
              geometric.target == L"volume-a",
          "non-opted widgets retain unchanged geometric navigation");

    fresh.ForgetWidget(L"groups.widget");
    fresh.SetFocus(L"groups.widget", successor, L"groups.entry");
    Check(fresh.ResolveDirectionalFocus(
              L"groups.widget", successor, NavigationDirection::Down, render).target ==
              L"groups.play",
          "runtime or package retirement clears ordinary group memory");
}

void OneShotFocusGroupEntryUsesRuntimeHighWaterAuthority() {
    using namespace widgetrail::input;
    Check(ResolveFocusGroupEntryAdmission({true, true, false, false, false}) ==
              FocusGroupEntryAdmission::Active,
          "visible ordinary widget input remains eligible with an unrelated click-through pin");
    Check(ResolveFocusGroupEntryAdmission({false, true, false, false, true}) ==
              FocusGroupEntryAdmission::Dormant,
          "already-hidden same-widget context preserves an existing request dormant");
    Check(ResolveFocusGroupEntryAdmission({true, false, false, false, true}) ==
              FocusGroupEntryAdmission::Dormant,
          "logical Hidden preserves the same-widget request during visible close animation");
    Check(ResolveFocusGroupEntryAdmission({false, true, false, false, false}) ==
              FocusGroupEntryAdmission::Retire,
          "a late snapshot while hidden cannot queue focus entry");
    Check(ResolveFocusGroupEntryAdmission({true, false, false, false, false}) ==
              FocusGroupEntryAdmission::Retire &&
              ResolveFocusGroupEntryAdmission({true, true, true, false, false}) ==
                  FocusGroupEntryAdmission::Retire &&
              ResolveFocusGroupEntryAdmission({true, true, false, true, false}) ==
                  FocusGroupEntryAdmission::Retire,
          "tray modal and pinned-controller owners reject ordinary group entry");
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 101;
    snapshot.instanceId = L"entry.instance";
    snapshot.activeInputScopeId = L"entry.root";
    snapshot.root.id = L"entry.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"entry.root";
    widgetrail::WidgetNode group;
    group.id = L"entry.group";
    group.kind = L"row";
    group.initialChildFocusId = L"entry.first";
    group.children = {Button(L"entry.first"), Button(L"entry.remembered")};
    snapshot.root.children = {group};
    snapshot.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{17, L"entry.group"};

    widgetrail::RenderResult render;
    render.succeeded = true;
    AddRenderTarget(render, L"entry.first", {0.0F, 0.0F, 80.0F, 30.0F});
    AddRenderTarget(render, L"entry.remembered", {90.0F, 0.0F, 80.0F, 30.0F});
    render.focusScopes[L"entry.first"] = L"entry.root";
    render.focusScopes[L"entry.remembered"] = L"entry.root";
    WidgetInteractionSession session;
    session.SetFocus(L"entry.widget", snapshot, L"entry.remembered");
    auto authority = Authority(
        snapshot, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "fresh request becomes the one bounded pending group entry");

    auto successor = snapshot;
    successor.sequence++;
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "identical compatible request follows a newer sequence without replay");
    Check(session.FocusGroupEntryRequestPending(authority),
          "exact request authority identifies the pending preparation owner");
    const auto preview = session.PreviewFocusGroupEntryRequest(authority, render);
    Check(preview.current && preview.target == L"entry.remembered",
          "preparation resolves the remembered child without consuming the request");
    Check(session.FocusGroupEntryRequestPending(authority),
          "preparation cannot consume before a successful target-backed frame");
    const auto confirmed = session.PreviewFocusGroupEntryRequest(authority, render);
    Check(confirmed.current && confirmed.target == preview.target,
          "final preparation validates the same exact remembered target");
    const auto applied = session.CommitPreparedFocusGroupEntryRequest(
        authority, confirmed.target);
    Check(applied.consumed && applied.target == L"entry.remembered",
          "successful exact-current frame commits its already-rendered target once");
    Check(!session.CommitPreparedFocusGroupEntryRequest(authority, confirmed.target).consumed,
          "consumed request cannot apply twice");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "same runtime and instance reject the consumed request ID");

    successor.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{18, L"entry.group"};
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "a later entry request remains independently eligible");
    auto changedScope = successor;
    changedScope.activeInputScopeId = L"dialog";
    auto changedScopeAuthority = Authority(
        changedScope, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              changedScopeAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "scope replacement terminally retires pending entry");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "returning to the old scope cannot resurrect the consumed ID");

    successor.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{19, L"entry.group"};
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Retire) ==
              FocusGroupEntryObservation::Retired,
          "hidden tray pinned or modal admission terminally retires the request");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "an ineligible admission cannot steal focus on reopen");

    successor.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{20, L"entry.group"};
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "a later request arms under exact presentation authority");
    const auto replacedPresentation = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              replacedPresentation, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "presentation replacement terminally retires pending entry");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "presentation rollback cannot resurrect the consumed ID");

    successor.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{21, L"entry.group"};
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "a new request can follow presentation retirement");
    auto omitted = successor;
    omitted.focusGroupEntryRequest.reset();
    const auto omittedAuthority = Authority(
        omitted, L"entry.widget", L"runtime-a", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              omittedAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "request omission terminally retires pending entry");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "an omitted request ID cannot replay when reintroduced");

    successor.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{22, L"entry.group"};
    authority = Authority(
        successor, L"entry.widget", L"runtime-a", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "a new exact group target arms once");
    auto retargeted = successor;
    retargeted.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{22, L"entry.other"};
    const auto retargetedAuthority = Authority(
        retargeted, L"entry.widget", L"runtime-a", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              retargetedAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Retired,
          "same-ID retargeting terminally retires rather than redirects");

    auto restarted = successor;
    restarted.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{1, L"entry.group"};
    auto restartedAuthority = Authority(
        restarted, L"entry.widget", L"runtime-b", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              restartedAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "fresh runtime owns an independent request high-water mark");
    session.ForgetRuntime(L"entry.instance");
    Check(session.ObserveFocusGroupEntryRequest(
              restartedAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "runtime retirement removes its bounded high-water entry");
    session.ResetFocusGroupEntryRequests();
    Check(session.ObserveFocusGroupEntryRequest(
              restartedAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "Bridge-session replacement clears request tracking");

    auto failedPreparation = restarted;
    failedPreparation.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{2, L"entry.group"};
    const auto failedPreparationAuthority = Authority(
        failedPreparation, L"entry.widget", L"runtime-b", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              failedPreparationAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "a later request can enter renderer preparation");
    Check(session.RetireFocusGroupEntryRequest(failedPreparationAuthority) &&
              !session.FocusGroupEntryRequestPending(failedPreparationAuthority),
          "failed or unstable preparation retires without committing focus");
    Check(!session.RetireFocusGroupEntryRequest(failedPreparationAuthority),
          "retired preparation cannot be consumed or retired twice");

    auto noRequest = failedPreparation;
    noRequest.focusGroupEntryRequest.reset();
    const auto noRequestAuthority = Authority(
        noRequest, L"entry.widget", L"runtime-b", L"presentation-b");
    Check(session.ObserveFocusGroupEntryRequest(
              noRequestAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::None &&
              !session.FocusGroupEntryRequestPending(noRequestAuthority),
          "a snapshot without a request cannot invent preparation authority");
}

void DeferredFocusGroupEntryWaitsForReadyContent() {
    using namespace widgetrail::input;
    widgetrail::WidgetSnapshot ready;
    ready.sequence = 1;
    ready.instanceId = L"deferred.instance";
    ready.activeInputScopeId = L"deferred.root";
    ready.root.id = L"deferred.root";
    ready.root.kind = L"stack";
    ready.root.inputScopeId = L"deferred.root";
    auto header = Button(L"deferred.header");
    widgetrail::WidgetNode group;
    group.id = L"deferred.group";
    group.kind = L"row";
    group.initialChildFocusId = L"deferred.first";
    group.children = {Button(L"deferred.first"), Button(L"deferred.remembered")};
    ready.root.children = {header, group};

    WidgetInteractionSession session;
    session.SetFocus(L"deferred.widget", ready, L"deferred.remembered");

    auto loading = ready;
    loading.sequence = 2;
    loading.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{41, L"deferred.group"};
    loading.root.children[1].initialChildFocusId.clear();
    widgetrail::WidgetNode loadingText;
    loadingText.id = L"deferred.loading";
    loadingText.kind = L"text";
    loadingText.text = L"Loading";
    loading.root.children[1].children = {loadingText};
    auto loadingAuthority = Authority(
        loading, L"deferred.widget", L"runtime-a", L"presentation-a");
    widgetrail::RenderResult loadingRender;
    loadingRender.succeeded = true;
    AddRenderTarget(
        loadingRender, L"deferred.header", {0.0F, 0.0F, 80.0F, 30.0F});
    loadingRender.focusScopes[L"deferred.header"] = L"deferred.root";
    Check(session.ObserveFocusGroupEntryRequest(
              loadingAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "deferred request is observed before content becomes focusable");
    const auto waiting = session.PreviewFocusGroupEntryRequest(
        loadingAuthority, loadingRender);
    Check(waiting.current && !waiting.target,
          "spinner-only group retains exact pending request without a target");
    Check(!session.CommitPreparedFocusGroupEntryRequest(
              loadingAuthority, waiting.target).consumed &&
              session.FocusGroupEntryRequestPending(loadingAuthority),
          "targetless preparation cannot consume deferred entry");
    Check(session.RestoreFocus(L"deferred.widget", loading).empty() &&
              session.focusedElementId().empty() &&
              session.FocusRestoreCandidate(
                  L"deferred.widget", loading).empty(),
          "targetless pending entry suppresses ordinary restore and its candidate");
    (void)session.MoveFocus(
        L"deferred.widget", loading, L"deferred.header");
    session.RememberFocus(L"deferred.widget", loading);
    session.ClearLiveFocus();
    Check(session.focusedElementId().empty() &&
              session.FocusGroupEntryRequestPending(loadingAuthority),
          "provisional live focus and explicit remember cannot retire pending entry");

    auto hiddenLoading = loading;
    hiddenLoading.sequence++;
    auto hiddenAuthority = Authority(
        hiddenLoading, L"deferred.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              hiddenAuthority, FocusGroupEntryAdmission::Dormant) ==
              FocusGroupEntryObservation::Dormant &&
              session.FocusGroupEntryRequestPending(hiddenAuthority),
          "temporary hidden same-widget context preserves pending entry dormant");

    auto successor = ready;
    successor.sequence = 4;
    successor.focusGroupEntryRequest = loading.focusGroupEntryRequest;
    auto successorAuthority = Authority(
        successor, L"deferred.widget", L"runtime-a", L"presentation-a");
    widgetrail::RenderResult readyRender;
    readyRender.succeeded = true;
    AddRenderTarget(
        readyRender, L"deferred.first", {0.0F, 40.0F, 80.0F, 30.0F});
    AddRenderTarget(
        readyRender, L"deferred.remembered", {90.0F, 40.0F, 80.0F, 30.0F});
    readyRender.focusScopes[L"deferred.first"] = L"deferred.root";
    readyRender.focusScopes[L"deferred.remembered"] = L"deferred.root";
    Check(session.ObserveFocusGroupEntryRequest(
              successorAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "compatible Ready snapshot retains the original pending request");
    const auto remembered = session.PreviewFocusGroupEntryRequest(
        successorAuthority, readyRender);
    Check(remembered.current && remembered.target == L"deferred.remembered",
          "automatic provisional focus did not overwrite remembered child memory");
    Check(session.CommitPreparedFocusGroupEntryRequest(
              successorAuthority, remembered.target).consumed,
          "Ready remembered child consumes deferred request once");
    Check(session.FocusRestoreCandidate(
              L"deferred.widget", successor) == L"deferred.remembered",
          "provisional move and remember calls cannot overwrite surface memory");

    WidgetInteractionSession hiddenFresh;
    Check(hiddenFresh.ObserveFocusGroupEntryRequest(
              hiddenAuthority, FocusGroupEntryAdmission::Dormant) ==
              FocusGroupEntryObservation::Retired &&
              hiddenFresh.ObserveFocusGroupEntryRequest(
                  successorAuthority, FocusGroupEntryAdmission::Active) ==
                  FocusGroupEntryObservation::Retired,
          "hidden context cannot admit a new request or replay it on reopen");

    WidgetInteractionSession fresh;
    Check(fresh.ObserveFocusGroupEntryRequest(
              successorAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending &&
              fresh.PreviewFocusGroupEntryRequest(
                  successorAuthority, readyRender).target == L"deferred.first",
          "first Ready visit falls back to the authored initial child");
    Check(fresh.RetireFocusGroupEntryRequest(successorAuthority),
          "deliberate user intent retires pending deferred entry");
    Check(!fresh.PreviewFocusGroupEntryRequest(
              successorAuthority, readyRender).current,
          "retired deferred request cannot steal focus on late Ready");

    WidgetInteractionSession directional;
    Check(directional.ObserveFocusGroupEntryRequest(
              loadingAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending &&
              directional.RestoreFocus(L"deferred.widget", loading).empty(),
          "explicit directional fixture begins in the targetless waiting state");
    const auto noTarget = directional.ResolveDirectionalFocus(
        L"deferred.widget", loading, NavigationDirection::Down, {});
    Check(noTarget.disposition ==
              DirectionalFocusDisposition::MissingVisibleFocus &&
              !noTarget.target &&
              directional.FocusGroupEntryRequestPending(loadingAuthority),
          "directional input with no visible target leaves pending entry intact");
    const auto visibleRecovery = directional.ResolveDirectionalFocus(
        L"deferred.widget", loading, NavigationDirection::Down, loadingRender);
    Check(visibleRecovery.disposition ==
              DirectionalFocusDisposition::VisibleRecovery &&
              visibleRecovery.target == L"deferred.header",
          "fresh directional input resolves one exact authored-order visible target");
    Check(directional.RetireFocusGroupEntryRequest(loadingAuthority),
          "exact visible recovery retires pending entry before focus commit");
    const auto moved = directional.MoveFocus(
        L"deferred.widget", loading, *visibleRecovery.target);
    Check(moved.changed && moved.priorFocus.empty() &&
              directional.focusedElementId() == L"deferred.header" &&
              !directional.FocusGroupEntryRequestPending(loadingAuthority),
          "visible recovery commits only its exact target without a second move");
}

void ProvisionalOrdinalRestorePreservesRequestedGroupMemory() {
    using namespace widgetrail::input;
    widgetrail::WidgetSnapshot page;
    page.sequence = 1;
    page.instanceId = L"ordinal-pages.instance";
    page.activeInputScopeId = L"ordinal-pages.root";
    page.initialFocusId = L"page-a.first";
    page.root.id = L"ordinal-pages.root";
    page.root.kind = L"stack";
    page.root.inputScopeId = page.activeInputScopeId;
    widgetrail::WidgetNode group;
    group.id = L"page-a.group";
    group.kind = L"stack";
    group.initialChildFocusId = L"page-a.first";
    group.children = {Button(L"page-a.first"), Button(L"page-a.remembered")};
    page.root.children = {group};

    widgetrail::RenderResult render;
    render.succeeded = true;
    AddRenderTarget(render, L"page-a.first", {0.0F, 0.0F, 160.0F, 30.0F});
    AddRenderTarget(render, L"page-a.remembered", {0.0F, 40.0F, 160.0F, 30.0F});
    render.focusScopes[L"page-a.first"] = page.activeInputScopeId;
    render.focusScopes[L"page-a.remembered"] = page.activeInputScopeId;

    auto utilities = page;
    utilities.sequence = 2;
    utilities.initialFocusId = L"utilities.only";
    utilities.root.children = {Button(L"utilities.only")};

    WidgetInteractionSession session;
    session.SetFocus(L"ordinal-pages.widget", page, L"page-a.remembered");
    session.SetFocus(L"ordinal-pages.widget", utilities, L"utilities.only");

    auto requestedReturn = page;
    requestedReturn.sequence = 3;
    requestedReturn.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{1, L"page-a.group"};
    auto authority = Authority(
        requestedReturn, L"ordinal-pages.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending,
          "returning page arms its exact focus-group request before admission restore");
    Check(session.RestoreFocus(L"ordinal-pages.widget", requestedReturn).empty() &&
              session.FocusRestoreCandidate(
                  L"ordinal-pages.widget", requestedReturn).empty(),
          "pending page entry waits focusless instead of applying ordinal restore");
    const auto remembered = session.PreviewFocusGroupEntryRequest(authority, render);
    Check(remembered.current && remembered.target == L"page-a.remembered",
          "focusless pending wait preserves the requested page's remembered non-first child");
    Check(session.CommitPreparedFocusGroupEntryRequest(authority, remembered.target).consumed,
          "the prepared remembered target commits once after the rendered frame");

    session.SetFocus(L"ordinal-pages.widget", requestedReturn, L"page-a.remembered");
    session.SetFocus(L"ordinal-pages.widget", utilities, L"utilities.only");
    auto ordinaryReturn = page;
    ordinaryReturn.sequence = 4;
    const auto ordinaryAuthority = Authority(
        ordinaryReturn, L"ordinal-pages.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              ordinaryAuthority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::None &&
              session.RestoreFocus(L"ordinal-pages.widget", ordinaryReturn) ==
                  L"page-a.first",
          "a return without a request keeps ordinary ordinal restore semantics");
    ordinaryReturn.sequence = 5;
    ordinaryReturn.focusGroupEntryRequest =
        widgetrail::FocusGroupEntryRequest{2, L"page-a.group"};
    authority = Authority(
        ordinaryReturn, L"ordinal-pages.widget", L"runtime-a", L"presentation-a");
    Check(session.ObserveFocusGroupEntryRequest(
              authority, FocusGroupEntryAdmission::Active) ==
              FocusGroupEntryObservation::Pending &&
              session.PreviewFocusGroupEntryRequest(authority, render).target ==
                  L"page-a.first",
          "ordinary no-request restore still records its resolved first-child focus");
}

void HorizontalRailsKeepDirectionalBoundaries() {
    using namespace widgetrail::input;
    for (const float scale : {0.85F, 1.0F, 1.05F, 1.5F, 2.0F}) {
        widgetrail::WidgetSnapshot snapshot;
        snapshot.sequence = 1;
        snapshot.instanceId = L"rail.instance";
        snapshot.activeInputScopeId = L"root";
        snapshot.initialFocusId = L"first";
        snapshot.root.id = L"root";
        snapshot.root.kind = L"stack";
        widgetrail::WidgetNode header;
        header.id = L"header";
        header.kind = L"row";
        header.initialChildFocusId = L"refresh";
        header.children = {Button(L"refresh")};
        widgetrail::WidgetNode rail;
        rail.id = L"rail";
        rail.kind = L"scroll";
        rail.scrollAxis = L"horizontal";
        rail.children = {Button(L"previous"), Button(L"first"), Button(L"next")};
        snapshot.root.children = {std::move(header), Button(L"side-action"), std::move(rail)};
        widgetrail::RenderResult render;
        const auto rect = [scale](float x, float y, float w, float h) {
            return widgetrail::declarative::Rect{x * scale, y * scale, w * scale, h * scale};
        };
        AddNavigationTarget(render, L"refresh", rect(100, 20, 96, 44), true);
        AddNavigationTarget(render, L"side-action", rect(20, 180, 44, 44), true);
        AddNavigationTarget(render, L"previous", rect(-64, 120, 150, 225), false);
        AddNavigationTarget(render, L"first", rect(100, 120, 150, 225), true);
        AddNavigationTarget(render, L"next", rect(264, 120, 150, 225), false);
        render.scrollViewports[L"rail"] = widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Horizontal, rect(90, 110, 180, 240), 0, 500};
        WidgetInteractionSession session;
        session.SetFocus(L"rail.widget", snapshot, L"first");
        const auto move = [&](NavigationDirection direction) {
            return session.ResolveDirectionalFocus(L"rail.widget", snapshot, direction, render);
        };
        auto result = move(NavigationDirection::Left);
        Check(result.target == L"previous" && !result.requiresScrollBoundaryAdmission,
            "existing rail-first search selects its offscreen previous item over a closer aligned external action");
        result = move(NavigationDirection::Right);
        Check(result.target == L"next" && !result.requiresScrollBoundaryAdmission,
            "existing rail-first search selects its offscreen next item");

        snapshot.root.children[2].children.erase(snapshot.root.children[2].children.begin());
        render.navigationRects.erase(L"previous");
        render.revealableFocusIds.erase(L"previous");
        render.navigationEnabled.erase(L"previous");
        result = move(NavigationDirection::Left);
        Check(result.target == L"side-action" && result.requiresScrollBoundaryAdmission,
            "an aligned external control remains reachable after the rail is exhausted");
        render.navigationEnabled[L"side-action"] = false;
        result = move(NavigationDirection::Left);
        Check(result.disposition == DirectionalFocusDisposition::Boundary && !result.target &&
            session.focusedElementId() == L"first",
            "first poster remains focused instead of entering the Refresh group above it");
        result = move(NavigationDirection::Up);
        Check(result.disposition == DirectionalFocusDisposition::Geometric && result.target == L"refresh",
            "Up can enter the header group through its remembered-child owner");
        snapshot.root.children[2].children[0].focusLeft = L"header";
        result = move(NavigationDirection::Left);
        Check(result.disposition == DirectionalFocusDisposition::Explicit && result.target == L"refresh",
            "explicit authored links retain priority over automatic row eligibility");
    }
}

void ResponsiveGridScrollOwnsDirectionalPriority() {
    using namespace widgetrail::input;
    for (const float pixelScale : {1.0F, 1.5F}) {
        for (const std::size_t columns : {1U, 2U, 4U, 5U}) {
            auto fixture = ResponsiveGridScroll(columns, pixelScale);
            auto& snapshot = fixture.snapshot;
            auto& render = fixture.render;
            WidgetInteractionSession session;
            const std::size_t currentIndex = columns * 2 - 1;
            const std::size_t nextRowIndex = std::min(
                columns * 3 - 1, std::size_t{11});
            const auto currentId = L"tile." + std::to_wstring(currentIndex);
            const auto nextRowId = L"tile." + std::to_wstring(nextRowIndex);
            session.SetFocus(L"directional.widget", snapshot, currentId);
            const auto down = session.ResolveDirectionalFocus(
                L"directional.widget", snapshot,
                NavigationDirection::Down, render);
            Check(down.disposition == DirectionalFocusDisposition::Geometric &&
                      down.target == nextRowId &&
                      !down.requiresScrollBoundaryAdmission &&
                      render.revealableFocusIds.contains(nextRowId),
                  "the nearest responsive grid wins over closer external geometry and reveals its offscreen row");

            session.SetFocus(L"directional.widget", snapshot, nextRowId);
            const std::size_t nextRowColumn = nextRowIndex % columns;
            const auto priorRowId = L"tile." + std::to_wstring(
                columns + nextRowColumn);
            const auto up = session.ResolveDirectionalFocus(
                L"directional.widget", snapshot,
                NavigationDirection::Up, render);
            Check(up.disposition == DirectionalFocusDisposition::Geometric &&
                      up.target == priorRowId &&
                      !up.requiresScrollBoundaryAdmission,
                  "upward movement mirrors the same grid-owned geometry at every supported width and scale");
        }
    }

    auto fixture = ResponsiveGridScroll(4, 1.0F);
    auto& snapshot = fixture.snapshot;
    auto& render = fixture.render;
    WidgetInteractionSession session;
    auto& grid = snapshot.root.children[2].children[0];
    grid.children[7].focusDown = L"back";
    session.SetFocus(L"directional.widget", snapshot, L"tile.7");
    auto resolution = session.ResolveDirectionalFocus(
        L"directional.widget", snapshot, NavigationDirection::Down, render);
    Check(resolution.disposition == DirectionalFocusDisposition::Explicit &&
              resolution.target == L"back" &&
              !resolution.requiresScrollBoundaryAdmission,
          "an explicit authored target remains ahead of grid, scroll, and pagination ownership");
    grid.children[7].focusDown.clear();

    session.SetFocus(L"directional.widget", snapshot, L"tile.11");
    resolution = session.ResolveDirectionalFocus(
        L"directional.widget", snapshot, NavigationDirection::Down, render);
    Check(resolution.disposition == DirectionalFocusDisposition::Geometric &&
              resolution.target == L"scroll.action" &&
              !resolution.requiresScrollBoundaryAdmission,
          "after the nearest grid is exhausted the containing Scroll still wins over closer external geometry");

    session.SetFocus(L"directional.widget", snapshot, L"scroll.action");
    resolution = session.ResolveDirectionalFocus(
        L"directional.widget", snapshot, NavigationDirection::Down, render);
    Check(resolution.disposition == DirectionalFocusDisposition::Geometric &&
              resolution.target == L"footer" &&
              resolution.requiresScrollBoundaryAdmission,
          "a surface-wide target outside the owning Scroll requires boundary admission before focus can leave");
    const auto authority = Authority(snapshot, L"directional.widget");
    const auto paged = session.ObserveScrollPaginationBoundaryIntent(
        authority, render, L"scroll.action", NavigationDirection::Down,
        ScrollPaginationIntentSource::DirectionalNavigation, 1650);
    Check(paged.retainFocus && paged.pagination.dispatchReady &&
              session.focusedElementId() == L"scroll.action",
          "an unloaded Scroll edge retains focus and dispatches pagination before the proposed external target");

    auto finite = snapshot;
    auto& finiteScroll = finite.root.children[2];
    finiteScroll.scrollNearEndActionId.clear();
    finiteScroll.scrollPaginationThreshold = 0;
    WidgetInteractionSession finiteSession;
    finiteSession.SetFocus(L"directional.widget", finite, L"scroll.action");
    const auto finiteExit = finiteSession.ResolveDirectionalFocus(
        L"directional.widget", finite, NavigationDirection::Down, render);
    const auto unpaged = finiteSession.ObserveScrollPaginationBoundaryIntent(
        Authority(finite, L"directional.widget"), render, L"scroll.action",
        NavigationDirection::Down,
        ScrollPaginationIntentSource::DirectionalNavigation, 1660);
    Check(finiteExit.target == L"footer" &&
              finiteExit.requiresScrollBoundaryAdmission &&
              !unpaged.retainFocus && !unpaged.pagination.dispatchReady,
          "a true logical boundary without page authority may leave the Scroll after bounded admission");

    auto staleRender = render;
    staleRender.scrollViewports.clear();
    WidgetInteractionSession staleSession;
    staleSession.SetFocus(L"directional.widget", snapshot, L"scroll.action");
    const auto stale = staleSession.ResolveDirectionalFocus(
        L"directional.widget", snapshot, NavigationDirection::Down,
        staleRender);
    Check(!stale.target &&
              stale.disposition ==
                  DirectionalFocusDisposition::BlockedAuthority,
          "stale Scroll geometry fails closed instead of exposing an external focus target");
}

void FreeScrollAndRetainedRefreshLifecycle() {
    using namespace widgetrail::input;
    auto snapshot = Snapshot();
    WidgetInteractionSession session;
    session.SetFocus(L"fixture.widget", snapshot, L"row.second");
    const auto current = Authority(snapshot);
    Check(session.BindFreeScroll(
              current, L"items.scroll", widgetrail::declarative::ScrollAxis::Vertical),
          "the exact admitted authority binds free scroll");
    auto decision = session.EvaluateFreeScrollAuthority(current);
    Check(decision.disposition == FreeScrollAuthorityDisposition::Current &&
              decision.followSuppressed,
          "current free-scroll authority suppresses focused-descendant follow");

    const auto retained = Authority(
        snapshot, L"fixture.widget", L"runtime-7", L"presentation-9", true);
    decision = session.EvaluateFreeScrollAuthority(retained);
    Check(decision.disposition == FreeScrollAuthorityDisposition::Retained &&
              decision.followSuppressed,
          "an exact retained refresh preserves only free-scroll follow suppression");
    Check(session.SetRefreshDeferred(true),
          "retained refresh records one pending re-entry deferral");
    Check(!session.SetRefreshDeferred(true),
          "duplicate retained refresh cannot create another transition");
    const auto retainedPresentation = session.Presentation(retained);
    Check(retainedPresentation.focusedElementId == L"row.second" &&
              retainedPresentation.suppressFocusedDescendantFollow,
          "retained refresh keeps semantic focus stationary without reacquiring it");

    auto mismatched = current;
    mismatched.widgetId = L"other.widget";
    Check(session.EvaluateFreeScrollAuthority(mismatched).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "widget replacement invalidates pending free scroll");
    mismatched = current;
    mismatched.runtimeGeneration = L"runtime-8";
    Check(session.EvaluateFreeScrollAuthority(mismatched).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "runtime replacement invalidates pending free scroll");
    mismatched = current;
    mismatched.presentationGeneration = L"presentation-10";
    Check(session.EvaluateFreeScrollAuthority(mismatched).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "presentation replacement invalidates pending free scroll");

    auto instanceReplacement = snapshot;
    instanceReplacement.instanceId = L"fixture.restarted";
    Check(session.EvaluateFreeScrollAuthority(Authority(instanceReplacement)).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "instance replacement invalidates pending free scroll");
    auto scopeReplacement = snapshot;
    scopeReplacement.activeInputScopeId = L"dialog";
    Check(session.EvaluateFreeScrollAuthority(Authority(scopeReplacement)).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "scope replacement invalidates pending free scroll");

    session.SetFocus(L"fixture.widget", snapshot, L"first");
    Check(session.EvaluateFreeScrollAuthority(current).disposition ==
              FreeScrollAuthorityDisposition::Replaced,
          "focus replacement invalidates pending free scroll");
    session.SetFocus(L"fixture.widget", snapshot, L"row.second");

    widgetrail::RenderResult render;
    render.scrollViewports.emplace(
        L"items.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {10.0F, 20.0F, 200.0F, 120.0F}, 80.0F, 300.0F});
    AddRenderTarget(render, L"row.partial", {10.0F, 8.0F, 200.0F, 44.0F});
    AddRenderTarget(render, L"row.first", {10.0F, 24.0F, 200.0F, 44.0F});
    AddRenderTarget(render, L"row.second", {10.0F, 72.0F, 200.0F, 44.0F});
    const auto reentry = session.ResolveFreeScrollReentry(current, render);
    Check(reentry.disposition ==
              FreeScrollReentryDisposition::ResumeDirectionalInput &&
              !reentry.target &&
              reentry.retiredBinding &&
              reentry.retiredBinding->focusedElementId == L"row.second",
          "visible vertical focus retires free scroll without consuming directional input");
    const auto verticalNavigation = session.ResolveDirectionalFocus(
        L"fixture.widget", snapshot, NavigationDirection::Up, render);
    Check(verticalNavigation.target == L"row.first",
          "the same vertical press continues through ordinary focus navigation");
    Check(session.ResolveFreeScrollReentry(current, render).disposition ==
              FreeScrollReentryDisposition::None,
          "free-scroll re-entry retires exactly once");

    WidgetInteractionSession horizontalSession;
    auto horizontalSnapshot = Snapshot(
        41, L"fixture.instance", L"root", L"horizontal");
    horizontalSession.SetFocus(
        L"fixture.widget", horizontalSnapshot, L"row.partial");
    const auto horizontalAuthority = Authority(horizontalSnapshot);
    Check(horizontalSession.BindFreeScroll(
              horizontalAuthority, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Horizontal),
          "horizontal free scroll binds the exact focused descendant");
    widgetrail::RenderResult horizontalRender;
    horizontalRender.scrollViewports.emplace(
        L"items.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Horizontal,
            {20.0F, 10.0F, 120.0F, 100.0F}, 60.0F, 240.0F});
    AddRenderTarget(
        horizontalRender, L"row.partial", {8.0F, 10.0F, 44.0F, 100.0F});
    AddRenderTarget(
        horizontalRender, L"row.first", {24.0F, 10.0F, 44.0F, 100.0F});
    AddRenderTarget(
        horizontalRender, L"row.second", {72.0F, 10.0F, 44.0F, 100.0F});
    horizontalRender.focusRects[L"row.partial"] = {
        20.0F, 10.0F, 32.0F, 100.0F};
    const auto horizontalReentry = horizontalSession.ResolveFreeScrollReentry(
        horizontalAuthority, horizontalRender);
    Check(horizontalReentry.disposition ==
              FreeScrollReentryDisposition::ResumeDirectionalInput &&
              horizontalReentry.retiredBinding && !horizontalReentry.target,
          "partially visible horizontal focus resumes the same directional input");
    const auto horizontalNavigation = horizontalSession.ResolveDirectionalFocus(
        L"fixture.widget", horizontalSnapshot, NavigationDirection::Right,
        horizontalRender);
    Check(horizontalNavigation.target == L"row.first",
          "the same horizontal press continues through ordinary focus navigation");

    WidgetInteractionSession recoverySession;
    recoverySession.SetFocus(L"fixture.widget", snapshot, L"row.second");
    Check(recoverySession.BindFreeScroll(
              current, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "out-of-view recovery binds exact free-scroll authority");
    auto recoveryRender = render;
    recoveryRender.focusRects.erase(L"row.second");
    recoveryRender.navigationRects[L"row.second"] = {
        10.0F, 164.0F, 200.0F, 44.0F};
    for (auto& region : recoveryRender.hitRegions) {
        if (region.nodeId == L"row.second")
            region.rect = recoveryRender.navigationRects[L"row.second"];
    }
    const auto recovery = recoverySession.ResolveFreeScrollReentry(
        current, recoveryRender);
    Check(recovery.disposition ==
              FreeScrollReentryDisposition::RecoveryConsumed &&
              recovery.target == L"row.first" && recovery.retiredBinding,
          "offscreen focus consumes one press for fully-visible-first recovery");

    WidgetInteractionSession activationStateSession;
    auto activationStateSnapshot = snapshot;
    activationStateSnapshot.root.children[3].children[2].isDisabled = true;
    activationStateSnapshot.root.children[3].children[2].isBusy = true;
    activationStateSession.SetFocus(
        L"fixture.widget", activationStateSnapshot, L"row.second");
    const auto activationStateAuthority = Authority(activationStateSnapshot);
    Check(activationStateSession.BindFreeScroll(
              activationStateAuthority, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "disabled and busy activation state retains exact scroll authority");
    const auto activationState = activationStateSession.ResolveFreeScrollReentry(
        activationStateAuthority, render);
    Check(activationState.disposition ==
              FreeScrollReentryDisposition::ResumeDirectionalInput,
          "disabled and busy activation state does not remove navigable focus");
    Check(activationStateSession.ResolveDirectionalFocus(
              L"fixture.widget", activationStateSnapshot,
              NavigationDirection::Up, render).target == L"row.first",
          "the same directional input can move away from disabled or busy activation state");

    WidgetInteractionSession ineligibleSession;
    ineligibleSession.SetFocus(L"fixture.widget", snapshot, L"row.second");
    Check(ineligibleSession.BindFreeScroll(
              current, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "navigation-ineligible recovery binds the prior exact scroll authority");
    auto ineligibleRender = render;
    ineligibleRender.navigationEnabled[L"row.second"] = false;
    for (auto& region : ineligibleRender.hitRegions) {
        if (region.nodeId == L"row.second") region.enabled = false;
    }
    const auto ineligible = ineligibleSession.ResolveFreeScrollReentry(
        current, ineligibleRender);
    Check(ineligible.disposition ==
              FreeScrollReentryDisposition::RecoveryConsumed &&
              ineligible.target == L"row.first",
          "renderer-ineligible current focus uses deterministic recovery");

    WidgetInteractionSession missingSession;
    missingSession.SetFocus(L"fixture.widget", snapshot, L"row.missing");
    Check(missingSession.BindFreeScroll(
              current, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "missing-focus recovery retains the exact prior scroll authority");
    const auto missing = missingSession.ResolveFreeScrollReentry(current, render);
    Check(missing.disposition ==
              FreeScrollReentryDisposition::RecoveryConsumed &&
              missing.target == L"row.first",
          "missing current focus uses deterministic recovery");

    auto nestedSnapshot = snapshot;
    auto& outerScroll = nestedSnapshot.root.children[3];
    widgetrail::WidgetNode innerScroll;
    innerScroll.id = L"items.inner-scroll";
    innerScroll.kind = L"scroll";
    innerScroll.scrollAxis = L"vertical";
    innerScroll.children.push_back(std::move(outerScroll.children.back()));
    outerScroll.children.pop_back();
    outerScroll.children.push_back(std::move(innerScroll));
    auto nestedRender = render;
    nestedRender.scrollViewports.emplace(
        L"items.inner-scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {10.0F, 20.0F, 200.0F, 120.0F}, 80.0F, 300.0F});
    WidgetInteractionSession outerOwnerSession;
    outerOwnerSession.SetFocus(
        L"fixture.widget", nestedSnapshot, L"row.second");
    const auto nestedAuthority = Authority(nestedSnapshot);
    Check(outerOwnerSession.BindFreeScroll(
              nestedAuthority, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "nested fixture can expose a stale outer-scroll binding");
    const auto outerOwner = outerOwnerSession.ResolveFreeScrollReentry(
        nestedAuthority, nestedRender);
    Check(outerOwner.disposition ==
              FreeScrollReentryDisposition::RecoveryConsumed,
          "visible focus cannot resume through a different nested scroll owner");

    WidgetInteractionSession innerOwnerSession;
    innerOwnerSession.SetFocus(
        L"fixture.widget", nestedSnapshot, L"row.second");
    Check(innerOwnerSession.BindFreeScroll(
              nestedAuthority, L"items.inner-scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "nested fixture binds the deepest exact scroll owner");
    const auto innerOwner = innerOwnerSession.ResolveFreeScrollReentry(
        nestedAuthority, nestedRender);
    Check(innerOwner.disposition ==
              FreeScrollReentryDisposition::ResumeDirectionalInput &&
              !innerOwner.target,
          "visible focus resumes only through its deepest exact scroll owner");

    WidgetInteractionSession scopeSession;
    scopeSession.SetFocus(L"fixture.widget", snapshot, L"row.second");
    Check(scopeSession.BindFreeScroll(
              current, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "scope replacement starts from exact bound authority");
    auto changedScope = snapshot;
    changedScope.activeInputScopeId = L"dialog";
    const auto changedScopeReentry =
        SurfaceInteractionTransactions::ResolveFreeScrollReentry(
            scopeSession.freeScrollState(), Authority(changedScope),
            scopeSession.focusedElementId(), render);
    Check(changedScopeReentry.disposition ==
              FreeScrollReentryDisposition::None &&
              !changedScopeReentry.retiredBinding &&
              !scopeSession.freeScrollBinding(),
          "scope replacement clears stale free scroll without re-entry authority");

    WidgetInteractionSession staleGeometrySession;
    staleGeometrySession.SetFocus(L"fixture.widget", snapshot, L"row.second");
    Check(staleGeometrySession.BindFreeScroll(
              current, L"items.scroll",
              widgetrail::declarative::ScrollAxis::Vertical),
          "stale-geometry recovery starts from exact bound authority");
    auto staleGeometry = render;
    staleGeometry.scrollViewports.clear();
    const auto staleReentry =
        SurfaceInteractionTransactions::ResolveFreeScrollReentry(
            staleGeometrySession.freeScrollState(), current,
            staleGeometrySession.focusedElementId(), staleGeometry);
    Check(staleReentry.disposition == FreeScrollReentryDisposition::None &&
              !staleReentry.retiredBinding &&
              !staleGeometrySession.freeScrollBinding(),
          "missing scroll geometry clears stale authority without consuming input");

    Check(session.BindFreeScroll(
              current, L"items.scroll", widgetrail::declarative::ScrollAxis::Vertical),
          "free scroll can bind again after re-entry");
    const auto cleared = session.ClearFreeScroll();
    Check(cleared && cleared->scrollId == L"items.scroll" &&
              !session.freeScrollBinding(),
          "hide, pointer, UIA, resize, failure, or restart can retire one exact binding");
    Check(session.EvaluateFreeScrollAuthority({}).disposition ==
              FreeScrollAuthorityDisposition::Missing,
          "inert semantics never authorize free-scroll movement or re-entry");
}

void ExactSliderRequestAuthorityAndRollback() {
    using namespace widgetrail::input;
    auto snapshot = Snapshot();
    WidgetInteractionSession session;
    session.SetFocus(L"fixture.widget", snapshot, L"volume-a");
    const auto authority = Authority(snapshot);

    const auto physical = session.AdjustSlider(
        authority, Node(snapshot, L"volume-a"), NavigationDirection::Right, 1'000);
    Check(physical.consumed && physical.visualChanged && !physical.actionRequest,
          "physical controller adjustment creates one pending optimistic preview");
    const auto dispatched = session.TakePendingSliderAction(
        authority, Node(snapshot, L"volume-a"), 1'000, true);
    Check(dispatched.consumed && dispatched.actionRequest,
          "an explicit final flush captures one exact physical request");
    const auto& request = *dispatched.actionRequest;
    Check(request.widgetId == L"fixture.widget" &&
              request.widgetInstanceId == L"fixture.instance" &&
              request.runtimeGeneration == L"runtime-7" &&
              request.presentationGeneration == L"presentation-9" &&
              request.inputScopeId == L"root" &&
              request.sourceElementId == L"volume-a" &&
              request.actionId == L"volume-a.changed" &&
              request.snapshotSequence == 41 && request.requestedValue &&
              request.sliderIntentGeneration != 0,
          "physical request captures every dispatch authority token");
    Near(*request.requestedValue, 0.5,
         "physical request carries the latest optimistic target");

    const auto accessibility = session.RequestSliderValue(
        authority, Node(snapshot, L"volume-b"), 0.25, 1'010);
    Check(accessibility.consumed && accessibility.visualChanged &&
              accessibility.actionRequest,
          "accessibility RangeValue uses the same exact request owner");
    const auto& accessibilityRequest = *accessibility.actionRequest;
    Check(accessibilityRequest.widgetId == request.widgetId &&
              accessibilityRequest.widgetInstanceId == request.widgetInstanceId &&
              accessibilityRequest.runtimeGeneration == request.runtimeGeneration &&
              accessibilityRequest.presentationGeneration ==
                  request.presentationGeneration &&
              accessibilityRequest.inputScopeId == request.inputScopeId &&
              accessibilityRequest.sourceElementId == L"volume-b" &&
              accessibilityRequest.actionId == L"volume-b.changed" &&
              accessibilityRequest.snapshotSequence == request.snapshotSequence &&
              accessibilityRequest.requestedValue == 0.25,
          "accessibility request captures the same complete authority tuple");

    session.SetFocus(L"fixture.widget", snapshot, L"volume-b");
    Check(request.sourceElementId == L"volume-a" &&
              request.actionId == L"volume-a.changed",
          "a captured request cannot be re-resolved from newer mutable focus");

    auto wrongIntent = request;
    ++wrongIntent.sliderIntentGeneration;
    Check(!session.CancelSliderAction(wrongIntent, 1'020).consumed,
          "wrong intent generation cannot retire a dispatched request");
    auto staleFallback = request;
    staleFallback.sliderIntentGeneration = 0;
    ++staleFallback.snapshotSequence;
    Check(!session.CancelSliderAction(staleFallback, 1'020).consumed,
          "generation-zero cancellation retains exact snapshot fallback authority");
    auto wrongAction = request;
    wrongAction.actionId = L"volume-b.changed";
    Check(!session.CancelSliderAction(wrongAction, 1'021).consumed,
          "wrong action identity cannot retire an optimistic request");
    auto wrongNode = request;
    wrongNode.sourceElementId = L"volume-b";
    Check(!session.CancelSliderAction(wrongNode, 1'022).consumed,
          "wrong node identity cannot retire an optimistic request");

    auto presentation = session.PrepareRenderPresentation(
        snapshot, L"volume-b", true, true);
    Check(presentation.sliderValueOverrides.contains(L"volume-a") &&
              presentation.sliderValueOverrides.contains(L"volume-b"),
          "failed stale rollback attempts leave both exact optimistic requests intact");
    const auto rolledBack = session.CancelSliderAction(request, 1'024);
    Check(rolledBack.consumed && rolledBack.visualChanged,
          "exact dispatch rejection retires its optimistic request");
    presentation = session.PrepareRenderPresentation(
        snapshot, L"volume-b", true, true);
    Check(!presentation.sliderValueOverrides.contains(L"volume-a") &&
              presentation.sliderValueOverrides.contains(L"volume-b"),
          "rollback retires only the exact optimistic request");

    auto copiedSnapshot = snapshot;
    const auto staleNode = session.AdjustSlider(
        authority, Node(copiedSnapshot, L"volume-a"),
        NavigationDirection::Right, 1'030);
    Check(!staleNode.consumed && !staleNode.actionRequest,
          "a node from a non-admitted snapshot cannot capture dispatch authority");

    auto replacement = snapshot;
    replacement.sequence = 42;
    replacement.root.children[2].value = 0.25;
    const auto reconciliation = session.ReconcileAdmission(
        replacement, L"volume-b", 1'040);
    Check(reconciliation.sliderDamageNodeIds.empty(),
          "an authoritative acknowledgement does not repaint an already optimistic value");

    const auto pendingAgain = session.RequestSliderValue(
        Authority(replacement), Node(replacement, L"volume-a"), 0.8, 2'000);
    Check(pendingAgain.actionRequest.has_value(),
          "a replacement snapshot can establish fresh exact slider authority");
    const auto replacementAuthority = Authority(replacement);
    const auto timedOut = session.Tick(
        &replacementAuthority, L"volume-b", 4'001);
    Check(timedOut.sliderDamageNodeIds == std::vector<std::wstring>{L"volume-a"},
          "timeout returns only the exact current slider rollback damage");
}

void PressedAndAdmissionReconciliation() {
    using namespace widgetrail::input;
    auto snapshot = Snapshot();
    WidgetInteractionSession session;
    session.SetFocus(L"fixture.widget", snapshot, L"first");
    Check(session.TransitionPressedPresentation(
              PressedInputTransition::Begin, &snapshot, L"first", L"a"),
          "physical A starts the exact pressed presentation");
    auto presentation = session.Presentation(Authority(snapshot));
    Check(presentation.pressedElementId == L"first",
          "current admitted authority exposes the pressed element");
    Check(!session.TransitionPressedPresentation(
              PressedInputTransition::End, nullptr, {}, L"rightBumper"),
          "unrelated release cannot clear the pressed presentation");
    Check(session.TransitionPressedPresentation(
              PressedInputTransition::End, nullptr, {}, L"a"),
          "matching release clears the pressed presentation");

    Check(session.TransitionPressedPresentation(
              PressedInputTransition::Begin, &snapshot, L"first", L"a"),
          "pressed presentation can begin before snapshot replacement");
    auto replacement = snapshot;
    replacement.sequence = 42;
    Check(session.ReconcilePressedPresentation(replacement),
          "snapshot replacement retires stale pressed authority");
    Check(session.Presentation(Authority(replacement)).pressedElementId.empty(),
          "replacement cannot inherit an older pressed visual");

    const auto retirement = session.RetirePresentations();
    Check(retirement.sliderRollbacks.empty() &&
              !retirement.pressedPresentationChanged,
          "idempotent lifecycle cleanup returns no unrelated damage");
}

void PaginationPrefetchLifecycle() {
    using namespace widgetrail::input;
    auto snapshot = PagedSnapshot();
    auto authority = Authority(snapshot, L"paged.widget");
    const auto middle = PagedRender(0, 44.0F);
    const auto trailing = PagedRender(0, 136.0F);

    WidgetInteractionSession session;
    session.SetFocus(L"paged.widget", snapshot, L"page.row.3");
    auto outcome = session.ReconcileScrollPagination(authority, trailing, 100);
    Check(outcome.dispatchReady && outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::Queued &&
              outcome.diagnostics.front().request.demandReason ==
                  ScrollPaginationDemandReason::Initial &&
              outcome.diagnostics.front().request.action.anchorKey == L"key.2",
          "one unambiguous initial viewport edge queues with retained-anchor authority");
    auto [request, acquire] = session.AcquireScrollPaginationDispatch(
        authority, trailing, 105);
    Check(request && acquire.diagnostics.empty(),
          "the queued viewport demand admits one adjacent action");
    const auto [duplicate, duplicateOutcome] =
        session.AcquireScrollPaginationDispatch(authority, trailing, 106);
    Check(!duplicate && duplicateOutcome.diagnostics.empty(),
          "an in-flight edge cannot dispatch twice");

    outcome = session.CompleteScrollPaginationDispatch({
        *request, ScrollPaginationDispatchDisposition::Admitted, {}, 106});
    Check(outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::Admitted &&
              outcome.diagnostics.front().adjacentActionCount == 1 &&
              outcome.diagnostics.front().queueLatencyMilliseconds == 5,
          "admission records one adjacent action and bounded queue latency");
    outcome = session.ReconcileScrollPagination(authority, trailing, 110);
    Check(!outcome.dispatchReady && outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::Suppressed &&
              outcome.diagnostics.front().reason == L"in-flight",
          "repeated retained geometry is deduplicated while the page is in flight");

    auto shiftedSnapshot = PagedSnapshot(1, 101);
    auto shiftedAuthority = Authority(shiftedSnapshot, L"paged.widget");
    const auto shiftedTrailing = PagedRender(1, 136.0F);
    outcome = session.ReconcileScrollPagination(
        shiftedAuthority, shiftedTrailing, 145);
    Check(!outcome.dispatchReady && outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::Completed &&
              outcome.diagnostics.front().reason ==
                  L"requested-edge-changed" &&
              outcome.diagnostics.front().adjacentActionCount == 1 &&
              outcome.diagnostics.front().visibleCompletionCount == 1 &&
              outcome.diagnostics.front().thresholdToVisibleMilliseconds == 45 &&
              outcome.diagnostics.front().averageVisibleLatencyMilliseconds == 45 &&
              session.focusedElementId() == L"page.row.3",
          "a visible page completes once with measured latency and retained focus/anchor");
    outcome = session.ReconcileScrollPagination(
        shiftedAuthority, shiftedTrailing, 146);
    Check(!outcome.dispatchReady,
          "page completion and retained-anchor reconciliation cannot mint another demand");

    const auto checkDirectIntent = [&](const ScrollPaginationIntentSource source,
                                       const char* message) {
        WidgetInteractionSession inputSession;
        auto inputSnapshot = PagedSnapshot();
        auto inputAuthority = Authority(inputSnapshot, L"paged.widget");
        Check(!inputSession.ReconcileScrollPagination(
                   inputAuthority, middle, 200).dispatchReady,
              "a middle viewport establishes residence without prefetch");
        (void)inputSession.ObserveScrollPaginationIntent(
            inputAuthority, L"page.scroll",
            widgetrail::declarative::ScrollAxis::Vertical,
            ScrollPaginationEdge::After, source, 201);
        const auto queued = inputSession.ReconcileScrollPagination(
            inputAuthority, trailing, 202);
        Check(queued.dispatchReady && queued.diagnostics.size() == 1 &&
                  queued.diagnostics.front().request.intentSource == source &&
                  queued.diagnostics.front().request.demandReason ==
                      ScrollPaginationDemandReason::ThresholdReentry,
              message);
        const auto acquired = inputSession.AcquireScrollPaginationDispatch(
            inputAuthority, trailing, 203);
        Check(acquired.first.has_value(),
              "each exact input demand reaches the existing final dispatcher");
    };
    checkDirectIntent(
        ScrollPaginationIntentSource::RightStick,
        "right-stick viewport intent rearms on a genuine threshold entry");
    checkDirectIntent(
        ScrollPaginationIntentSource::Pointer,
        "pointer scrolling uses the same exact viewport demand");
    checkDirectIntent(
        ScrollPaginationIntentSource::Accessibility,
        "accessibility scrolling uses the same exact viewport demand");

    WidgetInteractionSession directionalSession;
    directionalSession.SetFocus(L"paged.widget", snapshot, L"page.row.2");
    Check(!directionalSession.ReconcileScrollPagination(
               authority, middle, 300).dispatchReady,
          "directional fixture initializes outside the threshold");
    (void)directionalSession.ObserveScrollPaginationFocusIntent(
        authority, trailing, L"page.row.2", L"page.row.3",
        ScrollPaginationIntentSource::DirectionalNavigation, 301);
    const auto focusMove = directionalSession.MoveFocus(
        L"paged.widget", snapshot, L"page.row.3");
    Check(focusMove.changed && focusMove.focusedElementId == L"page.row.3",
          "D-pad/left-stick movement is never consumed by pagination admission");
    outcome = directionalSession.ReconcileScrollPagination(
        authority, trailing, 302);
    Check(outcome.dispatchReady && outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().request.intentSource ==
                  ScrollPaginationIntentSource::DirectionalNavigation,
          "focus-follow movement independently records its viewport prefetch intent");

    WidgetInteractionSession boundarySession;
    boundarySession.SetFocus(L"paged.widget", snapshot, L"page.row.4");
    Check(!boundarySession.ReconcileScrollPagination(
               authority, middle, 320).dispatchReady,
          "boundary fixture establishes a non-threshold viewport");
    const auto boundary = boundarySession.ObserveScrollPaginationBoundaryIntent(
        authority, trailing, L"page.row.4", NavigationDirection::Down,
        ScrollPaginationIntentSource::DirectionalNavigation, 321);
    Check(boundary.retainFocus && boundary.pagination.dispatchReady &&
              boundarySession.focusedElementId() == L"page.row.4",
          "D-pad at an unloaded logical edge retains exact widget focus and queues one page");
    const auto boundaryAcquire = boundarySession.AcquireScrollPaginationDispatch(
        authority, trailing, 322);
    Check(boundaryAcquire.first.has_value(),
          "retained boundary focus reaches the existing single-flight dispatcher");
    const auto joinedBoundary =
        boundarySession.ObserveScrollPaginationBoundaryIntent(
            authority, trailing, L"page.row.4", NavigationDirection::Down,
            ScrollPaginationIntentSource::DirectionalNavigation, 323);
    Check(joinedBoundary.retainFocus &&
              !joinedBoundary.pagination.dispatchReady &&
              boundarySession.focusedElementId() == L"page.row.4",
          "repeated D-pad cannot escape to tray or dispatch a second in-flight page");
    const auto unrelatedBoundary =
        boundarySession.ObserveScrollPaginationBoundaryIntent(
            authority, middle, L"page.row.4", NavigationDirection::Up,
            ScrollPaginationIntentSource::DirectionalNavigation, 324);
    Check(!unrelatedBoundary.retainFocus,
          "a direction without logical page authority retains ordinary boundary behavior");

    WidgetInteractionSession failureSession;
    Check(!failureSession.ReconcileScrollPagination(
               authority, middle, 400).dispatchReady,
          "failure fixture initializes outside the threshold");
    (void)failureSession.ObserveScrollPaginationIntent(
        authority, L"page.scroll",
        widgetrail::declarative::ScrollAxis::Vertical,
        ScrollPaginationEdge::After,
        ScrollPaginationIntentSource::RightStick, 401);
    Check(failureSession.ReconcileScrollPagination(
              authority, trailing, 402).dispatchReady,
          "failure fixture queues one exact demand");
    auto failedAcquire = failureSession.AcquireScrollPaginationDispatch(
        authority, trailing, 403);
    auto& failedRequest = failedAcquire.first;
    outcome = failureSession.CompleteScrollPaginationDispatch({
        *failedRequest, ScrollPaginationDispatchDisposition::TransportFailure,
        L"bounded transport failure", 404});
    Check(outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::TerminalFailure,
          "a dispatch error terminates the exact single-flight authority");
    Check(!failureSession.ReconcileScrollPagination(
               authority, trailing, 405).dispatchReady,
          "terminal failure cannot retry without new viewport intent");
    outcome = failureSession.ObserveScrollPaginationIntent(
        authority, L"page.scroll",
        widgetrail::declarative::ScrollAxis::Vertical,
        ScrollPaginationEdge::After,
        ScrollPaginationIntentSource::RightStick, 406);
    Check(outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::Retired &&
              outcome.diagnostics.front().reason == L"new-user-intent",
          "a new same-direction user intent explicitly clears terminal failure");
    Check(failureSession.ReconcileScrollPagination(
              authority, trailing, 407).dispatchReady,
          "the explicit retry can queue exactly once");
    auto retryAcquire = failureSession.AcquireScrollPaginationDispatch(
        authority, trailing, 408);
    auto& retryRequest = retryAcquire.first;
    Check(retryRequest &&
              retryRequest->demandGeneration > failedRequest->demandGeneration,
          "retry carries a newer bounded demand generation");
    outcome = failureSession.ObserveScrollPaginationFailure({
        L"paged.widget", L"runtime-7", retryRequest->action.actionId,
        retryRequest->action.sourceElementId,
        widgetrail::WidgetActionFailureCode::ControllerActionFailed}, 409);
    Check(outcome.diagnostics.size() == 1 &&
              outcome.diagnostics.front().kind ==
                  ScrollPaginationDiagnosticKind::TerminalFailure &&
              outcome.diagnostics.front().reason == L"worker-action-failure",
          "typed worker failure clears only the matching in-flight edge");

    auto replacementScope = PagedSnapshot(0, 102, L"dialog");
    outcome = failureSession.ReconcileScrollPagination(
        Authority(replacementScope, L"paged.widget"), trailing, 410);
    Check(!outcome.dispatchReady &&
              std::ranges::any_of(outcome.diagnostics, [](const auto& item) {
                  return item.kind == ScrollPaginationDiagnosticKind::Retired &&
                      item.reason == L"route-changed";
              }),
          "scope replacement deterministically retires pagination authority");
    Check(failureSession.RetireScrollPagination(
              L"paged.widget", L"widget-hidden").diagnostics.empty(),
          "route retirement is idempotent after scope invalidation");

    std::cout << "DLV-269 corrected measurement: adjacent-actions=1 "
                 "input-to-visible-page-ms=45\n";
}

void AnchoredSelectPopupIsExactAndBounded() {
    using widgetrail::input::ComputeSelectPopupLayout;
    using widgetrail::input::NavigationDirection;
    using widgetrail::input::SelectActivationResult;
    using widgetrail::input::WidgetInteractionSession;

    auto snapshot = Snapshot(71);
    snapshot.root.children.push_back(Select(L"density"));
    auto& select = snapshot.root.children.back();
    const auto authority = Authority(snapshot);
    WidgetInteractionSession session;
    Check(session.OpenSelectPopup(authority, select) ==
              SelectActivationResult::Opened,
          "A opens one exact-current Select popup");
    Check(session.selectPopup() && session.selectPopup()->highlightedOption == 0,
          "opening highlights the authored selected option");
    Check(session.MoveSelectPopup(authority, select, NavigationDirection::Down) &&
              session.selectPopup()->highlightedOption == 1,
          "Down moves to the next available option");
    const auto action = session.CommitSelectPopup(authority, select);
    Check(action && action->request.actionId == L"density.comfortable" &&
              action->request.sourceElementId == L"density" &&
              action->optionId == L"comfortable" && !session.selectPopup(),
          "A commits one exact option action with opener authority");

    Check(session.OpenSelectPopup(authority, select) ==
              SelectActivationResult::Opened &&
              session.CloseSelectPopup() && !session.selectPopup(),
          "B dismissal closes without an action");
    Check(session.OpenSelectPopup(authority, select) ==
              SelectActivationResult::Opened,
          "the same exact Select can reopen");
    auto compatible = snapshot;
    compatible.sequence = 72;
    compatible.root.children.back().selectOptions[0].isSelected = false;
    compatible.root.children.back().selectOptions[1].isSelected = true;
    auto reconciliation = session.ReconcileAdmission(compatible, {}, 100);
    Check(session.selectPopup() &&
              session.selectPopup()->snapshotSequence == 72 &&
              session.selectPopup()->options[1].isSelected &&
              reconciliation.sliderDamageNodeIds.empty(),
          "compatible Select publication preserves popup authority and reconciles state");
    Check(session.CloseSelectPopup(), "reconciled popup closes normally");

    auto selectedDisabled = snapshot;
    auto& disabledSelect = selectedDisabled.root.children.back();
    disabledSelect.selectOptions[0].isDisabled = true;
    Check(session.OpenSelectPopup(Authority(selectedDisabled), disabledSelect) ==
              SelectActivationResult::Opened &&
              session.selectPopup()->highlightedOption == 1,
          "disabled selected option falls back to the first available option");
    Check(session.CloseSelectPopup(), "disabled-selected fallback popup closes");
    for (auto& option : disabledSelect.selectOptions) option.isBusy = true;
    Check(session.OpenSelectPopup(Authority(selectedDisabled), disabledSelect) ==
              SelectActivationResult::ConsumedClosed &&
              !session.selectPopup(),
          "Select with no available option consumes activation while remaining closed");
    Check(session.OpenSelectPopup(authority, snapshot.root) ==
              SelectActivationResult::NotSelect,
          "only a non-Select returns the generic activation fallback result");

    Check(session.OpenSelectPopup(authority, select) ==
              SelectActivationResult::Opened,
          "the exact Select reopens before authority replacement");
    auto successor = snapshot;
    successor.sequence = 73;
    successor.root.children.back().kind = L"slider";
    successor.root.children.back().isSelect = false;
    const auto& changed = successor.root.children.back();
    Check(!session.SelectPopupCurrent(Authority(successor), changed),
          "opener kind replacement retires stale Select authority");

    const auto compact = ComputeSelectPopupLayout(
        {12.0F, 10.0F, 240.0F, 44.0F},
        {0.0F, 0.0F, 260.0F, 24.0F},
        *session.selectPopup());
    Check(compact.bounds.height <= 24.0F && compact.items.size() == 1 &&
              compact.items.front().bounds.height <= 24.0F,
          "small high-scale-equivalent viewports bound row count and height");

    auto longSnapshot = Snapshot(80);
    longSnapshot.root.children.push_back(Select(L"long-select"));
    auto& longSelect = longSnapshot.root.children.back();
    for (std::size_t index = longSelect.selectOptions.size(); index < 12; ++index) {
        auto option = longSelect.selectOptions.back();
        option.id = L"option-" + std::to_wstring(index);
        option.label = L"Option " + std::to_wstring(index);
        option.actionId = L"long-select.option-" + std::to_wstring(index);
        option.accessibilityLabel = option.label;
        option.isSelected = false;
        longSelect.selectOptions.push_back(std::move(option));
    }
    WidgetInteractionSession longSession;
    Check(longSession.OpenSelectPopup(Authority(longSnapshot), longSelect) ==
              SelectActivationResult::Opened,
          "long Select opens with bounded popup ownership");
    for (int step = 0; step < 9; ++step)
        Check(longSession.MoveSelectPopup(
                  Authority(longSnapshot), longSelect,
                  NavigationDirection::Down),
              "wheel-equivalent navigation advances the long Select");
    const auto scrolled = ComputeSelectPopupLayout(
        {40.0F, 120.0F, 180.0F, 44.0F},
        {32.0F, 80.0F, 260.0F, 304.0F},
        *longSession.selectPopup());
    Check(scrolled.items.size() == 8 &&
              scrolled.items.front().optionIndex == 2 &&
              scrolled.items.back().optionIndex == 9 &&
              scrolled.bounds.x >= 32.0F && scrolled.bounds.y >= 80.0F &&
              scrolled.bounds.x + scrolled.bounds.width <= 292.0F &&
              scrolled.bounds.y + scrolled.bounds.height <= 384.0F,
          "wheel-reachable rows scroll beyond eight while staying inside content bounds");
}

} // namespace

int main() {
    FocusAndSurfaceLifecycle();
    FocusRestorationPrecedesInteractiveAdmission();
    HostFocusRestorationUsesPresentationAuthority();
    PinnedViewReturnUsesExistingFocusMemory();
    ResponsiveFocusHandoffUsesInteractionOwner();
    RememberedGroupsUseExplicitAndGeometricEntryOwners();
    OneShotFocusGroupEntryUsesRuntimeHighWaterAuthority();
    DeferredFocusGroupEntryWaitsForReadyContent();
    ProvisionalOrdinalRestorePreservesRequestedGroupMemory();
    HorizontalRailsKeepDirectionalBoundaries();
    ResponsiveGridScrollOwnsDirectionalPriority();
    FreeScrollAndRetainedRefreshLifecycle();
    ExactSliderRequestAuthorityAndRollback();
    PressedAndAdmissionReconciliation();
    PaginationPrefetchLifecycle();
    AnchoredSelectPopupIsExactAndBounded();
    std::cout << "WidgetInteractionSessionTests passed (" << checks
              << " checks)\n";
    return EXIT_SUCCESS;
}
