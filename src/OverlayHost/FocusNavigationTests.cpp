#include "FocusNavigation.h"
#include "WidgetBridgeClient.h"

#include <cstdlib>
#include <iostream>
#include <string>

namespace {

int checks{};
void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Add(widgetrail::RenderResult& result, std::wstring id, widgetrail::declarative::Rect rect,
         const bool enabled = true, std::wstring scope = L"root") {
    result.focusScopes[id] = std::move(scope);
    result.focusRects[id] = rect;
    result.navigationRects[id] = rect;
    result.navigationEnabled[id] = enabled;
    result.hitRegions.push_back({std::move(id), rect, enabled});
}

} // namespace

int main() {
    using widgetrail::input::FindGeometricFocusTarget;
    using widgetrail::input::NavigationDirection;
    using widgetrail::input::ResolveResponsiveFocusPersistenceTarget;
    using widgetrail::input::ResolveVisibleFocusTarget;
    // A narrower header can have its center left of the first poster while
    // being entirely above it. Horizontal navigation must not jump rows.
    for (const float scale : {0.85F, 1.0F, 1.05F, 1.5F, 2.0F}) {
        widgetrail::RenderResult rail;
        const auto rect = [scale](float x, float y, float w, float h) {
            return widgetrail::declarative::Rect{x * scale, y * scale, w * scale, h * scale};
        };
        Add(rail, L"first", rect(40, 120, 150, 225));
        Add(rail, L"last", rect(204, 120, 150, 225));
        Add(rail, L"refresh", rect(40, 20, 96, 44));
        Add(rail, L"header-right", rect(370, 20, 60, 44));
        Check(!FindGeometricFocusTarget(L"first", NavigationDirection::Left, rail),
            "Left at the first poster does not select the narrower header above it");
        Check(!FindGeometricFocusTarget(L"last", NavigationDirection::Right, rail),
            "Right at the last poster does not jump diagonally to the header");
        Check(FindGeometricFocusTarget(L"first", NavigationDirection::Up, rail) == L"refresh",
            "Up still reaches the header above the rail");
        Check(FindGeometricFocusTarget(L"first", NavigationDirection::Right, rail) == L"last",
            "horizontal movement within the poster row remains intact");
        Add(rail, L"side-action", rect(-70, 200, 80, 44));
        Check(FindGeometricFocusTarget(L"first", NavigationDirection::Left, rail) == L"side-action",
            "an aligned action outside the rail remains horizontally reachable");
        rail.navigationEnabled[L"side-action"] = false;
        Check(!FindGeometricFocusTarget(L"first", NavigationDirection::Left, rail),
            "a disabled side action does not make a diagonal header eligible");
        rail.navigationRects[L"offscreen"] = rect(-124, 120, 150, 225);
        rail.focusScopes[L"offscreen"] = L"root";
        rail.navigationEnabled[L"offscreen"] = true;
        rail.revealableFocusIds.insert(L"offscreen");
        Check(FindGeometricFocusTarget(L"first", NavigationDirection::Left, rail) == L"offscreen",
            "horizontal navigation still includes revealable offscreen posters");
    }
    widgetrail::RenderResult touchingRows;
    Add(touchingRows, L"source", {100, 100, 100, 44});
    Add(touchingRows, L"above-left", {0, 56, 100, 44});
    Check(!FindGeometricFocusTarget(L"source", NavigationDirection::Left, touchingRows),
        "merely touching vertical bounds do not count as the same row");
    touchingRows.navigationRects[L"above-left"].y += 0.1F;
    Check(!FindGeometricFocusTarget(L"source", NavigationDirection::Left, touchingRows),
        "subpixel edge overlap does not turn an upper row into a Left target");
    widgetrail::RenderResult result;
    Add(result, L"play", {100, 50, 60, 60});
    Add(result, L"previous", {30, 55, 48, 48});
    Add(result, L"next", {182, 55, 48, 48});
    Add(result, L"like", {109, 132, 42, 42});
    Add(result, L"non-navigable", {109, 190, 42, 42}, false);
    Add(result, L"far-down", {300, 210, 42, 42});
    Add(result, L"modal-button", {109, 100, 42, 42}, true, L"modal");

    Check(widgetrail::input::FindPointerHitTarget(120, 70, L"root", result)->id == L"play",
          "pointer hit resolves visible control in active scope");
    Check(!widgetrail::input::FindPointerHitTarget(120, 202, L"root", result)->enabled,
          "pointer can select a disabled control without activating it");
    Check(!widgetrail::input::FindPointerHitTarget(120, 110, L"root", result) ||
              widgetrail::input::FindPointerHitTarget(120, 110, L"root", result)->id !=
                  L"modal-button",
          "pointer cannot cross into a nested inactive scope");
    Check(widgetrail::input::FindPointerHitTarget(120, 110, L"modal", result)->id ==
              L"modal-button",
          "pointer resolves the active nested scope");
    Check(!widgetrail::input::FindPointerHitTarget(800, 800, L"root", result),
          "pointer outside all visible geometry is ignored");

    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Left, result) == L"previous",
          "row navigation chooses previous");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Right, result) == L"next",
          "row navigation chooses next");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Down, result) == L"like",
          "column navigation chooses like");
    Check(FindGeometricFocusTarget(L"like", NavigationDirection::Down, result) == L"far-down",
          "non-navigable target is skipped");
    Check(FindGeometricFocusTarget(L"previous", NavigationDirection::Up, result) == std::nullopt,
          "missing direction stays put");
    Check(!widgetrail::input::IsEnabledFocusTarget(L"non-navigable", result),
          "host-excluded component is not focusable");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Down, result) != L"modal-button",
          "geometric fallback cannot cross nested input scopes");
    Check(ResolveVisibleFocusTarget(L"play", L"root", result) == L"play",
          "visible preferred focus survives responsive layout");

    result.focusRects.erase(L"play");
    Check(!widgetrail::input::IsEnabledFocusTarget(L"play", result),
          "clipped control is never an enabled focus target");
    Check(ResolveVisibleFocusTarget(L"play", L"root", result) == L"previous",
          "clipped preferred focus recovers in deterministic tree order");
    result.hitRegions[0].enabled = false;
    Check(ResolveVisibleFocusTarget(L"play", L"root", result) == L"previous",
          "clipped host-excluded focus cannot receive controller input");
    result.focusRects.erase(L"previous");
    Check(ResolveVisibleFocusTarget(L"play", L"root", result) == L"next",
          "recovery skips controls clipped by a smaller viewport");
    Check(ResolveVisibleFocusTarget(L"play", L"modal", result) == L"modal-button",
          "responsive recovery stays inside the active nested input scope");

    result.focusRects.erase(L"modal-button");
    Check(!ResolveVisibleFocusTarget(L"play", L"modal", result),
          "fully clipped active scope reports no actionable focus");
    Check(!ResolveVisibleFocusTarget({}, L"modal", result),
          "focusless fully clipped scope remains explicitly unavailable");

    widgetrail::RenderResult focuslessRoot;
    Add(focuslessRoot, L"first-visible", {0, 0, 160, 44});
    Check(ResolveVisibleFocusTarget({}, L"root", focuslessRoot) == L"first-visible",
          "focusless responsive recovery selects the first visible root target");

    widgetrail::WidgetSnapshot responsiveSnapshot;
    responsiveSnapshot.activeInputScopeId = L"root";
    responsiveSnapshot.root.id = L"root";
    responsiveSnapshot.root.kind = L"stack";
    widgetrail::WidgetNode compact;
    compact.id = L"compact";
    compact.kind = L"row";
    compact.visibleWhen = L"compactOnly";
    compact.children = {
        widgetrail::WidgetNode{.id = L"compact-home", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.home"},
        widgetrail::WidgetNode{.id = L"compact-library", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.library"},
    };
    widgetrail::WidgetNode rail;
    rail.id = L"rail";
    rail.kind = L"stack";
    rail.visibleWhen = L"expandedOnly";
    rail.children = {
        widgetrail::WidgetNode{.id = L"rail-home", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.home"},
        widgetrail::WidgetNode{.id = L"rail-library", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.library"},
    };
    responsiveSnapshot.root.children = {compact, rail};
    const auto expandedLibrary = ResolveResponsiveFocusPersistenceTarget(
        responsiveSnapshot, L"compact-library", L"root", false);
    Check(expandedLibrary == L"rail-library",
          "expanded presentation preserves the explicit logical destination");
    Check(!ResolveResponsiveFocusPersistenceTarget(
              responsiveSnapshot, *expandedLibrary, L"root", false),
          "same committed expanded mode cannot remap an already visible alias");
    const auto compactHome = ResolveResponsiveFocusPersistenceTarget(
        responsiveSnapshot, L"rail-home", L"root", true);
    Check(compactHome == L"compact-home",
          "compact presentation preserves the explicit logical destination");
    Check(!ResolveResponsiveFocusPersistenceTarget(
              responsiveSnapshot, *compactHome, L"root", true),
          "one compact transition consumes the responsive alias handoff");

    auto sharedActionOnly = responsiveSnapshot;
    sharedActionOnly.root.children[1].children[1].focusPersistenceId = L"nav.other";
    Check(!ResolveResponsiveFocusPersistenceTarget(
              sharedActionOnly, L"compact-library", L"root", false),
          "a shared action ID is never treated as focus equivalence");

    auto ambiguousPersistence = responsiveSnapshot;
    ambiguousPersistence.root.children[1].children[0].focusPersistenceId = L"nav.library";
    Check(!ResolveResponsiveFocusPersistenceTarget(
              ambiguousPersistence, L"compact-library", L"root", false),
          "ambiguous explicit focus persistence fails closed");

    widgetrail::RenderResult twoColumnGrid;
    Add(twoColumnGrid, L"grid-0", {0, 0, 100, 44});
    Add(twoColumnGrid, L"grid-1", {110, 0, 100, 44});
    Add(twoColumnGrid, L"grid-2", {0, 52, 100, 44});
    Add(twoColumnGrid, L"grid-3", {110, 52, 100, 44});
    Add(twoColumnGrid, L"grid-4", {0, 104, 100, 44});
    Check(FindGeometricFocusTarget(
              L"grid-0", NavigationDirection::Right, twoColumnGrid) == L"grid-1",
          "responsive two-column grid navigates across its realized row");
    Check(FindGeometricFocusTarget(
              L"grid-0", NavigationDirection::Down, twoColumnGrid) == L"grid-2",
          "responsive two-column grid navigates down its realized column");
    Check(FindGeometricFocusTarget(
              L"grid-3", NavigationDirection::Down, twoColumnGrid) == L"grid-4",
          "odd final grid row remains reachable from the preceding column");
    Check(!FindGeometricFocusTarget(
              L"grid-4", NavigationDirection::Down, twoColumnGrid),
          "last responsive grid control exposes the root Down boundary to the tray");

    widgetrail::RenderResult oneColumnGrid;
    for (int index = 0; index < 5; ++index)
        Add(oneColumnGrid, L"grid-" + std::to_wstring(index),
            {0, static_cast<float>(index * 52), 210, 44});
    Check(FindGeometricFocusTarget(
              L"grid-1", NavigationDirection::Down, oneColumnGrid) == L"grid-2",
          "narrow one-column reflow preserves document-order navigation");
    Check(ResolveVisibleFocusTarget(L"grid-3", L"root", oneColumnGrid) == L"grid-3",
          "responsive resize retains the same stable focus ID");

    widgetrail::RenderResult scaledGrid;
    Add(scaledGrid, L"grid-0", {0, 0, 150, 66});
    Add(scaledGrid, L"grid-1", {165, 0, 150, 66});
    Add(scaledGrid, L"grid-2", {0, 78, 150, 66});
    Check(FindGeometricFocusTarget(
              L"grid-0", NavigationDirection::Right, scaledGrid) == L"grid-1",
          "DPI-scaled responsive geometry preserves directional navigation");
    Check(ResolveVisibleFocusTarget(L"grid-2", L"root", scaledGrid) == L"grid-2",
          "DPI-scaled reflow retains the same stable focus ID");

    widgetrail::WidgetNode pagedScroll{
        .id = L"collection.scroll",
        .kind = L"scroll",
        .scrollAxis = L"vertical",
        .scrollNearStartActionId = L"collection.before",
        .scrollNearEndActionId = L"collection.after",
        .scrollPaginationThreshold = 1,
        .collectionAnchorKey = L"key.2",
    };
    for (int index = 0; index < 5; ++index) {
        pagedScroll.children.push_back(widgetrail::WidgetNode{
            .id = L"item." + std::to_wstring(index),
            .kind = L"button",
            .collectionItemKey = L"key." + std::to_wstring(index),
        });
    }
    const auto verticalPage = [&](const float viewportY) {
        widgetrail::RenderResult page;
        page.scrollViewports.emplace(
            L"collection.scroll",
            widgetrail::RenderScrollViewport{
                widgetrail::declarative::ScrollAxis::Vertical,
                {0.0F, viewportY, 200.0F, 80.0F}, viewportY, 140.0F});
        for (int index = 0; index < 5; ++index) {
            Add(page, L"item." + std::to_wstring(index),
                {0.0F, static_cast<float>(index * 44), 200.0F, 40.0F});
        }
        return page;
    };
    const auto middleActions = widgetrail::input::FindScrollPaginationActions(
        pagedScroll, L"root", verticalPage(44.0F));
    Check(middleActions.empty(),
          "a viewport outside both authored thresholds does not prefetch");
    const auto beforeActions = widgetrail::input::FindScrollPaginationActions(
        pagedScroll, L"root", verticalPage(0.0F));
    Check(beforeActions.size() == 1 &&
              beforeActions.front().edge ==
                  widgetrail::input::ScrollPaginationEdge::Before &&
              beforeActions.front().actionId == L"collection.before" &&
              beforeActions.front().edgeKey == L"key.0" &&
              beforeActions.front().anchorKey == L"key.2",
          "the rendered leading viewport threshold preserves cursor and anchor authority");
    const auto afterActions = widgetrail::input::FindScrollPaginationActions(
        pagedScroll, L"root", verticalPage(136.0F));
    Check(afterActions.size() == 1 &&
              afterActions.front().edge ==
                  widgetrail::input::ScrollPaginationEdge::After &&
              afterActions.front().actionId == L"collection.after" &&
              afterActions.front().edgeKey == L"key.4",
          "the rendered trailing viewport threshold—not focused-row identity—prefetches");

    auto horizontalScroll = pagedScroll;
    horizontalScroll.id = L"horizontal.scroll";
    horizontalScroll.scrollAxis = L"horizontal";
    for (int index = 0; index < 5; ++index)
        horizontalScroll.children[index].id = L"horizontal." + std::to_wstring(index);
    widgetrail::RenderResult horizontalPage;
    horizontalPage.scrollViewports.emplace(
        L"horizontal.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Horizontal,
            {136.0F, 0.0F, 80.0F, 120.0F}, 136.0F, 140.0F});
    for (int index = 0; index < 5; ++index) {
        Add(horizontalPage, L"horizontal." + std::to_wstring(index),
            {static_cast<float>(index * 44), 0.0F, 40.0F, 120.0F});
    }
    const auto horizontalActions = widgetrail::input::FindScrollPaginationActions(
        horizontalScroll, L"root", horizontalPage);
    Check(horizontalActions.size() == 1 &&
              horizontalActions.front().axis ==
                  widgetrail::declarative::ScrollAxis::Horizontal &&
              horizontalActions.front().edge ==
                  widgetrail::input::ScrollPaginationEdge::After,
          "horizontal viewport thresholds use the same generic pagination contract");

    widgetrail::WidgetNode nestedRoot{
        .id = L"nested.root",
        .kind = L"stack",
        .children = {widgetrail::WidgetNode{
            .id = L"outer.scroll",
            .kind = L"scroll",
            .scrollAxis = L"vertical",
            .children = {horizontalScroll},
        }},
    };
    auto nestedRender = horizontalPage;
    nestedRender.scrollViewports.emplace(
        L"outer.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {0.0F, 0.0F, 240.0F, 180.0F}, 0.0F, 400.0F});
    const auto horizontalOwner = widgetrail::input::ResolveFocusedScrollOwner(
        nestedRoot, L"horizontal.4",
        widgetrail::declarative::ScrollAxis::Horizontal,
        L"root", nestedRender);
    const auto verticalOwner = widgetrail::input::ResolveFocusedScrollOwner(
        nestedRoot, L"horizontal.4",
        widgetrail::declarative::ScrollAxis::Vertical,
        L"root", nestedRender);
    Check(horizontalOwner.disposition ==
              widgetrail::input::FocusedScrollResolutionDisposition::Resolved &&
              horizontalOwner.scrollId == L"horizontal.scroll",
          "exact ancestry selects the deepest eligible horizontal Scroll");
    Check(verticalOwner.disposition ==
              widgetrail::input::FocusedScrollResolutionDisposition::Resolved &&
              verticalOwner.scrollId == L"outer.scroll",
          "exact ancestry falls back to the eligible vertical ancestor without axis guessing");

    widgetrail::RenderResult scrolled;
    Add(scrolled, L"session-0", {0, 0, 200, 44});
    scrolled.focusScopes[L"session-1"] = L"root";
    scrolled.navigationRects[L"session-1"] = {0, 48, 200, 44};
    scrolled.navigationEnabled[L"session-1"] = true;
    scrolled.revealableFocusIds.insert(L"session-1");
    Check(FindGeometricFocusTarget(
              L"session-0", NavigationDirection::Down, scrolled) == L"session-1",
          "offscreen scroll descendant participates in geometric navigation");
    Check(widgetrail::input::IsEnabledFocusTarget(L"session-1", scrolled),
          "host-revealable descendant is an enabled focus target");
    Check(ResolveVisibleFocusTarget(L"session-1", L"root", scrolled) == L"session-1",
          "preferred offscreen scroll focus survives until the reveal render pass");

    using widgetrail::input::FindFreeScrollReentryTarget;
    widgetrail::WidgetNode reentryRoot{
        .id = L"reentry.root",
        .kind = L"stack",
        .children = {
            widgetrail::WidgetNode{
                .id = L"outer.scroll",
                .kind = L"scroll",
                .scrollAxis = L"vertical",
                .children = {
                    widgetrail::WidgetNode{
                        .id = L"inner.scroll",
                        .kind = L"scroll",
                        .scrollAxis = L"vertical",
                        .children = {
                            widgetrail::WidgetNode{.id = L"partial-leading", .kind = L"button"},
                            widgetrail::WidgetNode{.id = L"fully-second", .kind = L"button"},
                            widgetrail::WidgetNode{.id = L"fully-first", .kind = L"button"},
                            widgetrail::WidgetNode{.id = L"disabled-leading", .kind = L"button"},
                            widgetrail::WidgetNode{.id = L"other-scope", .kind = L"button"},
                        },
                    },
                },
            },
        },
    };
    widgetrail::RenderResult reentry;
    reentry.scrollViewports.emplace(
        L"inner.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {10.0F, 20.0F, 200.0F, 120.0F}, 80.0F, 300.0F});
    Add(reentry, L"partial-leading", {10.0F, 8.0F, 200.0F, 44.0F});
    Add(reentry, L"fully-second", {10.0F, 72.0F, 200.0F, 44.0F});
    Add(reentry, L"fully-first", {10.0F, 24.0F, 200.0F, 44.0F});
    Add(reentry, L"disabled-leading", {10.0F, 20.0F, 200.0F, 44.0F}, false);
    Add(reentry, L"other-scope", {10.0F, 21.0F, 200.0F, 44.0F}, true, L"modal");
    Check(FindFreeScrollReentryTarget(
              reentryRoot, L"inner.scroll",
              widgetrail::declarative::ScrollAxis::Vertical,
              L"root", reentry) == L"fully-first",
          "vertical re-entry chooses the topmost fully visible enabled descendant");

    reentry.navigationRects[L"fully-first"] = {10.0F, 126.0F, 200.0F, 44.0F};
    reentry.hitRegions[2].rect = reentry.navigationRects[L"fully-first"];
    reentry.navigationRects[L"fully-second"] = {10.0F, 130.0F, 200.0F, 44.0F};
    reentry.hitRegions[1].rect = reentry.navigationRects[L"fully-second"];
    Check(FindFreeScrollReentryTarget(
              reentryRoot, L"inner.scroll",
              widgetrail::declarative::ScrollAxis::Vertical,
              L"root", reentry) == L"partial-leading",
          "vertical re-entry falls back to the leading partially visible target");

    reentry.scrollViewports[L"inner.scroll"] = {
        widgetrail::declarative::ScrollAxis::Horizontal,
        {20.0F, 10.0F, 120.0F, 100.0F}, 60.0F, 240.0F};
    reentryRoot.children[0].children[0].scrollAxis = L"horizontal";
    reentry.navigationRects[L"partial-leading"] = {8.0F, 10.0F, 44.0F, 100.0F};
    reentry.hitRegions[0].rect = reentry.navigationRects[L"partial-leading"];
    reentry.navigationRects[L"fully-first"] = {72.0F, 10.0F, 44.0F, 100.0F};
    reentry.hitRegions[2].rect = reentry.navigationRects[L"fully-first"];
    reentry.navigationRects[L"fully-second"] = {24.0F, 10.0F, 44.0F, 100.0F};
    reentry.hitRegions[1].rect = reentry.navigationRects[L"fully-second"];
    Check(FindFreeScrollReentryTarget(
              reentryRoot, L"inner.scroll",
              widgetrail::declarative::ScrollAxis::Horizontal,
              L"root", reentry) == L"fully-second",
          "horizontal re-entry chooses the leading-most fully visible target");
    Check(!FindFreeScrollReentryTarget(
              reentryRoot, L"missing.scroll",
              widgetrail::declarative::ScrollAxis::Vertical,
              L"root", reentry),
          "missing scroll geometry clears re-entry without inventing focus");
    Check(!FindFreeScrollReentryTarget(
              reentryRoot, L"inner.scroll",
              widgetrail::declarative::ScrollAxis::Vertical,
              L"root", reentry),
          "axis mismatch clears re-entry instead of crossing scroll authority");

    std::cout << "FocusNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
