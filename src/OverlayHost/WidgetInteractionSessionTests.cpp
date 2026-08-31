#include "WidgetInteractionSession.h"

#include <algorithm>
#include <cmath>
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

widgetrail::WidgetSnapshot Snapshot(
    const long long sequence = 41,
    const wchar_t* instanceId = L"fixture.instance",
    const wchar_t* scope = L"root") {
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
    scroll.scrollAxis = L"vertical";
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
    Check(adjustment.actionRequest && adjustment.visualChanged,
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
    snapshot.root.children = {std::move(entry), std::move(controls)};

    widgetrail::RenderResult render;
    AddRenderTarget(render, L"groups.entry", {0.0F, 0.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.play", {0.0F, 50.0F, 100.0F, 30.0F});
    AddRenderTarget(render, L"groups.seek", {120.0F, 50.0F, 100.0F, 30.0F});
    render.focusScopes[L"groups.entry"] = L"groups.root";
    render.focusScopes[L"groups.play"] = L"groups.root";
    render.focusScopes[L"groups.seek"] = L"groups.root";

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
    Check(reentry.consumed && reentry.target == L"row.first" &&
              reentry.retiredBinding &&
              reentry.retiredBinding->focusedElementId == L"row.second",
          "one re-entry consumes the binding and chooses the topmost fully visible target");
    Check(!session.ResolveFreeScrollReentry(current, render).consumed,
          "ordinary navigation resumes after exactly one consumed re-entry");

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
    Check(physical.consumed && physical.visualChanged && physical.actionRequest,
          "physical controller adjustment captures one exact request");
    const auto& request = *physical.actionRequest;
    Check(request.widgetId == L"fixture.widget" &&
              request.widgetInstanceId == L"fixture.instance" &&
              request.runtimeGeneration == L"runtime-7" &&
              request.presentationGeneration == L"presentation-9" &&
              request.inputScopeId == L"root" &&
              request.sourceElementId == L"volume-a" &&
              request.actionId == L"volume-a.changed" &&
              request.snapshotSequence == 41 && request.requestedValue,
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

    auto wrongSequence = request;
    ++wrongSequence.snapshotSequence;
    Check(!session.CancelSliderAction(wrongSequence, 1'020).consumed,
          "wrong snapshot sequence cannot retire an optimistic request");
    auto wrongAction = request;
    wrongAction.actionId = L"volume-b.changed";
    Check(!session.CancelSliderAction(wrongAction, 1'021).consumed,
          "wrong action identity cannot retire an optimistic request");
    auto wrongNode = request;
    wrongNode.sourceElementId = L"volume-b";
    Check(!session.CancelSliderAction(wrongNode, 1'022).consumed,
          "wrong node identity cannot retire an optimistic request");

    auto presentation = session.PrepareRenderPresentation(
        snapshot, L"volume-b", 1'023, false, true, true);
    Check(presentation.sliderValueOverrides.contains(L"volume-a") &&
              presentation.sliderValueOverrides.contains(L"volume-b"),
          "failed stale rollback attempts leave both exact optimistic requests intact");
    const auto rolledBack = session.CancelSliderAction(request, 1'024);
    Check(rolledBack.consumed && rolledBack.visualChanged,
          "exact dispatch rejection retires its optimistic request");
    presentation = session.PrepareRenderPresentation(
        snapshot, L"volume-b", 1'025, false, true, true);
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
    const auto reconciliation = session.ReconcileAdmission(replacement, 1'040);
    Check(reconciliation.sliderDamageNodeIds.empty(),
          "an authoritative acknowledgement does not repaint an already optimistic value");

    const auto pendingAgain = session.RequestSliderValue(
        Authority(replacement), Node(replacement, L"volume-a"), 0.8, 2'000);
    Check(pendingAgain.actionRequest.has_value(),
          "a replacement snapshot can establish fresh exact slider authority");
    const auto timedOut = session.Tick(&replacement, 4'001);
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

} // namespace

int main() {
    FocusAndSurfaceLifecycle();
    ResponsiveFocusHandoffUsesInteractionOwner();
    RememberedGroupsUseExplicitAndGeometricEntryOwners();
    FreeScrollAndRetainedRefreshLifecycle();
    ExactSliderRequestAuthorityAndRollback();
    PressedAndAdmissionReconciliation();
    PaginationPrefetchLifecycle();
    std::cout << "WidgetInteractionSessionTests passed (" << checks
              << " checks)\n";
    return EXIT_SUCCESS;
}
