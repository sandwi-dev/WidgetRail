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
            edge,
            *firstVisible,
            *lastVisible,
            items.size(),
        });
    };
    if (*firstVisible < node.scrollPaginationThreshold) {
        append(ScrollPaginationEdge::Before,
               node.scrollNearStartActionId, 0);
    }
    if (items.size() - *lastVisible <= node.scrollPaginationThreshold) {
        append(ScrollPaginationEdge::After,
               node.scrollNearEndActionId, items.size() - 1);
    }
}

} // namespace

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
    if (direction == NavigationDirection::None) return std::nullopt;
    const auto& geometry = renderResult.navigationRects.empty()
        ? renderResult.focusRects
        : renderResult.navigationRects;
    const auto currentEntry = geometry.find(currentId);
    if (currentEntry == geometry.end()) return std::nullopt;
    const auto& current = currentEntry->second;
    const auto currentScopeEntry = renderResult.focusScopes.find(currentId);
    const std::wstring_view currentScope = currentScopeEntry == renderResult.focusScopes.end()
        ? std::wstring_view{}
        : std::wstring_view(currentScopeEntry->second);

    std::optional<std::wstring> best;
    auto bestScore = std::tuple{true,
        std::numeric_limits<float>::infinity(),
        std::numeric_limits<float>::infinity(),
        std::wstring{}};
    for (const auto& [id, candidate] : geometry) {
        if (id == currentId || !IsEnabledFocusTarget(id, renderResult)) continue;
        const auto candidateScopeEntry = renderResult.focusScopes.find(id);
        const std::wstring_view candidateScope =
            candidateScopeEntry == renderResult.focusScopes.end()
                ? std::wstring_view{}
                : std::wstring_view(candidateScopeEntry->second);
        if (candidateScope != currentScope) continue;
        const float dx = CenterX(candidate) - CenterX(current);
        const float dy = CenterY(candidate) - CenterY(current);
        float primary{};
        float perpendicular{};
        bool inDirection{};
        bool inBeam{};
        switch (direction) {
        case NavigationDirection::Left:
            inDirection = dx < -0.5F;
            primary = -dx;
            perpendicular = std::abs(dy);
            inBeam = Overlaps(current.y, current.y + current.height,
                              candidate.y, candidate.y + candidate.height);
            break;
        case NavigationDirection::Right:
            inDirection = dx > 0.5F;
            primary = dx;
            perpendicular = std::abs(dy);
            inBeam = Overlaps(current.y, current.y + current.height,
                              candidate.y, candidate.y + candidate.height);
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
        default: break;
        }
        if (!inDirection) continue;
        // Lexicographic scoring makes behavior deterministic: controls in the
        // same row/column win, then nearest forward distance, then lateral
        // distance, then stable ID for exact geometric ties.
        const auto score = std::tuple{!inBeam, primary, perpendicular, id};
        if (score < bestScore) {
            bestScore = score;
            best = id;
        }
    }
    return best;
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

} // namespace widgetrail::input
