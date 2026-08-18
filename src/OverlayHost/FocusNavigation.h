#pragma once

#include "ControllerNavigation.h"
#include "DeclarativeRenderer.h"

#include <optional>
#include <string>
#include <string_view>

namespace widgetrail {
struct WidgetNode;
struct WidgetSnapshot;
}

namespace widgetrail::input {

struct PointerHitTarget final {
    std::wstring id;
    bool enabled{};
};

struct ScrollPaginationAction final {
    std::wstring actionId;
    std::wstring sourceElementId;
};

/// Resolves the configured pagination action for a focused descendant that is
/// already inside a scroll viewport's leading or trailing threshold. This is
/// independent of whether ordinary focus navigation found another target, so
/// the host can paginate before falling through to its root/tray boundary.
[[nodiscard]] std::optional<ScrollPaginationAction> FindScrollPaginationAction(
    const WidgetNode& root,
    std::wstring_view focusedId,
    NavigationDirection direction);

/// Resolves the topmost visible pointer region in the active input scope.
/// Disabled/busy controls remain selectable but report enabled=false so the
/// host can move focus without invoking their action.
[[nodiscard]] std::optional<PointerHitTarget> FindPointerHitTarget(
    float x,
    float y,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

/// Finds the closest enabled focus target in a direction. Candidates whose
/// perpendicular span overlaps the current control are preferred, producing
/// stable row/column behavior before falling back across asymmetric layouts.
[[nodiscard]] std::optional<std::wstring> FindGeometricFocusTarget(
    std::wstring_view currentId,
    NavigationDirection direction,
    const RenderResult& renderResult);

[[nodiscard]] bool IsEnabledFocusTarget(
    std::wstring_view id,
    const RenderResult& renderResult) noexcept;

/// Resolves one explicit protocol-v13 focus identity before paint when a
/// responsive branch hides the preferred presentation. Omitted identities,
/// cross-scope matches, and ambiguous active matches fail closed.
[[nodiscard]] std::optional<std::wstring> ResolveResponsiveFocusPersistenceTarget(
    const WidgetSnapshot& snapshot,
    std::wstring_view preferredId,
    std::wstring_view activeScopeId,
    bool compactMode);

/// Keeps controller focus attached to geometry that is actually visible after
/// responsive layout. The preferred target wins when it remains visible or
/// can be revealed by a semantic scroll container and is enabled in the active
/// input scope; otherwise the first visible target in deterministic render/tree
/// order is returned. Explicit cross-presentation persistence is resolved from
/// the immutable snapshot before paint. A surface with no visible controls has
/// no resolved target.
[[nodiscard]] std::optional<std::wstring> ResolveVisibleFocusTarget(
    std::wstring_view preferredId,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

} // namespace widgetrail::input
