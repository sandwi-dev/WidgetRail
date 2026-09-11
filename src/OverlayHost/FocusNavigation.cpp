#include "FocusNavigation.h"
#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <tuple>
#include <vector>

namespace widgetrail::input {
namespace {

using declarative::Rect;

float CenterX(const Rect& rect) noexcept { return rect.x + rect.width * 0.5F; }
float CenterY(const Rect& rect) noexcept { return rect.y + rect.height * 0.5F; }

bool Overlaps(const float a0, const float a1, const float b0, const float b1) noexcept {
    return std::min(a1, b1) >= std::max(a0, b0);
}

bool IsFocusableNode(const WidgetNode& node) noexcept {
    return node.kind == L"button" || node.kind == L"slider" ||
        node.kind == L"actionSurface";
}

bool IsResponsiveVisible(const WidgetNode& node, const bool compactMode) noexcept {
    return node.visibleWhen.empty() || node.visibleWhen == L"always" ||
        (compactMode && node.visibleWhen == L"compactOnly") ||
        (!compactMode && node.visibleWhen == L"expandedOnly");
}

const WidgetNode* FindNode(
    const WidgetNode& node,
    const std::wstring_view nodeId) noexcept {
    if (node.id == nodeId) return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindNode(child, nodeId)) return found;
    }
    return nullptr;
}

bool FindNodePath(
    const WidgetNode& node,
    const std::wstring_view nodeId,
    std::vector<const WidgetNode*>& path) {
    path.push_back(&node);
    if (node.id == nodeId) return true;
    for (const auto& child : node.children) {
        if (FindNodePath(child, nodeId, path)) return true;
    }
    path.pop_back();
    return false;
}

void CollectCollectionItems(
    const WidgetNode& node,
    const WidgetNode& collectionRoot,
    std::vector<const WidgetNode*>& items) {
    if (&node != &collectionRoot && node.kind == L"scroll") return;
    if (!node.collectionItemKey.empty()) {
        items.push_back(&node);
        return;
    }
    for (const auto& child : node.children)
        CollectCollectionItems(child, collectionRoot, items);
}

std::optional<Rect> CollectionItemBounds(
    const WidgetNode& item,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    std::optional<Rect> bounds;
    const auto visit = [&](const auto& self, const WidgetNode& node,
                           const bool root) -> void {
        if (!root && node.kind == L"scroll") return;
        const auto geometry = renderResult.navigationRects.find(node.id);
        const auto scope = renderResult.focusScopes.find(node.id);
        if (geometry != renderResult.navigationRects.end() &&
            scope != renderResult.focusScopes.end() &&
            scope->second == activeScopeId && geometry->second.width > 0.0F &&
            geometry->second.height > 0.0F) {
            if (!bounds) {
                bounds = geometry->second;
            } else {
                const float left = std::min(bounds->x, geometry->second.x);
                const float top = std::min(bounds->y, geometry->second.y);
                const float right = std::max(
                    bounds->x + bounds->width,
                    geometry->second.x + geometry->second.width);
                const float bottom = std::max(
                    bounds->y + bounds->height,
                    geometry->second.y + geometry->second.height);
                *bounds = Rect{left, top, right - left, bottom - top};
            }
        }
        for (const auto& child : node.children) self(self, child, false);
    };
    visit(visit, item, true);
    return bounds;
}

bool Intersects(const Rect& item, const Rect& viewport) noexcept {
    constexpr float epsilon = 0.5F;
    return item.x < viewport.x + viewport.width - epsilon &&
        item.x + item.width > viewport.x + epsilon &&
        item.y < viewport.y + viewport.height - epsilon &&
        item.y + item.height > viewport.y + epsilon;
}

void ExpandBounds(std::optional<Rect>& bounds, const Rect& candidate) noexcept {
    if (!bounds) {
        bounds = candidate;
        return;
    }
    const float left = std::min(bounds->x, candidate.x);
    const float top = std::min(bounds->y, candidate.y);
    const float right = std::max(
        bounds->x + bounds->width, candidate.x + candidate.width);
    const float bottom = std::max(
        bounds->y + bounds->height, candidate.y + candidate.height);
    *bounds = {left, top, right - left, bottom - top};
}

void CollectFocusGroupDescendants(
    const WidgetNode& group,
    const std::wstring_view activeScopeId,
    const std::wstring_view inheritedScope,
    const RenderResult& renderResult,
    GeometricFocusGroupCandidate& candidate) {
    const auto& geometry = renderResult.navigationRects.empty()
        ? renderResult.focusRects
        : renderResult.navigationRects;
    for (const auto& child : group.children) {
        const std::wstring_view childScope = child.inputScopeId.empty()
            ? inheritedScope
            : std::wstring_view(child.inputScopeId);
        const auto navigation = geometry.find(child.id);
        const auto scope = renderResult.focusScopes.find(child.id);
        if (childScope == activeScopeId &&
            navigation != geometry.end() &&
            scope != renderResult.focusScopes.end() &&
            scope->second == activeScopeId) {
            candidate.descendantFocusIds.push_back(child.id);
            const auto visible = renderResult.focusRects.find(child.id);
            if (visible != renderResult.focusRects.end() &&
                visible->second.width > 0.0F && visible->second.height > 0.0F &&
                IsEnabledFocusTarget(child.id, renderResult)) {
                ExpandBounds(candidate.bounds, visible->second);
            }
        }
        CollectFocusGroupDescendants(
            child, activeScopeId, childScope, renderResult, candidate);
    }
}

void CollectOutermostFocusGroups(
    const WidgetNode& node,
    const std::wstring_view activeScopeId,
    const std::wstring_view inheritedScope,
    const RenderResult& renderResult,
    std::vector<GeometricFocusGroupCandidate>& groups) {
    const std::wstring_view scope = node.inputScopeId.empty()
        ? inheritedScope
        : std::wstring_view(node.inputScopeId);
    if (scope == activeScopeId && !node.initialChildFocusId.empty()) {
        GeometricFocusGroupCandidate candidate;
        candidate.groupId = node.id;
        CollectFocusGroupDescendants(
            node, activeScopeId, scope, renderResult, candidate);
        groups.push_back(std::move(candidate));
        return;
    }
    for (const auto& child : node.children) {
        CollectOutermostFocusGroups(
            child, activeScopeId, scope, renderResult, groups);
    }
}

bool ContainsFocusId(
    const GeometricFocusGroupCandidate& group,
    const std::wstring_view id) noexcept {
    return std::ranges::find(group.descendantFocusIds, id) !=
        group.descendantFocusIds.end();
}

struct GeometricFocusScore final {
    bool inDirection{};
    std::tuple<bool, float, float, std::wstring> value{
        true,
        std::numeric_limits<float>::infinity(),
        std::numeric_limits<float>::infinity(),
        {}};
};

GeometricFocusScore ScoreGeometricCandidate(
    const Rect& current,
    const Rect& candidate,
    const NavigationDirection direction,
    std::wstring id) {
    const float dx = CenterX(candidate) - CenterX(current);
    const float dy = CenterY(candidate) - CenterY(current);
    float primary{};
    float perpendicular{};
    bool inDirection{};
    bool inBeam{};
    switch (direction) {
    case NavigationDirection::Left:
        primary = -dx;
        perpendicular = std::abs(dy);
        inBeam = std::min(current.y + current.height, candidate.y + candidate.height) -
            std::max(current.y, candidate.y) > 0.5F;
        inDirection = dx < -0.5F && inBeam;
        break;
    case NavigationDirection::Right:
        primary = dx;
        perpendicular = std::abs(dy);
        // Left/Right stays within vertically overlapping controls. A narrower
        // header above a poster is not a horizontal neighbor merely because
        // their centers differ. Ignore subpixel edge contact as well.
        inBeam = std::min(current.y + current.height, candidate.y + candidate.height) -
            std::max(current.y, candidate.y) > 0.5F;
        inDirection = dx > 0.5F && inBeam;
        break;
    case NavigationDirection::Up:
        inDirection = dy < -0.5F;
        primary = -dy;
        perpendicular = std::abs(dx);
        inBeam = Overlaps(current.x, current.x + current.width,
                          candidate.x, candidate.x + candidate.width);
        break;
    case NavigationDirection::Down:
        inDirection = dy > 0.5F;
        primary = dy;
        perpendicular = std::abs(dx);
        inBeam = Overlaps(current.x, current.x + current.width,
                          candidate.x, candidate.x + candidate.width);
        break;
    case NavigationDirection::None:
        break;
    }
    return {inDirection, {!inBeam, primary, perpendicular, std::move(id)}};
}

declarative::ScrollAxis DirectionAxis(
    const NavigationDirection direction) noexcept {
    switch (direction) {
    case NavigationDirection::Left:
    case NavigationDirection::Right:
        return declarative::ScrollAxis::Horizontal;
    case NavigationDirection::Up:
    case NavigationDirection::Down:
        return declarative::ScrollAxis::Vertical;
    case NavigationDirection::None:
        return declarative::ScrollAxis::None;
    }
    return declarative::ScrollAxis::None;
}

bool ScrollMatchesAxis(
    const WidgetNode& node,
    const declarative::ScrollAxis axis) noexcept {
    return node.kind == L"scroll" &&
        ((axis == declarative::ScrollAxis::Horizontal &&
          node.scrollAxis == L"horizontal") ||
         (axis == declarative::ScrollAxis::Vertical &&
          node.scrollAxis == L"vertical"));
}

bool IsResponsiveGrid(const WidgetNode& node) noexcept {
    return node.kind == L"grid" && node.gridMinimumColumnWidth.has_value();
}

template <typename IncludeCandidate>
std::optional<std::wstring> FindGeometricFocusTargetImpl(
    const std::wstring_view currentId,
    const NavigationDirection direction,
    const RenderResult& renderResult,
    const std::vector<GeometricFocusGroupCandidate>& groups,
    IncludeCandidate&& includeCandidate) {
    if (direction == NavigationDirection::None) return std::nullopt;
    const auto& geometry = renderResult.navigationRects.empty()
        ? renderResult.focusRects
        : renderResult.navigationRects;
    const auto currentEntry = geometry.find(currentId);
    if (currentEntry == geometry.end()) return std::nullopt;
    const auto& current = currentEntry->second;
    const auto currentScopeEntry = renderResult.focusScopes.find(currentId);
    const std::wstring_view currentScope =
        currentScopeEntry == renderResult.focusScopes.end()
        ? std::wstring_view{}
        : std::wstring_view(currentScopeEntry->second);

    std::optional<std::wstring> best;
    auto bestScore = std::tuple{
        true,
        std::numeric_limits<float>::infinity(),
        std::numeric_limits<float>::infinity(),
        std::wstring{}};
    for (const auto& [id, candidate] : geometry) {
        if (id == currentId || !includeCandidate(id) ||
            !IsEnabledFocusTarget(id, renderResult) ||
            std::ranges::any_of(groups, [&](const auto& group) {
                return ContainsFocusId(group, id);
            })) {
            continue;
        }
        const auto candidateScopeEntry = renderResult.focusScopes.find(id);
        const std::wstring_view candidateScope =
            candidateScopeEntry == renderResult.focusScopes.end()
            ? std::wstring_view{}
            : std::wstring_view(candidateScopeEntry->second);
        if (candidateScope != currentScope) continue;
        const auto score = ScoreGeometricCandidate(
            current, candidate, direction, id);
        if (!score.inDirection) continue;
        if (score.value < bestScore) {
            bestScore = score.value;
            best = id;
        }
    }
    for (const auto& group : groups) {
        if (!group.bounds || !includeCandidate(group.groupId)) continue;
        const auto score = ScoreGeometricCandidate(
            current, *group.bounds, direction, group.groupId);
        if (score.inDirection && score.value < bestScore) {
            bestScore = score.value;
            best = group.groupId;
        }
    }
    return best;
}

void CollectScrollPaginationActions(
    const WidgetNode& node,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult,
    std::vector<ScrollPaginationAction>& actions) {
    for (const auto& child : node.children) {
        CollectScrollPaginationActions(
            child, activeScopeId, renderResult, actions);
    }
    if (node.kind != L"scroll" || node.scrollPaginationThreshold == 0 ||
        node.children.empty()) {
        return;
    }
    const auto viewport = renderResult.scrollViewports.find(node.id);
    if (viewport == renderResult.scrollViewports.end() ||
        viewport->second.axis == declarative::ScrollAxis::None ||
        viewport->second.rect.width <= 0.0F ||
        viewport->second.rect.height <= 0.0F) {
        return;
    }

    std::vector<const WidgetNode*> items;
    if (!node.collectionAnchorKey.empty()) {
        CollectCollectionItems(node, node, items);
    } else {
        items.reserve(node.children.size());
        for (const auto& child : node.children) items.push_back(&child);
    }
    if (items.empty()) return;

    std::optional<std::size_t> firstVisible;
    std::optional<std::size_t> lastVisible;
    for (std::size_t index = 0; index < items.size(); ++index) {
        const auto bounds = CollectionItemBounds(
            *items[index], activeScopeId, renderResult);
        if (!bounds || !Intersects(*bounds, viewport->second.rect)) continue;
        if (!firstVisible) firstVisible = index;
        lastVisible = index;
    }
    if (!firstVisible || !lastVisible) return;

    const auto append = [&](const ScrollPaginationEdge edge,
                            const std::wstring& actionId,
                            const std::size_t boundaryIndex) {
        if (actionId.empty()) return;
        const auto& boundary = *items[boundaryIndex];
        actions.push_back(ScrollPaginationAction{
            node.id,
            actionId,
            node.id,
            boundary.collectionItemKey.empty()
                ? boundary.id
                : boundary.collectionItemKey,
            node.collectionAnchorKey,
            viewport->second.axis,
            edge,
            *firstVisible,
            *lastVisible,
            items.size(),
        });
        actions.back().collectionGeneration = node.collectionGeneration;
        if (edge == ScrollPaginationEdge::After && *firstVisible == 0 && *lastVisible + 1 == items.size()) {
            const auto last = CollectionItemBounds(*items.back(), activeScopeId, renderResult);
            const auto& rect = viewport->second.rect;
            if (last) actions.back().viewportUnderfilled = viewport->second.axis == declarative::ScrollAxis::Vertical
                ? last->y + last->height < rect.y + rect.height - 1.0F
                : last->x + last->width < rect.x + rect.width - 1.0F;
        }
        for (auto index = *firstVisible; index <= *lastVisible; ++index) {
            if (node.collectionStartIndex && !items[index]->collectionItemKey.empty())
                actions.back().visibleCollectionKeys.push_back(items[index]->collectionItemKey);
        }
    };
    bool nearBefore = *firstVisible < node.scrollPaginationThreshold;
    bool nearAfter = items.size() - *lastVisible <= node.scrollPaginationThreshold;
    if (node.collectionStartIndex) {
        const auto first = CollectionItemBounds(*items.front(), activeScopeId, renderResult);
        const auto last = CollectionItemBounds(*items.back(), activeScopeId, renderResult);
        const auto& rect = viewport->second.rect;
        const bool vertical = viewport->second.axis == declarative::ScrollAxis::Vertical;
        const float start = vertical ? rect.y : rect.x;
        const float extent = vertical ? rect.height : rect.width;
        // Two measured viewports provide lead time for remote cursor requests,
        // independent of tile size, grid columns, DPI, and transport page size.
        const float lead = extent * 2.0F;
        if (first) nearBefore = nearBefore || start - (vertical ? first->y : first->x) <= lead;
        if (last) nearAfter = nearAfter ||
            (vertical ? last->y + last->height : last->x + last->width) - start - extent <= lead;
    }
    if (nearBefore) {
        append(ScrollPaginationEdge::Before,
               node.scrollNearStartActionId, 0);
    }
    if (nearAfter) {
        append(ScrollPaginationEdge::After,
               node.scrollNearEndActionId, items.size() - 1);
    }
}

} // namespace

FocusedScrollResolution ResolveFocusedScrollOwner(
    const WidgetNode& root,
    const std::wstring_view focusedElementId,
    const declarative::ScrollAxis axis,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    if (focusedElementId.empty() || axis == declarative::ScrollAxis::None)
        return {FocusedScrollResolutionDisposition::MissingFocus};
    std::vector<const WidgetNode*> path;
    if (!FindNodePath(root, focusedElementId, path))
        return {FocusedScrollResolutionDisposition::MissingFocus};
    const auto scope = renderResult.focusScopes.find(focusedElementId);
    if (scope == renderResult.focusScopes.end() ||
        scope->second != activeScopeId) {
        return {FocusedScrollResolutionDisposition::ScopeMismatch};
    }

    bool foundSemanticScroll{};
    for (auto item = path.rbegin(); item != path.rend(); ++item) {
        if (!ScrollMatchesAxis(**item, axis)) continue;
        foundSemanticScroll = true;
        const auto viewport = renderResult.scrollViewports.find((*item)->id);
        if (viewport == renderResult.scrollViewports.end() ||
            viewport->second.axis != axis ||
            viewport->second.rect.width <= 0.0F ||
            viewport->second.rect.height <= 0.0F) {
            continue;
        }
        return {
            FocusedScrollResolutionDisposition::Resolved,
            (*item)->id,
            axis,
        };
    }
    return {
        foundSemanticScroll
            ? FocusedScrollResolutionDisposition::StaleGeometry
            : FocusedScrollResolutionDisposition::NoEligibleScroll,
    };
}

bool IsExactScrollAuthorityCurrent(
    const WidgetNode& root,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis,
    const RenderResult& renderResult) noexcept {
    if (scrollId.empty() || axis == declarative::ScrollAxis::None)
        return false;
    const auto* scroll = FindNode(root, scrollId);
    const auto viewport = renderResult.scrollViewports.find(scrollId);
    return scroll && scroll->kind == L"scroll" &&
        viewport != renderResult.scrollViewports.end() &&
        viewport->second.axis == axis &&
        viewport->second.rect.width > 0.0F &&
        viewport->second.rect.height > 0.0F;
}

std::vector<ScrollPaginationAction> FindScrollPaginationActions(
    const WidgetNode& root,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    std::vector<ScrollPaginationAction> actions;
    CollectScrollPaginationActions(
        root, activeScopeId, renderResult, actions);
    return actions;
}

std::optional<PointerHitTarget> FindPointerHitTarget(
    const float x,
    const float y,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    // Regions follow paint/tree order; reverse search gives overlapping
    // descendants precedence over the surface painted beneath them.
    for (auto region = renderResult.hitRegions.rbegin();
         region != renderResult.hitRegions.rend(); ++region) {
        const auto scope = renderResult.focusScopes.find(region->nodeId);
        if (scope == renderResult.focusScopes.end() ||
            scope->second != activeScopeId) continue;
        const auto& rect = region->rect;
        if (rect.width <= 0.0F || rect.height <= 0.0F ||
            x < rect.x || y < rect.y ||
            x >= rect.x + rect.width || y >= rect.y + rect.height) continue;
        return PointerHitTarget{region->nodeId, region->enabled};
    }
    return std::nullopt;
}

bool IsEnabledFocusTarget(
    const std::wstring_view id,
    const RenderResult& renderResult) noexcept {
    const auto enabled = renderResult.navigationEnabled.find(id);
    if (enabled != renderResult.navigationEnabled.end()) {
        return enabled->second &&
            (renderResult.focusRects.contains(id) ||
             renderResult.revealableFocusIds.contains(id));
    }
    // Compatibility for synthetic/older render results used by host seams.
    const auto region = std::find_if(
        renderResult.hitRegions.begin(), renderResult.hitRegions.end(),
        [id](const RenderHitRegion& item) { return item.nodeId == id; });
    return renderResult.focusRects.contains(id) &&
        region != renderResult.hitRegions.end() && region->enabled;
}

std::optional<std::wstring> ResolveResponsiveFocusPersistenceTarget(
    const WidgetSnapshot& snapshot,
    const std::wstring_view preferredId,
    const std::wstring_view activeScopeId,
    const bool compactMode) {
    if (preferredId.empty() || activeScopeId.empty()) return std::nullopt;

    struct Candidate final {
        const WidgetNode* node{};
        std::wstring_view scope;
    };
    const WidgetNode* preferred{};
    std::wstring_view preferredScope;
    bool preferredVisible{};
    std::vector<Candidate> visibleCandidates;
    const auto visit = [&](const auto& self,
                           const WidgetNode& node,
                           const std::wstring_view inheritedScope,
                           const bool ancestorsVisible) -> void {
        const std::wstring_view scope = node.inputScopeId.empty()
            ? inheritedScope
            : std::wstring_view(node.inputScopeId);
        const bool visible = ancestorsVisible && IsResponsiveVisible(node, compactMode);
        if (node.id == preferredId) {
            preferred = &node;
            preferredScope = scope;
            preferredVisible = visible;
        }
        if (visible && scope == activeScopeId && IsFocusableNode(node) &&
            !node.focusPersistenceId.empty()) {
            visibleCandidates.push_back({&node, scope});
        }
        for (const auto& child : node.children) self(self, child, scope, visible);
    };
    const std::wstring_view rootScope = snapshot.root.inputScopeId.empty()
        ? std::wstring_view(snapshot.root.id)
        : std::wstring_view(snapshot.root.inputScopeId);
    visit(visit, snapshot.root, rootScope, true);

    if (!preferred || preferredVisible || preferredScope != activeScopeId ||
        preferred->focusPersistenceId.empty()) {
        return std::nullopt;
    }
    std::optional<std::wstring> equivalent;
    for (const auto& candidate : visibleCandidates) {
        if (candidate.node->id == preferredId ||
            candidate.node->focusPersistenceId != preferred->focusPersistenceId) {
            continue;
        }
        if (equivalent) return std::nullopt;
        equivalent = candidate.node->id;
    }
    return equivalent;
}

std::optional<std::wstring> ResolveVisibleFocusTarget(
    const std::wstring_view preferredId,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    const auto isVisibleInScope = [&](const std::wstring_view id) {
        const auto rect = renderResult.focusRects.find(id);
        const auto visible = rect != renderResult.focusRects.end() &&
            rect->second.width > 0.0F && rect->second.height > 0.0F;
        if ((!visible && !renderResult.revealableFocusIds.contains(id)) ||
            !IsEnabledFocusTarget(id, renderResult)) {
            return false;
        }
        const auto scope = renderResult.focusScopes.find(id);
        return scope != renderResult.focusScopes.end() &&
               scope->second == activeScopeId;
    };

    if (!preferredId.empty() && isVisibleInScope(preferredId)) {
        return std::wstring{preferredId};
    }
    // Hit regions are emitted during the renderer's depth-first tree walk,
    // unlike the unordered lookup maps. Using them preserves author order and
    // makes resize recovery deterministic across processes and builds.
    for (const auto& region : renderResult.hitRegions) {
        if (isVisibleInScope(region.nodeId)) return region.nodeId;
    }
    return std::nullopt;
}

std::optional<std::wstring> FindGeometricFocusTarget(
    const std::wstring_view currentId,
    const NavigationDirection direction,
    const RenderResult& renderResult) {
    return FindGeometricFocusTarget(currentId, direction, renderResult, {});
}

std::optional<std::wstring> FindGeometricFocusTarget(
    const std::wstring_view currentId,
    const NavigationDirection direction,
    const RenderResult& renderResult,
    const std::vector<GeometricFocusGroupCandidate>& groups) {
    return FindGeometricFocusTargetImpl(
        currentId, direction, renderResult, groups,
        [](const std::wstring_view) { return true; });
}

DirectionalSubtreeFocusResolution FindDirectionalFocusTargetInOwningSubtrees(
    const WidgetNode& root,
    const std::wstring_view currentId,
    const NavigationDirection direction,
    const RenderResult& renderResult,
    const std::vector<GeometricFocusGroupCandidate>& groups) {
    const auto axis = DirectionAxis(direction);
    if (currentId.empty() || axis == declarative::ScrollAxis::None) return {};
    std::vector<const WidgetNode*> path;
    if (!FindNodePath(root, currentId, path)) return {{}, true};

    for (auto item = path.rbegin(); item != path.rend(); ++item) {
        const auto* owner = *item;
        const bool responsiveGrid = IsResponsiveGrid(*owner);
        const bool matchingScroll = ScrollMatchesAxis(*owner, axis);
        if (!responsiveGrid && !matchingScroll) continue;
        if (matchingScroll) {
            const auto viewport = renderResult.scrollViewports.find(owner->id);
            if (viewport == renderResult.scrollViewports.end() ||
                viewport->second.axis != axis ||
                viewport->second.rect.width <= 0.0F ||
                viewport->second.rect.height <= 0.0F) {
                return {{}, true};
            }
        }
        const auto target = FindGeometricFocusTargetImpl(
            currentId, direction, renderResult, groups,
            [owner](const std::wstring_view candidateId) {
                return FindNode(*owner, candidateId) != nullptr;
            });
        if (target) return {target, false};
    }
    return {};
}

DirectionalScrollExitDisposition ClassifyDirectionalScrollExit(
    const WidgetNode& root,
    const std::wstring_view currentId,
    const std::wstring_view targetId,
    const NavigationDirection direction,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    const auto axis = DirectionAxis(direction);
    if (axis == declarative::ScrollAxis::None) {
        return DirectionalScrollExitDisposition::NoOwner;
    }
    const auto owner = ResolveFocusedScrollOwner(
        root, currentId, axis, activeScopeId, renderResult);
    switch (owner.disposition) {
    case FocusedScrollResolutionDisposition::NoEligibleScroll:
        return DirectionalScrollExitDisposition::NoOwner;
    case FocusedScrollResolutionDisposition::Resolved:
        break;
    case FocusedScrollResolutionDisposition::MissingFocus:
    case FocusedScrollResolutionDisposition::ScopeMismatch:
    case FocusedScrollResolutionDisposition::StaleGeometry:
        return DirectionalScrollExitDisposition::StaleAuthority;
    }
    const auto* scroll = FindNode(root, owner.scrollId);
    if (!scroll) return DirectionalScrollExitDisposition::StaleAuthority;
    return FindNode(*scroll, targetId)
        ? DirectionalScrollExitDisposition::InsideOwner
        : DirectionalScrollExitDisposition::OutsideOwner;
}

std::vector<GeometricFocusGroupCandidate> FindExternalFocusGroupCandidates(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult) {
    std::vector<GeometricFocusGroupCandidate> groups;
    const std::wstring_view rootScope = snapshot.root.inputScopeId.empty()
        ? std::wstring_view(snapshot.root.id)
        : std::wstring_view(snapshot.root.inputScopeId);
    CollectOutermostFocusGroups(
        snapshot.root, snapshot.activeInputScopeId, rootScope,
        renderResult, groups);
    std::erase_if(groups, [&](const auto& group) {
        return ContainsFocusId(group, focusedElementId);
    });
    return groups;
}

std::optional<std::wstring> FindFreeScrollReentryTarget(
    const WidgetNode& root,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis,
    const std::wstring_view activeScopeId,
    const RenderResult& renderResult) {
    if (axis == declarative::ScrollAxis::None || scrollId.empty())
        return std::nullopt;
    const auto viewport = renderResult.scrollViewports.find(scrollId);
    const auto* scroll = FindNode(root, scrollId);
    if (!scroll || viewport == renderResult.scrollViewports.end() ||
        viewport->second.axis != axis || viewport->second.rect.width <= 0.0F ||
        viewport->second.rect.height <= 0.0F) {
        return std::nullopt;
    }

    constexpr float epsilon = 0.5F;
    const auto& clip = viewport->second.rect;
    struct Candidate final {
        std::wstring id;
        Rect rect;
        bool fullyVisible{};
    };
    std::vector<Candidate> candidates;
    const auto collect = [&](const auto& self, const WidgetNode& node) -> void {
        const auto geometry = renderResult.navigationRects.find(node.id);
        const auto scope = renderResult.focusScopes.find(node.id);
        const auto hit = std::ranges::find_if(
            renderResult.hitRegions,
            [&](const RenderHitRegion& region) {
                return region.nodeId == node.id;
            });
        if (geometry != renderResult.navigationRects.end() &&
            scope != renderResult.focusScopes.end() &&
            scope->second == activeScopeId &&
            hit != renderResult.hitRegions.end() && hit->enabled) {
            const auto& rect = geometry->second;
            const bool intersects = rect.x < clip.x + clip.width - epsilon &&
                rect.x + rect.width > clip.x + epsilon &&
                rect.y < clip.y + clip.height - epsilon &&
                rect.y + rect.height > clip.y + epsilon;
            if (intersects) {
                const bool fullyVisible =
                    rect.x >= clip.x - epsilon &&
                    rect.y >= clip.y - epsilon &&
                    rect.x + rect.width <= clip.x + clip.width + epsilon &&
                    rect.y + rect.height <= clip.y + clip.height + epsilon;
                candidates.push_back({node.id, rect, fullyVisible});
            }
        }
        for (const auto& child : node.children) self(self, child);
    };
    collect(collect, *scroll);
    const bool hasFullyVisible = std::ranges::any_of(
        candidates, [](const Candidate& candidate) {
            return candidate.fullyVisible;
        });
    std::erase_if(candidates, [&](const Candidate& candidate) {
        return hasFullyVisible && !candidate.fullyVisible;
    });
    if (candidates.empty()) return std::nullopt;
    std::ranges::sort(candidates, [axis](const Candidate& left,
                                        const Candidate& right) {
        const auto primary = [axis](const Rect& rect) {
            return axis == declarative::ScrollAxis::Vertical ? rect.y : rect.x;
        };
        const auto secondary = [axis](const Rect& rect) {
            return axis == declarative::ScrollAxis::Vertical ? rect.x : rect.y;
        };
        return std::tuple{
                   primary(left.rect), secondary(left.rect), left.id} <
            std::tuple{
                   primary(right.rect), secondary(right.rect), right.id};
    });
    return candidates.front().id;
}

std::optional<std::wstring> ResolveContextMenuSource(
    const WidgetSnapshot& snapshot, const std::wstring_view focusedId,
    const std::wstring_view button, const RenderResult& renderResult) {
    const WidgetNode* focused{};
    const WidgetNode* container{};
    bool ambiguous{};
    const auto visit = [&](const auto& self, const WidgetNode& node,
                           const std::wstring_view inheritedScope) -> void {
        const std::wstring_view scope = node.inputScopeId.empty() ? inheritedScope : std::wstring_view{node.inputScopeId};
        if (node.isDisabled || node.isBusy) return;
        if (scope == snapshot.activeInputScopeId && !node.isDisabled && !node.isBusy && !node.contextActions.empty()) {
            const std::wstring_view trigger = node.contextMenuButton.empty() ? std::wstring_view{L"menu"} : std::wstring_view{node.contextMenuButton};
            if (trigger == button) {
                if (node.kind == L"actionSurface" && node.id == focusedId && renderResult.focusRects.contains(node.id))
                    focused = &node;
                else if (node.kind != L"actionSurface" && !node.contextMenuButton.empty() && renderResult.contextMenuRects.contains(node.id)) {
                    ambiguous = ambiguous || container != nullptr;
                    container = &node;
                }
            }
        }
        for (const auto& child : node.children) self(self, child, scope);
    };
    visit(visit, snapshot.root, snapshot.root.inputScopeId.empty() ? snapshot.root.id : snapshot.root.inputScopeId);
    if (focused) return focused->id;
    if (container && !ambiguous) return container->id;
    return std::nullopt;
}

} // namespace widgetrail::input
