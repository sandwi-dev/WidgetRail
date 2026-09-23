#pragma once

#include "OverlayPosition.h"

#include <cstddef>
#include <cstdint>
#include <stdexcept>
#include <string_view>

namespace widgetrail {

struct OverlayPresentationExtent final {
    int widthDip{};
    int heightDip{};

    [[nodiscard]] friend constexpr bool operator==(
        const OverlayPresentationExtent&,
        const OverlayPresentationExtent&) noexcept = default;
};

enum class OverlayPresentationDirective {
    None,
    Hide,
    Repaint,
    Place,
};

enum class DisplayEnvironmentChange {
    Dpi,
    Topology,
    SystemSettings,
};

struct DisplayRefreshPlan final {
    bool recreateGraphics{};
    bool reapplyAppearance{};
    bool repositionWindows{};

    [[nodiscard]] friend constexpr bool operator==(
        const DisplayRefreshPlan&,
        const DisplayRefreshPlan&) noexcept = default;
};

// Pure policy for Win32 display notifications. Hidden windows resolve current
// state when next shown. Visible windows always re-read monitor/work-area/DPI;
// system settings additionally reapply accessibility and theme policy.
[[nodiscard]] constexpr DisplayRefreshPlan DecideDisplayRefresh(
    const bool visible,
    const DisplayEnvironmentChange change) noexcept {
    if (!visible) return {};
    return {true, change == DisplayEnvironmentChange::SystemSettings, true};
}

// Display migration is reported as a burst on real systems: one monitor move
// can synchronously produce DPI, topology, size, and work-area notifications.
// Applying each notification independently exposes intermediate geometry and
// repeatedly destroys the render target. This accumulator merges all pending
// work into one posted message-loop refresh without polling or dropping the
// stronger appearance refresh requested by WM_SETTINGCHANGE.
class DisplayRefreshAccumulator final {
public:
    // Returns true only when the caller must post a refresh message. Further
    // notifications merge into the already-posted unit of work.
    [[nodiscard]] bool Enqueue(
        bool visible,
        DisplayEnvironmentChange change) noexcept;

    // Consumes the merged plan and permits a later notification to schedule a
    // fresh message. A queued plan may safely be ignored if the overlay became
    // hidden before delivery; its next show resolves the environment afresh.
    [[nodiscard]] DisplayRefreshPlan Take() noexcept;

private:
    bool scheduled_{};
    DisplayRefreshPlan pending_{};
};

// Pure presentation policy shared by state transitions and asynchronous
// snapshot refreshes. A visible HWND only needs another placement pass when
// its requested logical extent or monitor target changed. Selection, focus,
// and content-only changes are repaints; repeatedly calling SetWindowPos with
// SWP_SHOWWINDOW for those changes causes unnecessary DWM/Direct2D churn.
[[nodiscard]] constexpr OverlayPresentationDirective DecideOverlayPresentation(
    const bool wasVisible,
    const bool isVisible,
    const OverlayPresentationExtent before,
    const OverlayPresentationExtent after,
    const bool targetChanged = false) noexcept {
    if (!isVisible) {
        return wasVisible
            ? OverlayPresentationDirective::Hide
            : OverlayPresentationDirective::None;
    }
    if (!wasVisible || targetChanged || before != after) {
        return OverlayPresentationDirective::Place;
    }
    return OverlayPresentationDirective::Repaint;
}

/// A resize or monitor move of an already visible HWND must commit its new
/// frame before returning to the message loop; otherwise DWM can briefly show
/// the newly exposed client strip. Initial show and repaint-only transitions
/// do not need this synchronous path.
[[nodiscard]] constexpr bool ShouldCommitVisiblePlacementSynchronously(
    const bool wasWindowVisible,
    const OverlayPresentationDirective directive) noexcept {
    return wasWindowVisible && directive == OverlayPresentationDirective::Place;
}

struct RenderTargetResizePlan final {
    bool resizeInPlace{};
    bool invalidate{};

    [[nodiscard]] friend constexpr bool operator==(
        const RenderTargetResizePlan&,
        const RenderTargetResizePlan&) noexcept = default;
};

struct CompositionGeometryPlan final {
    unsigned int retainedClipWidth{};
    unsigned int retainedClipHeight{};
    bool clipBeforeCommit{};
    bool placeAfterCommit{};

    [[nodiscard]] friend constexpr bool operator==(
        const CompositionGeometryPlan&,
        const CompositionGeometryPlan&) noexcept = default;
};

struct CompositionMotionPlan final {
    unsigned int containerWidth{};
    unsigned int containerHeight{};
    float scaleX{1.0F};
    float scaleY{1.0F};
    float offsetX{};
    float offsetY{};
    bool retainsTransparentContainer{};
    OverlayPosition position{OverlayPosition::Center};

    [[nodiscard]] friend constexpr bool operator==(
        const CompositionMotionPlan&,
        const CompositionMotionPlan&) noexcept = default;
};

struct CompositionChildCoordinateSpaces final {
    CompositionMotionPlan content;
    float chromeOffsetX{};
    float chromeOffsetY{};
    float guideOffsetX{};
    float guideOffsetY{};
    float trayOffsetX{};
    float trayOffsetY{};

    [[nodiscard]] friend constexpr bool operator==(
        const CompositionChildCoordinateSpaces&,
        const CompositionChildCoordinateSpaces&) noexcept = default;
};

struct CompositionPoint final {
    float x{};
    float y{};

    [[nodiscard]] friend constexpr bool operator==(
        const CompositionPoint&,
        const CompositionPoint&) noexcept = default;
};

/// The destination content owns the animated transform. Host chrome is placed
/// once at the destination's final bottom origin inside the union HWND.
[[nodiscard]] constexpr CompositionChildCoordinateSpaces
PlanCompositionChildCoordinates(
    const CompositionMotionPlan content,
    const unsigned int targetWidth,
    const unsigned int targetHeight) noexcept {
    if (content.containerWidth == 0 || content.containerHeight == 0 ||
        targetWidth == 0 || targetHeight == 0 ||
        targetWidth > content.containerWidth ||
        targetHeight > content.containerHeight) return {};
    return {
        content,
        (static_cast<float>(content.containerWidth) -
         static_cast<float>(targetWidth)) * HorizontalAnchor(content.position),
        static_cast<float>(content.containerHeight - targetHeight),
        (static_cast<float>(content.containerWidth) -
         static_cast<float>(targetWidth)) * HorizontalAnchor(content.position),
        static_cast<float>(content.containerHeight - targetHeight),
        (static_cast<float>(content.containerWidth) -
         static_cast<float>(targetWidth)) * HorizontalAnchor(content.position),
        static_cast<float>(content.containerHeight - targetHeight),
    };
}

/// Uses the destination HWND placement rather than half-width arithmetic when
/// the union container has an odd per-axis delta. This keeps retained chrome
/// on the same physical pixel before and after the HWND settles.
[[nodiscard]] constexpr CompositionChildCoordinateSpaces
PlanCompositionChildCoordinates(
    const CompositionMotionPlan content,
    const unsigned int targetWidth,
    const unsigned int targetHeight,
    const float destinationOffsetX,
    const float destinationOffsetY) noexcept {
    auto result = PlanCompositionChildCoordinates(
        content, targetWidth, targetHeight);
    if (result.content.containerWidth == 0 ||
        !(destinationOffsetX >= 0.0F) || !(destinationOffsetY >= 0.0F) ||
        destinationOffsetX + static_cast<float>(targetWidth) >
            static_cast<float>(content.containerWidth) ||
        destinationOffsetY + static_cast<float>(targetHeight) >
            static_cast<float>(content.containerHeight)) return {};
    result.chromeOffsetX = destinationOffsetX;
    result.chromeOffsetY = destinationOffsetY;
    result.guideOffsetX = destinationOffsetX;
    result.guideOffsetY = destinationOffsetY;
    result.trayOffsetX = destinationOffsetX;
    result.trayOffsetY = destinationOffsetY;
    return result;
}

/// Fixed chrome lives in a companion HWND. Its guide/tray offsets are resolved
/// from that real client origin after the animated content space is planned.
[[nodiscard]] constexpr CompositionChildCoordinateSpaces
ApplyFixedChromeChildOffsets(
    CompositionChildCoordinateSpaces result,
    const CompositionPoint guideOffset,
    const CompositionPoint trayOffset) noexcept {
    result.guideOffsetX = guideOffset.x;
    result.guideOffsetY = guideOffset.y;
    result.trayOffsetX = trayOffset.x;
    result.trayOffsetY = trayOffset.y;
    return result;
}

[[nodiscard]] constexpr CompositionPoint ProjectContentPoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint local) noexcept {
    return {
        spaces.content.offsetX + local.x * spaces.content.scaleX,
        spaces.content.offsetY + local.y * spaces.content.scaleY,
    };
}

[[nodiscard]] constexpr CompositionPoint InverseContentPoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint presented) noexcept {
    if (spaces.content.scaleX <= 0.0F || spaces.content.scaleY <= 0.0F) return {};
    return {
        (presented.x - spaces.content.offsetX) / spaces.content.scaleX,
        (presented.y - spaces.content.offsetY) / spaces.content.scaleY,
    };
}

[[nodiscard]] constexpr CompositionPoint ProjectChromePoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint local) noexcept {
    return {spaces.chromeOffsetX + local.x, spaces.chromeOffsetY + local.y};
}

[[nodiscard]] constexpr CompositionPoint InverseChromePoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint presented) noexcept {
    return {presented.x - spaces.chromeOffsetX,
            presented.y - spaces.chromeOffsetY};
}

[[nodiscard]] constexpr CompositionPoint ProjectGuidePoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint local) noexcept {
    return {spaces.guideOffsetX + local.x, spaces.guideOffsetY + local.y};
}

[[nodiscard]] constexpr CompositionPoint InverseGuidePoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint presented) noexcept {
    return {presented.x - spaces.guideOffsetX,
            presented.y - spaces.guideOffsetY};
}

[[nodiscard]] constexpr CompositionPoint ProjectTrayPoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint local) noexcept {
    return {spaces.trayOffsetX + local.x, spaces.trayOffsetY + local.y};
}

[[nodiscard]] constexpr CompositionPoint InverseTrayPoint(
    const CompositionChildCoordinateSpaces& spaces,
    const CompositionPoint presented) noexcept {
    return {presented.x - spaces.trayOffsetX,
            presented.y - spaces.trayOffsetY};
}

enum class CompositionVerticalAnchor {
    Center,
    Bottom,
};

/// One fully rendered destination surface is transformed inside a transparent
/// client container. The container is the per-axis union of source and target,
/// so no animation tick resizes the HWND or recreates/redraws the surface.
[[nodiscard]] constexpr CompositionMotionPlan PlanCompositionMotion(
    const unsigned int sourceWidth,
    const unsigned int sourceHeight,
    const unsigned int targetWidth,
    const unsigned int targetHeight,
    const float presentedWidth,
    const float presentedHeight,
    const CompositionVerticalAnchor verticalAnchor =
        CompositionVerticalAnchor::Center,
    const OverlayPosition position = OverlayPosition::Center) noexcept {
    if (sourceWidth == 0 || sourceHeight == 0 ||
        targetWidth == 0 || targetHeight == 0 ||
        presentedWidth <= 0.0F || presentedHeight <= 0.0F) return {};
    const auto containerWidth = sourceWidth > targetWidth ? sourceWidth : targetWidth;
    const auto containerHeight = sourceHeight > targetHeight ? sourceHeight : targetHeight;
    const float scaleX = presentedWidth / static_cast<float>(targetWidth);
    const float scaleY = presentedHeight / static_cast<float>(targetHeight);
    const float visualWidth = static_cast<float>(targetWidth) * scaleX;
    const float visualHeight = static_cast<float>(targetHeight) * scaleY;
    const float remainingHeight =
        static_cast<float>(containerHeight) - visualHeight;
    return {
        containerWidth,
        containerHeight,
        scaleX,
        scaleY,
        (static_cast<float>(containerWidth) - visualWidth) * HorizontalAnchor(position),
        verticalAnchor == CompositionVerticalAnchor::Bottom
            ? remainingHeight
            : remainingHeight * 0.5F,
        containerWidth != targetWidth || containerHeight != targetHeight,
        position,
    };
}

/// Geometry never reveals pixels that have not been committed. When replacing
/// a live surface, shrink either dimension before the visual commit and grow
/// or move only after the complete destination surface is committed.
[[nodiscard]] constexpr CompositionGeometryPlan PlanCompositionGeometry(
    const unsigned int currentWidth,
    const unsigned int currentHeight,
    const unsigned int targetWidth,
    const unsigned int targetHeight) noexcept {
    if (targetWidth == 0 || targetHeight == 0) return {};
    if (currentWidth == 0 || currentHeight == 0) {
        return {targetWidth, targetHeight, false, true};
    }
    const auto clipWidth = currentWidth < targetWidth ? currentWidth : targetWidth;
    const auto clipHeight = currentHeight < targetHeight ? currentHeight : targetHeight;
    return {
        clipWidth,
        clipHeight,
        clipWidth != currentWidth || clipHeight != currentHeight,
        currentWidth != targetWidth || currentHeight != targetHeight,
    };
}

/// A valid non-minimized WM_SIZE always invalidates viewport-derived geometry.
/// When an HWND target already exists, resize that target in place and rebuild
/// only its dependent resources. Destroying the target during a visible
/// SetWindowPos exposes an uncommitted color-key/back-buffer interval to DWM.
[[nodiscard]] constexpr RenderTargetResizePlan PlanRenderTargetResize(
    const bool hasRenderTarget,
    const bool minimized,
    const unsigned int width,
    const unsigned int height) noexcept {
    if (minimized || width == 0 || height == 0) return {};
    return {hasRenderTarget, true};
}

enum class WidgetExtentAuthority {
    AdmittedSnapshot,
    RetainedCommittedSurface,
    CompactStartupFallback,
};

/// A worker-start placeholder is presentation copy, never sizing authority.
/// Retain the last committed surface until the incoming immutable snapshot is
/// admitted. Compact fallback is reserved for the first widget open only.
[[nodiscard]] constexpr WidgetExtentAuthority ResolveWidgetExtentAuthority(
    const bool snapshotAdmitted,
    const bool committedSurfaceAvailable) noexcept {
    if (snapshotAdmitted) return WidgetExtentAuthority::AdmittedSnapshot;
    if (committedSurfaceAvailable)
        return WidgetExtentAuthority::RetainedCommittedSurface;
    return WidgetExtentAuthority::CompactStartupFallback;
}

enum class WidgetContentAuthority {
    AdmittedSnapshot,
    InertRetainedSnapshot,
    RetainedCommittedSnapshot,
    StableStartupStatus,
    Count,
};

inline constexpr std::size_t WidgetContentAuthorityCount =
    static_cast<std::size_t>(WidgetContentAuthority::Count);
static_assert(
    WidgetContentAuthorityCount == 4,
    "Update the exhaustive WidgetContentAuthority focus-source mapping");

struct WidgetContentFocusSources final {
    std::wstring_view current{};
    std::wstring_view refreshRetained{};
    std::wstring_view retainedCommitted{};
};

/// Selects rendered focus from the exact content owner. Unknown authorities
/// are rejected rather than inheriting an unrelated focus source.
[[nodiscard]] constexpr std::wstring_view ResolveWidgetContentFocusId(
    const WidgetContentAuthority authority,
    const WidgetContentFocusSources sources) {
    switch (authority) {
    case WidgetContentAuthority::AdmittedSnapshot:
        return sources.current;
    case WidgetContentAuthority::InertRetainedSnapshot:
        return sources.refreshRetained;
    case WidgetContentAuthority::RetainedCommittedSnapshot:
        return sources.retainedCommitted;
    case WidgetContentAuthority::StableStartupStatus:
        return sources.current;
    case WidgetContentAuthority::Count:
        break;
    }
    throw std::invalid_argument{"unknown_widget_content_authority"};
}

/// The host-generated worker-start copy must not replace already-painted
/// widget content for a single frame. A healthy refreshing session keeps its
/// last-admitted checkpoint as the coherent committed render/input/UIA view;
/// a failed session keeps only inert pixels. The transition checkpoint is used
/// only when the destination has never admitted one; stable startup status is
/// reserved for the first widget open with no retained content.
[[nodiscard]] constexpr WidgetContentAuthority ResolveWidgetContentAuthority(
    const bool sessionSnapshotAvailable,
    const bool sessionSnapshotInteractive,
    const bool committedSnapshotAvailable) noexcept {
    if (sessionSnapshotAvailable) return sessionSnapshotInteractive
        ? WidgetContentAuthority::AdmittedSnapshot
        : WidgetContentAuthority::InertRetainedSnapshot;
    if (committedSnapshotAvailable)
        return WidgetContentAuthority::RetainedCommittedSnapshot;
    return WidgetContentAuthority::StableStartupStatus;
}

// Pure foreground-target state used by the HWND host and deterministic tests.
// Native window validity remains an OS concern supplied at each boundary.
class ForegroundTargetTracker final {
public:
    void SetOwnedWindows(std::uintptr_t overlay, std::uintptr_t backdrop) noexcept;

    // Returns true only when a valid external foreground target changed.
    bool Observe(std::uintptr_t candidate, bool candidateIsValid) noexcept;

    // Uses the remembered external target while it remains valid, otherwise
    // falls back without erasing history needed for a later focus restore.
    [[nodiscard]] std::uintptr_t Resolve(
        std::uintptr_t fallback,
        bool rememberedTargetIsValid) const noexcept;

    [[nodiscard]] std::uintptr_t remembered() const noexcept { return remembered_; }

private:
    std::uintptr_t overlay_{};
    std::uintptr_t backdrop_{};
    std::uintptr_t remembered_{};
};

// Prevents synchronous WM_DPICHANGED/WM_SIZE dispatch from recursively
// entering placement while SetWindowPos is still applying the first move.
// A single deferred refresh preserves the latest display state.
class PlacementRefreshGate final {
public:
    [[nodiscard]] bool TryEnter() noexcept;
    [[nodiscard]] bool Complete() noexcept;

private:
    bool active_{};
    bool pending_{};
};

} // namespace widgetrail
