#pragma once

#include "ControllerNavigation.h"
#include "DeclarativeRenderer.h"

#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail {
struct WidgetNode;
struct WidgetSnapshot;
}

namespace widgetrail::input {

struct PointerHitTarget final {
    std::wstring id;
    bool enabled{};
};

/// One non-focusable remembered-child group projected into geometric
/// navigation. Bounds exist only when at least one descendant is currently
/// visible and navigable. All in-scope descendant focus IDs remain listed so
/// hidden or revealable leaves cannot bypass their external-entry owner.
struct GeometricFocusGroupCandidate final {
    std::wstring groupId;
    std::optional<declarative::Rect> bounds;
    std::vector<std::wstring> descendantFocusIds;
};

/// One ordered geometric lookup inside the focused node's semantic layout
/// ancestry. ResponsiveGrid and matching-axis Scroll owners are considered
/// from nearest to outermost before ordinary surface-wide geometry. A stale
/// rendered Scroll owner fails closed instead of exposing controls outside it.
struct DirectionalSubtreeFocusResolution final {
    std::optional<std::wstring> target;
    bool staleAuthority{};
};

enum class DirectionalScrollExitDisposition {
    NoOwner,
    InsideOwner,
    OutsideOwner,
    StaleAuthority,
};

enum class ScrollPaginationEdge {
    Before,
    After,
};

struct ScrollPaginationAction final {
    std::wstring scrollId;
    std::wstring actionId;
    std::wstring sourceElementId;
    std::wstring edgeKey;
    std::wstring anchorKey;
    declarative::ScrollAxis axis{declarative::ScrollAxis::None};
    ScrollPaginationEdge edge{ScrollPaginationEdge::Before};
    std::size_t firstVisibleIndex{};
    std::size_t lastVisibleIndex{};
    std::size_t itemCount{};
};

enum class FocusedScrollResolutionDisposition {
    Resolved,
    MissingFocus,
    ScopeMismatch,
    NoEligibleScroll,
    StaleGeometry,
};

struct FocusedScrollResolution final {
    FocusedScrollResolutionDisposition disposition{
        FocusedScrollResolutionDisposition::MissingFocus};
    std::wstring scrollId;
    declarative::ScrollAxis axis{declarative::ScrollAxis::None};
};

/// Resolves the deepest rendered Scroll ancestor of one exact focused node.
/// This is the shared owner lookup for vertical and horizontal free scrolling
/// and pagination intent; callers must not guess from axis alone.
[[nodiscard]] FocusedScrollResolution ResolveFocusedScrollOwner(
    const WidgetNode& root,
    std::wstring_view focusedElementId,
    declarative::ScrollAxis axis,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

/// Validates that a previously bound Scroll still exists in the current
/// semantic tree and the committed renderer geometry with the same axis.
[[nodiscard]] bool IsExactScrollAuthorityCurrent(
    const WidgetNode& root,
    std::wstring_view scrollId,
    declarative::ScrollAxis axis,
    const RenderResult& renderResult) noexcept;

/// Resolves pagination actions from each rendered Scroll viewport's visible
/// collection range. Focus identity and input source are deliberately absent:
/// right-stick, pointer/UIA, focus-follow, and retained-anchor movement all
/// converge on the same committed renderer geometry.
[[nodiscard]] std::vector<ScrollPaginationAction> FindScrollPaginationActions(
    const WidgetNode& root,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

/// Resolves the topmost visible pointer region in the active input scope.
/// Disabled/busy controls remain selectable but report enabled=false so the
/// host can move focus without invoking their action.
[[nodiscard]] std::optional<PointerHitTarget> FindPointerHitTarget(
    float x,
    float y,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

/// Finds the closest enabled focus target in a direction. Left/Right requires
/// vertical overlap beyond subpixel edge contact, preventing jumps to another
/// row. Up/Down prefers horizontal overlap but permits diagonal fallback so
/// ragged grids and transitions between differently sized sections stay usable.
[[nodiscard]] std::optional<std::wstring> FindGeometricFocusTarget(
    std::wstring_view currentId,
    NavigationDirection direction,
    const RenderResult& renderResult);

/// Applies the ordinary geometric score to standalone controls plus external
/// remembered-child group candidates. Descendant leaves of supplied groups
/// are suppressed, even when clipped or revealable, so entry cannot bypass
/// remembered/initial group authority.
[[nodiscard]] std::optional<std::wstring> FindGeometricFocusTarget(
    std::wstring_view currentId,
    NavigationDirection direction,
    const RenderResult& renderResult,
    const std::vector<GeometricFocusGroupCandidate>& groups);

/// Searches the focused node's nearest ResponsiveGrid or matching-axis Scroll
/// subtree first, then each containing owner in order. Offscreen descendants
/// remain eligible only through the renderer's existing revealable authority.
[[nodiscard]] DirectionalSubtreeFocusResolution
FindDirectionalFocusTargetInOwningSubtrees(
    const WidgetNode& root,
    std::wstring_view currentId,
    NavigationDirection direction,
    const RenderResult& renderResult,
    const std::vector<GeometricFocusGroupCandidate>& groups);

/// Classifies whether a resolved surface-wide target would leave the focused
/// node's deepest matching-axis Scroll. Callers use OutsideOwner to admit the
/// existing pagination boundary before committing the external focus move.
[[nodiscard]] DirectionalScrollExitDisposition ClassifyDirectionalScrollExit(
    const WidgetNode& root,
    std::wstring_view currentId,
    std::wstring_view targetId,
    NavigationDirection direction,
    std::wstring_view activeScopeId,
    const RenderResult& renderResult);

/// Collects deterministic outermost remembered-child groups in the active
/// input scope. A group is represented by the union of its currently visible,
/// navigable descendants. If focus is already inside any such group, returns
/// no groups so internal navigation remains ordinary leaf-to-leaf geometry.
[[nodiscard]] std::vector<GeometricFocusGroupCandidate>
FindExternalFocusGroupCandidates(
    const WidgetSnapshot& snapshot,
    std::wstring_view focusedElementId,
    const RenderResult& renderResult);

/// Re-enters ordinary focus navigation after right-stick free scroll. The
/// exact scroll subtree and its real rendered viewport remain authoritative;
/// fully visible enabled descendants win before the first partial fallback.
[[nodiscard]] std::optional<std::wstring> FindFreeScrollReentryTarget(
    const WidgetNode& root,
    std::wstring_view scrollId,
    declarative::ScrollAxis axis,
    std::wstring_view activeScopeId,
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
