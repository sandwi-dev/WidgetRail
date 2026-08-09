#include "FocusNavigation.h"
#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <tuple>
#include <vector>

namespace gba::input {
namespace {

using declarative::Rect;

float CenterX(const Rect& rect) noexcept { return rect.x + rect.width * 0.5F; }
float CenterY(const Rect& rect) noexcept { return rect.y + rect.height * 0.5F; }

bool Overlaps(const float a0, const float a1, const float b0, const float b1) noexcept {
    return std::min(a1, b1) >= std::max(a0, b0);
}

bool ContainsWidgetNode(
    const WidgetNode& node,
    const std::wstring_view nodeId) noexcept {
    if (node.id == nodeId) return true;
    return std::ranges::any_of(node.children, [&](const WidgetNode& child) {
        return ContainsWidgetNode(child, nodeId);
    });
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

} // namespace

std::optional<ScrollPaginationAction> FindScrollPaginationAction(
    const WidgetNode& root,
    const std::wstring_view focusedId,
    const NavigationDirection direction) {
    for (const auto& child : root.children) {
        if (const auto nested = FindScrollPaginationAction(
                child, focusedId, direction))
            return nested;
    }
    if (root.kind != L"scroll" || root.scrollPaginationThreshold == 0 ||
        root.children.empty())
        return std::nullopt;
    const bool towardStart =
        (root.scrollAxis == L"vertical" && direction == NavigationDirection::Up) ||
        (root.scrollAxis == L"horizontal" && direction == NavigationDirection::Left);
    const bool towardEnd =
        (root.scrollAxis == L"vertical" && direction == NavigationDirection::Down) ||
        (root.scrollAxis == L"horizontal" && direction == NavigationDirection::Right);
    if (!towardStart && !towardEnd) return std::nullopt;
    for (std::size_t index = 0; index < root.children.size(); ++index) {
        if (!ContainsWidgetNode(root.children[index], focusedId)) continue;
        if (towardStart && !root.scrollNearStartActionId.empty() &&
            index < root.scrollPaginationThreshold)
            return ScrollPaginationAction{root.scrollNearStartActionId, root.id};
        if (towardEnd && !root.scrollNearEndActionId.empty() &&
            root.children.size() - index <= root.scrollPaginationThreshold)
            return ScrollPaginationAction{root.scrollNearEndActionId, root.id};
        break;
    }
    return std::nullopt;
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

} // namespace gba::input
