#include "FocusNavigation.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <tuple>

namespace gba::input {
namespace {

using declarative::Rect;

float CenterX(const Rect& rect) noexcept { return rect.x + rect.width * 0.5F; }
float CenterY(const Rect& rect) noexcept { return rect.y + rect.height * 0.5F; }

bool Overlaps(const float a0, const float a1, const float b0, const float b1) noexcept {
    return std::min(a1, b1) >= std::max(a0, b0);
}

} // namespace

bool IsEnabledFocusTarget(
    const std::wstring_view id,
    const RenderResult& renderResult) noexcept {
    const auto region = std::find_if(
        renderResult.hitRegions.begin(), renderResult.hitRegions.end(),
        [id](const RenderHitRegion& item) { return item.nodeId == id; });
    return region != renderResult.hitRegions.end() && region->enabled;
}

std::optional<std::wstring> FindGeometricFocusTarget(
    const std::wstring_view currentId,
    const NavigationDirection direction,
    const RenderResult& renderResult) {
    if (direction == NavigationDirection::None) return std::nullopt;
    const auto currentEntry = renderResult.focusRects.find(currentId);
    if (currentEntry == renderResult.focusRects.end()) return std::nullopt;
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
    for (const auto& [id, candidate] : renderResult.focusRects) {
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
