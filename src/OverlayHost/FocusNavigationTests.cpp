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

void Add(gba::RenderResult& result, std::wstring id, gba::declarative::Rect rect,
         const bool enabled = true, std::wstring scope = L"root") {
    result.focusScopes[id] = std::move(scope);
    result.focusRects[id] = rect;
    result.navigationRects[id] = rect;
    result.navigationEnabled[id] = enabled;
    result.hitRegions.push_back({std::move(id), rect, enabled});
}

} // namespace

int main() {
    using gba::input::FindGeometricFocusTarget;
    using gba::input::NavigationDirection;
    using gba::input::ResolveResponsiveFocusPersistenceTarget;
    using gba::input::ResolveVisibleFocusTarget;
    gba::RenderResult result;
    Add(result, L"play", {100, 50, 60, 60});
    Add(result, L"previous", {30, 55, 48, 48});
    Add(result, L"next", {182, 55, 48, 48});
    Add(result, L"like", {109, 132, 42, 42});
    Add(result, L"non-navigable", {109, 190, 42, 42}, false);
    Add(result, L"far-down", {300, 210, 42, 42});
    Add(result, L"modal-button", {109, 100, 42, 42}, true, L"modal");

    Check(gba::input::FindPointerHitTarget(120, 70, L"root", result)->id == L"play",
          "pointer hit resolves visible control in active scope");
    Check(!gba::input::FindPointerHitTarget(120, 202, L"root", result)->enabled,
          "pointer can select a disabled control without activating it");
    Check(!gba::input::FindPointerHitTarget(120, 110, L"root", result) ||
              gba::input::FindPointerHitTarget(120, 110, L"root", result)->id !=
                  L"modal-button",
          "pointer cannot cross into a nested inactive scope");
    Check(gba::input::FindPointerHitTarget(120, 110, L"modal", result)->id ==
              L"modal-button",
          "pointer resolves the active nested scope");
    Check(!gba::input::FindPointerHitTarget(800, 800, L"root", result),
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
    Check(!gba::input::IsEnabledFocusTarget(L"non-navigable", result),
          "host-excluded component is not focusable");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Down, result) != L"modal-button",
          "geometric fallback cannot cross nested input scopes");
    Check(ResolveVisibleFocusTarget(L"play", L"root", result) == L"play",
          "visible preferred focus survives responsive layout");

    result.focusRects.erase(L"play");
    Check(!gba::input::IsEnabledFocusTarget(L"play", result),
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

    gba::RenderResult focuslessRoot;
    Add(focuslessRoot, L"first-visible", {0, 0, 160, 44});
    Check(ResolveVisibleFocusTarget({}, L"root", focuslessRoot) == L"first-visible",
          "focusless responsive recovery selects the first visible root target");

    gba::WidgetSnapshot responsiveSnapshot;
    responsiveSnapshot.activeInputScopeId = L"root";
    responsiveSnapshot.root.id = L"root";
    responsiveSnapshot.root.kind = L"stack";
    gba::WidgetNode compact;
    compact.id = L"compact";
    compact.kind = L"row";
    compact.visibleWhen = L"compactOnly";
    compact.children = {
        gba::WidgetNode{.id = L"compact-home", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.home"},
        gba::WidgetNode{.id = L"compact-library", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.library"},
    };
    gba::WidgetNode rail;
    rail.id = L"rail";
    rail.kind = L"stack";
    rail.visibleWhen = L"expandedOnly";
    rail.children = {
        gba::WidgetNode{.id = L"rail-home", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.home"},
        gba::WidgetNode{.id = L"rail-library", .kind = L"button",
            .actionId = L"shared-action", .focusPersistenceId = L"nav.library"},
    };
    responsiveSnapshot.root.children = {compact, rail};
    Check(ResolveResponsiveFocusPersistenceTarget(
              responsiveSnapshot, L"compact-library", L"root", false) ==
              L"rail-library",
          "expanded presentation preserves the explicit logical destination");
    Check(ResolveResponsiveFocusPersistenceTarget(
              responsiveSnapshot, L"rail-home", L"root", true) ==
              L"compact-home",
          "compact presentation preserves the explicit logical destination");

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

    gba::RenderResult twoColumnGrid;
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

    gba::RenderResult oneColumnGrid;
    for (int index = 0; index < 5; ++index)
        Add(oneColumnGrid, L"grid-" + std::to_wstring(index),
            {0, static_cast<float>(index * 52), 210, 44});
    Check(FindGeometricFocusTarget(
              L"grid-1", NavigationDirection::Down, oneColumnGrid) == L"grid-2",
          "narrow one-column reflow preserves document-order navigation");
    Check(ResolveVisibleFocusTarget(L"grid-3", L"root", oneColumnGrid) == L"grid-3",
          "responsive resize retains the same stable focus ID");

    gba::RenderResult scaledGrid;
    Add(scaledGrid, L"grid-0", {0, 0, 150, 66});
    Add(scaledGrid, L"grid-1", {165, 0, 150, 66});
    Add(scaledGrid, L"grid-2", {0, 78, 150, 66});
    Check(FindGeometricFocusTarget(
              L"grid-0", NavigationDirection::Right, scaledGrid) == L"grid-1",
          "DPI-scaled responsive geometry preserves directional navigation");
    Check(ResolveVisibleFocusTarget(L"grid-2", L"root", scaledGrid) == L"grid-2",
          "DPI-scaled reflow retains the same stable focus ID");

    gba::WidgetNode pagedScroll{
        .id = L"library.scroll",
        .kind = L"scroll",
        .scrollAxis = L"vertical",
        .scrollNearStartActionId = L"library.previous",
        .scrollNearEndActionId = L"library.next",
        .scrollPaginationThreshold = 1,
        .children = {
            gba::WidgetNode{.id = L"library.first", .kind = L"button"},
            gba::WidgetNode{
                .id = L"library.last",
                .kind = L"actionSurface",
                .children = {
                    gba::WidgetNode{.id = L"library.last.label", .kind = L"text"},
                },
            },
        },
    };
    const auto nextPage = gba::input::FindScrollPaginationAction(
        pagedScroll, L"library.last.label", NavigationDirection::Down);
    Check(nextPage && nextPage->actionId == L"library.next" &&
              nextPage->sourceElementId == L"library.scroll",
          "Down on an already-focused last row resolves pagination before tray fallback");
    const auto previousPage = gba::input::FindScrollPaginationAction(
        pagedScroll, L"library.first", NavigationDirection::Up);
    Check(previousPage && previousPage->actionId == L"library.previous",
          "Up on an already-focused first row resolves previous-page pagination");
    Check(!gba::input::FindScrollPaginationAction(
              pagedScroll, L"library.first", NavigationDirection::Down),
          "A non-boundary direction preserves ordinary focus navigation");

    gba::WidgetNode singleItemScroll = pagedScroll;
    singleItemScroll.children.resize(1);
    singleItemScroll.children[0].id = L"library.only";
    const auto singleItemNext = gba::input::FindScrollPaginationAction(
        singleItemScroll, L"library.only", NavigationDirection::Down);
    Check(singleItemNext && singleItemNext->actionId == L"library.next",
          "A one-row page can paginate when no ordinary focus move exists");

    pagedScroll.scrollNearEndActionId.clear();
    Check(!gba::input::FindScrollPaginationAction(
              pagedScroll, L"library.last", NavigationDirection::Down),
          "A boundary without a configured action remains available to tray fallback");

    gba::RenderResult scrolled;
    Add(scrolled, L"session-0", {0, 0, 200, 44});
    scrolled.focusScopes[L"session-1"] = L"root";
    scrolled.navigationRects[L"session-1"] = {0, 48, 200, 44};
    scrolled.navigationEnabled[L"session-1"] = true;
    scrolled.revealableFocusIds.insert(L"session-1");
    Check(FindGeometricFocusTarget(
              L"session-0", NavigationDirection::Down, scrolled) == L"session-1",
          "offscreen scroll descendant participates in geometric navigation");
    Check(gba::input::IsEnabledFocusTarget(L"session-1", scrolled),
          "host-revealable descendant is an enabled focus target");
    Check(ResolveVisibleFocusTarget(L"session-1", L"root", scrolled) == L"session-1",
          "preferred offscreen scroll focus survives until the reveal render pass");

    std::cout << "FocusNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
